using System;
using RecompOne.Runtime.Context;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// Place cards the disc does not have, and every name this mod shows, through the
/// port's lettering: Verdite2's built-in text, which publishes the "kf2.text" slot
/// (TextApi.cs). Nothing else is needed.
///
/// A custom card: the Truth Glass's place call is pointed at an ordinary place
/// card (510, the same 256x256 4-bit layout and palette), and the lettering is
/// asked to redraw the very next picture with the card's text, in the cards' own
/// font. Only while it can: in the CLASSIC font, without the lettering, or in a
/// Japanese run (whose pictures are the Japanese disc's own) such a place shows no
/// card, as the Underworld Prisons' signs would not either way, but still unlocks.
///
/// Wording: a translation can put its own words in the lettering's string table,
/// as "encyclopedia:" + this mod's English for names, titles and areas, and
/// "encyclopedia-card:" + it for a custom card, as the port's Retranslation does.
/// Without one, the English here is shown as written.
/// </summary>
public sealed partial class EncyclopediaMod
{
    const uint Placeholder = 510;               // any single place card: same size, layout and palette

    /// <summary>A name, title or area as the installed translation has it.</summary>
    static string L(string english) => TextApi.GetString("encyclopedia:" + english) ?? english;

    static string CardText(string english) => TextApi.GetString("encyclopedia-card:" + english) ?? english;

    /// <summary>The custom card for a place, when one can be lettered now; else null.</summary>
    static string? CustomCardOf(int place) =>
        Lettered && !JapaneseRun() ? Zones.CustomCard(place) : null;

    static System.Reflection.PropertyInfo? _japaneseActive;
    static bool _lookedForJapanese;

    /// <summary>The port is running in Japanese (PORT SETTINGS ▸ GAMEPLAY ▸ LANGUAGE),
    /// where it leaves the pictures to the Japanese disc. Found by name, since a mod
    /// is built without the port's own types.</summary>
    static bool JapaneseRun()
    {
        if (!_lookedForJapanese)
        {
            _lookedForJapanese = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetType("Kf2.Japanese.JapaneseDisc") is { } t)
                    {
                        _japaneseActive = t.GetProperty("Active", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        break;
                    }
            }
            catch (Exception e) { Console.WriteLine($"[encyclopedia] looking for the Japanese language: {e.Message}"); }
        }
        try { return _japaneseActive?.GetValue(null) is true; }
        catch { return false; }
    }

    /// <summary>Called at the Truth Glass's place call: show a custom card in
    /// place of the entry it was about to show.</summary>
    static void ShowCustomCard(CpuContext c, string text)
    {
        TextApi.NextPicture("card", CardText(text));
        c.A1 = Placeholder;
    }
}
