using System;
using System.Collections.Generic;
using System.IO;
using RecompOne.Runtime.Cdrom;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// What the encyclopedia reads for its models off the disc itself, once, the first time it shows one:
///
/// - `COM\MO.T` -- the models. Entry n is creature ID n (and 128 on are objects).
///   An entry is the record the game's model table points at: `+4` the clip
///   count (0 for a rigid model), `+8` the TMD's offset, `+0xC` the keyframe
///   table's, `+0x10` the clip table's.
/// - `COM\RTIM.T` -- the textures. Entries 0-7 are the areas' sets, each a run of
///   pairs: a 16x1 CLUT, then the 4-bit image it colours, every block with its
///   own VRAM rect. The creature and NPC part of VRAM (y 256-511) is written by
///   these sets only; entries 10 on are the map's region sets and stay in y 0-255.
/// - `COM\FDAT.T` -- entry 3a+1 of area a starts with its creature types (120
///   bytes each, byte +0 the creature ID) and then its 200 placed creatures (16
///   bytes each, byte +1 the type), which is how a model's home area is known.
///
/// Each archive is `u16` count, then `u16` start sectors (count + 1 of them).
/// </summary>
static class Disc
{
    sealed class Archive
    {
        public readonly byte[] Data;
        readonly int[] _starts;
        public Archive(byte[] data)
        {
            Data = data;
            int n = BitConverter.ToUInt16(data, 0);
            _starts = new int[n + 1];
            for (int i = 0; i <= n; i++) _starts[i] = BitConverter.ToUInt16(data, 2 + 2 * i) * 2048;
        }
        public int Count => _starts.Length - 1;
        public int Offset(int i) => _starts[i];
        public int Length(int i)
        {
            if (i < 0 || i >= Count) return 0;
            int end = Math.Min(_starts[i + 1], Data.Length);
            return Math.Max(end - _starts[i], 0);
        }
    }

    public readonly record struct Block(int X, int Y, int W, int H, int Offset)
    {
        public int Bytes => W * H * 2;
    }

    public readonly record struct Pair(Block Clut, Block Image);

    static Archive? _mo, _rtim, _fdat;
    static bool _tried;
    public static string Error { get; private set; } = "";

    /// <summary>Spawn counts per area, per creature ID.</summary>
    static readonly Dictionary<int, int>[] _spawns = new Dictionary<int, int>[8];
    static readonly List<Pair>?[] _pairs = new List<Pair>?[128];

    public static bool Ready => _mo != null;

    /// <summary>Read the three archives, once. False (with <see cref="Error"/>) if
    /// the disc could not be read.</summary>
    public static bool Load()
    {
        if (_tried) return _mo != null;
        _tried = true;
        try
        {
            string? path = DiscPath();
            if (path == null) { Error = "the disc image was not found"; return false; }
            using var disc = DiscFs.Open(path);
            var mo = new Archive(disc.ReadFile("CD/COM/MO.T"));
            var rtim = new Archive(disc.ReadFile("CD/COM/RTIM.T"));
            var fdat = new Archive(disc.ReadFile("CD/COM/FDAT.T"));
            _rtim = rtim;
            _fdat = fdat;
            for (int a = 0; a < 8; a++) _spawns[a] = ReadSpawns(a);
            _mo = mo;
            Console.WriteLine($"[encyclopedia] read the disc: {mo.Count} models, {rtim.Count} texture sets, from {path}");
            return true;
        }
        catch (Exception e)
        {
            Error = e.Message;
            Console.WriteLine($"[encyclopedia] reading the disc: {e}");
            return false;
        }
    }

    /// <summary>The port's configured disc, else a disc image on the command line.</summary>
    static string? DiscPath()
    {
        string p = RecompOne.Runtime.Runtime.CdPath;
        if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
        foreach (var a in Environment.GetCommandLineArgs())
        {
            string ext = Path.GetExtension(a).ToLowerInvariant();
            if ((ext is ".cue" or ".bin" or ".chd" or ".iso") && File.Exists(a)) return a;
        }
        return null;
    }

    /// <summary>An MO.T entry, or null if the disc has none there.</summary>
    public static byte[]? Model(int id)
    {
        if (_mo == null || id < 0 || id >= _mo.Count) return null;
        int n = _mo.Length(id);
        if (n < 0x20) return null;
        var bytes = new byte[n];
        Buffer.BlockCopy(_mo.Data, _mo.Offset(id), bytes, 0, n);
        return bytes;
    }

    // ---- where a model lives ----

    static Dictionary<int, int> ReadSpawns(int area)
    {
        var counts = new Dictionary<int, int>();
        var f = _fdat!;
        int at = f.Offset(3 * area + 1), len = f.Length(3 * area + 1);
        if (len < 8) return counts;
        var d = f.Data;
        int defBytes = (int)BitConverter.ToUInt32(d, at);
        int defs = at + 4;
        int chunk = defs + defBytes;
        if (defBytes <= 0 || chunk + 4 > at + len) return counts;
        int n = (int)BitConverter.ToUInt32(d, chunk);
        int recs = chunk + 4;
        if (n <= 0 || recs + n > at + len) return counts;
        for (int r = 0; r + 16 <= n; r += 16)
        {
            if (d[recs + r] == 0xFF) continue;
            int type = d[recs + r + 1];
            if (type >= 40 || type * 120 >= defBytes) continue;
            int id = d[defs + type * 120];
            if (id >= 0xFE) continue;
            counts[id] = counts.TryGetValue(id, out int k) ? k + 1 : 1;
        }
        return counts;
    }

    /// <summary>The areas a creature ID is placed in, most placements first.</summary>
    public static List<int> HomeAreas(int id)
    {
        var areas = new List<(int Area, int Count)>();
        for (int a = 0; a < 8; a++)
            if (_spawns[a] != null && _spawns[a].TryGetValue(id, out int k)) areas.Add((a, k));
        areas.Sort((x, y) => y.Count != x.Count ? y.Count.CompareTo(x.Count) : x.Area.CompareTo(y.Area));
        var list = new List<int>(areas.Count);
        foreach (var (a, _) in areas) list.Add(a);
        return list;
    }

    // ---- textures ----

    /// <summary>A set's CLUT and image pairs, in the order the game loads them: 0-7
    /// the areas', 10 on the map regions'.</summary>
    public static List<Pair> Pairs(int set)
    {
        if (_rtim == null || set < 0 || set >= _rtim.Count || set >= _pairs.Length) return [];
        if (_pairs[set] is { } cached) return cached;
        var list = new List<Pair>();
        var d = _rtim.Data;
        int p = _rtim.Offset(set), end = p + _rtim.Length(set);
        Block? clut = null;
        while (p + 16 <= end)
        {
            uint a = BitConverter.ToUInt32(d, p), b = BitConverter.ToUInt32(d, p + 4);
            uint a2 = BitConverter.ToUInt32(d, p + 8), b2 = BitConverter.ToUInt32(d, p + 12);
            int x = (int)(a & 0xFFFF), y = (int)(a >> 16), w = (int)(b & 0xFFFF), h = (int)(b >> 16);
            if (a != a2 || b != b2 || w == 0 || h == 0 || w == 0xFFFF || x >= 1024 || y >= 512) break;
            var blk = new Block(x, y, w, h, p + 16);
            if (blk.Offset + blk.Bytes > end) break;
            if (h == 1 && clut == null) clut = blk;
            else if (clut is { } cl)
            {
                list.Add(new Pair(cl, blk));
                clut = null;
            }
            p = blk.Offset + blk.Bytes;
        }
        _pairs[set] = list;
        return list;
    }

    public static ReadOnlySpan<byte> Bytes(Block b) =>
        _rtim == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_rtim.Data, b.Offset, b.Bytes);
}
