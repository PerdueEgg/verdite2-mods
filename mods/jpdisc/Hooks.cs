// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using Controller = RecompOne.Runtime.Hardware.Controller;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using RecompOne.Runtime.Sdk;

namespace Kf2.Mods.JpDisc;

/// <summary>
/// The movies' music and the ending's length, in the US OPEN.EXE and END.EXE.
///
/// ---- OPEN.EXE ----
///
/// func_80013DC8(n) plays \OP\OPn.S (0 the ASCII logo, 1 the From Software logo, 2
/// the prologue), fading the master volume in first, and ends a movie on its frame
/// count (0x43F for OP2) or a button, fading the master volume out after it. It
/// starts the stream with func_80014170. So the Japanese opening's music (the
/// second SEQ in OP\OP.D, played once, at song volume 0x40, as the Japanese
/// func_800134F0 plays it) starts there, and ends as the Japanese one does, before
/// the US fade (BeforeMasterVolume). At boot the US plays the three movies before it loads the title's sound,
/// so the music is played here rather than by the game's libsnd.
///
/// ---- END.EXE ----
///
/// func_80011CC4(n) plays \OP\EDn.S; it stores the frame count to stop at in
/// 0x80039A04 (0x6A2 for ED0) and starts the stream with func_80011EA0, and counts
/// the frames shown in 0x800399FC. For the Japanese ending the count is the
/// Japanese END.EXE's, 0x858, so its staff credits play out. Its music is OP\ED.D's
/// SEQ on repeat at 0x40; from frame 0x538 it is turned down a step a frame and
/// then played once more from the top at 0x40, as the Japanese END.EXE does. After
/// the Japanese ending, with the US ED1.S to follow, the last frame waits for the
/// music to end (or a button) before ED1.S starts; with ED1.S left out (the
/// default), the music plays on over the held last frame
/// (patches/EndingHold.cs holds it after the movie player's second call, which
/// still happens: the hook below only skips the movie).
/// </summary>
public sealed partial class JpDiscMod
{
    const uint OpenMovie = 0x80013DC8, OpenStream = 0x80014170, SetMasterVolume = 0x8001FD90;
    const uint OpenShown = 0x8003EA58, OpenStopAt = 0x8003EA60;
    const uint EndMovie = 0x80011CC4, EndStream = 0x80011EA0;
    const uint EndStopAt = 0x80039A04, EndShown = 0x800399FC;
    const int JpEndFrames = 0x858, JpEndFade = 0x538, SongVolume = 0x40;

    static int _openMovie = -1, _endMovie = -1;
    static bool _openMusic, _endMusic, _endAgain;

    // ---- OPEN.EXE ----

    [PreHook("open", Address = OpenMovie)]
    static void BeforeOpenMovie(CpuContext c, IMemory m)
    {
        _openMovie = (int)c.A0;
        try { Music.KeepReverb(); } catch { }
    }

    [PreHook("open", Address = OpenStream)]
    static void BeforeOpenStream(CpuContext c, IMemory m)
    {
        if (_openMovie != 2 || !Swapped(Opening) || _opd == null) return;
        try
        {
            var bank = SoundBank.Of(_opd, 1);
            if (bank == null) { Music.Status = "the Japanese OP.D has no opening music"; return; }
            _openMusic = Music.Start(bank.Value.Head, bank.Value.Body, bank.Value.Seq, 1, SongVolume);
            if (_openMusic) Console.WriteLine("[jpdisc] the opening's music");
        }
        catch (Exception e) { Console.WriteLine($"[jpdisc] the opening's music: {e.Message}"); }
    }

    /// <summary>The movie player's fade-out, after the last frame (SsSetMVol, the
    /// first call once the music is playing; the fade-in comes before it starts):
    /// first the Japanese ending of the music, as JP func_800134F0 does it, 90 VSyncs
    /// of the last note ringing out unless a button ended the movie, then the song
    /// turned down a step a VSync from 0x40 and stopped. The US fade then runs as
    /// it does, over nothing.</summary>
    [PreHook("open", Address = SetMasterVolume)]
    static void BeforeMasterVolume(CpuContext c, IMemory m)
    {
        if (!_openMusic || !Music.Playing) return;
        _openMusic = false;
        uint a0 = c.A0, a1 = c.A1, a2 = c.A2, a3 = c.A3, v0 = c.V0, v1 = c.V1;
        try
        {
            bool skipped = m.ReadU32(OpenShown) + 2 < m.ReadU32(OpenStopAt);
            if (!skipped) for (int k = 0; k < 90 && Music.Playing; k++) { c.A0 = 0; LibEtc.VSync(c, m); }
            for (int v = Music.Volume(); v >= 0 && Music.Playing; v--)
            {
                Music.SetVolume(v);
                c.A0 = 0;
                LibEtc.VSync(c, m);
            }
        }
        finally
        {
            Music.Stop();
            c.A0 = a0; c.A1 = a1; c.A2 = a2; c.A3 = a3; c.V0 = v0; c.V1 = v1;
        }
    }

    [PostHook("open", Address = OpenMovie)]
    static void AfterOpenMovie(CpuContext c, IMemory m)
    {
        if (_openMusic) Music.Stop();
        _openMusic = false;
        _openMovie = -1;
    }

    // ---- END.EXE ----

    [PreHook("end", Address = EndMovie)]
    static bool BeforeEndMovie(CpuContext c, IMemory m)
    {
        _endMovie = (int)c.A0;
        // ED1.S left out: no movie, the Japanese ending's last frame stays up
        if (_endMovie == 1 && Swapped(Ending) && !_usCredits)
        {
            Console.WriteLine("[jpdisc] the US ending's second part left out");
            return false;
        }
        return true;
    }

    [PreHook("end", Address = EndStream)]
    static void BeforeEndStream(CpuContext c, IMemory m)
    {
        if (_endMovie != 0 || !Swapped(Ending)) return;
        m.WriteU32(EndStopAt, JpEndFrames);
        if (_edd == null) return;
        try
        {
            var bank = SoundBank.Of(_edd, 0);
            if (bank == null) { Music.Status = "the Japanese ED.D has no music"; return; }
            Music.ApplyReverb();
            _endMusic = Music.Start(bank.Value.Head, bank.Value.Body, bank.Value.Seq, 0, SongVolume);
            _endAgain = false;
            if (_endMusic) Console.WriteLine("[jpdisc] the ending's music");
        }
        catch (Exception e) { Console.WriteLine($"[jpdisc] the ending's music: {e.Message}"); }
    }

    const int RingVSyncs = 180, MostVSyncs = 60 * 60 * 10;

    /// <summary>After the Japanese ending, with the US part to follow: the last
    /// frame stays up until the music has played to its end and its last notes
    /// have rung out (about 3 seconds), or until a button is pressed. A press
    /// has to be seen going down, so a button still held from skipping the movie
    /// does not count. Then the music is faded out quickly, and ED1.S plays with
    /// its own sound. A movie ended early by a button goes straight to the fade.</summary>
    [PostHook("end", Address = EndMovie)]
    static void AfterEndMovie(CpuContext c, IMemory m)
    {
        if (_endMovie != 0 || !_endMusic) return;
        if (!_usCredits) return;                     // it plays on over the held frame
        uint v0 = c.V0, a0 = c.A0;
        try
        {
            bool skipped = (int)m.ReadU32(EndShown) + 2 < JpEndFrames;
            if (!skipped)
            {
                Console.WriteLine("[jpdisc] holding the last frame for the music; any button goes on");
                bool released = false;
                int rung = 0;
                for (int k = 0; k < MostVSyncs && Music.Playing; k++)
                {
                    c.A0 = 0;
                    LibEtc.VSync(c, m);
                    bool down = Controller.State != 0xFFFF;
                    if (!down) released = true;
                    else if (released) break;
                    if (Music.Done && ++rung >= RingVSyncs) break;
                }
            }
            for (int v = Music.Volume(); v >= 0 && Music.Playing; v -= 2)
            {
                Music.SetVolume(v);
                c.A0 = 0;
                LibEtc.VSync(c, m);
            }
        }
        finally
        {
            Music.Stop();
            _endMusic = false;
            c.V0 = v0; c.A0 = a0;
        }
    }

    /// <summary>Each VSync: the ending's music turned down from frame 0x538 and
    /// started once more, as the Japanese END.EXE does it.</summary>
    static void EndingMusic()
    {
        if (!_endMusic || _endAgain || !Music.Playing || _endMovie != 0) return;
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return;
        int shown = (int)m.ReadU32(EndShown);
        if (shown < JpEndFade || shown > JpEndFrames + 8) return;
        int v = SongVolume - (shown - JpEndFade);
        if (v > 0) { Music.SetVolume(v); return; }
        Music.Restart(1, SongVolume);
        _endAgain = true;
    }
}

/// <summary>A VAB and SEQs, as OP\OP.D and OP\ED.D hold them: OP.D its title
/// pictures (TIMs) first, then the VAB header, the VAB body and two SEQs (the
/// title's and the opening's); ED.D the VAB header, the body and one SEQ.</summary>
static class SoundBank
{
    /// <summary>The VAB and the SEQ number <paramref name="seq"/> (from 0) after it.</summary>
    public static (byte[] Head, byte[] Body, byte[] Seq)? Of(byte[] d, int seq)
    {
        int p = 0;
        while (p + 8 <= d.Length && BitConverter.ToUInt32(d, p) == 0x10)       // TIMs: id, flags, then blocks
        {
            uint flag = BitConverter.ToUInt32(d, p + 4);
            int q = p + 8;
            if ((flag & 8) != 0) q += (int)BitConverter.ToUInt32(d, q);
            q += (int)BitConverter.ToUInt32(d, q);
            if (q <= p || q > d.Length) return null;
            p = q;
        }
        if (p + 32 > d.Length || d[p] != 'p' || d[p + 1] != 'B' || d[p + 2] != 'A' || d[p + 3] != 'V') return null;
        int blocks = 0;
        for (int k = 0; k < 128; k++) if (d[p + 32 + k * 16] > 0) blocks++;
        int sizes = p + 32 + 128 * 16 + blocks * 512, headLen = sizes + 512 - p;
        int bodyLen = 0;
        for (int k = 0; k < 256; k++) bodyLen += BitConverter.ToUInt16(d, sizes + 2 * k) * 8;
        int body = p + headLen, s = body + bodyLen;
        var seqs = new List<int>();
        for (int k = s; k + 4 <= d.Length; k++)
            if (d[k] == 'p' && d[k + 1] == 'Q' && d[k + 2] == 'E' && d[k + 3] == 'S') seqs.Add(k);
        if (seq >= seqs.Count || body + bodyLen > d.Length) return null;
        int from = seqs[seq], to = seq + 1 < seqs.Count ? seqs[seq + 1] : d.Length;
        return (d[p..body], d[body..(body + bodyLen)], d[from..to]);
    }
}
