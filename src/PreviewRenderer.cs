using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace VideoMixApp
{
    // Pictures for the 成片效果 tab, made with the bundled ffmpeg: a frame of the chosen footage (scaled
    // and padded to the 成片比例 the way 规格统一 does) and still or animated frames of the stickers
    // (the filters are applied in Axe itself, see CubeLut). The pictures are kept in _preview in the
    // Axe folder, so each is made once per file version.
    internal sealed class PreviewRenderer
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private readonly string _ff;
        private readonly string _dir;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        public PreviewRenderer(string appDir)
        {
            _ff = Path.Combine(appDir, "ffmpeg", "bin", "ffmpeg.exe");
            _dir = Path.Combine(appDir, "_preview");
        }

        public bool Available
        {
            get { return File.Exists(_ff); }
        }

        // The video the preview shows: the first of the folder in Explorer's name order.
        public static FileInfo SampleVideo(string root, string category)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
                List<FileInfo> files = MixEngine.ScanSources(root, category);
                if (files.Count == 0) return null;
                files.Sort((a, b) => StrCmpLogicalW(a.FullName, b.FullName));
                return files[0];
            }
            catch
            {
                return null;
            }
        }

        // PNG of a frame a third into the video (at most 3 s in), scaled and padded to w x h. Kept per
        // video version and size, so opening Axe again shows the preview at once.
        public byte[] Frame(FileInfo video, int w, int h)
        {
            string file = CachePath("frame", video.FullName, w * 100000 + h) + ".png";
            try
            {
                if (File.Exists(file)) return File.ReadAllBytes(file);
            }
            catch
            {
            }
            double at = 1.0;
            Unified.Facts x = Unified.Read(video);
            if (x.Duration > 0) at = Math.Min(3.0, x.Duration * 0.3);
            string vf = string.Format(Inv, "scale={0}:{1}:force_original_aspect_ratio=decrease,pad={0}:{1}:(ow-iw)/2:(oh-ih)/2,setsar=1", w, h);
            byte[] png = Pipe(string.Format(Inv, "-ss {0:0.###} -i \"{1}\" -frames:v 1 -vf \"{2}\"", at, video.FullName, vf));
            // Very short or damaged files: the first frame instead.
            if (png == null) png = Pipe(string.Format(Inv, "-i \"{0}\" -frames:v 1 -vf \"{1}\"", video.FullName, vf));
            if (png != null)
            {
                Store(file, png);
                Prune(Path.GetDirectoryName(file), 40);
            }
            return png;
        }

        // Keeps the newest files of a cache folder (one frame per product folder and ratio is plenty).
        private static void Prune(string dir, int keep)
        {
            try
            {
                var files = new List<FileInfo>(new DirectoryInfo(dir).GetFiles("*.png"));
                if (files.Count <= keep) return;
                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = keep; i < files.Count; i++) TryDelete(files[i].FullName);
            }
            catch
            {
            }
        }

        // No footage chosen yet (or none readable): a colour gradient, which still shows what each
        // filter does to sky, skin, plants and warm tones.
        public byte[] SampleFrame(int w, int h)
        {
            // End points must lie inside the frame (on the edge ffmpeg picks random ones).
            return Pipe(string.Format(Inv, "-f lavfi -i \"gradients=s={0}x{1}:c0=0x5b8fd6:c1=0xf2b36b:c2=0x7fbf73:c3=0xe57373:c4=0xf5e6c8:nb_colors=5:type=linear:x0=0:y0=0:x1={2}:y1={3}:speed=0:seed=1\" -frames:v 1",
                w, h, w - 1, h - 1));
        }

        // A still picture of the sticker (a frame a little into its animation), at most size x size.
        public byte[] StickerPicture(EffectAsset s, int size)
        {
            string file = CachePath("still", s.Path, size) + ".png";
            try
            {
                if (File.Exists(file)) return File.ReadAllBytes(file);
            }
            catch
            {
            }
            string vf = string.Format(Inv, "scale={0}:{0}:force_original_aspect_ratio=decrease", size);
            byte[] png = Pipe(EffectAssets.InputOptions(s.Path) + "-i \"" + s.Path + "\" -vf \"select=gte(n\\,8)," + vf + "\" -frames:v 1");
            if (png == null) png = Pipe("-i \"" + s.Path + "\" -vf \"" + vf + "\" -frames:v 1");
            if (png != null) Store(file, png);
            return png;
        }

        // The sticker's animation as frames at 12 per second (2 s); one frame for a still picture.
        public List<byte[]> StickerAnimation(EffectAsset s, int size)
        {
            var frames = new List<byte[]>();
            string dir = CachePath("anim", s.Path, size);
            try
            {
                if (Directory.Exists(dir))
                {
                    var files = new List<string>(Directory.GetFiles(dir, "f*.png"));
                    files.Sort(StringComparer.Ordinal);
                    foreach (string f in files) frames.Add(File.ReadAllBytes(f));
                    if (frames.Count > 0) return frames;
                }
                string opts = EffectAssets.InputOptions(s.Path);
                bool still = opts.StartsWith("-loop", StringComparison.Ordinal);
                string tmp = dir + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                Directory.CreateDirectory(tmp);
                string vf = string.Format(Inv, "fps=12,scale={0}:{0}:force_original_aspect_ratio=decrease", size);
                if (Files(opts + "-i \"" + s.Path + "\" -vf \"" + vf + "\" -frames:v " + (still ? 1 : 24) + " f%02d.png", tmp))
                {
                    try
                    {
                        if (!Directory.Exists(dir)) Directory.Move(tmp, dir);
                    }
                    catch
                    {
                    }
                }
                string from = Directory.Exists(dir) ? dir : tmp;
                var list = new List<string>(Directory.GetFiles(from, "f*.png"));
                list.Sort(StringComparer.Ordinal);
                foreach (string f in list) frames.Add(File.ReadAllBytes(f));
                if (Directory.Exists(tmp)) try { Directory.Delete(tmp, true); } catch { }
            }
            catch
            {
            }
            return frames;
        }

        // _preview\<kind>\<hash of the file version and size>: a changed sticker gets new pictures.
        private string CachePath(string kind, string path, int size)
        {
            string key = path;
            try
            {
                var fi = new FileInfo(path);
                key += "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + size;
            }
            catch
            {
            }
            using (var sha = SHA1.Create())
            {
                string hex = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key.ToLowerInvariant()))).Replace("-", "").Substring(0, 16);
                return Path.Combine(_dir, kind, hex);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void Store(string file, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string tmp = file + "." + Guid.NewGuid().ToString("N").Substring(0, 6);
                File.WriteAllBytes(tmp, data);
                if (File.Exists(file)) File.Delete(tmp);
                else File.Move(tmp, file);
            }
            catch
            {
            }
        }

        // One PNG on stdout.
        private byte[] Pipe(string args)
        {
            byte[] outBytes;
            return Run(args + " -f image2pipe -c:v png -", null, out outBytes) && outBytes.Length > 8 ? outBytes : null;
        }

        // Output files written into workDir.
        private bool Files(string args, string workDir)
        {
            byte[] unused;
            return Run(args, workDir, out unused);
        }

        private bool Run(string args, string workDir, out byte[] stdout)
        {
            stdout = new byte[0];
            if (!Available) return false;
            try
            {
                var psi = new ProcessStartInfo(_ff, "-hide_banner -loglevel error -nostdin -y " + args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                if (workDir != null) psi.WorkingDirectory = workDir;
                using (var p = Process.Start(psi))
                {
                    ChildProcesses.Add(p);
                    ChildProcesses.Background(p);
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginErrorReadLine();
                    var ms = new MemoryStream();
                    Task copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    copy.Wait(5000);
                    stdout = ms.ToArray();
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
