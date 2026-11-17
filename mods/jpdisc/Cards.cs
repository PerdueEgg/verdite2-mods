// The movies' text cards: the Retranslation workbook's Movies sheet, with frames
// counted as the STR sectors number them (from 1), and their English. Found by comparing each Japanese frame with
// its US twin; each range is where the Japanese text shows at all, and the work
// takes eight frames either side.
using System.Collections.Generic;

namespace Kf2.Mods.JpDisc;

static class Cards
{
    /// <summary>Key, the Japanese movie, its US twin, first and last frame.</summary>
    public static readonly (string Key, string Movie, string Us, int First, int Last)[] All =
    [
        ("OP.1", "OP/OP.S", "OP/OP2.S", 139, 244),
        ("OP.2", "OP/OP.S", "OP/OP2.S", 335, 459),
        ("OP.3", "OP/OP.S", "OP/OP2.S", 592, 710),
        ("OP.4", "OP/OP.S", "OP/OP2.S", 808, 984),
        ("ED.1", "OP/ED.S", "OP/ED0.S", 166, 244),
        ("ED.2", "OP/ED.S", "OP/ED0.S", 566, 830),
        ("ED.3", "OP/ED.S", "OP/ED0.S", 1043, 1372),
    ];

    /// <summary>Each card's English: the Retranslation's (the workbook's Mod EN). A
    /// translation can give its own through the port's text string table, as
    /// "movie:" + the key, and that is used instead.</summary>
    public static readonly Dictionary<string, string> English = new()
    {
        ["OP.1"] = "Shrouded in darkness for all eternity, the island was said to be the dwelling of a god or the lair of a demon. None dared approach it.",
        ["OP.2"] = "Long, long ago, the High Elves of the forest tell of a blue light that fell from the heavens, then sank deep into the earth beneath the island.",
        ["OP.3"] = "Since ancient times, all who have meddled with the island have lost their power and perished, leaving only traces of their passing.",
        ["OP.4"] = "\"In darkness, the Slumbering One waits only for the hour of his awakening,\" the legend goes on. None must ever draw near...",
        ["ED.1"] = "At last, the darkness that shrouded the island vanished, and dazzling sunlight flooded the land.",
        ["ED.2"] = "For a while, Aleph stood gazing up at the heavens, then at last, he began to walk. Now that he held the other \"holy sword,\" it no longer mattered to him whether the two dragons were gods or demons. For his friend's sword would remain a holy sword for as long as his people never doubted it...",
        ["ED.3"] = "From then on, the monsters on the continent vanished as well, and neither Seath nor Ghirah was ever seen in the northern lands again. The two swords were enshrined in Verdite and sealed away, with prayers that this time peace would endure forever.",
    };
}
