using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoMixApp
{
    // Turns the 成片命名 template into file names. Tokens: {产品} {日期}（11月9日）{批次} {序号} {时长}.
    // {序号} continues after the files this template already produced in the product folder, so a
    // new batch never repeats an existing name; a template containing {日期} therefore starts again
    // from 0001 each day, one without it keeps counting up.
    internal sealed class OutputNaming
    {
        public const string DefaultFormat = "成片_{序号}";

        // Token, menu label, example shown in the menu ({日期}'s example is today, see Example).
        public static readonly string[][] Tokens =
        {
            new[] { "{产品}", "产品名", "密封泥胶" },
            new[] { "{日期}", "日期", "11月9日" },
            new[] { "{序号}", "序号", "0001" },
            new[] { "{批次}", "当天第几批", "1" },
            new[] { "{时长}", "时长（秒）", "35" }
        };

        // Dates in names and batch folders: 11月9日.
        public static string DateText(DateTime d)
        {
            return d.Month.ToString(CultureInfo.InvariantCulture) + "月" + d.Day.ToString(CultureInfo.InvariantCulture) + "日";
        }

        public static string Example(string[] token)
        {
            return token[0] == "{日期}" ? DateText(DateTime.Now) : token[2];
        }

        private static readonly Regex TokenRe = new Regex("\\{(产品|日期|批次|序号|时长)\\}");
        private const int MaxNameLength = 120;

        private readonly string _format;
        private readonly string _product;
        private readonly DateTime _date;
        private readonly int _batch;

        public OutputNaming(string format, string product, DateTime date, int batch)
        {
            _format = Normalize(format);
            _product = product ?? "";
            _date = date;
            _batch = batch;
        }

        public string Format
        {
            get { return _format; }
        }

        // Empty means the default; ".mp4" typed at the end is dropped; a template without {序号}
        // gets "_{序号}" appended, otherwise every video of a batch would get the same name.
        public static string Normalize(string format)
        {
            string f = (format ?? "").Trim();
            if (f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) f = f.Substring(0, f.Length - 4).TrimEnd();
            if (f.Length == 0) return DefaultFormat;
            if (f.IndexOf("{序号}", StringComparison.Ordinal) < 0) f += "_{序号}";
            return f;
        }

        public string FileName(int index, double seconds)
        {
            string name = Clean(Fill(index.ToString("D4", CultureInfo.InvariantCulture), ((int)Math.Round(seconds)).ToString(CultureInfo.InvariantCulture)));
            return name + ".mp4";
        }

        // First {序号} for a new batch: one after the highest number found on names this template
        // could have produced, in the product folder and its batch folders.
        public int NextIndex(string productDir)
        {
            if (!Directory.Exists(productDir)) return 1;
            Regex re = Pattern();
            var dirs = new List<string> { productDir };
            try { dirs.AddRange(Directory.GetDirectories(productDir)); } catch { }
            int max = 0;
            foreach (string d in dirs)
            {
                string[] files;
                try { files = Directory.GetFiles(d, "*.mp4"); }
                catch { continue; }
                foreach (string f in files)
                {
                    Match m = re.Match(Path.GetFileNameWithoutExtension(f));
                    int n;
                    if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n > max) max = n;
                }
            }
            return max + 1;
        }

        private string Fill(string index, string seconds)
        {
            return TokenRe.Replace(_format, m => Value(m.Groups[1].Value, index, seconds));
        }

        private string Value(string token, string index, string seconds)
        {
            switch (token)
            {
                case "产品": return _product;
                case "日期": return DateText(_date);
                case "批次": return _batch.ToString(CultureInfo.InvariantCulture);
                case "时长": return seconds;
                default: return index;
            }
        }

        // Matches names produced by this template for the same product, date and batch values;
        // the first {序号} is captured, {时长} may be any number.
        private Regex Pattern()
        {
            var sb = new StringBuilder("^");
            int pos = 0;
            bool captured = false;
            foreach (Match m in TokenRe.Matches(_format))
            {
                string literal = ReplaceInvalid(_format.Substring(pos, m.Index - pos));
                sb.Append(Regex.Escape(pos == 0 ? literal.TrimStart() : literal));
                string token = m.Groups[1].Value;
                if (token == "序号")
                {
                    sb.Append(captured ? "\\d+" : "(\\d+)");
                    captured = true;
                }
                else if (token == "时长")
                {
                    sb.Append("\\d+");
                }
                else
                {
                    sb.Append(Regex.Escape(ReplaceInvalid(Value(token, "", ""))));
                }
                pos = m.Index + m.Length;
            }
            sb.Append(Regex.Escape(ReplaceInvalid(_format.Substring(pos)).TrimEnd(' ', '.')));
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string Clean(string name)
        {
            string s = ReplaceInvalid(name).Trim().TrimEnd('.');
            if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength).TrimEnd(' ', '.');
            return s.Length == 0 ? "成片" : s;
        }

        private static string ReplaceInvalid(string s)
        {
            var chars = s.ToCharArray();
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(bad, chars[i]) >= 0) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
