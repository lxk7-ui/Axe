using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace VideoMixApp
{
    public partial class MixEngine
    {
        // Batch folder under the product folder: "11月9日（1）", "11月9日（2）"… The number continues after
        // the day's earlier batches, including folders named the v2.2 way ("11.09", "11.09（2）").
        internal static string BuildBatchDir(string productDir, DateTime date, out int batchNo)
        {
            string datePart = OutputNaming.DateText(date);
            string oldPart = date.ToString("MM.dd", CultureInfo.InvariantCulture);
            int max = 0;
            if (Directory.Exists(productDir))
            {
                var re = new Regex("^(?:" + Regex.Escape(datePart) + "|" + Regex.Escape(oldPart) + ")(?:[（(](\\d+)[）)])?$");
                foreach (string d in Directory.GetDirectories(productDir))
                {
                    Match m = re.Match(Path.GetFileName(d));
                    if (!m.Success) continue;
                    int n;
                    if (m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out n) && n > max) max = n;
                    else if (!m.Groups[1].Success && max < 1) max = 1;
                }
            }
            batchNo = max + 1;
            return Path.Combine(productDir, datePart + "（" + batchNo.ToString(CultureInfo.InvariantCulture) + "）");
        }

        // What 快拼 has to do first: videos not unified yet (waiting), clips an older version left in
        // the cache that can be moved back into the folder (moveBack), cached clips made before sound
        // tracks were verified (soundCheck).
        // Videos set to 成片不用 (skip) are not waited for: this batch leaves them out anyway. lowRate:
        // unified videos under the bitrate floor, raised once (v2.7.0).
        private void CountWaiting(Manifest manifest, List<FileInfo> sources, HashSet<string> skip, out int waiting, out int lowRate, out int moveBack, out bool soundCheck)
        {
            waiting = 0;
            lowRate = 0;
            moveBack = 0;
            soundCheck = false;
            var record = UnifyRecord.From(manifest, CacheDir, UnifyMark, RaiseFloor);
            foreach (var f in sources)
            {
                if (skip != null && skip.Contains(f.FullName)) continue;
                switch (record.Check(f))
                {
                    case UnifyRecord.State.Done:
                    case UnifyRecord.State.Failed:
                        break;
                    case UnifyRecord.State.LowRate:
                        lowRate++;
                        break;
                    case UnifyRecord.State.Cached:
                        if (record.NeedsSoundCheck(f)) soundCheck = true;
                        if (!Unified.IsOtherSpec(Unified.Read(f), UnifyMark) && Unified.CanWrite(f.DirectoryName)) moveBack++;
                        break;
                    default:
                        waiting++;
                        break;
                }
            }
        }

        // The videos set to 成片不用, or null when the 素材规则 cannot be read (logged).
        private HashSet<string> UnusedSources(ClipRules rules, List<FileInfo> sources)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in sources) if ((rules.Of(f.FullName) & ClipRule.NoUse) != 0) set.Add(f.FullName);
            }
            catch (ClipRulesException ex)
            {
                Log(RulesError(ex));
                return null;
            }
            return set;
        }

        private static string RulesError(ClipRulesException ex)
        {
            return "错误：素材规则文件读不出来（" + ex.File + "：" + ex.Message + "）。为了不把设成「不用」的素材用进成片，本次没有出片，请稍后再试。";
        }

        // The combinations of one batch. Clip lengths are in source seconds; with 变速 a video holds
        // speed times as much material for the same length.
        private List<Plan> BuildMixPlan(List<PoolClip> pool, int count, double targetSec, double tolerance, double introDuration,
            string introPath, double speed, History history, List<KeyValuePair<string, string>> moved, Random rng)
        {
            double lo = targetSec - tolerance;
            double hi = targetSec + tolerance;
            if (hi - introDuration < 1.0)
            {
                Log("错误：片头时长已接近或超过成片上限，请增大目标时长或更换更短的片头。");
                return null;
            }
            double bodyMin = Math.Max(0.0, lo - introDuration) * speed;
            double bodyMax = (hi - introDuration) * speed;
            var usable = new List<PoolClip>();
            // A clip without a known length would break the duration maths (and the picking loop).
            foreach (var c in pool) if (c.Duration > 0) usable.Add(c);
            if (usable.Count == 0)
            {
                Log("当前选定的素材目录下没有可用素材，请先点「规格统一处理」。");
                return null;
            }
            double poolTotal = 0.0;
            foreach (var c in usable) poolTotal += c.Duration;
            Log(string.Format("本次可选素材 {0} 个，共 {1:N0} 秒。", usable.Count, poolTotal));
            if (poolTotal < bodyMin)
            {
                Log(string.Format("警告：当前分类片段总时长（{0:N1}秒）小于正文下限，成片内将出现重复片段。", poolTotal));
            }
            var seen = new HashSet<string>();
            var plans = new List<Plan>();
            int maxAttempts = Math.Max(500, count * 30);
            int attempts = 0;
            while (plans.Count < count && attempts < maxAttempts)
            {
                if (_cancel) return null;
                attempts++;
                List<PoolClip> body = BuildOneBody(usable, bodyMin, bodyMax, rng);
                string fp = BodyFingerprint(body);
                // A combination made before is remembered here too, so a repeat costs no second look.
                if (!seen.Add(fp)) continue;
                if (history.Used(RatioTag, fp, Sources(body), moved)) continue;
                double bodyDur = 0.0;
                foreach (var c in body) bodyDur += c.Duration;
                plans.Add(new Plan
                {
                    Index = plans.Count + 1,
                    Picked = body,
                    IntroPath = introPath,
                    IntroDuration = introDuration,
                    TotalDuration = introDuration + bodyDur / speed,
                    Fingerprint = fp
                });
            }
            if (plans.Count < count && !_cancel && poolTotal >= bodyMin) TakeRemaining(usable, bodyMin, bodyMax, speed, introDuration, introPath, count, seen, history, moved, plans, rng);
            if (plans.Count < count)
            {
                Log(string.Format("警告：受历史组合数量限制，仅生成 {0} 条互不重复成片（要求 {1} 条）。", plans.Count, count));
            }
            return plans;
        }

        // Few combinations left (most are in the history): random tries mostly hit used ones and may
        // miss the last few, so with a pool small enough to list them all, the unused ones are taken
        // directly. The list follows BuildOneBody's rules (lengths in tenths of a second).
        private void TakeRemaining(List<PoolClip> usable, double bodyMin, double bodyMax, double speed, double introDuration, string introPath,
            int count, HashSet<string> seen, History history, List<KeyValuePair<string, string>> moved, List<Plan> plans, Random rng)
        {
            var clips = new List<ComboClip>();
            foreach (var c in usable) clips.Add(new ComboClip { Id = c.Id, Path = c.Source, Duration = c.Duration, NoOpen = c.NoOpen });
            List<int[]> all = ComboCounter.List(clips, bodyMin, bodyMax, ComboCounter.ListLimit);
            if (all == null) return;
            Shuffle(all, rng);
            int found = 0;
            foreach (int[] seq in all)
            {
                if (plans.Count >= count || _cancel) break;
                var body = new List<PoolClip>();
                double bodyDur = 0;
                foreach (int i in seq)
                {
                    body.Add(usable[i]);
                    bodyDur += usable[i].Duration;
                }
                if (body[0].NoOpen) continue;
                string fp = BodyFingerprint(body);
                if (!seen.Add(fp) || history.Used(RatioTag, fp, Sources(body), moved)) continue;
                plans.Add(new Plan
                {
                    Index = plans.Count + 1,
                    Picked = body,
                    IntroPath = introPath,
                    IntroDuration = introDuration,
                    TotalDuration = introDuration + bodyDur / speed,
                    Fingerprint = fp
                });
                found++;
            }
            if (found > 0) Log(string.Format("剩下的组合不多，已直接挑出 {0} 条没用过的组合。", found));
        }

        // The source videos of a combination, in order (as History names them).
        private static List<string> Sources(List<PoolClip> body)
        {
            var list = new List<string>(body.Count);
            foreach (var c in body) list.Add(c.Source);
            return list;
        }

        // 素材规则 (see ClipRules): 成片不用 videos leave the pool, 开头不用 ones stay in it but are kept
        // out of the first position by BuildOneBody. false = no videos can be made: the rules could
        // not be read (making videos without them would use exactly the clips ruled out), or they
        // leave nothing to use or nothing to start with.
        private bool ApplyClipRules(List<PoolClip> pool, ClipRules rules)
        {
            int unused = 0;
            int noOpen = 0;
            try
            {
                for (int i = pool.Count - 1; i >= 0; i--)
                {
                    ClipRule r = rules.Of(pool[i].Source);
                    if ((r & ClipRule.NoUse) != 0)
                    {
                        pool.RemoveAt(i);
                        unused++;
                    }
                    else if ((r & ClipRule.NoOpen) != 0)
                    {
                        pool[i].NoOpen = true;
                        noOpen++;
                    }
                }
            }
            catch (ClipRulesException ex)
            {
                Log(RulesError(ex));
                return false;
            }
            if (unused == 0 && noOpen == 0) return true;
            Log("素材规则：" + ClipRules.Summary(noOpen, unused));
            bool opener = false;
            foreach (var c in pool) if (!c.NoOpen && c.Duration > 0) opener = true;
            if (pool.Count == 0)
            {
                Log("错误：这个素材目录里能用的素材都设成了「成片不用」，没有素材可以出片。请在「素材规则」里取消几个再试。");
                return false;
            }
            if (!opener)
            {
                Log("错误：能用的素材都设成了「开头不用」，没有素材可以放在成片开头。请在「素材规则」里取消几个再试。");
                return false;
            }
            return true;
        }

        // The clips of one output in the order they play, each with the time it starts at in the video
        // ("0:00 a.mp4 | 0:12 b.mp4"), for 生成报告.csv: a clip seen in an output is found in the folder
        // at once, e.g. to give it a 素材规则. A clip of a subfolder is named with the subfolder.
        private static string ClipOrder(Plan p, double speed, string sourceRoot)
        {
            var sb = new StringBuilder();
            string top = (sourceRoot ?? "").TrimEnd('\\', '/');
            double t = p.IntroDuration;
            foreach (var c in p.Picked)
            {
                if (sb.Length > 0) sb.Append(" | ");
                int sec = (int)Math.Floor(t + 0.05);
                sb.Append((sec / 60).ToString(Inv)).Append(':').Append((sec % 60).ToString("00", Inv)).Append(' ');
                string dir = (Path.GetDirectoryName(c.Source) ?? "").TrimEnd('\\', '/');
                if (!string.Equals(dir, top, StringComparison.OrdinalIgnoreCase)) sb.Append(c.Category).Append('\\');
                sb.Append(Path.GetFileName(c.Source));
                t += c.Duration / speed;
            }
            return sb.ToString();
        }

        // The 片头 in the unified format: the file itself when it is unified already, else its copy in
        // the cache (made once). Returns its path, or null for no intro.
        public string EnsureIntro(string introSrc)
        {
            if (string.IsNullOrEmpty(introSrc)) return null;
            var fi = new FileInfo(introSrc);
            if (!fi.Exists)
            {
                Log("片头素材不存在，已忽略片头。");
                return null;
            }
            Unified.Facts facts = Unified.Read(fi);
            bool unified = Unified.IsUnified(facts, UnifyMark);
            if (unified && !Unified.LowRate(facts, RaiseFloor))
            {
                Log("片头已是统一格式，直接使用：" + fi.Name);
                return fi.FullName;
            }
            string tag;
            using (var sha = SHA256.Create())
            {
                string key = string.Concat(new object[] { fi.FullName, "|", fi.Length, "|", fi.LastWriteTimeUtc.Ticks });
                tag = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 12);
            }
            string path = Path.Combine(CacheDir, "intro_" + tag + ".mp4");
            double dur;
            // A copy made before the bitrate floor moved into 规格统一 (v2.7.0) may be under it.
            if (File.Exists(path) && CheckOutput(path, out dur) == null && !Unified.LowRate(Unified.Read(new FileInfo(path)), RaiseFloor))
            {
                Log("片头缓存有效，复用: " + Path.GetFileName(path));
                return path;
            }
            Log((unified ? "片头视频码率低于 " + RaiseFloor + " kbps，在软件缓存里做一份提高码率的（片头文件本身不动）: " : "正在标准化片头素材: ") + fi.Name);
            if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
            var job = new Job
            {
                Tag = tag,
                Name = fi.Name,
                Kind = JobKind.Intro,
                SrcPath = fi.FullName,
                Origin = fi.FullName,
                SrcBytes = fi.Length,
                Out = path,
                Part = Path.Combine(CacheDir, "part_intro_" + tag + ".mp4")
            };
            // Already unified, only under the floor: raised straight away, its sound copied.
            if (unified) StartRaise(job, Unified.Kbps(facts));
            var failed = new List<Job>();
            if (!RunTranscodeJobs(new List<Job> { job }, 1, failed, false, "片头处理")) return null;
            if (failed.Count > 0)
            {
                Log("片头处理失败，本次将不使用片头。");
                return null;
            }
            Log(string.Format("片头就绪，时长 {0:N1} 秒（计入总时长）", ProbeDuration(path)));
            return path;
        }

        private static string ConcatLine(string path)
        {
            // concat demuxer quoting: a single quote inside the path is written as '\''.
            return "file '" + path.Replace('\\', '/').Replace("'", "'\\''") + "'";
        }

        public string Mix(int count, double targetSec, double tolerance, int mixParallel, string sourceRoot, string category, string outputRoot, string introFile, string nameFormat)
        {
            Log("=== 阶段 2：随机快拼出片 ===");
            LastMade = 0;
            if (_cancel || !EnsureFFmpeg()) return null;
            EffectOptions fx = Effects != null && Effects.Any ? Effects : null;
            double speed = fx != null ? EffectOptions.ValidSpeed(fx.Speed) : 1.0;
            Unified.ResetWritable();
            // 素材规则 are read once, before anything else, and hold for the whole batch.
            var rules = new ClipRules(AppDir);
            List<FileInfo> sources = ScanSources(sourceRoot, category);
            HashSet<string> unused = UnusedSources(rules, sources);
            if (unused == null) return null;
            Manifest manifest = LoadManifest();
            int waiting;
            int lowRate;
            int moveBack;
            bool soundCheck;
            CountWaiting(manifest, sources, unused, out waiting, out lowRate, out moveBack, out soundCheck);
            if (waiting > 0 || lowRate > 0 || moveBack > 0 || soundCheck)
            {
                string raise = lowRate > 0 ? string.Format("{0} 个素材视频码率低于 {1} kbps，提高一次码率（每个素材只做一次，以后快拼直接拼接）", lowRate, RaiseFloor) : null;
                if (waiting > 0) Log(string.Format("当前素材目录有 {0} 个未统一的素材，先做规格统一{1}...", waiting, raise != null ? "；另有 " + raise : ""));
                else if (raise != null) Log("先做规格统一：" + raise + "...");
                else if (moveBack > 0) Log(string.Format("先把 {0} 个旧版统一好的素材从软件缓存移回素材文件夹（只复制，不转码）...", moveBack));
                else Log("先检查一遍旧版本统一的素材是否带音轨...");
                _autoSkip = unused.Count > 0 ? unused : null;
                try
                {
                    manifest = Preprocess(sourceRoot, category, false, Math.Max(mixParallel, 2), false);
                }
                finally
                {
                    _autoSkip = null;
                }
                if (manifest == null) return null;
            }
            if (_cancel) return null;
            string introPath = EnsureIntro(introFile);
            if (_cancel) return null;
            double introDur = 0.0;
            if (!string.IsNullOrEmpty(introPath)) introDur = ProbeDuration(introPath);
            double lo = targetSec - tolerance;
            double hi = targetSec + tolerance;
            string product = ProductName(sourceRoot, category);
            DateTime now = DateTime.Now;
            string stamp = now.ToString("yyyyMMdd_HHmmss");
            string productDir = Path.Combine(outputRoot, product);
            bool productDirExisted = Directory.Exists(productDir);
            int batchNo;
            string batchDir = BuildBatchDir(productDir, now, out batchNo);
            Directory.CreateDirectory(batchDir);
            Log(string.Format("输出位置：{0}（产品「{1}」，批次文件夹 {2}）", batchDir, product, Path.GetFileName(batchDir)));
            string taskRoot = Path.Combine(CacheDir, "_mix_tasks_" + stamp);
            Directory.CreateDirectory(taskRoot);
            var running = new List<Concat>();
            var produced = new List<string>();
            bool removeBatch = false;
            try
            {
                // Before v2.7.0 a history that could not be read (another program holding it) was
                // skipped, and the batch could repeat earlier combinations.
                History history = History.Load(AppDir);
                if (history == null)
                {
                    Log("错误：历史记录文件 _history\\mix_history.csv 读不出来（可能正被别的程序占用）。为了不出和以前重复的成片，本次没有出片，请稍后再试。");
                    removeBatch = true;
                    return null;
                }
                Log(string.Format("已载入历史组合记录 {0} 条，本次将避开这些组合。", history.Count));
                var rng = new Random((int)(DateTime.Now.Ticks % int.MaxValue));
                // Read the folder again: it may have changed while the unify pass ran.
                List<PoolClip> pool = BuildPool(manifest, sourceRoot, category);
                if (!ApplyClipRules(pool, rules))
                {
                    removeBatch = true;
                    return null;
                }
                var poolPaths = new List<string>();
                foreach (var c in pool) poolPaths.Add(c.Source);
                // Combinations recorded while this material lay elsewhere (another drive letter or PC).
                List<KeyValuePair<string, string>> moved = history.Translations(RatioTag, sourceRoot, poolPaths);
                List<Plan> plans = BuildMixPlan(pool, count, targetSec, tolerance, introDur, introPath, speed, history, moved, rng);
                if (plans == null || plans.Count == 0)
                {
                    removeBatch = true;
                    return null;
                }
                var naming = new OutputNaming(nameFormat, product, now, batchNo);
                int first = naming.NextIndex(productDir);
                var queue = new LinkedList<Concat>();
                var items = new List<Concat>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in plans)
                {
                    string listFile = Path.Combine(taskRoot, string.Format("concat_{0:D4}.txt", p.Index));
                    var lines = new List<string>();
                    // With 成片效果 the 片头 is its own input (it is not sped up).
                    if (!string.IsNullOrEmpty(p.IntroPath) && fx == null) lines.Add(ConcatLine(p.IntroPath));
                    foreach (var c in p.Picked) lines.Add(ConcatLine(c.Path));
                    File.WriteAllLines(listFile, lines, new UTF8Encoding(false));
                    string name = naming.FileName(first + p.Index - 1, p.TotalDuration);
                    string stem = Path.GetFileNameWithoutExtension(name);
                    for (int k = 2; names.Contains(name) || File.Exists(Path.Combine(batchDir, name)); k++) name = stem + "（" + k + "）.mp4";
                    names.Add(name);
                    var item = new Concat
                    {
                        Plan = p,
                        Name = name,
                        Out = Path.Combine(batchDir, name),
                        Part = Path.Combine(batchDir, "~" + Path.GetFileNameWithoutExtension(name) + ".part.mp4"),
                        ListFile = listFile
                    };
                    queue.AddLast(item);
                    items.Add(item);
                }
                Log(string.Format("成片命名：{0}（本批第一条：{1}）", naming.Format, queue.First.Value.Name));
                if (fx != null && !PrepareEffects(items, fx, taskRoot, speed, rng))
                {
                    removeBatch = true;
                    return null;
                }
                PlanBitrates(items);
                long need = 0;
                foreach (var c in items)
                {
                    long bytes = 0;
                    if (!string.IsNullOrEmpty(c.Plan.IntroPath) && File.Exists(c.Plan.IntroPath)) bytes += new FileInfo(c.Plan.IntroPath).Length;
                    foreach (var pc in c.Plan.Picked)
                    {
                        if (File.Exists(pc.Path)) bytes += new FileInfo(pc.Path).Length;
                    }
                    if (c.Fx != null) bytes = (long)(bytes / speed);
                    // Raised to the floor: picture at the set bitrate plus the sound.
                    if (c.AbrKbps > 0) bytes = Math.Max(bytes, (long)((c.AbrKbps + 160) * 125.0 * c.Plan.TotalDuration));
                    need += bytes;
                }
                if (!EnsureDiskSpace(batchDir, need, "成片输出"))
                {
                    removeBatch = true;
                    return null;
                }

                EnsureThreadPool(mixParallel);
                int encodes = 0;
                bool abr = false;
                foreach (var c in items)
                {
                    if (c.Encodes) encodes++;
                    if (c.AbrKbps > 0) abr = true;
                }
                if (encodes > 0) EnsureHardwareChecked();
                bool hwOn = encodes > 0 && _useHw;
                // The GPU also makes the outputs that need a set bitrate when it takes those settings
                // (checked with a tiny test encode); every result is measured afterwards anyway.
                bool hwAbr = hwOn && abr && HwEncoder.AverageBitrateWorks(FFmpeg, _hw);
                int gpuable = 0;
                foreach (var c in items) if (c.Encodes && (c.AbrKbps == 0 || hwAbr)) gpuable++;
                if (gpuable == 0) hwOn = false;
                int parallel = Math.Max(1, mixParallel);
                int hwCap = hwOn ? Math.Min(parallel, HwEncoder.MaxSessions(_hw)) : 0;
                int cores = Math.Max(1, Environment.ProcessorCount);
                int cpuCap = Math.Min(parallel, DeviceCheck.EncodeCap());
                if (encodes > 0)
                {
                    // Said plainly, because "重新编码" read like an extra step before the effects: a
                    // filter, sticker, subtitle or speed change alters the picture itself, so such a
                    // video is made in one encode, in which its clips are joined as well.
                    int withFx = 0;
                    foreach (var c in items) if (c.Fx != null) withFx++;
                    string why = withFx == encodes ? "加成片效果需要编码 {0} 条（拼接和效果在同一次编码里完成）"
                        : withFx == 0 ? "提高码率需要重新编码 {0} 条" : "加成片效果或提高码率需要编码 {0} 条";
                    Log(string.Format(why, encodes) + "：" + (hwOn ? string.Format("{0} 同时处理 {1} 条 + CPU 处理其余", HwEncoder.DisplayName(_hw), hwCap)
                        : string.Format("CPU（libx264）同时处理 {0} 条", Math.Min(cpuCap, encodes))));
                }
                var okPlans = new List<Plan>();
                int done = 0;
                int failedCount = 0;
                double doneSec = 0;
                double totalSec = 0;
                foreach (var p in plans) totalSec += p.TotalDuration;
                var sw = Stopwatch.StartNew();
                var report = new List<string> { (fx != null ? "成片,下限秒,上限秒,实际秒,滤镜,贴纸,变速,字幕条数,视频码率kbps" : "成片,下限秒,上限秒,实际秒,视频码率kbps") + ",素材顺序" };
                long lastStatus = -10000;
                var memory = new MemoryGate();
                long lastLook = -1;
                while (queue.Count > 0 || running.Count > 0)
                {
                    if (_cancel)
                    {
                        removeBatch = true;
                        return null;
                    }
                    while (queue.Count > 0 && running.Count < parallel)
                    {
                        int hwRunning = 0;
                        int cpuRunning = 0;
                        foreach (var r in running)
                        {
                            if (r.UsingHw) hwRunning++;
                            else if (r.Encodes) cpuRunning++;
                        }
                        // The first waiting video that may start now: a CPU encode waits while cpuCap of
                        // them run, a stream-copy join or a GPU encode behind it may go first.
                        LinkedListNode<Concat> node = queue.First;
                        bool useHw = false;
                        for (; node != null; node = node.Next)
                        {
                            Concat q = node.Value;
                            useHw = q.Encodes && hwOn && (q.AbrKbps == 0 || hwAbr) && !q.HwFailed && hwRunning < hwCap;
                            if (!q.Encodes || useHw || cpuRunning < cpuCap) break;
                        }
                        if (node == null) break;
                        Concat c = node.Value;
                        long memNeed = c.Encodes ? 300 : 60;
                        if (!memory.MayStart(memNeed, running.Count))
                        {
                            NoteMemory(memory, running.Count);
                            break;
                        }
                        queue.Remove(node);
                        TryDelete(c.Part);
                        c.Task = new FfTask();
                        c.UsingHw = false;
                        if (!c.Encodes)
                        {
                            string args = string.Format("-hide_banner -nostdin -y -loglevel error -nostats -progress pipe:1 -f concat -safe 0 -i \"{0}\" -c copy -movflags +faststart \"{1}\"", c.ListFile, c.Part);
                            StartFFmpeg(args, c.Task);
                        }
                        else
                        {
                            int cpuJobs = Math.Max(1, Math.Min(cpuCap, Math.Min(parallel, running.Count + 1 + queue.Count) - (useHw ? hwRunning + 1 : hwRunning)));
                            int threads = Math.Max(2, Math.Min(cores, (int)Math.Ceiling(cores / (double)cpuJobs)));
                            c.UsingHw = useHw;
                            StartFFmpeg(c.Fx != null ? BuildEffectArgs(c, useHw, threads) : BuildRecodeArgs(c, useHw, threads), c.Task, taskRoot);
                        }
                        memory.Started(memNeed);
                        running.Add(c);
                    }
                    Thread.Sleep(100);
                    if (Slept(ref lastLook)) foreach (var r in running) RestartClock(r.Task);
                    foreach (var c in running.ToArray())
                    {
                        FfState st = CheckTask(c.Task, c.Encodes ? StallMs : CopyStallMs);
                        if (st == FfState.Running) continue;
                        running.Remove(c);
                        string problem = null;
                        if (st == FfState.Stalled)
                        {
                            Log(string.Format("  {0} {1} {2} 秒没有进展，自动结束该进程", c.Name, c.Encodes ? "处理" : "拼接", (c.Encodes ? StallMs : CopyStallMs) / 1000));
                            Finish(c.Task, true);
                            problem = c.Encodes ? "处理卡住" : "拼接卡住";
                        }
                        else if (st == FfState.ExitHung)
                        {
                            Finish(c.Task, true);
                        }
                        else
                        {
                            int code = Finish(c.Task, false);
                            if (code != 0) problem = "ffmpeg 出错（代码 " + code + "）";
                        }
                        if (_cancel)
                        {
                            TryDelete(c.Part);
                            removeBatch = true;
                            return null;
                        }
                        double actual = 0.0;
                        double kbps = 0.0;
                        if (problem == null) problem = CheckOutput(c.Part, out actual);
                        if (problem == null)
                        {
                            Mp4Probe.Info made = Mp4Probe.TryRead(c.Part);
                            kbps = made != null ? made.VideoKbps : 0;
                            // Still under the floor (an estimate was off, or a GPU encode came out lean):
                            // made again at a set average bitrate on the CPU, at most twice.
                            if (MinKbps > 0 && kbps > 0 && kbps < FloorKbps && c.RateTries < 2)
                            {
                                c.RateTries++;
                                if (!c.Encodes)
                                {
                                    c.Recode = true;
                                    // From now on progress follows encoding time, as for the other encodes.
                                    encodes++;
                                }
                                // x264 reliably reaches a set bitrate; a GPU that came out lean does not get it again.
                                if (c.UsingHw) c.HwFailed = true;
                                c.AbrKbps = Math.Max(TargetKbps, (int)Math.Round(c.AbrKbps * 1.25));
                                Log(string.Format("  {0} 视频码率 {1:N0} kbps，低于平台要求的 {2} kbps，重新编码到约 {3} kbps", c.Name, kbps, MinKbps, c.AbrKbps));
                                TryDelete(c.Part);
                                queue.AddFirst(c);
                                continue;
                            }
                        }
                        if (problem == null)
                        {
                            try
                            {
                                MoveIntoPlace(c.Part, c.Out);
                                produced.Add(c.Out);
                            }
                            catch (Exception ex)
                            {
                                problem = "无法保存：" + ex.Message;
                            }
                        }
                        if (problem != null)
                        {
                            TryDelete(c.Part);
                            if (c.UsingHw && !c.HwFailed)
                            {
                                // The GPU failed on this one; the CPU makes it again.
                                c.HwFailed = true;
                                Log("  显卡编码失败，自动改用 CPU 重新处理：" + c.Name);
                                queue.AddFirst(c);
                                continue;
                            }
                            if (!c.Retried)
                            {
                                c.Retried = true;
                                queue.AddFirst(c);
                                continue;
                            }
                        }
                        done++;
                        doneSec += c.Plan.TotalDuration;
                        if (encodes == 0) Progress((int)(done * 100.0 / plans.Count));
                        if (problem == null)
                        {
                            okPlans.Add(c.Plan);
                            string row = string.Format(Inv, "{0},{1},{2},{3}", CsvField(c.Name), lo, hi, actual);
                            if (c.Fx != null)
                            {
                                row += string.Format(Inv, ",{0},{1},{2},{3}", CsvField(FilterLabel(c.Fx)), CsvField(c.Fx.StickerName ?? "无"),
                                    EffectOptions.SpeedText(c.Fx.Speed), c.Fx.AssFile != null ? c.Fx.Cues : 0);
                            }
                            else if (fx != null)
                            {
                                row += ",无,无,1x,0";
                            }
                            row += string.Format(Inv, ",{0:0}", kbps);
                            row += "," + CsvField(ClipOrder(c.Plan, speed, sourceRoot));
                            report.Add(row);
                            string low = MinKbps > 0 && kbps > 0 && kbps < FloorKbps ? "，仍低于要求" : "";
                            Log(string.Format("  已完成 {0}/{1}: {2}（实际 {3:N1} 秒，视频码率 {4:N0} kbps{5}）", done, plans.Count, c.Name, actual, kbps, low));
                        }
                        else
                        {
                            failedCount++;
                            Log(string.Format("  第 {0}/{1} 条{2}失败：{3}（{4}）", done, plans.Count, c.Encodes ? "处理" : "拼接", c.Name, problem));
                            lock (c.Task.ErrTail)
                            {
                                foreach (string e in c.Task.ErrTail) Log("    ffmpeg: " + e);
                            }
                        }
                        if (Status != null && encodes == 0) Status(string.Format("快拼出片 {0}/{1} · 已用 {2}", done, plans.Count, FormatSpan(sw.Elapsed)));
                    }
                    if (encodes > 0 && totalSec > 0)
                    {
                        double cur = doneSec;
                        foreach (var r in running) cur += Math.Min(Interlocked.Read(ref r.Task.OutUs) / 1000000.0, r.Plan.TotalDuration);
                        double frac = Math.Min(1.0, cur / totalSec);
                        Progress(Math.Min(99, (int)(frac * 100)));
                        if (Status != null && sw.ElapsedMilliseconds - lastStatus >= 1000)
                        {
                            lastStatus = sw.ElapsedMilliseconds;
                            Status(string.Format("快拼出片（{0}）{1}%（{2}/{3}）· 已用 {4} · 预计剩余 {5}", fx != null ? "含效果" : "提高码率", (int)(frac * 100), done, plans.Count, FormatSpan(sw.Elapsed),
                                frac > 0.02 ? FormatSpan(TimeSpan.FromSeconds(sw.Elapsed.TotalSeconds * (1 - frac) / frac)) : "计算中"));
                        }
                    }
                }
                if (_cancel)
                {
                    removeBatch = true;
                    return null;
                }
                sw.Stop();
                WriteReport(Path.Combine(batchDir, "生成报告.csv"), report, okPlans.Count > 0);
                // Only combinations that actually produced a video are remembered.
                if (okPlans.Count > 0 && AppendHistory(okPlans, sourceRoot, category, targetSec, tolerance))
                    Log(string.Format("已把 {0} 条组合写入历史记录，以后不会再出现相同组合。", okPlans.Count));
                if (failedCount > 0) Log(string.Format("注意：{0} 条成片生成失败，详见上方日志。", failedCount));
                Log(string.Format("全部完成：{0} 条互不重复成片，总耗时 {1:N1} 秒", okPlans.Count, sw.Elapsed.TotalSeconds));
                if (okPlans.Count == 0)
                {
                    removeBatch = true;
                    return null;
                }
                Progress(100);
                LastMade = okPlans.Count;
                return batchDir;
            }
            finally
            {
                foreach (var c in running)
                {
                    Finish(c.Task, true);
                    TryDelete(c.Part);
                }
                try { Directory.Delete(taskRoot, true); } catch { }
                if (removeBatch) RemoveBatch(produced, batchDir, productDir, productDirExisted);
            }
        }

        // Chooses each video's 成片效果 (random ones per video), copies the LUTs into the task folder
        // and writes the subtitle files. false = stopped.
        private bool PrepareEffects(List<Concat> items, EffectOptions fx, string taskRoot, double speed, Random rng)
        {
            List<EffectAsset> filters = EffectAssets.Filters(AppDir);
            List<EffectAsset> stickers = EffectAssets.Stickers(AppDir);
            bool anyFilter = fx.Filter == EffectOptions.Random;
            bool anySticker = fx.Sticker == EffectOptions.Random;
            EffectAsset filter = null;
            EffectAsset sticker = null;
            if (!string.IsNullOrEmpty(fx.Filter) && !anyFilter)
            {
                filter = EffectAssets.Find(filters, fx.Filter);
                if (filter == null) Log("找不到滤镜「" + fx.Filter + "」（effects\\滤镜 里没有这个文件），本批不加滤镜。");
            }
            if (!string.IsNullOrEmpty(fx.Sticker) && !anySticker)
            {
                sticker = EffectAssets.Find(stickers, fx.Sticker);
                if (sticker == null) Log("找不到贴纸「" + fx.Sticker + "」（effects\\贴纸 里没有这个文件），本批不加贴纸。");
            }
            if (anyFilter && filters.Count == 0) Log("effects\\滤镜 里没有滤镜文件，本批不加滤镜。");
            if (anySticker && stickers.Count == 0) Log("effects\\贴纸 里没有贴纸文件，本批不加贴纸。");
            double strength = EffectOptions.ValidStrength(fx.FilterStrength);
            // LUT file in the task folder and the strength it got, per filter (null = unusable).
            var luts = new Dictionary<string, KeyValuePair<string, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in items)
            {
                var p = new FxPick { Speed = speed };
                EffectAsset f = anyFilter ? (filters.Count > 0 ? filters[rng.Next(filters.Count)] : null) : filter;
                if (f != null)
                {
                    KeyValuePair<string, double> lut;
                    if (!luts.TryGetValue(f.Path, out lut))
                    {
                        string rel = "lut_" + (luts.Count + 1) + ".cube";
                        double got = CopyLut(f, Path.Combine(taskRoot, rel), strength);
                        lut = new KeyValuePair<string, double>(got > 0 ? rel : null, got);
                        luts[f.Path] = lut;
                    }
                    if (lut.Key != null)
                    {
                        p.FilterName = f.Name;
                        p.LutFile = lut.Key;
                        p.Strength = lut.Value;
                    }
                }
                EffectAsset s = anySticker ? (stickers.Count > 0 ? stickers[rng.Next(stickers.Count)] : null) : sticker;
                if (s != null)
                {
                    p.StickerName = s.Name;
                    p.StickerPath = s.Path;
                }
                c.Fx = p;
            }
            var parts = new List<string>();
            bool filtered = anyFilter ? filters.Count > 0 : filter != null;
            parts.Add("滤镜 " + (anyFilter ? "随机" : filter != null ? filter.Name : "无") + (filtered && strength < 0.995 ? "（强度 " + EffectOptions.PercentText(strength) + "）" : ""));
            parts.Add("贴纸 " + (anySticker ? "随机" : sticker != null ? sticker.Name : "无"));
            parts.Add("变速 " + EffectOptions.SpeedText(speed));
            parts.Add("字幕 " + (fx.Subtitles ? "识别语音" : "无"));
            Log("成片效果：" + string.Join("，", parts.ToArray()));
            if (fx.Subtitles && !WriteSubtitles(items, fx, taskRoot, speed)) return false;
            // A video that ends up with nothing to add (e.g. subtitles on, but no speech in its clips)
            // is joined by stream copy like before: same result, much faster.
            int plain = 0;
            foreach (var c in items)
            {
                FxPick p = c.Fx;
                if (p.LutFile != null || p.StickerPath != null || p.AssFile != null || Math.Abs(p.Speed - 1.0) > 0.001) continue;
                c.Fx = null;
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(c.Plan.IntroPath)) lines.Add(ConcatLine(c.Plan.IntroPath));
                foreach (var pc in c.Plan.Picked) lines.Add(ConcatLine(pc.Path));
                File.WriteAllLines(c.ListFile, lines, new UTF8Encoding(false));
                plain++;
            }
            if (plain > 0) Log(string.Format("{0} 条成片没有要加的效果，直接无损拼接。", plain));
            return !_cancel;
        }

        // A filter's LUT copied into the task folder, mixed down to 滤镜强度 when that is below 100 %.
        // Returns the strength it got (1 when the file can only be used as it is), -1 when unusable.
        private double CopyLut(EffectAsset f, string dst, double strength)
        {
            try
            {
                if (strength < 0.995)
                {
                    CubeLut lut = CubeLut.Get(f.Path);
                    if (lut != null && lut.WriteBlended(dst, strength, "Axe 滤镜：" + f.Name + "  强度 " + EffectOptions.PercentText(strength))) return strength;
                    Log("滤镜「" + f.Name + "」的文件格式不支持调整强度，按 100% 使用。");
                }
                File.Copy(f.Path, dst, true);
                return 1.0;
            }
            catch (Exception ex)
            {
                Log("滤镜「" + f.Name + "」无法读取（" + ex.Message + "），这条不加滤镜。");
                return -1;
            }
        }

        // Subtitles of every video from the speech of its clips, placed on the video's timeline (the
        // 片头 plays at normal speed, the clips after it at the chosen speed).
        private bool WriteSubtitles(List<Concat> items, EffectOptions fx, string taskRoot, double speed)
        {
            var clips = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in items)
            {
                if (!string.IsNullOrEmpty(c.Plan.IntroPath) && seen.Add(c.Plan.IntroPath)) clips.Add(c.Plan.IntroPath);
                foreach (var pc in c.Plan.Picked) if (seen.Add(pc.Path)) clips.Add(pc.Path);
            }
            Dictionary<string, Transcript> spoken = Transcribe(clips, taskRoot);
            if (_cancel) return false;
            int maxChars = SubtitleWriter.MaxChars(_width, _height, fx);
            var lines = new Dictionary<string, List<Cue>>(StringComparer.OrdinalIgnoreCase);
            Func<string, List<Cue>> of = path =>
            {
                List<Cue> l;
                if (!lines.TryGetValue(path, out l))
                {
                    Transcript t;
                    l = spoken.TryGetValue(path, out t) ? SubtitleWriter.Lines(t, maxChars) : new List<Cue>();
                    lines[path] = l;
                }
                return l;
            };
            int with = 0;
            foreach (var c in items)
            {
                var cues = new List<Cue>();
                double t0 = 0;
                if (!string.IsNullOrEmpty(c.Plan.IntroPath))
                {
                    foreach (var l in of(c.Plan.IntroPath))
                    {
                        if (l.Start < c.Plan.IntroDuration) cues.Add(new Cue { Start = l.Start, End = Math.Min(l.End, c.Plan.IntroDuration), Text = l.Text });
                    }
                    t0 = c.Plan.IntroDuration;
                }
                foreach (var pc in c.Plan.Picked)
                {
                    foreach (var l in of(pc.Path))
                    {
                        if (l.Start >= pc.Duration) continue;
                        cues.Add(new Cue { Start = t0 + l.Start / speed, End = t0 + Math.Min(l.End, pc.Duration) / speed, Text = l.Text });
                    }
                    t0 += pc.Duration / speed;
                }
                if (cues.Count == 0) continue;
                // Sped up (3x, 4x) a short line would flash by: it is joined with the next one when they
                // follow each other without a pause and still fit on one line.
                for (int i = 0; i + 1 < cues.Count;)
                {
                    Cue a = cues[i];
                    Cue b = cues[i + 1];
                    if (a.End - a.Start < 0.7 && b.Start - a.End < 0.25 && a.Text.Length + 1 + b.Text.Length <= maxChars)
                    {
                        a.Text = a.Text + " " + b.Text;
                        a.End = b.End;
                        cues.RemoveAt(i + 1);
                        continue;
                    }
                    i++;
                }
                // A line never stays on screen into the next one (they share one position).
                for (int i = 0; i + 1 < cues.Count; i++)
                {
                    if (cues[i].End > cues[i + 1].Start) cues[i].End = Math.Max(cues[i].Start + 0.05, cues[i + 1].Start);
                }
                string rel = string.Format("sub_{0:D4}.ass", c.Plan.Index);
                File.WriteAllText(Path.Combine(taskRoot, rel), SubtitleWriter.Ass(_width, _height, fx, cues), new UTF8Encoding(true));
                c.Fx.AssFile = rel;
                c.Fx.Cues = cues.Count;
                with++;
            }
            Log(with > 0 ? string.Format("字幕：{0}/{1} 条成片有字幕（没有人声的素材不出字幕）。", with, items.Count) : "字幕：这些素材里没有识别到人声，本批不加字幕。");
            return true;
        }

        // One encode per video: [片头] + the clips (sped up) → filter → sticker → subtitles.
        // -reinit_filter 0: clips made from different sources can carry different colour tags (e.g.
        // one "tv / bt470bg", the next "unknown"). By default ffmpeg rebuilds the whole filter graph
        // at such a change, which restarts every timestamp there and cut the video short at the
        // second clip. Unified clips always share size and pixel format, so the graph can stay.
        // v2.8.3: the decoders and the filter graph get this job's share of the CPU (threads), as the
        // encoder already did. Left alone ffmpeg gives each of them as many threads as the CPU has, in
        // every one of the jobs running at once: with 10 jobs on 16 threads that was about 950 threads
        // and 2.3 GB; capped 390 threads, 1.8 GB and the batch 6 % sooner (_tools\v30\bench2.ps1).
        private string BuildEffectArgs(Concat c, bool hw, int threads)
        {
            var sb = new StringBuilder("-hide_banner -nostdin -y -loglevel error -nostats -progress pipe:1 ");
            sb.Append("-filter_complex_threads ").Append(threads).Append(' ');
            string dec = "-threads " + threads.ToString(Inv) + " ";
            if (!string.IsNullOrEmpty(c.Plan.IntroPath)) sb.Append(dec).Append("-reinit_filter 0 -i \"").Append(c.Plan.IntroPath).Append("\" ");
            sb.Append(dec).Append("-reinit_filter 0 -f concat -safe 0 -i \"").Append(c.ListFile).Append("\" ");
            if (c.Fx.StickerPath != null) sb.Append(EffectAssets.InputOptions(c.Fx.StickerPath)).Append("-i \"").Append(c.Fx.StickerPath).Append("\" ");
            string audio;
            sb.Append("-filter_complex \"").Append(EffectGraph(c, hw, threads, out audio)).Append("\" ");
            sb.Append("-map \"[vout]\" -map ").Append(audio).Append(' ');
            sb.Append("-max_muxing_queue_size 4096 ").Append(BuildEncodeArgs(hw, threads, c.AbrKbps)).Append(" -movflags +faststart \"").Append(c.Part).Append('"');
            return sb.ToString();
        }

        // A plain output whose clips are below the bitrate floor: the clips joined and the picture
        // encoded once at a set average bitrate; the sound is copied, as stream copy would.
        // superfast: the clips are already unified and get about twice their bitrate here, so the
        // result matches them just as closely as with veryfast (SSIM 0.9873 vs 0.9877) in 36 % less
        // time. -reinit_filter 0 for the same reason as in BuildEffectArgs.
        private string BuildRecodeArgs(Concat c, bool hw, int threads)
        {
            string video = hw
                ? HwEncoder.VideoArgs(_hw, _crf, _gop, c.AbrKbps) + (_hw == HwKind.Qsv ? " -vf format=nv12" : "")
                : string.Format(Inv, "-c:v libx264 -preset superfast -b:v {0}k -profile:v high -level 4.1 -pix_fmt yuv420p -g {1} -keyint_min {1} -sc_threshold 0 -threads {2}", c.AbrKbps, _gop, threads);
            return string.Format(Inv, "-hide_banner -nostdin -y -loglevel error -nostats -progress pipe:1 -filter_threads {3} -threads {3} -reinit_filter 0 -f concat -safe 0 -i \"{0}\" -map 0:v:0 -map 0:a:0 " +
                "{1} -c:a copy -max_muxing_queue_size 4096 -movflags +faststart \"{2}\"", c.ListFile, video, c.Part, threads);
        }

        // An output must reach the platforms' minimum with room to spare: upload checks count a kbps
        // as 1000 or 1024 bit/s and measure the length a little differently.
        private double FloorKbps
        {
            get { return MinKbps * 1.1; }
        }

        // What x264 is asked for when an output has to reach the floor. One-pass average bitrate lands
        // within a few per cent, mostly a little under (1205-1353 kbps when asked for 1300).
        private int TargetKbps
        {
            get { return (int)Math.Round(MinKbps * 1.35); }
        }

        // The video bitrate of every output is known before it is made: joined by stream copy it is
        // exactly that of its clips together (from their headers). An output that would stay under the
        // platforms' minimum is encoded at a set average bitrate instead: a plain one from its clips
        // (sound copied), one with 成片效果 in the single encode it gets anyway. Outputs safely above
        // it are made as before (stream copy, or the usual quality setting).
        private void PlanBitrates(List<Concat> items)
        {
            if (MinKbps <= 0) return;
            var facts = new Dictionary<string, Unified.Facts>(StringComparer.OrdinalIgnoreCase);
            int recode = 0;
            int abr = 0;
            double lowest = double.MaxValue;
            foreach (var c in items)
            {
                c.JoinKbps = JoinedKbps(c.Plan, facts);
                // Unknown (a clip the header reader cannot measure): made as usual and checked after.
                if (c.JoinKbps <= 0) continue;
                if (c.Fx == null)
                {
                    if (c.JoinKbps >= FloorKbps) continue;
                    c.Recode = true;
                    c.AbrKbps = TargetKbps;
                    recode++;
                }
                else
                {
                    // Re-encoded at the usual quality an output comes out near its clips' bitrate, and
                    // higher when sped up (measured 1.27x at 2x, 1.55x at 4x), but with a filter it can
                    // also come out lower (0.85x at 1.5x on calm clips raised to the floor). Below 1.7x
                    // the minimum the usual setting could end up under it (and be made a second time),
                    // so the bitrate is set in the one encode instead.
                    double expect = c.JoinKbps * Math.Pow(c.Fx.Speed, 0.35);
                    if (expect >= MinKbps * 1.7) continue;
                    c.AbrKbps = Math.Max(TargetKbps, (int)Math.Round(expect * 1.1));
                    abr++;
                }
                lowest = Math.Min(lowest, c.JoinKbps);
            }
            if (recode > 0)
                Log(string.Format("码率：{0} 条成片的素材视频码率偏低（最低约 {1:N0} kbps，平台要求不低于 {2} kbps），这几条改为重新编码到约 {3} kbps，其余照常无损拼接。",
                    recode, lowest, MinKbps, TargetKbps));
            if (abr > 0)
                Log(string.Format("码率：{0} 条成片的素材视频码率偏低（平台要求不低于 {1} kbps），加效果编码时按约 {2} kbps 以上输出。", abr, MinKbps, TargetKbps));
        }

        // Video bitrate of the clips of one output (with its 片头) joined as they are, in kbps; 0 when a
        // clip's header does not tell.
        private static double JoinedKbps(Plan p, Dictionary<string, Unified.Facts> facts)
        {
            long bytes = 0;
            double sec = 0;
            var paths = new List<string>();
            if (!string.IsNullOrEmpty(p.IntroPath)) paths.Add(p.IntroPath);
            foreach (var c in p.Picked) paths.Add(c.Path);
            foreach (string path in paths)
            {
                Unified.Facts x;
                if (!facts.TryGetValue(path, out x))
                {
                    x = Unified.Read(new FileInfo(path));
                    facts[path] = x;
                }
                if (x.VideoBytes <= 0 || !(x.VideoSeconds > 0)) return 0;
                bytes += x.VideoBytes;
                sec += x.VideoSeconds;
            }
            return sec > 0 ? bytes * 8.0 / sec / 1000.0 : 0;
        }

        // 生成报告.csv of the batch (added to when the folder has one). A report that cannot be written
        // (the file open in Excel, a virus scanner) is retried and else only logged: the videos are
        // made, and the history written after it must not be skipped.
        private void WriteReport(string path, List<string> report, bool any)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var rest = new List<string>();
                        for (int i = 1; i < report.Count; i++) rest.Add(report[i]);
                        if (rest.Count > 0) File.AppendAllLines(path, rest, new UTF8Encoding(true));
                    }
                    else if (any)
                    {
                        File.WriteAllLines(path, report, new UTF8Encoding(true));
                    }
                    return;
                }
                catch (IOException) when (attempt < 6)
                {
                    Thread.Sleep(250 * attempt);
                }
                catch (Exception ex)
                {
                    Log("生成报告写入失败（成片不受影响）：" + ex.Message);
                    return;
                }
            }
        }

        // The filter as the report names it: with its strength when that is not 100 %.
        private static string FilterLabel(FxPick p)
        {
            if (p.FilterName == null) return "无";
            return p.Strength < 0.995 ? p.FilterName + " " + EffectOptions.PercentText(p.Strength) : p.FilterName;
        }

        private string EffectGraph(Concat c, bool hw, int threads, out string audioMap)
        {
            FxPick p = c.Fx;
            EffectOptions fx = Effects;
            bool intro = !string.IsNullOrEmpty(c.Plan.IntroPath);
            int body = intro ? 1 : 0;
            var g = new StringBuilder();
            string v = "[" + body + ":v]";
            string a = "[" + body + ":a]";
            bool audioFiltered = false;
            if (Math.Abs(p.Speed - 1.0) > 0.001)
            {
                string k = p.Speed.ToString("0.###", Inv);
                g.Append(v).Append("setpts=(PTS-STARTPTS)/").Append(k).Append(",fps=").Append(_fps).Append("[bv];");
                g.Append(a).Append("atempo=").Append(k).Append(",asetpts=PTS-STARTPTS[ba];");
                v = "[bv]";
                a = "[ba]";
                audioFiltered = true;
            }
            if (intro)
            {
                g.Append("[0:v][0:a]").Append(v).Append(a).Append("concat=n=2:v=1:a=1[cv][ca];");
                v = "[cv]";
                a = "[ca]";
                audioFiltered = true;
            }
            if (p.LutFile != null)
            {
                // ffmpeg has SIMD code (SSE/AVX2/AVX-512) for lut3d only on float RGB; left to itself it
                // picks 8-bit RGB and the plain C loop, which made a filter cost twice the encode. Same
                // look (the float path is even a little more precise), never slower on any x64 CPU:
                // 10 videos at once finish about 15 % sooner. The colour conversions get this job's
                // threads (swscale uses one by default) and end in yuv420p, so the sticker and the
                // subtitles work as before.
                // v2.8.3: the way back is always BT.709 and the frames say so. Before, the picture came
                // back as BT.709 while the video kept the tag of its first clip, so clips tagged BT.601
                // or untagged were shown with slightly wrong colours (BT.709 clips are unchanged).
                g.Append(v).Append("scale=threads=").Append(threads).Append(",format=gbrpf32,lut3d=file=").Append(p.LutFile)
                    .Append(":interp=tetrahedral,scale=threads=").Append(threads).Append(":out_color_matrix=bt709:out_range=tv,format=yuv420p,")
                    .Append("setparams=range=tv:color_primaries=bt709:color_trc=bt709:colorspace=bt709[lv];");
                v = "[lv]";
            }
            if (p.StickerPath != null)
            {
                // Relative to the shorter side, so a sticker looks the same size in every 成片比例.
                int w = Math.Max(16, (int)Math.Round(Math.Min(_width, _height) * SubtitleWriter.Clamp(fx.StickerSize, 0.05, 0.9)));
                string x = SubtitleWriter.Clamp(fx.StickerX, 0, 1).ToString("0.####", Inv);
                string y = SubtitleWriter.Clamp(fx.StickerY, 0, 1).ToString("0.####", Inv);
                g.Append('[').Append(body + 1).Append(":v]scale=").Append(w).Append(":-1,format=rgba[st];");
                g.Append(v).Append("[st]overlay=x='clip(W*").Append(x).Append("-w/2,0,W-w)':y='clip(H*").Append(y).Append("-h/2,0,H-h)':shortest=1[ov];");
                v = "[ov]";
            }
            g.Append(v);
            if (p.AssFile != null) g.Append("subtitles=").Append(p.AssFile).Append(',');
            g.Append("format=").Append(hw && _hw == HwKind.Qsv ? "nv12" : "yuv420p").Append("[vout]");
            audioMap = audioFiltered ? "\"" + a + "\"" : body + ":a:0";
            return g.ToString();
        }
    }
}
