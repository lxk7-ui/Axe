using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace VideoMixApp
{
    public partial class MixEngine
    {
        // 规格统一 of the chosen folder. Since v2.3.0 a unified video replaces its source in the
        // product folder and carries a mark (see Unified), so the folder itself holds the material 快拼
        // uses, wherever it is copied to. Copies in _cache_宽x高 remain for videos unified by v2.2 and
        // earlier (moved back into the folder here, by stream copy, without encoding again), videos
        // unified for another ratio, and folders Axe cannot write to.
        //
        // retryFailed: the 规格统一 buttons try failed files again; 快拼's automatic pass skips files
        // that failed before (until they change), so one broken video does not slow down every batch.
        // force (强制重建): copies in the cache are made again from their sources. A video already
        // replaced by its unified version is left as it is, because its original no longer exists.
        public Manifest Preprocess(string sourceRoot, string category, bool force, int parallelJobs, bool retryFailed = true)
        {
            Log("=== 阶段 1：规格统一 ===");
            if (_cancel || !EnsureFFmpeg()) return null;
            List<FileInfo> sources = ScanSources(sourceRoot, category);
            if (sources.Count == 0)
            {
                Log("素材目录中没有可用 MP4 文件。");
                return null;
            }
            Unified.ResetWritable();
            if (Unified.Recover(ScanFolders(sourceRoot, category), Log) > 0) sources = ScanSources(sourceRoot, category);
            string signature = ComputeSignature(sources);
            if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
            // Unfinished outputs of an interrupted run; finished clips are only ever renamed to norm_*.
            foreach (string f in Directory.GetFiles(CacheDir, "part_*.mp4")) TryDelete(f);

            Manifest old = LoadManifest();
            var oldSources = old != null && old.sources != null ? old.sources : new List<SourceEntry>();
            var oldDur = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            // Clips already verified to carry a sound track (absent in manifests of older versions).
            var withSound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (old != null && old.clips != null)
            {
                foreach (var c in old.clips)
                {
                    Match m = NormRe.Match(c.file ?? "");
                    if (!m.Success) continue;
                    oldDur[m.Groups[1].Value] = c.duration;
                    if (c.audio) withSound.Add(m.Groups[1].Value);
                }
            }
            var oldFailed = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
            if (old != null && old.failed != null)
            {
                foreach (var se in old.failed)
                {
                    if (se != null && !string.IsNullOrEmpty(se.tag) && !string.IsNullOrEmpty(se.path)) oldFailed[se.tag] = se;
                }
            }

            if (force)
            {
                foreach (var f in sources) TryDelete(NormPath(MakeTag(f.FullName)));
                foreach (string f in Directory.GetFiles(CacheDir, "clip_*.mp4")) TryDelete(f);
            }

            // Records of earlier runs. A source deleted from this folder loses its record and cache (the
            // cache only after the copy/move check below, so a video moved to another subfolder keeps
            // it); a source elsewhere keeps both even while its drive is not connected.
            var srcMap = new Dictionary<string, SourceEntry>(StringComparer.OrdinalIgnoreCase);
            var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var se in oldSources)
            {
                if (se == null || string.IsNullOrEmpty(se.tag) || string.IsNullOrEmpty(se.path)) continue;
                if (File.Exists(se.path))
                {
                    // A record whose tag does not match its path (older cache layout): move the cache.
                    string tag = MakeTag(se.path);
                    if (!string.Equals(tag, se.tag, StringComparison.OrdinalIgnoreCase))
                    {
                        string from = NormPath(se.tag);
                        string to = NormPath(tag);
                        if (File.Exists(from))
                        {
                            if (File.Exists(to)) TryDelete(from);
                            else TryMove(from, to);
                        }
                        double d;
                        if (oldDur.TryGetValue(se.tag, out d)) oldDur[tag] = d;
                        if (withSound.Contains(se.tag)) withSound.Add(tag);
                    }
                    srcMap[tag] = new SourceEntry { tag = tag, path = se.path, category = se.category, size = se.size, lastWrite = se.lastWrite };
                }
                else if (!IsInside(sourceRoot, se.path))
                {
                    srcMap[se.tag] = se;
                }
                else
                {
                    gone.Add(se.tag);
                }
            }

            // What each source looked like when its cached copy was made, for change detection.
            var recorded = new Dictionary<string, SourceEntry>(srcMap, StringComparer.OrdinalIgnoreCase);
            var tagToFile = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in sources)
            {
                string tag = MakeTag(f.FullName);
                tagToFile[tag] = f;
                srcMap[tag] = NewEntry(tag, f);
            }

            // Light check of the cached copies. Files recorded in the previous manifest are trusted by
            // existence and size; anything else (e.g. a half-written file from an interrupted run) is
            // opened and validated in-process. Copies made by older versions are checked once for a
            // sound track, because one clip without sound breaks concat copy.
            var valid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int noSound = 0;
            foreach (string file in Directory.GetFiles(CacheDir, "norm_*.mp4"))
            {
                Match m = NormRe.Match(Path.GetFileName(file));
                if (!m.Success || new FileInfo(file).Length < 10240)
                {
                    TryDelete(file);
                    continue;
                }
                string tag = m.Groups[1].Value;
                // Cache of a deleted source: reused by a moved copy below or removed afterwards.
                if (gone.Contains(tag) && !srcMap.ContainsKey(tag)) continue;
                bool known = srcMap.ContainsKey(tag) && oldDur.ContainsKey(tag);
                bool checkSound = tagToFile.ContainsKey(tag) && !withSound.Contains(tag);
                if (known && !checkSound)
                {
                    valid.Add(tag);
                    continue;
                }
                var info = Mp4Probe.TryRead(file);
                if (checkSound && info != null && info.HasVideo && !info.HasAudio)
                {
                    TryDelete(file);
                    noSound++;
                    continue;
                }
                if (checkSound && info != null && info.HasAudio) withSound.Add(tag);
                double dur = info != null && info.Duration > 0 ? info.Duration : ProbeDuration(file);
                if (known || dur > 0.2) valid.Add(tag);
                else TryDelete(file);
            }
            if (noSound > 0) Log(string.Format("{0} 个旧缓存没有音轨，将重新统一并补静音轨。", noSound));
            foreach (string f in Directory.GetFiles(CacheDir, "clip_*.mp4")) TryDelete(f);
            if (_cancel)
            {
                Log("已停止规格统一。");
                return null;
            }

            string mark = UnifyMark;
            var todo = new List<Job>();
            var cacheTodo = new List<KeyValuePair<string, FileInfo>>();
            var keepFailed = new List<SourceEntry>();
            var superseded = new List<string>();
            var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int inPlace = 0;
            int kept = 0;
            foreach (var kv in tagToFile)
            {
                string tag = kv.Key;
                FileInfo f = kv.Value;
                Unified.Facts x = Unified.Read(f);
                if (Unified.IsUnified(x, mark))
                {
                    inPlace++;
                    // A copy from v2.2 or a stopped run is no longer needed once the video is in place.
                    if (File.Exists(NormPath(tag))) superseded.Add(tag);
                    continue;
                }
                SourceEntry rec;
                if (valid.Contains(tag) && recorded.TryGetValue(tag, out rec) && UnifyRecord.IsChanged(rec, f))
                {
                    valid.Remove(tag);
                    Log("  素材有改动，重新统一：" + f.Name);
                }
                bool other = Unified.IsOtherSpec(x, mark);
                bool here = !other && Unified.CanWrite(f.DirectoryName);
                if (valid.Contains(tag))
                {
                    if (here)
                    {
                        todo.Add(AdoptJob(tag, f, NormPath(tag)));
                        inUse.Add(NormPath(tag));
                    }
                    else
                    {
                        kept++;
                    }
                    continue;
                }
                SourceEntry fe;
                if (!retryFailed && oldFailed.TryGetValue(tag, out fe) && SameVersion(fe, f))
                {
                    keepFailed.Add(fe);
                    continue;
                }
                TryDelete(NormPath(tag));
                if (!here)
                {
                    cacheTodo.Add(kv);
                    continue;
                }
                // A video copied or moved after an older version unified it.
                string copy = force ? null : FindCopy(f, oldSources);
                if (copy != null)
                {
                    todo.Add(AdoptJob(tag, f, copy));
                    inUse.Add(copy);
                }
                else
                {
                    todo.Add(InPlaceJob(tag, f));
                }
            }
            if (!force && cacheTodo.Count > 0 && oldSources.Count > 0)
            {
                int reused = ReuseCopiedCache(cacheTodo, oldSources, oldDur, withSound);
                if (reused > 0)
                {
                    Log(string.Format("已复用 {0} 个此前统一过的素材（文件只是被复制或移动），省去重新转码。", reused));
                    kept += reused;
                }
            }
            foreach (var kv in cacheTodo) todo.Add(CacheJob(kv.Key, kv.Value));
            foreach (string t in superseded)
            {
                // Unless a copied video is about to be moved into place from it.
                if (!inUse.Contains(NormPath(t))) TryDelete(NormPath(t));
            }
            int cleaned = 0;
            foreach (string t in gone)
            {
                if (srcMap.ContainsKey(t)) continue;
                string p = NormPath(t);
                if (!File.Exists(p) || inUse.Contains(p)) continue;
                TryDelete(p);
                cleaned++;
            }
            if (cleaned > 0) Log(string.Format("已清理 {0} 个已删除素材的缓存。", cleaned));

            int adopt = 0;
            int encode = 0;
            foreach (var j in todo)
            {
                if (j.Kind == JobKind.Adopt) adopt++;
                else encode++;
            }
            Log(string.Format("共 {0} 个视频：已统一 {1} 个，待统一 {2} 个{3}", sources.Count, inPlace + kept, encode,
                adopt > 0 ? string.Format("，另有 {0} 个旧版统一好的从软件缓存移回素材文件夹（只复制，不转码）", adopt) : ""));
            if (keepFailed.Count > 0) Log(string.Format("跳过 {0} 个上次统一失败的素材（点「规格统一」可重试）。", keepFailed.Count));
            if (force && inPlace > 0) Log(string.Format("强制重建：{0} 个视频已在原位置统一（原视频已被替换），不再重做。", inPlace));
            if (todo.Count > 0 && !CheckSpace(todo, parallelJobs)) return null;

            var sw = Stopwatch.StartNew();
            var failed = new List<Job>();
            bool completed = true;
            if (todo.Count > 0) completed = RunTranscodeJobs(todo, Math.Max(1, parallelJobs), failed, true, "规格统一");
            sw.Stop();

            // Copies that are no longer needed: a cached copy moved into place, the cache of a video
            // that is now unified in place. A copy shared with another existing source stays.
            var fresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int movedBack = 0;
            int nowInPlace = 0;
            foreach (var j in todo)
            {
                if (!j.Finished) continue;
                if (j.InCache)
                {
                    fresh.Add(j.Tag);
                    withSound.Add(j.Tag);
                    continue;
                }
                if (j.Kind == JobKind.Cache) continue;
                nowInPlace++;
                if (j.Kind == JobKind.Adopt) movedBack++;
                TryDelete(NormPath(j.Tag));
                if (j.AdoptedNorm != null && !string.Equals(j.AdoptedNorm, NormPath(j.Tag), StringComparison.OrdinalIgnoreCase) && !SourceExists(j.AdoptedNorm, srcMap))
                    TryDelete(j.AdoptedNorm);
            }

            // The record is saved in every case, so clips finished before a stop or next to a failed
            // file are kept and the folder status stays accurate.
            var newFailed = new List<SourceEntry>(keepFailed);
            foreach (var j in failed) newFailed.Add(srcMap[j.Tag]);
            foreach (var kv in oldFailed)
            {
                if (tagToFile.ContainsKey(kv.Key)) continue;
                if (File.Exists(kv.Value.path) || !IsInside(sourceRoot, kv.Value.path)) newFailed.Add(kv.Value);
            }
            Manifest manifest = SaveRecord(srcMap, oldDur, fresh, withSound, newFailed, signature);
            if (!completed)
            {
                int finished = 0;
                foreach (var j in todo) if (j.Finished) finished++;
                Log(string.Format("已停止规格统一：本次完成 {0} 个，还有 {1} 个未统一，下次点「规格统一」会接着处理。", finished, todo.Count - finished));
                return null;
            }
            if (failed.Count > 0)
            {
                Log(string.Format("{0} 个素材统一失败，快拼时不会选用：", failed.Count));
                foreach (var j in failed) Log("  失败: " + j.Origin + "（" + j.Problem + "）");
            }
            if (movedBack > 0) Log(string.Format("已把 {0} 个旧版统一好的素材移回素材文件夹，软件缓存相应减小。", movedBack));
            List<PoolClip> pool = BuildPool(manifest, sourceRoot, category);
            double total = 0.0;
            foreach (var c in pool) total += c.Duration;
            Log(string.Format("规格统一完成：{0}/{1} 个视频可用（{2} 个在素材原位置），总时长 {3:N1} 分钟，本次耗时 {4:N1} 分钟",
                pool.Count, sources.Count, inPlace + nowInPlace, total / 60.0, sw.Elapsed.TotalMinutes));
            Progress(100);
            return manifest;
        }

        // The folders ScanSources reads (also those whose only video is missing right now, so an
        // original moved aside by an interrupted replace is found and put back).
        private static List<string> ScanFolders(string sourceRoot, string category)
        {
            var list = new List<string>();
            try
            {
                var root = new DirectoryInfo(sourceRoot);
                if (string.IsNullOrEmpty(category) || root.Name == category) list.Add(root.FullName);
                foreach (var d in root.GetDirectories())
                {
                    if (IsSystemFolder(d)) continue;
                    if (string.IsNullOrEmpty(category) || d.Name == category) list.Add(d.FullName);
                }
            }
            catch
            {
            }
            return list;
        }

        // The cached copy of a video that was copied or moved after an older version unified it (same
        // name, size and modification time), or null. A copy whose source is gone (a move) is
        // preferred over one whose source still exists.
        private string FindCopy(FileInfo f, List<SourceEntry> oldSources)
        {
            string key = ReuseKey(f.Name, f.Length, f.LastWriteTimeUtc.ToString("o"));
            string best = null;
            foreach (var se in oldSources)
            {
                if (se == null || string.IsNullOrEmpty(se.path) || string.IsNullOrEmpty(se.tag) || string.IsNullOrEmpty(se.lastWrite)) continue;
                if (ReuseKey(Path.GetFileName(se.path), se.size, se.lastWrite) != key) continue;
                string norm = NormPath(se.tag);
                if (!File.Exists(norm) || new FileInfo(norm).Length < 10240) continue;
                if (!File.Exists(se.path)) return norm;
                if (best == null) best = norm;
            }
            return best;
        }

        // Whether a cached copy still belongs to a source that exists.
        private bool SourceExists(string norm, Dictionary<string, SourceEntry> srcMap)
        {
            Match m = NormRe.Match(Path.GetFileName(norm));
            SourceEntry se;
            return m.Success && srcMap.TryGetValue(m.Groups[1].Value, out se) && se != null && File.Exists(se.path);
        }

        // Room for the work: next to the sources a few unfinished outputs exist at once (each replaces
        // its source when done); copies kept in the cache all stay there.
        private bool CheckSpace(List<Job> jobs, int parallel)
        {
            var near = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
            long cache = 0;
            foreach (var j in jobs)
            {
                long size = j.Kind == JobKind.Adopt ? j.SrcBytes : OutputEstimate(j);
                if (j.Kind == JobKind.Cache)
                {
                    cache += size;
                    continue;
                }
                string root;
                try { root = Path.GetPathRoot(Path.GetFullPath(j.Origin)); }
                catch { continue; }
                List<long> list;
                if (!near.TryGetValue(root, out list)) near[root] = list = new List<long>();
                list.Add(size);
            }
            foreach (var kv in near)
            {
                kv.Value.Sort((a, b) => b.CompareTo(a));
                long need = 0;
                for (int i = 0; i < kv.Value.Count && i < Math.Max(1, parallel); i++) need += kv.Value[i];
                if (!EnsureDiskSpace(kv.Key, need, "素材")) return false;
            }
            return cache == 0 || EnsureDiskSpace(CacheDir, cache, "规格统一缓存");
        }

        // Size of one unified clip: its length at 6 Mbit/s (more than 720p at this quality needs) when
        // the header tells the length; else twice the source (a small, low-bitrate source can grow).
        // The source size alone is no measure: a 4K original is many times larger than its result.
        private static long OutputEstimate(Job j)
        {
            double sec = 0;
            try { sec = Unified.Read(new FileInfo(j.SrcPath)).Duration; } catch { }
            if (sec > 0) return Math.Max(10L << 20, (long)(sec * 750000));
            return Math.Max(j.SrcBytes * 2, 30L << 20);
        }

        private Job InPlaceJob(string tag, FileInfo f)
        {
            return new Job
            {
                Tag = tag,
                Name = f.Name,
                Kind = JobKind.InPlace,
                SrcPath = f.FullName,
                Origin = f.FullName,
                SrcBytes = f.Length,
                Out = f.FullName,
                Part = Unified.PartPath(f.FullName)
            };
        }

        private Job AdoptJob(string tag, FileInfo f, string norm)
        {
            long size = 0;
            try { size = new FileInfo(norm).Length; } catch { }
            return new Job
            {
                Tag = tag,
                Name = f.Name,
                Kind = JobKind.Adopt,
                SrcPath = norm,
                AdoptedNorm = norm,
                Origin = f.FullName,
                SrcBytes = size,
                Out = f.FullName,
                Part = Unified.PartPath(f.FullName)
            };
        }

        private Job CacheJob(string tag, FileInfo f)
        {
            return new Job
            {
                Tag = tag,
                Name = f.Name,
                Kind = JobKind.Cache,
                SrcPath = f.FullName,
                Origin = f.FullName,
                SrcBytes = f.Length,
                Out = NormPath(tag),
                Part = Path.Combine(CacheDir, "part_" + tag + ".mp4")
            };
        }

        // The unified clips of the folder that 快拼 can use right now (see UnifyRecord.Usable).
        private List<PoolClip> BuildPool(Manifest manifest, string sourceRoot, string category)
        {
            var record = UnifyRecord.From(manifest, CacheDir, UnifyMark);
            var pool = new List<PoolClip>();
            foreach (var f in ScanSources(sourceRoot, category))
            {
                PoolClip c = record.Usable(f);
                if (c != null) pool.Add(c);
            }
            return pool;
        }
    }
}
