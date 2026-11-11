// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;

namespace Kf2.Mods.JpDisc;

/// <summary>
/// PlayStation MDEC frames, versions 2 and 3: decoded to RGB and encoded back, for
/// the frames whose text is replaced. Every movie on both King's Field discs is
/// version 3 (King's Field III's were version 2, which this started from).
///
/// A frame: an 8-byte header (u16 the MDEC code count / 2 rounded up to 32, u16
/// 0x3800, u16 qscale, u16 the version), then a bitstream of 16-bit little-endian
/// words read from the top bit. Macroblocks of 16 x 16 pixels column by column (x
/// outer, y inner), each six 8 x 8 blocks: Cr, Cb, then Y top left, top right,
/// bottom left, bottom right. A block: its DC, then MPEG-1 run/level codes (table
/// B.14; '10' ends the block, '000001' escapes to a 6-bit run and a 10-bit level),
/// in zigzag order, dequantised as level * Q[i] * qscale / 8.
///
/// The DC: version 2, a 10-bit signed value (times Q[0], 2). Version 3, the
/// difference from the last block of the same component (Y, Cb or Cr; each starts
/// at 0 at the frame's top), as MPEG-1 codes it: the size from the luminance or
/// chrominance dct_dc_size table, then that many bits, a leading 0 meaning
/// negative; the 10-bit value is that sum times 4 (so the DC has 8 bits of
/// precision, the block's mean less 128).
///
/// The last block is followed by an end mark, ten bits (0111111111 in version 2,
/// 1111111111 in version 3, as every frame on the discs has it), then zero bits to
/// a 32-bit boundary: without it the game's decoder (libpress DecDCTvlc) reads on
/// into the padding as more blocks, and the port's MDEC, which keeps what it
/// decodes, puts every later frame out of step (the picture scrolls).
///
/// Checked against ffmpeg's decoder on the King's Field II (JP) movies: 51 dB on the
/// disc's frames, and a frame encoded here decodes in ffmpeg to 50 dB of what was
/// meant; the header's code count matches the disc's on every frame tried.
/// </summary>
static class Mdec
{
    public const int W = 320, H = 240;

    static readonly int[] Q =
    [
        2, 16, 19, 22, 26, 27, 29, 34, 16, 16, 22, 24, 27, 29, 34, 37,
        19, 22, 26, 27, 29, 34, 34, 38, 22, 22, 26, 27, 29, 34, 37, 40,
        22, 26, 27, 29, 32, 35, 40, 48, 26, 27, 29, 32, 35, 40, 48, 58,
        26, 27, 29, 34, 38, 46, 56, 69, 27, 29, 35, 38, 46, 56, 69, 83,
    ];
    static readonly int[] ZZ =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // MPEG-1 table B.14 without the sign bit: code, run, level ("EOB" and "ESC" have level 0, run -1 / -2).
    const string Table =
        "10 -1 0|11 0 1|011 1 1|0100 0 2|0101 2 1|00101 0 3|00111 3 1|00110 4 1|000110 1 2|000111 5 1|000101 6 1|000100 7 1|" +
        "0000110 0 4|0000100 2 2|0000111 8 1|0000101 9 1|000001 -2 0|00100110 0 5|00100001 0 6|00100101 1 3|00100100 3 2|00100111 10 1|" +
        "00100011 11 1|00100010 12 1|00100000 13 1|0000001010 0 7|0000001100 1 4|0000001011 2 3|0000001111 4 2|0000001001 5 2|" +
        "0000001110 14 1|0000001101 15 1|0000001000 16 1|000000011101 0 8|000000011000 0 9|000000010011 0 10|000000010000 0 11|" +
        "000000011011 1 5|000000010100 2 4|000000011100 3 3|000000010010 4 3|000000011110 6 2|000000010101 7 2|000000010001 8 2|" +
        "000000011111 17 1|000000011010 18 1|000000011001 19 1|000000010111 20 1|000000010110 21 1|0000000011010 0 12|0000000011001 0 13|" +
        "0000000011000 0 14|0000000010111 0 15|0000000010110 1 6|0000000010101 1 7|0000000010100 2 5|0000000010011 3 4|0000000010010 5 3|" +
        "0000000010001 9 2|0000000010000 10 2|0000000011111 22 1|0000000011110 23 1|0000000011101 24 1|0000000011100 25 1|0000000011011 26 1|" +
        "00000000011111 0 16|00000000011110 0 17|00000000011101 0 18|00000000011100 0 19|00000000011011 0 20|00000000011010 0 21|" +
        "00000000011001 0 22|00000000011000 0 23|00000000010111 0 24|00000000010110 0 25|00000000010101 0 26|00000000010100 0 27|" +
        "00000000010011 0 28|00000000010010 0 29|00000000010001 0 30|00000000010000 0 31|000000000011000 0 32|000000000010111 0 33|" +
        "000000000010110 0 34|000000000010101 0 35|000000000010100 0 36|000000000010011 0 37|000000000010010 0 38|000000000010001 0 39|" +
        "000000000010000 0 40|000000000011111 1 8|000000000011110 1 9|000000000011101 1 10|000000000011100 1 11|000000000011011 1 12|" +
        "000000000011010 1 13|000000000011001 1 14|0000000000010011 1 15|0000000000010010 1 16|0000000000010001 1 17|0000000000010000 1 18|" +
        "0000000000010100 6 3|0000000000011010 11 2|0000000000011001 12 2|0000000000011000 13 2|0000000000010111 14 2|0000000000010110 15 2|" +
        "0000000000010101 16 2|0000000000011111 27 1|0000000000011110 28 1|0000000000011101 29 1|0000000000011100 30 1|0000000000011011 31 1";

    // Decoding: the next 16 bits -> (code length, run, level); run -1 the block's end, -2 an escape.
    static readonly (byte Len, sbyte Run, byte Level)[] Lookup = new (byte, sbyte, byte)[1 << 16];
    // Encoding: (run, level) -> (code, length), for the table's pairs.
    static readonly Dictionary<int, (int Code, int Len)> Codes = new();
    static readonly double[,] C = new double[8, 8];

    static Mdec()
    {
        foreach (var e in Table.Split('|'))
        {
            var p = e.Split(' ');
            string code = p[0]; int run = int.Parse(p[1]), level = int.Parse(p[2]);
            int len = code.Length, v = Convert.ToInt32(code, 2);
            int lo = v << (16 - len), n = 1 << (16 - len);
            for (int i = 0; i < n; i++) Lookup[lo + i] = ((byte)len, (sbyte)run, (byte)level);
            if (run >= 0) Codes[run << 8 | level] = (v, len);
        }
        for (int u = 0; u < 8; u++)
            for (int x = 0; x < 8; x++)
                C[u, x] = Math.Sqrt((u == 0 ? 1 : 2) / 8.0) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
    }

    sealed class Reader
    {
        readonly byte[] _d; int _bit; readonly int _end;
        public Reader(byte[] d, int start) { _d = d; _bit = start * 8; _end = d.Length / 2 * 16; }
        int Bit(int i)
        {
            if (i >= _end) return 0;
            int w = i >> 4, b = 15 - (i & 15);
            int word = _d[2 * w] | _d[2 * w + 1] << 8;
            return word >> b & 1;
        }
        public int Peek16() { int v = 0; for (int i = 0; i < 16; i++) v = v << 1 | Bit(_bit + i); return v; }
        public int Read(int n) { int v = 0; for (int i = 0; i < n; i++) v = v << 1 | Bit(_bit++); return v; }
        public void Skip(int n) => _bit += n;
    }

    static int Signed(int v, int bits) => v >= 1 << (bits - 1) ? v - (1 << bits) : v;

    /// <summary>RGB, three bytes a pixel, and the frame's qscale; null for a frame
    /// this doesn't read.</summary>
    public static byte[]? Decode(byte[] data, out int qscale) => Decode(data, out qscale, out _);

    /// <summary>The frame's version (2 or 3) as well.</summary>
    public static byte[]? Decode(byte[] data, out int qscale, out int version)
    {
        qscale = data.Length >= 8 ? data[4] | data[5] << 8 : 0;
        version = data.Length >= 8 ? data[6] | data[7] << 8 : 0;
        if (data.Length < 8 || (version != 2 && version != 3)) return null;
        var r = new Reader(data, 8);
        var last = new int[3];               // version 3's DC predictors: Y, Cb, Cr
        var y = new double[H * W]; var cb = new double[H / 2 * W / 2]; var cr = new double[H / 2 * W / 2];
        var blk = new double[64];
        try
        {
            for (int mx = 0; mx < W / 16; mx++)
                for (int my = 0; my < H / 16; my++)
                    for (int b = 0; b < 6; b++)
                    {
                        Block(r, qscale, blk, version, last, b == 0 ? 2 : b == 1 ? 1 : 0);
                        if (b == 0) Put(cr, W / 2, mx * 8, my * 8, blk);
                        else if (b == 1) Put(cb, W / 2, mx * 8, my * 8, blk);
                        else { int k = b - 2; Put(y, W, mx * 16 + (k % 2) * 8, my * 16 + (k / 2) * 8, blk); }
                    }
        }
        catch (Exception) { return null; }
        return ToRgb(y, cb, cr);
    }

    // MPEG-1's dct_dc_size codes, by size 0-8.
    static readonly string[] DcLuma = ["100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110"];
    static readonly string[] DcChroma = ["00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110"];
    static readonly (int Code, int Len)[] DcLumaCodes = Codes9(DcLuma), DcChromaCodes = Codes9(DcChroma);

    static (int, int)[] Codes9(string[] t)
    {
        var o = new (int, int)[t.Length];
        for (int i = 0; i < t.Length; i++) o[i] = (Convert.ToInt32(t[i], 2), t[i].Length);
        return o;
    }

    /// <summary>A version 3 DC size: the code that the next bits start with.</summary>
    static int DcSize(Reader r, bool luma)
    {
        var t = luma ? DcLumaCodes : DcChromaCodes;
        int bits = r.Peek16();
        for (int s = 0; s < t.Length; s++)
            if (bits >> (16 - t[s].Len) == t[s].Code) { r.Skip(t[s].Len); return s; }
        throw new InvalidOperationException("bad DC size");
    }

    static void Block(Reader r, int qs, double[] outPx, int version, int[] last, int comp)
    {
        Span<double> co = stackalloc double[64];
        co.Clear();
        if (version == 2) co[0] = Signed(r.Read(10), 10) * Q[0];
        else
        {
            int size = DcSize(r, comp == 0), diff = 0;
            if (size > 0)
            {
                int v = r.Read(size);
                diff = (v >> (size - 1)) != 0 ? v : v - (1 << size) + 1;
            }
            last[comp] += diff;
            co[0] = last[comp] * 4 * Q[0];
        }
        int i = 0;
        while (true)
        {
            var (len, run, level) = Lookup[r.Peek16()];
            if (len == 0) throw new InvalidOperationException("bad code");
            r.Skip(len);
            int lev;
            if (run == -1) break;
            if (run == -2) { run = (sbyte)r.Read(6); lev = Signed(r.Read(10), 10); }
            else { lev = level; if (r.Read(1) == 1) lev = -lev; }
            i += run + 1;
            if (i > 63) throw new InvalidOperationException("run past the block");
            co[ZZ[i]] = lev * Q[ZZ[i]] * qs / 8.0;
        }
        Idct(co, outPx);
    }

    static void Idct(ReadOnlySpan<double> F, double[] f)
    {
        Span<double> t = stackalloc double[64];
        for (int u = 0; u < 8; u++)              // t = C^T F (columns)
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++) s += C[k, x] * F[k * 8 + u];
                t[x * 8 + u] = s;
            }
        for (int x = 0; x < 8; x++)
            for (int yv = 0; yv < 8; yv++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++) s += t[x * 8 + k] * C[k, yv];
                f[x * 8 + yv] = s;
            }
    }

    static void Put(double[] plane, int stride, int x0, int y0, double[] b)
    {
        for (int yy = 0; yy < 8; yy++)
            for (int xx = 0; xx < 8; xx++) plane[(y0 + yy) * stride + x0 + xx] = b[yy * 8 + xx];
    }

    static byte[] ToRgb(double[] y, double[] cb, double[] cr)
    {
        var o = new byte[W * H * 3];
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                double Y = y[py * W + px] + 128, B = cb[py / 2 * (W / 2) + px / 2], R = cr[py / 2 * (W / 2) + px / 2];
                int i = (py * W + px) * 3;
                o[i] = Clamp(Y + 1.402 * R);
                o[i + 1] = Clamp(Y - 0.3437 * B - 0.7143 * R);
                o[i + 2] = Clamp(Y + 1.772 * B);
            }
        return o;
    }

    static byte Clamp(double v) => (byte)Math.Clamp((int)(v + 0.5), 0, 255);

    // ---- encoding ----

    sealed class Writer
    {
        readonly List<ushort> _w = new(); int _acc, _n;
        public void Put(int v, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                _acc = _acc << 1 | (v >> i & 1);
                if (++_n == 16) { _w.Add((ushort)_acc); _acc = 0; _n = 0; }
            }
        }
        public byte[] Bytes(int head0, int qs, int version)
        {
            while (_n != 0) Put(0, 1);
            while (_w.Count % 2 != 0) _w.Add(0);
            _w.Add(0); _w.Add(0);
            var o = new byte[8 + _w.Count * 2];
            o[0] = (byte)head0; o[1] = (byte)(head0 >> 8); o[2] = 0x00; o[3] = 0x38; o[4] = (byte)qs; o[5] = (byte)(qs >> 8); o[6] = (byte)version; o[7] = 0;
            for (int i = 0; i < _w.Count; i++) { o[8 + 2 * i] = (byte)_w[i]; o[9 + 2 * i] = (byte)(_w[i] >> 8); }
            return o;
        }
    }

    /// <summary>A frame's bitstream at this qscale, in this version (2 or 3).</summary>
    public static byte[] Encode(byte[] rgb, int qs, int version = 3)
    {
        var last = new int[3];
        var y = new double[W * H]; var cb = new double[W / 2 * H / 2]; var cr = new double[W / 2 * H / 2];
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                int i = (py * W + px) * 3;
                double r = rgb[i], g = rgb[i + 1], b = rgb[i + 2];
                double Y = 0.299 * r + 0.587 * g + 0.114 * b;
                y[py * W + px] = Y - 128;
                int c = py / 2 * (W / 2) + px / 2;
                cb[c] += (b - Y) / 1.772 / 4;
                cr[c] += (r - Y) / 1.402 / 4;
            }
        var w = new Writer(); int codes = 0;
        var blk = new double[64];
        for (int mx = 0; mx < W / 16; mx++)
            for (int my = 0; my < H / 16; my++)
            {
                Take(cr, W / 2, mx * 8, my * 8, blk); codes += EncodeBlock(blk, qs, w, version, last, 2);
                Take(cb, W / 2, mx * 8, my * 8, blk); codes += EncodeBlock(blk, qs, w, version, last, 1);
                for (int k = 0; k < 4; k++) { Take(y, W, mx * 16 + (k % 2) * 8, my * 16 + (k / 2) * 8, blk); codes += EncodeBlock(blk, qs, w, version, last, 0); }
            }
        w.Put(version == 2 ? 0x1FF : 0x3FF, 10);  // the end of the frame, as every frame on the disc has it
        return w.Bytes(((codes + 1) / 2 + 31) & ~31, qs, version);
    }

    static void Take(double[] plane, int stride, int x0, int y0, double[] b)
    {
        for (int yy = 0; yy < 8; yy++)
            for (int xx = 0; xx < 8; xx++) b[yy * 8 + xx] = plane[(y0 + yy) * stride + x0 + xx];
    }

    static int EncodeBlock(double[] px, int qs, Writer w, int version, int[] last, int comp)
    {
        Span<double> t = stackalloc double[64];
        Span<double> F = stackalloc double[64];
        for (int u = 0; u < 8; u++)              // F = C f C^T
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++) s += C[u, k] * px[k * 8 + x];
                t[u * 8 + x] = s;
            }
        for (int u = 0; u < 8; u++)
            for (int v = 0; v < 8; v++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++) s += t[u * 8 + k] * C[v, k];
                F[u * 8 + v] = s;
            }
        if (version == 2)
        {
            int dc = Math.Clamp((int)Math.Round(F[0] / 2), -512, 511);
            w.Put(dc & 0x3FF, 10);
        }
        else
        {
            int mean = Math.Clamp((int)Math.Round(F[0] / 8), -128, 127), diff = mean - last[comp];
            last[comp] = mean;
            int size = 0;
            for (int a = Math.Abs(diff); a != 0; a >>= 1) size++;
            var code = (comp == 0 ? DcLumaCodes : DcChromaCodes)[size];
            w.Put(code.Code, code.Len);
            if (size > 0) w.Put(diff > 0 ? diff : diff + (1 << size) - 1, size);
        }
        int run = 0, n = 1;
        for (int i = 1; i < 64; i++)
        {
            int l = (int)Math.Round(F[ZZ[i]] * 8 / (Q[ZZ[i]] * qs));
            if (l == 0) { run++; continue; }
            int a = Math.Abs(l);
            if (a < 256 && Codes.TryGetValue(run << 8 | a, out var code)) { w.Put(code.Code, code.Len); w.Put(l < 0 ? 1 : 0, 1); }
            else { l = Math.Clamp(l, -512, 511); w.Put(1, 6); w.Put(run, 6); w.Put(l & 0x3FF, 10); }
            run = 0; n++;
        }
        w.Put(2, 2);                              // '10', the block's end
        return n + 1;
    }

    /// <summary>The bitstream at the lowest qscale from <paramref name="qs0"/> that fits.</summary>
    public static byte[]? Fit(byte[] rgb, int budget, int qs0, int version = 3)
    {
        for (int qs = Math.Max(1, qs0); qs < 64; qs++)
        {
            var d = Encode(rgb, qs, version);
            if (d.Length <= budget) return d;
        }
        return null;
    }
}
