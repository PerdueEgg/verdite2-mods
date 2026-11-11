// ModCompiler compiles mods with no implicit usings, so every namespace the
// file needs must be named here -- including System.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ImGuiNET;
using RecompOne.Runtime.Cdrom;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.JpDisc;

/// <summary>
/// The Japanese release's title screen, opening and ending, read off the player's
/// own King's Field II (Japan) disc image while the US game runs.
///
/// ---- the movies ----
///
/// The port streams an STR movie in its own thread (LibCdStream), reading the
/// sectors straight off the disc image from the start the game found with the US
/// directory. So the movies are swapped beneath the game: the US image the port
/// reads (the private field DiscFs._image of Runtime.Cd.Fs) is wrapped by
/// <see cref="Remapped"/>, which answers a sector of a swapped US movie with the
/// same sector of its Japanese twin:
///
///   US OP\OP2.S (the prologue, 1089 frames)  from  JP OP\OP.S (the same 1089)
///   US OP\ED0.S (the ending, 1700 frames)    from  JP OP\ED.S (the same 1700, then
///                                                  441 of the Japanese staff credits)
///
/// The footage is the same frame for frame; only the burned-in text differs. The
/// Japanese movies are 10 sectors a frame and carry no sound, the US ones 13 to 15
/// with the narrated XA sound interleaved, and the port paces a movie by the
/// sectors it reads, so the Japanese ones play at their own speed (15 frames a
/// second, against the US 10) and in silence; Music.cs plays their music as the
/// Japanese game does. Past the end of the Japanese file the US file's sectors are
/// answered blank, so none of its narration is ever read.
///
/// The US logos before the opening (OP0.S, OP1.S) and the US ending's second part
/// (ED1.S: the four spells, the US staff and localization credits, "The End") are
/// the US's own and have no Japanese twin. ED1.S plays after the Japanese ending by
/// default; a setting leaves it out, and then the last Japanese frame is held, as
/// the Japanese END.EXE holds it.
///
/// ---- the rest ----
///
/// Title.cs: the title screen. Hooks.cs: the movies' music, the ending's length.
/// MovieText.cs: the English over the movies' text cards.
/// </summary>
public sealed partial class JpDiscMod : IMod
{
    const string PathKey = "kf2.jpdisc.path", TitleKey = "kf2.jpdisc.title", OpeningKey = "kf2.jpdisc.opening",
                 EndingKey = "kf2.jpdisc.ending", UsCreditsKey = "kf2.jpdisc.uscredits", InkKey = "kf2.jpdisc.ink", StyleKey = "kf2.jpdisc.style";
    static readonly string[] Styles = ["dialogue", "card"];

    // US movie, its Japanese twin.
    const string Opening = "OP/OP2.S", Ending = "OP/ED0.S";
    static readonly (string Us, string Jp)[] Twins = [(Opening, "OP/OP.S"), (Ending, "OP/ED.S")];

    static string _path = "";
    static bool _title = true, _opening = true, _ending = true, _usCredits = false;
    static string _status = "no Japanese disc image chosen";

    static DiscFs? _jp;                 // the Japanese disc, open while the game runs
    static Remapped? _remap;            // installed over the US image
    static DiscFs? _installedOn;
    static byte[]? _opd, _edd;          // the JP OP\OP.D and OP\ED.D: the music
    static readonly List<string> _swapped = new();
    static string _installedFor = "";

    public void OnLoad()
    {
        var v = RecompOne.Runtime.Runtime.View;
        _path = v.GetString(PathKey, "");
        _title = v.GetBool(TitleKey, true);
        _opening = v.GetBool(OpeningKey, true);
        _ending = v.GetBool(EndingKey, true);
        _usCredits = v.GetBool(UsCreditsKey, false);
        MovieText.Ink = (byte)Math.Clamp(v.GetInt(InkKey, 205), 64, 255);
        string st = v.GetString(StyleKey, "dialogue");
        MovieText.Style = Array.IndexOf(Styles, st) >= 0 ? st : "dialogue";
        Open();
        Event.AddListener(_onVSync);
        Event.AddListener(_onOverlay);
        Console.WriteLine("[jpdisc] loaded");
    }

    public void OnUnload()
    {
        Event.RemoveListener(_onVSync);
        Event.RemoveListener(_onOverlay);
        Music.Detach();
        MovieText.Stop();
        Uninstall();
        _jp?.Dispose(); _jp = null; Jp.Image = null;
    }

    static readonly Action<VSyncEvent> _onVSync = _ =>
    {
        try { Install(); } catch (Exception e) { _status = e.Message; }
        try { if (_jp != null) MovieText.Letter(); } catch (Exception e) { MovieText.Status = "lettering: " + e.Message; }
        try { Music.Tick(); EndingMusic(); } catch (Exception e) { Music.Status = "the music: " + e.Message; Music.Stop(); }
    };

    static readonly Action<OverlayLoadedEvent> _onOverlay = e =>
    {
        // A new executable: whatever was playing belongs to the old one.
        Music.Stop();
        _endMovie = -1; _openMovie = -1;
    };

    static (int Lba, int Count)? JpFile(string name) =>
        _jp != null && _jp.Locate(name, out int l, out uint n) ? (l, (int)((n + 2047) / 2048)) : null;

    /// <summary>The disc is King's Field II (Japan): its own boot file, the Japanese
    /// opening and ending, and its COPY.TXT.</summary>
    static bool IsJapaneseKf2(DiscFs fs)
    {
        if (!fs.Locate("PSX.EXE", out _, out _) || !fs.Locate("OP/OP.S", out _, out _) || !fs.Locate("OP/ED.S", out _, out _)) return false;
        try { return Encoding.ASCII.GetString(fs.ReadFile("COPY.TXT")).Contains("King's Field 2"); }
        catch { return false; }
    }

    /// <summary>A Japanese image where one is likely to be, when none is chosen: the
    /// .cue and .chd files beside the US disc image, then in this mod's folder.</summary>
    static string? Find()
    {
        var dirs = new List<string>();
        try
        {
            string us = RecompOne.Runtime.Runtime.CdPath;
            if (!string.IsNullOrWhiteSpace(us) && Path.GetDirectoryName(Path.GetFullPath(us)) is { } d) dirs.Add(d);
        }
        catch { }
        try
        {
            foreach (var e in ModLoader.Mods)
                if (string.Equals(e.Info.Id, "kf2.jpdisc", StringComparison.OrdinalIgnoreCase) && Directory.Exists(e.Info.SourcePath))
                    dirs.Add(e.Info.SourcePath);
        }
        catch { }
        dirs.Add(Path.Combine("mods", "jpdisc"));
        string usFull = "";
        try { usFull = Path.GetFullPath(RecompOne.Runtime.Runtime.CdPath); } catch { }
        foreach (var d in dirs)
        {
            if (!Directory.Exists(d)) continue;
            var files = new List<string>();
            try { files.AddRange(Directory.GetFiles(d, "*.cue")); files.AddRange(Directory.GetFiles(d, "*.chd")); } catch { continue; }
            foreach (var f in files)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(f), usFull, StringComparison.OrdinalIgnoreCase)) continue;
                    using var fs = DiscFs.Open(f);
                    if (IsJapaneseKf2(fs)) return f;
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>Open the Japanese image and check it is King's Field II (Japan).</summary>
    static void Open()
    {
        if (string.IsNullOrWhiteSpace(_path) && Find() is { } found)
        {
            _path = found;
            RecompOne.Runtime.Runtime.View.SetString(PathKey, _path);
            RecompOne.Runtime.Runtime.SaveView();
            Console.WriteLine($"[jpdisc] found the Japanese disc: {found}");
        }
        Uninstall();
        MovieText.Stop();
        _jp?.Dispose(); _jp = null; _opd = null; _edd = null; Jp.Image = null;
        if (string.IsNullOrWhiteSpace(_path)) { _status = "no Japanese disc image found beside the US one or in this mod's folder: choose one"; return; }
        try
        {
            if (!File.Exists(_path)) { _status = "not found: " + _path; return; }
            var fs = DiscFs.Open(_path);
            if (!IsJapaneseKf2(fs))
            {
                fs.Dispose();
                _status = "that image is not King's Field II (Japan)";
                return;
            }
            _jp = fs;
            Jp.Image = fs.Image;
            try { _opd = fs.ReadFile("OP/OP.D"); } catch { }
            try { _edd = fs.ReadFile("OP/ED.D"); } catch { }
            _status = "Japanese disc ready";
            MovieText.Prepare(JpFile);
        }
        catch (Exception e) { _status = "could not open the image: " + e.Message; }
    }

    static readonly FieldInfo? ImageField = typeof(DiscFs).GetField("_image", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Wrap the US image the port reads, once it is there (and again if the
    /// port opens it anew, or the movies switched on change).</summary>
    static void Install()
    {
        var cd = RecompOne.Runtime.Runtime.Cd;
        if (cd == null || _jp == null) return;
        string want = (_opening ? "o" : "") + (_ending ? "e" : "");
        var fs = cd.Fs;
        if (ReferenceEquals(fs, _installedOn) && fs.Image is Remapped && want == _installedFor) return;
        if (want.Length == 0) { Uninstall(); _installedFor = want; return; }
        if (ImageField == null) { _status = "this port's disc reader has changed: movies not swapped"; return; }
        var us = fs.Image is Remapped r ? r.Us : fs.Image;
        var map = new List<Remapped.Range>();
        _swapped.Clear();
        foreach (var (usName, jpName) in Twins)
        {
            if (usName == Opening ? !_opening : !_ending) continue;
            if (!fs.Locate(usName, out int ul, out uint usz) || !_jp.Locate(jpName, out int jl, out uint jsz)) continue;
            map.Add(new Remapped.Range(ul, (int)((usz + 2047) / 2048), jl, (int)((jsz + 2047) / 2048)));
            _swapped.Add(usName.Substring(usName.IndexOf('/') + 1) + " from " + jpName.Substring(jpName.IndexOf('/') + 1));
        }
        _remap = new Remapped(us, map);
        ImageField.SetValue(fs, _remap);
        _installedOn = fs;
        _installedFor = want;
        _status = $"Japanese disc ready: {string.Join(", ", _swapped)}";
        Console.WriteLine($"[jpdisc] movies from the Japanese disc: {string.Join(", ", _swapped)}");
    }

    static void Uninstall()
    {
        if (_installedOn != null && ImageField != null && _installedOn.Image is Remapped r)
            ImageField.SetValue(_installedOn, r.Us);
        _installedOn = null;
        _remap = null;
        _installedFor = "";
    }

    static bool Swapped(string usMovie) =>
        _remap != null && _jp != null && (usMovie == Opening ? _opening : _ending);

    public void DrawSettings()
    {
        ImGui.TextWrapped("King's Field's Japanese title screen, opening and ending (King's Field II in Japan), the movies read from your " +
                          "Japanese disc image as the game plays, with their own music in place of the US narration. With Retranslation, " +
                          "the movies' text is in the new English.");
        string p = _path;
        if (ImGui.InputText("Japanese disc image (.cue or .chd)", ref p, 1024)) _path = p;
        if (ImGui.Button("Browse..."))
        {
            string? picked = Pick();
            if (picked != null) { _path = picked; Use(); }
        }
        ImGui.SameLine();
        if (ImGui.Button("Use this image")) Use();
        ImGui.TextDisabled(_status);
        ImGui.Separator();
        if (ImGui.Checkbox("Japanese title screen", ref _title)) Save(TitleKey, _title);
        ImGui.TextDisabled("  the painting and logo; works without the Japanese image (the US disc carries them too)");
        if (ImGui.Checkbox("Japanese opening, with its music", ref _opening)) Save(OpeningKey, _opening);
        if (ImGui.Checkbox("Japanese ending, with its music and the Japanese staff credits", ref _ending)) Save(EndingKey, _ending);
        bool off = !_ending;
        if (off) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Then the US ending's second part (the four spells, the US and localization credits, The End)", ref _usCredits)) Save(UsCreditsKey, _usCredits);
        if (off) ImGui.EndDisabled();
        ImGui.TextDisabled("  off: the last Japanese frame is held, as the Japanese game holds it");
        ImGui.TextDisabled("Movie changes apply from the next movie; the title screen from the next time it loads.");
        ImGui.TextDisabled(Music.Playing ? "Music: " + Music.Status : "Music: " + (Music.Status.Length > 0 ? Music.Status : "not playing"));
        ImGui.Separator();
        ImGui.TextWrapped("The movies' English (from Retranslation): the Japanese text is taken out of each card's frames, the scene under " +
                          "it taken from the US movie's same frame, and the English drawn in.");
        ImGui.TextDisabled(MovieText.Status);
        int ink = MovieText.Ink;
        if (ImGui.SliderInt("Lettering brightness", ref ink, 64, 255))
        {
            MovieText.Ink = (byte)ink;
            RecompOne.Runtime.Runtime.View.SetInt(InkKey, ink);
            RecompOne.Runtime.Runtime.SaveView();
            MovieText.Redraw();
        }
        int si = Math.Max(0, Array.IndexOf(Styles, MovieText.Style));
        if (ImGui.Combo("Lettering font", ref si, ["Dialogue", "Signs (larger)"], Styles.Length))
        {
            MovieText.Style = Styles[si];
            RecompOne.Runtime.Runtime.View.SetString(StyleKey, MovieText.Style);
            RecompOne.Runtime.Runtime.SaveView();
            MovieText.Redraw();
        }
    }

    static void Use()
    {
        RecompOne.Runtime.Runtime.View.SetString(PathKey, _path);
        RecompOne.Runtime.Runtime.SaveView();
        Open();
    }

    /// <summary>The system's file picker, as the port's own disc picker opens it
    /// (NativeFileDialogNET: new NativeFileDialog().SelectFile().AddFilter(...).Open(out
    /// path, folder)), found by name since a mod is built without it. Null when it is
    /// cancelled or not there (then the path can still be typed).</summary>
    static string? Pick()
    {
        try
        {
            Type? t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if ((t = asm.GetType("NativeFileDialogNET.NativeFileDialog", false)) != null) break;
            if (t == null) { _status = "no file picker in this build: type the path"; return null; }
            object? dlg = Activator.CreateInstance(t);
            object? sel = t.GetMethod("SelectFile", Type.EmptyTypes)?.Invoke(dlg, null) ?? dlg;
            sel = sel!.GetType().GetMethod("AddFilter", new[] { typeof(string), typeof(string) })?.Invoke(sel, new object[] { "Disc images", "cue,chd" }) ?? sel;
            string? dir = null;
            try { if (!string.IsNullOrWhiteSpace(_path)) dir = Path.GetDirectoryName(_path); } catch { }
            // Open(out string? path, string? folder, ...): the one-path overload, any
            // further parameters left at their defaults.
            MethodInfo? open = null;
            foreach (var mi in sel.GetType().GetMethods())
            {
                var ps = mi.GetParameters();
                if (mi.Name != "Open" || ps.Length < 1 || !ps[0].ParameterType.IsByRef) continue;
                if (ps[0].ParameterType.GetElementType() != typeof(string)) continue;
                bool rest = true;
                for (int i = 2; i < ps.Length; i++) rest &= ps[i].HasDefaultValue;
                if (rest) { open = mi; break; }
            }
            if (open == null) { _status = "the file picker has changed: type the path"; return null; }
            var pars = open.GetParameters();
            var args = new object?[pars.Length];
            if (pars.Length > 1) args[1] = dir;
            for (int i = 2; i < pars.Length; i++) args[i] = pars[i].DefaultValue;
            object? result = open.Invoke(sel, args);
            (sel as IDisposable)?.Dispose();
            if (dlg != sel) (dlg as IDisposable)?.Dispose();
            string? picked = args[0] as string;
            return result?.ToString() == "Okay" && !string.IsNullOrWhiteSpace(picked) ? picked : null;
        }
        catch (Exception e) { _status = "file picker: " + (e.InnerException ?? e).Message; return null; }
    }

    static void Save(string key, bool v)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, v);
        RecompOne.Runtime.Runtime.SaveView();
    }
}

/// <summary>The US disc image with the sectors of some files answered from the
/// Japanese image: US file sector k is JP file sector k while the JP file lasts,
/// and blank after it.</summary>
sealed class Remapped : IDiscImage
{
    public readonly record struct Range(int UsLba, int UsCount, int JpLba, int JpCount);

    public readonly IDiscImage Us;
    readonly Range[] _map;

    public Remapped(IDiscImage us, List<Range> map) { Us = us; _map = map.ToArray(); }

    /// <summary>-1: not a swapped sector; -2: a swapped file's, past the Japanese end.</summary>
    int Map(int lba)
    {
        foreach (var r in _map)
            if (lba >= r.UsLba && lba < r.UsLba + r.UsCount)
            {
                int k = lba - r.UsLba;
                if (Jp.Image == null) return -1;
                return k < r.JpCount ? r.JpLba + k : -2;
            }
        return -1;
    }

    static byte[]? Lettered(int jp)
    {
        try { return MovieText.Patched(jp); }
        catch (Exception e) { Console.WriteLine($"[jpdisc] lettering: {e.Message}"); return null; }
    }

    // A blank data sector: no video magic and not XA, so the stream steps over it.
    static byte[] Blank(int size)
    {
        var b = new byte[size];
        if (size == 2336) { b[2] = b[6] = 0x08; }
        return b;
    }

    public byte[] ReadSectorData(int lba, int size)
    {
        int jp = Map(lba);
        if (jp == -1) return Us.ReadSectorData(lba, size);
        if (jp == -2) return Blank(size);
        var u = (size == 2048 || size == 2336) ? Lettered(jp) : null;
        if (u == null) return Jp.Read(jp, size);
        if (size == 2048) return (byte[])u.Clone();
        var s = Jp.Read(jp, 2336);
        Buffer.BlockCopy(u, 0, s, 8, 2048);
        return s;
    }

    public byte[] ReadRawSector(int lba)
    {
        int jp = Map(lba);
        if (jp == -1) return Us.ReadRawSector(lba);
        if (jp == -2)
        {
            var b = (byte[])Us.ReadRawSector(lba).Clone();
            if (b.Length >= 24) { Array.Clear(b, 24, b.Length - 24); b[18] = b[22] = 0x08; }
            return b;
        }
        var u = Lettered(jp);
        var s = Jp.Raw(jp);
        if (u != null && s.Length >= 24 + 2048) Buffer.BlockCopy(u, 0, s, 24, 2048);
        return s;
    }

    public string Format => Us.Format;
    public int FirstTrack => Us.FirstTrack;
    public int LastTrack => Us.LastTrack;
    public bool HasTracks => Us.HasTracks;
    public int LeadoutLba => Us.LeadoutLba;
    public int DataSectors => Us.DataSectors;
    public IReadOnlyList<DiscTrack> Tracks => Us.Tracks;
    public bool TrackStartLba(int track, out int lba) => Us.TrackStartLba(track, out lba);
    public void Dispose() => Us.Dispose();   // the port disposes its disc through us; the JP image is the mod's
}
