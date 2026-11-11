using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

[assembly: System.Reflection.AssemblyTitle("AxeAsr")]
[assembly: System.Reflection.AssemblyProduct("Axe 语音识别")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

// Speech recognition helper for Axe's 识别语音配字幕. Runs as its own process so a problem in the
// native recognizer can never take Axe down with it.
//
//   AxeAsr.exe --model-dir <dir> --ffmpeg <ffmpeg.exe> --list <list.tsv> [--threads N]
//
// list.tsv (UTF-8): one "<video>\t<result.json>" per line. For every line the sound is decoded by
// ffmpeg, cut into speech segments by the silero VAD and recognized by SenseVoice; the result is
// written as JSON (seconds): {"v":1,"dur":12.3,"segs":[{"s":0.8,"e":3.2,"t":"…","tok":[…],"ts":[…]}]}.
// Progress goes to stdout as "OK <n>" / "ERR <n> <message>" lines; "READY" once the models are loaded.
//
// Paths are handed to sherpa-onnx as UTF-8, which is what it expects on Windows, so folders with
// Chinese names work (its own command-line tools receive them in the ANSI code page and fail).
internal static class AxeAsr
{
    private const string Dll = "sherpa-onnx-c-api";
    private const int SampleRate = 16000;

    [StructLayout(LayoutKind.Sequential)] private struct FeatureConfig { public int SampleRate; public int FeatureDim; }
    [StructLayout(LayoutKind.Sequential)] private struct TransducerCfg { public IntPtr Encoder, Decoder, Joiner; }
    [StructLayout(LayoutKind.Sequential)] private struct OneModelCfg { public IntPtr Model; }
    [StructLayout(LayoutKind.Sequential)] private struct WhisperCfg { public IntPtr Encoder, Decoder, Language, Task; public int TailPaddings, EnableTokenTimestamps, EnableSegmentTimestamps; }
    [StructLayout(LayoutKind.Sequential)] private struct SenseVoiceCfg { public IntPtr Model, Language; public int UseItn; }
    [StructLayout(LayoutKind.Sequential)] private struct MoonshineCfg { public IntPtr Preprocessor, Encoder, UncachedDecoder, CachedDecoder, MergedDecoder; }
    [StructLayout(LayoutKind.Sequential)] private struct TwoModelCfg { public IntPtr Encoder, Decoder; }
    [StructLayout(LayoutKind.Sequential)] private struct CanaryCfg { public IntPtr Encoder, Decoder, SrcLang, TgtLang; public int UsePnc; }
    [StructLayout(LayoutKind.Sequential)] private struct FunAsrNanoCfg { public IntPtr EncoderAdaptor, Llm, Embedding, Tokenizer, SystemPrompt, UserPrompt; public int MaxNewTokens; public float Temperature, TopP; public int Seed; public IntPtr Language; public int Itn; public IntPtr Hotwords; }
    [StructLayout(LayoutKind.Sequential)] private struct Qwen3Cfg { public IntPtr ConvFrontend, Encoder, Decoder, Tokenizer; public int MaxTotalLen, MaxNewTokens; public float Temperature, TopP; public int Seed; public IntPtr Hotwords; }
    [StructLayout(LayoutKind.Sequential)] private struct CohereCfg { public IntPtr Encoder, Decoder, Language; public int UsePunct, UseItn; }

    // Mirrors SherpaOnnxOfflineModelConfig of sherpa-onnx v2.0.1 field by field (c-api.h).
    [StructLayout(LayoutKind.Sequential)]
    private struct OfflineModelConfig
    {
        public TransducerCfg Transducer;
        public OneModelCfg Paraformer;
        public OneModelCfg NemoCtc;
        public WhisperCfg Whisper;
        public OneModelCfg Tdnn;
        public IntPtr Tokens;
        public int NumThreads;
        public int Debug;
        public IntPtr Provider;
        public IntPtr ModelType;
        public IntPtr ModelingUnit;
        public IntPtr BpeVocab;
        public IntPtr TeleSpeechCtc;
        public SenseVoiceCfg SenseVoice;
        public MoonshineCfg Moonshine;
        public TwoModelCfg FireRedAsr;
        public OneModelCfg Dolphin;
        public OneModelCfg ZipformerCtc;
        public CanaryCfg Canary;
        public OneModelCfg WenetCtc;
        public OneModelCfg Omnilingual;
        public OneModelCfg MedAsr;
        public FunAsrNanoCfg FunAsrNano;
        public OneModelCfg FireRedAsrCtc;
        public Qwen3Cfg Qwen3Asr;
        public CohereCfg CohereTranscribe;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LmCfg { public IntPtr Model; public float Scale; }
    [StructLayout(LayoutKind.Sequential)] private struct HrCfg { public IntPtr DictDir, Lexicon, RuleFsts; }
    [StructLayout(LayoutKind.Sequential)] private struct CtcFstCfg { public IntPtr Graph; public int MaxActive; }

    [StructLayout(LayoutKind.Sequential)]
    private struct OfflineRecognizerConfig
    {
        public FeatureConfig Feat;
        public OfflineModelConfig Model;
        public LmCfg Lm;
        public IntPtr DecodingMethod;
        public int MaxActivePaths;
        public IntPtr HotwordsFile;
        public float HotwordsScore;
        public IntPtr RuleFsts;
        public IntPtr RuleFars;
        public float BlankPenalty;
        public HrCfg Hr;
        public CtcFstCfg CtcFst;
    }

    [StructLayout(LayoutKind.Sequential)] private struct SileroCfg { public IntPtr Model; public float Threshold, MinSilence, MinSpeech; public int WindowSize; public float MaxSpeech; }

    [StructLayout(LayoutKind.Sequential)]
    private struct VadConfig
    {
        public SileroCfg Silero;
        public int SampleRate;
        public int NumThreads;
        public IntPtr Provider;
        public int Debug;
        public SileroCfg TenVad;   // same layout as SherpaOnnxTenVadModelConfig; left empty
    }

    [StructLayout(LayoutKind.Sequential)] private struct SpeechSegmentNative { public long Start; public IntPtr Samples; public int N; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResultNative
    {
        public IntPtr Text;
        public IntPtr Timestamps;
        public int Count;
        public IntPtr Tokens;
        public IntPtr TokensArr;
        public IntPtr Json;
        public IntPtr Lang;
        public IntPtr Emotion;
        public IntPtr Event;
        public IntPtr Durations;
        public IntPtr YsLogProbs;
        public IntPtr SegmentTimestamps;
        public IntPtr SegmentDurations;
        public IntPtr SegmentTexts;
        public IntPtr SegmentTextsArr;
        public int SegmentCount;
    }

    [DllImport(Dll)] private static extern IntPtr SherpaOnnxCreateOfflineRecognizer(ref OfflineRecognizerConfig config);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOfflineRecognizer(IntPtr recognizer);
    [DllImport(Dll)] private static extern IntPtr SherpaOnnxCreateOfflineStream(IntPtr recognizer);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOfflineStream(IntPtr stream);
    [DllImport(Dll)] private static extern void SherpaOnnxAcceptWaveformOffline(IntPtr stream, int sampleRate, float[] samples, int n);
    [DllImport(Dll)] private static extern void SherpaOnnxDecodeMultipleOfflineStreams(IntPtr recognizer, IntPtr[] streams, int n);
    [DllImport(Dll)] private static extern IntPtr SherpaOnnxGetOfflineStreamResult(IntPtr stream);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyOfflineRecognizerResult(IntPtr result);
    [DllImport(Dll)] private static extern IntPtr SherpaOnnxCreateVoiceActivityDetector(ref VadConfig config, float bufferSizeInSeconds);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroyVoiceActivityDetector(IntPtr vad);
    [DllImport(Dll)] private static extern void SherpaOnnxVoiceActivityDetectorAcceptWaveform(IntPtr vad, float[] samples, int n);
    [DllImport(Dll)] private static extern int SherpaOnnxVoiceActivityDetectorEmpty(IntPtr vad);
    [DllImport(Dll)] private static extern void SherpaOnnxVoiceActivityDetectorPop(IntPtr vad);
    [DllImport(Dll)] private static extern IntPtr SherpaOnnxVoiceActivityDetectorFront(IntPtr vad);
    [DllImport(Dll)] private static extern void SherpaOnnxDestroySpeechSegment(IntPtr segment);
    [DllImport(Dll)] private static extern void SherpaOnnxVoiceActivityDetectorFlush(IntPtr vad);
    [DllImport(Dll)] private static extern void SherpaOnnxVoiceActivityDetectorReset(IntPtr vad);

    private sealed class Segment
    {
        public double Start;
        public float[] Samples;
        public string Text = "";
        public string[] Tokens = new string[0];
        public float[] Times;
    }

    private static readonly List<IntPtr> Strings = new List<IntPtr>();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i + 1 < args.Length; i += 2) opt[args[i].TrimStart('-')] = args[i + 1];
        string dir, ffmpeg, list;
        if (!opt.TryGetValue("model-dir", out dir) || !opt.TryGetValue("ffmpeg", out ffmpeg) || !opt.TryGetValue("list", out list))
        {
            Console.WriteLine("FATAL 用法：AxeAsr --model-dir <dir> --ffmpeg <ffmpeg.exe> --list <list.tsv> [--threads N]");
            return 2;
        }
        int threads = 4;
        string t;
        if (opt.TryGetValue("threads", out t)) int.TryParse(t, out threads);
        threads = Math.Max(1, Math.Min(16, threads));
        string lang = opt.ContainsKey("lang") ? opt["lang"] : "zh";

        IntPtr rec = IntPtr.Zero;
        IntPtr vad = IntPtr.Zero;
        try
        {
            string model = Path.Combine(dir, "model.int8.onnx");
            string tokens = Path.Combine(dir, "tokens.txt");
            string silero = Path.Combine(dir, "silero_vad.onnx");
            foreach (string f in new[] { model, tokens, silero })
            {
                if (!File.Exists(f))
                {
                    Console.WriteLine("FATAL 缺少模型文件：" + f);
                    return 2;
                }
            }
            var rc = new OfflineRecognizerConfig();
            rc.Feat.SampleRate = SampleRate;
            rc.Feat.FeatureDim = 80;
            rc.Model.SenseVoice.Model = Utf8(model);
            rc.Model.SenseVoice.Language = Utf8(lang);
            rc.Model.SenseVoice.UseItn = 1;
            rc.Model.Tokens = Utf8(tokens);
            rc.Model.NumThreads = threads;
            rc.Model.Provider = Utf8("cpu");
            rc.Model.ModelType = Utf8("");
            rc.Model.ModelingUnit = Utf8("cjkchar");
            rc.Model.BpeVocab = Utf8("");
            rc.Model.TeleSpeechCtc = Utf8("");
            rc.DecodingMethod = Utf8("greedy_search");
            rc.MaxActivePaths = 4;
            rc.HotwordsFile = Utf8("");
            rc.HotwordsScore = 1.5f;
            rc.RuleFsts = Utf8("");
            rc.RuleFars = Utf8("");
            rec = SherpaOnnxCreateOfflineRecognizer(ref rc);
            if (rec == IntPtr.Zero)
            {
                Console.WriteLine("FATAL 语音识别模型加载失败（文件可能不完整）");
                return 2;
            }
            var vc = new VadConfig();
            vc.Silero.Model = Utf8(silero);
            vc.Silero.Threshold = 0.5f;
            vc.Silero.MinSilence = 0.3f;
            vc.Silero.MinSpeech = 0.25f;
            vc.Silero.WindowSize = 512;
            vc.Silero.MaxSpeech = 12f;
            vc.SampleRate = SampleRate;
            vc.NumThreads = 1;
            vc.Provider = Utf8("cpu");
            vc.TenVad.Model = Utf8("");
            vad = SherpaOnnxCreateVoiceActivityDetector(ref vc, 60f);
            if (vad == IntPtr.Zero)
            {
                Console.WriteLine("FATAL 语音检测模型加载失败");
                return 2;
            }
            Console.WriteLine("READY");
            Console.Out.Flush();

            string[] lines = File.ReadAllLines(list, Encoding.UTF8);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                string input = line.Substring(0, tab);
                string output = line.Substring(tab + 1);
                try
                {
                    float[] audio = Decode(ffmpeg, input);
                    List<Segment> segs = Detect(vad, audio);
                    Recognize(rec, segs);
                    WriteResult(output, audio.Length / (double)SampleRate, segs);
                    Console.WriteLine("OK " + n);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERR " + n + " " + ex.Message.Replace('\r', ' ').Replace('\n', ' '));
                }
                Console.Out.Flush();
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FATAL " + ex.Message.Replace('\r', ' ').Replace('\n', ' '));
            return 3;
        }
        finally
        {
            if (vad != IntPtr.Zero) SherpaOnnxDestroyVoiceActivityDetector(vad);
            if (rec != IntPtr.Zero) SherpaOnnxDestroyOfflineRecognizer(rec);
            foreach (IntPtr p in Strings) Marshal.FreeHGlobal(p);
        }
    }

    // Null-terminated UTF-8 copy that stays valid until the process ends.
    private static IntPtr Utf8(string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s ?? "");
        IntPtr p = Marshal.AllocHGlobal(b.Length + 1);
        Marshal.Copy(b, 0, p, b.Length);
        Marshal.WriteByte(p, b.Length, 0);
        Strings.Add(p);
        return p;
    }

    private static string FromUtf8(IntPtr p)
    {
        if (p == IntPtr.Zero) return "";
        int len = 0;
        while (Marshal.ReadByte(p, len) != 0) len++;
        var b = new byte[len];
        Marshal.Copy(p, b, 0, len);
        return Encoding.UTF8.GetString(b);
    }

    // 16 kHz mono float samples of the first sound track; empty for a video without sound.
    private static float[] Decode(string ffmpeg, string input)
    {
        var psi = new ProcessStartInfo(ffmpeg, "-nostdin -hide_banner -loglevel error -i \"" + input + "\" -map 0:a:0? -vn -ac 1 -ar 16000 -f f32le pipe:1");
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        using (var p = Process.Start(psi))
        {
            var err = new StringBuilder();
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) if (err.Length < 2000) err.Append(e.Data).Append(' '); };
            p.BeginErrorReadLine();
            var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            if (!p.WaitForExit(60000))
            {
                try { p.Kill(); } catch { }
                throw new IOException("读取声音超时");
            }
            if (p.ExitCode != 0 && ms.Length == 0) throw new IOException("读取声音失败：" + err.ToString().Trim());
            byte[] raw = ms.ToArray();
            var f = new float[raw.Length / 4];
            Buffer.BlockCopy(raw, 0, f, 0, f.Length * 4);
            return f;
        }
    }

    private static List<Segment> Detect(IntPtr vad, float[] audio)
    {
        var segs = new List<Segment>();
        SherpaOnnxVoiceActivityDetectorReset(vad);
        const int window = 512;
        var chunk = new float[window];
        for (int i = 0; i + window <= audio.Length; i += window)
        {
            Array.Copy(audio, i, chunk, 0, window);
            SherpaOnnxVoiceActivityDetectorAcceptWaveform(vad, chunk, window);
            Drain(vad, segs);
        }
        SherpaOnnxVoiceActivityDetectorFlush(vad);
        Drain(vad, segs);
        return segs;
    }

    private static void Drain(IntPtr vad, List<Segment> segs)
    {
        while (SherpaOnnxVoiceActivityDetectorEmpty(vad) == 0)
        {
            IntPtr p = SherpaOnnxVoiceActivityDetectorFront(vad);
            try
            {
                var s = (SpeechSegmentNative)Marshal.PtrToStructure(p, typeof(SpeechSegmentNative));
                if (s.Start >= 0 && s.N > 0)
                {
                    var samples = new float[s.N];
                    Marshal.Copy(s.Samples, samples, 0, s.N);
                    segs.Add(new Segment { Start = s.Start / (double)SampleRate, Samples = samples });
                }
            }
            finally
            {
                SherpaOnnxDestroySpeechSegment(p);
            }
            SherpaOnnxVoiceActivityDetectorPop(vad);
        }
    }

    // Decodes the segments a few at a time (one batch shares the model run).
    private static void Recognize(IntPtr rec, List<Segment> segs)
    {
        const int batch = 8;
        for (int i = 0; i < segs.Count; i += batch)
        {
            int n = Math.Min(batch, segs.Count - i);
            var streams = new IntPtr[n];
            try
            {
                for (int k = 0; k < n; k++)
                {
                    streams[k] = SherpaOnnxCreateOfflineStream(rec);
                    float[] s = segs[i + k].Samples;
                    SherpaOnnxAcceptWaveformOffline(streams[k], SampleRate, s, s.Length);
                }
                SherpaOnnxDecodeMultipleOfflineStreams(rec, streams, n);
                for (int k = 0; k < n; k++)
                {
                    IntPtr r = SherpaOnnxGetOfflineStreamResult(streams[k]);
                    if (r == IntPtr.Zero) continue;
                    try
                    {
                        var res = (ResultNative)Marshal.PtrToStructure(r, typeof(ResultNative));
                        Segment seg = segs[i + k];
                        seg.Text = FromUtf8(res.Text).Trim();
                        if (res.Count > 0 && res.TokensArr != IntPtr.Zero)
                        {
                            seg.Tokens = new string[res.Count];
                            for (int j = 0; j < res.Count; j++) seg.Tokens[j] = FromUtf8(Marshal.ReadIntPtr(res.TokensArr, j * IntPtr.Size));
                            if (res.Timestamps != IntPtr.Zero)
                            {
                                seg.Times = new float[res.Count];
                                Marshal.Copy(res.Timestamps, seg.Times, 0, res.Count);
                            }
                        }
                    }
                    finally
                    {
                        SherpaOnnxDestroyOfflineRecognizerResult(r);
                    }
                }
            }
            finally
            {
                foreach (IntPtr s in streams) if (s != IntPtr.Zero) SherpaOnnxDestroyOfflineStream(s);
            }
        }
    }

    private static void WriteResult(string output, double duration, List<Segment> segs)
    {
        var sb = new StringBuilder();
        sb.Append("{\"v\":1,\"dur\":").Append(Num(duration)).Append(",\"segs\":[");
        bool first = true;
        foreach (Segment s in segs)
        {
            if (s.Text.Length == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"s\":").Append(Num(s.Start)).Append(",\"e\":").Append(Num(s.Start + s.Samples.Length / (double)SampleRate));
            sb.Append(",\"t\":").Append(Json(s.Text)).Append(",\"tok\":[");
            for (int i = 0; i < s.Tokens.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Json(s.Tokens[i]));
            }
            sb.Append("],\"ts\":[");
            if (s.Times != null)
            {
                for (int i = 0; i < s.Times.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Num(s.Times[i]));
                }
            }
            sb.Append("]}");
        }
        sb.Append("]}");
        string tmp = output + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        if (File.Exists(output)) File.Delete(output);
        File.Move(tmp, output);
    }

    private static string Num(double d)
    {
        return Math.Round(d, 3).ToString("0.###", Inv);
    }

    private static string Json(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
