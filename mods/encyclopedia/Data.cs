// The encyclopedia's contents: names in display order and the ITEM.T
// description entries behind them. The names can be edited freely; they are
// drawn in the game's menu font through the port's lettering, and a translation can
// replace them through its string table (see Cards.cs).
//
// desc: entries in COM\ITEM.T (archive 6). The first is the one the menu
//   opens; any of them unlocks the entry. Creature ID = desc - 240 (byte +0x1
//   of a creature record); place zone number = desc - 510.
// also: other entries whose unlocking also unlocks this one.
using System;

namespace Kf2.Mods.Encyclopedia;

sealed record Entry(string Name, int[] Desc, int[] Also, int Area = -1);

static class Data
{
    /// <summary>Creature IDs (as desc numbers) that are a destructible part of a
    /// bigger creature rather than the creature: destroying one gives EXP but is
    /// not a kill, so it does not unlock the entry by encounter. The Truth Glass
    /// on a part still does, since it shows the creature's own description.</summary>
    public static readonly int[] Parts =
    [
        270,   // Kraken: a part of 269
        245,   // Baltail: the tail of 244
        247,   // Poison Baltail: the tail of 246
    ];

    /// <summary>Not enemies at all, so never killed: the Mage's Candlesticks are the
    /// Magic Palace of Fire's puzzle, four to light (2000 HP, 0 EXP). By encounter
    /// they unlock when the player strikes one, which is how they are lit.</summary>
    public static readonly int[] Struck =
    [
        289,   // Mage's Candlesticks
    ];

    public static readonly Entry[] Enemies =
    [
        new("Kraken", [269, 270], []),
        new("Sigill", [242], []),
        new("Deep-Sea Sigill", [243], []),
        new("Baltail", [244, 245], []),
        new("Fire-Elemental", [248, 249], []),
        new("Green Slime", [240], []),
        new("Head Eater", [254], []),
        new("S-Knight", [256], []),
        new("Skeleton", [261], []),
        new("Ghosts", [250], []),
        new("Poison Slime", [241], []),
        new("Necron's Soldiers", [257], []),
        new("Archers", [258], []),
        new("Termite", [262], []),
        new("Queen Termite", [265], []),
        new("Reiks", [251], []),
        new("Copper Knight", [274], []),
        new("The Picture of a King", [285], []),
        new("Flame Spirit", [281], []),
        new("Salamander", [280], []),
        new("Psythe", [264], []),
        new("Mogle", [255], []),
        new("Refma", [272], []),              // the blue ones (creature 32), found all over
        new("Refma (Red)", [291], []),        // the red one (creature 51): only one, in the Elf Shrine
        new("Poison Baltail", [246, 247], []),
        new("Log Stalker", [267], []),
        new("Kald", [263], []),
        new("Earth-Elemental", [271], []),
        new("Mage's Candlesticks", [289], []),
        new("Billent", [282], []),
        new("Tarn", [273], []),
        new("Freeze Ball", [266], [273]),
        new("Demon Lord", [268], []),
        new("Dias Bagil", [275], []),
        new("Mecha-Termite", [287], []),
        new("Mecha-Reiks", [288], []),
        new("Mecha-Demon Lord", [286], []),
        new("Bitt", [290], []),
        new("Guyra", [279], []),
    ];

    public static readonly Entry[] People =
    [
        new("Jose Harven", [307], []),
        new("Raffy Foss", [306], []),
        new("Nola Bagil", [310], []),
        new("Al Hunt", [323], []),
        new("Fai Fadlin", [325], []),
        new("Rand Ferrer", [327], []),
        new("Mark Wozz", [335], []),
        new("Earnest Clyde", [346, 336], []),
        new("Celffy Foss", [304, 305], []),
        new("Meryl", [313], []),
        new("Radd Bilheim", [330], []),
        new("Krola Amgun", [331, 332], []),
        new("Sandler Amgun", [333, 334], []),
        new("Kehl Hunt", [337, 338], []),
        new("Gigi Budwell", [314, 316], []),
        new("Teo Budwell", [317, 318, 319], []),
        new("Karen Shore", [320, 339], []),
        new("Leon Shore", [321, 322], []),
        new("Harris Carvitto", [324], []),
        new("The Grave of a Saint", [345], []),
        new("Dragon Grass", [347], []),
        new("Cliff Lore", [326], []),
        new("David Tabler", [328], []),
        new("Dalf Vice", [329], []),
        new("Skeleton of One-Eyed Giant", [284], []),
        new("Miria", [349], []),
    ];

    /// <summary>The areas the Places list is split into, by the game's area
    /// number. Mostly the name of each area's whole-area fallback zone -- the
    /// place the Truth Glass names where nothing more specific covers you --
    /// with 1 and 4 named for their best-known place instead.</summary>
    public static readonly string[] AreaNames =
    [
        "West Seaside",                 // 0
        "Small Mine",                   // 1: the villages, and the small mine
        "Harvine's Castle",             // 2: the ruins, and Base No. 2
        "East Seaside",                 // 3: and Base No. 3
        "Elf Shrine",                   // 4: and the palaces
        "The Big Mine",                 // 5
        "Necron's Coliseum",            // 6
        "Guyra's Dimension",            // 7: no zones of its own
    ];

    /// <summary>The area pages' titles, where the name is too long for the title box.</summary>
    public static readonly string[] AreaTitles =
    [
        "West Seaside", "Small Mine", "Harvine's Castle", "East Seaside",
        "Elf Shrine", "Big Mine", "Coliseum", "Dimension",
    ];

    public static readonly Entry[] Places =
    [
        new("West Seaside", [510], [], 0),
        new("The Pirate's Cave", [511], [], 0),
        new("The Cave of Old-Hand", [512], [], 0),
        new("The Mage's Lighthouse", [513], [], 0),
        new("Kraken's Nest", [550], [], 0),
        new("The North Village", [514], [], 1),
        new("The South Village", [515], [], 1),
        new("A Small Mine", [516], [], 1),
        new("The North Graveyard", [517], [], 1),
        new("Base No. 1", [518], [], 1),
        new("Seath's Fountain", [532], [], 1),
        new("The South Graveyard", [536], [], 1),
        new("The Underworld Prison No. 1", [551], [], 1),
        new("Base No. 2", [519], [], 2),
        new("The Ruins of King Harvine's Castle", [524], [], 2),
        new("The Termite's Nest", [525], [], 2),
        new("The Window of Homesickness", [528], [], 2),
        new("The Village of the Wind", [546], [], 2),
        new("Base No. 3", [520], [], 3),
        new("The Cemetery", [526], [], 3),
        new("The Soldier's Cemetery", [527], [], 3),
        new("The Elf Graveyard", [529], [], 3),
        new("The East Village", [530], [], 3),
        new("The East Seaside", [547], [], 3),
        new("Base No. 4", [521], [], 4),
        new("The Elf Shrine", [531], [], 4),
        new("The Magic Palace of Fire", [533], [], 4),
        new("The Magic Palace of Ice", [534], [], 4),
        new("The Underworld Prison No. 2", [552], [], 4),
        new("Royal Treasure House", [553], [], 4),
        new("Base No. 5", [522], [], 5),
        new("The Big Mine", [537], [], 5),
        new("The Poisoned Cave", [538], [], 5),
        new("The Cave of the Darkside", [539], [], 5),
        new("The Elf Cave", [540], [], 5),
        new("The Cave of The Earth Soul", [541], [], 5),
        new("The Lookout", [542], [], 5),
        new("Base No. 6", [523], [], 6),
        new("Necron's Coliseum", [543], [], 6),
        new("The Research Office", [545], [], 6),
        new("Space for Completion", [554], [], 6),
        new("Guyra's Dimension", [555], [], 7),
        new("The Demon's Grave", [535], [], 6),
        new("The Warrior's Graveyard", [544], [], 6),
    ];

}

/// <summary>A game zone given a different place: the area-6 zones the
/// developers labelled 35 (The Research Office) though they lie apart from it.
/// Matched by the zone's own rectangle (tiles) and floor half.</summary>
sealed record ZoneFix(int Area, int Floor, int X, int Z, int W, int H, int From, int To);

/// <summary>A zone the disc does not have. Floor 0 = either half. X0..X1, Z0..Z1
/// are inclusive tile ranges, as read off the map panel.</summary>
sealed record ExtraZone(int Area, int Floor, int X0, int Z0, int X1, int Z1, int Place);

static class Zones
{
    public static readonly ZoneFix[] Fixes =
    [
        new(6, 2, 12, 41, 17, 13, 35, 34),   // x 12-28, z 41-53: The Warrior's Graveyard's entrance hall
        new(6, 2, 13, 23, 16, 13, 35, 34),   // x 13-28, z 23-35: a second, smaller warrior's graveyard
    ];

    public static readonly ExtraZone[] Extra =
    [
        new(6, 0, 28, 55, 35, 64, 25),        // The Demon's Grave, inside the graveyard (first: the first match wins)
        new(6, 0, 12, 54, 44, 73, 34),        // The Warrior's Graveyard, from where its entrance hall's zone ends

        // Places the disc has no card or zone for.
        // Upper floor is half 2, lower half 1. Place numbers from 40 are the mod's own.
        new(0, 2, 58, 53, 69, 63, KrakensNest),
        new(1, 2, 53,  0, 60, 30, Prison1),
        new(4, 2, 47, 51, 53, 64, Prison2),
        new(4, 2, 54, 61, 64, 77, TreasureHouse),
        new(6, 1,  3, 14, 21, 31, SpaceForCompletion),
        new(7, 0,  0,  0, 79, 79, GuyrasDimension),   // the whole area: it has no zones of its own
    ];

    public const int KrakensNest = 40, Prison1 = 41, Prison2 = 42, TreasureHouse = 43, SpaceForCompletion = 44,
                     GuyrasDimension = 45;

    /// <summary>A place of the mod's own, one the game never named (40 on), as
    /// against the disc's places and the graves fix. These can be switched off.</summary>
    public static bool IsOwn(int place) => place >= KrakensNest;

    /// <summary>A card for a place the disc never named, drawn by the port's lettering (Cards.cs), or null.</summary>
    public static string? CustomCard(int place) => place switch
    {
        KrakensNest => "Kraken's Nest",
        TreasureHouse => "Royal Treasure House",
        SpaceForCompletion => "The Space For Completion",   // the sign's first line, without "No Trespassing"
        GuyrasDimension => "Guyra's Dimension",
        _ => null,
    };

    /// <summary>The picture the Truth Glass shows for a place: its own card
    /// (510 + place) for the disc's places, a sign for the mod's where one exists,
    /// and -1 for none (the glass then shows nothing, but the place still
    /// unlocks).</summary>
    public static int CardOf(int place) => place switch
    {
        < 40 => 510 + place,
        Prison1 => 146,              // "The Underworld Prison No.1"
        Prison2 => 147,              // "The Underworld Prison No.2"
        _ => -1,                     // the rest have custom cards (CustomCard)
    };
}
