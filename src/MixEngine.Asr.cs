using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace VideoMixApp
{
    // 识别语音配字幕: speech in the clips is recognized by asr\AxeAsr.exe (sherpa-onnx with the
    // SenseVoice model) in its own process, so a problem there can never take Axe down. Results are
    // kept in _asr\ by content, so every clip is recognized once, wherever it is moved or copied.
    public partial class MixEngine
    {
        // The model file downloaded on first use (see ModelDownloader); tokens.txt and the voice
        // detector (silero_vad.onnx) ship with Axe.
        internal const string AsrModelName = "model.int8.onnx";
        internal const long AsrModelSize = 239233841;
        internal const string AsrModelSha256 = "c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51";

        internal static string AsrDir(string appDir)
        {
            return Path.Combine(appDir, "asr");
        }

        internal static string AsrModelDir(string appDir)
        {
            return Path.Combine(appDir, "asr", "model");
        }

        // Everything 识别语音配字幕 needs is in place.
        internal static bool SubtitleModelReady(string appDir)
        {
            try
            {
                string dir = AsrDir(appDir);
                string model = AsrModelDir(appDir);
                var m = new FileInfo(Path.Combine(model, AsrModelName));
                return File.Exists(Path.Combine(dir, "AxeAsr.exe")) && File.Exists(Path.Combine(dir, "sherpa-onnx-c-api.dll")) && File.Exists(Path.Combine(dir, "onnxruntime.dll"))
                    && m.Exists && m.Length == AsrModelSize && File.Exists(Path.Combine(model, "tokens.txt")) && File.Exists(Path.Combine(model, "silero_vad.onnx"));
            }
            catch
            {
                return false;
            }
        }

        // Size plus SHA-1 of the first and last 64 KB: a moved or copied clip keeps its transcript,
        // and two different clips practically never share one. null when the file cannot be read.
        internal static string ContentKey(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA1.Create())
                {
                    long len = fs.Length;
                    var buf = new byte[65536];
                    int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, len));
                    sha.TransformBlock(buf, 0, n, null, 0);
                    if (len > buf.Length)
                    {
                        fs.Position = Math.Max(buf.Length, len - buf.Length);
                        n = fs.Read(buf, 0, buf.Length);
                        sha.TransformBlock(buf, 0, n, null, 0);
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    return len.ToString() + "_" + BitConverter.ToString(sha.Hash, 0, 10).Replace("-", "");
                }
            }
            catch
            {
                return null;
            }
        }

        // Transcripts of the clips (missing ones are recognized now). A clip whose speech could not
        // be recognized is simply absent; subtitles then skip it.
        private Dictionary<string, Transcript> Transcribe(List<string> clips, string taskRoot)
        {
            var result = new Dictionary<string, Transcript>(StringComparer.OrdinalIgnoreCase);
            string store = Path.Combine(AppDir, "_asr");
            var todo = new List<KeyValuePair<string, string>>();
            foreach (string clip in clips)
            {
                if (result.ContainsKey(clip)) continue;
                string key = ContentKey(clip);
                if (key == null) continue;
                string json = Path.Combine(store, key + ".json");
                Transcript t = File.Exists(json) ? Transcript.Load(json) : null;
                if (t != null) result[clip] = t;
                else todo.Add(new KeyValuePair<string, string>(clip, json));
            }
            if (todo.Count == 0) return result;
            if (!SubtitleModelReady(AppDir))
            {
                Log("没有找到语音识别模型，本批成片不加字幕（勾选「识别语音配字幕」时可以下载模型）。");
                return result;
            }
            Directory.CreateDirectory(store);
            Log(string.Format("识别语音：{0} 个素材需要识别{1}…", todo.Count, result.Count > 0 ? string.Format("（{0} 个已识别过，直接沿用）", result.Count) : ""));
            var sw = Stopwatch.StartNew();
            int procs = todo.Count >= 12 ? 2 : 1;
            int threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount / (2 * procs)));
            var lists = new List<List<KeyValuePair<string, string>>>();
            for (int i = 0; i < procs; i++) lists.Add(new List<KeyValuePair<string, string>>());
            for (int i = 0; i < todo.Count; i++) lists[i % procs].Add(todo[i]);
            int done = 0;
            var workers = new List<Thread>();
            for (int i = 0; i < procs; i++)
            {
                var list = lists[i];
                string listFile = Path.Combine(taskRoot, "asr_" + i + ".tsv");
                var lines = new List<string>();
                foreach (var kv in list) lines.Add(kv.Key + "\t" + kv.Value);
                File.WriteAllLines(listFile, lines, new UTF8Encoding(false));
                var w = new Thread(() =>
                {
                    RunAsr(listFile, threads, line =>
                    {
                        if (line.StartsWith("OK ", StringComparison.Ordinal) || line.StartsWith("ERR ", StringComparison.Ordinal))
                        {
                            int d = Interlocked.Increment(ref done);
                            if (Status != null) Status(string.Format("识别语音 {0}/{1} · 已用 {2}", d, todo.Count, FormatSpan(sw.Elapsed)));
                            if (line.StartsWith("ERR ", StringComparison.Ordinal))
                            {
                                int sp = line.IndexOf(' ', 4);
                                int n;
                                if (sp > 0 && int.TryParse(line.Substring(4, sp - 4), out n) && n < list.Count)
                                    Log("  识别语音失败：" + Path.GetFileName(list[n].Key) + "（" + line.Substring(sp + 1) + "）");
                            }
                        }
                        else if (line.StartsWith("FATAL ", StringComparison.Ordinal))
                        {
                            Log("语音识别无法运行：" + line.Substring(6));
                        }
                    });
                });
                w.IsBackground = true;
                w.Start();
                workers.Add(w);
            }
            foreach (var w in workers) w.Join();
            int speech = 0;
            foreach (var kv in todo)
            {
                Transcript t = File.Exists(kv.Value) ? Transcript.Load(kv.Value) : null;
                if (t == null) continue;
                result[kv.Key] = t;
                if (t.Segments.Count > 0) speech++;
            }
            if (!_cancel) Log(string.Format("识别语音完成：{0} 个素材，其中 {1} 个有人声，用时 {2}", todo.Count, speech, FormatSpan(sw.Elapsed)));
            return result;
        }

        // One AxeAsr.exe run; every stdout line goes to onLine. Ended (and its result ignored) when it
        // prints nothing for 3 minutes or when 停止 is clicked.
        private void RunAsr(string listFile, int threads, Action<string> onLine)
        {
            string exe = Path.Combine(AsrDir(AppDir), "AxeAsr.exe");
            var psi = new ProcessStartInfo(exe, string.Format("--model-dir \"{0}\" --ffmpeg \"{1}\" --list \"{2}\" --threads {3}", AsrModelDir(AppDir), FFmpeg, listFile, threads));
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = AsrDir(AppDir);
            long last = _clock.ElapsedMilliseconds;
            try
            {
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null) return;
                        Interlocked.Exchange(ref last, _clock.ElapsedMilliseconds);
                        onLine(e.Data);
                    };
                    p.ErrorDataReceived += (s, e) => { };
                    p.Start();
                    ChildProcesses.Add(p);
                    ChildProcesses.Background(p);
                    lock (_live)
                    {
                        _live.Add(p);
                        if (_cancel)
                        {
                            try { p.Kill(); } catch { }
                        }
                    }
                    try
                    {
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        while (!p.WaitForExit(500))
                        {
                            if (_clock.ElapsedMilliseconds - Interlocked.Read(ref last) > 180000)
                            {
                                Log("  语音识别 3 分钟没有进展，已结束；没识别完的素材本次不加字幕");
                                try { p.Kill(); } catch { }
                                p.WaitForExit(5000);
                                break;
                            }
                        }
                        p.WaitForExit(3000);
                    }
                    finally
                    {
                        Forget(p);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("语音识别无法启动：" + ex.Message);
            }
        }
    }
}
