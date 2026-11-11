using System;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Silk.NET.Input;

namespace Kf2.Mods.TruthGlass;

/// <summary>
/// A hotkey that uses the Truth Glass on whatever is in front of you.
///
/// ---- the game already does the work ----
///
/// The in-game menu is opened from `func_80029CBC`, and what it does with the
/// menu's answer is the whole feature:
///
///     r = func_80018E80();                  // the menu
///     if (r >= 0 && func_80029560(r) == 0)  // an item, not one used on yourself
///         func_800474D0(pos, facing, r);    // use it on what is in front of you
///
/// The Truth Glass is used from the Items page like a key or a vial: the page
/// returns its item id, and `func_800474D0` looks at what is in front of you
/// and shows the enemy's or NPC's message. This mod does not call any of that
/// itself, and so does not need to know the position or the facing. It lets
/// `func_80029CBC` run exactly as it would for the menu key, and when that
/// function asks the menu for an answer, answers for it: the Truth Glass's id,
/// without the menu ever being drawn. Everything after that is the game's own
/// path.
///
/// ---- opening ----
///
/// The menu opens on Circle just pressed (mask word 0x8006E56C against the pad
/// words at 0x80199554 / last frame's 0x80199556), so the hotkey sets that
/// press for one call of `func_80029CBC`. Every other condition the game puts
/// on opening the menu still applies (no menu mid-cutscene, and so on), and if
/// it does not open, `func_80018E80` is never reached and nothing happens.
///
/// ---- not carrying it ----
///
/// The inventory is a u8 count per item id at 0x8009B52C. The use path expects
/// the item to be there (and a use may take one off), so when you are not
/// carrying the Truth Glass its count is set to 1 for the duration of that
/// one call and put back to 0 afterwards. Nothing is left in the inventory.
///
/// ---- which id ----
///
/// The Truth Glass is item 0x55 (0x8009B52C + 0x55 = 0x8009B581, the byte
/// after the Phantom Rod's).
/// </summary>
public sealed class TruthGlassMod : IMod
{
    const uint PadNow = 0x80199554, PadLast = 0x80199556;
    const uint MenuMask = 0x8006E56C;          // u32, the button that opens the menu

    const uint Inventory = 0x8009B52C;
    const int GlassId = 0x55;

    const string KeyKey = "kf2.truthglass.key";
    const string PadKey = "kf2.truthglass.pad";
    const string OwnedKey = "kf2.truthglass.owned";

    static readonly (string Name, Key Key)[] Keys =
    [
        ("`", Key.GraveAccent), ("T", Key.T), ("Y", Key.Y), ("U", Key.U), ("M", Key.M), ("N", Key.N), ("B", Key.B),
        ("V", Key.V), ("C", Key.C), ("X", Key.X), ("Z", Key.Z),
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

    static int _key = 0;              // `
    static int _pad = 0;              // None
    static bool _onlyIfOwned = false;

    // The hotkey, latched once a frame and good for a few frames: the routine
    // that opens the menu only runs on world ticks.
    static bool _held;
    static int _latch;
    const int LatchFrames = 6;

    static bool _injected;            // Circle is pressed on the game's behalf this call
    static ushort _savedNow, _savedLast;
    static bool _pending;             // the next menu call is answered with the Truth Glass
    static int _borrowedId = -1;      // the id whose count was set to 1 for this use

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _key = IndexOf(Keys, view.GetString(KeyKey, "`"));
        _pad = IndexOf(PadButtons, view.GetString(PadKey, "None"));
        _onlyIfOwned = view.GetBool(OwnedKey, false);
        Event.AddListener(_onVSync);
    }

    public void OnUnload() => Event.RemoveListener(_onVSync);

    static int IndexOf<T>((string Name, T)[] list, string name)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i].Name == name) return i;
        return 0;
    }

    /// <summary>A text box in the port's panels has the keyboard: the key is a letter there.</summary>
    static bool Typing => ImGui.GetCurrentContext() != nint.Zero && ImGui.GetIO().WantTextInput;

    static readonly Action<VSyncEvent> _onVSync = _ =>
    {
        var key = Keys[_key].Key;
        bool down = key != Key.Unknown && !Typing && HostWindow.IsKeyDown(key);
        int pad = PadButtons[_pad].Button;
        if (!down && pad >= 0 && HostWindow.IsPadButtonDown(pad)) down = true;
        if (down && !_held) _latch = LatchFrames;
        else if (_latch > 0) _latch--;
        _held = down;
    };

    // ---- opening ----

    [PreHook("game", Address = 0x80029CBC)]
    static bool BeforeMenuCheck(CpuContext c, IMemory m)
    {
        if (_latch == 0) return true;
        _latch = 0;

        if (_onlyIfOwned && m.ReadU8(Inventory + GlassId) == 0)
        {
            ToastNotifications.ShowText("Truth Glass", "You are not carrying the Truth Glass");
            return true;
        }

        ushort mask = (ushort)m.ReadU32(MenuMask);
        _savedNow = m.ReadU16(PadNow);
        _savedLast = m.ReadU16(PadLast);
        m.WriteU16(PadNow, (ushort)(_savedNow | mask));
        m.WriteU16(PadLast, (ushort)(_savedLast & ~mask));
        _injected = true;
        _pending = true;
        return true;
    }

    // ---- answering for the menu ----

    [PreHook("game", Address = 0x80018E80)]
    static bool BeforeMenu(CpuContext c, IMemory m)
    {
        if (!_pending) return true;
        _pending = false;

        if (m.ReadU8(Inventory + GlassId) == 0)
        {
            m.WriteU8(Inventory + GlassId, 1);
            _borrowedId = GlassId;
        }
        c.V0 = GlassId;    // the "page" answered with the Truth Glass
        return false;
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
        if (_borrowedId >= 0)
        {
            m.WriteU8(Inventory + (uint)_borrowedId, 0);
            _borrowedId = -1;
        }
        // The menu has come and gone (or the game would not open it just now).
        _pending = false;
    }

    // ---- settings ----

    public void DrawSettings()
    {
        ImGui.TextWrapped($"Press {Keys[_key].Name} in game to use the Truth Glass on whatever is in front of you, "
                        + "as if you had chosen it from the item menu: the game's own enemy or NPC description.");
        ImGui.Separator();

        if (Combo("Key", ref _key, Keys)) Persist(KeyKey, Keys[_key].Name);
        if (Combo("Pad button", ref _pad, PadButtons)) Persist(PadKey, PadButtons[_pad].Name);

        if (ImGui.Checkbox("Only when carrying the Truth Glass", ref _onlyIfOwned))
        {
            RecompOne.Runtime.Runtime.View.SetBool(OwnedKey, _onlyIfOwned);
            RecompOne.Runtime.Runtime.SaveView();
        }
        ImGui.TextDisabled("Off: works without the Truth Glass, and does not give you one.");
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
