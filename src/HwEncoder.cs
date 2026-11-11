using System;
using System.Diagnostics;
using System.Globalization;

namespace VideoMixApp
{
    public enum HwKind { None, Nvenc, Qsv, Amf }

    // Detects a usable GPU H.264 encoder by running a tiny test encode with the exact options used
    // for real jobs. The result is cached for the lifetime of the process.
    internal static class HwEncoder
    {
        private static readonly object Gate = new object();
        private static string _cachedFor;
        private static HwKind _cached;

        public static HwKind Detect(string ffmpeg)
        {
            lock (Gate)
            {
                if (_cachedFor != null && string.Equals(_cachedFor, ffmpeg, StringComparison.OrdinalIgnoreCase)) return _cached;
                HwKind found = HwKind.None;
                foreach (HwKind k in new[] { HwKind.Nvenc, HwKind.Qsv, HwKind.Amf })
                {
                    if (TestEncode(ffmpeg, k, VideoArgs(k, 23, 60))) { found = k; break; }
                }
                _cachedFor = ffmpeg;
                _cached = found;
                return found;
            }
        }

        public static string DisplayName(HwKind k)
        {
            switch (k)
            {
                case HwKind.Nvenc: return "NVIDIA 显卡（h264_nvenc）";
                case HwKind.Qsv: return "Intel 核显（h264_qsv）";
                case HwKind.Amf: return "AMD 显卡（h264_amf）";
                default: return "CPU（libx264）";
            }
        }

        public static string ShortName(HwKind k)
        {
            switch (k)
            {
                case HwKind.Nvenc: return "NVENC";
                case HwKind.Qsv: return "QSV";
                case HwKind.Amf: return "AMF";
                default: return "CPU";
            }
        }

        // Concurrent GPU sessions. Consumer NVIDIA drivers have historically allowed only 3
        // simultaneous NVENC sessions; extra jobs are given to the CPU instead of failing.
        public static int MaxSessions(HwKind k)
        {
            switch (k)
            {
                case HwKind.Nvenc: return 3;
                case HwKind.Qsv: return 3;
                case HwKind.Amf: return 2;
                default: return 0;
            }
        }

        public static string FilterSuffix(HwKind k)
        {
            return k == HwKind.Qsv ? ",format=nv12" : "";
        }

        // abrKbps > 0: an average bitrate instead of a quality level (outputs that must reach the
        // platforms' minimum; Mix checks every result and makes a short one again with x264).
        public static string VideoArgs(HwKind k, int crf, int gop, int abrKbps = 0)
        {
            var ci = CultureInfo.InvariantCulture;
            if (abrKbps > 0)
            {
                switch (k)
                {
                    case HwKind.Nvenc:
                        return string.Format(ci, "-c:v h264_nvenc -preset p4 -tune hq -rc vbr -b:v {0}k -maxrate {1}k -bufsize {1}k -profile:v high -level 4.1 -pix_fmt yuv420p -g {2} -keyint_min {2} -sc_threshold 0", abrKbps, abrKbps * 2, gop);
                    case HwKind.Qsv:
                        // Measured on Iris Xe: 1344 kbps for 1350 asked, on footage that gets 575 at CRF 23.
                        return string.Format(ci, "-c:v h264_qsv -preset veryfast -b:v {0}k -maxrate {1}k -bufsize {1}k -look_ahead 0 -profile:v high -g {2} -bf 2", abrKbps, abrKbps * 2, gop);
                    case HwKind.Amf:
                        return string.Format(ci, "-c:v h264_amf -quality balanced -rc vbr_peak -b:v {0}k -maxrate {1}k -bufsize {1}k -profile:v high -pix_fmt yuv420p -g {2}", abrKbps, abrKbps * 2, gop);
                    default:
                        return null;
                }
            }
            switch (k)
            {
                case HwKind.Nvenc:
                    return string.Format(ci, "-c:v h264_nvenc -preset p4 -tune hq -rc vbr -cq {0} -b:v 0 -profile:v high -level 4.1 -pix_fmt yuv420p -g {1} -keyint_min {1} -sc_threshold 0", crf, gop);
                case HwKind.Qsv:
                    // ICQ needs a slightly higher value than x264 CRF for a similar file size.
                    return string.Format(ci, "-c:v h264_qsv -preset veryfast -global_quality {0} -look_ahead 0 -profile:v high -g {1} -bf 2", crf + 2, gop);
                case HwKind.Amf:
                    return string.Format(ci, "-c:v h264_amf -quality balanced -rc cqp -qp_i {0} -qp_p {1} -qp_b {2} -profile:v high -pix_fmt yuv420p -g {3}", crf, crf + 2, crf + 4, gop);
                default:
                    return null;
            }
        }

        private static string _abrCheckedFor;
        private static bool _abrWorks;

        // Whether this GPU also takes the average-bitrate settings (a tiny test encode, once per run).
        public static bool AverageBitrateWorks(string ffmpeg, HwKind k)
        {
            if (k == HwKind.None) return false;
            lock (Gate)
            {
                string key = ffmpeg + "|" + k;
                if (_abrCheckedFor == key) return _abrWorks;
                _abrWorks = TestEncode(ffmpeg, k, VideoArgs(k, 23, 60, 1350));
                _abrCheckedFor = key;
                return _abrWorks;
            }
        }

        private static bool TestEncode(string ffmpeg, HwKind k, string videoArgs)
        {
            string args = "-hide_banner -loglevel error -nostdin -f lavfi -i color=black:s=720x1280:r=30 -frames:v 5 -vf \"setsar=1"
                + FilterSuffix(k) + "\" " + videoArgs + " -f null -";
            try
            {
                var psi = new ProcessStartInfo(ffmpeg, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                using (var p = Process.Start(psi))
                {
                    ChildProcesses.Add(p);
                    p.ErrorDataReceived += (s, e) => { };
                    p.OutputDataReceived += (s, e) => { };
                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    p.WaitForExit();
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
