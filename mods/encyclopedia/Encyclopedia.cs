using System;
using System.Collections.Generic;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;
using KingsField2 = Recompiled.KingsField2_game;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// An encyclopedia of everything the Truth Glass and the fortune teller can
/// describe, as a menu of the game's own: a hotkey (or ENCYCLOPEDIA on the DATA
/// page) opens it, Enemies / People / Places / Equipment lead to scrolling lists,
/// and picking an enemy, a person or an item shows its description.
///
/// ---- opening ----
///
/// The hotkey presses the menu button on the game's behalf for one call of
/// `func_80029CBC`, so the menu opens exactly where the game
/// would open its own. The in-game menu `func_80018E80` is then replaced for that
/// one call: this mod runs the same open (`func_80022754(1)`), its own pages, and
/// the same close (`func_800228C8(0)`), and returns 0x80000000, which
/// `func_80029CBC` ignores.
///
/// ---- the pages ----
///
/// Each page is the game's scrolling list: a descriptor stepped by
/// `func_8001EB70` and drawn by `func_800209E0`, with the frame head
/// `func_80022530` and presenter `func_800226A8` around it, twice per step as
/// every page of the game's menu does. The descriptor's row text is left empty
/// and the names are drawn by this mod in the same glyphs, because the game's
/// text drawer stops at 24 characters and some names are longer.
///
/// ---- descriptions ----
///
/// `func_80035B48(6, entry)` shows an ITEM.T picture from load to close, and is
/// safe inside the menu (Item Info does the same). Creature descriptions are
/// entries 240 + the creature ID, item descriptions 360 + the item ID; place
/// cards are 510 + the zone number and have no description, so picking a place
/// shows nothing more.
///
/// ---- the Sigill fix (always on) ----
///
/// The Truth Glass shows 240 + the creature record's byte +0x1, and both Sigill
/// types carry 2, so the deep-sea one shows the shore one's text. At the glass's
/// call (return address 0x80047FB8) V1 still holds the creature record; its
/// type's descriptor (0x80172624 + type * 120) has byte +0x9 = 0x32 for the big
/// one and 0x64 for the small one, and the entry is changed to 243 for the big.
///
/// Discovery, and keeping it with each save, is in Discovery.cs.
/// </summary>
public sealed partial class EncyclopediaMod : IMod
{
    // ---- game addresses ----
    const uint PadNow = 0x80199554, PadLast = 0x80199556;
    const uint MenuMask = 0x8006E56C;          // u32, the button that opens the menu
    const uint Quantity = 0x8006E5C8;          // u32 Left/Right counter func_8001EB70 steps
    const uint MaxHp = 0x80199426;             // u16, 0 until a game is loaded
    const uint AfterPlayerStage = 0x8002A550;

    const uint ItemFile = 6;                   // COM\ITEM.T
    const uint CloseMenu = 0x80000000;
    const uint FakeRa = 0x8001FFF0;            // a return address no other hook keys on

    const uint TruthGlassCall = 0x80047FB8;    // func_800474D0 -> func_80035B48, creature arm
    const uint TruthGlassScan = 0x80047F90;    // func_800474D0 -> func_8003B4B0, the creature in front
    const uint Creatures = 0x8016C544, CreatureSize = 0x7C, CreatureCount = 200;
    const uint Descriptors = 0x80172624, DescriptorSize = 120;

    // ---- settings ----
    const string KeyKey = "kf2.encyclopedia.key";
    const string PadKey = "kf2.encyclopedia.pad";
    const string OwnPlacesKey = "kf2.encyclopedia.ownplaces";

    static readonly (string Name, Key Key)[] Keys =
    [
        ("J", Key.J), ("B", Key.B), ("K", Key.K), ("L", Key.L), ("N", Key.N), ("M", Key.M),
        ("Y", Key.Y), ("U", Key.U), ("O", Key.O), ("H", Key.H),
        ("F5", Key.F5), ("F6", Key.F6), ("F7", Key.F7), ("F8", Key.F8), ("F9", Key.F9),
        ("None", Key.Unknown),
    ];

    // SDL GameControllerButton indices, the encoding HostWindow.IsPadButtonDown takes.
    static readonly (string Name, int Button)[] PadButtons =
    [
        ("None", -1),
        ("Back (select)", 4),
        ("R3 (right stick click)", 8),
        ("L3 (left stick click)", 7),
        ("Misc (share / mute)", 15),
    ];

    static int _keyIndex;
    static int _padIndex;
    static bool _ownPlaces = true;             // the places the game never named (Zones.IsOwn)

    static Entry[]? _discPlaces;

    /// <summary>The places the encyclopedia lists and counts: all of them, or
    /// without the mod's own when those are switched off.</summary>
    static Entry[] Places => _ownPlaces ? Data.Places
        : _discPlaces ??= Array.FindAll(Data.Places, p => !Zones.IsOwn(p.Desc[0] - 510));

    // ---- opening state ----
    static bool _held;
    static int _latch;
    const int LatchFrames = 6;
    static bool _injected;
    static ushort _savedNow, _savedLast;
    static bool _open;                          // the next func_80018E80 is ours

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _keyIndex = IndexOf(Keys, view.GetString(KeyKey, "J"));
        _padIndex = IndexOf(PadButtons, view.GetString(PadKey, "None"));
        _ownPlaces = view.GetBool(OwnPlacesKey, true);
        LoadDiscoverySettings();
        Event.AddListener(_onVSync);
    }

    public void OnUnload() => Event.RemoveListener(_onVSync);

    static int IndexOf<T>((string Name, T)[] list, string name)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i].Name == name) return i;
        return 0;
    }

    static bool Typing => ImGui.GetCurrentContext() != nint.Zero && ImGui.GetIO().WantTextInput;

    static readonly Action<VSyncEvent> _onVSync = _ =>
    {
        var key = Keys[_keyIndex].Key;
        bool down = !Typing && key != Key.Unknown && HostWindow.IsKeyDown(key);
        int pad = PadButtons[_padIndex].Button;
        if (!down && pad >= 0 && HostWindow.IsPadButtonDown(pad)) down = true;
        if (down && !_held) _latch = LatchFrames;
        else if (_latch > 0) _latch--;
        _held = down;
    };

    // ================================================================
    // Opening: press the menu button for one call of func_80029CBC
    // ================================================================

    [PreHook("game", Address = 0x80029CBC)]
    static bool BeforeMenuCheck(CpuContext c, IMemory m)
    {
        if (_latch == 0) return true;
        _latch = 0;
        if (m.ReadU16(MaxHp) == 0) return true;

        ushort mask = (ushort)m.ReadU32(MenuMask);
        _savedNow = m.ReadU16(PadNow);
        _savedLast = m.ReadU16(PadLast);
        m.WriteU16(PadNow, (ushort)(_savedNow | mask));
        m.WriteU16(PadLast, (ushort)(_savedLast & ~mask));
        _injected = true;
        _open = true;
        return true;
    }

    [PostHook("game", Address = 0x80029CBC)]
    static void AfterMenuCheck(CpuContext c, IMemory m)
    {
        if (_injected)
        {
            ushort mask = (ushort)m.ReadU32(MenuMask);
            m.WriteU16(PadNow, (ushort)((m.ReadU16(PadNow) & ~mask) | (_savedNow & mask)));
            m.WriteU16(PadLast, (ushort)((m.ReadU16(PadLast) & ~mask) | (_savedLast & mask)));
            _injected = false;
        }
        _open = false;   // the game opened it and we ran, or it would not open just now
    }

    [PreHook("game", Address = 0x80018E80)]
    static bool BeforeMenu(CpuContext c, IMemory m)
    {
        if (!_open) return true;
        _open = false;
        RunSession(c, m);
        c.V0 = CloseMenu;
        return false;
    }

    // ================================================================
    // The session
    // ================================================================

    // Scratch on the emulated stack, below the caller's frame, for the length of
    // the session: two descriptors, filler row buffers, and the step's two flags.
    const uint Scratch = 0x1000;
    const uint OffDescs = 0x100;         // 3 x 0x40
    const uint OffRows = 0x200;          // u8 filler, 0xFF
    const uint OffCounts = 0x300;        // u8 per entry, 0xFF = nothing drawn
    const uint OffWords = 0x400;         // u32 per entry
    const uint OffFlags = 0x600;         // per level: confirmed, result
    const uint OffIds = 0x800;           // u8 item id per row, for a page with models
    const uint OffModel = 0xA00;         // Render.cs: the turn, matrices, blender slots (0x100)
    const int MaxEntries = 0x40;

    static uint _base;

    sealed class Page
    {
        public string Header = "";
        public Func<int> Count = () => 0;
        public Func<int, string> Label = _ => "";
        public Func<CpuContext, IMemory, int, bool> Confirm = (_, _, _) => false;  // true: played its own sound
        public Func<string> Counter = () => "";   // drawn in a box right of the title
        public int Visible = 10;
        public int ListY = 39;           // the list's top: 39 for the long lists, 159 for the item pages' four rows
        public Func<int, int>? ModelId;  // an item id per row: the page shows its preview model, as the Items page does
        public Func<int, bool> ShowModel = _ => true;
        public Func<int, int>? Model;    // a model per row (Models.cs), -1 for none: drawn by Render.cs
        public Func<int, Tune?>? ModelTune;   // a row's own tuning over its model's, or null
        public int Area = -1;            // the area a page's models are textured from; -1 each model's own
        public bool Numbered;            // each row led by its number, locked or not
        public Func<int, string>? RowCounter;   // drawn at the right end of each row
        public int Sel, Scroll, Row;     // remembered between visits
    }

    // The menus themselves are built in Menus.cs.

    /// <summary>The whole encyclopedia, from the world (opening and closing the
    /// game's menu around it) or from the DATA page with the menu already open
    /// (<paramref name="inMenu"/>: the menu, its pointer and its music as they are).</summary>
    static void RunSession(CpuContext c, IMemory m, bool inMenu = false)
    {
        var snap = c.Snapshot();
        uint quantity = m.ReadU32(Quantity);
        try
        {
            c.SP -= Scratch;
            _base = (c.SP + 0xF) & ~0xFu;
            for (uint i = 0; i < 0x100; i++) m.WriteU8(_base + OffRows + i, 0xFF);
            for (uint i = 0; i < 0x100; i++) m.WriteU8(_base + OffCounts + i, 0xFF);
            for (uint i = 0; i < 0x200; i += 4) m.WriteU32(_base + OffWords + i, 0xFFFFFFFF);

            if (!inMenu)
            {
                MenuPointer(c, m, true);                   // release mouse look, as the game's menu does
                Call(c, m, KingsField2.func_80022754, 1); // open the menu: buffers, frozen frame, music pause
                Sound(c, m, 0x10);
            }
            BeginModels(c, m, _base + OffModel);
            RunPage(c, m, _main, 0);
            EndModels(c, m);
            Call(c, m, KingsField2.func_80022D80);
            if (!inMenu)
            {
                Call(c, m, KingsField2.func_800228C8, 0); // close it
                MenuPointer(c, m, false);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[encyclopedia] {e}");
            try { EndModels(c, m); }   // the VRAM and the model table as they were
            catch (Exception e2) { Console.WriteLine($"[encyclopedia] cleanup: {e2.Message}"); }
        }
        finally
        {
            m.WriteU32(Quantity, quantity);
            c.Restore(snap);
        }
    }

    /// <summary>One page: step, act, draw, until backed out of.</summary>
    static int _level;   // the page whose Confirm is running, so a menu can open the next level down

    static void RunPage(CpuContext c, IMemory m, Page page, int level)
    {
        uint d = _base + OffDescs + (uint)level * 0x40;
        uint confirmed = _base + OffFlags + (uint)level * 8, result = confirmed + 4;
        InitDesc(m, d, page);
        m.WriteU32(confirmed, 0);
        m.WriteU32(result, 0xFFFFFF9D);

        // A page with models hands func_8001EB70 a row of item ids, as the Items
        // page does: every cursor move then loads that row's preview through
        // func_80022CAC, and the first one is loaded here.
        uint ids = 0;
        if (page.ModelId != null)
        {
            ids = _base + OffIds;
            int n = Math.Min(page.Count(), 0x80);
            for (int i = 0; i < n; i++) m.WriteU8(ids + (uint)i, (byte)page.ModelId(i));
            LoadPreview(c, m, d, ids);
        }
        PageModel(c, m, d, page);

        Frames(c, m, d, page);
        Call(c, m, KingsField2.func_80022EFC);            // let go of the button that got us here

        while (true)
        {
            Call(c, m, KingsField2.func_8001EB70, d, ids, confirmed, result);
            if (m.ReadU32(result) == 0xFFFFFFFF)           // back (the step played its sound)
            {
                Remember(m, d, page);
                if (ids != 0) Call(c, m, KingsField2.func_80022D80);   // done with the preview
                if (page.Model != null) HideModel(c, m);               // and the model, the VRAM back as it was
                Call(c, m, KingsField2.func_80022EFC);
                return;
            }
            PageModel(c, m, d, page);                     // nothing, unless the cursor moved
            if (m.ReadU32(confirmed) != 0)
            {
                m.WriteU32(confirmed, 0);
                int sel = m.ReadU8(d + 0x21);
                Remember(m, d, page);
                int saved = _level;
                _level = level;
                if (sel < page.Count()) page.Confirm(c, m, sel);
                _level = saved;
                InitDesc(m, d, page);                     // a page below may have shared the buffers
                if (ids != 0) LoadPreview(c, m, d, ids);   // and the preview, after a description or a page below
                PageModel(c, m, d, page);
                Call(c, m, KingsField2.func_80022EFC);
            }
            Frames(c, m, d, page);
        }
    }

    static void LoadPreview(CpuContext c, IMemory m, uint d, uint ids)
    {
        if (m.ReadU8(d + 0x1E) == 0) return;
        Call(c, m, KingsField2.func_80022CAC, m.ReadU8(ids + m.ReadU8(d + 0x21)));
    }

    /// <summary>The highlighted row's model on show (Render.cs): loaded when the
    /// cursor moves onto it, nothing while it stays.</summary>
    static void PageModel(CpuContext c, IMemory m, uint d, Page page)
    {
        if (page.Model == null) return;
        int sel = m.ReadU8(d + 0x21);
        bool any = page.Count() > 0 && sel < page.Count();
        ShowModel(c, m, any ? page.Model(sel) : -1, page.Area, any ? page.ModelTune?.Invoke(sel) : null);
    }

    static void Frames(CpuContext c, IMemory m, uint d, Page page)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Call(c, m, KingsField2.func_80022530);         // frame head
            if (page.ModelId != null && page.Count() > 0)
            {
                int sel = m.ReadU8(d + 0x21);
                if (sel < page.Count() && page.ShowModel(sel))
                    Call(c, m, KingsField2.func_8002156C, (uint)page.ModelId(sel));   // the turning preview, as the Items page draws it
            }
            if (page.Model != null) DrawModel(c, m);       // the same place, for a creature, person or place
            Call(c, m, KingsField2.func_800209E0, d, 1);   // header, panel, highlight, cursor, scrollbar
            DrawCounter(c, m, page.Counter());
            DrawRows(m, d, page);
            Call(c, m, KingsField2.func_800226A8);         // present
        }
    }

    // A second header box right of the title, with the count centred in it.
    const uint OffCounterRec = 0x700;
    const int CounterX = 147, CounterY = 19, BoxWidth = 124;
    const uint HeaderBox = 0x80064C20;

    static void DrawCounter(CpuContext c, IMemory m, string text)
    {
        if (text.Length == 0) return;
        uint rec = _base + OffCounterRec;
        m.WriteU16(rec, CounterX);
        m.WriteU16(rec + 2, CounterY);
        m.WriteU8(rec + 4, 0xFF);
        Call(c, m, KingsField2.func_800218B4, HeaderBox, rec);   // the box, as func_800209E0 draws the title's
        int left = CounterX - 6;                                  // the box's drawn left edge
        int x = left + (BoxWidth - TextWidth(text)) / 2;
        DrawText(m, x, CounterY, text, 16);
    }

    static void InitDesc(IMemory m, uint d, Page page)
    {
        for (uint i = 0; i < 0x40; i++) m.WriteU8(d + i, 0);
        m.WriteU16(d + 0x00, 17);                          // header box, as the game's pages place it
        m.WriteU16(d + 0x02, 19);
        WriteText(m, d + 4, NoThe(L(page.Header)), 24);
        int n = page.Count();
        int vis = Math.Min(page.Visible, Math.Max(n, 1));
        int sel = Math.Clamp(page.Sel, 0, Math.Max(n - 1, 0));
        int scroll = Math.Clamp(page.Scroll, 0, Math.Max(n - vis, 0));
        if (sel < scroll) scroll = sel;
        if (sel >= scroll + vis) scroll = sel - vis + 1;
        m.WriteU8(d + 0x1C, 42);                           // list left edge
        m.WriteU8(d + 0x1D, (byte)page.ListY);             // row 0 at ListY + 5
        m.WriteU8(d + 0x1E, (byte)n);
        m.WriteU8(d + 0x1F, (byte)vis);
        m.WriteU8(d + 0x20, (byte)scroll);
        m.WriteU8(d + 0x21, (byte)sel);
        m.WriteU8(d + 0x22, (byte)(sel - scroll));
        m.WriteU8(d + 0x23, 1);                            // one byte a row, and it is 0xFF: the game draws no text
        m.WriteU32(d + 0x24, _base + OffRows);
        m.WriteU32(d + 0x28, _base + OffRows);
        m.WriteU32(d + 0x2C, _base + OffCounts);
        m.WriteU32(d + 0x30, _base + OffWords);
    }

    static void Remember(IMemory m, uint d, Page page)
    {
        page.Sel = m.ReadU8(d + 0x21);
        page.Scroll = m.ReadU8(d + 0x20);
        page.Row = m.ReadU8(d + 0x22);
    }

    static void DrawRows(IMemory m, uint d, Page page)
    {
        int x = m.ReadU8(d + 0x1C) + 5, y = m.ReadU8(d + 0x1D) + 5;
        int scroll = m.ReadU8(d + 0x20), vis = m.ReadU8(d + 0x1F), n = page.Count();
        for (int r = 0; r < vis; r++)
        {
            int i = scroll + r;
            if (i >= n) break;
            // A name in the game's capitals (an item's, with no translation) as the
            // port draws the game's own menus: Short Sword, not SHORT SWORD.
            string label = TextApi.MenuShown(NoThe(page.Label(i)));
            if (page.Numbered) label = ((i + 1) + ".").PadLeft(n.ToString().Length + 1) + " " + label;
            DrawText(m, x, y + 14 * r, label, 33);
            if (page.RowCounter != null)
            {
                string count = page.RowCounter(i);
                DrawText(m, PanelRight - TextWidth(count), y + 14 * r, count, 16);
            }
        }
    }

    // The list panel is 236 wide from the list's left edge (0x80064C38's width);
    // a row's right-hand count ends a few pixels inside it, wherever its width
    // lands, so "9/38" and "102/102" both stop at the same place.
    const int PanelRight = 42 + 236 - 8;

    /// <summary>A name without a leading "The": every list, menu and title is
    /// drawn this way, so "The Ruins of King Harvine's Castle" fits its row.</summary>
    static string NoThe(string s) =>
        s.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? s.Substring(4) : s;

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

    // The port's patches/MenuMouse.cs gives the pointer back while the in-game
    // menu runs, from hooks on func_80018E80 -- which this mod replaces, so they
    // never fire. Its two hook bodies are public; call them by name. Reflection,
    // because a mod is compiled without a reference to the port's own assembly.
    static System.Reflection.MethodInfo? _menuEnter, _menuLeave;
    static bool _menuLooked;

    static void MenuPointer(CpuContext c, IMemory m, bool enter)
    {
        if (!_menuLooked)
        {
            _menuLooked = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Kf2.MenuMouse", false);
                if (t == null) continue;
                _menuEnter = t.GetMethod("BeforeMenu", [typeof(CpuContext), typeof(IMemory)]);
                _menuLeave = t.GetMethod("AfterMenu", [typeof(CpuContext), typeof(IMemory)]);
                break;
            }
            if (_menuEnter == null || _menuLeave == null)
                Console.WriteLine("[encyclopedia] the port's menu pointer code was not found; mouse look stays locked in the menu");
        }
        try { (enter ? _menuEnter : _menuLeave)?.Invoke(null, [c, m]); }
        catch (Exception e) { Console.WriteLine($"[encyclopedia] menu pointer: {e.InnerException?.Message ?? e.Message}"); }
    }

    static void Sound(CpuContext c, IMemory m, uint id) => Call(c, m, KingsField2.func_80022DC4, id);

    static void ShowMessage(CpuContext c, IMemory m, uint file, uint entry)
    {
        var snap = c.Snapshot();
        try { Call(c, m, KingsField2.func_80035B48, file, entry); }
        finally { c.Restore(snap); }
    }

    // ================================================================
    // Text in the menu font
    // ================================================================

    const uint Cursor = 0x8006E914, ActiveDescriptor = 0x8017E0A4, OrderingTable = 0x8018E0A8;
    const uint TextTemplate = 0x80064BF0;

    /// <summary>The menu font's cell for a character, or -1: through the port's
    /// lettering, which has lower case and more punctuation while its font is drawing
    /// (in the CLASSIC font, lower case comes out in capitals); without it, the disc's
    /// own capitals.</summary>
    static int Glyph(char ch) => TextApi.Present ? TextApi.MenuIndex(ch) : DiscCell(ch);

    /// <summary>The disc's label font: A-Z 0x00-0x19, 0-9 0x20-0x29, ". , ' - = / * #
    /// ! ■ ?" from 0x30, space 0x7F. Lower case is drawn in capitals.</summary>
    const string DiscSigns = ".,'-=/*#!■?";

    static int DiscCell(char ch)
    {
        if (ch == ' ') return 0x7F;
        if (ch == '’') ch = '\'';
        ch = char.ToUpperInvariant(ch);
        if (ch >= 'A' && ch <= 'Z') return ch - 'A';
        if (ch >= '0' && ch <= '9') return 0x20 + (ch - '0');
        int k = DiscSigns.IndexOf(ch);
        return k >= 0 ? 0x30 + k : -1;
    }

    static char DiscChar(int cell)
    {
        if (cell == 0x7F) return ' ';
        if (cell >= 0 && cell < 26) return (char)('A' + cell);
        if (cell >= 0x20 && cell <= 0x29) return (char)('0' + cell - 0x20);
        if (cell >= 0x30 && cell < 0x30 + DiscSigns.Length) return DiscSigns[cell - 0x30];
        return '\0';
    }

    /// <summary>The port's new fonts are drawing (its MODERN font): its menu spacing
    /// applies, and it can letter a picture. Not in the CLASSIC font or without the
    /// lettering (KF2_TEXT=0).</summary>
    static bool Lettered => TextApi.MenuReady;

    static List<int> Glyphs(string s)
    {
        var g = new List<int>(s.Length);
        foreach (char ch in s)
        {
            int v = Glyph(ch);
            if (v >= 0) g.Add(v);
        }
        // Collapse the doubled space left where a character was dropped, e.g. "(".
        for (int i = g.Count - 1; i > 0; i--)
            if (g[i] == 0x7F && g[i - 1] == 0x7F) g.RemoveAt(i);
        return g;
    }

    static void WriteText(IMemory m, uint at, string s, int size)
    {
        var g = Glyphs(s);
        int n = Math.Min(g.Count, size - 1);
        for (int i = 0; i < n; i++) m.WriteU8(at + (uint)i, (byte)g[i]);
        m.WriteU8(at + (uint)n, 0xFF);
    }

    const int Advance = 7;             // the game's own letter spacing, without the port's fonts

    static int TextWidth(string s)
    {
        if (Lettered && TextApi.MenuPlace(s) is { } placed) return placed.Width;   // the menus' own spacing
        return Glyphs(s).Count * Advance;
    }

    // func_80021E10's quad, byte for byte, without its 24-character stop.
    static void DrawText(IMemory m, int x, int y, string s, int max)
    {
        var glyphs = Glyphs(s);
        int[]? at = null;
        if (Lettered && TextApi.MenuPlace(s) is { } placed) { glyphs = new List<int>(placed.Cells); at = placed.X; }   // spaced as the menus are
        int w1 = (short)m.ReadU16(TextTemplate + 8), h = (short)m.ReadU16(TextTemplate + 0xA);
        int uw1 = m.ReadU8(TextTemplate + 8), vh = m.ReadU8(TextTemplate + 0xA);
        ushort tpage = m.ReadU16(TextTemplate), clut = m.ReadU16(TextTemplate + 2);
        int X = x;
        for (int k = 0; k < glyphs.Count && k < max; X += Advance, k++)
        {
            if (at != null) X = x + at[k];
            int g = glyphs[k] & 0x7F;
            int w = w1, uw = uw1;
            uint p = NewQuad(m);
            m.WriteU16(p + 0x08, (ushort)X); m.WriteU16(p + 0x0A, (ushort)y);
            m.WriteU16(p + 0x10, (ushort)(X + w)); m.WriteU16(p + 0x12, (ushort)y);
            m.WriteU16(p + 0x18, (ushort)X); m.WriteU16(p + 0x1A, (ushort)(y + h));
            m.WriteU16(p + 0x20, (ushort)(X + w)); m.WriteU16(p + 0x22, (ushort)(y + h));
            m.WriteU16(p + 0x16, tpage);
            m.WriteU16(p + 0x0E, clut);
            int u = (g & 15) * 8, v = (g >> 4) * 15;
            m.WriteU8(p + 0x0C, (byte)u); m.WriteU8(p + 0x0D, (byte)v);
            m.WriteU8(p + 0x14, (byte)(u + uw)); m.WriteU8(p + 0x15, (byte)v);
            m.WriteU8(p + 0x1C, (byte)u); m.WriteU8(p + 0x1D, (byte)(v + vh));
            m.WriteU8(p + 0x24, (byte)(u + uw)); m.WriteU8(p + 0x25, (byte)(v + vh));
            Link(m, p, 10);
            TextApi.MenuShadow(m, p, 10);   // the port's drop shadow, as the game's own labels get it
        }
    }

    // func_800229D8: length 9 and code 0x2C, colour grey.
    static uint NewQuad(IMemory m)
    {
        uint p = m.ReadU32(Cursor);
        m.WriteU8(p + 3, 9);
        m.WriteU8(p + 7, 0x2C);
        m.WriteU8(p + 4, 0x68);
        m.WriteU8(p + 5, 0x68);
        m.WriteU8(p + 6, 0x68);
        return p;
    }

    // func_80022A28: AddPrim onto the slot, then bump the cursor twice.
    static void Link(IMemory m, uint p, int slot)
    {
        uint entry = m.ReadU32(OrderingTable) + (uint)(slot * 4);
        uint tag = m.ReadU32(p);
        uint head = m.ReadU32(entry);
        m.WriteU32(p, (tag & 0xFF000000) | (head & 0x00FFFFFF));
        m.WriteU32(entry, (head & 0xFF000000) | (p & 0x00FFFFFF));
        uint next = m.ReadU32(Cursor) + 0x28;
        m.WriteU32(Cursor, next);
        m.WriteU32(m.ReadU32(ActiveDescriptor) + 8, next);
    }

    // ================================================================
    // The Sigill fix
    // ================================================================

    // The creature the Truth Glass is looking at: func_8003B4B0's result at the
    // glass's own call, rather than V1 at the message call, which another hook on
    // func_80035B48 may already have used.
    static uint _glassTarget;

    [PostHook("game", Address = 0x8003B4B0)]
    static void AfterTargetScan(CpuContext c, IMemory m)
    {
        if (c.RA != TruthGlassScan) return;
        _glassTarget = c.V0;
        if (c.V0 != 0) return;

        // No creature in view, so the glass looks for a zone next -- and where the
        // game has none at all (area 7) it never gets to a place call. Show the
        // mod's own zone from here instead.
        if (PlaceAt(m, extras: false) >= 0) return;   // the game will find one itself
        int place = PlaceAt(m);
        if (place < 0) return;
        Mark(510 + place);
        if (CustomCardOf(place) is { } custom) { TextApi.NextPicture("card", CardText(custom)); ShowMessage(c, m, ItemFile, Placeholder); }
        else if (Zones.CardOf(place) is var card and >= 0) ShowMessage(c, m, ItemFile, (uint)card);
        else return;
        // func_800474D0 starts this flag at 0, sets it when it shows something, and
        // says "Nothing happens" at its end if it is still 0. Something happened.
        m.WriteU32(UseHappened, 1);
    }

    const uint UseHappened = 0x801B3084;

    [PreHook("game", Address = 0x80035B48)]
    static bool BeforeMessage(CpuContext c, IMemory m)
    {
        if (c.A0 == ItemFile && c.A1 == 242 && c.RA == TruthGlassCall)
        {
            uint rec = IsCreature(_glassTarget) ? _glassTarget : c.V1;
            if (IsCreature(rec))
            {
                if (IsDeepSeaSigill(m, rec)) c.A1 = 243;   // the deep-sea Sigill's own description
            }
        }

        // A place card from the Truth Glass: name it with this mod's zone
        // corrections (the graves the disc labels The Research Office).
        if (c.A0 == ItemFile && c.RA == TruthGlassPlaceCall)
        {
            int place = PlaceAt(m);
            if (place < 0) place = (int)c.A1 - 510;
            Mark(510 + place);
            if (CustomCardOf(place) is { } custom)
            {
                ShowCustomCard(c, custom);
                return true;
            }
            if (Zones.CustomCard(place) != null) return false;  // a card that can't be lettered now: nothing to show; it still unlocked
            int card = Zones.CardOf(place);
            if (card < 0) return false;                        // nothing to show; it still unlocked
            c.A1 = (uint)card;
            return true;
        }

        // Discovery: what the Truth Glass shows, and who you are talking to.
        if (c.A0 == ItemFile && c.RA == TruthGlassCall) OnTruthGlass(c.A1);
        else if (c.A0 == TalkFile) OnTalk(m);
        if (c.A0 == ItemFile) OnItemCard(c.RA, c.A1);
        return true;
    }

    static bool IsCreature(uint rec) => rec >= Creatures && rec < Creatures + CreatureSize * CreatureCount;

    // Two signs, either enough: the big Sigill's type has byte +0x9 = 0x32 (the
    // shore one's is 0x64) and 300-400 HP (the shore one's is 150-210).
    static bool IsDeepSeaSigill(IMemory m, uint rec)
    {
        uint desc = Descriptors + m.ReadU8(rec + 2) * DescriptorSize;
        if (m.ReadU8(desc) != 2) return false;
        return m.ReadU8(desc + 9) == 0x32 || m.ReadU16(desc + 0x1A) >= 300;
    }

    // ================================================================
    // Discovery by encounter, in play (Discovery.cs, Items.cs)
    // ================================================================

    /// <summary>Where you stand is checked every this many ticks, under a second:
    /// places are big, and nobody crosses one that quickly.</summary>
    const int PlaceEvery = 15;
    static int _placeTick;

    [PostHook("game", Address = AfterPlayerStage)]
    static void AfterPlayerStageHook(CpuContext c, IMemory m)
    {
        if (m.ReadU16(MaxHp) == 0) return;   // not in a game (the title also runs stage 3)
        if (_quiet > 0 && --_quiet > 0) { CheckItems(m); return; }   // starting gear: found, not announced
        if (++_placeTick >= PlaceEvery) { _placeTick = 0; CheckPlaces(m); }
        CheckItems(m);
    }

    // ================================================================
    // Settings
    // ================================================================

    public void DrawSettings()
    {
        ImGui.TextWrapped($"Press {Keys[_keyIndex].Name} in the world, or choose ENCYCLOPEDIA on the menu's DATA page, to open "
                        + "the encyclopedia: every enemy, person, place and item. Pick an enemy, person or item to read its "
                        + "description. The hotkey opens it wherever the game's own menu would open.");
        ImGui.Separator();

        if (Combo("Open key", ref _keyIndex, Keys)) Persist(KeyKey, Keys[_keyIndex].Name);
        if (Combo("Open pad button", ref _padIndex, PadButtons)) Persist(PadKey, PadButtons[_padIndex].Name);

        if (ImGui.Checkbox("Places the game never named", ref _ownPlaces))
        {
            RecompOne.Runtime.Runtime.View.SetBool(OwnPlacesKey, _ownPlaces);
            RecompOne.Runtime.Runtime.SaveView();
        }
        ImGui.TextDisabled("The Kraken's Nest, the two Underworld Prisons, the Royal Treasure House, the Space for Completion and "
                         + "Guyra's Dimension, with the cards made for them. Off: they leave the Places list and the totals, and "
                         + "the Truth Glass names those spots as the game does. What you found there is kept for when it is back on.");

        ImGui.Separator();
        DrawDiscoverySettings();
    }

    static bool Combo<T>(string label, ref int index, (string Name, T)[] list)
    {
        bool changed = false;
        if (ImGui.BeginCombo(label, list[index].Name))
        {
            for (int i = 0; i < list.Length; i++)
                if (ImGui.Selectable(list[i].Name, i == index))
                {
                    index = i;
                    changed = true;
                }
            ImGui.EndCombo();
        }
        return changed;
    }

    static void Persist(string key, string value)
    {
        RecompOne.Runtime.Runtime.View.SetString(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }
}
