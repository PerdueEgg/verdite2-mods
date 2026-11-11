using System;
using System.Collections.Generic;
using RecompOne.Runtime.Memory;

namespace Kf2.Mods.Retranslation;

/// <summary>
/// The Text mod's functions (mods/text), looked up where it publishes them: the
/// process data slot "kf2.text". The same file goes in every mod that uses it,
/// with only the namespace changed. A mod that needs the Text mod lists "kf2.text"
/// under "dependencies" in its mod.json, so it loads first; a mod that only uses it
/// when it is there can leave it out, since every call looks the Text mod up as it
/// is made and gives a plain answer when it is missing. See TextMod.cs in the Text
/// mod for the list.
/// </summary>
static class TextApi
{
    static IDictionary<string, object>? Api => AppDomain.CurrentDomain.GetData("kf2.text") as IDictionary<string, object>;

    static T? Get<T>(string name) where T : class =>
        Api is { } api && api.TryGetValue(name, out var f) ? f as T : null;

    /// <summary>The Text mod is loaded.</summary>
    public static bool Present => Api != null;

    /// <summary>The fonts are loaded (reads the disc the first time).</summary>
    public static bool Ready => Get<Func<bool>>("Ready")?.Invoke() ?? false;

    /// <summary>Lower case is in the menu font (until then text is encoded in capitals).</summary>
    public static bool MenuReady => Get<Func<bool>>("MenuReady")?.Invoke() ?? false;

    /// <summary>The menu font cell for a character, -1 if it has none.</summary>
    public static int MenuIndex(char c) => Get<Func<char, int>>("MenuIndex")?.Invoke(c) ?? -1;

    /// <summary>The character a menu font cell shows, '\0' if none.</summary>
    public static char MenuChar(int index) => Get<Func<int, char>>("MenuChar")?.Invoke(index) ?? '\0';

    /// <summary>Text as menu font cells, at most <paramref name="max"/>, then 0xFF.</summary>
    public static byte[] MenuEncode(string text, int max) =>
        Get<Func<string, int, byte[]>>("MenuEncode")?.Invoke(text, max) ?? [0xFF];

    /// <summary>The game's own capitals (SHORT SWORD) as the Text mod draws them
    /// (Short Sword, with its normal case on); anything else as it is.</summary>
    public static string MenuShown(string text) => Get<Func<string, string>>("MenuShown")?.Invoke(text) ?? text;

    /// <summary>A string as menu cells, each cell's offset from the first, and the
    /// width, spaced as the menus are. Null when the Text mod is not loaded.</summary>
    public static (int[] Cells, int[] X, int Width)? MenuPlace(string text) =>
        Get<Func<string, (int[] Cells, int[] X, int Width)>>("MenuPlace")?.Invoke(text);

    /// <summary>A drop shadow under a menu letter quad just linked onto an ordering
    /// slot, when the Text mod's menu shadow is on.</summary>
    public static void MenuShadow(IMemory m, uint quad, int slot) => Get<Action<IMemory, uint, int>>("MenuShadow")?.Invoke(m, quad, slot);

    /// <summary>Redraw a disc picture with this text wherever the game shows it.
    /// Style: "dialogue", "card" or "auto".</summary>
    public static void SetPicture(string owner, int archive, int entry, string style, string text) =>
        Get<Action<string, int, int, string, string>>("SetPicture")?.Invoke(owner, archive, entry, style, text);

    /// <summary>Forget every picture this owner set.</summary>
    public static void ClearPictures(string owner) => Get<Action<string>>("ClearPictures")?.Invoke(owner);

    /// <summary>Redraw the very next picture shown with this text.</summary>
    public static void NextPicture(string style, string text) => Get<Action<string, string>>("NextPicture")?.Invoke(style, text);

    /// <summary>How many lines the text wraps to in that style.</summary>
    public static int Lines(string style, string text) => Get<Func<string, string, int>>("Lines")?.Invoke(style, text) ?? 0;

    /// <summary>Text wrapped to a width and drawn as ink coverage, a byte a pixel, in
    /// a picture style's font ("dialogue" or "card"). Null when the Text mod is not loaded.</summary>
    public static (byte[] Ink, int W, int H)? Coverage(string style, string text, int width) =>
        Get<Func<string, string, int, (byte[] Ink, int W, int H)>>("Coverage") is { } f ? f(style, text, width) : null;

    /// <summary>The shared string table: set (null removes) and get.</summary>
    public static void SetString(string key, string? value) => Get<Action<string, string?>>("SetString")?.Invoke(key, value);
    public static string? GetString(string key) => Get<Func<string, string?>>("GetString")?.Invoke(key);
}
