using System;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.CursorMemory;

/// <summary>
/// King's Field 3 style menu cursors: the Items page and the Magic page open on what you
/// last had highlighted, and every Equipment sub menu opens on what is equipped.
///
/// ---- how the pages keep their cursor ----
///
/// Every scrolling list in the in-game menu has a descriptor that
/// `func_80022A78` fills in, then zeroes the cursor in:
///
///     +0x1E  rows in the list          +0x21  the highlighted entry
///     +0x1F  rows shown at once        +0x22  its row in the window
///     +0x20  the entry at the window's top      (+0x21 - +0x20)
///
/// and `func_8001EB70(desc, ids, &state, &choice)` moves it, once a frame, wrapping
/// at both ends. Each page builds its rows first and then runs that loop:
///
///     Items         func_80019204   rows by func_80019444, ids at S4;
///                                   step called from 0x80019348
///     Magic         func_8001A04C   the support spells, spell numbers 14..19, built
///                                   by func_8001A1E8 (return 0x8001A09C);
///                                   step from 0x8001A170
///     Attack magic  func_8001AB48   spells 0..13 and a "none" row (0xFF), built by
///                                   func_8001A1E8 (return 0x8001AB9C);
///                                   step from 0x8001AD08
///     Gear          func_8001A6E8   weapon and armour, ids at S3, preview call
///                                   0x8001A8FC, step from 0x8001A994
///     Slot item     func_8001AD84   the second slot: item rows (ids at S7) then spell
///                                   rows (spell ids at frame + 0x908); preview call
///                                   0x8001AF68, step from 0x8001B004
///
/// So the page always opens on entry 0. The cursor is remembered the way the
/// pages themselves know their rows -- by id, the item or spell number -- so it
/// stays on the same thing when the list changes (a spell learned, an item used
/// up); if the id is gone it stays on the same row number, which is the next
/// entry.
///
/// ---- when ----
///
/// * Recorded after every step of the Items and Magic pages.
/// * Restored before the page's first step. The Items page also shows the
///   highlighted item's preview model, which it loads once while it sets up, with
///   `func_80022CAC(ids[cursor])` (return 0x800192A8) -- that is the moment the
///   cursor is set and the call is pointed at the remembered item, so the preview
///   matches. The magic pages have no preview; their first step is early enough.
///
/// The window keeps the row it had when the page was closed where it can, so the
/// list looks as you left it.
///
/// ---- the Equipment sub menus ----
///
/// The Equipment page (func_8001A2DC) opens the gear pages by kind (the slot each
/// writes: 0 weapon 0x801994AF, 3, 4, 2, 5, 6, 7, 8 the armour and accessory bytes
/// 0x801994D4..DA), the attack magic page (the spell the Cast button uses is the
/// byte 0x801994AB, read by func_8002957C) and the second slot's page (an item in
/// 0x801994AD or a spell in 0x801994AC, 0xFF for none; setting one clears the
/// other -- func_80025FA8 and func_80025F80). Each of those opens with the
/// highlight on the row whose id is the one equipped. A slot with nothing in it
/// leaves the highlight on the first row, as the game does. These do not remember
/// anything: the Equipment pages open on what is equipped, wherever you left off.
/// </summary>
public sealed class CursorMemoryMod : IMod
{
    const int Items = 0, Magic = 1, Attack = 2;
    const uint AttackSpellByte = 0x801994AB, SlotSpellByte = 0x801994AC, SlotItemByte = 0x801994AD;
    static readonly string[] Keys = ["kf2.cursormemory.items", "kf2.cursormemory.magic"];
    static readonly string[] Names = ["Remember the cursor on the Items page", "Remember the cursor on the Magic page"];
    const string EquippedKey = "kf2.cursormemory.equipped";

    static bool _equipped = true;
    static readonly bool[] _on = [true, true];
    static readonly bool[] _has = new bool[2];
    static readonly int[] _id = new int[2], _entry = new int[2], _diff = new int[2];

    static readonly bool[] _fresh = new bool[3];       // the page opened and has not stepped yet
    static uint _magicIds, _attackIds;                 // the builder's id arrays
    static uint _stepDesc, _stepIds;                   // the arguments of the step running now

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        for (int i = 0; i < 2; i++) _on[i] = view.GetBool(Keys[i], true);
        _equipped = view.GetBool(EquippedKey, true);
    }

    // ---- a page opening ----

    [PreHook("game", Address = 0x8001A04C)]
    static bool MagicOpens(CpuContext c, IMemory m) { _fresh[Magic] = true; return true; }

    [PreHook("game", Address = 0x8001AB48)]
    static bool AttackOpens(CpuContext c, IMemory m) { _fresh[Attack] = true; return true; }

    [PreHook("game", Address = 0x8001A1E8)]
    static bool Builder(CpuContext c, IMemory m)
    {
        if (c.RA == 0x8001A09C) _magicIds = c.A3;
        else if (c.RA == 0x8001AB9C) _attackIds = c.A3;
        return true;
    }

    // ---- pages that show a preview: set the cursor where it is loaded ----

    [PreHook("game", Address = 0x80022CAC)]
    static bool ItemPreview(CpuContext c, IMemory m)
    {
        uint desc = c.SP + 0x18;                       // the page's own frame
        uint ids;
        int at;
        switch (c.RA)
        {
            case 0x800192A8:                           // Items
                ids = c.S4;
                at = Restore(Items, m, desc, ids);
                break;
            case 0x8001A8FC:                           // gear
                ids = c.S3;
                at = _equipped ? Focus(m, desc, ids, GearSlot(m, (int)c.S6)) : -1;
                break;
            case 0x8001AF68:                           // the second slot
                ids = c.S7;
                at = _equipped ? FocusSlot(m, desc, ids, c.SP + 0x908) : -1;
                break;
            default:
                return true;
        }
        if (at >= 0) c.A0 = m.ReadU8(ids + (uint)at);
        return true;
    }

    // ---- the cursor step, every frame of every list ----

    [PreHook("game", Address = 0x8001EB70)]
    static bool BeforeStep(CpuContext c, IMemory m)
    {
        // The arguments, for the post hook: a called function is free to change A0..A3.
        _stepDesc = c.A0;
        _stepIds = c.A1;
        int k = c.RA == 0x8001A170 ? Magic : c.RA == 0x8001AD08 ? Attack : -1;
        if (k < 0 || !_fresh[k]) return true;
        _fresh[k] = false;
        if (k == Magic) Restore(Magic, m, c.A0, _magicIds);
        else if (_equipped) Focus(m, c.A0, _attackIds, m.ReadU8(AttackSpellByte));
        return true;
    }

    [PostHook("game", Address = 0x8001EB70)]
    static void AfterStep(CpuContext c, IMemory m)
    {
        uint ra = c.RA;
        if (ra == 0x80019348) Record(Items, m, _stepDesc, _stepIds);
        else if (ra == 0x8001A170) Record(Magic, m, _stepDesc, _magicIds);
    }

    static void Record(int k, IMemory m, uint desc, uint ids)
    {
        if (!_on[k] || ids == 0) return;
        int n = m.ReadU8(desc + 0x1E);
        int cur = m.ReadU8(desc + 0x21);
        if (n == 0 || cur >= n) return;
        _has[k] = true;
        _id[k] = m.ReadU8(ids + (uint)cur);
        _entry[k] = cur;
        _diff[k] = m.ReadU8(desc + 0x22);
    }

    static int GearSlot(IMemory m, int kind)
    {
        switch (kind)
        {
            case 0: return m.ReadU8(0x801994AF);
            case 3: return m.ReadU8(0x801994D4);
            case 4: return m.ReadU8(0x801994D5);
            case 2: return m.ReadU8(0x801994D6);
            case 5: return m.ReadU8(0x801994D7);
            case 6: return m.ReadU8(0x801994D8);
            case 7: return m.ReadU8(0x801994D9);
            case 8: return m.ReadU8(0x801994DA);
            default: return 0xFF;
        }
    }

    /// <summary>The second slot: the equipped item's row, else the equipped spell's.</summary>
    static int FocusSlot(IMemory m, uint desc, uint itemIds, uint spellIds)
    {
        int at = Focus(m, desc, itemIds, m.ReadU8(SlotItemByte));
        if (at >= 0) return at;
        return Focus(m, desc, spellIds, m.ReadU8(SlotSpellByte));
    }

    /// <summary>Puts the cursor on the row whose id is `id`. Returns the row, or -1 when
    /// nothing is equipped (0xFF) or the id is not in the list.</summary>
    static int Focus(IMemory m, uint desc, uint ids, int id)
    {
        if (id == 0xFF || ids == 0) return -1;
        int n = m.ReadU8(desc + 0x1E);
        int at = -1;
        for (int i = 0; i < n; i++)
            if (m.ReadU8(ids + (uint)i) == id) { at = i; break; }
        if (at < 0) return -1;
        Place(m, desc, n, at, (Math.Max(1, (int)m.ReadU8(desc + 0x1F)) - 1) / 2);
        return at;
    }

    /// <summary>Writes the cursor: the entry, the window's top and the entry's row in it.</summary>
    static void Place(IMemory m, uint desc, int n, int at, int row)
    {
        int visible = Math.Max(1, (int)m.ReadU8(desc + 0x1F));
        int scroll = at - Math.Min(row, visible - 1);
        scroll = Math.Clamp(scroll, 0, Math.Max(0, n - visible));
        if (at >= scroll + visible) scroll = at - visible + 1;
        m.WriteU8(desc + 0x21, (byte)at);
        m.WriteU8(desc + 0x20, (byte)scroll);
        m.WriteU8(desc + 0x22, (byte)(at - scroll));
    }

    /// <summary>Puts the list's cursor on the remembered entry. Returns the entry, or -1.</summary>
    static int Restore(int k, IMemory m, uint desc, uint ids)
    {
        if (!_on[k] || !_has[k] || ids == 0) return -1;
        int n = m.ReadU8(desc + 0x1E);
        if (n == 0) return -1;

        int at = -1;
        for (int i = 0; i < n; i++)
            if (m.ReadU8(ids + (uint)i) == _id[k]) { at = i; break; }
        if (at < 0) at = Math.Min(_entry[k], n - 1);

        Place(m, desc, n, at, _diff[k]);
        return at;
    }

    public void DrawSettings()
    {
        ImGui.TextWrapped("The Items and Magic pages open on the entry you last had highlighted. The "
                        + "Equipment sub menus (weapon, armour, accessories, attack magic and the second slot) "
                        + "open on what is equipped.");
        ImGui.Separator();
        for (int i = 0; i < 2; i++)
        {
            if (ImGui.Checkbox(Names[i], ref _on[i]))
            {
                RecompOne.Runtime.Runtime.View.SetBool(Keys[i], _on[i]);
                RecompOne.Runtime.Runtime.SaveView();
            }
        }
        if (ImGui.Checkbox("Equipment sub menus open on what is equipped", ref _equipped))
        {
            RecompOne.Runtime.Runtime.View.SetBool(EquippedKey, _equipped);
            RecompOne.Runtime.Runtime.SaveView();
        }
    }
}
