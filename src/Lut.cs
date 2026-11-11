using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoMixApp
{
    // A .cube 3D LUT read exactly the way ffmpeg's lut3d filter reads it (parse_cube in vf_lut3d.c),
    // so the preview drawn here matches the encode, and a file ffmpeg would refuse is refused here
    // too: lines before LUT_3D_SIZE are ignored, DOMAIN_MIN / DOMAIN_MAX / TITLE lines may stand
    // between the values, the red index changes fastest.
    //
    // 滤镜强度 mixes the table with "no change" (what each grid point stands for). Interpolation is
    // linear in the table, so that is exactly the filtered picture mixed with the original one, and
    // the encode costs the same at every strength.
    internal sealed class CubeLut
    {
        private const int MaxSize = 256;
        // fgets in ffmpeg reads at most this many bytes of a line at a time (MAX_LINE_SIZE - 1).
        private const int MaxLine = 511;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly ConcurrentDictionary<string, CubeLut> Cache = new ConcurrentDictionary<string, CubeLut>(StringComparer.OrdinalIgnoreCase);

        public readonly int Size;
        // Entry (r, g, b index) at ((r * Size + g) * Size + b) * 3, the order ffmpeg keeps it in.
        private readonly float[] _t;
        private readonly float[] _min;
        private readonly float[] _max;
        // ffmpeg's input scale per channel: 1 / (max - min), at most 1 (the minimum itself is unused).
        private readonly float[] _scale = new float[3];

        private CubeLut(int size, float[] table, float[] min, float[] max)
        {
            Size = size;
            _t = table;
            _min = min;
            _max = max;
            for (int c = 0; c < 3; c++)
            {
                float s = 1f / (max[c] - min[c]);
                _scale[c] = float.IsNaN(s) ? 0f : Math.Max(0f, Math.Min(1f, s));
            }
        }

        // The LUT of this file version, parsed once; null when ffmpeg would not read it either.
        public static CubeLut Get(string path)
        {
            string key;
            try
            {
                var fi = new FileInfo(path);
                key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch
            {
                return null;
            }
            CubeLut lut;
            if (Cache.TryGetValue(key, out lut)) return lut;
            lut = Load(path);
            if (Cache.Count > 64) Cache.Clear();
            Cache[key] = lut;
            return lut;
        }

        // Straight from the bytes, line by line as fgets hands them to ffmpeg (no text decoding: the
        // values are ASCII; read in small pieces, so parsing leaves next to nothing behind).
        private static CubeLut Load(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096))
                {
                    return Parse(new Lines(fs));
                }
            }
            catch
            {
                return null;
            }
        }

        private static CubeLut Parse(Lines lines)
        {
            byte[] f = lines.Line;
            int size = 0;
            bool found = false;
            while (lines.Next())
            {
                if (!Starts(f, 0, lines.Length, "LUT_3D_SIZE")) continue;
                // strtol(line + 12, NULL, 0), as ffmpeg does.
                size = (int)StrToL(f, 12, lines.Length);
                found = true;
                break;
            }
            if (!found || size < 2 || size > MaxSize) return null;
            var min = new float[] { 0f, 0f, 0f };
            var max = new float[] { 1f, 1f, 1f };
            var t = new float[size * size * size * 3];
            var v = new float[3];
            for (int b = 0; b < size; b++)
            {
                for (int g = 0; g < size; g++)
                {
                    for (int r = 0; r < size; r++)
                    {
                        int e;
                        while (true)
                        {
                            if (!lines.Next()) return null;
                            e = lines.Length;
                            if (Starts(f, 0, e, "DOMAIN_"))
                            {
                                float[] dst = Starts(f, 7, e, "MIN ") ? min : Starts(f, 7, e, "MAX ") ? max : null;
                                if (dst == null) return null;
                                // Like av_sscanf there: whatever it reads is kept, the rest stays.
                                Scan(f, 11, e, dst);
                                continue;
                            }
                            if (Starts(f, 0, e, "TITLE") || Blank(f, 0, e)) continue;
                            break;
                        }
                        if (Scan(f, 0, e, v) != 3) return null;
                        int o = ((r * size + g) * size + b) * 3;
                        t[o] = v[0];
                        t[o + 1] = v[1];
                        t[o + 2] = v[2];
                    }
                }
            }
            return new CubeLut(size, t, min, max);
        }

        // The lines of a file as fgets returns them: up to and including '\n', at most MaxLine bytes
        // (a longer line goes on as the next one).
        private sealed class Lines
        {
            private readonly Stream _s;
            private readonly byte[] _buf = new byte[65536];
            private int _pos;
            private int _len;
            public readonly byte[] Line = new byte[MaxLine];
            public int Length;

            public Lines(Stream s)
            {
                _s = s;
            }

            public bool Next()
            {
                Length = 0;
                while (Length < MaxLine)
                {
                    if (_pos >= _len)
                    {
                        _len = _s.Read(_buf, 0, _buf.Length);
                        _pos = 0;
                        if (_len <= 0)
                        {
                            _len = 0;
                            return Length > 0;
                        }
                    }
                    byte c = _buf[_pos++];
                    Line[Length++] = c;
                    if (c == (byte)'\n') break;
                }
                return true;
            }
        }

        // What grid point index of a channel stands for: no filter maps it to itself.
        private float Identity(int index, int channel)
        {
            return index / ((Size - 1) * _scale[channel]);
        }

        // This LUT at the given strength (0..1) as a .cube file ffmpeg reads the same way. false when
        // its domain leaves no usable "no change" (a range of zero or less).
        public bool WriteBlended(string path, double strength, string title)
        {
            for (int c = 0; c < 3; c++) if (!(_scale[c] > 0)) return false;
            float s = (float)Math.Max(0.0, Math.Min(1.0, strength));
            int n = Size;
            var id = new float[3, n];
            for (int c = 0; c < 3; c++) for (int k = 0; k < n; k++) id[c, k] = Identity(k, c);
            var sb = new StringBuilder(n * n * n * 28 + 256);
            sb.Append("# ").Append((title ?? "").Replace('\r', ' ').Replace('\n', ' ')).Append('\n');
            sb.Append("LUT_3D_SIZE ").Append(n.ToString(Inv)).Append('\n');
            // Written after LUT_3D_SIZE: before it ffmpeg ignores them.
            if (_min[0] != 0f || _min[1] != 0f || _min[2] != 0f || _max[0] != 1f || _max[1] != 1f || _max[2] != 1f)
            {
                sb.Append("DOMAIN_MIN ").Append(_min[0].ToString("R", Inv)).Append(' ').Append(_min[1].ToString("R", Inv)).Append(' ').Append(_min[2].ToString("R", Inv)).Append('\n');
                sb.Append("DOMAIN_MAX ").Append(_max[0].ToString("R", Inv)).Append(' ').Append(_max[1].ToString("R", Inv)).Append(' ').Append(_max[2].ToString("R", Inv)).Append('\n');
            }
            for (int b = 0; b < n; b++)
            {
                for (int g = 0; g < n; g++)
                {
                    for (int r = 0; r < n; r++)
                    {
                        int o = ((r * n + g) * n + b) * 3;
                        AppendValue(sb, id[0, r] + s * (_t[o] - id[0, r]));
                        sb.Append(' ');
                        AppendValue(sb, id[1, g] + s * (_t[o + 1] - id[1, g]));
                        sb.Append(' ');
                        AppendValue(sb, id[2, b] + s * (_t[o + 2] - id[2, b]));
                        sb.Append('\n');
                    }
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return true;
        }

        // Six decimals, like the LUTs Axe ships (far finer than one step of 8-bit video).
        private static void AppendValue(StringBuilder sb, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) > 1e9f)
            {
                sb.Append(v.ToString("R", Inv));
                return;
            }
            long u = (long)Math.Round(v * 1000000.0);
            if (u < 0)
            {
                sb.Append('-');
                u = -u;
            }
            sb.Append(u / 1000000).Append('.');
            long frac = u % 1000000;
            for (long d = 100000; d > 0; d /= 10) sb.Append((char)('0' + frac / d % 10));
        }

        public byte[] Apply(byte[] px)
        {
            var dst = new byte[px.Length];
            Apply(px, dst);
            return dst;
        }

        // The picture (Bgr32: blue, green, red, unused) through this LUT with tetrahedral
        // interpolation, step for step as lut3d computes it; values are rounded to 8 bits.
        public void Apply(byte[] px, byte[] dst)
        {
            int n = Size;
            float lutMax = n - 1;
            var lo = new int[3][];
            var hi = new int[3][];
            var fr = new float[3][];
            for (int c = 0; c < 3; c++)
            {
                lo[c] = new int[256];
                hi[c] = new int[256];
                fr[c] = new float[256];
                for (int v = 0; v < 256; v++)
                {
                    float x = v * (1f / 255f) * (_scale[c] * lutMax);
                    if (!(x > 0f)) x = 0f;
                    if (x > lutMax) x = lutMax;
                    int p = (int)x;
                    lo[c][v] = p;
                    hi[c][v] = Math.Min(p + 1, n - 1);
                    fr[c][v] = x - p;
                }
            }
            int[] loR = lo[0], loG = lo[1], loB = lo[2], hiR = hi[0], hiG = hi[1], hiB = hi[2];
            float[] frR = fr[0], frG = fr[1], frB = fr[2];
            float[] t = _t;
            int len = Math.Min(px.Length, dst.Length);
            for (int i = 0; i + 3 < len; i += 4)
            {
                int bv = px[i], gv = px[i + 1], rv = px[i + 2];
                int r0 = loR[rv], g0 = loG[gv], b0 = loB[bv];
                int r1 = hiR[rv], g1 = hiG[gv], b1 = hiB[bv];
                float dr = frR[rv], dg = frG[gv], db = frB[bv];
                int c000 = ((r0 * n + g0) * n + b0) * 3;
                int c111 = ((r1 * n + g1) * n + b1) * 3;
                int ca, cb;
                float w0, w1, w2, w3;
                if (dr > dg)
                {
                    if (dg > db)
                    {
                        ca = ((r1 * n + g0) * n + b0) * 3;
                        cb = ((r1 * n + g1) * n + b0) * 3;
                        w0 = 1 - dr; w1 = dr - dg; w2 = dg - db; w3 = db;
                    }
                    else if (dr > db)
                    {
                        ca = ((r1 * n + g0) * n + b0) * 3;
                        cb = ((r1 * n + g0) * n + b1) * 3;
                        w0 = 1 - dr; w1 = dr - db; w2 = db - dg; w3 = dg;
                    }
                    else
                    {
                        ca = ((r0 * n + g0) * n + b1) * 3;
                        cb = ((r1 * n + g0) * n + b1) * 3;
                        w0 = 1 - db; w1 = db - dr; w2 = dr - dg; w3 = dg;
                    }
                }
                else
                {
                    if (db > dg)
                    {
                        ca = ((r0 * n + g0) * n + b1) * 3;
                        cb = ((r0 * n + g1) * n + b1) * 3;
                        w0 = 1 - db; w1 = db - dg; w2 = dg - dr; w3 = dr;
                    }
                    else if (db > dr)
                    {
                        ca = ((r0 * n + g1) * n + b0) * 3;
                        cb = ((r0 * n + g1) * n + b1) * 3;
                        w0 = 1 - dg; w1 = dg - db; w2 = db - dr; w3 = dr;
                    }
                    else
                    {
                        ca = ((r0 * n + g1) * n + b0) * 3;
                        cb = ((r1 * n + g1) * n + b0) * 3;
                        w0 = 1 - dg; w1 = dg - dr; w2 = dr - db; w3 = db;
                    }
                }
                dst[i + 2] = Byte(w0 * t[c000] + w1 * t[ca] + w2 * t[cb] + w3 * t[c111]);
                dst[i + 1] = Byte(w0 * t[c000 + 1] + w1 * t[ca + 1] + w2 * t[cb + 1] + w3 * t[c111 + 1]);
                dst[i] = Byte(w0 * t[c000 + 2] + w1 * t[ca + 2] + w2 * t[cb + 2] + w3 * t[c111 + 2]);
                dst[i + 3] = 255;
            }
        }

        private static byte Byte(float v)
        {
            if (!(v > 0f)) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f + 0.5f);
        }

        // original + (filtered - original) * strength for every byte: the picture the mixed LUT gives.
        public static void Mix(byte[] original, byte[] filtered, double strength, byte[] dst)
        {
            int k = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, strength)) * 256);
            int len = Math.Min(dst.Length, Math.Min(original.Length, filtered.Length));
            for (int i = 0; i < len; i++)
            {
                int a = original[i];
                dst[i] = (byte)(a + (((filtered[i] - a) * k + 128) >> 8));
            }
        }

        // ---- reading the bytes the way the C library does ----

        private static bool Starts(byte[] f, int s, int e, string word)
        {
            if (e - s < word.Length) return false;
            for (int i = 0; i < word.Length; i++) if (f[s + i] != (byte)word[i]) return false;
            return true;
        }

        private static bool IsSpace(byte c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r';
        }

        // Empty or a comment (skip_line in vf_lut3d.c).
        private static bool Blank(byte[] f, int s, int e)
        {
            for (int i = s; i < e; i++)
            {
                if (IsSpace(f[i])) continue;
                return f[i] == (byte)'#';
            }
            return true;
        }

        // strtol(p, NULL, 0): decimal, 0x hex or leading-0 octal; 0 when there is no number.
        private static long StrToL(byte[] f, int p, int e)
        {
            while (p < e && IsSpace(f[p])) p++;
            bool neg = false;
            if (p < e && (f[p] == '+' || f[p] == '-'))
            {
                neg = f[p] == '-';
                p++;
            }
            int radix = 10;
            if (p + 2 < e && f[p] == '0' && (f[p + 1] == 'x' || f[p + 1] == 'X') && Digit(f[p + 2], 16) >= 0)
            {
                radix = 16;
                p += 2;
            }
            else if (p < e && f[p] == '0')
            {
                radix = 8;
            }
            long v = 0;
            for (; p < e; p++)
            {
                int d = Digit(f[p], radix);
                if (d < 0) break;
                v = Math.Min(int.MaxValue, v * radix + d);
            }
            return neg ? -v : v;
        }

        private static int Digit(byte c, int radix)
        {
            int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            return d < radix ? d : -1;
        }

        // sscanf(p, "%f %f %f"): up to dst.Length numbers, each after optional white space; returns
        // how many were read (it stops at the first that is not a number).
        private static int Scan(byte[] f, int p, int e, float[] dst)
        {
            int got = 0;
            while (got < dst.Length)
            {
                while (p < e && IsSpace(f[p])) p++;
                float v;
                if (!Number(f, ref p, e, out v)) break;
                dst[got++] = v;
            }
            return got;
        }

        private static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22
        };

        private static bool Word(byte[] f, int p, int e, string word)
        {
            if (e - p < word.Length) return false;
            for (int i = 0; i < word.Length; i++) if ((f[p + i] | 0x20) != word[i]) return false;
            return true;
        }

        // One decimal number (sign, digits, point, digits, exponent), or inf / nan, like strtof.
        private static bool Number(byte[] f, ref int p, int e, out float value)
        {
            value = 0f;
            int q = p;
            bool neg = false;
            if (q < e && (f[q] == '+' || f[q] == '-'))
            {
                neg = f[q] == '-';
                q++;
            }
            if (Word(f, q, e, "inf"))
            {
                p = q + (Word(f, q, e, "infinity") ? 8 : 3);
                value = neg ? float.NegativeInfinity : float.PositiveInfinity;
                return true;
            }
            if (Word(f, q, e, "nan"))
            {
                p = q + 3;
                value = float.NaN;
                return true;
            }
            long mant = 0;
            int digits = 0;
            int exp = 0;
            bool any = false;
            for (; q < e && f[q] >= '0' && f[q] <= '9'; q++)
            {
                any = true;
                if (digits < 18)
                {
                    mant = mant * 10 + (f[q] - '0');
                    if (mant > 0) digits++;
                }
                else
                {
                    exp++;
                }
            }
            if (q < e && f[q] == '.')
            {
                q++;
                for (; q < e && f[q] >= '0' && f[q] <= '9'; q++)
                {
                    any = true;
                    if (digits < 18)
                    {
                        mant = mant * 10 + (f[q] - '0');
                        if (mant > 0) digits++;
                        exp--;
                    }
                }
            }
            if (!any) return false;
            if (q < e && (f[q] == 'e' || f[q] == 'E'))
            {
                int x = q + 1;
                bool eneg = false;
                if (x < e && (f[x] == '+' || f[x] == '-'))
                {
                    eneg = f[x] == '-';
                    x++;
                }
                if (x < e && f[x] >= '0' && f[x] <= '9')
                {
                    int ev = 0;
                    for (; x < e && f[x] >= '0' && f[x] <= '9'; x++) ev = Math.Min(100000, ev * 10 + (f[x] - '0'));
                    exp += eneg ? -ev : ev;
                    q = x;
                }
            }
            double d;
            if (mant == 0) d = 0;
            else if (exp >= 0 && exp <= 22) d = mant * Pow10[exp];
            else if (exp < 0 && exp >= -22) d = mant / Pow10[-exp];
            else d = double.Parse(mant.ToString(Inv) + "E" + exp.ToString(Inv), NumberStyles.Float, Inv);
            value = (float)(neg ? -d : d);
            p = q;
            return true;
        }
    }
}
