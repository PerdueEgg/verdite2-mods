// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using System.Threading;
using RecompOne.Runtime.Cdrom;

namespace Kf2.Mods.JpDisc;

/// <summary>
/// The English over the Japanese movies' text cards (Cards.cs; the text is
/// Retranslation's, "movie:" + the card's key in the Text mod's string table).
///
/// ---- the twin frames ----
///
/// The Japanese OP\OP.S and the US OP\OP2.S are the same footage frame for frame,
/// and so are the Japanese OP\ED.S and the US OP\ED0.S as far as the US one goes;
/// only the text burned into them differs. So each frame of a card has a twin on
/// the US disc, with the scene under the Japanese text in it wherever the US text
/// isn't. (King's Field III's movies had that only for the ending's crawl; here it
/// is every card.)
///
/// ---- each card, worked out once (a background thread, when both discs are there) ----
///
/// - The Japanese text: over up to nine frames of the middle half of the card, the
///   pixels where the JP frame is brighter than its twin (by 12 grey levels, a
///   channel 20 apart) on a thin bright stroke (top-hat over 15), or a stroke over
///   25 within three pixels of those; kept where at least half the frames agree.
///   Added: the JP frame's own strokes (median top-hat over those frames less the
///   most it has on the clean frames around the card, over 25) within four pixels,
///   for the strokes under a US letter, where the twins agree. Kept: the pieces of
///   4 pixels or more (once grown by four pixels; 0.5 kept only those 8% the size
///   of the biggest, which lost lone ellipsis dots), inside the block (the run of
///   rows, gaps of up to 8 bridged, holding the most of it, then its columns).
/// - How strongly it shows on each frame: the mean of (JP - US) over it, from the
///   level of the frames around the card to the level mid-card.
/// - The US text where it is fullest (pixels brighter than the JP twin on a thin
///   stroke, on at least half the frames that have 70% of the most of it), and how
///   much of it shows on each frame: the US cards can come and go apart from the
///   Japanese ones (the ending's last card does).
///
/// ---- each frame ----
///
/// - The US text on it: the frame's own (brighter than the JP twin by 6, a channel
///   10 apart, a stroke over 10, or a stroke over 30 within two pixels of those,
///   grown by one) and, while it shows at all, the still one. Also a US stroke over
///   22 on the Japanese text that is as bright as that text: where the two texts
///   share pixels the twins agree, and 0.5 copied the US stroke back in.
/// - What is taken out: the Japanese text grown by two pixels (its soft edges), and
///   the frame's own Japanese strokes within four pixels of it that the US twin
///   doesn't have (brighter by 12, a stroke over 12), grown by one.
/// - Each of those pixels the US text doesn't cover: the US twin's, raised or lowered
///   by the mean difference between the twins around it (15 x 15, both texts left
///   out), since the Japanese movies are encoded harder and come out a little
///   different.
/// - Under the US text: from the clean JP frames just outside the card, nearest
///   first and no more than 40 frames away, where the scene around the pixel still
///   matches; the rest from around it (60 rounds of averaging).
/// - Then the last strays: in what was filled, a thin mark brighter or darker than
///   around it by 18 (top-hat or black-hat), grown a pixel, is filled from around it
///   again; and what was filled gets noise as strong as the Japanese frame's own
///   (its high frequencies around the texts, by their median), new each frame as the
///   Japanese frames' is, so the smoothed patches don't look flat.
/// - Then the English: the Text mod's lettering wrapped to the Japanese block's width
///   (240 at least), at its left, centred on it, at its strength, over a shadow one
///   pixel right and down as the game draws its own.
/// - Encoded again into the frame's own sectors, at the lowest qscale from the
///   frame's own that fits them.
///
/// The frames are kept as the sectors' new user data (about 25 MB for both movies),
/// so playing them costs nothing; JpDisc.cs's Remapped answers them.
/// </summary>
sealed class Card
{
    public string Key = "", Movie = "", UsMovie = "";
    public int First, Last, Lo, Hi;

    // worked out once
    public volatile bool Ready;
    public bool[] M0 = [], M = [], M2 = [], Near4 = [], Uts = [];
    public int X0, X1, Y0, Y1;
    public float[] Strength = [];       // from Lo
    public float[] UsShows = [];        // from Lo

    // the English, laid out on the main thread; replaced whole, never changed in place
    public volatile Letters? Letters;
    public string? Text;
    public volatile int Version, Built = -1;
}

sealed record Letters(byte[] Ink, int W, int H, int X, int Y, byte Level);

static class MovieText
{
    const int W = Img.W, H = Img.H, N = Img.N;
    public static readonly List<Card> Cards = new();
    public static string Status = "";
    public static byte Ink = 205;
    public static string Style = "dialogue";
    static readonly Dictionary<int, byte[]> _sectors = new();
    static readonly Dictionary<string, List<int>> _owned = new();
    static readonly object _lock = new();
    static readonly AutoResetEvent _wake = new(false);
    static int _run;

    // ---- the work ----

    /// <summary>Start over for a newly opened Japanese image: work out every card,
    /// then letter each as its English arrives.</summary>
    public static void Prepare(Func<string, (int Lba, int Count)?> jpFile)
    {
        int run = Interlocked.Increment(ref _run);
        lock (_lock)
        {
            Cards.Clear(); _sectors.Clear(); _owned.Clear();
            foreach (var (key, movie, us, first, last) in Kf2.Mods.JpDisc.Cards.All)
                Cards.Add(new Card { Key = key, Movie = movie, UsMovie = us, First = first, Last = last, Lo = Math.Max(1, first - 8), Hi = last + 8 });
        }
        Status = $"reading the movies' text cards (0 of {Cards.Count})";
        var t = new Thread(() =>
        {
            try { Work(run, jpFile); }
            catch (Exception e) { Status = "the movies' text: " + e.Message; Console.WriteLine($"[jpdisc] movie text: {e}"); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "jpdisc lettering" };
        t.Start();
    }

    /// <summary>Stop the work (the image was closed).</summary>
    public static void Stop()
    {
        Interlocked.Increment(ref _run);
        _wake.Set();
        lock (_lock) { _sectors.Clear(); _owned.Clear(); }
    }

    static void Work(int run, Func<string, (int Lba, int Count)?> jpFile)
    {
        // The US image, opened again for reading the twin frames (the port may not
        // have been given it yet when the mods load).
        string usPath = RecompOne.Runtime.Runtime.CdPath;
        while (string.IsNullOrWhiteSpace(usPath) || !System.IO.File.Exists(usPath))
        {
            if (run != _run) return;
            Status = "waiting for the US disc image";
            _wake.WaitOne(1000);
            usPath = RecompOne.Runtime.Runtime.CdPath;
        }
        using var usDisc = DiscFs.Open(usPath);
        var jpIndex = new Dictionary<string, FrameIndex>();
        var usIndex = new Dictionary<string, FrameIndex>();
        Card[] cards;
        lock (_lock) cards = Cards.ToArray();
        int prepared = 0, found = 0;

        // Cards are worked out in the order they play, and each is lettered as soon as
        // its English is laid out, before the next is worked out, so the opening's
        // first card is ready first.
        while (run == _run)
        {
            bool busy = false;
            foreach (var c in cards)
            {
                if (run != _run) return;
                if (!c.Ready || c.Built == c.Version) continue;
                int v = c.Version;
                var l = c.Letters;
                var made = l == null ? new Dictionary<int, byte[]>() : Build(c, l, new Frames(jpIndex[c.Movie]), new Frames(usIndex[c.UsMovie]), run);
                if (made == null || run != _run) return;
                lock (_lock)
                {
                    if (_owned.TryGetValue(c.Key, out var old)) foreach (var k in old) _sectors.Remove(k);
                    foreach (var (k, u) in made) _sectors[k] = u;
                    _owned[c.Key] = new List<int>(made.Keys);
                }
                c.Built = v;
                busy = true;
                Report(cards, prepared);
                if (l != null) Console.WriteLine($"[jpdisc] card {c.Key} in English: {FramesOf(made)} frames");
                break;
            }
            if (!busy && prepared < cards.Length)
            {
                var c = cards[prepared++];
                busy = true;
                try
                {
                    if (!jpIndex.TryGetValue(c.Movie, out var jx))
                    {
                        var f = jpFile(c.Movie);
                        if (f == null) { Console.WriteLine($"[jpdisc] {c.Movie} is not on the Japanese disc"); continue; }
                        jpIndex[c.Movie] = jx = FrameIndex.Scan(lba => Jp.Read(lba, 2336), f.Value.Lba, f.Value.Count);
                    }
                    if (!usIndex.TryGetValue(c.UsMovie, out var ux))
                    {
                        if (!usDisc.Locate(c.UsMovie, out int ul, out uint us)) { Console.WriteLine($"[jpdisc] {c.UsMovie} is not on the US disc"); continue; }
                        usIndex[c.UsMovie] = ux = FrameIndex.Scan(lba => usDisc.ReadSectorData(lba, 2336), ul, (int)((us + 2047) / 2048));
                    }
                    if (PrepareCard(c, new Frames(jx), new Frames(ux))) { found++; c.Ready = true; }
                    else Console.WriteLine($"[jpdisc] card {c.Key}: no text found");
                }
                catch (Exception e) { Console.WriteLine($"[jpdisc] card {c.Key}: {e.Message}"); }
                Report(cards, prepared);
                if (prepared == cards.Length) Console.WriteLine($"[jpdisc] {found} of {cards.Length} movie text cards found");
            }
            if (!busy) _wake.WaitOne(1000);
        }
    }

    static int FramesOf(Dictionary<int, byte[]> sectors)
    {
        var frames = new HashSet<uint>();
        foreach (var u in sectors.Values) frames.Add(BitConverter.ToUInt32(u, 8));
        return frames.Count;
    }

    static void Report(Card[] cards, int prepared)
    {
        int done = 0, ready = 0;
        foreach (var c in cards) if (c.Ready) { ready++; if (c.Built == c.Version && c.Letters != null) done++; }
        Status = prepared < cards.Length
            ? $"reading the movies' text cards ({prepared} of {cards.Length}); {done} in English"
            : $"{done} of {ready} movie text cards in English" + (done < ready ? " (the rest wait for the English: Text and Retranslation on?)" : "");
    }

    /// <summary>A movie's frames, decoded, the last few kept.</summary>
    sealed class Frames
    {
        readonly FrameIndex _x;
        readonly Dictionary<int, byte[]?> _cache = new();
        readonly Queue<int> _order = new();
        public Frames(FrameIndex x) { _x = x; }
        public FrameIndex Index => _x;

        public byte[]? this[int no]
        {
            get
            {
                if (_cache.TryGetValue(no, out var f)) return f;
                f = no >= 1 ? _x.Rgb(no) : null;
                _cache[no] = f; _order.Enqueue(no);
                while (_order.Count > 40) _cache.Remove(_order.Dequeue());
                return f;
            }
        }
    }

    static float Median(List<float> v)
    {
        if (v.Count == 0) return 0;
        v.Sort();
        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
    }

    static int MaxDiff(byte[] a, byte[] b, int i) =>
        Math.Max(Math.Abs(a[3 * i] - b[3 * i]), Math.Max(Math.Abs(a[3 * i + 1] - b[3 * i + 1]), Math.Abs(a[3 * i + 2] - b[3 * i + 2])));

    static bool PrepareCard(Card c, Frames jp, Frames us)
    {
        int a = c.First, b = c.Last, span = b - a;
        int m0 = a + span / 4, m1 = b - span / 4, step = Math.Max(1, (m1 - m0) / 9);
        var samples = new List<int>();
        for (int f = m0; f < m1 && samples.Count < 9; f += step) if (jp[f] != null && us[f] != null) samples.Add(f);
        if (samples.Count == 0) return false;

        // The Japanese text, where the twins disagree.
        var votes = new int[N];
        var tops = new List<float[]>();
        foreach (int f in samples)
        {
            var j = jp[f]!; var u = us[f]!;
            var gj = Img.Gray(j); var gu = Img.Gray(u); var tj = Img.TopHat(gj);
            tops.Add(tj);
            var bas = new bool[N];
            for (int i = 0; i < N; i++) bas[i] = MaxDiff(j, u, i) > 20 && gj[i] > gu[i] + 12 && tj[i] > 15;
            var near = Img.Dilate(bas, 3);
            for (int i = 0; i < N; i++) if (bas[i] || (tj[i] > 25 && near[i])) votes[i]++;
        }
        var m = new bool[N];
        for (int i = 0; i < N; i++) m[i] = votes[i] * 2 >= samples.Count;

        // The strokes under the US text, by the JP frames alone.
        var off = new float[N];
        foreach (int f in new[] { c.Lo, c.Lo + 1, c.Hi - 1, c.Hi })
            if (jp[f] is { } cf) { var t = Img.TopHat(cf); for (int i = 0; i < N; i++) off[i] = Math.Max(off[i], t[i]); }
        var near4 = Img.Dilate(m, 4);
        var col = new List<float>(tops.Count);
        for (int i = 0; i < N; i++)
        {
            if (m[i] || !near4[i]) continue;
            col.Clear();
            foreach (var t in tops) col.Add(t[i]);
            if (Median(col) - off[i] > 25) m[i] = true;
        }

        // The pieces of the block.
        var (lab, n) = Img.Label(Img.Dilate(m, 4));
        if (n == 0) return false;
        var size = new int[n + 1];
        for (int i = 0; i < N; i++) if (m[i]) size[lab[i]]++;
        for (int i = 0; i < N; i++) m[i] = m[i] && size[lab[i]] >= MinPiece;
        var rows = new int[H];
        for (int i = 0; i < N; i++) if (m[i]) rows[i / W]++;
        var (y0, y1) = Run(rows, 8);
        if (y1 < y0) return false;
        var cols = new int[W];
        for (int y = y0; y <= y1; y++) for (int x = 0; x < W; x++) if (m[y * W + x]) cols[x]++;
        var (x0, x1) = Run(cols, 24);
        if (x1 < x0) return false;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (y < y0 - 4 || y > y1 + 4 || x < x0 - 4 || x > x1 + 4) m[y * W + x] = false;
        c.M0 = m; c.M = Img.Dilate(m, 1); c.M2 = Img.Dilate(m, 2); c.Near4 = Img.Dilate(m, 4);
        c.X0 = x0; c.X1 = x1; c.Y0 = y0; c.Y1 = y1;

        // How strongly each frame shows it, and how much of the US text is on each.
        int len = c.Hi - c.Lo + 1;
        var level = new float[len]; var usCount = new int[len];
        int cnt = 0; foreach (var v in m) if (v) cnt++;
        for (int f = c.Lo; f <= c.Hi; f++)
        {
            var j = jp[f]; var u = us[f];
            if (j == null || u == null) continue;
            var gj = Img.Gray(j); var gu = Img.Gray(u); var tu = Img.TopHat(gu);
            double s = 0; int uc = 0;
            for (int i = 0; i < N; i++)
            {
                if (m[i]) s += Math.Max(0, gj[i] - gu[i]);
                if (MaxDiff(j, u, i) > 20 && gu[i] > gj[i] + 12 && tu[i] > 15) uc++;
            }
            level[f - c.Lo] = (float)(s / Math.Max(1, cnt));
            usCount[f - c.Lo] = uc;
        }
        var edge = new List<float>();
        foreach (int f in new[] { c.Lo, c.Lo + 1, c.Lo + 2, c.Hi - 2, c.Hi - 1, c.Hi }) edge.Add(level[Math.Clamp(f - c.Lo, 0, len - 1)]);
        float bas0 = Median(edge);
        var mid = new List<float>();
        foreach (int f in samples) mid.Add(level[f - c.Lo]);
        float peak = Median(mid);
        c.Strength = new float[len];
        for (int k = 0; k < len; k++) c.Strength[k] = Math.Clamp((level[k] - bas0) / Math.Max(1e-6f, peak - bas0), 0, 1);

        // The US text where it is fullest.
        int most = 0; foreach (var v in usCount) most = Math.Max(most, v);
        c.UsShows = new float[len];
        var uvotes = new int[N]; int full = 0;
        for (int k = 0; k < len; k++)
        {
            c.UsShows[k] = most > 0 ? usCount[k] / (float)most : 0;
            if (most == 0 || usCount[k] < 0.7 * most) continue;
            var j = jp[c.Lo + k]; var u = us[c.Lo + k];
            if (j == null || u == null) continue;
            var gj = Img.Gray(j); var gu = Img.Gray(u); var tu = Img.TopHat(gu);
            for (int i = 0; i < N; i++) if (MaxDiff(j, u, i) > 20 && gu[i] > gj[i] + 12 && tu[i] > 15) uvotes[i]++;
            full++;
        }
        var uts = new bool[N];
        for (int i = 0; i < N; i++) uts[i] = full > 0 && uvotes[i] * 2 >= full;
        c.Uts = Img.Dilate(uts, 1);
        return true;
    }

    /// <summary>The run of rows or columns holding the most (counts under a tenth of
    /// the biggest, or 3, left out; gaps up to <paramref name="gap"/> bridged).</summary>
    static (int From, int To) Run(int[] counts, int gap)
    {
        int max = 0; foreach (var v in counts) max = Math.Max(max, v);
        int min = Math.Max(3, (int)Math.Ceiling(0.1 * max));
        int bestTot = 0, bestFrom = 0, bestTo = -1, i = 0, n = counts.Length;
        while (i < n)
        {
            if (counts[i] < min) { i++; continue; }
            int j = i, last = i, tot = 0;
            while (j < n && j - last <= gap)
            {
                if (counts[j] >= min) { last = j; tot += counts[j]; }
                j++;
            }
            if (tot > bestTot) { bestTot = tot; bestFrom = i; bestTo = last; }
            i = last + 1;
        }
        return (bestFrom, bestTo);
    }

    /// <summary>Every frame of the card with the English in, as sectors; null when a
    /// newer run took over.</summary>
    static Dictionary<int, byte[]>? Build(Card c, Letters l, Frames jp, Frames us, int run)
    {
        var o = new Dictionary<int, byte[]>();
        for (int f = c.Lo; f <= c.Hi; f++)
        {
            if (run != _run) return null;
            float st = c.Strength[f - c.Lo];
            if (st <= 0.02f) continue;                     // the Japanese doesn't show: the frame as it is
            var bits = jp.Index.Bits(f);
            if (bits == null) continue;
            var j = Mdec.Decode(bits, out int qs, out int ver);
            var u = us[f];
            if (j == null || u == null) continue;
            var img = Clean(c, f, j, u, jp);
            Img.Compose(img, l.Ink, l.W, l.H, l.X, l.Y, st, l.Level);
            var enc = Mdec.Fit(Img.ToBytes(img), jp.Index.Room(f), qs, ver);
            if (enc == null) { Console.WriteLine($"[jpdisc] {c.Key} frame {f}: does not fit"); continue; }
            foreach (var (k, v) in jp.Index.Sectors(f, enc)) o[k] = v;
        }
        return o;
    }

    const int MinPiece = 4;           // pixels: a piece of the Japanese text this small is still kept (an ellipsis dot)
    const float Stray = 18;           // a thin mark this much brighter or darker than around it, in what was filled

    static double[] Clean(Card c, int f, byte[] j, byte[] u, Frames jp)
    {
        var gj = Img.Gray(j); var gu = Img.Gray(u); var tu = Img.TopHat(gu); var tj = Img.TopHat(gj);
        int bx0 = Math.Max(0, c.X0 - 6), bx1 = Math.Min(W - 1, c.X1 + 6), by0 = Math.Max(0, c.Y0 - 6), by1 = Math.Min(H - 1, c.Y1 + 6);
        bool InBox(int i) { int x = i % W, y = i / W; return x >= bx0 && x <= bx1 && y >= by0 && y <= by1; }

        // The US text on this frame; also its own strokes where the Japanese text is,
        // where the two texts share pixels and the twins agree: a stroke in the US
        // frame there as bright as this frame's Japanese text (7/10 of its median), so
        // a dim line of the scene across the text is not taken for one.
        var lit = new List<float>();
        for (int i = 0; i < N; i++) if (c.M0[i] && tj[i] > 15) lit.Add(gj[i]);
        float level = 255;
        if (lit.Count > 20) { lit.Sort(); level = lit[lit.Count / 2]; }
        var ub = new bool[N];
        for (int i = 0; i < N; i++) ub[i] = (MaxDiff(j, u, i) > 10 && gu[i] > gj[i] + 6 && tu[i] > 10) || (c.M[i] && tu[i] > 22 && gu[i] > 0.7f * level);
        var near = Img.Dilate(ub, 2);
        for (int i = 0; i < N; i++) ub[i] = ub[i] || (tu[i] > 30 && near[i]);
        var ut = Img.Dilate(ub, 1);
        if (c.UsShows[f - c.Lo] > 0.03f) for (int i = 0; i < N; i++) ut[i] |= c.Uts[i];

        // The Japanese text, grown two pixels, and this frame's own Japanese strokes
        // near it that its twin doesn't have.
        var jx = new bool[N];
        for (int i = 0; i < N; i++) jx[i] = c.Near4[i] && InBox(i) && gj[i] > gu[i] + 12 && tj[i] > 12 && !ub[i];
        var mm = Img.Dilate(jx, 1);
        for (int i = 0; i < N; i++) mm[i] |= c.M2[i];

        var img = new double[N * 3];
        for (int i = 0; i < N * 3; i++) img[i] = j[i];
        var known = new bool[N];
        for (int i = 0; i < N; i++) known[i] = !mm[i] && !ut[i] && MaxDiff(j, u, i) < 40;
        var mean = new Img.Integral[3];
        for (int ch = 0; ch < 3; ch++) { int k = ch; mean[ch] = new Img.Integral(i => j[3 * i + k] - u[3 * i + k], i => known[i]); }
        var rest = new bool[N];
        var covered = new bool[N];
        var fromUs = new bool[N];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                covered[i] = mm[i] || ut[i];
                if (!mm[i]) continue;
                if (ut[i]) { rest[i] = true; continue; }
                fromUs[i] = true;
                for (int ch = 0; ch < 3; ch++)
                {
                    var (s, n) = mean[ch].Box(x - 7, y - 7, x + 8, y + 8);
                    double offset = n > 0 ? s / n : 0;
                    img[3 * i + ch] = Math.Clamp(u[3 * i + ch] + offset, 0, 255);
                }
            }
        var filled = (bool[])rest.Clone();
        var near0 = new List<int>();
        for (int g = c.First - 12; g < c.First; g++) if (Math.Abs(g - f) <= 40) near0.Add(g);
        for (int g = c.Last + 1; g <= c.Last + 12; g++) if (Math.Abs(g - f) <= 40) near0.Add(g);
        near0.Sort((p, q) => Math.Abs(p - f).CompareTo(Math.Abs(q - f)));
        foreach (int g in near0)
        {
            bool left = false;
            for (int i = 0; i < N && !left; i++) left = rest[i];
            if (!left) break;
            if (jp[g] is { } cleanFrame) Img.Temporal(j, covered, img, rest, cleanFrame, bx0, bx1, by0, by1);
        }
        Img.Diffuse(img, rest, bx0, bx1, by0, by1);

        // The last stray marks: a thin bright or dark mark in what was filled, grown a
        // pixel, filled again from around it.
        var zone = Img.Dilate(mm, 1);
        var go = Img.Gray(img); var th = Img.TopHat(go); var bh = Img.BlackHat(go);
        var stray = new bool[N];
        bool anyStray = false;
        for (int i = 0; i < N; i++) stray[i] = zone[i] && InBox(i) && filled[i] && (th[i] > Stray || bh[i] > Stray);
        stray = Img.Dilate(stray, 1);
        for (int i = 0; i < N; i++)
        {
            stray[i] = stray[i] && zone[i] && InBox(i) && !fromUs[i];
            if (stray[i]) { anyStray = true; filled[i] = true; }
        }
        if (anyStray) Img.Diffuse(img, stray, bx0, bx1, by0, by1);

        Grain(img, filled, gj, mm, ut, InBox, f);
        return img;
    }

    /// <summary>Noise on what was filled in, as strong as the Japanese frame's own
    /// (its high frequencies just around the texts, by their median), new each frame
    /// as the Japanese frames' is, so a smoothed patch doesn't stand out as flat.</summary>
    static void Grain(double[] img, bool[] filled, float[] gj, bool[] mm, bool[] ut, Func<int, bool> inBox, int f)
    {
        bool any = false;
        foreach (var v in filled) if (v) { any = true; break; }
        if (!any) return;
        var blur = Img.Blur(gj, 1.0);
        var cover = new bool[N];
        for (int i = 0; i < N; i++) cover[i] = mm[i] || ut[i];
        var ring = Img.Dilate(cover, 4);
        var dev = new List<float>();
        for (int i = 0; i < N; i++) if (ring[i] && !cover[i] && inBox(i)) dev.Add(Math.Abs(gj[i] - blur[i]));
        double sd = 3;
        if (dev.Count > 50) { dev.Sort(); sd = 1.4826 * dev[dev.Count / 2]; }
        var rnd = new Random(f);
        var noise = new float[N];
        for (int i = 0; i < N; i++)
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            noise[i] = (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
        noise = Img.Blur(noise, 0.6);
        double sq = 0;
        for (int i = 0; i < N; i++) sq += noise[i] * noise[i];
        double scale = sd / Math.Max(1e-6, Math.Sqrt(sq / N));
        for (int i = 0; i < N; i++)
        {
            if (!filled[i]) continue;
            double v = noise[i] * scale;
            for (int ch = 0; ch < 3; ch++) img[3 * i + ch] = Math.Clamp(img[3 * i + ch] + v, 0, 255);
        }
    }

    // ---- the English, laid out on the main thread ----

    /// <summary>Lay out each worked-out card's English when it has none or its text
    /// changed (the Text mod's fonts are the main thread's).</summary>
    public static void Letter()
    {
        if (!TextApi.Ready) return;
        Card[] cards;
        lock (_lock) cards = Cards.ToArray();
        bool changed = false;
        foreach (var c in cards)
        {
            if (!c.Ready) continue;
            string? t = TextApi.GetString("movie:" + c.Key);
            if (t == c.Text) continue;
            if (t == null) { c.Letters = null; c.Text = null; c.Version++; changed = true; continue; }
            int width = Math.Clamp(Math.Max(c.X1 - c.X0, 240), 64, Math.Max(64, W - c.X0 - 12));
            var cov = TextApi.Coverage(Style, t, width);
            if (cov == null) return;
            var (ink, iw, ih) = cov.Value;
            int y = Math.Clamp((c.Y0 + c.Y1) / 2 - ih / 2, 10, Math.Max(10, H - 10 - ih));
            c.Letters = new Letters(ink, iw, ih, c.X0, y, Ink);
            c.Text = t;
            c.Version++;
            changed = true;
        }
        if (changed) _wake.Set();
    }

    /// <summary>Letter every card again (the font or brightness changed).</summary>
    public static void Redraw()
    {
        lock (_lock) foreach (var c in Cards) c.Text = "\u0000";
    }

    // ---- the frames ----

    /// <summary>A sector's 2048 bytes of user data with the English in, or null to
    /// read it as it is.</summary>
    public static byte[]? Patched(int jpLba)
    {
        lock (_lock) return _sectors.TryGetValue(jpLba, out var u) ? u : null;
    }

    public static int Kept { get { lock (_lock) return _sectors.Count; } }
}
