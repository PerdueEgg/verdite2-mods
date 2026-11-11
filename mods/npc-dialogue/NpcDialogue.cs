using System;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using KingsField2 = Recompiled.KingsField2_game;

namespace Kf2.Mods.NpcDialogue;

/// <summary>
/// King's Field 3 style conversations: while a line is up, Interact goes straight
/// to the NPC's next line, and Cancel ends the conversation.
///
/// ---- how a conversation runs ----
///
/// Talking to an NPC runs `func_80047000(npc)`, called from the use handler
/// `func_800489FC` and from a few area modules. It is a small script interpreter:
///
///     rec = *(0x8017265C + npc[2] * 0x78)    // the NPC's script record, 0x70 first
///     rec[0x10]                              // the script cursor, KEPT between talks
///     rec[0x12] >> 4                         // what follows the talk: 0..3 a menu
///                                            // (shop, fortuneteller...), else nothing
///     rec[0x1] / rec[0x11]                   // the talk and the return animation
///
/// A byte below 0xF0 is a line: `func_80035B48(3, rec[0xC] + byte)` shows it, from
/// the call site whose return address is 0x800472C0. The 0xF? bytes are commands
/// (flags, jumps, an area event, `F4 n` = "n more commands in this talk"). The
/// talk animation plays once before the first line (0x80047290 / 0x800472AC, both
/// `func_80046DE4`), and the return animation once after the last
/// (0x80047408). A talk shows one line and leaves the cursor after it, which is
/// why talking again says the next one -- and why the last line repeats: the
/// script ends by jumping back to it.
///
/// ---- what this mod does ----
///
/// Talking again IS the next line, so the mod simply talks again, right away:
///
/// 1. When a line closes (`func_80035B48` returns to 0x800472C0), it checks which
///    button closed it. The message closes on any button press, so the pad is
///    sampled where the closing fade starts (`func_800356F4`, return address
///    0x80035C6C) -- the fade is long enough for a quick tap to be over by the
///    time `func_80035B48` returns. Interact (the Cross mask at 0x8006E568), and
///    not Cancel (0x8006E56C), means "next line".
///    Only NPCs with nothing after the talk are chained: a shopkeeper's line is
///    followed by the shop, and must still be.
/// 2. When that talk ends (post hook on `func_80047000`), the mod calls
///    `func_80047000` again for the same NPC, and again for as long as Interact
///    closes the line. Cancel, or any other button, closes it the way the game
///    always did.
/// 3. No animations in between. The return animation of a talk that is about to
///    be followed by another is skipped (pre hook on `func_80046DE4`), and so are
///    the talk animations of the follow-up. The NPC stays in its talking pose
///    until the conversation really ends, and then the return animation plays
///    once.
///
/// Two loose ends the game's own flow leaves, both handled:
///
/// * The talk ends with `S2[0xC] = S5`, putting the NPC's animation id back to
///   what it was before the talk (S5, read as the talk animation starts). The
///   follow-up talks read that id after the first one changed it, so the last one
///   would put back the talking id. The idle id is remembered from the first talk
///   and written back at the end.
/// * Closing a line with Cancel (Circle), which also opens the in-game menu,
///   would open the menu a tick later: `func_80025D38`, run at the end of every
///   talk, strips Circle from this tick's pad word, so the next tick sees it as a
///   new press. If Cancel is still held when the conversation ends it is put
///   back in both pad words (0x80199554 now, 0x80199556 last tick), so it is not
///   new. Switchable, since it changes what Cancel does after a conversation.
/// </summary>
public sealed class NpcDialogueMod : IMod
{
    const uint PadNow = 0x80199554, PadLast = 0x80199556;
    const uint CrossMask = 0x8006E568, CancelMask = 0x8006E56C;

    const string SkipKey = "kf2.npcdialogue.skipanims";
    const string GuardKey = "kf2.npcdialogue.guardmenu";

    static bool _skipAnims = true;
    static bool _guardMenu = true;

    static bool _in;          // inside a conversation (the outermost call to func_80047000)
    static bool _looping;     // the chained calls below are running
    static bool _cont;        // the call running now is a chained one
    static bool _chain;       // the last line was closed by Interact: talk again
    static bool _lineOpen;    // one of the NPC's lines is up
    static ushort _closeHeld; // the pad, as the line began to close
    static uint _npc;         // the NPC entity
    static bool _haveIdle;
    static byte _idle, _talk; // the animation ids before the talk and during it
    static bool _returned;    // the last talk played its return animation
    static bool _didChain;
    static bool _menuless;    // the conversation has no menu after it

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _skipAnims = view.GetBool(SkipKey, true);
        _guardMenu = view.GetBool(GuardKey, true);
    }

    // ---- the conversation ----

    [PreHook("game", Address = 0x80047000)]
    static bool TalkBegins(CpuContext c, IMemory m)
    {
        if (_looping) return true;           // one of the chained calls
        _in = true;
        _cont = false;
        _chain = false;
        _lineOpen = false;
        _haveIdle = false;
        _returned = false;
        _didChain = false;
        _menuless = false;
        _npc = c.A0;
        return true;
    }

    [PostHook("game", Address = 0x80047000)]
    static void TalkEnds(CpuContext c, IMemory m)
    {
        if (_looping || !_in) return;
        _looping = true;
        var snap = c.Snapshot();
        try
        {
            // 1000 is a guard, not a limit anyone reaches: every pass needs a press.
            for (int guard = 0; _chain && guard < 1000; guard++)
            {
                _chain = false;
                _cont = true;
                _didChain = true;
                c.A0 = _npc;
                KingsField2.func_80047000(c, m);
            }

            // The last talk put the NPC's animation id back to what it read when
            // it began -- the talking id, once a chained talk ran. Put back the
            // one from before the first.
            if (_didChain && _returned && _haveIdle && m.ReadU8(_npc + 0xC) == _talk)
                m.WriteU8(_npc + 0xC, _idle);

            if (_guardMenu && _menuless) KeepCancelHeld(c, m);
        }
        finally
        {
            c.Restore(snap);
            _looping = false;
            _in = false;
            _cont = false;
            _chain = false;
            _lineOpen = false;
        }
    }

    // ---- a line closing ----

    [PreHook("game", Address = 0x80035B48)]
    static bool LineOpens(CpuContext c, IMemory m)
    {
        // The call site in func_80047000 that shows the NPC's line. The fortuneteller's
        // (0x800473AC) and every other message are not touched.
        if (_in && c.RA == 0x800472C0)
        {
            _lineOpen = true;
            _closeHeld = 0;
        }
        return true;
    }

    [PreHook("game", Address = 0x800356F4)]
    static bool FadeBegins(CpuContext c, IMemory m)
    {
        // func_80035B48's closing fade (the opening one is 0x80035C24). The button
        // that closed the line is still down here; a few frames later it may not be.
        if (_lineOpen && c.RA == 0x80035C6C) _closeHeld = Held(c, m);
        return true;
    }

    [PostHook("game", Address = 0x80035B48)]
    static void LineCloses(CpuContext c, IMemory m)
    {
        if (!_lineOpen || c.RA != 0x800472C0) return;
        _lineOpen = false;

        ushort cross = (ushort)m.ReadU32(CrossMask);
        ushort cancel = (ushort)m.ReadU32(CancelMask);

        // S1 is the NPC's script record here, as in func_80047000. Its +0x12 high
        // nibble 0..3 opens a menu once the talk is over (shops, the fortuneteller);
        // a line in front of one must not be chained over.
        bool menuless = (m.ReadU8(c.S1 + 0x12) & 0xF0) >= 0x40;
        if (menuless) _menuless = true;

        _chain = menuless
              && cross != 0
              && (_closeHeld & cross) != 0
              && (_closeHeld & cancel) == 0;
    }

    // ---- animations ----

    [PreHook("game", Address = 0x80046DE4)]
    static bool Animate(CpuContext c, IMemory m)
    {
        if (!_in) return true;
        uint ra = c.RA;

        // The talk animation (0x800472AC), and finishing the one in progress before
        // it (0x80047290). S5 holds the NPC's animation id as it was before.
        if (ra == 0x80047290 || ra == 0x800472AC)
        {
            if (!_haveIdle && !_cont)
            {
                _haveIdle = true;
                _idle = (byte)c.S5;
                _talk = m.ReadU8(c.S1 + 1);
            }
            return !(_cont && _skipAnims);
        }

        // The return animation. Before another talk it is skipped; the line after it
        // writes S5 back to the animation id, so S5 is set to the id the NPC has now.
        if (ra == 0x80047408)
        {
            if (_chain && _skipAnims)
            {
                c.S5 = m.ReadU8(_npc + 0xC);
                return false;
            }
            _returned = true;
        }
        return true;
    }

    // ---- the pad ----

    /// <summary>The buttons down now, as the game's own PadRead(1) says (active high).</summary>
    static ushort Held(CpuContext c, IMemory m)
    {
        var snap = c.Snapshot();
        try
        {
            c.A0 = 1;
            KingsField2.PadRead_game(c, m);
            return (ushort)c.V0;
        }
        finally { c.Restore(snap); }
    }

    /// <summary>If Cancel is still down, make it "not new" in both pad words, so
    /// the world tick that follows does not read it as a press and open the menu.</summary>
    static void KeepCancelHeld(CpuContext c, IMemory m)
    {
        ushort cancel = (ushort)m.ReadU32(CancelMask);
        ushort cross = (ushort)m.ReadU32(CrossMask);
        if (cancel == 0 || cancel == cross) return;
        if ((Held(c, m) & cancel) == 0) return;
        m.WriteU16(PadNow, (ushort)(m.ReadU16(PadNow) | cancel));
        m.WriteU16(PadLast, (ushort)(m.ReadU16(PadLast) | cancel));
    }

    public void DrawSettings()
    {
        ImGui.TextWrapped("While an NPC's line is up, Interact (Cross) goes straight to their next line, and "
                        + "Cancel (Circle) ends the conversation. At the end of their dialogue Interact repeats "
                        + "the last line until you cancel. NPCs that open a menu after talking (shops, the "
                        + "fortuneteller) are left as they were.");
        ImGui.Separator();

        if (ImGui.Checkbox("No animations between lines", ref _skipAnims))
            Persist(SkipKey, _skipAnims);
        ImGui.TextWrapped("The NPC stays in their talking pose until the conversation ends, then the return "
                        + "animation plays once. Off: every line plays both animations, as separate talks do.");

        if (ImGui.Checkbox("Cancel does not open the menu", ref _guardMenu))
            Persist(GuardKey, _guardMenu);
        ImGui.TextWrapped("The game closes a line on any button, and Cancel is also the menu button, so ending "
                        + "a conversation with it would open the menu a moment later.");
    }

    static void Persist(string key, bool value)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }
}
