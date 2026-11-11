// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ImGuiNET;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;
using KingsField2 = Recompiled.KingsField2_game;

namespace Kf2.Mods.Shortcuts;

/// <summary>
/// The game's Select shortcut, five times over.
///
/// ---- the game's own shortcut ----
///
/// The Equipment page (func_8001A2DC) opens the shortcut list from its row 9:
/// func_8001AD84. That page lists the inventory (func_80019444) and then every
/// learned spell, 0..19, attack and support alike (func_8001A1E8(.., 0, 0x13)),
/// then a "remove" row, with two parallel id arrays on its frame: a spell id per
/// row at sp+0x908 and an item id per row at sp+0x8B8, 0xFF where a row is not
/// one. After the Yes prompt the chosen row is u32 sp+0x95C, and the page's last
/// act is two setters:
///
///     func_80025F80(spellIds[row])   // u8 0x801994AC = spell; clears the item if not 0xFF
///     func_80025FA8(itemIds[row])    // u8 0x801994AD = item;  clears the spell if not 0xFF
///
/// called from 0x8001B080 and 0x8001B098 (return addresses 0x8001B088, 0x8001B0A0).
///
/// Select is read in player control, func_8002957C, as a just-pressed test of
/// the action-mask word 0x8006E578 against the pad words 0x80199554 (this
/// frame) and 0x80199556 (last frame):
///
///     if (spell != 0xFF) func_80027DC0(spell);          // the cast: MP, gauge, every guard
///     if (item != 0xFF) {
///         if (count[item] == 0)        func_80033F08(0x14);       // none left
///         else if (func_80029560(item)) func_800197D4(item);      // 71..80: used on yourself
///         else                         func_800474D0(.., item);   // used on what is in front
///     }
///
/// ---- what this mod adds ----
///
/// Assigning: the spell setter is hooked when it comes from the shortcut page.
/// Before it runs, a second list of the game's own kind (stepped by
/// func_8001EB70, drawn by func_800209E0, as the Encyclopedia's pages are) asks
/// which shortcut the pick is for. Shortcut 1 lets both setters run, so the
/// game's own Select slot is set exactly as before; one of the other four stores
/// the pick in this mod and skips both setters; backing out changes nothing.
///
/// Using: a shortcut's key or pad button latches a press. At the next
/// func_8002957C Select is pressed on the game's behalf for that one call; for
/// shortcuts 2-5 their spell or item is first put into the Select slot, and
/// afterwards both the slot and the pad words are put back. So the game's own
/// Select code does the work and its rules all hold. Shortcut 1 is the game's
/// Select itself: its key only presses Select, so it needs no rebinding in the
/// game's controls.
///
/// The d-pad: the pad's d-pad is the game's walk and turn (the runtime binds it
/// to the PS1 d-pad). A d-pad direction given to a shortcut is taken out of the
/// pad word in the world, before player control reads it; menus still scroll
/// with it, and the sticks are untouched.
///
/// ---- text ----
///
/// Every label is drawn with the game's own label routine, func_80021E10, as a
/// record (s16 x, s16 y, font cells to 0xFF). Names are copied cell for cell out
/// of the game's name slots (items 0x80065B24, spells 0x80066664, 24 bytes each),
/// never decoded and re-encoded. So whatever another mod does to that routine or
/// to those slots applies here as it does to the game's own menus: the Text
/// mod's proportional spacing, normal case and shadows, and the Retranslation
/// mod's names in its lowercase cells. This mod's own words are encoded by the
/// Text mod when it is loaded and its lowercase is in the font, and in the
/// disc's capitals otherwise. The Text mod is looked up at each call (TextApi),
/// never required.
///
/// The Equipment page copies its ten row labels (24-byte slots from 0x80065674)
/// into a buffer on its frame and draws them from there (func_800209E0 called
/// from 0x8001A4A8). With keyboard labels, row 9 -- the Select button's,
/// 0x8006574C -- is written as HOTKEYS in that buffer before each draw; with
/// controller labels it is written back from the slot.
///
/// ---- saving ----
///
/// Shortcuts 2-5 are kept beside the card saves as shortcuts_slotN.txt, written
/// on a card save (func_80023764) and read on a card load (func_80023638
/// returning 0), as the Encyclopedia keeps its record. A new game (game_entry)
/// and a death with no Seath's Tear (the reset from 0x8002B064) clear them, as
/// they clear the character.
/// </summary>
public sealed class ShortcutsMod : IMod
{
    // ---- game addresses ----
    const uint PadNow = 0x80199554, PadLast = 0x80199556;
    const uint SelectMask = 0x8006E578;                      // u32 action-mask entry 4
    const uint MaskUp = 0x8006E590, MaskDown = 0x8006E594, MaskLeft = 0x8006E59C, MaskRight = 0x8006E598;
    const uint SlotSpell = 0x801994AC, SlotItem = 0x801994AD;
    const uint MaxHp = 0x80199426;                           // u16, 0 until a game is loaded
    const uint Quantity = 0x8006E5C8;                        // u32 Left/Right counter func_8001EB70 steps
    const uint ItemNames = 0x80065B24, SpellNames = 0x80066664, NameStride = 0x18;
    const uint SelectLabel = 0x8006574C;                     // the Equipment page's "■ BUTTON"
    const uint RemoveLabel = 0x8006577C;                     // the shortcut list's empty row, "REMOVE"
    const int ItemCount = 120, SpellCount = 20;

    const uint SpellSetterRet = 0x8001B088;                  // func_8001AD84 -> func_80025F80
    const uint ItemSetterRet = 0x8001B0A0;                   // func_8001AD84 -> func_80025FA8
    const uint FrameRow = 0x95C, FrameSpells = 0x908, FrameItems = 0x8B8;
    const uint EquipListDraw = 0x8001A4B0;                   // func_8001A2DC -> func_800209E0
    const int EquipRows = 10, SelectRow = 9;

    const uint TextFont = 0x80064BF0;                        // func_80021E10's template
    const uint SaveSlot = 0x8006E5D4;                        // u8 1..3
    const uint DeathReset = 0x8002B064;                      // func_8002A550 -> func_80025B4C
    const uint FakeRa = 0x8001FFF4;

    const int Count = 5;                                     // shortcut 1 (Select) and 2..5
    const byte None = 0xFF, End = 0xFF, Space = 0x7F;
    const int MaxCells = 23;

    // ---- settings ----
    const string LabelsKey = "kf2.shortcuts.labels";
    const string DpadKey = "kf2.shortcuts.dpad";
    static string KeyKey(int i) => $"kf2.shortcuts.key{i + 1}";
    static string PadKey(int i) => $"kf2.shortcuts.pad{i + 1}";

    // SDL GameControllerButton indices, the encoding HostWindow.IsPadButtonDown takes.
    static readonly (string Name, string Label, int Button)[] PadButtons =
    [
        ("None", "-", -1),
        ("D-pad Up", "Up", 11),
        ("D-pad Down", "Down", 12),
        ("D-pad Left", "Left", 13),
        ("D-pad Right", "Right", 14),
        ("L3 (left stick click)", "L3", 7),
        ("R3 (right stick click)", "R3", 8),
        ("Misc (share / mute)", "Misc", 15),
    ];

    static readonly Key[] DefaultKeys = [Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5];
    static readonly int[] DefaultPads = [-1, 11, 12, 13, 14];   // shortcut 1 is on the pad's own Select already

    static readonly Key[] _keys = new Key[Count];
    static readonly int[] _pads = new int[Count];             // index into PadButtons
    static bool _keyboardLabels = true;
    static bool _dpadTakeover = true;

    // ---- the shortcuts (index 0, Select, is the game's own and unused here) ----
    struct Slot { public byte Spell, Item; }
    static readonly Slot[] _slots = new Slot[Count];
    static string _lastFile = "";
    static int _loadSlot;

    // ---- input ----
    static readonly bool[] _held = new bool[Count];
    static readonly int[] _latch = new int[Count];
    const int LatchFrames = 8;
    static bool _inMenu;
    static int _capture = -1;                                 // a shortcut waiting for a key in the settings

    // ---- the press made on the game's behalf ----
    static bool _injected, _swapped;
    static ushort _savedNow, _savedLast;
    static byte _savedSpell, _savedItem;

    // ---- the picker ----
    static int _decision = -2;                                // -2 no picker ran, -1 back, 0 Select, 1..4
    static int _lastPick;
    static IMemory? _mem;                                     // for names in the settings panel

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _keyboardLabels = view.GetString(LabelsKey, "keyboard") != "controller";
        _dpadTakeover = view.GetBool(DpadKey, true);
        for (int i = 0; i < Count; i++)
        {
            string k = view.GetString(KeyKey(i), DefaultKeys[i].ToString());
            _keys[i] = Enum.TryParse<Key>(k, out var key) ? key : Key.Unknown;
            int pad = (int)view.GetFloat(PadKey(i), DefaultPads[i]);
            _pads[i] = 0;
            for (int p = 0; p < PadButtons.Length; p++)
                if (PadButtons[p].Button == pad) _pads[i] = p;
        }
        ClearSlots();
        Event.AddListener(_onVSync);
    }

    public void OnUnload() => Event.RemoveListener(_onVSync);

    static void ClearSlots()
    {
        for (int i = 0; i < Count; i++) _slots[i] = new Slot { Spell = None, Item = None };
    }

    static bool Typing => ImGui.GetCurrentContext() != nint.Zero && ImGui.GetIO().WantTextInput;

    // ================================================================
    // Input: latch each shortcut's press, once a frame
    // ================================================================

    static readonly Action<VSyncEvent> _onVSync = _ =>
    {
        bool typing = Typing;
        for (int i = 0; i < Count; i++)
        {
            var key = _keys[i];
            int pad = PadButtons[_pads[i]].Button;
            bool down = (!typing && key != Key.Unknown && HostWindow.IsKeyDown(key))
                     || (pad >= 0 && HostWindow.IsPadButtonDown(pad));
            if (_inMenu || _capture >= 0) _latch[i] = 0;
            else if (down && !_held[i]) _latch[i] = LatchFrames;
            else if (_latch[i] > 0) _latch[i]--;
            _held[i] = down;
        }
    };

    [PreHook("game", Address = 0x80018E80)]
    static bool BeforeMenu(CpuContext c, IMemory m) { _inMenu = true; return true; }

    [PostHook("game", Address = 0x80018E80)]
    static void AfterMenu(CpuContext c, IMemory m)
    {
        _inMenu = false;
        for (int i = 0; i < Count; i++) _latch[i] = 0;
    }

    // ================================================================
    // Using a shortcut: Select, pressed on the game's behalf
    // ================================================================

    [PreHook("game", Address = 0x8002957C)]
    static bool BeforeControl(CpuContext c, IMemory m)
    {
        _mem = m;
        if (m.ReadU16(MaxHp) == 0) return true;

        // A d-pad direction that is a shortcut is not also a step or a turn.
        if (_dpadTakeover)
        {
            ushort now = m.ReadU16(PadNow), was = now;
            for (int i = 0; i < Count; i++)
            {
                int b = PadButtons[_pads[i]].Button;
                if (b < 11 || b > 14 || !HostWindow.IsPadButtonDown(b)) continue;
                uint mask = b switch { 11 => MaskUp, 12 => MaskDown, 13 => MaskLeft, _ => MaskRight };
                now &= (ushort)~m.ReadU32(mask);
            }
            if (now != was) m.WriteU16(PadNow, now);
        }

        int fire = -1;
        for (int i = 0; i < Count; i++)
            if (_latch[i] > 0) { fire = i; break; }
        if (fire < 0) return true;
        for (int i = 0; i < Count; i++) _latch[i] = 0;

        // Shortcut 1 is the game's Select as it stands; 2-5 lend it their contents.
        _swapped = false;
        if (fire > 0)
        {
            var s = _slots[fire];
            if (s.Spell == None && s.Item == None) return true;
            _savedSpell = m.ReadU8(SlotSpell);
            _savedItem = m.ReadU8(SlotItem);
            m.WriteU8(SlotSpell, s.Spell);
            m.WriteU8(SlotItem, s.Spell != None ? None : s.Item);
            _swapped = true;
        }

        ushort sel = (ushort)m.ReadU32(SelectMask);
        _savedNow = m.ReadU16(PadNow);
        _savedLast = m.ReadU16(PadLast);
        m.WriteU16(PadNow, (ushort)(_savedNow | sel));
        m.WriteU16(PadLast, (ushort)(_savedLast & ~sel));
        _injected = true;
        return true;
    }

    [PostHook("game", Address = 0x8002957C)]
    static void AfterControl(CpuContext c, IMemory m)
    {
        if (!_injected) return;
        _injected = false;
        ushort sel = (ushort)m.ReadU32(SelectMask);
        m.WriteU16(PadNow, (ushort)((m.ReadU16(PadNow) & ~sel) | (_savedNow & sel)));
        m.WriteU16(PadLast, (ushort)((m.ReadU16(PadLast) & ~sel) | (_savedLast & sel)));
        if (_swapped)
        {
            m.WriteU8(SlotSpell, _savedSpell);
            m.WriteU8(SlotItem, _savedItem);
            _swapped = false;
        }
    }

    // ================================================================
    // The Equipment page's Select row says HOTKEYS with keyboard labels
    // ================================================================

    [PreHook("game", Address = 0x800209E0)]
    static bool BeforeListDraw(CpuContext c, IMemory m)
    {
        if (c.RA != EquipListDraw) return true;
        uint d = c.A0;
        if (m.ReadU8(d + 0x1E) != EquipRows || m.ReadU8(d + 0x23) != NameStride) return true;
        uint buf = m.ReadU32(d + 0x24);
        if (buf == 0) return true;
        byte[] cells = _keyboardLabels ? Word("hotkeys", "Hotkeys") : Cells(m, SelectLabel);
        WriteCells(m, buf + SelectRow * NameStride, cells);
        return true;
    }

    // ================================================================
    // Assigning: which shortcut, asked before the game's setters run
    // ================================================================

    [PreHook("game", Address = 0x80025F80)]
    static bool BeforeSetSpell(CpuContext c, IMemory m)
    {
        if (c.RA != SpellSetterRet) return true;
        _mem = m;
        uint sp = c.SP;
        uint row = m.ReadU32(sp + FrameRow);
        byte spell = (byte)c.A0;
        byte item = m.ReadU8(sp + FrameItems + row);
        if (m.ReadU8(sp + FrameSpells + row) != spell) item = None;   // not the frame we expect: be safe

        int choice;
        try { choice = RunPicker(c, m, spell, item); }
        catch (Exception e)
        {
            Console.WriteLine($"[shortcuts] {e}");
            choice = 0;                                   // fall back to the game's own behaviour
        }
        _decision = choice;
        if (choice >= 1 && choice < Count)
        {
            _slots[choice] = spell != None
                ? new Slot { Spell = spell, Item = None }
                : new Slot { Spell = None, Item = item };
        }
        return choice == 0;
    }

    [PreHook("game", Address = 0x80025FA8)]
    static bool BeforeSetItem(CpuContext c, IMemory m)
    {
        if (c.RA != ItemSetterRet) return true;
        bool run = _decision == 0 || _decision == -2;
        _decision = -2;
        return run;
    }

    // ---- the picker page ----

    const uint Scratch = 0x800;
    const uint OffDesc = 0x100, OffRows = 0x200, OffCounts = 0x300, OffIds = 0x400;
    const uint OffWords = 0x500, OffFlags = 0x700, OffBox = 0x740, OffRec = 0x760;
    static uint _base;

    /// <summary>-1 back, 0 the game's Select, 1..4 a shortcut of this mod.</summary>
    static int RunPicker(CpuContext c, IMemory m, byte spell, byte item)
    {
        var snap = c.Snapshot();
        uint quantity = m.ReadU32(Quantity);
        try
        {
            c.SP -= Scratch;
            _base = (c.SP + 0xF) & ~0xFu;
            for (uint i = 0; i < 0x100; i++)
            {
                m.WriteU8(_base + OffRows + i, 0xFF);
                m.WriteU8(_base + OffCounts + i, 0xFF);
                m.WriteU8(_base + OffIds + i, 0xFF);   // func_80022CAC(0xFF): no preview, as on a spell row
            }
            for (uint i = 0; i < 0x200; i += 4) m.WriteU32(_base + OffWords + i, 0xFFFFFFFF);

            byte[] what = spell != None && spell < SpellCount ? Cells(m, SpellNames + spell * NameStride)
                        : item != None && item < ItemCount ? Cells(m, ItemNames + item * NameStride)
                        : Cells(m, RemoveLabel);
            uint d = _base + OffDesc, confirmed = _base + OffFlags, result = confirmed + 4;
            InitDesc(m, d, Math.Clamp(_lastPick, 0, Count - 1));
            m.WriteU32(confirmed, 0);
            m.WriteU32(result, 0xFFFFFF9D);

            Frames(c, m, d, what);
            Call(c, m, KingsField2.func_80022EFC);            // let go of the Yes

            while (true)
            {
                Call(c, m, KingsField2.func_8001EB70, d, _base + OffIds, confirmed, result);
                if (m.ReadU32(result) == 0xFFFFFFFF)           // back (the step played its sound)
                {
                    Call(c, m, KingsField2.func_80022EFC);
                    return -1;
                }
                if (m.ReadU32(confirmed) != 0)
                {
                    int sel = m.ReadU8(d + 0x21);
                    _lastPick = sel;
                    Call(c, m, KingsField2.func_80022DC4, 0x11);
                    Call(c, m, KingsField2.func_80022EFC);
                    return sel;
                }
                Frames(c, m, d, what);
            }
        }
        finally
        {
            m.WriteU32(Quantity, quantity);
            c.Restore(snap);
        }
    }

    static void InitDesc(IMemory m, uint d, int sel)
    {
        for (uint i = 0; i < 0x40; i++) m.WriteU8(d + i, 0);
        m.WriteU16(d + 0x00, 17);                          // header box, where the game's pages put it
        m.WriteU16(d + 0x02, 19);
        WriteCells(m, d + 4, _keyboardLabels ? Word("hotkeys", "Hotkeys") : Word("shortcuts", "Shortcuts"));
        m.WriteU8(d + 0x1C, 42);                           // list left edge
        m.WriteU8(d + 0x1D, 39);                           // row 0 at 39 + 5
        m.WriteU8(d + 0x1E, Count);
        m.WriteU8(d + 0x1F, Count);
        m.WriteU8(d + 0x20, 0);
        m.WriteU8(d + 0x21, (byte)sel);
        m.WriteU8(d + 0x22, (byte)sel);
        m.WriteU8(d + 0x23, 1);                            // one 0xFF byte a row: the game draws no text
        m.WriteU32(d + 0x24, _base + OffRows);
        m.WriteU32(d + 0x28, _base + OffRows);
        m.WriteU32(d + 0x2C, _base + OffCounts);
        m.WriteU32(d + 0x30, _base + OffWords);
    }

    static void Frames(CpuContext c, IMemory m, uint d, byte[] what)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Call(c, m, KingsField2.func_80022530);         // frame head
            Call(c, m, KingsField2.func_800209E0, d, 1);   // header, panel, highlight, cursor
            DrawBox(c, m, what);
            DrawRows(c, m, d);
            Call(c, m, KingsField2.func_800226A8);         // present
        }
    }

    const int NameColumn = 63;    // pixels from the row's left: nine cells of the game's 7

    static void DrawRows(CpuContext c, IMemory m, uint d)
    {
        int x = m.ReadU8(d + 0x1C) + 5, y = m.ReadU8(d + 0x1D) + 5;
        for (int r = 0; r < Count; r++)
        {
            Draw(c, m, x, y + 14 * r, Word(null, Label(r)));
            Draw(c, m, x + NameColumn, y + 14 * r, Contents(m, r));
        }
    }

    // A second header box right of the title, with the pick's name centred in it.
    const int BoxX = 147, BoxY = 19, BoxWidth = 124;
    const uint HeaderBox = 0x80064C20;

    static void DrawBox(CpuContext c, IMemory m, byte[] cells)
    {
        uint rec = _base + OffBox;
        m.WriteU16(rec, BoxX);
        m.WriteU16(rec + 2, BoxY);
        m.WriteU8(rec + 4, End);
        Call(c, m, KingsField2.func_800218B4, HeaderBox, rec);
        int x = BoxX - 6 + Math.Max(0, (BoxWidth - Width(cells)) / 2);
        Draw(c, m, x, BoxY, cells);
    }

    /// <summary>One label through the game's own routine, so every mod that hooks
    /// it (spacing, case, shadows, translation) applies.</summary>
    static void Draw(CpuContext c, IMemory m, int x, int y, byte[] cells)
    {
        uint rec = _base + OffRec;
        m.WriteU16(rec, (ushort)x);
        m.WriteU16(rec + 2, (ushort)y);
        WriteCells(m, rec + 4, cells);
        Call(c, m, KingsField2.func_80021E10, TextFont, rec);
    }

    // ================================================================
    // Labels, names and font cells
    // ================================================================

    /// <summary>Row 0 is the game's Select; rows 1..4 are this mod's. Normal case:
    /// Word puts it in capitals when the font has no lowercase.</summary>
    static string Label(int row)
    {
        if (!_keyboardLabels) return row == 0 && _pads[0] == 0 ? "Select" : PadButtons[_pads[row]].Label;
        if (row == 0 && _keys[0] == Key.Unknown) return SelectKeyLabel();
        return KeyLabel(_keys[row]);
    }

    static string SelectKeyLabel()
    {
        foreach (var name in new[] { ConfigManager.Game.Keys.Select, ConfigManager.Game.Keys2.Select })
            if (!string.IsNullOrEmpty(name) && Enum.TryParse<Key>(name, out var k)) return KeyLabel(k);
        return "Select";
    }

    static string KeyLabel(Key k)
    {
        if (k == Key.Unknown) return "-";
        string n = k.ToString();
        if (n.StartsWith("Number") && n.Length == 7) return n.Substring(6);
        if (n.StartsWith("Keypad") && n.Length == 7) return "Num " + n.Substring(6);
        switch (n)
        {
            case "ShiftLeft": return "L Shift";
            case "ShiftRight": return "R Shift";
            case "ControlLeft": return "L Ctrl";
            case "ControlRight": return "R Ctrl";
            case "AltLeft": return "L Alt";
            case "AltRight": return "R Alt";
            case "Backspace": return "Bksp";
            case "CapsLock": return "Caps";
            case "GraveAccent": return "Tilde";
            case "Minus": return "-";
            case "Equal": return "=";
            case "Slash": return "/";
            case "Period": return ".";
            case "Comma": return ",";
            case "Apostrophe": return "'";
            case "Semicolon": return "Semi";
            case "LeftBracket": return "L Brkt";
            case "RightBracket": return "R Brkt";
            case "BackSlash": return "BSlash";
        }
        var s = new StringBuilder();
        foreach (char ch in n)
            if (char.IsLetterOrDigit(ch)) s.Append(ch);
        string t = s.ToString();
        return t.Length > 7 ? t.Substring(0, 7) : t;
    }

    static byte[] Contents(IMemory m, int row)
    {
        byte spell, item;
        if (row == 0) { spell = m.ReadU8(SlotSpell); item = m.ReadU8(SlotItem); }
        else { spell = _slots[row].Spell; item = _slots[row].Item; }
        if (spell != None && spell < SpellCount) return Cells(m, SpellNames + spell * NameStride);
        if (item != None && item < ItemCount) return Cells(m, ItemNames + item * NameStride);
        return Word("empty", "Empty");
    }

    /// <summary>A name slot's cells as they are in RAM, whoever wrote them, then 0xFF.</summary>
    static byte[] Cells(IMemory m, uint slot)
    {
        var b = new List<byte>((int)NameStride);
        for (uint i = 0; i < MaxCells; i++)
        {
            byte g = m.ReadU8(slot + i);
            if (g == End) break;
            b.Add(g);
        }
        b.Add(End);
        return b.ToArray();
    }

    static void WriteCells(IMemory m, uint at, byte[] cells)
    {
        int n = 0;
        for (; n < cells.Length && n < MaxCells && cells[n] != End; n++) m.WriteU8(at + (uint)n, cells[n]);
        m.WriteU8(at + (uint)n, End);
    }

    /// <summary>This mod's own word in the menu font. With the Text mod and its
    /// lowercase loaded, as written (and a translation can reword it through the
    /// Text mod's string table, key "shortcuts:" + <paramref name="key"/>);
    /// otherwise in the disc's capitals.</summary>
    static byte[] Word(string? key, string text)
    {
        if (TextApi.Present)
        {
            if (key != null && TextApi.GetString("shortcuts:" + key) is { Length: > 0 } custom) text = custom;
            if (TextApi.MenuReady)
            {
                var e = TextApi.MenuEncode(text, MaxCells);
                if (e.Length > 1) return e;
            }
        }
        return Capitals(text);
    }

    /// <summary>The disc's font: A-Z 0x00-0x19, 0-9 0x20-0x29,
    /// ". , ' - = / * # !" 0x30-0x38, "?" 0x3A, space 0x7F.</summary>
    const string Signs = ".,'-=/*#!";

    static byte[] Capitals(string s)
    {
        var b = new List<byte>(s.Length + 1);
        foreach (char raw in s.ToUpperInvariant())
        {
            if (raw >= 'A' && raw <= 'Z') b.Add((byte)(raw - 'A'));
            else if (raw >= '0' && raw <= '9') b.Add((byte)(0x20 + raw - '0'));
            else if (raw == ' ') b.Add(Space);
            else if (raw == '?') b.Add(0x3A);
            else if (Signs.IndexOf(raw) is int k && k >= 0) b.Add((byte)(0x30 + k));
            if (b.Count == MaxCells) break;
        }
        b.Add(End);
        return b.ToArray();
    }

    /// <summary>A cell's character: the Text mod's map when it is loaded (its
    /// lowercase and extra signs), the disc's otherwise; '\0' if unknown.</summary>
    static char CharOf(byte g)
    {
        if (g == Space) return ' ';
        if (TextApi.Present)
        {
            char c = TextApi.MenuChar(g);
            if (c != '\0') return c;
        }
        if (g < 26) return (char)('A' + g);
        if (g >= 0x20 && g <= 0x29) return (char)('0' + g - 0x20);
        if (g >= 0x30 && g < 0x30 + Signs.Length) return Signs[g - 0x30];
        if (g == 0x3A) return '?';
        return '\0';
    }

    static string Text(byte[] cells)
    {
        var s = new StringBuilder();
        foreach (byte g in cells)
        {
            if (g == End) break;
            char c = CharOf(g);
            s.Append(c == '\0' ? ' ' : c);
        }
        return s.ToString().Trim();
    }

    /// <summary>How wide the cells draw: the Text mod's menu spacing when it is
    /// loaded, with a name in the game's capitals measured as the Text mod draws it
    /// (its normal case); the game's 7 a letter otherwise.</summary>
    static int Width(byte[] cells)
    {
        int n = 0;
        while (n < cells.Length && cells[n] != End) n++;
        if (TextApi.Present && TextApi.MenuReady && TextApi.MenuPlace(TextApi.MenuShown(Text(cells))) is { } placed && placed.Width > 0)
            return placed.Width;
        return n * 7;
    }

    // ================================================================
    // Calls into the game
    // ================================================================

    static uint Call(CpuContext c, IMemory m, Action<CpuContext, IMemory> f,
                     uint a0 = 0, uint a1 = 0, uint a2 = 0, uint a3 = 0)
    {
        c.A0 = a0; c.A1 = a1; c.A2 = a2; c.A3 = a3;
        c.RA = FakeRa;
        f(c, m);
        return c.V0;
    }

    // ================================================================
    // Kept with each card save
    // ================================================================

    static string SlotPath(int slot) => Path.GetFullPath($"shortcuts_slot{slot}.txt");

    [PreHook("game", Address = 0x8004A628)]
    static bool OnGameStart(CpuContext c, IMemory m)
    {
        ClearSlots();
        _lastFile = "new game";
        return true;
    }

    [PreHook("game", Address = 0x80025B4C)]
    static bool BeforeCharacterReset(CpuContext c, IMemory m)
    {
        if (c.RA != DeathReset) return true;
        ClearSlots();
        _lastFile = "new game (died)";
        return true;
    }

    [PreHook("game", Address = 0x80023638)]
    static bool BeforeCardLoad(CpuContext c, IMemory m)
    {
        _loadSlot = (int)c.A0;
        return true;
    }

    [PostHook("game", Address = 0x80023638)]
    static void AfterCardLoad(CpuContext c, IMemory m)
    {
        if (c.V0 != 0) return;   // no file or a bad checksum: nothing was loaded
        ClearSlots();
        string path = SlotPath(_loadSlot);
        try
        {
            if (File.Exists(path))
            {
                // One line per shortcut 2..5: spell,item (-1 for none).
                var lines = File.ReadAllLines(path);
                for (int i = 1; i < Count && i - 1 < lines.Length; i++)
                {
                    var f = lines[i - 1].Split(',');
                    if (f.Length < 2) continue;
                    if (int.TryParse(f[0], out int sp) && int.TryParse(f[1], out int it))
                        _slots[i] = new Slot
                        {
                            Spell = sp >= 0 && sp < SpellCount ? (byte)sp : None,
                            Item = it >= 0 && it < ItemCount ? (byte)it : None,
                        };
                }
            }
            _lastFile = $"slot {_loadSlot}";
        }
        catch (Exception e) { Console.WriteLine($"[shortcuts] reading {path}: {e.Message}"); }
    }

    [PostHook("game", Address = 0x80023764)]
    static void AfterCardSave(CpuContext c, IMemory m)
    {
        int slot = m.ReadU8(SaveSlot);
        if (slot < 1 || slot > 3) return;
        string path = SlotPath(slot);
        try
        {
            var lines = new string[Count - 1];
            for (int i = 1; i < Count; i++)
                lines[i - 1] = $"{(_slots[i].Spell == None ? -1 : _slots[i].Spell)},{(_slots[i].Item == None ? -1 : _slots[i].Item)}";
            File.WriteAllLines(path, lines);
            _lastFile = $"slot {slot}";
        }
        catch (Exception e) { Console.WriteLine($"[shortcuts] writing {path}: {e.Message}"); }
    }

    // ================================================================
    // Settings
    // ================================================================

    public void DrawSettings()
    {
        var view = RecompOne.Runtime.Runtime.View;
        ImGui.TextWrapped("Shortcut 1 is the game's own Select shortcut; its key here presses Select, so the game's "
                        + "controls need no change. Shortcuts 2-5 are this mod's. To assign one, open Equipment, "
                        + "choose the Select button row (Hotkeys with keyboard labels), pick an item or spell and "
                        + "answer Yes; then choose which shortcut it goes on.");
        ImGui.Separator();

        int labels = _keyboardLabels ? 0 : 1;
        if (ImGui.Combo("In-game labels", ref labels, "Keyboard keys\0Controller buttons\0"))
        {
            _keyboardLabels = labels == 0;
            view.SetString(LabelsKey, _keyboardLabels ? "keyboard" : "controller");
            RecompOne.Runtime.Runtime.SaveView();
        }
        ImGui.TextDisabled("Keyboard keys also renames the Equipment page's Select button row to Hotkeys.");

        // A key being bound takes the first key down.
        if (_capture >= 0)
        {
            if (HostWindow.IsKeyDown(Key.Escape)) _capture = -1;
            else
                foreach (Key k in Enum.GetValues(typeof(Key)))
                {
                    if (k == Key.Unknown || k == Key.Escape || !HostWindow.IsKeyDown(k)) continue;
                    for (int j = 0; j < Count; j++)
                        if (j != _capture && _keys[j] == k) { _keys[j] = Key.Unknown; view.SetString(KeyKey(j), "Unknown"); }
                    _keys[_capture] = k;
                    view.SetString(KeyKey(_capture), k.ToString());
                    RecompOne.Runtime.Runtime.SaveView();
                    _capture = -1;
                    break;
                }
        }

        for (int i = 0; i < Count; i++)
        {
            ImGui.PushID(i);
            ImGui.Text(i == 0 ? "Shortcut 1 (Select):" : $"Shortcut {i + 1}:");
            ImGui.SameLine(150);
            string keyText = _capture == i ? "Press a key (Esc cancels)" : KeyLabel(_keys[i]);
            if (ImGui.Button(keyText + "##key", new System.Numerics.Vector2(170, 0))) _capture = _capture == i ? -1 : i;
            ImGui.SameLine();
            if (ImGui.Button("No key"))
            {
                _keys[i] = Key.Unknown;
                view.SetString(KeyKey(i), "Unknown");
                RecompOne.Runtime.Runtime.SaveView();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(170);
            string padName = i == 0 && _pads[i] == 0 ? "Select only" : PadButtons[_pads[i]].Name;
            if (ImGui.BeginCombo("##pad", padName))
            {
                for (int p = 0; p < PadButtons.Length; p++)
                    if (ImGui.Selectable(i == 0 && p == 0 ? "Select only" : PadButtons[p].Name, p == _pads[i]))
                    {
                        _pads[i] = p;
                        view.SetFloat(PadKey(i), PadButtons[p].Button);
                        RecompOne.Runtime.Runtime.SaveView();
                    }
                ImGui.EndCombo();
            }
            if (_mem != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(Text(Contents(_mem, i)));
                if (i > 0)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Clear")) _slots[i] = new Slot { Spell = None, Item = None };
                }
            }
            ImGui.PopID();
        }
        ImGui.TextDisabled("The pad's own Select button always works for shortcut 1. With no key for shortcut 1, "
                         + "the in-game list shows the game's own Select key.");

        if (ImGui.Checkbox("D-pad directions used by a shortcut don't walk or turn", ref _dpadTakeover))
        {
            view.SetBool(DpadKey, _dpadTakeover);
            RecompOne.Runtime.Runtime.SaveView();
        }
        ImGui.TextDisabled("Only in the world: menus still scroll with the d-pad, and the sticks are untouched.");
        ImGui.TextDisabled($"Shortcuts 2-5 are kept with each memory-card save. Loaded from: {(_lastFile.Length > 0 ? _lastFile : "nothing yet")}.");
    }
}
