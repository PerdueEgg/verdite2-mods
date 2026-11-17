using System;
using System.Collections.Generic;
using System.Linq;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// The encyclopedia's pages:
///
///     ENCYCLOPEDIA
///       ENEMIES                       a list
///       PEOPLE                        a list
///       PLACES       -> the eight areas, each a list (?????? until one place in it is found)
///       EQUIPMENT    -> ITEMS, WEAPON, SHIELD, HEAD, BODY, ARMS, FEET, ACCESSORY,
///                       each a four-row list with the item's turning preview
///
/// Every page is the game's own scrolling list (see RunPage); a menu page's
/// Confirm opens the page below it one level down.
/// </summary>
public sealed partial class EncyclopediaMod
{
    /// <summary>The equipment pages, by item id range as the game numbers them
    /// (and as its own equipment pages split them).</summary>
    static readonly (string Name, Func<int, bool> Holds)[] EquipmentKinds =
    [
        ("ITEMS",     id => id >= 60),
        ("WEAPON",    id => id <= 19),
        ("SHIELD",    id => id >= 33 && id <= 39),
        ("HEAD",      id => id >= 20 && id <= 26),
        ("BODY",      id => id >= 27 && id <= 32),
        ("ARMS",      id => id >= 40 && id <= 45),
        ("FEET",      id => id >= 46 && id <= 51),
        ("ACCESSORY", id => id >= 52 && id <= 59),
    ];

    // After EquipmentKinds: static fields in one file are set in the order written.
    static readonly Page _main = BuildMenus();

    static Page BuildMenus()
    {
        // Each area's places, with and without the mod's own (Zones.IsOwn): the
        // switch in the settings picks one, and an area left with none (Guyra's
        // Dimension) drops off the list.
        var areas = Enumerable.Range(0, Data.AreaNames.Length).Select(a =>
        {
            var all = Data.Places.Where(p => p.Area == a).ToArray();
            var disc = all.Where(p => !Zones.IsOwn(p.Desc[0] - 510)).ToArray();
            return ListPage(Data.AreaTitles[a], () => _ownPlaces ? all : disc, describes: false, area: a);
        }).ToArray();
        int[] allAreas = Enumerable.Range(0, Data.AreaNames.Length).Where(a => Data.Places.Any(p => p.Area == a)).ToArray();
        int[] discAreas = allAreas.Where(a => Data.Places.Any(p => p.Area == a && !Zones.IsOwn(p.Desc[0] - 510))).ToArray();
        // An area stays ?????? and shut until one of its places is found, so the
        // list gives nothing away.
        var places = MenuPage("PLACES", Data.AreaNames, areas,
            () => $"{UnlockedCount(Places)}/{Places.Length}",
            open: a => Places.Any(p => p.Area == a && IsUnlocked(p)),
            rows: () => _ownPlaces ? allAreas : discAreas);

        var kinds = EquipmentKinds.Select(k => ItemPage(k.Name, k.Holds)).ToArray();
        var equipment = MenuPage("EQUIPMENT", EquipmentKinds.Select(k => k.Name).ToArray(), kinds,
            () => $"{ItemsUnlocked()}/{ItemIds().Count}");

        var main = MenuPage("ENCYCLOPEDIA", ["ENEMIES", "PEOPLE", "PLACES", "EQUIPMENT"],
            [ListPage("ENEMIES", () => Data.Enemies, describes: true), ListPage("PEOPLE", () => Data.People, describes: true), places, equipment],
            TotalCounter);
        main.RowCounter = i => i switch
        {
            0 => $"{UnlockedCount(Data.Enemies)}/{Data.Enemies.Length}",
            1 => $"{UnlockedCount(Data.People)}/{Data.People.Length}",
            2 => $"{UnlockedCount(Places)}/{Places.Length}",
            _ => $"{ItemsUnlocked()}/{ItemIds().Count}",
        };
        return main;
    }

    /// <summary>The main page's title box: the whole as a percentage, rounded
    /// down so 100% means everything. Each list's own count is on its row.</summary>
    static string TotalCounter()
    {
        int have = UnlockedCount(Data.Enemies) + UnlockedCount(Data.People) + UnlockedCount(Places) + ItemsUnlocked();
        int all = Data.Enemies.Length + Data.People.Length + Places.Length + ItemIds().Count;
        int pct = all == 0 ? 0 : have * 100 / all;
        return $"{pct}%";
    }

    /// <summary>A page of names that each open the page below; with
    /// <paramref name="open"/>, a name it refuses shows ?????? and opens nothing.
    /// <paramref name="rows"/> picks which names are listed (all of them by default).</summary>
    static Page MenuPage(string header, string[] names, Page[] below, Func<string> counter,
                         Func<int, bool>? open = null, Func<int[]>? rows = null)
    {
        int[] every = Enumerable.Range(0, names.Length).ToArray();
        Func<int[]> shown = rows ?? (() => every);
        return new()
        {
            Header = header,
            Count = () => shown().Length,
            Label = i => { int k = shown()[i]; return open == null || open(k) ? L(names[k]) : "??????"; },
            Visible = Math.Min(names.Length, 10),
            Counter = counter,
            Confirm = (c, m, i) =>
            {
                int k = shown()[i];
                if (open != null && !open(k)) return true;   // nothing to open, no sound
                RunPage(c, m, below[k], _level + 1);
                return false;
            },
        };
    }

    /// <summary>Enemies, people, or one area's places, in the item pages' shape:
    /// four rows low on the screen, and the highlighted entry's model turning
    /// above them once it is unlocked. A place's model is textured from its
    /// <paramref name="area"/>.</summary>
    static Page ListPage(string header, Func<Entry[]> list, bool describes, int area = -1) => new()
    {
        Header = header,
        Count = () => Math.Min(list().Length, MaxEntries),
        Label = i => IsUnlocked(list()[i]) ? L(list()[i].Name) : "??????",
        Counter = () => $"{UnlockedCount(list())}/{list().Length}",
        Visible = 4,
        ListY = 159,
        Numbered = true,
        ModelTune = i => list()[i].Desc.Length > 0 && Models.ForEntryTune.TryGetValue(list()[i].Desc[0], out var t) ? t : null,
        Model = i => IsUnlocked(list()[i]) ? ModelOf(list()[i]) : -1,
        Area = area,
        Confirm = (c, m, i) =>
        {
            var e = list()[i];
            if (!describes || !IsUnlocked(e)) return true;   // nothing to open, no sound
            Sound(c, m, 0x11);
            HideModel(c, m);   // put away while the card is up; shown again after
            ShowCard(c, m, (uint)e.Desc[0]);
            return true;
        },
    };

    /// <summary>The model an entry shows (Models.cs), or -1.</summary>
    static int ModelOf(Entry e)
    {
        if (e.Desc.Length == 0) return -1;
        if (e.Area >= 0) return Models.ForPlace.TryGetValue(e.Desc[0] - 510, out int place) ? place : -1;
        return Models.ForEntry.TryGetValue(e.Desc[0], out int model) ? model : -1;
    }

    /// <summary>One kind of equipment, in the item pages' shape: four rows low on
    /// the screen, and the highlighted item's model turning above them.</summary>
    static Page ItemPage(string header, Func<int, bool> holds)
    {
        List<int> Ids() => ItemIds().Where(holds).ToList();
        return new Page
        {
            Header = header,
            Count = () => Ids().Count,
            Label = i => ItemUnlocked(Ids()[i]) ? ItemNameOf(Ids()[i]) : "??????",
            Counter = () => { var ids = Ids(); return $"{Enumerable.Count(ids, ItemUnlocked)}/{ids.Count}"; },
            Visible = 4,
            ListY = 159,
            Numbered = true,
            ModelId = i => Ids()[i],
            ShowModel = i => ItemUnlocked(Ids()[i]),
            Confirm = (c, m, i) =>
            {
                int id = Ids()[i];
                if (!ItemUnlocked(id)) return true;
                Sound(c, m, 0x11);
                ShowCard(c, m, (uint)(ItemDescBase + id));
                return true;
            },
        };
    }

    /// <summary>A description card, with the pointer handed to mouse look while
    /// it is up (the card is a game screen, where a click closes it) and taken
    /// back for the encyclopedia after.</summary>
    static void ShowCard(CpuContext c, IMemory m, uint entry)
    {
        MenuPointer(c, m, false);
        try { ShowMessage(c, m, ItemFile, entry); }
        finally { MenuPointer(c, m, true); }
    }
}
