using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace VideoMixApp
{
    public partial class MixEngine
    {
        private class Plan
        {
            public int Index;
            public List<PoolClip> Picked;
            public string IntroPath;
            public double IntroDuration;
            public double TotalDuration;
            public string Fingerprint;
        }

        // Where a normalization result goes. InPlace replaces the source in its folder (the normal
        // case since v2.3.0); Adopt does the same from an existing copy in the cache (clips unified by
        // v2.2 and earlier) with a stream copy instead of encoding; Cache keeps the result in
        // _cache_宽x高 (videos unified for another ratio, folders Axe cannot write to); Intro is the
        // 片头's copy in the cache.
        private enum JobKind { InPlace, Adopt, Cache, Intro }

        // One running ffmpeg and the progress it last reported. Activity is the engine clock time of the
        // last change (encoded time, frame count or bytes written); EndAt is when ffmpeg printed
        // progress=end, which it does after the output file is finalized. The watchdog uses both.
        private sealed class FfTask
        {
            public Process Proc;
            public long OutUs;
            public long Frames = -1;
            public long Bytes = -1;
            public long Activity;
            public long EndAt = -1;
            public volatile bool OutDone;
            public volatile bool ErrDone;
            public readonly Queue<string> ErrTail = new Queue<string>();
        }

        private enum FfState { Running, Exited, Stalled, ExitHung }

        // One source video (or the intro) waiting to be normalized.
        private sealed class Job
        {
            public string Tag;
            public string Name;
            public JobKind Kind;
            // What ffmpeg reads: the source, or for Adopt the cached copy.
            public string SrcPath;
            // The source video in its folder (the file an InPlace / Adopt result replaces).
            public string Origin;
            public long SrcBytes;
            public string Out;
            public string Part;
            public double SrcDuration;
            public bool DurationKnown;
            public bool HeaderDuration;
            public bool NoAudio;
            public double SrcFps;
            public long SrcPixels;
            public bool HwFailed;
            public bool UsingHw;
            // Compatibility mode for a file that failed or hung, see BuildTranscodeArgs.
            public bool Safe;
            public FfTask Task;
            public string Problem;
            // Adopt: the cached copy it was made from (removed once the result is in place).
            public string AdoptedNorm;
            // The result ended up in the cache (Cache jobs, or the source could not be replaced).
            public bool InCache;
            public bool Finished;
        }

        // One output video: joined by stream copy, or encoded once when 成片效果 are on (Fx) or when its
        // clips are below the bitrate floor (Recode: joined clips encoded, sound copied).
        private sealed class Concat
        {
            public Plan Plan;
            public string Name;
            public string Out;
            public string Part;
            public string ListFile;
            public FfTask Task;
            public bool Retried;
            public FxPick Fx;
            public bool Recode;
            // x264 average bitrate in kbps for an output that has to reach the floor; 0 = CRF.
            public int AbrKbps;
            // Video bitrate of the clips as they are (kbps, 0 = unknown) and how often the output was
            // made again for a too low bitrate.
            public double JoinKbps;
            public int RateTries;
            public bool UsingHw;
            public bool HwFailed;

            public bool Encodes
            {
                get { return Fx != null || Recode; }
            }
        }

        private sealed class SrcInfo
        {
            public double Duration;
            // The duration comes from the MP4/MOV header, not from ffprobe's estimate.
            public bool FromHeader;
            public bool Known;
            public bool HasVideo;
            public bool HasAudio;
            // Average frame rate and width x height from the header (0 = unknown).
            public double Fps;
            public long Pixels;
        }

        public string AppDir;
        public string FFmpeg;
        public string FFprobe;
        public string CacheDir;
        public Action<string> Log;
        public Action<int> Progress;
        public Action<string> Status;
        // 成片效果 for 快拼 (filter, sticker, speed, subtitles); null or all off = plain stream copy.
        public EffectOptions Effects;
        // Lowest video bitrate (kbps) the upload platforms accept, e.g. 1000; 0 = no check.
        public int MinKbps;

        // Watchdog limits in milliseconds (fields so the test harness can shorten them): a transcode
        // or concat whose progress has not moved for this long is treated as hung, and a process that
        // finalized its output but is still alive after ExitGraceMs is ended and its output kept.
        internal int StallMs = 90000;
        internal int CopyStallMs = 60000;
        internal int ExitGraceMs = 15000;

        private int _width = 720;
        private int _height = 1280;
        private int _fps = 30;
        private int _crf = 23;
        private int _gop = 60;
        private int _segmentSeconds = 15;
        private bool _useHw;
        private bool _hwPending;
        private HwKind _hw = HwKind.None;
        private volatile bool _cancel;
        private readonly List<Process> _live = new List<Process>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private const string AudioArgs = "-c:a aac -b:a 128k -ar 44100 -ac 2";
        private const string NoSoundProblem = "输出没有声音";
        private const string RunawayProblem = "时间戳异常，输出过长";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Regex NormRe = new Regex("^norm_([0-9A-F]{12})\\.mp4$");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        // Each ffmpeg needs memory (measured: about 450 MB while a 4K source is unified, 160-300 MB for
        // the other jobs). With many jobs on a PC that has little free memory, Windows would start
        // swapping, which makes everything slow and can stall jobs; so a job only starts when its share
        // is free. Jobs started in the last seconds are still growing and are counted as well. The
        // first job always starts.
        private sealed class MemoryGate
        {
            private const long ReserveMb = 700;
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            private readonly List<KeyValuePair<long, long>> _recent = new List<KeyValuePair<long, long>>();
            // Free memory when a job was first held back (-1 = never), for one line in the log.
            public long HeldAtMb = -1;
            public bool Reported;

            public bool MayStart(long needMb, int running)
            {
                if (running == 0) return true;
                long free = DeviceCheck.FreeMemoryMb();
                if (free < 0) return true;
                long now = _clock.ElapsedMilliseconds;
                _recent.RemoveAll(kv => now - kv.Key > 8000);
                long growing = 0;
                foreach (var kv in _recent) growing += kv.Value;
                if (free - growing >= ReserveMb + needMb) return true;
                if (HeldAtMb < 0) HeldAtMb = free;
                return false;
            }

            public void Started(long needMb)
            {
                _recent.Add(new KeyValuePair<long, long>(_clock.ElapsedMilliseconds, needMb));
            }
        }

        private void NoteMemory(MemoryGate gate, int running)
        {
            if (gate.Reported || gate.HeldAtMb < 0) return;
            gate.Reported = true;
            Log(string.Format("  可用内存不多（约 {0:N0} MB），暂时同时处理 {1} 个，内存够了自动多开，不影响结果", gate.HeldAtMb, running));
        }

        // The schedulers look at their jobs every 0.1-0.15 s. A much longer gap means the PC was asleep
        // (lid closed, sleep button): the "no progress" clocks of the running jobs would count that
        // time and end healthy jobs, so the caller starts them again from now (RestartClock).
        private bool Slept(ref long lastLook)
        {
            long now = _clock.ElapsedMilliseconds;
            long gap = lastLook >= 0 ? now - lastLook : 0;
            lastLook = now;
            if (gap <= 15000) return false;
            Log(string.Format("  电脑暂停了约 {0} 秒（睡眠或休眠），继续处理", gap / 1000));
            return true;
        }

        private void RestartClock(FfTask t)
        {
            Interlocked.Exchange(ref t.Activity, _clock.ElapsedMilliseconds);
        }

        // Moves a finished file into place. A virus scanner often still reads a file ffmpeg has just
        // closed, and the move fails for a moment then; that is retried instead of failing the job.
        private static void MoveIntoPlace(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(from, to);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 6 && File.Exists(from))
                {
                    Thread.Sleep(250 * attempt);
                }
            }
        }

        public bool SetHardwareAccel(bool want)
        {
            if (want)
            {
                _hw = HwEncoder.Detect(FFmpeg);
                _useHw = _hw != HwKind.None;
            }
            else
            {
                _hw = HwKind.None;
                _useHw = false;
            }
            _hwPending = false;
            return _useHw;
        }

        // Like SetHardwareAccel, but the GPU test encodes run only once something is actually
        // transcoded, so a 快拼 or 规格统一 with nothing new to convert starts at once.
        public void PreferHardwareAccel(bool want)
        {
            _hw = HwKind.None;
            _useHw = false;
            _hwPending = want;
        }

        private void EnsureHardwareChecked()
        {
            if (!_hwPending) return;
            _hwPending = false;
            _hw = HwEncoder.Detect(FFmpeg);
            _useHw = _hw != HwKind.None;
        }

        public HwKind HardwareKind
        {
            get { return _useHw ? _hw : HwKind.None; }
        }

        public void Configure(int width, int height, int fps, int crf, int segmentSeconds)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _crf = crf;
            _gop = fps * 2;
            _segmentSeconds = segmentSeconds;
        }

        public bool IsCancelled
        {
            get { return _cancel; }
        }

        // Stops the current task. Running ffmpeg processes are killed at once; the task then removes its
        // unfinished files (and, for 快拼, every video of the batch) before it returns.
        public void Cancel()
        {
            _cancel = true;
            lock (_live)
            {
                foreach (var p in _live)
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
            }
        }

        // The .mp4 files of the folder itself and of its direct subfolders (only the one named category
        // when a category is chosen). Windows' own folders in a drive root (System Volume Information,
        // $RECYCLE.BIN: hidden + system) are skipped; reading them would fail.
        public static List<FileInfo> ScanSources(string sourceRoot, string category)
        {
            var list = new List<FileInfo>();
            var root = new DirectoryInfo(sourceRoot);
            if (string.IsNullOrEmpty(category) || root.Name == category) AddVideos(root, list);
            foreach (var d in root.GetDirectories())
            {
                if (IsSystemFolder(d)) continue;
                if (string.IsNullOrEmpty(category) || d.Name == category) AddVideos(d, list);
            }
            return list;
        }

        private static void AddVideos(DirectoryInfo dir, List<FileInfo> list)
        {
            foreach (var f in dir.GetFiles("*.mp4"))
            {
                if (IsSourceVideo(f.Name)) list.Add(f);
            }
        }

        // GetFiles("*.mp4") also returns longer extensions such as ".mp4x"; macOS leaves "._name.mp4"
        // metadata files on USB drives. Neither is a video. The folder picker's badges use this too.
        internal static bool IsSourceVideo(string fileName)
        {
            return fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith("._", StringComparison.Ordinal);
        }

        internal static bool IsSystemFolder(DirectoryInfo d)
        {
            try
            {
                FileAttributes a = d.Attributes;
                return (a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0;
            }
            catch
            {
                return false;
            }
        }

        // Product folder name under the output root: the category, else the source folder's name. A
        // drive root (E:\) has no name of its own and becomes "E盘"; a share root uses the share name.
        internal static string ProductName(string sourceRoot, string category)
        {
            if (!string.IsNullOrEmpty(category)) return category;
            try
            {
                string full = Path.GetFullPath((sourceRoot ?? "").Trim());
                string trimmed = full.TrimEnd('\\', '/');
                string drive = (Path.GetPathRoot(full) ?? "").TrimEnd('\\', '/');
                if (!string.Equals(trimmed, drive, StringComparison.OrdinalIgnoreCase)) return new DirectoryInfo(trimmed).Name;
                if (drive.Length == 2 && drive[1] == ':') return char.ToUpperInvariant(drive[0]) + "盘";
                string share = Path.GetFileName(drive);
                if (!string.IsNullOrEmpty(share)) return share;
            }
            catch
            {
            }
            return "产品";
        }

        private string ComputeSignature(List<FileInfo> sources)
        {
            var sb = new StringBuilder();
            foreach (var f in sources)
            {
                sb.Append(f.FullName).Append('|').Append(f.Length).Append('|').Append(f.LastWriteTimeUtc.Ticks).Append(';');
            }
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
            }
        }

        // Cache tag = first 12 hex chars of SHA256(lower-case full path). Must stay identical to
        // v2.0.x so existing norm_<TAG>.mp4 files are recognised.
        internal static string MakeTag(string fullPath)
        {
            return BitConverter.ToString(Sha().ComputeHash(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()))).Replace("-", "").Substring(0, 12);
        }

        // Tags are computed for every video on each status check; one hasher per thread saves
        // creating (and finding through CryptoConfig) a new one each time.
        [ThreadStatic] private static SHA256 _sha;

        private static SHA256 Sha()
        {
            return _sha ?? (_sha = SHA256.Create());
        }

        private string NormPath(string tag)
        {
            return Path.Combine(CacheDir, "norm_" + tag + ".mp4");
        }

        // Fast path reads the MP4 header in-process; ffprobe is only used for files it cannot parse.
        public double ProbeDuration(string path)
        {
            var info = Mp4Probe.TryRead(path);
            if (info != null && info.Duration > 0) return info.Duration;
            return ProbeDurationFFprobe(path);
        }

        private double ProbeDurationFFprobe(string path)
        {
            string text = RunFFprobe(string.Format("-v error -show_entries format=duration -of csv=p=0 \"{0}\"", path));
            if (text == null) return 0.0;
            string s1 = text.Trim();
            int nl = s1.IndexOfAny(new[] { '\r', '\n' });
            if (nl >= 0) s1 = s1.Substring(0, nl);
            double d;
            return double.TryParse(s1, NumberStyles.Float, Inv, out d) ? Math.Round(d, 3) : 0.0;
        }

        // Duration and streams of a source. Files the in-process reader cannot fully describe
        // (fragmented MP4 from web downloads, other formats renamed to .mp4) are asked from ffprobe,
        // so a source without sound is always recognised and gets a silent track.
        private SrcInfo ProbeSource(string path)
        {
            var r = new SrcInfo();
            var info = Mp4Probe.TryRead(path);
            if (info != null)
            {
                r.Known = true;
                r.HasVideo = info.HasVideo;
                r.HasAudio = info.HasAudio;
                r.Duration = info.Duration;
                r.FromHeader = r.Duration > 0;
                r.Fps = info.VideoFps;
                r.Pixels = (long)info.VideoWidth * info.VideoHeight;
                if (r.Duration > 0) return r;
            }
            string text = RunFFprobe("-v error -show_entries format=duration:stream=codec_type -of default=noprint_wrappers=1 \"" + path + "\"");
            if (text == null) return r;
            bool video = false;
            bool audio = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line == "codec_type=video") video = true;
                else if (line == "codec_type=audio") audio = true;
                else if (line.StartsWith("duration=", StringComparison.Ordinal))
                {
                    double d;
                    if (double.TryParse(line.Substring(9), NumberStyles.Float, Inv, out d) && d > 0) r.Duration = Math.Round(d, 3);
                }
            }
            if (video || audio)
            {
                r.Known = true;
                r.HasVideo = video;
                r.HasAudio = audio;
            }
            return r;
        }

        // Runs ffprobe with a time limit (a damaged file must not be able to block the task).
        private string RunFFprobe(string args)
        {
            if (string.IsNullOrEmpty(FFprobe) || !File.Exists(FFprobe)) return null;
            try
            {
                var psi = new ProcessStartInfo(FFprobe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.CreateNoWindow = true;
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    var sb = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.Append(e.Data).Append('\n'); };
                    p.ErrorDataReceived += (s, e) => { };
                    p.Start();
                    ChildProcesses.Add(p);
                    // Listed with the encoders so 停止 also ends a probe that is still reading.
                    lock (_live) _live.Add(p);
                    try
                    {
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        if (!p.WaitForExit(20000))
                        {
                            try { p.Kill(); } catch { }
                            p.WaitForExit(5000);
                            return null;
                        }
                        p.WaitForExit();
                        lock (sb) return sb.ToString();
                    }
                    finally
                    {
                        Forget(p);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private bool EnsureFFmpeg()
        {
            string missing = null;
            if (string.IsNullOrEmpty(FFmpeg) || !File.Exists(FFmpeg)) missing = FFmpeg;
            else if (string.IsNullOrEmpty(FFprobe) || !File.Exists(FFprobe)) missing = FFprobe;
            if (missing != null || string.IsNullOrEmpty(FFmpeg))
            {
                Log("未找到 FFmpeg 组件，期望位置：" + (string.IsNullOrEmpty(missing) ? "(未设置)" : missing));
                Log("请使用完整解压后的软件文件夹（内含 ffmpeg 子目录），不要只复制 Axe.exe；也可重新下载压缩包后整体解压。");
                return false;
            }
            return true;
        }

        // workDir: 成片效果 name their LUT and subtitle files relative to the task folder, which keeps
        // Windows paths (drive colons, backslashes, quotes) out of the filter description.
        private Process StartFFmpeg(string argLine, FfTask task, string workDir = null)
        {
            var psi = new ProcessStartInfo(FFmpeg, argLine);
            if (workDir != null) psi.WorkingDirectory = workDir;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.CreateNoWindow = true;
            var p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            task.Proc = p;
            Interlocked.Exchange(ref task.Activity, _clock.ElapsedMilliseconds);
            p.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) OnProgressLine(task, e.Data);
                else task.OutDone = true;
            };
            p.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null)
                {
                    task.ErrDone = true;
                    return;
                }
                lock (task.ErrTail)
                {
                    task.ErrTail.Enqueue(e.Data);
                    while (task.ErrTail.Count > 12) task.ErrTail.Dequeue();
                }
            };
            p.Start();
            ChildProcesses.Add(p);
            // Encoding should not make the rest of the PC sluggish; it still uses all idle CPU.
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            lock (_live)
            {
                _live.Add(p);
                // A stop that arrived while this process was being started.
                if (_cancel)
                {
                    try { p.Kill(); } catch { }
                }
            }
            return p;
        }

        private void Forget(Process p)
        {
            lock (_live) _live.Remove(p);
        }

        private void OnProgressLine(FfTask t, string line)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) return;
            string key = line.Substring(0, eq);
            string val = line.Substring(eq + 1).Trim();
            long now = _clock.ElapsedMilliseconds;
            if (key == "progress")
            {
                if (val == "end") Interlocked.Exchange(ref t.EndAt, now);
                return;
            }
            long n;
            if (!long.TryParse(val, NumberStyles.Integer, Inv, out n)) return;
            bool moved = false;
            // ffmpeg -progress reports microseconds in out_time_us (older builds: out_time_ms).
            if (key == "out_time_us" || key == "out_time_ms") moved = Advance(ref t.OutUs, n);
            else if (key == "frame") moved = Advance(ref t.Frames, n);
            else if (key == "total_size") moved = Advance(ref t.Bytes, n);
            if (moved) Interlocked.Exchange(ref t.Activity, now);
        }

        private static bool Advance(ref long field, long value)
        {
            if (value <= Interlocked.Read(ref field)) return false;
            Interlocked.Exchange(ref field, value);
            return true;
        }

        private FfState CheckTask(FfTask t, int stallMs)
        {
            if (t.Proc.HasExited) return FfState.Exited;
            long now = _clock.ElapsedMilliseconds;
            long end = Interlocked.Read(ref t.EndAt);
            if (end >= 0) return now - end > ExitGraceMs ? FfState.ExitHung : FfState.Running;
            return now - Interlocked.Read(ref t.Activity) > stallMs ? FfState.Stalled : FfState.Running;
        }

        private long IdleMs(FfTask t)
        {
            return _clock.ElapsedMilliseconds - Interlocked.Read(ref t.Activity);
        }

        // Releases an exited ffmpeg (or kills a running one first); returns its exit code, -1 if killed.
        private int Finish(FfTask t, bool kill)
        {
            Process p = t.Proc;
            int code = -1;
            try
            {
                if (kill && !p.HasExited) p.Kill();
            }
            catch
            {
            }
            try
            {
                if (p.WaitForExit(15000))
                {
                    // Give the output readers a moment for the last lines, but never wait on them
                    // without a limit: a pipe kept open by another process would block the task.
                    long until = _clock.ElapsedMilliseconds + 3000;
                    while (!(t.OutDone && t.ErrDone) && _clock.ElapsedMilliseconds < until) Thread.Sleep(15);
                    if (!kill) code = p.ExitCode;
                }
            }
            catch
            {
            }
            Forget(p);
            try { p.Dispose(); } catch { }
            return code;
        }

        // Each running ffmpeg keeps two thread-pool threads waiting on its output; have them available
        // at once so progress lines are never held back while many jobs start together.
        private static void EnsureThreadPool(int parallel)
        {
            int worker;
            int io;
            ThreadPool.GetMinThreads(out worker, out io);
            int want = parallel * 2 + 8;
            if (worker < want) ThreadPool.SetMinThreads(want, io);
        }

        // A source with more frames per second than the output (phones often film at 50 or 60) drops
        // the extra frames before scaling instead of after: the same frames come out (fps picks them
        // by time, scale and pad do not touch time; checked frame by frame), with up to half less
        // scaling. Slower sources keep the order, so frames fps repeats are not scaled twice.
        private string BuildVideoFilter(double srcFps)
        {
            string fit = string.Format(Inv, "scale={0}:{1}:force_original_aspect_ratio=decrease,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2,setsar=1", _width, _height);
            string fps = "fps=" + _fps.ToString(Inv);
            return srcFps > _fps * 1.05 ? fps + "," + fit : fit + "," + fps;
        }

        // abrKbps > 0: the encoder aims at that average bitrate instead of a quality level (outputs that
        // would otherwise stay below the platforms' minimum, see Mix).
        private string BuildEncodeArgs(bool hw, int threads, int abrKbps = 0)
        {
            if (hw) return HwEncoder.VideoArgs(_hw, _crf, _gop, abrKbps) + " " + AudioArgs;
            string rate = abrKbps > 0 ? "-b:v " + abrKbps.ToString(Inv) + "k" : "-crf " + _crf.ToString(Inv);
            return string.Format(Inv, "-c:v libx264 -preset veryfast {0} -profile:v high -level 4.1 -pix_fmt yuv420p -g {1} -keyint_min {1} -sc_threshold 0 {2} -threads {3}", rate, _gop, AudioArgs, threads);
        }

        // Full ffmpeg command for normalizing one source. Compatibility mode (j.Safe) is the fallback
        // after a failure or hang: CPU only, single-threaded decoding, damaged packets skipped, audio
        // gaps filled with silence and the output capped at the source duration.
        private string BuildTranscodeArgs(Job j, bool hw, int threads)
        {
            var sb = new StringBuilder("-hide_banner -nostdin -y -loglevel error -nostats -progress pipe:1 ");
            if (j.Kind == JobKind.Adopt)
            {
                // The cached copy is already unified: copy its streams and add the mark.
                sb.Append("-i \"").Append(j.SrcPath).Append("\" -map 0:v:0 -map 0:a:0 -c copy ");
                sb.Append(MarkArgs()).Append('"').Append(j.Part).Append('"');
                return sb.ToString();
            }
            if (j.Safe) sb.Append("-threads 1 -fflags +genpts+discardcorrupt -err_detect ignore_err ");
            else if (!hw) sb.Append("-threads ").Append(threads).Append(' ');
            sb.Append("-i \"").Append(j.SrcPath).Append("\" ");
            if (j.NoAudio)
            {
                // Sources without sound get a silent stereo track, so every cached clip has the
                // same streams and concat copy never produces a silent or desynced output.
                sb.Append("-f lavfi ");
                if (j.DurationKnown) sb.Append("-t ").Append(j.SrcDuration.ToString("0.###", Inv)).Append(' ');
                sb.Append("-i anullsrc=channel_layout=stereo:sample_rate=44100 ");
            }
            sb.Append("-vf \"").Append(BuildVideoFilter(j.SrcFps)).Append(hw ? HwEncoder.FilterSuffix(_hw) : "").Append("\" ");
            if (j.NoAudio)
            {
                // The silent track is already cut to the source length; -shortest (and its extra
                // stream syncing at the end) is only needed when that length is unknown.
                sb.Append("-map 0:v:0 -map 1:a:0 ");
                if (!j.DurationKnown) sb.Append("-shortest ");
            }
            else
            {
                sb.Append("-map 0:v:0 -map 0:a:0? ");
                if (j.Safe) sb.Append("-af aresample=async=1:first_pts=0 ");
            }
            if (j.Safe && j.DurationKnown) sb.Append("-t ").Append((j.SrcDuration + 0.5).ToString("0.###", Inv)).Append(' ');
            sb.Append("-max_muxing_queue_size 4096 ");
            sb.Append(BuildEncodeArgs(hw, threads)).Append(' ');
            sb.Append(MarkArgs()).Append('"').Append(j.Part).Append('"');
            return sb.ToString();
        }

        private string UnifyMark
        {
            get { return Unified.Mark(_width, _height, _fps); }
        }

        // The mark that tells later runs (and other PCs) the file is unified; -f because the
        // unfinished file next to the source does not end in .mp4.
        private string MarkArgs()
        {
            return "-metadata \"comment=" + UnifyMark + "\" -f mp4 ";
        }

        private static JavaScriptSerializer NewSerializer()
        {
            // The default 2 MB limit breaks once a cache holds several thousand clips.
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js;
        }

        internal static Manifest ReadManifest(string cacheDir)
        {
            string p = Path.Combine(cacheDir, "manifest.json");
            // Only the new copy is left if saving was interrupted between removing the old file and
            // moving the new one in (see SaveManifest); a half-written one simply fails to parse.
            if (!File.Exists(p)) p += ".tmp";
            if (!File.Exists(p)) return null;
            try
            {
                string json;
                // Shared read: the folder status check must never block a 规格统一 that saves meanwhile.
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    json = sr.ReadToEnd();
                }
                return NewSerializer().Deserialize<Manifest>(json);
            }
            catch
            {
                return null;
            }
        }

        public Manifest LoadManifest()
        {
            return ReadManifest(CacheDir);
        }

        private void SaveManifest(Manifest m)
        {
            string json = NewSerializer().Serialize(m);
            string p = Path.Combine(CacheDir, "manifest.json");
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(true));
            // Replace atomically so a crash while saving never leaves a half-written manifest. A virus
            // scanner or a second Axe window can hold the file for a moment, so that is retried.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(p))
                    {
                        try
                        {
                            File.Replace(tmp, p, null);
                            return;
                        }
                        catch
                        {
                            // Some network drives do not support it: delete and move instead.
                        }
                        File.Delete(p);
                    }
                    File.Move(tmp, p);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 10)
                {
                    Thread.Sleep(200);
                }
            }
        }

        private bool EnsureDiskSpace(string targetPath, long needBytes, string label)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(targetPath));
                var di = new DriveInfo(root);
                long free = di.AvailableFreeSpace;
                long reserve = 200L * 1024 * 1024;
                double needMb = needBytes / 1048576.0;
                double freeMb = free / 1048576.0;
                if (free < needBytes + reserve)
                {
                    Log(string.Format("磁盘空间不足：{0} 所在盘 {1} 可用 {2:N1} MB，本次约需 {3:N1} MB。请清理磁盘后重试。", label, root, freeMb, needMb));
                    return false;
                }
                Log(string.Format("磁盘空间检查通过：{0} 所在盘 {1} 可用 {2:N1} MB（本次约需 {3:N1} MB）。", label, root, freeMb, needMb));
                return true;
            }
            catch (Exception ex)
            {
                Log("磁盘空间检查跳过（无法获取驱动器信息）：" + ex.Message);
                return true;
            }
        }

        internal static string ReuseKey(string fileName, long size, string lastWrite)
        {
            return (fileName ?? "").ToLowerInvariant() + "|" + size.ToString(Inv) + "|" + lastWrite;
        }

        // A copied or moved source keeps its name, size and modification time but gets a new path
        // (and therefore a new tag). Reuse its existing normalized file instead of re-encoding.
        private int ReuseCopiedCache(List<KeyValuePair<string, FileInfo>> todo, List<SourceEntry> oldSources, Dictionary<string, double> oldDur, HashSet<string> withSound)
        {
            var index = new Dictionary<string, List<SourceEntry>>(StringComparer.Ordinal);
            foreach (var se in oldSources)
            {
                if (se == null || string.IsNullOrEmpty(se.path) || string.IsNullOrEmpty(se.tag) || string.IsNullOrEmpty(se.lastWrite)) continue;
                string key = ReuseKey(Path.GetFileName(se.path), se.size, se.lastWrite);
                List<SourceEntry> list;
                if (!index.TryGetValue(key, out list)) index[key] = list = new List<SourceEntry>();
                list.Add(se);
            }
            int reused = 0;
            for (int i = todo.Count - 1; i >= 0; i--)
            {
                var kv = todo[i];
                List<SourceEntry> cands;
                if (!index.TryGetValue(ReuseKey(kv.Value.Name, kv.Value.Length, kv.Value.LastWriteTimeUtc.ToString("o")), out cands)) continue;
                // Prefer a copy whose original is gone (a move: rename its cache, leaving no orphan)
                // over one that still exists (a copy: share it with a hard link).
                var ordered = new List<SourceEntry>(cands);
                ordered.Sort((x, y) => File.Exists(x.path).CompareTo(File.Exists(y.path)));
                foreach (var c in ordered)
                {
                    if (string.Equals(c.tag, kv.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    string from = NormPath(c.tag);
                    if (!File.Exists(from) || new FileInfo(from).Length < 10240) continue;
                    string to = NormPath(kv.Key);
                    try
                    {
                        if (File.Exists(to)) File.Delete(to);
                        if (File.Exists(c.path))
                        {
                            // Original still in use: share the file (hard link costs no extra space).
                            if (!CreateHardLink(to, from, IntPtr.Zero)) File.Copy(from, to);
                        }
                        else
                        {
                            File.Move(from, to);
                        }
                    }
                    catch
                    {
                        continue;
                    }
                    double d;
                    if (oldDur.TryGetValue(c.tag, out d) && d > 0) oldDur[kv.Key] = d;
                    if (withSound.Contains(c.tag)) withSound.Add(kv.Key);
                    Log("  复用已统一素材（复制/移动过的同一文件，无需重新转码）：" + kv.Value.Name);
                    todo.RemoveAt(i);
                    reused++;
                    break;
                }
            }
            return reused;
        }

        private static SourceEntry NewEntry(string tag, FileInfo f)
        {
            return new SourceEntry { tag = tag, path = f.FullName, category = f.Directory.Name, size = f.Length, lastWrite = f.LastWriteTimeUtc.ToString("o") };
        }

        private static bool SameVersion(SourceEntry se, FileInfo f)
        {
            return se.size == f.Length && se.lastWrite == f.LastWriteTimeUtc.ToString("o");
        }

        // Writes manifest.json from the norm files on disk: one clip per file whose source is known.
        // Files without a record are left alone (a later run of their folder picks them up again).
        private Manifest SaveRecord(Dictionary<string, SourceEntry> srcMap, Dictionary<string, double> oldDur, HashSet<string> fresh,
            HashSet<string> withSound, List<SourceEntry> failed, string signature)
        {
            Log("正在汇总完整素材并校验时长...");
            var normFiles = new List<string>(Directory.GetFiles(CacheDir, "norm_*.mp4"));
            normFiles.Sort(StringComparer.Ordinal);
            var clips = new List<ClipEntry>();
            var srcList = new List<SourceEntry>();
            foreach (string file in normFiles)
            {
                Match m = NormRe.Match(Path.GetFileName(file));
                if (!m.Success) continue;
                string tag = m.Groups[1].Value;
                SourceEntry se;
                if (!srcMap.TryGetValue(tag, out se) || se == null || string.IsNullOrEmpty(se.path)) continue;
                double dur;
                if (fresh.Contains(tag) || !oldDur.TryGetValue(tag, out dur) || !(dur > 0.0)) dur = ProbeDuration(file);
                srcList.Add(se);
                clips.Add(new ClipEntry { file = Path.GetFileName(file), duration = dur, category = se.category, sourceIndex = srcList.Count, audio = withSound.Contains(tag) });
            }
            var manifest = new Manifest
            {
                builtAt = DateTime.Now.ToString("o"),
                spec = new SpecInfo { width = _width, height = _height, fps = _fps, gop = _gop, crf = _crf, segmentSeconds = _segmentSeconds },
                sourceSignature = signature,
                sources = srcList,
                clips = clips,
                failed = failed
            };
            SaveManifest(manifest);
            return manifest;
        }

        // Runs normalization jobs. Largest files start first so no long file is left running alone at
        // the end; CPU jobs get a thread count matched to how many run at once; GPU jobs (when enabled)
        // are capped at the driver's session limit. A job that fails or stops making progress is
        // ended and tried again (on the CPU, then in compatibility mode) while the others carry on;
        // only then is it reported as failed. Returns false when stopped.
        private bool RunTranscodeJobs(List<Job> jobs, int parallel, List<Job> failed, bool allowHw, string label)
        {
            EnsureThreadPool(parallel);
            if (Status != null) Status(label + " · 正在读取素材信息…");
            if (!ProbeJobs(jobs)) return false;
            jobs.Sort((a, b) => b.SrcBytes.CompareTo(a.SrcBytes));

            double known = 0;
            int knownCount = 0;
            foreach (var j in jobs) if (j.DurationKnown) { known += j.SrcDuration; knownCount++; }
            double avg = knownCount > 0 ? known / knownCount : 30.0;
            foreach (var j in jobs) if (!j.DurationKnown) j.SrcDuration = avg;
            double totalDur = 0;
            foreach (var j in jobs) totalDur += j.SrcDuration;

            // Moving a cached copy into place is a stream copy: no encoder, no GPU check for it.
            bool encodes = false;
            foreach (var j in jobs) if (j.Kind != JobKind.Adopt) encodes = true;
            if (allowHw && encodes) EnsureHardwareChecked();
            bool hwOn = allowHw && encodes && _useHw;
            int cores = Math.Max(1, Environment.ProcessorCount);
            int hwCap = hwOn ? Math.Min(parallel, HwEncoder.MaxSessions(_hw)) : 0;
            if (hwOn) Log(string.Format("编码方式：{0} 同时处理 {1} 个 + CPU 处理其余（CPU 共 {2} 线程）", HwEncoder.DisplayName(_hw), hwCap, cores));
            else if (allowHw && encodes) Log(string.Format("编码方式：CPU（libx264），并行 {0} 个，按任务数自动分配 {1} 个 CPU 线程", Math.Min(parallel, jobs.Count), cores));

            var pending = new LinkedList<Job>(jobs);
            var running = new List<Job>();
            var memory = new MemoryGate();
            long lastLook = -1;
            int done = 0;
            int hwDone = 0;
            int hwFails = 0;
            int total = jobs.Count;
            double doneDur = 0;
            var sw = Stopwatch.StartNew();
            long lastStatus = -10000;
            int lastPct = -1;
            try
            {
                while (pending.Count > 0 || running.Count > 0)
                {
                    if (_cancel) return false;
                    while (pending.Count > 0 && running.Count < parallel)
                    {
                        var j = pending.First.Value;
                        // A stream copy needs little; a 4K source the most (decoder frames).
                        long need = j.Kind == JobKind.Adopt ? 60 : (j.SrcPixels >= 2560L * 1440 ? 450 : 260);
                        if (!memory.MayStart(need, running.Count))
                        {
                            NoteMemory(memory, running.Count);
                            break;
                        }
                        pending.RemoveFirst();
                        int hwRunning = 0;
                        foreach (var r in running) if (r.UsingHw) hwRunning++;
                        bool useHw = hwOn && j.Kind != JobKind.Adopt && !j.HwFailed && !j.Safe && hwRunning < hwCap;
                        int concurrentCpu = Math.Max(1, Math.Min(parallel, running.Count + 1 + pending.Count) - (useHw ? hwRunning + 1 : hwRunning));
                        int threads = Math.Max(2, Math.Min(cores, (int)Math.Ceiling(cores / (double)concurrentCpu)));
                        TryDelete(j.Part);
                        // Still held by the ended attempt or a virus scanner: write the retry elsewhere.
                        if (File.Exists(j.Part)) j.Part = FreePath(j.Part);
                        j.UsingHw = useHw;
                        j.Task = new FfTask();
                        StartFFmpeg(BuildTranscodeArgs(j, useHw, threads), j.Task);
                        memory.Started(need);
                        running.Add(j);
                        Log(string.Format("  预处理调度 tag={0}: {1}{2}", j.Tag, j.Name, Describe(j, useHw)));
                    }

                    Thread.Sleep(150);
                    if (Slept(ref lastLook)) foreach (var r in running) RestartClock(r.Task);

                    foreach (var j in running.ToArray())
                    {
                        FfState st = CheckTask(j.Task, StallMs);
                        bool runaway = st == FfState.Running && IsRunaway(j);
                        if (st == FfState.Running && !runaway) continue;
                        running.Remove(j);
                        string problem = null;
                        if (runaway)
                        {
                            Log(string.Format("  {0} 输出已远超素材时长（{1:N0} 秒），时间戳异常，自动结束该进程", j.Name, j.SrcDuration));
                            Finish(j.Task, true);
                            problem = RunawayProblem;
                        }
                        else if (st == FfState.Stalled)
                        {
                            Log(string.Format("  {0} 已 {1} 秒没有进展，自动结束该进程", j.Name, StallMs / 1000));
                            Finish(j.Task, true);
                            problem = "处理卡住";
                        }
                        else if (st == FfState.ExitHung)
                        {
                            // The output is finalized; only the process failed to exit. Keep the file.
                            Log("  " + j.Name + " 已处理完但 ffmpeg 没有退出，自动结束该进程");
                            Finish(j.Task, true);
                        }
                        else
                        {
                            int code = Finish(j.Task, false);
                            if (code != 0) problem = "ffmpeg 出错（代码 " + code + "）";
                        }
                        if (_cancel)
                        {
                            TryDelete(j.Part);
                            return false;
                        }
                        double outDur = 0;
                        if (problem == null) problem = CheckOutput(j.Part, out outDur);
                        if (problem == null) problem = Commit(j, outDur);
                        if (problem != null)
                        {
                            TryDelete(j.Part);
                            if (Retry(j, problem, ref hwCap, ref hwFails, hwDone))
                            {
                                pending.AddFirst(j);
                                continue;
                            }
                            j.Problem = problem;
                            failed.Add(j);
                            lock (j.Task.ErrTail)
                            {
                                foreach (string e in j.Task.ErrTail) Log("    ffmpeg: " + e);
                            }
                        }
                        else
                        {
                            j.Finished = true;
                            if (j.UsingHw) hwDone++;
                        }
                        done++;
                        doneDur += j.SrcDuration;
                        if (problem != null) Log(string.Format("  第 {0}/{1} 个统一失败 (tag={2})：{3}（{4}）", done, total, j.Tag, j.Name, problem));
                        else Log(string.Format("  已完成 {0}/{1} (tag={2})", done, total, j.Tag));
                    }

                    double cur = doneDur;
                    foreach (var r in running) cur += Math.Min(Interlocked.Read(ref r.Task.OutUs) / 1000000.0, r.SrcDuration);
                    double frac = totalDur > 0 ? Math.Min(1.0, cur / totalDur) : done / (double)total;
                    int pct = (int)(frac * 100.0);
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        Progress(Math.Min(99, pct));
                    }
                    if (Status != null && sw.ElapsedMilliseconds - lastStatus >= 1000)
                    {
                        lastStatus = sw.ElapsedMilliseconds;
                        Status(StatusLine(label, Math.Min(99, pct), done, total, sw.Elapsed, frac, running, pending.Count));
                    }
                }
            }
            finally
            {
                foreach (var r in running)
                {
                    Finish(r.Task, true);
                    TryDelete(r.Part);
                }
            }
            sw.Stop();
            if (allowHw && encodes && sw.Elapsed.TotalSeconds > 0.5 && totalDur > 0)
            {
                Log(string.Format("本次转码 {0} 个素材（{1:N1} 分钟视频）用时 {2}，约 {3:N1} 倍实时速度{4}。",
                    total, totalDur / 60.0, FormatSpan(sw.Elapsed), totalDur / sw.Elapsed.TotalSeconds,
                    hwDone > 0 ? string.Format("，其中 {0} 个由{1}完成", hwDone, HwEncoder.DisplayName(_hw)) : ""));
            }
            return true;
        }

        // Puts a finished output where it belongs. An InPlace / Adopt result replaces the source in its
        // folder. When that cannot be done (the file is open in another program, no permission) or
        // the result is much shorter than the source, the source stays as it is and the result is
        // kept in the cache instead, where 快拼 uses it just the same; a later run moves it into place.
        private string Commit(Job j, double outDur)
        {
            if (j.Kind == JobKind.Cache || j.Kind == JobKind.Intro)
            {
                try
                {
                    MoveIntoPlace(j.Part, j.Out);
                    j.InCache = j.Kind == JobKind.Cache;
                    return null;
                }
                catch (Exception ex)
                {
                    return "无法保存：" + ex.Message;
                }
            }
            string why = null;
            if (j.Kind == JobKind.InPlace && j.DurationKnown && j.HeaderDuration && outDur > 0 && outDur < j.SrcDuration * 0.5 - 1)
                why = string.Format("统一后只有 {0:N1} 秒（原视频 {1:N1} 秒）", outDur, j.SrcDuration);
            if (why == null)
            {
                why = Unified.ReplaceInPlace(j.Part, j.Origin);
                if (why == null) return null;
                why = "原视频无法替换（" + why + "）";
            }
            string norm = NormPath(j.Tag);
            try
            {
                if (!string.Equals(norm, j.SrcPath, StringComparison.OrdinalIgnoreCase)) MoveIntoPlace(j.Part, norm);
            }
            catch (Exception ex)
            {
                return "无法保存：" + ex.Message;
            }
            TryDelete(j.Part);
            j.InCache = true;
            Log("  " + j.Name + "：" + why + "，原视频保留，统一结果先放在软件缓存里（快拼照常使用）");
            return null;
        }

        // How a failed or hung job is tried again: from the source when a cached copy could not be
        // moved into place, with a silent track when its output came out without sound, on the CPU
        // after a GPU failure, then in compatibility mode. false = give up.
        private bool Retry(Job j, string problem, ref int hwCap, ref int hwFails, int hwDone)
        {
            if (j.Kind == JobKind.Adopt)
            {
                j.Kind = JobKind.InPlace;
                j.SrcPath = j.Origin;
                j.AdoptedNorm = null;
                Log("  " + j.Name + " 缓存里的统一结果无法使用（" + problem + "），改为重新统一");
                return true;
            }
            if (problem == NoSoundProblem && !j.NoAudio)
            {
                j.NoAudio = true;
                Log("  " + j.Name + " 没有可用的声音，补静音轨重新处理");
                return true;
            }
            if (problem == RunawayProblem && !j.Safe)
            {
                // The encoder is fine, the timestamps are not: go straight to the capped mode.
                j.Safe = true;
                Log("  " + j.Name + " 改用兼容模式重新处理（按素材时长截止）");
                return true;
            }
            if (j.UsingHw && !j.HwFailed)
            {
                j.HwFailed = true;
                hwFails++;
                Log("  显卡编码失败，自动改用 CPU 重新处理：" + j.Name);
                if (hwCap > 0 && hwFails >= 2 && hwDone == 0)
                {
                    // The GPU path is clearly broken on this machine; stop feeding it jobs.
                    hwCap = 0;
                    Log("  显卡编码连续失败，本次剩余素材全部改用 CPU 处理。");
                }
                return true;
            }
            if (!j.Safe)
            {
                j.Safe = true;
                Log("  " + j.Name + " " + problem + "，改用兼容模式重新处理");
                return true;
            }
            return false;
        }

        // Durations and sound tracks of the sources. A file the in-process reader cannot fully
        // describe (fragmented MP4 from web downloads, other formats renamed to .mp4) costs an
        // ffprobe run of 0.1-0.5 s, so several files are read at once. false = stopped.
        private bool ProbeJobs(List<Job> jobs)
        {
            int workers = Math.Min(jobs.Count, Math.Max(2, Math.Min(8, Environment.ProcessorCount / 2)));
            if (workers <= 1)
            {
                foreach (var j in jobs)
                {
                    if (_cancel) return false;
                    ProbeJob(j);
                }
                return !_cancel;
            }
            // Every ffprobe keeps two pool threads reading its output.
            EnsureThreadPool(workers);
            int next = -1;
            var threads = new List<Thread>();
            for (int w = 0; w < workers; w++)
            {
                var t = new Thread(() =>
                {
                    int i;
                    while (!_cancel && (i = Interlocked.Increment(ref next)) < jobs.Count) ProbeJob(jobs[i]);
                });
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
            foreach (var t in threads) t.Join();
            return !_cancel;
        }

        private void ProbeJob(Job j)
        {
            try
            {
                SrcInfo si = ProbeSource(j.SrcPath);
                j.SrcDuration = si.Duration;
                j.DurationKnown = si.Duration > 0;
                j.HeaderDuration = si.FromHeader;
                j.NoAudio = si.Known && si.HasVideo && !si.HasAudio;
                j.SrcFps = si.Fps;
                j.SrcPixels = si.Pixels;
            }
            catch
            {
                // Unknown duration and streams: handled like a file ffprobe could not read.
            }
        }

        // Broken timestamps can make ffmpeg fill a gap with copies of one frame: progress keeps
        // moving, so the stall check never fires, and the job never ends. Output far longer than the
        // source means exactly that; compatibility mode then stops the output at the source length.
        // Only trusted for MP4/MOV header durations: ffprobe's estimate for other formats (e.g. TS
        // saved as .mp4 with timestamp resets) can be shorter than the real content.
        private bool IsRunaway(Job j)
        {
            if (!j.DurationKnown || !j.HeaderDuration || j.Safe) return false;
            double limit = j.SrcDuration * 2 + 120;
            return Interlocked.Read(ref j.Task.OutUs) > limit * 1000000.0 || Interlocked.Read(ref j.Task.Frames) > limit * _fps;
        }

        // part_X.mp4 -> part_X_2.mp4, a.mp4.axepart -> a.mp4_2.axepart (or the next free number): the
        // extension stays, so the clean-up still finds it and it is never read as a source.
        private static string FreePath(string part)
        {
            string dir = Path.GetDirectoryName(part);
            string stem = Path.GetFileNameWithoutExtension(part);
            string ext = Path.GetExtension(part);
            for (int k = 2; ; k++)
            {
                string p = Path.Combine(dir, stem + "_" + k + ext);
                if (!File.Exists(p)) return p;
            }
        }

        private string Describe(Job j, bool useHw)
        {
            var parts = new List<string>();
            if (j.Kind == JobKind.Adopt) parts.Add("从软件缓存移回原位置");
            else if (j.Kind == JobKind.Cache) parts.Add("结果放在软件缓存");
            if (useHw) parts.Add(HwEncoder.ShortName(_hw));
            if (j.NoAudio) parts.Add("无声，自动补静音轨");
            if (j.Safe) parts.Add("兼容模式");
            return parts.Count == 0 ? "" : "（" + string.Join("，", parts) + "）";
        }

        private string StatusLine(string label, int pct, int done, int total, TimeSpan elapsed, double frac, List<Job> running, int pendingCount)
        {
            string s = string.Format("{0} {1}%（{2}/{3}）· 已用 {4}", label, pct, done, total, FormatSpan(elapsed));
            // Name the file that holds things up (instead of an estimate that would not be true).
            Job slow = null;
            long idle = 0;
            foreach (var r in running)
            {
                long i = IdleMs(r.Task);
                if (i > idle)
                {
                    idle = i;
                    slow = r;
                }
            }
            if (slow != null && idle >= 20000) return s + string.Format(" · {0} 已 {1} 秒无进展", Short(slow.Name), idle / 1000);
            s += " · 预计剩余 " + (frac > 0.02 ? FormatSpan(TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - frac) / frac)) : "计算中");
            if (pendingCount == 0 && running.Count == 1) s += " · 正在处理 " + Short(running[0].Name);
            return s;
        }

        private static string Short(string name)
        {
            return name.Length > 16 ? name.Substring(0, 15) + "…" : name;
        }

        // A finished clip must be a readable MP4 with picture, sound and a real duration: concat copy
        // needs every clip to carry the same streams.
        private string CheckOutput(string path, out double duration)
        {
            duration = 0;
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < 10240) return "输出文件不完整";
                bool video;
                bool audio;
                var info = Mp4Probe.TryRead(path);
                if (info != null)
                {
                    video = info.HasVideo;
                    audio = info.HasAudio;
                    duration = info.Duration;
                }
                else
                {
                    SrcInfo si = ProbeSource(path);
                    if (!si.Known) return "输出文件无法读取";
                    video = si.HasVideo;
                    audio = si.HasAudio;
                    duration = si.Duration;
                }
                if (!video) return "输出没有画面";
                if (!audio) return NoSoundProblem;
                if (duration <= 0) duration = ProbeDurationFFprobe(path);
                return duration > 0.2 ? null : "输出时长为 0";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryMove(string from, string to)
        {
            try { File.Move(from, to); } catch { }
        }

        // A file that was just written by a killed process can stay locked for a moment.
        private static bool DeleteWithRetry(string path)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    if (!File.Exists(path)) return true;
                    File.Delete(path);
                    return true;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
            return !File.Exists(path);
        }

        private static void TryDeleteEmptyDir(string dir)
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            catch
            {
            }
        }

        internal static string FormatSpan(TimeSpan t)
        {
            if (t.TotalHours >= 1) return string.Format("{0}小时{1:00}分", (int)t.TotalHours, t.Minutes);
            if (t.TotalMinutes >= 1) return string.Format("{0}分{1:00}秒", (int)t.TotalMinutes, t.Seconds);
            return string.Format("{0}秒", Math.Max(0, (int)Math.Round(t.TotalSeconds)));
        }

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private List<PoolClip> BuildOneBody(List<PoolClip> pool, double bodyMin, double bodyMax, Random rng)
        {
            var order = new List<PoolClip>(pool);
            Shuffle(order, rng);
            var picked = new List<PoolClip>();
            var used = new HashSet<string>();
            picked.Add(order[0]);
            used.Add(order[0].Id);
            double total = order[0].Duration;
            int i = 1;
            while (total < bodyMin)
            {
                var fits = new List<int>();
                for (int j = i; j < order.Count; j++)
                {
                    if (!used.Contains(order[j].Id) && total + order[j].Duration <= bodyMax) fits.Add(j);
                }
                int pick;
                if (fits.Count > 0)
                {
                    pick = fits[rng.Next(fits.Count)];
                }
                else
                {
                    int best = -1;
                    for (int j = i; j < order.Count; j++)
                    {
                        if (used.Contains(order[j].Id)) continue;
                        if (best < 0 || order[j].Duration < order[best].Duration) best = j;
                    }
                    pick = best;
                }
                if (pick < 0)
                {
                    // Pool exhausted below the minimum: start a new shuffled round (repeats allowed).
                    order = new List<PoolClip>(pool);
                    Shuffle(order, rng);
                    used.Clear();
                    i = 0;
                    continue;
                }
                if (pick != i)
                {
                    var tmp = order[i];
                    order[i] = order[pick];
                    order[pick] = tmp;
                }
                picked.Add(order[i]);
                used.Add(order[i].Id);
                total += order[i].Duration;
                i++;
            }
            while (i < order.Count && total + order[i].Duration <= bodyMax)
            {
                if (used.Add(order[i].Id))
                {
                    picked.Add(order[i]);
                    total += order[i].Duration;
                }
                i++;
            }
            return picked;
        }

        private string BodyFingerprint(List<PoolClip> picked)
        {
            var sb = new StringBuilder();
            foreach (var c in picked) sb.Append(c.Id).Append('|');
            return BitConverter.ToString(Sha().ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        private static bool IsInside(string rootDir, string fullPath)
        {
            if (string.IsNullOrEmpty(rootDir) || string.IsNullOrEmpty(fullPath)) return false;
            string r = rootDir.TrimEnd('\\').ToLowerInvariant();
            string f = fullPath.ToLowerInvariant();
            return f == r || f.StartsWith(r + "\\", StringComparison.Ordinal);
        }

        private string HistoryDir
        {
            get { return Path.Combine(AppDir, "_history"); }
        }

        private string HistoryFile
        {
            get { return Path.Combine(HistoryDir, "mix_history.csv"); }
        }

        private string RatioTag
        {
            get { return _width + "x" + _height; }
        }

        private string HistoryKey(string bodyFp)
        {
            return RatioTag + "|" + bodyFp;
        }

        private HashSet<string> LoadHistoryKeys()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(HistoryFile)) return set;
                foreach (string line in File.ReadAllLines(HistoryFile, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split(',');
                    if (parts.Length < 6) continue;
                    // The fingerprint is always the last column; a comma inside the quoted
                    // source path would otherwise shift it.
                    set.Add(parts[1].Trim() + "|" + parts[parts.Length - 1].Trim());
                }
            }
            catch (Exception ex)
            {
                Log("历史记录读取失败（不影响本次出片）：" + ex.Message);
            }
            return set;
        }

        private void AppendHistory(IList<Plan> plans, string sourceRoot, double targetSec, double tolerance)
        {
            var sb = new StringBuilder();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (var p in plans)
            {
                if (string.IsNullOrEmpty(p.Fingerprint)) continue;
                sb.Append(now).Append(',').Append(RatioTag).Append(',').Append(Csv(sourceRoot)).Append(',')
                  .Append(targetSec.ToString("0")).Append(',').Append(tolerance.ToString("0")).Append(',')
                  .Append(p.Fingerprint).Append('\n');
            }
            // A combination missing here could be made again later, so a file held for a moment (a
            // second Axe window, a virus scanner, a cloud drive syncing it) is retried.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (!Directory.Exists(HistoryDir)) Directory.CreateDirectory(HistoryDir);
                    File.AppendAllText(HistoryFile, sb.ToString(), new UTF8Encoding(false));
                    return;
                }
                catch (IOException) when (attempt < 6)
                {
                    Thread.Sleep(250 * attempt);
                }
                catch (Exception ex)
                {
                    Log("历史记录写入失败：" + ex.Message);
                    return;
                }
            }
        }

        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // Report column: quoted only when needed, so plain names look as before.
        private static string CsvField(string s)
        {
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? Csv(s) : s;
        }

        // After a stop (or a batch that produced nothing) removes every video of the batch and the
        // folders this run created, so nothing half-done is left behind. History is not touched:
        // it is only written for a batch that completed.
        private void RemoveBatch(List<string> produced, string batchDir, string productDir, bool productDirExisted)
        {
            int removed = 0;
            var locked = new List<string>();
            foreach (string f in produced)
            {
                if (DeleteWithRetry(f)) removed++;
                else locked.Add(Path.GetFileName(f));
            }
            try
            {
                if (Directory.Exists(batchDir))
                {
                    foreach (string f in Directory.GetFiles(batchDir, "~*.part.mp4")) DeleteWithRetry(f);
                }
            }
            catch
            {
            }
            TryDeleteEmptyDir(batchDir);
            if (!productDirExisted) TryDeleteEmptyDir(productDir);
            if (_cancel) Log(string.Format("已停止快拼：本批次已生成的 {0} 条成片已删除，不计入历史记录。", removed));
            if (locked.Count > 0) Log("以下成片正被其他程序占用，未能删除，请手动删除：" + string.Join("、", locked.ToArray()));
        }
    }
}
