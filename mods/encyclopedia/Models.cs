using System;
using System.Collections.Generic;

namespace Kf2.Mods.Encyclopedia;

/// <summary>How a model is shown: a scale on top of the automatic fit, a
/// position, a tilt, the clip it plays, whether it stands still at a facing
/// (models made to be seen from the front), and the clip time it is held at.
/// A part's own tuning only gives its clip and hold: it is drawn at its body's
/// size and tilt.</summary>
sealed class Tune
{
    public double Scale = 1.0;
    public int X, Y, Tilt, Clip;
    public bool Fixed;              // does not turn
    public int Facing;              // the yaw it stands at, or turns from
    public int Hold = -1;           // the clip time it is held at, 0-4095; -1 plays
}

/// <summary>
/// The model each entry shows, and how.
///
/// Model numbers are MO.T entries: 0-127 the creatures and people (the creature
/// ID, Truth Glass entry - 240), 128 on the objects. From 1000 an item, shown by
/// the game's own item preview (1000 + the item id).
/// </summary>
static class Models
{
    public const int ItemBase = 1000;

    /// <summary>Enemies and people, by their first description entry.</summary>
    public static readonly Dictionary<int, int> ForEntry = new()
    {
        // Enemies
        [269] = 29,    // Kraken, with its part
        [242] = 2,     // Sigill
        [243] = 2,     // Deep-Sea Sigill: it has the shore one's model
        [244] = 4,     // Baltail, with its tail
        [248] = 9,     // Fire-Elemental
        [240] = 0,     // Green Slime
        [254] = 14,    // Head Eater
        [256] = 16,    // S-Knight
        [261] = 21,    // Skeleton
        [250] = 10,    // Ghosts
        [241] = 1,     // Poison Slime
        [257] = 17,    // Necron's Soldiers
        [258] = 18,    // Archers
        [262] = 22,    // Termite
        [265] = 25,    // Queen Termite
        [251] = 11,    // Reiks
        [274] = 34,    // Copper Knight
        [285] = 45,    // The Picture of a King
        [281] = 41,    // Flame Spirit
        [280] = 40,    // Salamander
        [264] = 24,    // Psythe
        [255] = 15,    // Mogle
        [272] = 32,    // Refma (blue)
        [291] = 51,    // Refma (Red)
        [246] = 6,     // Poison Baltail, with its tail
        [267] = 27,    // Log Stalker
        [263] = 23,    // Kald
        [271] = 31,    // Earth-Elemental
        [289] = 49,    // Mage's Candlesticks
        [282] = 42,    // Billent
        [273] = 33,    // Tarn
        [266] = 26,    // Freeze Ball
        [268] = 28,    // Demon Lord
        [275] = 35,    // Dias Bagil
        [287] = 47,    // Mecha-Termite
        [288] = 48,    // Mecha-Reiks
        [286] = 46,    // Mecha-Demon Lord
        [290] = 50,    // Bitt
        [279] = 39,    // Guyra

        // People
        [307] = 67,    // Jose Harven
        [306] = 66,    // Raffy Foss
        [310] = 70,    // Nola Bagil
        [323] = 83,    // Al Hunt
        [325] = 85,    // Fai Fadlin
        [327] = 87,    // Rand Ferrer
        [335] = 95,    // Mark Wozz
        [346] = 106,   // Earnest Clyde
        [304] = 64,    // Celffy Foss
        [313] = 73,    // Meryl
        [330] = 90,    // Radd Bilheim
        [331] = 91,    // Krola Amgun
        [333] = 94,    // Sandler Amgun (second model)
        [337] = 97,    // Kehl Hunt
        [314] = 76,    // Gigi Budwell (second model)
        [317] = 79,    // Teo Budwell (third model)
        [320] = 80,    // Karen Shore
        [321] = 82,    // Leon Shore (second model)
        [324] = 84,    // Harris Carvitto
        [345] = 105,   // The Grave of a Saint
        [347] = 107,   // Dragon Grass
        [326] = 86,    // Cliff Lore
        [328] = 88,    // David Tabler
        [329] = 89,    // Dalf Vice
        [284] = 44,    // Skeleton of One-Eyed Giant
        [349] = 109,   // Miria
    };

    /// <summary>Places, by place number (description entry - 510). A place's
    /// model takes its textures from the place's area.</summary>
    public static readonly Dictionary<int, int> ForPlace = new()
    {
        [0] = 323,               // West Seaside
        [1] = ItemBase + 67,     // The Pirate's Cave: the Pirate's Map
        [2] = ItemBase + 101,    // The Cave of Old-Hand: the Skull Key
        [3] = 291,               // The Mage's Lighthouse
        [40] = 29,               // Kraken's Nest: the Kraken
        [4] = 70,                // The North Village: Nola Bagil
        [5] = 66,                // The South Village: Raffy Foss
        [6] = 391,               // A Small Mine
        [7] = 279,               // The North Graveyard
        [8] = 17,                // Base No. 1: Necron's Soldiers
        [22] = 318,              // Seath's Fountain: the fountain, with its water
        [26] = 284,              // The South Graveyard
        [41] = 96,               // The Underworld Prison No. 1: Earnest Clyde
        [9] = 285,               // Base No. 2
        [14] = 45,               // The Ruins of King Harvine's Castle: the Picture of a King
        [15] = 22,               // The Termite's Nest: Termite
        [18] = 10,               // The Window of Homesickness: Ghosts
        [36] = 64,               // The Village of the Wind: Celffy Foss
        [10] = 18,               // Base No. 3: Archers
        [16] = 21,               // The Cemetery: Skeleton
        [17] = 280,              // The Soldier's Cemetery
        [19] = 281,              // The Elf Graveyard
        [20] = 78,               // The East Village: Teo Budwell
        [37] = 277,              // The East Seaside
        [11] = 18,               // Base No. 4: Archers
        [21] = 105,              // The Elf Shrine: the Grave of a Saint
        [23] = 34,               // The Magic Palace of Fire: Copper Knight
        [24] = 33,               // The Magic Palace of Ice: Tarn
        [42] = 81,               // The Underworld Prison No. 2: Leon Shore
        [43] = 251,              // Royal Treasure House: a treasure chest, with its lid
        [12] = 17,               // Base No. 5: Necron's Soldiers
        [27] = 305,              // The Big Mine
        [28] = 274,              // The Poisoned Cave
        [29] = 23,               // The Cave of the Darkside: Kald
        [30] = 227,              // The Elf Cave
        [31] = 31,               // The Cave of The Earth Soul: Earth-Elemental
        [32] = 18,               // The Lookout: Archers
        [13] = 329,              // Base No. 6
        [33] = 393,              // Necron's Coliseum
        [35] = 404,              // The Research Office
        [44] = 28,               // Space for Completion: Demon Lord
        [45] = 359,              // Guyra's Dimension
        [25] = 282,              // The Demon's Grave
        [34] = 24,               // The Warrior's Graveyard: Psythe
    };

    /// <summary>Parts drawn with a body, each at its place in the body's space
    /// (model units: X across, Y down, Z along).</summary>
    public static readonly Dictionary<int, (int Part, int X, int Y, int Z)[]> Parts = new()
    {
        [29] = [(30, 0, -2790, 0)],    // Kraken and its part
        [4] = [(5, 0, -142, 196)],     // Baltail and its tail
        [6] = [(7, 0, -142, 196)],     // Poison Baltail and its tail
        [318] = [(320, 0, 0, 0)],      // Seath's Fountain and its water
        [251] = [(250, 0, -480, 0)],   // the treasure chest and its lid
    };

    /// <summary>Textures a model takes from a map region's RTIM.T set rather
    /// than its area's: (set, CLUT x, CLUT y). The pictures' canvases.</summary>
    public static readonly Dictionary<int, (int Set, int ClutX, int ClutY)[]> ExtraTextures = new()
    {
        [45] = [(18, 576, 274)],    // the Picture of a King
    };

    /// <summary>Home areas for models no area places as a creature of their own:
    /// the Freeze Ball is cast by the Tarn, in area 4.</summary>
    public static readonly Dictionary<int, int> HomeArea = new()
    {
        [26] = 4,
    };

    static readonly Tune Plain = new();

    /// <summary>Entries that share a model but are shown differently, by their
    /// first description entry: the Deep-Sea Sigill is the Sigill's model, grown.</summary>
    public static readonly Dictionary<int, Tune> ForEntryTune = new()
    {
        [243] = new Tune { Scale = 0.80 },   // Deep-Sea Sigill
    };

    public static Tune TuneOf(int id) => Tunes.TryGetValue(id, out var t) ? t : Plain;

    /// <summary>The tuning of every model above that has any.</summary>
    static readonly Dictionary<int, Tune> Tunes = new()
    {
        // Enemies
        [29] = new Tune { Scale = 0.66, Y = -40, Tilt = 15, Clip = 4 },     // Kraken
        [30] = new Tune { Clip = 1 },                                       // Kraken part
        [2] = new Tune { Scale = 0.65 },                                    // Sigill
        [4] = new Tune { Scale = 1.1, Y = -9, Tilt = 120 },                 // Baltail
        [9] = new Tune { Scale = 0.31, Fixed = true },                     // Fire-Elemental
        [0] = new Tune { Tilt = 120 },                                      // Green Slime
        [14] = new Tune { Scale = 0.65 },                                   // Head Eater
        [16] = new Tune { Scale = 1.21 },                                   // S-Knight
        [21] = new Tune { Scale = 0.75 },                                   // Skeleton
        [10] = new Tune { Scale = 0.75 },                                   // Ghosts
        [1] = new Tune { Tilt = 120 },                                      // Poison Slime
        [17] = new Tune { Y = -110 },                                       // Necron's Soldiers
        [18] = new Tune { Scale = 0.71 },                                   // Archers
        [22] = new Tune { Scale = 0.76, Tilt = 120 },                       // Termite
        [25] = new Tune { Scale = 1.35, Tilt = 75 },                        // Queen Termite
        [11] = new Tune { Scale = 0.62, Tilt = 30 },                        // Reiks
        [34] = new Tune { Scale = 0.86, Y = -66, Tilt = 15 },               // Copper Knight
        [45] = new Tune { Scale = 0.55, Y = -31, Fixed = true },            // Picture of a King
        [41] = new Tune { Scale = 0.5, Facing = 421, Fixed = true },        // Flame Spirit
        [40] = new Tune { Scale = 0.87, Clip = 2 },                         // Salamander
        [24] = new Tune { Scale = 0.65 },                                   // Psythe
        [15] = new Tune { Scale = 0.71, Tilt = 158 },                       // Mogle
        [32] = new Tune { Scale = 0.8, Tilt = 30 },                         // Refma
        [51] = new Tune { Scale = 0.68, Tilt = 60 },                        // Refma (Red)
        [6] = new Tune { Scale = 1.1, Tilt = 120 },                         // Poison Baltail
        [27] = new Tune { Scale = 0.8 },                                    // Log Stalker
        [49] = new Tune { Scale = 0.73 },                                   // Mage's Candlesticks
        [42] = new Tune { Scale = 0.62, Facing = 421, Fixed = true },       // Billent
        [33] = new Tune { Scale = 0.75, Tilt = 45 },                        // Tarn
        [26] = new Tune { Scale = 0.51, Tilt = 105 },                       // Freeze Ball
        [28] = new Tune { Scale = 0.93, Y = -84, Tilt = 45, Clip = 1 },     // Demon Lord
        [35] = new Tune { Scale = 0.75, X = 40, Y = 53, Clip = 2 },         // Dias Bagil
        [47] = new Tune { Scale = 0.76, Tilt = 120 },                       // Mecha-Termite
        [48] = new Tune { Scale = 0.62, Tilt = 30 },                        // Mecha-Reiks
        [46] = new Tune { Scale = 0.93, Y = -84, Tilt = 45, Clip = 1 },     // Mecha-Demon Lord
        [50] = new Tune { Scale = 0.42 },                                   // Bitt
        [39] = new Tune { Scale = 1.6, Tilt = 15 },                         // Guyra

        // People
        [67] = new Tune { Scale = 1.08, Tilt = 15 },                        // Jose Harven
        [66] = new Tune { Scale = 0.57, Y = 44, Tilt = 30 },                // Raffy Foss
        [70] = new Tune { Scale = 0.73 },                                   // Nola Bagil
        [83] = new Tune { Scale = 0.85, Tilt = 75 },                        // Al Hunt
        [85] = new Tune { Scale = 0.71 },                                   // Fai Fadlin
        [87] = new Tune { Scale = 0.64 },                                   // Rand Ferrer
        [95] = new Tune { Scale = 1.31, Y = 88, Tilt = 83, Fixed = true },  // Mark Wozz
        [106] = new Tune { Scale = 0.69, Tilt = 15 },                       // Earnest Clyde
        [96] = new Tune { Scale = 0.53, Y = 146, Clip = 3 },                // Earnest Clyde (Underworld Prison No. 1)
        [64] = new Tune { Scale = 0.68 },                                   // Celffy Foss
        [73] = new Tune { Scale = 0.71 },                                   // Meryl
        [90] = new Tune { Scale = 0.71, Y = 57, Tilt = 83, Fixed = true },  // Radd Bilheim
        [91] = new Tune { Scale = 0.75 },                                   // Krola Amgun
        [94] = new Tune { Scale = 0.57, Tilt = 58 },                        // Sandler Amgun
        [97] = new Tune { Scale = 0.75 },                                   // Kehl Hunt
        [76] = new Tune { Scale = 0.54 },                                   // Gigi Budwell
        [78] = new Tune { Scale = 0.63 },                                   // Teo Budwell (The East Village)
        [79] = new Tune { Scale = 0.57, Tilt = 44 },                        // Teo Budwell
        [80] = new Tune { Scale = 0.65, Tilt = 75 },                        // Karen Shore
        [81] = new Tune { Scale = 0.48, Y = 48 },                           // Leon Shore (Underworld Prison No. 2)
        [82] = new Tune { Scale = 0.72 },                                   // Leon Shore
        [84] = new Tune { Scale = 0.71 },                                   // Harris Carvitto
        [105] = new Tune { Scale = 0.78, Tilt = 75 },                       // Grave of a Saint
        [107] = new Tune { Scale = 0.67, Tilt = 90 },                       // Dragon Grass
        [86] = new Tune { Scale = 0.6, Y = 45, Tilt = -100 },               // Cliff Lore
        [88] = new Tune { Scale = 0.52, Tilt = 49 },                        // David Tabler
        [89] = new Tune { Scale = 0.75 },                                   // Dalf Vice
        [44] = new Tune { Scale = 1.15, Tilt = 175 },                       // Skeleton of One-Eyed Giant
        [109] = new Tune { Scale = 0.64, Y = 79, Tilt = 45 },               // Miria

        // Places' objects
        [227] = new Tune { Scale = 0.52 },                                  // The Elf Cave
        [251] = new Tune { Scale = 0.62, Y = 65, Tilt = 80 },               // Royal Treasure House's chest
        [274] = new Tune { Scale = 0.67, Tilt = 120 },                      // The Poisoned Cave
        [279] = new Tune { Scale = 0.49, Tilt = 75 },                       // The North Graveyard
        [280] = new Tune { Scale = 0.53 },                                  // The Soldier's Cemetery
        [281] = new Tune { Scale = 0.51, Tilt = 90 },                       // The Elf Graveyard
        [282] = new Tune { Scale = 0.79, Tilt = 203 },                      // The Demon's Grave
        [284] = new Tune { Scale = 0.44, Tilt = 75 },                       // The South Graveyard
        [285] = new Tune { Scale = 0.54, Tilt = 128 },                      // Base No. 2
        [291] = new Tune { Scale = 0.73 },                                  // The Mage's Lighthouse
        [277] = new Tune { Scale = 0.57, Tilt = 227},                       // The East Seaside
        [305] = new Tune { Scale = 0.89, Tilt = 196 },                      // The Big Mine
        [318] = new Tune { Scale = 1.09, Y = 165, Tilt = 10 },              // Seath's Fountain
        [320] = new Tune { Hold = 4095 },                                   // its water, held full
        [323] = new Tune { Scale = 0.71, Y = 72, Tilt = 129 },              // West Seaside
        [329] = new Tune { Scale = 0.72, Y = -600 },                        // Base No. 6
        [359] = new Tune { Scale = 0.448 },                                 // Guyra's Dimension
        [391] = new Tune { Scale = 0.84 },                                  // A Small Mine
        [393] = new Tune { Scale = 0.53 },                                  // Necron's Coliseum
        [404] = new Tune { Scale = 0.76 },                                  // The Research Office
    };
}
