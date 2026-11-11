using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace VideoMixApp
{
    // 成片效果 of a 快拼 run, as set in the main window. Positions are relative to the frame (0..1,
    // the centre of the element), so they fit every 成片比例.
    public sealed class EffectOptions
    {
        public const string Random = "随机";
        public static readonly double[] Speeds = { 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

        // "" = none, "随机" = a different one for every video, else a name from effects\滤镜 / 贴纸.
        public string Filter = "";
        // 滤镜强度 0.1..1 (1 = the filter as it is); the same for every filter, also with 随机.
        public double FilterStrength = 1.0;
        public string Sticker = "";
        public double Speed = 1.0;
        public bool Subtitles;
        public double SubX = 0.5;
        public double SubY = 0.80;
        // Font height relative to the frame's shorter side.
        public double SubSize = 0.065;
        public double StickerX = 0.80;
        public double StickerY = 0.16;
        // Sticker width relative to the frame's shorter side.
        public double StickerSize = 0.30;

        public bool Any
        {
            get { return !string.IsNullOrEmpty(Filter) || !string.IsNullOrEmpty(Sticker) || Math.Abs(Speed - 1.0) > 0.001 || Subtitles; }
        }

        public static string SpeedText(double s)
        {
            return s.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        }

        // One of the offered speeds (a hand-edited settings file may hold anything).
        public static double ValidSpeed(double s)
        {
            foreach (double v in Speeds) if (Math.Abs(v - s) < 0.001) return v;
            return 1.0;
        }

        // 10 %..100 % in whole per cent; anything else (0 from older settings) is 100 %.
        public static double ValidStrength(double s)
        {
            if (double.IsNaN(s) || s <= 0 || s > 1) return 1.0;
            return Math.Max(0.1, Math.Round(s * 100) / 100);
        }

        public static string PercentText(double s)
        {
            return ((int)Math.Round(ValidStrength(s) * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }
    }

    // The effects one output video gets (random choices are made per video).
    internal sealed class FxPick
    {
        public string FilterName;
        public double Strength = 1.0;
        public string LutFile;      // relative to the task folder
        public string StickerName;
        public string StickerPath;
        public double Speed = 1.0;
        public string AssFile;      // relative to the task folder; null = no subtitles
        public int Cues;
    }

    internal sealed class EffectAsset
    {
        public string Name;
        public string Path;
        // Size and modification time when listed: a file replaced under the same name is noticed.
        public long Stamp;
    }

    // Filters are .cube 3D LUTs in effects\滤镜, stickers are images in effects\贴纸 (animated WebP,
    // GIF or APNG, or still PNG / WebP). The file name is the name shown in the list; a leading
    // "01_" only sets the order. Files added to these folders appear in the lists by themselves.
    internal static class EffectAssets
    {
        private static readonly string[] StickerExts = { ".webp", ".gif", ".png", ".apng" };

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        public static string FilterDir(string appDir)
        {
            return Path.Combine(appDir, "effects", "滤镜");
        }

        public static string StickerDir(string appDir)
        {
            return Path.Combine(appDir, "effects", "贴纸");
        }

        public static List<EffectAsset> Filters(string appDir)
        {
            return List(FilterDir(appDir), new[] { ".cube" });
        }

        public static List<EffectAsset> Stickers(string appDir)
        {
            return List(StickerDir(appDir), StickerExts);
        }

        private static List<EffectAsset> List(string dir, string[] exts)
        {
            var list = new List<EffectAsset>();
            try
            {
                if (!Directory.Exists(dir)) return list;
                var files = new List<string>(Directory.GetFiles(dir));
                files.Sort((a, b) => StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b)));
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    string ext = Path.GetExtension(f);
                    if (Array.IndexOf(exts, ext.ToLowerInvariant()) < 0) continue;
                    var fi = new FileInfo(f);
                    if ((fi.Attributes & FileAttributes.Hidden) != 0) continue;
                    string name = Display(f);
                    if (seen.Add(name)) list.Add(new EffectAsset { Name = name, Path = f, Stamp = fi.LastWriteTimeUtc.Ticks ^ (fi.Length << 20) });
                }
            }
            catch
            {
            }
            return list;
        }

        private static string Display(string file)
        {
            string s = Path.GetFileNameWithoutExtension(file);
            int us = s.IndexOf('_');
            if (us > 0 && us <= 3)
            {
                bool digits = true;
                for (int i = 0; i < us; i++) if (!char.IsDigit(s[i])) digits = false;
                if (digits && us + 1 < s.Length) s = s.Substring(us + 1);
            }
            return s;
        }

        public static EffectAsset Find(List<EffectAsset> list, string name)
        {
            foreach (var a in list) if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        // ffmpeg input options so the sticker keeps moving (or standing) for the whole video.
        public static string InputOptions(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".gif" || ext == ".apng" || (ext == ".png" && IsApng(path)) || (ext == ".webp" && IsAnimatedWebp(path))) return "-ignore_loop 0 ";
            return "-loop 1 ";
        }

        private static byte[] Head(string path, int n)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var b = new byte[n];
                    int got = fs.Read(b, 0, n);
                    if (got < n) Array.Resize(ref b, got);
                    return b;
                }
            }
            catch
            {
                return new byte[0];
            }
        }

        // RIFF....WEBPVP8X with the animation flag.
        private static bool IsAnimatedWebp(string path)
        {
            byte[] b = Head(path, 21);
            return b.Length >= 21 && Encoding.ASCII.GetString(b, 8, 8) == "WEBPVP8X" && (b[20] & 0x02) != 0;
        }

        // An animated PNG has an acTL chunk before the first image data.
        private static bool IsApng(string path)
        {
            byte[] b = Head(path, 4096);
            string s = Encoding.ASCII.GetString(b);
            int ac = s.IndexOf("acTL", StringComparison.Ordinal);
            int idat = s.IndexOf("IDAT", StringComparison.Ordinal);
            return ac > 0 && (idat < 0 || ac < idat);
        }
    }

    // Speech recognized in one clip (written by AxeAsr.exe): segments with the text and, per
    // character, its start time relative to the segment.
    internal sealed class Transcript
    {
        public sealed class Segment
        {
            public double Start;
            public double End;
            public string Text;
            public List<string> Tokens = new List<string>();
            public List<double> Times = new List<double>();
        }

        public double Duration;
        public readonly List<Segment> Segments = new List<Segment>();

        public static Transcript Load(string path)
        {
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                var t = new Transcript { Duration = Num(root, "dur") };
                object segs;
                if (root.TryGetValue("segs", out segs) && segs is System.Collections.ArrayList)
                {
                    foreach (object o in (System.Collections.ArrayList)segs)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        var s = new Segment { Start = Num(d, "s"), End = Num(d, "e"), Text = d.ContainsKey("t") ? Convert.ToString(d["t"], CultureInfo.InvariantCulture) : "" };
                        object tok;
                        object ts;
                        if (d.TryGetValue("tok", out tok) && tok is System.Collections.ArrayList && d.TryGetValue("ts", out ts) && ts is System.Collections.ArrayList)
                        {
                            var a = (System.Collections.ArrayList)tok;
                            var b = (System.Collections.ArrayList)ts;
                            if (a.Count == b.Count)
                            {
                                for (int i = 0; i < a.Count; i++)
                                {
                                    s.Tokens.Add(Convert.ToString(a[i], CultureInfo.InvariantCulture));
                                    s.Times.Add(Convert.ToDouble(b[i], CultureInfo.InvariantCulture));
                                }
                            }
                        }
                        if (s.End > s.Start && !string.IsNullOrEmpty(s.Text)) t.Segments.Add(s);
                    }
                }
                return t;
            }
            catch
            {
                return null;
            }
        }

        private static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }

    // One subtitle line on the output's timeline.
    internal sealed class Cue
    {
        public double Start;
        public double End;
        public string Text;
    }

    // Turns recognized speech into subtitle lines and an ASS file for ffmpeg's subtitles filter
    // (rendered with the system's 微软雅黑 through libass).
    internal static class SubtitleWriter
    {
        private const string Breaks = "，。！？；：、,!?;:…";
        private const string Drop = "，。！？；：、,!?;:…“”\"《》";

        // Lines of one clip, times relative to the clip start. A line ends at punctuation or when it
        // would get longer than maxChars; its time comes from its first character.
        public static List<Cue> Lines(Transcript t, int maxChars)
        {
            var cues = new List<Cue>();
            if (t == null) return cues;
            foreach (var s in t.Segments)
            {
                if (s.Tokens.Count == 0)
                {
                    SplitEven(s, maxChars, cues);
                    continue;
                }
                int first = -1;
                var text = new StringBuilder();
                var lines = new List<KeyValuePair<int, string>>();
                for (int i = 0; i < s.Tokens.Count; i++)
                {
                    string tok = s.Tokens[i];
                    bool punct = tok.Length == 1 && Breaks.IndexOf(tok[0]) >= 0 && !DecimalPoint(s.Tokens, i);
                    if (punct)
                    {
                        if (text.Length > 0) lines.Add(new KeyValuePair<int, string>(first, text.ToString()));
                        text.Clear();
                        first = -1;
                        continue;
                    }
                    if (text.Length > 0 && text.Length + tok.Length > maxChars)
                    {
                        lines.Add(new KeyValuePair<int, string>(first, text.ToString()));
                        text.Clear();
                        first = -1;
                    }
                    if (first < 0) first = i;
                    text.Append(tok);
                }
                if (text.Length > 0) lines.Add(new KeyValuePair<int, string>(first, text.ToString()));
                for (int k = 0; k < lines.Count; k++)
                {
                    double start = s.Start + s.Times[lines[k].Key];
                    double end = k + 1 < lines.Count ? s.Start + s.Times[lines[k + 1].Key] : s.End;
                    string shown = Clean(lines[k].Value);
                    if (shown.Length == 0) continue;
                    cues.Add(new Cue { Start = start, End = Math.Max(end, start + 0.4), Text = shown });
                }
            }
            return cues;
        }

        // "9.9块": a full stop between digits is a decimal point, not the end of a phrase.
        private static bool DecimalPoint(List<string> tokens, int i)
        {
            if (tokens[i] != "." || i == 0 || i + 1 >= tokens.Count) return false;
            string a = tokens[i - 1];
            string b = tokens[i + 1];
            return a.Length > 0 && b.Length > 0 && char.IsDigit(a[a.Length - 1]) && char.IsDigit(b[0]);
        }

        // Segments without character times: equal parts by length.
        private static void SplitEven(Transcript.Segment s, int maxChars, List<Cue> cues)
        {
            string text = Clean(s.Text);
            if (text.Length == 0) return;
            int parts = (text.Length + maxChars - 1) / maxChars;
            int per = (text.Length + parts - 1) / parts;
            double len = s.End - s.Start;
            for (int k = 0; k * per < text.Length; k++)
            {
                int a = k * per;
                int n = Math.Min(per, text.Length - a);
                cues.Add(new Cue { Start = s.Start + len * a / text.Length, End = s.Start + len * (a + n) / text.Length, Text = text.Substring(a, n) });
            }
        }

        // Short-video style: no punctuation on screen; braces and backslashes would be ASS codes.
        private static string Clean(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '.' && i > 0 && i + 1 < s.Length && char.IsDigit(s[i - 1]) && char.IsDigit(s[i + 1]))
                {
                    sb.Append(c);
                    continue;
                }
                if (Drop.IndexOf(c) >= 0)
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                    continue;
                }
                if (c == '{') c = '（';
                else if (c == '}') c = '）';
                else if (c == '\\') c = '＼';
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public static int FontPixels(int width, int height, EffectOptions fx)
        {
            return Math.Max(16, (int)Math.Round(Math.Min(width, height) * Clamp(fx.SubSize, 0.03, 0.12)));
        }

        public static int MaxChars(int width, int height, EffectOptions fx)
        {
            return Math.Max(6, (int)(width * 0.88 / FontPixels(width, height, fx)));
        }

        public static string Ass(int width, int height, EffectOptions fx, List<Cue> cues)
        {
            int font = FontPixels(width, height, fx);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("[Script Info]\r\nScriptType: v4.00+\r\nWrapStyle: 0\r\nScaledBorderAndShadow: yes\r\n");
            sb.Append("PlayResX: ").Append(width).Append("\r\nPlayResY: ").Append(height).Append("\r\n\r\n");
            sb.Append("[V4+ Styles]\r\n");
            sb.Append("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n");
            sb.AppendFormat(inv, "Style: Default,Microsoft YaHei,{0},&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,{1:0.#},{2:0.#},5,{3},{3},0,1\r\n\r\n",
                font, Math.Max(1.5, font * 0.07), Math.Max(0.5, font * 0.03), (int)(width * 0.04));
            sb.Append("[Events]\r\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\n");
            double margin = Math.Min(width, height) * EdgeMargin;
            int y = (int)Math.Round(Inside(height * Clamp(fx.SubY, 0, 1), font * 0.57, height, margin));
            foreach (var c in cues)
            {
                // Each line on its own: a long one near the edge moves in instead of being cut off.
                int x = (int)Math.Round(Inside(width * Clamp(fx.SubX, 0, 1), LineWidth(c.Text, font) / 2, width, margin));
                sb.AppendFormat(inv, "Dialogue: 0,{0},{1},Default,,0,0,0,,{{\\an5\\pos({2},{3})}}{4}\r\n", Time(c.Start), Time(c.End), x, y, c.Text);
            }
            return sb.ToString();
        }

        // Free space kept between a subtitle line and the frame edge (of the shorter side).
        internal const double EdgeMargin = 0.02;

        // About how wide libass draws a line in 微软雅黑 Bold: a CJK character is 0.75 of the font
        // size, Latin letters and digits about 0.6 of that, plus the outline.
        internal static double LineWidth(string text, int font)
        {
            double em = font * 0.75;
            double w = font * 0.14;
            foreach (char c in text) w += c >= (char)0x2E80 ? em : (c == ' ' ? em * 0.3 : em * 0.6);
            return w;
        }

        // A line centred on p, moved inward just enough to stay inside 0..size (the preview uses the
        // same rule, so what is seen there is where it ends up).
        internal static double Inside(double p, double half, double size, double margin)
        {
            double lo = half + margin;
            double hi = size - half - margin;
            if (lo > hi) return size / 2;
            return p < lo ? lo : (p > hi ? hi : p);
        }

        private static string Time(double s)
        {
            if (s < 0) s = 0;
            long cs = (long)Math.Round(s * 100);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", cs / 360000, cs / 6000 % 60, cs / 100 % 60, cs % 100);
        }

        internal static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
