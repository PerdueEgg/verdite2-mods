using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.Text;

/// <summary>
/// The game's text in new fonts. It owns the game's three fonts (Fonts.cs), the
/// menu font's added cells (MenuFont.cs) and every redrawn picture (Pictures.cs).
/// On its own it letters the unmodded game: the US text of every picture, and the
/// menus and messages in play in their normal case (GameText.cs, UsText.cs). A mod
/// that wants text of its own hands it over instead of carrying its own fonts.
///
/// A mod is compiled on its own and cannot name another mod's types, so the
/// functions are published in the process's own data slot, "kf2.text", as a
/// dictionary of plain delegates. A mod that uses them lists "kf2.text" under
/// "dependencies" in its mod.json -- the loader then loads this first, and skips
/// that mod with a message if this is missing -- and carries TextApi.cs, which
/// looks them up:
///
///   "Version"      int
///   "Ready"        Func&lt;bool&gt;                       the fonts are loaded
///   "MenuReady"    Func&lt;bool&gt;                       lower case is in the menu font
///   "MenuIndex"    Func&lt;char, int&gt;                  menu font cell, -1 none
///   "MenuChar"     Func&lt;int, char&gt;                  the character a cell shows, '\0' none
///   "MenuEncode"   Func&lt;string, int, byte[]&gt;        cells (at most n) then 0xFF
///   "MenuShown"    Func&lt;string, string&gt;            a label in the game's capitals as drawn
///                  (normal case on: SHORT SWORD is drawn Short Sword)
///   "SetPicture"   Action&lt;string, int, int, string, string&gt;  owner, archive, entry, style, text
///   "ClearPictures" Action&lt;string&gt;                  owner
///   "NextPicture"  Action&lt;string, string&gt;           style, text: the next picture shown
///   "Lines"        Func&lt;string, string, int&gt;        style, text: lines it wraps to
///   "SetString"    Action&lt;string, string?&gt;          a shared string table: key, value (null removes)
///   "GetString"    Func&lt;string, string?&gt;
///   "MenuPlace"    Func&lt;string, (int[] Cells, int[] X, int Width)&gt;   a string's menu
///                  cells, each one's offset from the first, and the width, spaced as
///                  the menus are (proportional unless switched off)
///   "MenuShadow"   Action&lt;IMemory, uint, int&gt;      a drop shadow under a menu letter quad
///                  just linked onto an ordering slot (memory, quad, slot), when the
///                  menu shadow is on
///   "Coverage"     Func&lt;string, string, int, (byte[] Ink, int W, int H)&gt;   style, text,
///                  width: the text wrapped and drawn as ink coverage, a byte a pixel
///                  (the Japanese Disc mod letters the movies with it)
///
/// The short messages in play (LEVEL UP, 36 GOLD, ...) are redrawn in the menu
/// font (HudMessages.cs); a mod words them through the string table as "hud:" +
/// the message's number, 0-35 in the page's order (21 is GOLD).
///
/// The string table is how mods share wording: a translation sets
/// "encyclopedia:The Elf Cave" and the Encyclopedia shows that instead.
/// </summary>
public sealed class TextMod : IMod
{
    public const string Slot = "kf2.text";
    const uint UploadTim = 0x80035684;     // func_80035684(buf): a message TIM, before upload
    const uint MainLoop = 0x800140AC;      // stage 9, once a frame in play

    const string PropPicturesKey = "kf2.text.proportional.pictures";
    const string PropMenusKey = "kf2.text.proportional.menus";
    const string ShadowPicturesKey = "kf2.text.shadow.pictures";
    const string ShadowMenusKey = "kf2.text.shadow.menus";
    const string GamePicturesKey = "kf2.text.game.pictures";
    const string GameCaseKey = "kf2.text.game.case";
    static readonly Dictionary<string, string> _strings = new();
    static string _note = "";

    public void OnLoad()
    {
        var api = new Dictionary<string, object>
        {
            ["Version"] = 1,
            ["Ready"] = new Func<bool>(Fonts.Ready),
            ["MenuReady"] = new Func<bool>(() => MenuFont.Ready),
            ["MenuIndex"] = new Func<char, int>(MenuFont.Index),
            ["MenuChar"] = new Func<int, char>(MenuFont.Char),
            ["MenuEncode"] = new Func<string, int, byte[]>(MenuFont.Encode),
            ["MenuShown"] = new Func<string, string>(GameText.Shown),
            ["SetPicture"] = new Action<string, int, int, string, string>(Pictures.Set),
            ["ClearPictures"] = new Action<string>(Pictures.Clear),
            ["NextPicture"] = new Action<string, string>(Pictures.Next),
            ["Lines"] = new Func<string, string, int>(Pictures.Lines),
            ["SetString"] = new Action<string, string?>(SetString),
            ["GetString"] = new Func<string, string?>(k => _strings.TryGetValue(k, out var v) ? v : null),
            ["MenuPlace"] = new Func<string, (int[] Cells, int[] X, int Width)>(MenuText.PlaceText),
            ["MenuShadow"] = new Action<IMemory, uint, int>((m, quad, slot) => { try { Shadow.Under(m, quad, (uint)slot, Shadow.KeepFree); } catch { } }),
            ["Coverage"] = new Func<string, string, int, (byte[] Ink, int W, int H)>(Pictures.Coverage),
        };
        HudMessages.Lookup = k => _strings.TryGetValue(k, out var v) ? v : null;
        var view = RecompOne.Runtime.Runtime.View;
        Fonts.PropPictures = view.GetBool(PropPicturesKey, true);
        Fonts.PropMenus = view.GetBool(PropMenusKey, true);
        Shadow.Pictures = view.GetBool(ShadowPicturesKey, false);
        Shadow.Menus = view.GetBool(ShadowMenusKey, false);
        GameText.NormalCase = view.GetBool(GameCaseKey, true);
        GameText.SetPictures(view.GetBool(GamePicturesKey, true));
        AppDomain.CurrentDomain.SetData(Slot, api);
        Console.WriteLine("[text] loaded");
    }

    public void OnUnload() => AppDomain.CurrentDomain.SetData(Slot, null);

    static void SetString(string key, string? value)
    {
        if (value == null) _strings.Remove(key); else _strings[key] = value;
        if (key.StartsWith(HudMessages.Key, StringComparison.Ordinal)) HudMessages.Invalidate();
    }

    [PreHook("game", Address = UploadTim)]
    static bool BeforeUpload(CpuContext c, IMemory m)
    {
        try { Pictures.BeforeUpload(m, c.A0); }
        catch (Exception e) { Console.WriteLine($"[text] picture: {e.Message}"); }
        return true;
    }

    [PreHook("game", Address = MenuFont.LoadImage)]
    static bool BeforeLoadImage(CpuContext c, IMemory m)
    {
        try { MenuFont.BeforeLoadImage(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] font: {e.Message}"); }
        try { HudMessages.BeforeLoadImage(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] on-screen messages: {e.Message}"); }
        return true;
    }

    [Replace("game", Address = MenuText.DrawLabel)]
    static void DrawLabel(Action<CpuContext, IMemory> orig, CpuContext c, IMemory m) => MenuText.Draw(orig, c, m);

    [PreHook("game", Address = Shadow.Link)]
    static bool BeforeLink(CpuContext c, IMemory m)
    {
        Shadow.BeforeLink(c, m);
        return true;
    }

    [PostHook("game", Address = Shadow.Link)]
    static void AfterLink(CpuContext c, IMemory m)
    {
        try { Shadow.AfterLink(m); }
        catch (Exception e) { Shadow.Menus = false; Console.WriteLine($"[text] menu shadow, switched off: {e.Message}"); }
    }

    [PostHook("game", Address = Shadow.FrameHead)]
    static void AfterFrameHead(CpuContext c, IMemory m) => Shadow.AfterFrameHead(m);

    [PreHook("game", Address = Shadow.Presenter)]
    static bool BeforePresent(CpuContext c, IMemory m)
    {
        try { Shadow.BeforePresent(m); }
        catch (Exception e) { Shadow.Pictures = false; Console.WriteLine($"[text] picture shadow, switched off: {e.Message}"); }
        return true;
    }

    [PostHook("game", Address = MainLoop)]
    static void AfterMainLoop(CpuContext c, IMemory m)
    {
        try { MenuFont.UploadOnce(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] font: {e.Message}"); }
        try { HudMessages.UploadOnce(c, m); }
        catch (Exception e) { Console.WriteLine($"[text] on-screen messages: {e.Message}"); }
    }

    [PreHook("game", Address = Shadow.HudSprite)]
    static bool BeforeHudSprite(CpuContext c, IMemory m)
    {
        Shadow.BeforeHudSprite(c, m);
        return true;
    }

    [PostHook("game", Address = Shadow.HudSprite)]
    static void AfterHudSprite(CpuContext c, IMemory m)
    {
        try { Shadow.AfterHudSprite(m); }
        catch (Exception e) { Console.WriteLine($"[text] message shadow: {e.Message}"); }
    }

    static void Persist(string key, bool value)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }

    public void DrawSettings()
    {
        ImGui.TextWrapped("New fonts for all of the game's text, with full upper and lower case and proportional spacing. " +
                          "Other mods (translations, the Encyclopedia) draw their own text with them too.");
        ImGui.TextDisabled($"Fonts: {Fonts.Status}");
        ImGui.TextDisabled(MenuFont.Ready ? "Menu font: lower case in place" : "Menu font: lower case not up yet");
        ImGui.TextDisabled($"Pictures: {Pictures.BaseCount} of the game's, {Pictures.Count} set by mods, {Pictures.Shown} redrawn this session");
        ImGui.Separator();
        bool pictures = GameText.ShowPictures;
        if (ImGui.Checkbox("Signs, dialogue, descriptions and place cards in the new fonts", ref pictures))
        {
            Persist(GamePicturesKey, pictures);
            GameText.SetPictures(pictures);
        }
        ImGui.TextDisabled("  the game's own words, redrawn; off shows the disc's pictures, unless another mod gives them text");
        if (ImGui.Checkbox("Menus, names and messages in normal capitalisation", ref GameText.NormalCase))
        {
            Persist(GameCaseKey, GameText.NormalCase);
            HudMessages.Invalidate();
        }
        ImGui.TextDisabled("  Use Item, Short Sword, Level up; off keeps the game's capitals. A translation's own text is left as it is");
        ImGui.Separator();
        if (ImGui.Checkbox("Proportional spacing and kerning in signs, dialogue and cards", ref Fonts.PropPictures))
            Persist(PropPicturesKey, Fonts.PropPictures);
        ImGui.TextDisabled("  for pictures shown from now on");
        if (ImGui.Checkbox("Proportional spacing and kerning in menus", ref Fonts.PropMenus))
        {
            Persist(PropMenusKey, Fonts.PropMenus);
            HudMessages.Invalidate();
        }
        ImGui.TextDisabled("  the numbers the game draws itself stay in fixed columns");
        if (ImGui.Checkbox("Drop shadow on signs, dialogue and cards", ref Shadow.Pictures))
            Persist(ShadowPicturesKey, Shadow.Pictures);
        ImGui.TextDisabled("  a pixel down and right, as in the Japanese Disc mod's movie text; it fades in and out with the letters");
        if (ImGui.Checkbox("Drop shadow on menu text and numbers", ref Shadow.Menus))
            Persist(ShadowMenusKey, Shadow.Menus);
        ImGui.TextDisabled("  and on the short messages in play (36 GOLD, LEVEL UP, LOCKED, ...)");
        ImGui.Separator();
        ImGui.TextWrapped($"Character maps: {Path.GetFullPath(Fonts.Folder)}");
        ImGui.TextWrapped("dialogue.png, sign.png and menu.png hold every letter; edit one and restart the game to use it. " +
                          "The *_template.png files are the same grids, empty, for drawing a new font. " +
                          "The *_spacing.json files hold each font's spacing and kerning.");
        if (ImGui.Button("Rebuild the character maps from the disc"))
        {
            try { Fonts.Rebuild(); _note = "Rebuilt from the disc. The mod's own fonts and any edits were replaced; reinstall the mod to get its fonts back."; }
            catch (Exception e) { _note = "Could not rebuild: " + e.Message; }
        }
        ImGui.TextDisabled("Replaces the mod's redrawn fonts with letters cut from your disc.");
        if (_note.Length > 0) ImGui.TextDisabled(_note);
    }
}
