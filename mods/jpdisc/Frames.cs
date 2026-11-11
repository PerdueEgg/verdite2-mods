// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using RecompOne.Runtime.Cdrom;

namespace Kf2.Mods.JpDisc;

/// <summary>The Japanese image, read under one lock by the port's stream thread and
/// the mod's own.</summary>
static class Jp
{
    public static IDiscImage? Image;
    public static readonly object Lock = new();

    public static byte[] Read(int lba, int size)
    {
        lock (Lock) return Image!.ReadSectorData(lba, size);
    }

    public static byte[] Raw(int lba)
    {
        lock (Lock) return Image!.ReadRawSector(lba);
    }
}

/// <summary>
/// Where each frame of an STR movie is, so a frame can be read back by number.
///
/// An STR video sector, as the 2336-byte view the port reads (subheader first):
/// +2 the submode (bit 2 set for XA sound), +8 magic 0x0160, +12 the sector's index
/// in its frame, +14 the frame's sector count, +16 the frame number (from 1), +20
/// the frame's bitstream bytes, +28 a copy of the MDEC header, and the 2016 bytes
/// of the bitstream from +40. A frame's last sectors can carry little or nothing.
/// </summary>
sealed class FrameIndex
{
    public const int Payload = 2016;

    public sealed class Frame
    {
        public int No, N, Len;
        public int[] Lbas = [];
    }

    public readonly Dictionary<int, Frame> Frames = new();
    public int Last;
    readonly Func<int, byte[]> _read;

    FrameIndex(Func<int, byte[]> read) { _read = read; }

    /// <summary>Every complete frame of the movie at these sectors.</summary>
    public static FrameIndex Scan(Func<int, byte[]> read2336, int lba, int count)
    {
        var x = new FrameIndex(read2336);
        var parts = new Dictionary<int, (int N, int Len, int[] Lbas, int Got)>();
        for (int k = 0; k < count; k++)
        {
            var s = read2336(lba + k);
            if (!IsVideo(s)) continue;
            int idx = s[12] | s[13] << 8, n = s[14] | s[15] << 8;
            int no = (int)BitConverter.ToUInt32(s, 16);
            if (n <= 0 || idx >= n) continue;
            if (!parts.TryGetValue(no, out var p)) p = (n, (int)BitConverter.ToUInt32(s, 20), new int[n], 0);
            if (p.Lbas[idx] == 0) { p.Lbas[idx] = lba + k; p.Got++; }
            parts[no] = p;
            if (p.Got == p.N)
            {
                x.Frames[no] = new Frame { No = no, N = p.N, Len = p.Len, Lbas = p.Lbas };
                x.Last = Math.Max(x.Last, no);
            }
        }
        return x;
    }

    public static bool IsVideo(byte[] s) => (s[2] & 4) == 0 && (s[8] | s[9] << 8) == 0x0160;

    /// <summary>The frame's bitstream, or null when the movie has no such frame.</summary>
    public byte[]? Bits(int no)
    {
        if (!Frames.TryGetValue(no, out var f)) return null;
        var b = new byte[Math.Min(f.Len, f.N * Payload)];
        for (int i = 0; i < f.N; i++)
        {
            int take = Math.Min(Payload, b.Length - i * Payload);
            if (take <= 0) break;
            var s = _read(f.Lbas[i]);
            Buffer.BlockCopy(s, 40, b, i * Payload, take);
        }
        return b;
    }

    /// <summary>The frame decoded to RGB, or null.</summary>
    public byte[]? Rgb(int no) => Bits(no) is { } b ? Mdec.Decode(b, out _) : null;

    /// <summary>A frame's sectors carrying a new bitstream: each one's 2048 bytes of
    /// user data, by sector, its STR header kept but for the frame's byte count and
    /// the copy of the MDEC header.</summary>
    public Dictionary<int, byte[]> Sectors(int no, byte[] enc)
    {
        var o = new Dictionary<int, byte[]>();
        var f = Frames[no];
        for (int i = 0; i < f.N; i++)
        {
            var s = _read(f.Lbas[i]);
            var u = new byte[2048];
            Buffer.BlockCopy(s, 8, u, 0, 2048);
            BitConverter.TryWriteBytes(u.AsSpan(12), (uint)enc.Length);
            Buffer.BlockCopy(enc, 0, u, 20, 8);
            Array.Clear(u, 32, Payload);
            int at = i * Payload;
            if (at < enc.Length) Buffer.BlockCopy(enc, at, u, 32, Math.Min(Payload, enc.Length - at));
            o[f.Lbas[i]] = u;
        }
        return o;
    }

    /// <summary>The room the frame has: its sectors' payload.</summary>
    public int Room(int no) => Frames.TryGetValue(no, out var f) ? f.N * Payload : 0;
}
