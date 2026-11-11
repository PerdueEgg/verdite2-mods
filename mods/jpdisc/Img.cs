// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;

namespace Kf2.Mods.JpDisc;

/// <summary>Image pieces for taking the Japanese text out of a frame: RGB frames of
/// 320 x 240, three bytes (or doubles) a pixel, and masks a bool a pixel.</summary>
static class Img
{
    public const int W = Mdec.W, H = Mdec.H, N = W * H;

    public static float[] Gray(byte[] rgb)
    {
        var g = new float[N];
        for (int i = 0; i < N; i++) g[i] = (rgb[3 * i] + rgb[3 * i + 1] + rgb[3 * i + 2]) / 3f;
        return g;
    }

    /// <summary>The grey level less its opening by 3 rows x 5 columns: thin strokes
    /// brighter than around them.</summary>
    public static float[] TopHat(byte[] rgb) => TopHat(Gray(rgb));

    public static float[] TopHat(float[] g)
    {
        var e = Filter(Filter(g, 2, 0, false), 0, 1, false);          // erosion: min over 5 across, 3 down
        var o = Filter(Filter(e, 2, 0, true), 0, 1, true);            // then dilation: the opening
        for (int i = 0; i < N; i++) o[i] = g[i] - o[i];
        return o;
    }

    /// <summary>The closing by 3 rows x 5 columns less the grey level: thin strokes
    /// darker than around them.</summary>
    public static float[] BlackHat(float[] g)
    {
        var d = Filter(Filter(g, 2, 0, true), 0, 1, true);            // dilation
        var c = Filter(Filter(d, 2, 0, false), 0, 1, false);          // then erosion: the closing
        for (int i = 0; i < N; i++) c[i] -= g[i];
        return c;
    }

    /// <summary>The grey level of a frame being worked on.</summary>
    public static float[] Gray(double[] rgb)
    {
        var g = new float[N];
        for (int i = 0; i < N; i++) g[i] = (float)((rgb[3 * i] + rgb[3 * i + 1] + rgb[3 * i + 2]) / 3);
        return g;
    }

    /// <summary>A Gaussian blur (edges repeated).</summary>
    public static float[] Blur(float[] a, double sigma)
    {
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var k = new float[2 * r + 1]; float sum = 0;
        for (int i = -r; i <= r; i++) { k[i + r] = (float)Math.Exp(-i * i / (2 * sigma * sigma)); sum += k[i + r]; }
        for (int i = 0; i < k.Length; i++) k[i] /= sum;
        var t = new float[N]; var o = new float[N];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = 0;
                for (int d = -r; d <= r; d++) v += k[d + r] * a[y * W + Math.Clamp(x + d, 0, W - 1)];
                t[y * W + x] = v;
            }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = 0;
                for (int d = -r; d <= r; d++) v += k[d + r] * t[Math.Clamp(y + d, 0, H - 1) * W + x];
                o[y * W + x] = v;
            }
        return o;
    }

    static float[] Filter(float[] a, int rx, int ry, bool max)
    {
        var o = new float[N];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = max ? float.MinValue : float.MaxValue;
                for (int dy = -ry; dy <= ry; dy++)
                {
                    int row = Math.Clamp(y + dy, 0, H - 1) * W;
                    for (int dx = -rx; dx <= rx; dx++)
                    {
                        float q = a[row + Math.Clamp(x + dx, 0, W - 1)];
                        v = max ? Math.Max(v, q) : Math.Min(v, q);
                    }
                }
                o[y * W + x] = v;
            }
        return o;
    }

    /// <summary>Grown by a pixel up, down, left and right, this many times.</summary>
    public static bool[] Dilate(bool[] m, int times)
    {
        var a = m;
        for (int t = 0; t < times; t++)
        {
            var b = (bool[])a.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!a[y * W + x]) continue;
                    if (x > 0) b[y * W + x - 1] = true;
                    if (x < W - 1) b[y * W + x + 1] = true;
                    if (y > 0) b[(y - 1) * W + x] = true;
                    if (y < H - 1) b[(y + 1) * W + x] = true;
                }
            a = b;
        }
        return times > 0 ? a : (bool[])m.Clone();
    }

    /// <summary>Connected pieces (up, down, left, right), numbered from 1.</summary>
    public static (int[] Lab, int Count) Label(bool[] m)
    {
        var lab = new int[N]; int n = 0;
        var stack = new Stack<int>();
        for (int s = 0; s < N; s++)
        {
            if (!m[s] || lab[s] != 0) continue;
            lab[s] = ++n; stack.Push(s);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % W, y = p / W;
                if (x > 0 && m[p - 1] && lab[p - 1] == 0) { lab[p - 1] = n; stack.Push(p - 1); }
                if (x < W - 1 && m[p + 1] && lab[p + 1] == 0) { lab[p + 1] = n; stack.Push(p + 1); }
                if (y > 0 && m[p - W] && lab[p - W] == 0) { lab[p - W] = n; stack.Push(p - W); }
                if (y < H - 1 && m[p + W] && lab[p + W] == 0) { lab[p + W] = n; stack.Push(p + W); }
            }
        }
        return (lab, n);
    }

    /// <summary>Box sums of a value and of a weight, for means over a window.</summary>
    public sealed class Integral
    {
        readonly double[] _v, _k;
        public Integral(Func<int, double> value, Func<int, bool> known)
        {
            _v = new double[(W + 1) * (H + 1)]; _k = new double[(W + 1) * (H + 1)];
            for (int y = 0; y < H; y++)
            {
                double rv = 0, rk = 0;
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (known(i)) { rk += 1; rv += value(i); }
                    _v[(y + 1) * (W + 1) + x + 1] = _v[y * (W + 1) + x + 1] + rv;
                    _k[(y + 1) * (W + 1) + x + 1] = _k[y * (W + 1) + x + 1] + rk;
                }
            }
        }

        /// <summary>The sum and the count over the window x0..x1-1, y0..y1-1.</summary>
        public (double Sum, double Count) Box(int x0, int y0, int x1, int y1)
        {
            x0 = Math.Clamp(x0, 0, W); x1 = Math.Clamp(x1, 0, W); y0 = Math.Clamp(y0, 0, H); y1 = Math.Clamp(y1, 0, H);
            int a = y0 * (W + 1), b = y1 * (W + 1);
            return (_v[b + x1] - _v[a + x1] - _v[b + x0] + _v[a + x0], _k[b + x1] - _k[a + x1] - _k[b + x0] + _k[a + x0]);
        }
    }

    /// <summary>Pixels of <paramref name="rest"/> (within the box) from a clean frame,
    /// where the scene around them is the same there: a mean difference under 10 over
    /// the pixels of a 9 x 9 window that neither text covers, a sixth of it at least.
    /// The pixels filled are taken off <paramref name="rest"/>.</summary>
    public static void Temporal(byte[] frame, bool[] covered, double[] outImg, bool[] rest, byte[] clean, int x0, int x1, int y0, int y1)
    {
        var ii = new Integral(i => (Math.Abs(frame[3 * i] - clean[3 * i]) + Math.Abs(frame[3 * i + 1] - clean[3 * i + 1]) + Math.Abs(frame[3 * i + 2] - clean[3 * i + 2])) / 3.0,
                              i => !covered[i]);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * W + x;
                if (!rest[i]) continue;
                var (num, den) = ii.Box(x - 4, y - 4, x + 5, y + 5);
                int area = (Math.Min(W, x + 5) - Math.Max(0, x - 4)) * (Math.Min(H, y + 5) - Math.Max(0, y - 4));
                if (den / area > 0.15 && num / den < 10)
                {
                    outImg[3 * i] = clean[3 * i]; outImg[3 * i + 1] = clean[3 * i + 1]; outImg[3 * i + 2] = clean[3 * i + 2];
                    rest[i] = false;
                }
            }
    }

    /// <summary>The rest of the masked pixels from their neighbours, by rounds of averaging.</summary>
    public static void Diffuse(double[] img, bool[] mask, int x0, int x1, int y0, int y1, int rounds = 60)
    {
        int ww = x1 - x0 + 1, hh = y1 - y0 + 1;
        var a = new double[ww * hh * 3]; var w = new double[ww * hh];
        bool any = false;
        for (int y = 0; y < hh; y++)
            for (int x = 0; x < ww; x++)
            {
                int i = (y + y0) * W + x + x0, j = y * ww + x;
                if (mask[i]) { any = true; continue; }
                w[j] = 1;
                for (int ch = 0; ch < 3; ch++) a[3 * j + ch] = img[3 * i + ch];
            }
        if (!any) return;
        var na = new double[a.Length]; var nw = new double[w.Length];
        for (int it = 0; it < rounds; it++)
        {
            Array.Copy(a, na, a.Length); Array.Copy(w, nw, w.Length);
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < ww; x++)
                {
                    int i = (y + y0) * W + x + x0, j = y * ww + x;
                    if (!mask[i]) continue;
                    double s0 = 0, s1 = 0, s2 = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int xx = Math.Clamp(x + dx, 0, ww - 1), yy = Math.Clamp(y + dy, 0, hh - 1), q = yy * ww + xx;
                            double wq = w[q];
                            if (wq <= 0) continue;
                            s0 += a[3 * q] * wq; s1 += a[3 * q + 1] * wq; s2 += a[3 * q + 2] * wq; n += wq;
                        }
                    if (n <= 0) continue;
                    na[3 * j] = s0 / n; na[3 * j + 1] = s1 / n; na[3 * j + 2] = s2 / n;
                    nw[j] = Math.Min(1.0, w[j] + 0.999);
                }
            (a, na) = (na, a); (w, nw) = (nw, w);
        }
        for (int y = 0; y < hh; y++)
            for (int x = 0; x < ww; x++)
            {
                int i = (y + y0) * W + x + x0, j = y * ww + x;
                if (!mask[i]) continue;
                for (int ch = 0; ch < 3; ch++) img[3 * i + ch] = a[3 * j + ch];
            }
    }

    /// <summary>Lettering over the frame at (x, y): a shadow one pixel right and down
    /// as the game draws its own, then the letters at this level, both at this strength.</summary>
    public static void Compose(double[] img, byte[] ink, int iw, int ih, int x0, int y0, float strength, byte level)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            int ox = x0 + (pass == 0 ? 1 : 0), oy = y0 + (pass == 0 ? 1 : 0);
            for (int y = 0; y < ih; y++)
                for (int x = 0; x < iw; x++)
                {
                    int px = ox + x, py = oy + y;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    double av = ink[y * iw + x] / 255.0 * strength;
                    if (av <= 0) continue;
                    int i = (py * W + px) * 3;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        double v = img[i + ch];
                        img[i + ch] = pass == 0 ? v * (1 - av * 0.6) : v * (1 - av) + level * av;
                    }
                }
        }
    }

    public static byte[] ToBytes(double[] img)
    {
        var o = new byte[img.Length];
        for (int i = 0; i < img.Length; i++) o[i] = (byte)Math.Clamp((int)(img[i] + 0.5), 0, 255);
        return o;
    }
}
