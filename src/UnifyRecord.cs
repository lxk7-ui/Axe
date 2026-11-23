using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace VideoMixApp
{
    // Result of checking one source folder.
    internal sealed class FolderStatus
    {
        public int Total;
        public int Done;
        // Videos that still need 规格统一, relative to the folder, with a reason suffix where useful.
        public readonly List<string> Waiting = new List<string>();
        public string Error;
        // 素材规则 of these videos (filled in by the main window; a video with both counts as 成片不用).
        public int NoOpen;
        public int NoUse;
        // Unified videos whose bitrate the next 规格统一 raises (v2.7.0); not in Waiting.
        public int LowRate;
    }

    // A unified clip 快拼 can use: Path is the file to join (the source itself, or its copy in the
    // cache); Id names the clip in mix_history.csv and is the same as in v2.2 (norm_<tag>.mp4), so
    // combinations made before the upgrade are still avoided.
    internal sealed class PoolClip
    {
        public string Id;
        public string Path;
        public string Source;
        public string Category;
        public double Duration;
        // 素材规则「开头不用」: never the first clip of an output (see ClipRules).
        public bool NoOpen;
    }

    // Whether source videos are unified for one ratio. Since v2.3.0 that is mainly a mark inside the
    // video itself (see Unified); the 规格统一 record of the ratio (manifest.json plus norm_*.mp4 in
    // _cache_宽x高) still describes copies kept in the cache: clips unified by v2.2 and earlier (moved
    // back into the product folder by the next 规格统一 or 快拼), copies of videos that are unified for
    // another ratio, and videos in folders Axe cannot write to. It also lists videos that failed.
    internal sealed class UnifyRecord
    {
        // LowRate (v2.7.0): unified, but under the bitrate floor and not raised for it yet.
        public enum State { Done, Cached, Pending, Changed, Failed, OtherRatio, LowRate }

        private static readonly Regex NormRe = new Regex("^norm_([0-9A-F]{12})\\.mp4$", RegexOptions.IgnoreCase);

        private readonly string _cacheDir;
        private readonly string _mark;
        // Bitrate floor in kbps (MixEngine.RaiseFloor), 0 = none.
        private readonly int _floor;
        private readonly HashSet<string> _norms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SourceEntry> _byTag = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _byReuseKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, SourceEntry> _failed = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _dur = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _withSound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private UnifyRecord(string cacheDir, string mark, int floorKbps)
        {
            _cacheDir = cacheDir;
            _mark = mark;
            _floor = floorKbps;
        }

        public string Mark
        {
            get { return _mark; }
        }

        public static UnifyRecord Load(string cacheDir, string mark, int floorKbps)
        {
            return From(MixEngine.ReadManifest(cacheDir), cacheDir, mark, floorKbps);
        }

        public static UnifyRecord From(Manifest m, string cacheDir, string mark, int floorKbps)
        {
            var r = new UnifyRecord(cacheDir, mark, floorKbps);
            try
            {
                if (Directory.Exists(cacheDir))
                {
                    foreach (var f in new DirectoryInfo(cacheDir).EnumerateFiles("norm_*.mp4"))
                    {
                        Match mm = NormRe.Match(f.Name);
                        if (mm.Success && f.Length >= 10240) r._norms.Add(mm.Groups[1].Value);
                    }
                }
            }
            catch
            {
            }
            if (m != null && m.sources != null)
            {
                foreach (var se in m.sources)
                {
                    if (se == null || string.IsNullOrEmpty(se.tag)) continue;
                    r._byTag[se.tag] = se;
                    if (!string.IsNullOrEmpty(se.path) && !string.IsNullOrEmpty(se.lastWrite))
                        r._byReuseKey[MixEngine.ReuseKey(Path.GetFileName(se.path), se.size, se.lastWrite)] = se.tag;
                }
            }
            if (m != null && m.clips != null)
            {
                foreach (var c in m.clips)
                {
                    Match mm = NormRe.Match(c.file ?? "");
                    if (!mm.Success) continue;
                    r._dur[mm.Groups[1].Value] = c.duration;
                    if (c.audio) r._withSound.Add(mm.Groups[1].Value);
                }
            }
            if (m != null && m.failed != null)
            {
                foreach (var se in m.failed)
                {
                    if (se != null && !string.IsNullOrEmpty(se.tag)) r._failed[se.tag] = se;
                }
            }
            return r;
        }

        public State Check(FileInfo f)
        {
            Unified.Facts x = Unified.Read(f);
            string tag;
            SourceEntry rec;
            if (Unified.IsUnified(x, _mark))
            {
                // Under the floor: raised by the next 规格统一, unless that cannot be done here (a folder
                // Axe cannot write to) or failed for this very version of the file.
                if (!Unified.LowRate(x, _floor) || !Unified.CanWrite(f.DirectoryName)) return State.Done;
                tag = MixEngine.MakeTag(f.FullName);
                return _failed.TryGetValue(tag, out rec) && rec.size == x.Size && rec.lastWrite == Stamp(f) ? State.Done : State.LowRate;
            }
            tag = MixEngine.MakeTag(f.FullName);
            if (_norms.Contains(tag))
            {
                return _byTag.TryGetValue(tag, out rec) && IsChanged(rec, f) ? State.Changed : State.Cached;
            }
            string lastWrite = Stamp(f);
            if (_failed.TryGetValue(tag, out rec) && rec.size == x.Size && rec.lastWrite == lastWrite) return State.Failed;
            // A copied or moved file whose original was unified by an older version.
            string other;
            if (_byReuseKey.TryGetValue(MixEngine.ReuseKey(f.Name, x.Size, lastWrite), out other) && _norms.Contains(other)) return State.Cached;
            if (Unified.IsOtherSpec(x, _mark)) return State.OtherRatio;
            return State.Pending;
        }

        // The clip 快拼 joins for this source, or null while it is not usable.
        public PoolClip Usable(FileInfo f)
        {
            Unified.Facts x = Unified.Read(f);
            string tag = MixEngine.MakeTag(f.FullName);
            var c = new PoolClip { Id = "norm_" + tag + ".mp4", Source = f.FullName, Category = f.Directory.Name };
            if (Unified.IsUnified(x, _mark))
            {
                c.Path = f.FullName;
                c.Duration = x.Duration;
                return c;
            }
            SourceEntry rec;
            double d;
            if (_norms.Contains(tag) && !(_byTag.TryGetValue(tag, out rec) && IsChanged(rec, f)) && _dur.TryGetValue(tag, out d) && d > 0)
            {
                c.Path = Path.Combine(_cacheDir, c.Id);
                c.Duration = d;
                return c;
            }
            return null;
        }

        // Cached clips made before sound tracks were verified (they are checked once).
        public bool NeedsSoundCheck(FileInfo f)
        {
            string tag = MixEngine.MakeTag(f.FullName);
            return _norms.Contains(tag) && !_withSound.Contains(tag);
        }

        internal static string Stamp(FileInfo f)
        {
            return f.LastWriteTimeUtc.ToString("o");
        }

        // The same videos 规格统一 would use for this folder (the folder itself and its subfolders).
        public FolderStatus CheckFolder(string root, string category)
        {
            var st = new FolderStatus();
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    st.Error = "目录不存在";
                    return st;
                }
                string top = root.TrimEnd('\\');
                List<FileInfo> files = MixEngine.ScanSources(root, category);
                // Videos never seen before are opened once to read their mark; the virus scanner checks
                // each on that first open, which goes much faster several at a time.
                System.Threading.Tasks.Parallel.ForEach(files, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 }, f => Unified.Read(f));
                foreach (var f in files)
                {
                    st.Total++;
                    State s = Check(f);
                    if (s == State.Done || s == State.Cached)
                    {
                        st.Done++;
                        continue;
                    }
                    if (s == State.LowRate)
                    {
                        st.Done++;
                        st.LowRate++;
                        continue;
                    }
                    string dir = f.DirectoryName.TrimEnd('\\');
                    string name = string.Equals(dir, top, StringComparison.OrdinalIgnoreCase) ? f.Name : Path.GetFileName(dir) + "\\" + f.Name;
                    if (s == State.Changed) name += "（已改动）";
                    else if (s == State.Failed) name += "（上次失败）";
                    else if (s == State.OtherRatio) name += "（其他比例）";
                    st.Waiting.Add(name);
                }
            }
            catch (Exception ex)
            {
                st.Error = ex.Message;
            }
            return st;
        }

        // Whether a source was replaced or edited after its cached copy was made. recorded holds what
        // the file looked like at that time: recorded.size (bytes) and recorded.lastWrite (UTC
        // modification time, DateTime "o" format). Only the size is compared: a replaced or
        // re-exported video practically always changes size, while the modification time also
        // changes when an untouched file is copied, unzipped or synced by a cloud drive. Records
        // without a size (older versions) count as unchanged.
        internal static bool IsChanged(SourceEntry recorded, FileInfo current)
        {
            if (recorded == null || recorded.size <= 0) return false;
            try
            {
                return current.Length != recorded.size;
            }
            catch
            {
                // The file vanished since the folder was read; the next scan handles it.
                return false;
            }
        }
    }
}
