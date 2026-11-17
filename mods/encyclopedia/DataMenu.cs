using System;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using KingsField2 = Recompiled.KingsField2_game;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// ENCYCLOPEDIA on the in-game menu's DATA page, between QUIT and RETURN.
///
/// The DATA page is func_8001B35C: a fixed option list, group 1 of the table at
/// 0x80064CD4 (0x134 a group: a header record, then rows of x, y and up to 24
/// font indices, 0x1C each). It steps its cursor with func_8001EA14(cursor, 2,
/// &selected, &confirmed, &cancelled) -- confirming the last row is a cancel,
/// which is RETURN -- and draws with func_800208D8(1, 3, cursor, confirmed).
/// Both counts are constants in its body, so the page is run here instead, the
/// same loop with one more row: LOAD and QUIT call the game's own screens
/// exactly as it does, and ENCYCLOPEDIA runs the encyclopedia inside the open
/// menu. The table's fourth row (the SAVE screen's header, which other pages
/// read) is lent for RETURN only for the length of each draw, so nothing else
/// ever sees it changed. The port's menu pointer measures the rows from that
/// same draw, so the mouse sees four rows too.
/// </summary>
public sealed partial class EncyclopediaMod
{
    const uint DataPage = 0x8001B35C;
    const uint DataGroup = 0x80064CD4 + 1 * 0x134;
    const uint RowSize = 0x1C;
    const uint SeqVolume = 0x80198578;                         // s16, the sequence QUIT fades out
    const int Pending = unchecked((int)0xFFFFFF9D);            // "nothing chosen yet", the menus' -99

    [PreHook("game", Address = DataPage)]
    static bool BeforeDataPage(CpuContext c, IMemory m)
    {
        var snap = c.Snapshot();
        int result;
        try { result = RunDataPage(c, m); }
        catch (Exception e)
        {
            Console.WriteLine($"[encyclopedia] DATA page: {e} -- the game's own page instead");
            c.Restore(snap);
            return true;
        }
        c.Restore(snap);
        c.V0 = (uint)result;
        return false;
    }

    /// <summary>func_8001B35C with a fourth row.</summary>
    static int RunDataPage(CpuContext c, IMemory m)
    {
        c.SP -= 0x100;
        uint sp = c.SP;
        uint sel = sp + 0x50, confirmed = sp + 0x54, result = sp + 0x58;
        m.WriteU32(sel, 0xFFFFFFFF);
        m.WriteU32(confirmed, 0);
        m.WriteU32(result, unchecked((uint)Pending));
        uint cursor = 0;

        while (true)
        {
            int s = (int)m.ReadU32(sel);
            if (s != -1 || (int)m.ReadU32(result) != Pending) Call(c, m, KingsField2.func_80022EFC);

            if (s == 0)
            {
                uint v = Call(c, m, KingsField2.func_8001B4F4);       // LOAD
                m.WriteU32(result, v != 0 ? v : 0xFFFFFFFD);
            }
            else if (s == 1)
            {
                uint v = Call(c, m, KingsField2.func_8001B9C8);       // QUIT
                m.WriteU32(result, v != 0 ? v : 0xFFFFFFFE);
            }
            else if (s == 2)
            {
                RunSession(c, m, inMenu: true);                       // ENCYCLOPEDIA
                c.SP = sp;
            }

            if (s != -1 && (int)m.ReadU32(result) == -1) m.WriteU32(result, unchecked((uint)Pending));   // back from a screen
            if ((int)m.ReadU32(result) != Pending) break;

            m.WriteU32(sp + 0x10, result);                            // the fifth argument, on the stack
            cursor = Call(c, m, KingsField2.func_8001EA14, cursor, 3, sel, confirmed);
            for (int pass = 0; pass < 2; pass++)
            {
                Call(c, m, KingsField2.func_80022530);
                DrawDataRows(c, m, cursor, m.ReadU32(confirmed));
                Call(c, m, KingsField2.func_800226A8);
            }
        }

        int r = (int)m.ReadU32(result);
        if (r == -2) FadeForQuit(c, m);
        return r;
    }

    /// <summary>func_800208D8(1, 4, ...) with rows 3 and 4 lent for the draw.</summary>
    static void DrawDataRows(CpuContext c, IMemory m, uint cursor, uint confirmed)
    {
        uint row3 = DataGroup + 3 * RowSize, row4 = DataGroup + 4 * RowSize;   // record 0 is the header: 1 LOAD, 2 QUIT, 3 RETURN, 4 the SAVE header
        var saved = new byte[2 * RowSize];
        for (uint i = 0; i < saved.Length; i++) saved[i] = m.ReadU8(row3 + i);
        try
        {
            for (uint i = 0; i < RowSize; i++) m.WriteU8(row4 + i, saved[i]);          // RETURN, a row lower
            m.WriteU16(row4 + 2, (ushort)(m.ReadU16(row3 + 2) + 26));
            WriteText(m, row3 + 4, L("ENCYCLOPEDIA"), 24);                                 // in RETURN's place
            Call(c, m, KingsField2.func_800208D8, 1, 4, cursor, confirmed);
        }
        finally
        {
            for (uint i = 0; i < saved.Length; i++) m.WriteU8(row3 + i, saved[i]);
        }
    }

    static MethodInfo? _seqSetVol;
    static bool _seqLooked;

    /// <summary>The tail of func_8001B35C on QUIT: thirty frames, then the music
    /// down. SsSeqSetVol is a library routine, reached by name.</summary>
    static void FadeForQuit(CpuContext c, IMemory m)
    {
        for (int i = 0; i < 30; i++) Call(c, m, KingsField2.func_8005FCC8, 0);
        if (!_seqLooked)
        {
            _seqLooked = true;
            _seqSetVol = typeof(KingsField2).GetMethod("SsSeqSetVol_game", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (_seqSetVol == null) Console.WriteLine("[encyclopedia] SsSeqSetVol not found; QUIT leaves the music up");
        }
        if (_seqSetVol == null) return;
        c.A0 = (uint)(short)m.ReadU16(SeqVolume); c.A1 = 0; c.A2 = 0; c.RA = FakeRa;
        try { _seqSetVol.Invoke(null, [c, m]); }
        catch (Exception e) { Console.WriteLine($"[encyclopedia] SsSeqSetVol: {e.InnerException?.Message ?? e.Message}"); }
    }
}
