using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace VideoMixApp
{
    // What 快拼 may do with one source video (v2.7.0). 成片不用 includes 开头不用.
    [Flags]
    internal enum ClipRule
    {
        None = 0,
        // Never the first clip of an output (the first one after the 片头 when there is one).
        NoOpen = 1,
        // Never part of an output.
        NoUse = 2
    }

    // 素材规则: the videos the user keeps out of the first position or out of the outputs altogether.
    // Like the 规格统一 mark, the rules stay with the material: every folder with videos has its own
    // small text file, "Axe素材规则.txt", that names the videos by file name. A product folder that is
    // copied, moved or opened on another PC keeps its rules, and the file can be read and edited in
    // Notepad. Folders Axe cannot write to (a read-only share, a CD) keep theirs in _rules in the
    // Axe folder instead.
    //
    // Reading fails loudly (ClipRulesException): making videos without the rules would put in
    // exactly the clips the user ruled out, so 快拼 stops instead.
    internal sealed class ClipRules
    {
        public const string FileName = "Axe素材规则.txt";
        public const string NoOpenWord = "开头不用";
        public const string NoUseWord = "成片不用";

        private sealed class Folder
        {
            public readonly Dictionary<string, ClipRule> Rules = new Dictionary<string, ClipRule>(StringComparer.OrdinalIgnoreCase);
            public bool Dirty;
        }

        private readonly string _appDir;
        private readonly Dictionary<string, Folder> _folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);

        public ClipRules(string appDir)
        {
            _appDir = appDir;
        }

        public ClipRule Of(string videoPath)
        {
            ClipRule r;
            return Get(Path.GetDirectoryName(videoPath)).Rules.TryGetValue(Path.GetFileName(videoPath), out r) ? r : ClipRule.None;
        }

        public void Set(string videoPath, ClipRule rule)
        {
            Folder f = Get(Path.GetDirectoryName(videoPath));
            string name = Path.GetFileName(videoPath);
            ClipRule old;
            if (!f.Rules.TryGetValue(name, out old)) old = ClipRule.None;
            if (old == rule) return;
            if (rule == ClipRule.None) f.Rules.Remove(name);
            else f.Rules[name] = rule;
            f.Dirty = true;
        }

        // How many of these videos carry each rule (a video with both counts as 成片不用).
        public void Count(IEnumerable<FileInfo> videos, out int noOpen, out int noUse)
        {
            noOpen = 0;
            noUse = 0;
            foreach (var v in videos)
            {
                ClipRule r = Of(v.FullName);
                if ((r & ClipRule.NoUse) != 0) noUse++;
                else if ((r & ClipRule.NoOpen) != 0) noOpen++;
            }
        }

        // "开头不用 2 · 成片不用 3", or "" without rules.
        public static string Summary(int noOpen, int noUse)
        {
            var parts = new List<string>();
            if (noOpen > 0) parts.Add(NoOpenWord + " " + noOpen);
            if (noUse > 0) parts.Add(NoUseWord + " " + noUse);
            return string.Join(" · ", parts.ToArray());
        }

        // Writes the folders whose rules changed. Returns null, or why a folder could not be saved.
        public string Save()
        {
            string error = null;
            foreach (var kv in _folders)
            {
                if (!kv.Value.Dirty) continue;
                string e = SaveFolder(kv.Key, kv.Value);
                if (e == null) kv.Value.Dirty = false;
                else if (error == null) error = e;
            }
            return error;
        }

        // One spelling per folder; a drive root keeps its backslash ("E:" alone would mean the current
        // folder of that drive).
        private static string Key(string dir)
        {
            string k = (dir ?? "").TrimEnd('\\', '/');
            return k.Length == 2 && k[1] == ':' ? k + "\\" : k;
        }

        // The rules file of a folder that cannot be written to.
        private string FallbackPath(string dir)
        {
            return Path.Combine(_appDir, "_rules", MixEngine.MakeTag(Key(dir)) + ".txt");
        }

        private Folder Get(string dir)
        {
            string key = Key(dir);
            Folder f;
            if (_folders.TryGetValue(key, out f)) return f;
            f = new Folder();
            string file = Current(key);
            if (file != null)
            {
                foreach (string line in ReadLines(file))
                {
                    string name;
                    ClipRule rule = Parse(line, out name);
                    if (rule == ClipRule.None) continue;
                    ClipRule old;
                    f.Rules[name] = f.Rules.TryGetValue(name, out old) ? old | rule : rule;
                }
            }
            _folders[key] = f;
            return f;
        }

        // The file that holds a folder's rules right now: its own, or the copy in the Axe folder when
        // that one is newer (the folder could not be written to when the rules were last changed).
        private string Current(string dir)
        {
            string own = Path.Combine(dir, FileName);
            string alt = FallbackPath(dir);
            bool hasOwn = File.Exists(own);
            bool hasAlt = File.Exists(alt);
            if (hasAlt && (!hasOwn || File.GetLastWriteTimeUtc(alt) > File.GetLastWriteTimeUtc(own))) return alt;
            return hasOwn ? own : null;
        }

        // A file held for a moment (a virus scanner, a cloud drive syncing it) is tried again.
        private static string[] ReadLines(string file)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    byte[] bytes;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    return Decode(bytes).Split('\n');
                }
                catch (FileNotFoundException)
                {
                    return new string[0];
                }
                catch (Exception ex) when (attempt >= 5)
                {
                    throw new ClipRulesException(file, ex.Message);
                }
                catch (Exception)
                {
                    Thread.Sleep(150 * attempt);
                }
            }
        }

        // Notepad saves UTF-8 (Windows 10 1903 and later, how Axe writes the file), UTF-16, or the ANSI
        // code page (GBK on Chinese Windows, older Notepad's default). Text that is not valid UTF-8 is
        // read as GBK, so a file saved that way still matches its Chinese file names.
        internal static string Decode(byte[] b)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(b);
            }
            catch (DecoderFallbackException)
            {
            }
            try
            {
                return Encoding.GetEncoding(936).GetString(b);
            }
            catch
            {
                return Encoding.Default.GetString(b);
            }
        }

        // "开头不用<Tab>名称.mp4". A line typed by hand may use spaces or a colon instead of the tab.
        // Lines starting with # and anything else are ignored.
        internal static ClipRule Parse(string line, out string name)
        {
            name = null;
            if (line == null) return ClipRule.None;
            string s = line.TrimEnd('\r', '\n').TrimStart(' ', '\t', '\u3000', '\uFEFF');
            if (s.Length == 0 || s[0] == '#') return ClipRule.None;
            ClipRule rule;
            string word;
            if (s.StartsWith(NoOpenWord, StringComparison.Ordinal))
            {
                rule = ClipRule.NoOpen;
                word = NoOpenWord;
            }
            else if (s.StartsWith(NoUseWord, StringComparison.Ordinal))
            {
                rule = ClipRule.NoUse;
                word = NoUseWord;
            }
            else
            {
                return ClipRule.None;
            }
            string rest = s.Substring(word.Length);
            // After a tab the name is taken as it is (a file name cannot end in a space); separators
            // typed by hand are trimmed.
            if (rest.Length > 0 && rest[0] == '\t') rest = rest.Substring(1).TrimEnd(' ', '\t');
            else rest = rest.Trim(' ', '\t', '　', ':', '：');
            if (rest.Length == 0) return ClipRule.None;
            name = rest;
            return rule;
        }

        private string SaveFolder(string dir, Folder f)
        {
            var names = new List<string>();
            foreach (var kv in f.Rules)
            {
                // Rules of videos that are no longer in the folder are dropped, so a different video
                // that later gets the same name does not inherit them.
                if (kv.Value != ClipRule.None && File.Exists(Path.Combine(dir, kv.Key))) names.Add(kv.Key);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            string own = Path.Combine(dir, FileName);
            string alt = FallbackPath(dir);
            if (names.Count == 0)
            {
                string e = Remove(own);
                // The folder's own file cannot be removed: an empty copy in the Axe folder, which is
                // newer, takes its place.
                if (e != null) return Write(alt, Text(dir, f, names), true);
                Remove(alt);
                return null;
            }
            string text = Text(dir, f, names);
            if (Write(own, text, false) == null)
            {
                Remove(alt);
                return null;
            }
            return Write(alt, text, true);
        }

        private static string Text(string dir, Folder f, List<string> names)
        {
            var sb = new StringBuilder();
            sb.Append("# Axe 素材规则（在 Axe 的「素材规则」里勾选后自动保存，和素材放在一起，拷到别的电脑照样有效）\r\n");
            sb.Append("# 一行一个视频：规则、Tab、视频文件名。删掉一行就取消那条规则，删掉这个文件就全部取消。\r\n");
            sb.Append("# 文件夹：").Append(dir).Append("\r\n");
            foreach (string n in names)
            {
                ClipRule r = f.Rules[n];
                if ((r & ClipRule.NoOpen) != 0) sb.Append(NoOpenWord).Append('\t').Append(n).Append("\r\n");
                if ((r & ClipRule.NoUse) != 0) sb.Append(NoUseWord).Append('\t').Append(n).Append("\r\n");
            }
            return sb.ToString();
        }

        // Written next to the file and swapped in, so a crash never leaves half a file.
        private static string Write(string file, string text, bool createDir)
        {
            string tmp = file + ".tmp";
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (createDir) Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllText(tmp, text, new UTF8Encoding(true));
                    if (File.Exists(file))
                    {
                        try
                        {
                            File.Replace(tmp, file, null);
                            return null;
                        }
                        catch
                        {
                            // Not supported on some network and FAT drives: delete and move instead.
                        }
                        File.Delete(file);
                    }
                    File.Move(tmp, file);
                    return null;
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    // No permission is not going to change; a file held for a moment may.
                    if (ex is UnauthorizedAccessException || !(ex is IOException) || attempt >= 4) return ex.Message;
                    Thread.Sleep(200 * attempt);
                }
            }
        }

        private static string Remove(string file)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    internal sealed class ClipRulesException : Exception
    {
        public readonly string File;

        public ClipRulesException(string file, string message) : base(message)
        {
            File = file;
        }
    }
}
