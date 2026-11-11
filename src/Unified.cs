using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VideoMixApp
{
    // Since v2.3.0 a unified video replaces its source in the product folder and carries a mark in
    // its MP4 metadata, e.g. "AxeUnified/1 720x1280@30". Whoever opens that folder later (another
    // copy, another PC) sees which videos are already unified without any record in the Axe folder.
    // The number after the slash is the encoding profile: clips can only be joined by stream copy
    // when they were encoded the same way, so a future change of settings would raise it.
    internal static class Unified
    {
        public const int Profile = 1;
        // Unfinished output next to the source, and the original kept during a non-atomic replace.
        // Neither ends in .mp4, so neither is ever read as a source.
        public const string PartExt = ".axepart";
        public const string OldExt = ".axeold";

        private static readonly Regex MarkRe = new Regex("^AxeUnified/(\\d+) (\\d+)x(\\d+)@(\\d+)$");

        public static string Mark(int width, int height, int fps)
        {
            return string.Format(CultureInfo.InvariantCulture, "AxeUnified/{0} {1}x{2}@{3}", Profile, width, height, fps);
        }

        // true for any Axe mark; size and frame rate are returned so a caller can tell "unified for
        // another ratio" from "not unified".
        public static bool ParseMark(string comment, out int width, out int height, out int fps)
        {
            width = height = fps = 0;
            if (string.IsNullOrEmpty(comment)) return false;
            Match m = MarkRe.Match(comment.Trim());
            if (!m.Success) return false;
            width = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            height = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            fps = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            return true;
        }

        // Header facts of one file version. A status check reads every video of a folder; this keeps
        // the second and later checks (window activation, the folder picker) to one stat call each.
        public sealed class Facts
        {
            public long Size;
            public long Ticks;
            public bool Readable;
            public bool Video;
            public bool Audio;
            public double Duration;
            public string Mark;
            // Bytes and length of the video track (Mp4Probe.Info); -1 = not read yet (facts kept by
            // v2.4 and earlier, which are read again once).
            public long VideoBytes = -1;
            public double VideoSeconds;
        }

        private static readonly ConcurrentDictionary<string, Facts> Cache = new ConcurrentDictionary<string, Facts>(StringComparer.OrdinalIgnoreCase);
        private static readonly object StoreGate = new object();
        private static string _store;
        private static volatile bool _dirty;

        // The facts are also kept in _videoinfo.tsv in the Axe folder, so the first status check after
        // starting Axe only looks at file sizes and dates, as before v2.3, instead of opening every
        // video. It is only a speed-up: a file whose size or date changed is read again.
        public static void UseStore(string appDir)
        {
            _store = Path.Combine(appDir, "_videoinfo.tsv");
            try
            {
                if (!File.Exists(_store)) return;
                var inv = CultureInfo.InvariantCulture;
                foreach (string line in File.ReadAllLines(_store, Encoding.UTF8))
                {
                    string[] c = line.Split('\t');
                    long size;
                    long ticks;
                    int flags;
                    double dur;
                    if (c.Length < 6 || !long.TryParse(c[1], NumberStyles.Integer, inv, out size) || !long.TryParse(c[2], NumberStyles.Integer, inv, out ticks)
                        || !int.TryParse(c[3], NumberStyles.Integer, inv, out flags) || !double.TryParse(c[4], NumberStyles.Float, inv, out dur)) continue;
                    var x = new Facts { Size = size, Ticks = ticks, Readable = (flags & 1) != 0, Video = (flags & 2) != 0, Audio = (flags & 4) != 0, Duration = dur, Mark = c[5].Length > 0 ? c[5] : null };
                    // Two more columns since v2.5 (older versions read the first six and ignore them).
                    long vb;
                    double vs;
                    if (c.Length >= 8 && long.TryParse(c[6], NumberStyles.Integer, inv, out vb) && double.TryParse(c[7], NumberStyles.Float, inv, out vs))
                    {
                        x.VideoBytes = vb;
                        x.VideoSeconds = vs;
                    }
                    Cache.TryAdd(c[0], x);
                }
            }
            catch
            {
            }
        }

        public static void SaveStore()
        {
            if (_store == null || !_dirty) return;
            lock (StoreGate)
            {
                if (!_dirty) return;
                _dirty = false;
                try
                {
                    var inv = CultureInfo.InvariantCulture;
                    var sb = new StringBuilder();
                    foreach (var kv in Cache)
                    {
                        Facts x = kv.Value;
                        int flags = (x.Readable ? 1 : 0) | (x.Video ? 2 : 0) | (x.Audio ? 4 : 0);
                        sb.Append(kv.Key).Append('\t').Append(x.Size.ToString(inv)).Append('\t').Append(x.Ticks.ToString(inv)).Append('\t')
                          .Append(flags.ToString(inv)).Append('\t').Append(x.Duration.ToString("R", inv)).Append('\t').Append(x.Mark ?? "").Append('\t')
                          .Append(x.VideoBytes.ToString(inv)).Append('\t').Append(x.VideoSeconds.ToString("R", inv)).Append('\n');
                    }
                    string tmp = _store + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(_store)) File.Replace(tmp, _store, null);
                    else File.Move(tmp, _store);
                }
                catch
                {
                    _dirty = true;
                }
            }
        }

        public static Facts Read(FileInfo f)
        {
            long size;
            long ticks;
            try
            {
                f.Refresh();
                size = f.Length;
                ticks = f.LastWriteTimeUtc.Ticks;
            }
            catch
            {
                return new Facts();
            }
            Facts known;
            if (Cache.TryGetValue(f.FullName, out known) && known.Size == size && known.Ticks == ticks && (known.VideoBytes >= 0 || !known.Readable)) return known;
            var facts = new Facts { Size = size, Ticks = ticks };
            Mp4Probe.Info info = Mp4Probe.TryRead(f.FullName);
            if (info != null)
            {
                facts.Readable = true;
                facts.Video = info.HasVideo;
                facts.Audio = info.HasAudio;
                facts.Duration = info.Duration;
                facts.Mark = info.Comment;
                facts.VideoBytes = info.VideoBytes;
                facts.VideoSeconds = info.VideoSeconds;
            }
            // A mark or a tab in a name would break a line of the store.
            if (facts.Mark != null && (facts.Mark.IndexOf('\t') >= 0 || facts.Mark.IndexOf('\n') >= 0)) facts.Mark = facts.Mark.Replace('\t', ' ').Replace('\n', ' ');
            if (Cache.Count > 200000) Cache.Clear();
            Cache[f.FullName] = facts;
            _dirty = true;
            return facts;
        }

        public static void Forget(string path)
        {
            Facts unused;
            if (Cache.TryRemove(path, out unused)) _dirty = true;
        }

        // Unified for exactly this size and frame rate, complete enough to be joined.
        public static bool IsUnified(Facts x, string mark)
        {
            return x.Readable && x.Video && x.Audio && x.Duration > 0.2 && string.Equals(x.Mark, mark, StringComparison.Ordinal);
        }

        // Unified by Axe, but for another ratio or profile: re-encoding it in place would only lose
        // quality, so such a video gets its own copy in the cache instead.
        public static bool IsOtherSpec(Facts x, string mark)
        {
            int w, h, fps;
            return x.Readable && !string.Equals(x.Mark, mark, StringComparison.Ordinal) && ParseMark(x.Mark, out w, out h, out fps);
        }

        public static string PartPath(string source)
        {
            return source + PartExt;
        }

        // Moves the finished output over the source. The result keeps the source's name, creation
        // and modification time (Explorer keeps sorting the folder the same way). Returns null on
        // success, else why it could not be done; the source is then left untouched.
        public static string ReplaceInPlace(string part, string source)
        {
            DateTime created;
            DateTime written;
            FileAttributes attrs;
            try
            {
                var fi = new FileInfo(source);
                created = fi.CreationTimeUtc;
                written = fi.LastWriteTimeUtc;
                attrs = fi.Attributes;
                if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(source, attrs & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            string error = null;
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                error = TryReplace(part, source);
                if (error == null) break;
                // A player, 剪映 or a virus scanner may hold the file for a moment.
                Thread.Sleep(400 * attempt);
            }
            if (error != null)
            {
                if ((attrs & FileAttributes.ReadOnly) != 0) TrySetAttributes(source, attrs);
                return error;
            }
            try
            {
                File.SetCreationTimeUtc(source, created);
                File.SetLastWriteTimeUtc(source, written);
                if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(source, attrs);
            }
            catch
            {
                // The video itself is in place; only the dates could not be kept.
            }
            Forget(source);
            return null;
        }

        private static string TryReplace(string part, string source)
        {
            try
            {
                // One atomic step on NTFS: no moment without either version under the name.
                File.Replace(part, source, null, true);
                return null;
            }
            catch (FileNotFoundException ex)
            {
                return ex.Message;
            }
            catch
            {
                // Not supported everywhere (some network and FAT drives); done in three steps below.
            }
            string old = source + OldExt;
            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(source, old);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            try
            {
                File.Move(part, source);
            }
            catch (Exception ex)
            {
                // Put the original back; Recover() does the same after a crash at this point.
                try { File.Move(old, source); } catch { }
                return ex.Message;
            }
            try { File.Delete(old); } catch { }
            return null;
        }

        private static void TrySetAttributes(string path, FileAttributes a)
        {
            try { File.SetAttributes(path, a); } catch { }
        }

        // Cleans up after a run that was interrupted (power loss, crash) in these folders: half
        // written outputs are deleted, an original that was moved aside is put back or, when its
        // unified replacement made it into place, deleted.
        public static int Recover(IEnumerable<string> dirs, Action<string> log)
        {
            int n = 0;
            foreach (string dir in dirs)
            {
                string[] parts;
                string[] olds;
                try
                {
                    parts = Directory.GetFiles(dir, "*" + PartExt);
                    olds = Directory.GetFiles(dir, "*" + OldExt);
                }
                catch
                {
                    continue;
                }
                foreach (string p in parts)
                {
                    if (!p.EndsWith(PartExt, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(p); n++; } catch { }
                }
                foreach (string o in olds)
                {
                    if (!o.EndsWith(OldExt, StringComparison.OrdinalIgnoreCase)) continue;
                    string source = o.Substring(0, o.Length - OldExt.Length);
                    try
                    {
                        if (File.Exists(source)) File.Delete(o);
                        else
                        {
                            File.Move(o, source);
                            if (log != null) log("  已恢复上次中断时留下的原视频：" + Path.GetFileName(source));
                        }
                        n++;
                    }
                    catch
                    {
                    }
                }
            }
            return n;
        }

        private static readonly ConcurrentDictionary<string, bool> Writable = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Whether new files can be written next to the sources (not a CD, a write-protected USB
        // stick or a read-only share). Asked once per folder and run.
        public static bool CanWrite(string dir)
        {
            bool ok;
            if (Writable.TryGetValue(dir, out ok)) return ok;
            string probe = Path.Combine(dir, ".axe-write-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                ok = true;
            }
            catch
            {
                ok = false;
            }
            Writable[dir] = ok;
            return ok;
        }

        public static void ResetWritable()
        {
            Writable.Clear();
        }
    }
}
