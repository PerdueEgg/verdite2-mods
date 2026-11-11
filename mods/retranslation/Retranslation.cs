using System;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.Retranslation;

/// <summary>
/// A new English translation from the Japanese release. All the lettering is the
/// Text mod's (a dependency); this mod only carries the text. Three parts, each
/// can be switched off on its own:
///
///  - Pictures: every sign, dialogue line, description and place card, handed to
///    the Text mod, which redraws it whenever the game shows it.
///  - Menus and names: GAME.EXE's menu-font text, rewritten in RAM (Strings.cs),
///    and the short messages in play (LEVEL UP, 36 GOLD, ...), which are a picture
///    the Text mod redraws from "hud:" + the message's number.
///  - Encyclopedia: its names, titles and cards, shared through the Text mod's
///    string table as "encyclopedia:" + its English, which it looks up itself.
///  - Movies: each text card's English, shared the same way as "movie:" + its key
///    ("OP.2"), for the Japanese Disc mod, which letters it over the Japanese
///    opening and ending.
///
/// The text is Text.cs's, or, with the translation workbook (an .ods) in this
/// mod's folder, the workbook's Mod EN column (Workbook.cs): read at start, and
/// again whenever Import is pressed in the settings, which puts it in place
/// while the game runs.
/// </summary>
public sealed class RetranslationMod : IMod
{
    const string Owner = "kf2.retranslation";
    const uint DrawLabel = 0x80021E10;     // func_80021E10(template, rec): menu-font text
    const uint MainLoop = 0x800140AC;      // stage 9, once a frame in play

    const string PicturesKey = "kf2.retranslation.pictures";
    const string StringsKey = "kf2.retranslation.strings";
    const string EncyclopediaKey = "kf2.retranslation.encyclopedia";
    const string MoviesKey = "kf2.retranslation.movies";
    const string WorkbookKey = "kf2.retranslation.workbook";

    static bool _pictures = true, _strings = true, _encyclopedia = true, _movies = true, _workbook = true;
    static bool _showNotes;
    static bool _restoreStrings;
    static IMemory? _memory;

    public void OnLoad()
    {
        var view = RecompOne.Runtime.Runtime.View;
        _pictures = view.GetBool(PicturesKey, true);
        _strings = view.GetBool(StringsKey, true);
        _encyclopedia = view.GetBool(EncyclopediaKey, true);
        _movies = view.GetBool(MoviesKey, true);
        _workbook = view.GetBool(WorkbookKey, true);
        if (!TextApi.Present)
        {
            Console.WriteLine("[retranslation] the Text mod is not loaded; nothing will change");
            return;
        }
        Workbook.KeepBuiltIn();
        if (_workbook && Workbook.Find() is { } path && Workbook.Read(path) is { } tables) tables.Install();
        SetPictures(_pictures);
        SetHud(_strings);
        SetEncyclopedia(_encyclopedia);
        SetMovies(_movies);
        Console.WriteLine("[retranslation] loaded");
    }

    public void OnUnload()
    {
        SetPictures(false);
        SetHud(false);
        SetEncyclopedia(false);
        SetMovies(false);
        if (_memory != null) Strings.Restore(_memory);
    }

    static void SetPictures(bool on)
    {
        TextApi.ClearPictures(Owner);
        if (!on) return;
        foreach (var (key, text) in Text.Messages)
        {
            int archive = key[0] == 'T' ? 3 : 6;
            if (int.TryParse(key.AsSpan(1), out int entry)) TextApi.SetPicture(Owner, archive, entry, "auto", text);
        }
    }

    /// <summary>The short messages in play (LEVEL UP, 36 GOLD, ...), by number:
    /// the Text mod redraws their page with these. Part of the menus switch.</summary>
    static void SetHud(bool on)
    {
        for (int i = 0; i < Text.Hud.Length; i++) TextApi.SetString("hud:" + i, on ? Text.Hud[i] : null);
    }

    static void SetEncyclopedia(bool on)
    {
        foreach (var (english, ours) in Text.Encyclopedia) TextApi.SetString("encyclopedia:" + english, on ? ours : null);
        foreach (var (english, ours) in Text.EncyclopediaCards) TextApi.SetString("encyclopedia-card:" + english, on ? ours : null);
    }

    static void SetMovies(bool on)
    {
        foreach (var (key, text) in Text.Movies) TextApi.SetString("movie:" + key, on ? text : null);
    }

    /// <summary>Swap in new tables while the game runs: the keys shared with the
    /// Text mod are taken back with the old tables first (a name the new ones no
    /// longer change must not keep its old text), then everything is set again.</summary>
    static void Install(Tables tables)
    {
        SetEncyclopedia(false);
        SetMovies(false);
        tables.Install();
        SetPictures(_pictures);
        SetHud(_strings);
        SetEncyclopedia(_encyclopedia);
        SetMovies(_movies);
        Strings.Refresh();
    }

    [PreHook("game", Address = DrawLabel)]
    static bool BeforeLabel(CpuContext c, IMemory m)
    {
        Keep(m);
        try
        {
            if (_strings) Strings.BeforeDraw(m, c.A1);
            else Strings.Skip();
        }
        catch (Exception e) { Console.WriteLine($"[retranslation] label: {e.Message}"); }
        return true;
    }

    [PostHook("game", Address = DrawLabel)]
    static void AfterLabel(CpuContext c, IMemory m)
    {
        try { Strings.AfterDraw(m); }
        catch (Exception e) { Console.WriteLine($"[retranslation] label: {e.Message}"); }
    }

    [PostHook("game", Address = MainLoop)]
    static void AfterMainLoop(CpuContext c, IMemory m) => Keep(m);

    static void Keep(IMemory m)
    {
        _memory = m;
        try
        {
            if (_restoreStrings) { Strings.Restore(m); _restoreStrings = false; }
            if (_strings) Strings.Ensure(m);
        }
        catch (Exception e) { Console.WriteLine($"[retranslation] strings: {e.Message}"); }
    }

    public void DrawSettings()
    {
        if (!TextApi.Present)
        {
            ImGui.TextWrapped("The Text mod is not loaded. Install it (mods/text) and switch it on.");
            return;
        }
        ImGui.TextWrapped("A new translation from the Japanese release, drawn in the game's own lettering by the Text mod. "
                        + "Switching a part off puts the original text back.");
        if (ImGui.Checkbox("Signs, dialogue and descriptions", ref _pictures)) { Persist(PicturesKey, _pictures); SetPictures(_pictures); }
        if (ImGui.Checkbox("Menus, items, equipment and magic names, and messages in play", ref _strings))
        {
            Persist(StringsKey, _strings);
            if (!_strings) _restoreStrings = true;
            SetHud(_strings);
        }
        ImGui.TextDisabled(Strings.Applied ? $"  {Strings.Written} strings in place" : "  applied in play");
        if (ImGui.Checkbox("Encyclopedia names, titles and cards", ref _encyclopedia))
        {
            Persist(EncyclopediaKey, _encyclopedia);
            SetEncyclopedia(_encyclopedia);
        }
        if (ImGui.Checkbox("Opening and ending text (with the Japanese Disc mod)", ref _movies))
        {
            Persist(MoviesKey, _movies);
            SetMovies(_movies);
        }
        DrawWorkbook();
    }

    static void DrawWorkbook()
    {
        ImGui.Separator();
        ImGui.TextUnformatted("Translation workbook");
        string? found = Workbook.Find();
        if (found == null)
            ImGui.TextWrapped($"No workbook here. Put KF2_retranslation.ods in {System.IO.Path.GetFullPath(Workbook.Folder)} "
                            + "to use its Mod EN column instead of the built-in text.");
        else
        {
            if (ImGui.Button("Import from workbook") && Workbook.Read(found) is { } tables) Install(tables);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Read the Mod EN column of {System.IO.Path.GetFileName(found)} and use it now. "
                               + "Save the workbook in LibreOffice first: this reads the file as it is on disk.");
            if (Workbook.File != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Use the built-in text")) Install(Workbook.BuiltIn());
            }
            if (ImGui.Checkbox("Read the workbook at start", ref _workbook)) Persist(WorkbookKey, _workbook);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Off: the built-in text is used until you press Import.");
        }
        ImGui.TextWrapped(Workbook.Status);
        if (Workbook.Stale())
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.8f, 0.3f, 1f), "The workbook has been saved since it was read: press Import to use the changes.");
        if (Workbook.Notes.Count > 0)
        {
            ImGui.Checkbox($"Show the {Workbook.Notes.Count} notes", ref _showNotes);
            if (_showNotes)
                foreach (var n in Workbook.Notes) ImGui.TextWrapped("  " + n);
        }
    }

    static void Persist(string key, bool value)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }
}
