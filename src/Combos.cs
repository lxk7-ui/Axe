using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace VideoMixApp
{
    // One clip as the combination count sees it.
    internal sealed class ComboClip
    {
        // norm_<tag>.mp4, as the clip is named in mix_history.csv; Path is the video itself, Key its
        // folder and file name (History.NameOf, lower case).
        public string Id;
        public string Path;
        public string Key;
        public double Duration;
        public bool NoOpen;
    }

    // 已用组合 / 剩余组合 / 总组合 of a folder at the chosen length (v2.7.0).
    internal sealed class ComboStats
    {
        public int Clips;
        public double Total;
        public double Used;
        // Records written before v2.7.0 keep no clip list; with many combinations they are counted by
        // folder and length instead of exactly.
        public bool UsedApprox;
        // So many clips fit into one video that lengths were counted in coarser steps (the total is
        // then astronomically large anyway).
        public bool TotalApprox;
        // All clips together are shorter than the minimum length (videos then repeat clips).
        public bool TooShort;

        public double Remaining
        {
            get { return Math.Max(0, Total - Used); }
        }
    }

    // One line of _history\mix_history.csv: time, ratio, source folder, target, tolerance, [category,
    // clips,] fingerprint. Versions before v2.7.0 wrote six columns; they read only the second and the
    // last column, so the two added ones do not disturb them.
    internal sealed class HistoryEntry
    {
        public string Ratio;
        public string Root;
        public string Category;
        public string Target;
        public string Tolerance;
        public string Fingerprint;
        // The clips by folder and file name, lower case (v2.7.0); null for older lines.
        public string[] Names;
        // Clip tags of lines written by v2.7.0 test builds; null otherwise.
        public string[] Tags;
    }

    // mix_history.csv as 快拼 uses it: every combination made, so none is made again (v2.7.0).
    //
    // Since v2.0 a combination is known by the fingerprint of its clips' full paths (the last column).
    // A path changes when the material is moved or renamed, copied to another PC, or its drive gets
    // another letter, and the old combinations would then come back. So since v2.7.0 a line also lists
    // its clips by folder and file name ("产品A\1.mp4"), which stays the same; and the paths of older
    // lines are seen through every folder of the same name (F:\素材\产品A\1.mp4 also matches what was
    // made as E:\素材\产品A\1.mp4).
    internal sealed class History
    {
        public readonly List<HistoryEntry> Entries = new List<HistoryEntry>();
        private readonly HashSet<string> _paths = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _roots = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Combinations on record (all ratios).
        public int Count
        {
            get { return _paths.Count; }
        }

        public static string FileOf(string appDir)
        {
            return System.IO.Path.Combine(appDir, "_history", "mix_history.csv");
        }

        // Read without blocking anyone (a second Axe window may be adding to it) and tried again for a
        // moment. null: the file is there but cannot be read; 快拼 must not make videos then, they
        // could repeat earlier ones.
        public static History Load(string appDir)
        {
            var h = new History();
            string file = FileOf(appDir);
            string text;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (!File.Exists(file)) return h;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        text = sr.ReadToEnd();
                    }
                    break;
                }
                catch (Exception) when (attempt < 6)
                {
                    Thread.Sleep(200 * attempt);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            foreach (string raw in text.Split('\n')) h.Add(raw.TrimEnd('\r'));
            return h;
        }

        private void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            List<string> f = ComboCounter.SplitCsv(line);
            HistoryEntry e;
            if (f.Count >= 6)
            {
                e = new HistoryEntry { Ratio = f[1].Trim(), Root = f[2], Target = f[3].Trim(), Tolerance = f[4].Trim(), Fingerprint = f[f.Count - 1].Trim() };
                if (f.Count >= 8)
                {
                    e.Category = f[5];
                    string clips = f[6].Trim();
                    if (clips.IndexOf('\\') >= 0)
                    {
                        var names = new List<string>();
                        foreach (string n in clips.Split('|')) if (n.Trim().Length > 0) names.Add(n.Trim().ToLowerInvariant());
                        if (names.Count > 0) e.Names = names.ToArray();
                    }
                    else if (clips.Length > 0)
                    {
                        e.Tags = clips.Split(';');
                    }
                }
            }
            else
            {
                // A damaged line (e.g. an unclosed quote): the ratio and the fingerprint as every
                // version since v2.0 reads them.
                string[] parts = line.Split(',');
                if (parts.Length < 6) return;
                e = new HistoryEntry { Ratio = parts[1].Trim(), Root = "", Target = "", Tolerance = "", Fingerprint = parts[parts.Length - 1].Trim() };
            }
            if (e.Fingerprint.Length == 0) return;
            Entries.Add(e);
            _paths.Add(e.Ratio + "|" + e.Fingerprint);
            if (e.Names != null) _names.Add(e.Ratio + "|" + MixEngine.Fingerprint(e.Names));
            string root = Norm(e.Root);
            if (root.Length == 0) return;
            HashSet<string> set;
            if (!_roots.TryGetValue(e.Ratio, out set)) _roots[e.Ratio] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(root);
        }

        // "产品A\1.mp4": the clip's folder and file name, how lines since v2.7.0 name it.
        public static string NameOf(string fullPath)
        {
            string dir = (System.IO.Path.GetDirectoryName(fullPath) ?? "").TrimEnd('\\', '/');
            return System.IO.Path.GetFileName(dir) + "\\" + System.IO.Path.GetFileName(fullPath);
        }

        // Full path, lower case, no trailing separator ("e:" for a drive root); "" when empty.
        internal static string Norm(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            string p = path.Trim();
            try
            {
                p = System.IO.Path.GetFullPath(p);
            }
            catch
            {
            }
            return p.TrimEnd('\\', '/').ToLowerInvariant();
        }

        private static string Parent(string norm)
        {
            int i = norm.LastIndexOf('\\');
            return i > 0 ? norm.Substring(0, i) : null;
        }

        private static string LastName(string norm)
        {
            int i = norm.LastIndexOf('\\');
            return i >= 0 ? norm.Substring(i + 1) : "";
        }

        // Folders of the same name, or two drive roots.
        private static bool SameName(string a, string b)
        {
            string na = LastName(a);
            string nb = LastName(b);
            return na.Length > 0 ? na == nb : nb.Length == 0;
        }

        // The folders a combination made from these clips may have been recorded under: for the source
        // folder, its parent and the clips' own folders (categories), every recorded folder of the same
        // name. Pairs (folder here, folder on record), lower case.
        public List<KeyValuePair<string, string>> Translations(string ratio, string sourceRoot, IEnumerable<string> clipPaths)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            HashSet<string> roots;
            if (ratio == null || !_roots.TryGetValue(ratio, out roots)) return pairs;
            var from = new HashSet<string>(StringComparer.Ordinal);
            string s = Norm(sourceRoot);
            if (s.Length > 0)
            {
                from.Add(s);
                string p = Parent(s);
                if (p != null) from.Add(p);
            }
            foreach (string c in clipPaths)
            {
                string d = Parent(Norm(c));
                if (d != null) from.Add(d);
            }
            foreach (string a in from)
            {
                foreach (string r in roots)
                {
                    if (a != r && SameName(a, r)) pairs.Add(new KeyValuePair<string, string>(a, r));
                }
            }
            return pairs;
        }

        // Whether this order of clips (full paths; fp = MixEngine.Fingerprint of their ids) was made
        // before for this ratio, also under another folder.
        public bool Used(string ratio, string fp, IList<string> paths, List<KeyValuePair<string, string>> pairs)
        {
            if (_paths.Contains(ratio + "|" + fp)) return true;
            if (_names.Count > 0)
            {
                var keys = new List<string>(paths.Count);
                foreach (string p in paths) keys.Add(NameOf(p).ToLowerInvariant());
                if (_names.Contains(ratio + "|" + MixEngine.Fingerprint(keys))) return true;
            }
            if (pairs == null) return false;
            foreach (var pr in pairs)
            {
                var ids = new List<string>(paths.Count);
                foreach (string p in paths)
                {
                    string lp = p.ToLowerInvariant();
                    if (!lp.StartsWith(pr.Key + "\\", StringComparison.Ordinal))
                    {
                        ids = null;
                        break;
                    }
                    ids.Add("norm_" + MixEngine.MakeTag(pr.Value + lp.Substring(pr.Key.Length)) + ".mp4");
                }
                if (ids != null && _paths.Contains(ratio + "|" + MixEngine.Fingerprint(ids))) return true;
            }
            return false;
        }
    }

    // The combinations 快拼 can make (see MixEngine.BuildOneBody): an order of different clips whose
    // length lies within [min, max], starting with a clip that may open a video, and ending where the
    // next clip would no longer fit. A clip left over that still fits would have been added, unless a
    // longer one that does not fit came first; so an order ends there exactly when the longest clip
    // left over does not fit (or no clip is left). Lengths are counted in tenths of a second.
    internal static class ComboCounter
    {
        // Listing combinations (exact 已用 for old records, the last few combinations of a batch) is
        // done up to this many.
        public const int ListLimit = 200000;
        private const int NodeLimit = 4000000;
        // Table cells × clips the count may take (about a second); beyond it lengths are counted in
        // coarser steps.
        private const double WorkLimit = 3e8;

        internal static int Units(double seconds)
        {
            return Math.Max(1, (int)Math.Round(seconds * 10));
        }

        internal static int Low(double min)
        {
            return Math.Max(0, (int)Math.Ceiling(min * 10 - 1e-6));
        }

        internal static int High(double max)
        {
            return (int)Math.Floor(max * 10 + 1e-6);
        }

        public static double Count(IList<ComboClip> clips, double min, double max)
        {
            bool approx;
            return CountCore(clips, min, max, null, out approx);
        }

        // How many orders there are. Grouped by the longest clip left over (u in descending order of
        // length): every longer clip is in the video, u is not, the shorter ones are counted by number
        // and length with a running table. A set of k clips with o openers gives o * (k - 1)! orders.
        // -1 when cancel() said so.
        internal static double CountCore(IList<ComboClip> clips, double min, double max, Func<bool> cancel, out bool approx)
        {
            approx = false;
            var order = new List<ComboClip>();
            foreach (var c in clips) if (c.Duration > 0) order.Add(c);
            int n = order.Count;
            if (n == 0 || High(max) <= 0 || Low(min) > High(max)) return 0;
            // Step in tenths of a second: 1, or more when the table would be too big.
            int step = 1;
            for (; ; step *= 2)
            {
                int hiS = (int)Math.Floor(max * 10 / step + 1e-6);
                var ascS = new List<int>();
                foreach (var c in order) ascS.Add(Math.Max(1, (int)Math.Round(c.Duration * 10 / step)));
                ascS.Sort();
                int kS = 0;
                long accS = 0;
                while (kS < n && accS + ascS[kS] <= hiS)
                {
                    accS += ascS[kS];
                    kS++;
                }
                if ((double)n * (kS + 1) * (hiS + 1) <= WorkLimit || hiS < 50) break;
                approx = true;
            }
            int lo = Math.Max(0, (int)Math.Ceiling(min * 10 / step - 1e-6));
            int hi = (int)Math.Floor(max * 10 / step + 1e-6);
            if (hi <= 0 || lo > hi) return 0;
            var w = new List<int>();
            var open = new List<bool>();
            order.Sort((a, b) => b.Duration.CompareTo(a.Duration));
            foreach (var c in order)
            {
                w.Add(Math.Max(1, (int)Math.Round(c.Duration * 10 / step)));
                open.Add(!c.NoOpen);
            }
            // Equal steps must stay in descending order (rounding keeps it, ties stay together).
            var asc = new List<int>(w);
            asc.Sort();
            int K = 0;
            long acc = 0;
            while (K < n && acc + asc[K] <= hi)
            {
                acc += asc[K];
                K++;
            }
            if (K == 0) return 0;
            var fact = new double[n + 1];
            fact[0] = 1;
            for (int i = 1; i <= n; i++) fact[i] = fact[i - 1] * i;
            var prefix = new long[n + 1];
            var openers = new int[n + 1];
            for (int i = 0; i < n; i++)
            {
                prefix[i + 1] = prefix[i] + w[i];
                openers[i + 1] = openers[i] + (open[i] ? 1 : 0);
            }
            double total = 0;
            if (prefix[n] >= lo && prefix[n] <= hi && openers[n] > 0) total += openers[n] * fact[n - 1];
            // cnt[k][s]: sets of k clips among those shorter than u with length s; opn[k][s]: their
            // openers added up.
            var cnt = new double[K + 1][];
            var opn = new double[K + 1][];
            for (int k = 0; k <= K; k++)
            {
                cnt[k] = new double[hi + 1];
                opn[k] = new double[hi + 1];
            }
            cnt[0][0] = 1;
            int rows = 0;
            for (int u = n - 1; u >= 0; u--)
            {
                if (cancel != null && (u & 15) == 0 && cancel()) return -1;
                if (u <= K && prefix[u] <= hi)
                {
                    long from = Math.Max(Math.Max(lo, hi - w[u] + 1) - prefix[u], 0);
                    long to = hi - prefix[u];
                    for (int k = 0; k <= Math.Min(K - u, rows) && from <= to; k++)
                    {
                        int size = u + k;
                        if (size == 0) continue;
                        double c = 0;
                        double o = 0;
                        double[] ck = cnt[k];
                        double[] ok = opn[k];
                        for (long s = from; s <= to; s++)
                        {
                            c += ck[s];
                            o += ok[s];
                        }
                        double weight = openers[u] * c + o;
                        if (weight > 0) total += weight * fact[size - 1];
                    }
                }
                if (u == 0) break;
                // Clip u joins the shorter ones for the next (longer) u.
                int wu = w[u];
                if (wu > hi) continue;
                int add = open[u] ? 1 : 0;
                for (int k = Math.Min(K - 1, rows); k >= 0; k--)
                {
                    double[] ck = cnt[k];
                    double[] ok = opn[k];
                    double[] cn = cnt[k + 1];
                    double[] on = opn[k + 1];
                    for (int s = hi - wu; s >= 0; s--)
                    {
                        double c = ck[s];
                        if (c == 0) continue;
                        cn[s + wu] += c;
                        on[s + wu] += ok[s] + add * c;
                    }
                }
                rows = Math.Min(K, rows + 1);
            }
            return total;
        }

        // Every combination as clip indices in playing order, or null when there are more than limit
        // (or listing them would take too long).
        public static List<int[]> List(IList<ComboClip> clips, double min, double max, int limit)
        {
            int lo = Low(min);
            int hi = High(max);
            var result = new List<int[]>();
            if (hi <= 0 || lo > hi) return result;
            var idx = new List<int>();
            for (int i = 0; i < clips.Count; i++) if (clips[i].Duration > 0) idx.Add(i);
            idx.Sort((a, b) => Units(clips[b].Duration).CompareTo(Units(clips[a].Duration)));
            int n = idx.Count;
            var w = new int[n];
            for (int i = 0; i < n; i++) w[i] = Units(clips[idx[i]].Duration);
            var used = new bool[n];
            var path = new int[n];
            long nodes = 0;
            bool overflow = false;
            Action<int, long> walk = null;
            walk = (depth, sum) =>
            {
                if (overflow) return;
                if (++nodes > NodeLimit)
                {
                    overflow = true;
                    return;
                }
                if (depth > 0 && sum >= lo)
                {
                    int longest = 0;
                    for (int i = 0; i < n; i++)
                    {
                        if (used[i]) continue;
                        longest = w[i];
                        break;
                    }
                    if (longest == 0 || sum + longest > hi)
                    {
                        if (result.Count >= limit)
                        {
                            overflow = true;
                            return;
                        }
                        var seq = new int[depth];
                        for (int i = 0; i < depth; i++) seq[i] = idx[path[i]];
                        result.Add(seq);
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    if (used[i] || sum + w[i] > hi) continue;
                    if (depth == 0 && clips[idx[i]].NoOpen) continue;
                    used[i] = true;
                    path[depth] = i;
                    walk(depth + 1, sum + w[i]);
                    used[i] = false;
                    if (overflow) return;
                }
            };
            walk(0, 0);
            return overflow ? null : result;
        }

        // Whether one recorded order (clips by id or by name, see byKey) is a combination of these clips
        // at this length.
        public static bool IsCombination(IList<ComboClip> clips, Dictionary<string, int> byKey, string[] keys, double min, double max)
        {
            int lo = Low(min);
            int hi = High(max);
            if (keys == null || keys.Length == 0) return false;
            var inside = new HashSet<int>();
            long sum = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                int k;
                if (!byKey.TryGetValue(keys[i], out k) || !inside.Add(k)) return false;
                if (i == 0 && clips[k].NoOpen) return false;
                sum += Units(clips[k].Duration);
            }
            if (sum < lo || sum > hi) return false;
            int longest = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (inside.Contains(i) || !(clips[i].Duration > 0)) continue;
                longest = Math.Max(longest, Units(clips[i].Duration));
            }
            return longest == 0 || sum + longest > hi;
        }

        // 已用 / 剩余 / 总. Exact where it can be: by listing every combination when there are few (then
        // every record matches the way 快拼 matches it, also old ones and moved folders), else by the
        // clip lists the records keep since v2.7.0; older records are then counted by folder and
        // length. null when cancel() said so.
        public static ComboStats Stats(IList<ComboClip> clips, double min, double max, History history, string ratio,
            string root, string category, string target, string tolerance, Func<bool> cancel)
        {
            var st = new ComboStats();
            double length = 0;
            foreach (var c in clips)
            {
                if (!(c.Duration > 0)) continue;
                st.Clips++;
                length += c.Duration;
            }
            st.TooShort = st.Clips > 0 && length < min;
            bool approx;
            st.Total = CountCore(clips, min, max, cancel, out approx);
            if (st.Total < 0) return null;
            st.TotalApprox = approx;
            if (st.Total <= 0 || history == null || history.Count == 0) return st;
            var paths = new List<string>();
            foreach (var c in clips) if (c.Path != null) paths.Add(c.Path);
            List<KeyValuePair<string, string>> pairs = history.Translations(ratio, root, paths);
            if (st.Total <= ListLimit)
            {
                List<int[]> all = List(clips, min, max, ListLimit);
                if (all != null)
                {
                    var ids = new List<string>();
                    var seqPaths = new List<string>();
                    int n = 0;
                    foreach (int[] seq in all)
                    {
                        if (cancel != null && (++n & 1023) == 0 && cancel()) return null;
                        ids.Clear();
                        seqPaths.Clear();
                        foreach (int i in seq)
                        {
                            ids.Add(clips[i].Id);
                            seqPaths.Add(clips[i].Path ?? "");
                        }
                        if (history.Used(ratio, MixEngine.Fingerprint(ids), seqPaths, pairs)) st.Used++;
                    }
                    return st;
                }
            }
            var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < clips.Count; i++)
            {
                if (clips[i].Id != null) byId[clips[i].Id] = i;
                if (clips[i].Key != null) byName[clips[i].Key] = i;
            }
            string s = History.Norm(root);
            var sameLevel = new HashSet<string>(StringComparer.Ordinal) { s };
            foreach (var pr in pairs) if (pr.Key == s) sameLevel.Add(pr.Value);
            var counted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var h in history.Entries)
            {
                if (h.Ratio != ratio || !counted.Add(h.Fingerprint)) continue;
                if (h.Names != null)
                {
                    if (IsCombination(clips, byName, h.Names, min, max)) st.Used++;
                }
                else if (h.Tags != null)
                {
                    var ids = new string[h.Tags.Length];
                    for (int i = 0; i < ids.Length; i++) ids[i] = "norm_" + h.Tags[i] + ".mp4";
                    if (IsCombination(clips, byId, ids, min, max)) st.Used++;
                }
                else if (string.IsNullOrEmpty(category) && sameLevel.Contains(History.Norm(h.Root)) && h.Target == target && h.Tolerance == tolerance)
                {
                    st.Used++;
                    st.UsedApprox = true;
                }
            }
            if (st.Used > st.Total) st.Used = st.Total;
            return st;
        }

        // 790, 3.2万, 1.5亿, 2万亿, 超过1亿亿.
        public static string Text(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v >= 1e16) return "超过1亿亿";
            if (v >= 1e12) return Short(v / 1e12) + "万亿";
            if (v >= 1e8) return Short(v / 1e8) + "亿";
            if (v >= 1e4) return Short(v / 1e4) + "万";
            return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
        }

        private static string Short(double v)
        {
            return v >= 100 ? Math.Floor(v).ToString("0", CultureInfo.InvariantCulture) : (Math.Floor(v * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);
        }

        // A CSV line with quoted fields ("" inside quotes is one quote).
        internal static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            // An unclosed quote: the line is damaged (the caller falls back to plain splitting).
            if (quoted) return new List<string>();
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
