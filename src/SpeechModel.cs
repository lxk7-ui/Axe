using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace VideoMixApp
{
    // Downloads the SenseVoice speech model (asr\model\model.int8.onnx, see MixEngine.AsrModel*) for
    // 识别语音配字幕. Same approach as 素材筛选器's model download: a single connection to these
    // servers can be slow, so the file comes in up to 8 ranges at once, each its own .part file
    // (stopping and starting again continues where it was). Every server is first tried both directly
    // and through the Windows proxy; the combination that answers first is used, and a range that
    // keeps failing moves on to the next one. The file is installed only after its SHA-256 matches.
    internal sealed class SpeechModelDownloader
    {
        private const int ProbeBytes = 256 * 1024;
        private const long PartBytes = 32L << 20;
        private const int MaxParts = 8;

        // hf-mirror is in China and needs no proxy; huggingface.co is the fallback.
        private static readonly string[] Sources =
        {
            "https://hf-mirror.com/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main/model.int8.onnx",
            "https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17/resolve/main/model.int8.onnx"
        };

        private sealed class Route
        {
            public string Url;
            public IWebProxy Proxy;   // null = direct
        }

        private sealed class Part
        {
            public string File;
            public long Start;
            public long Length;
            public long Have;
        }

        private sealed class Conn : IDisposable
        {
            private readonly SpeechModelDownloader _owner;
            public readonly HttpWebRequest Req;
            public readonly HttpWebResponse Resp;

            public Conn(SpeechModelDownloader owner, HttpWebRequest req, HttpWebResponse resp)
            {
                _owner = owner;
                Req = req;
                Resp = resp;
            }

            public void Dispose()
            {
                try { Resp.Close(); } catch { }
                _owner.Untrack(Req);
            }
        }

        private readonly string _target;
        private readonly string _work;
        private readonly Action<string> _log;
        private readonly List<HttpWebRequest> _live = new List<HttpWebRequest>();
        private readonly ManualResetEvent _stopped = new ManualResetEvent(false);
        private volatile bool _cancel;
        private volatile bool _probesOver;
        private volatile string _fatal;
        private long _done;
        private List<Route> _routes;
        private readonly List<Part> _parts = new List<Part>();

        public readonly long Total = MixEngine.AsrModelSize;
        public volatile string Phase = "连接";   // 连接 / 下载 / 校验
        public string Error { get; private set; }
        public bool Succeeded { get; private set; }
        public event Action Finished;              // raised on a worker thread

        static SpeechModelDownloader()
        {
            // An exe built without a target-framework attribute would offer TLS 1.0 only and open just
            // 2 connections per server.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 64);
            ServicePointManager.Expect100Continue = false;
        }

        public SpeechModelDownloader(string appDir, Action<string> log)
        {
            string dir = MixEngine.AsrModelDir(appDir);
            _target = Path.Combine(dir, MixEngine.AsrModelName);
            _work = Path.Combine(dir, "download");
            _log = log;
        }

        public long Done
        {
            get { return Interlocked.Read(ref _done); }
        }

        public void Start()
        {
            new Thread(Run) { IsBackground = true, Name = "语音模型下载" }.Start();
        }

        public void Cancel()
        {
            _cancel = true;
            _stopped.Set();
            AbortAll();
        }

        private void Run()
        {
            try
            {
                Directory.CreateDirectory(_work);
                Plan();
                Interlocked.Exchange(ref _done, _parts.Sum(p => Math.Min(p.Have, p.Length)));
                CheckDisk();
                _routes = PickRoutes();
                if (_cancel) return;
                Route best = _routes[0];
                Log("下载语音识别模型：用 " + new Uri(best.Url).Host + (best.Proxy == null ? "（直连）" : "（系统代理）"));
                Phase = "下载";
                var threads = new List<Thread>();
                foreach (Part part in _parts)
                {
                    if (part.Have >= part.Length) continue;
                    Part p = part;
                    var t = new Thread(() => RunPart(p)) { IsBackground = true };
                    threads.Add(t);
                    t.Start();
                }
                foreach (Thread t in threads) t.Join();
                if (_cancel) return;
                if (_fatal != null) throw new IOException(_fatal);
                Phase = "校验";
                Merge();
                Succeeded = true;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (!_cancel) Error = "没有权限往软件文件夹里写文件（" + ex.Message + "）。把软件文件夹放到桌面或 D 盘再试。";
            }
            catch (Exception ex)
            {
                if (!_cancel) Error = ex.Message;
            }
            finally
            {
                Log("下载语音识别模型：" + (Succeeded ? "完成" : _cancel ? "已停止（下次接着下载）" : "失败 " + Error));
                Action h = Finished;
                try
                {
                    if (h != null) h();
                }
                catch
                {
                }
            }
        }

        // The ranges still missing; parts of an earlier, unfinished run are kept and continued.
        private void Plan()
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = (int)Math.Max(1L, Math.Min(MaxParts, (Total + PartBytes - 1) / PartBytes));
            long step = (Total + n - 1) / n;
            for (int i = 0; i < n; i++)
            {
                long start = i * step;
                var p = new Part { File = Path.Combine(_work, MixEngine.AsrModelName + "." + i + "-" + n + ".part"), Start = start, Length = Math.Min(step, Total - start) };
                var fi = new FileInfo(p.File);
                p.Have = fi.Exists ? fi.Length : 0;
                _parts.Add(p);
                keep.Add(p.File);
            }
            // Longer than its range: stopped half way through joining. Start over.
            if (_parts.Any(p => p.Have > p.Length))
            {
                foreach (Part p in _parts)
                {
                    TryDelete(p.File);
                    p.Have = 0;
                }
            }
            foreach (string stale in Directory.GetFiles(_work))
            {
                if (!keep.Contains(stale)) TryDelete(stale);
            }
        }

        private void CheckDisk()
        {
            long need = Total - Done + _parts[0].Length + (20L << 20);
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_work)));
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    throw new IOException(string.Format("磁盘空间不够：还要 {0}MB，{1} 盘只剩 {2}MB。", need >> 20, drive.Name.TrimEnd('\\'), drive.AvailableFreeSpace >> 20));
                }
            }
            catch (ArgumentException)
            {
                // A network folder has no drive letter to look at; just try.
            }
        }

        // Fetches the first 256 KB over every server, directly and through the Windows proxy, all at
        // once. Routes that answered come first, fastest first; the rest are kept as fallbacks.
        private List<Route> PickRoutes()
        {
            Phase = "连接";
            IWebProxy sys = null;
            try
            {
                sys = WebRequest.GetSystemWebProxy();
                sys.Credentials = CredentialCache.DefaultCredentials;
            }
            catch
            {
            }
            var all = new List<Route>();
            foreach (string src in Sources)
            {
                all.Add(new Route { Url = src });
                if (sys != null) all.Add(new Route { Url = src, Proxy = sys });
            }
            var answered = new List<Route>();
            var noProxy = new List<Route>();
            int pending = all.Count;
            foreach (Route r in all)
            {
                Route route = r;
                new Thread(() =>
                {
                    try
                    {
                        // The first question to the Windows proxy can take seconds (auto-detection),
                        // so it is asked here, where it does not hold up the direct routes.
                        if (route.Proxy != null && route.Proxy.IsBypassed(new Uri(route.Url)))
                        {
                            lock (answered) noProxy.Add(route);
                            return;
                        }
                        if (Probe(route)) lock (answered) answered.Add(route);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Interlocked.Decrement(ref pending);
                    }
                }) { IsBackground = true }.Start();
            }
            var sw = Stopwatch.StartNew();
            long firstAt = -1;
            while (!_cancel && Volatile.Read(ref pending) > 0 && sw.ElapsedMilliseconds < 30000)
            {
                lock (answered)
                {
                    if (answered.Count > 0 && firstAt < 0) firstAt = sw.ElapsedMilliseconds;
                }
                if (firstAt >= 0 && sw.ElapsedMilliseconds - firstAt > 1000) break;
                Thread.Sleep(50);
            }
            List<Route> order;
            int working;
            lock (answered)
            {
                working = answered.Count;
                order = new List<Route>(answered);
                order.AddRange(all.Where(r => !answered.Contains(r) && !noProxy.Contains(r)));
            }
            _probesOver = true;
            AbortAll();
            if (_cancel) return order;
            if (working == 0) throw new IOException("连不上下载服务器。检查一下网络，过一会儿再点下载。");
            return order;
        }

        private bool Probe(Route r)
        {
            using (Conn c = Open(r.Url, r.Proxy, 0, ProbeBytes - 1))
            {
                CheckRange(c.Resp, 0);
                var buf = new byte[64 * 1024];
                long got = 0;
                using (Stream s = c.Resp.GetResponseStream())
                {
                    while (got < ProbeBytes && !_cancel && !_probesOver)
                    {
                        int n = s.Read(buf, 0, (int)Math.Min(buf.Length, ProbeBytes - got));
                        if (n <= 0) break;
                        got += n;
                    }
                }
                return got >= ProbeBytes;
            }
        }

        // Keeps one range going until it is complete. After two failed tries in a row it moves to the
        // next route; when every route failed several times over, the whole download gives up.
        private void RunPart(Part p)
        {
            try
            {
                int route = 0;
                int fails = 0;
                string last = "";
                while (!_cancel && _fatal == null && p.Have < p.Length)
                {
                    long before = p.Have;
                    try
                    {
                        Fetch(_routes[route], p);
                    }
                    catch (Exception ex)
                    {
                        if (_cancel) return;
                        last = ex.Message;
                    }
                    if (p.Have >= p.Length) return;
                    if (p.Have > before)
                    {
                        fails = 0;
                        continue;
                    }
                    fails++;
                    if (fails % 2 == 0) route = (route + 1) % _routes.Count;
                    if (fails >= 3 * _routes.Count + 2)
                    {
                        _fatal = "网络不好，一直下载不下来（" + last + "）。过一会儿再点「下载」，已经下好的部分不用重下。";
                        return;
                    }
                    _stopped.WaitOne(Math.Min(10000, 1000 * fails));
                }
            }
            catch (Exception ex)
            {
                if (!_cancel && _fatal == null) _fatal = ex.Message;
            }
        }

        private void Fetch(Route r, Part p)
        {
            long from = p.Start + p.Have;
            long to = p.Start + p.Length - 1;
            using (Conn c = Open(r.Url, r.Proxy, from, to))
            {
                CheckRange(c.Resp, from);
                using (Stream s = c.Resp.GetResponseStream())
                using (var fs = new FileStream(p.File, FileMode.Append, FileAccess.Write, FileShare.Read, 1))
                {
                    var buf = new byte[64 * 1024];
                    long need = to - from + 1;
                    while (need > 0 && !_cancel)
                    {
                        int n = s.Read(buf, 0, (int)Math.Min(buf.Length, need));
                        if (n <= 0) break;
                        fs.Write(buf, 0, n);
                        need -= n;
                        p.Have += n;
                        Interlocked.Add(ref _done, n);
                    }
                }
            }
        }

        // GET with a byte range. Redirects are followed here rather than by HttpWebRequest, which does
        // not know 308 (hf-mirror answers with it) and must keep the Range header on every hop.
        private Conn Open(string url, IWebProxy proxy, long from, long to)
        {
            var uri = new Uri(url);
            for (int hop = 0; hop < 10; hop++)
            {
                if (_cancel) throw new IOException("已停止");
                var req = (HttpWebRequest)WebRequest.Create(uri);
                req.Proxy = proxy;
                req.AllowAutoRedirect = false;
                req.Timeout = 20000;
                req.ReadWriteTimeout = 30000;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Axe/" + AppInfo.Version;
                req.AddRange(from, to);
                lock (_live) _live.Add(req);
                HttpWebResponse resp;
                try
                {
                    resp = (HttpWebResponse)req.GetResponse();
                }
                catch (WebException ex)
                {
                    resp = ex.Response as HttpWebResponse;
                    if (resp == null)
                    {
                        Untrack(req);
                        throw;
                    }
                }
                int code = (int)resp.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string loc = resp.Headers[HttpResponseHeader.Location];
                    resp.Close();
                    Untrack(req);
                    if (string.IsNullOrEmpty(loc)) throw new IOException("服务器跳转地址是空的");
                    uri = new Uri(uri, loc);
                    continue;
                }
                if (code >= 400)
                {
                    resp.Close();
                    Untrack(req);
                    throw new IOException("服务器返回 " + code + "（" + uri.Host + "）");
                }
                return new Conn(this, req, resp);
            }
            throw new IOException("跳转次数太多");
        }

        private static void CheckRange(HttpWebResponse resp, long from)
        {
            if (resp.StatusCode == HttpStatusCode.PartialContent)
            {
                string cr = resp.Headers["Content-Range"] ?? "";   // bytes 100-199/1000
                int sp = cr.IndexOf(' ');
                int dash = cr.IndexOf('-');
                long start;
                if (sp >= 0 && dash > sp && long.TryParse(cr.Substring(sp + 1, dash - sp - 1), out start) && start == from) return;
                throw new IOException("服务器给的分段不对（" + cr + "）");
            }
            // The whole file instead of a range is fine only when the range starts at 0.
            if (resp.StatusCode == HttpStatusCode.OK && from == 0) return;
            throw new IOException("服务器不支持分段下载（" + (int)resp.StatusCode + "）");
        }

        // Joins the parts (appending each to the first and deleting it, so little extra disk space is
        // needed) while hashing them; the model is installed only if the SHA-256 matches.
        private void Merge()
        {
            string first = _parts[0].File;
            var buf = new byte[1 << 20];
            using (var sha = new SHA256CryptoServiceProvider())
            {
                using (var fs = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                {
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0) sha.TransformBlock(buf, 0, n, null, 0);
                }
                if (_parts.Count > 1)
                {
                    using (var outFs = new FileStream(first, FileMode.Append, FileAccess.Write, FileShare.None, 1 << 16))
                    {
                        for (int i = 1; i < _parts.Count; i++)
                        {
                            using (var inFs = new FileStream(_parts[i].File, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                            {
                                int n;
                                while ((n = inFs.Read(buf, 0, buf.Length)) > 0)
                                {
                                    sha.TransformBlock(buf, 0, n, null, 0);
                                    outFs.Write(buf, 0, n);
                                }
                            }
                            File.Delete(_parts[i].File);
                        }
                    }
                }
                sha.TransformFinalBlock(buf, 0, 0);
                string hex = BitConverter.ToString(sha.Hash).Replace("-", "");
                if (!string.Equals(hex, MixEngine.AsrModelSha256, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(first);
                    Interlocked.Exchange(ref _done, 0);
                    throw new IOException("下载下来的内容不对（校验没通过），已经删掉了。再点一次「下载」重下。");
                }
            }
            if (File.Exists(_target)) File.Delete(_target);
            File.Move(first, _target);
            try { Directory.Delete(_work, true); } catch { }
        }

        // Runs on the download thread: a failing log must never end the program.
        private void Log(string s)
        {
            try
            {
                if (_log != null) _log(s);
            }
            catch
            {
            }
        }

        private void Untrack(HttpWebRequest req)
        {
            lock (_live) _live.Remove(req);
        }

        private void AbortAll()
        {
            lock (_live)
            {
                foreach (HttpWebRequest r in _live)
                {
                    try { r.Abort(); } catch { }
                }
                _live.Clear();
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
