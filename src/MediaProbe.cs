using System;
using System.IO;
using System.Text;

namespace VideoMixApp
{
    // Minimal in-process MP4/MOV reader. Reads only box headers plus the moov box, so getting a
    // duration costs a couple of small reads instead of starting ffprobe.exe (~60 ms each).
    // Returns null when the file is not a parsable MP4; callers then fall back to ffprobe.
    internal static class Mp4Probe
    {
        public sealed class Info
        {
            // 0 when the header carries no duration (fragmented MP4 without mehd); the track
            // flags are still reliable then, and callers ask ffprobe for the duration.
            public double Duration;
            public bool HasVideo;
            public bool HasAudio;
            // The "comment" metadata (moov/udta/meta/ilst/©cmt); 规格统一 writes its mark there.
            public string Comment;
            // The "description" metadata (ilst/desc): since v2.7.0 a clip whose bitrate 规格统一 raised
            // carries "AxeRate/1 <kbps>" there, the floor it was raised for.
            public string Description;
            // First video track: bytes of all its samples, its length and frame count (0 when the
            // header holds no sample table, e.g. fragmented MP4). Bitrate and frame rate come from these.
            public long VideoBytes;
            public double VideoSeconds;
            public long VideoFrames;
            // Picture size from the track header (0 when unknown).
            public int VideoWidth;
            public int VideoHeight;

            public double VideoKbps
            {
                get { return VideoBytes > 0 && VideoSeconds > 0 ? VideoBytes * 8.0 / VideoSeconds / 1000.0 : 0; }
            }

            public double VideoFps
            {
                get { return VideoFrames > 0 && VideoSeconds > 0 ? VideoFrames / VideoSeconds : 0; }
            }
        }

        private const long MaxMoovBytes = 256L * 1024 * 1024;
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        public static Info TryRead(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096))
                {
                    long len = fs.Length;
                    long pos = 0;
                    var head = new byte[16];
                    while (pos + 8 <= len)
                    {
                        fs.Position = pos;
                        if (!ReadFully(fs, head, 8)) return null;
                        long size = ReadU32(head, 0);
                        string type = Encoding.ASCII.GetString(head, 4, 4);
                        int hdr = 8;
                        if (size == 1)
                        {
                            if (!ReadFully(fs, head, 8)) return null;
                            size = (long)ReadU64(head, 0);
                            hdr = 16;
                        }
                        else if (size == 0)
                        {
                            size = len - pos;
                        }
                        if (size < hdr || pos + size > len + 8) return null;
                        if (type == "moov")
                        {
                            long body = Math.Min(size, len - pos) - hdr;
                            if (body <= 0 || body > MaxMoovBytes) return null;
                            var buf = new byte[body];
                            fs.Position = pos + hdr;
                            if (!ReadFully(fs, buf, (int)body)) return null;
                            return ParseMoov(buf, 0, buf.Length);
                        }
                        pos += size;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static Info ParseMoov(byte[] b, int start, int end)
        {
            var info = new Info();
            double mvhd = 0;
            long movieScale = 0;
            ulong fragmentDuration = 0;
            double maxTrack = 0;
            foreach (var box in Children(b, start, end))
            {
                if (box.Type == "mvhd")
                {
                    mvhd = ReadMvhdLike(b, box.Body, box.End, out movieScale);
                }
                else if (box.Type == "mvex")
                {
                    // Fragmented MP4 (web downloads): mvhd duration is 0, mehd has the total.
                    foreach (var x in Children(b, box.Body, box.End))
                    {
                        if (x.Type != "mehd" || x.Body + 8 > x.End) continue;
                        fragmentDuration = b[x.Body] == 1 && x.Body + 12 <= x.End ? ReadU64(b, x.Body + 4) : (ulong)ReadU32(b, x.Body + 4);
                    }
                }
                else if (box.Type == "trak")
                {
                    string handler = null;
                    double trackDur = 0;
                    long bytes = 0;
                    long frames = 0;
                    int width = 0;
                    int height = 0;
                    foreach (var t in Children(b, box.Body, box.End))
                    {
                        // tkhd ends with width and height as 16.16 fixed point (in both versions).
                        if (t.Type == "tkhd" && t.End - t.Body >= 84)
                        {
                            width = (int)(ReadU32(b, t.End - 8) >> 16);
                            height = (int)(ReadU32(b, t.End - 4) >> 16);
                        }
                        if (t.Type != "mdia") continue;
                        foreach (var m in Children(b, t.Body, t.End))
                        {
                            long unused;
                            if (m.Type == "mdhd") trackDur = ReadMvhdLike(b, m.Body, m.End, out unused);
                            else if (m.Type == "hdlr" && m.Body + 12 <= m.End) handler = Encoding.ASCII.GetString(b, m.Body + 8, 4);
                            else if (m.Type == "minf") SampleSizes(b, m.Body, m.End, out bytes, out frames);
                        }
                    }
                    if (handler == "vide" && !info.HasVideo)
                    {
                        info.VideoWidth = width;
                        info.VideoHeight = height;
                        if (bytes > 0 && trackDur > 0)
                        {
                            info.VideoBytes = bytes;
                            info.VideoSeconds = trackDur;
                            info.VideoFrames = frames;
                        }
                    }
                    if (handler == "vide") info.HasVideo = true;
                    else if (handler == "soun") info.HasAudio = true;
                    if ((handler == "vide" || handler == "soun") && trackDur > maxTrack) maxTrack = trackDur;
                }
                else if (box.Type == "udta")
                {
                    info.Comment = ReadTag(b, box.Body, box.End, "©cmt");
                    info.Description = ReadTag(b, box.Body, box.End, "desc");
                }
            }
            if (!info.HasVideo && !info.HasAudio) return null;
            double d = mvhd > 0 ? mvhd : (fragmentDuration > 0 && movieScale > 0 ? (double)fragmentDuration / movieScale : maxTrack);
            info.Duration = d > 0 && !double.IsNaN(d) && !double.IsInfinity(d) ? Math.Round(d, 3) : 0;
            return info;
        }

        // udta > meta (a full box: 4 bytes version/flags before its children) > ilst > item > data
        // (type and locale, 4 bytes each, then the UTF-8 text). That is where ffmpeg's
        // "-metadata comment=" (item ©cmt) and "-metadata description=" (item desc) end up in an MP4.
        private static string ReadTag(byte[] b, int start, int end, string tag)
        {
            foreach (var meta in Children(b, start, end))
            {
                if (meta.Type != "meta" || meta.Body + 4 > meta.End) continue;
                foreach (var ilst in Children(b, meta.Body + 4, meta.End))
                {
                    if (ilst.Type != "ilst") continue;
                    foreach (var item in Children(b, ilst.Body, ilst.End))
                    {
                        if (item.Type != tag) continue;
                        foreach (var data in Children(b, item.Body, item.End))
                        {
                            if (data.Type == "data" && data.Body + 8 <= data.End) return Encoding.UTF8.GetString(b, data.Body + 8, data.End - data.Body - 8);
                        }
                    }
                }
            }
            return null;
        }

        // minf > stbl > stsz: one size for all samples, or a size per sample (full box: version and
        // flags, the common size, the count, then the table).
        private static void SampleSizes(byte[] b, int start, int end, out long bytes, out long count)
        {
            bytes = 0;
            count = 0;
            foreach (var stbl in Children(b, start, end))
            {
                if (stbl.Type != "stbl") continue;
                foreach (var z in Children(b, stbl.Body, stbl.End))
                {
                    if (z.Type != "stsz" || z.Body + 12 > z.End) continue;
                    long size = ReadU32(b, z.Body + 4);
                    long n = ReadU32(b, z.Body + 8);
                    if (size > 0)
                    {
                        bytes = size * n;
                        count = n;
                        return;
                    }
                    if (z.Body + 12 + n * 4 > z.End) return;
                    long sum = 0;
                    int p = z.Body + 12;
                    for (long k = 0; k < n; k++, p += 4) sum += ReadU32(b, p);
                    bytes = sum;
                    count = n;
                    return;
                }
            }
        }

        // mvhd and mdhd share the same leading layout: version/flags, times, timescale, duration.
        private static double ReadMvhdLike(byte[] b, int p, int end, out long timescale)
        {
            timescale = 0;
            if (p + 4 > end) return 0;
            int version = b[p];
            ulong duration;
            if (version == 1)
            {
                if (p + 32 > end) return 0;
                timescale = ReadU32(b, p + 20);
                duration = ReadU64(b, p + 24);
                if (duration == ulong.MaxValue) return 0;
            }
            else
            {
                if (p + 20 > end) return 0;
                timescale = ReadU32(b, p + 12);
                duration = (ulong)ReadU32(b, p + 16);
                if (duration == 0xFFFFFFFF) return 0;
            }
            if (timescale <= 0) return 0;
            return (double)duration / timescale;
        }

        private struct Box
        {
            public string Type;
            public int Body;
            public int End;
        }

        private static System.Collections.Generic.IEnumerable<Box> Children(byte[] b, int start, int end)
        {
            int p = start;
            while (p + 8 <= end)
            {
                long size = ReadU32(b, p);
                // Latin-1, not ASCII: "©cmt" starts with byte 0xA9.
                string type = Latin1.GetString(b, p + 4, 4);
                int hdr = 8;
                if (size == 1)
                {
                    if (p + 16 > end) yield break;
                    size = (long)ReadU64(b, p + 8);
                    hdr = 16;
                }
                else if (size == 0)
                {
                    size = end - p;
                }
                if (size < hdr || p + size > end) yield break;
                yield return new Box { Type = type, Body = p + hdr, End = (int)(p + size) };
                p += (int)size;
            }
        }

        private static bool ReadFully(Stream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(buf, off, count - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        private static long ReadU32(byte[] b, int p)
        {
            return ((long)b[p] << 24) | ((long)b[p + 1] << 16) | ((long)b[p + 2] << 8) | b[p + 3];
        }

        private static ulong ReadU64(byte[] b, int p)
        {
            return ((ulong)ReadU32(b, p) << 32) | (ulong)ReadU32(b, p + 4);
        }
    }
}
