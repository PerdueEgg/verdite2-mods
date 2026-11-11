// The movies' text cards: the workbook's Movies sheet, with frames counted as the
// STR sectors number them (from 1). Found by comparing each Japanese frame with
// its US twin; each range is where the Japanese text shows at all, and the work
// takes eight frames either side.
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
}
