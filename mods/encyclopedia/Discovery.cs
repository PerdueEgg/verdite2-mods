using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// What has been discovered, how it gets discovered, and how it is kept with
/// each save.
///
/// ---- the record ----
///
/// A set of ITEM.T description numbers that have been "seen" (240-349 creatures,
/// 510-547 places). An entry is unlocked when any of its numbers, or any of its
/// "also" numbers, is in the set; so the two Kraken IDs share one entry, and
/// Freeze Ball unlocks with Tarn.
///
/// ---- discovering ----
///
/// Truth Glass (always): the glass's two calls to func_80035B48, the
/// creature arm (return address 0x80047FB8) and the place arm (0x80048078),
/// record the entry they show -- after the Sigill fix, so a deep-sea Sigill
/// records 243.
///
/// Encounters (off by default, a switch per list):
/// - a kill by the player: func_8003A9CC awards EXP (func_80024CAC, called from
///   0x8003ADAC) only when the attacker is the player, just before the death
///   reaction; the creature is in S4. Creatures killed by other creatures, or by
///   a script (func_8003A890), give no EXP and do not count.
/// - striking one, for what is never killed (Data.Struck, the Mage's
///   Candlesticks): every blow goes through func_8003A9CC(creature index, ...),
///   whose twelfth argument (the caller's sp+0x2C) says who struck, 0x10 in its
///   0x30 bits for the player, as its EXP award tests.
/// - talking: every NPC line is a TALK.T picture, func_80035B48(3, ...). The
///   person spoken to is taken as the nearest person-creature within 2.5 tiles.
/// - entering a place: the Truth Glass's own zone test, run each tick. Zones are
///   object-table records (0x80177714, 396 of 0x44) whose definition kind
///   (0x80175914 + u16[+0x6] * 0x18) is 0xE2, on the player's floor half
///   (u8[+0x40] == 2 if u16[0x8019953C] else 1), covering the player's tile:
///   origin tile u32[+0x14] >> 11, u32[+0x1C] >> 11, size u8[+0x38] x u8[+0x39]
///   (func_80037810). The first match is the one the glass would name.
///
/// ---- saving ----
///
/// The record lives beside the card saves as encyclopedia_slotN.txt in the data
/// folder. A card save (func_80023764) writes the current slot's file; a card
/// load (func_80023638, returning 0) reads it back, so progress after the last
/// save is lost on a reload exactly as the game's own is. Every start of
/// GAME.EXE (game_entry, 0x8004A628) clears it, so a new game starts empty.
/// </summary>
public sealed partial class EncyclopediaMod
{
    const uint TruthGlassPlaceCall = 0x80048078;   // func_800474D0 -> func_80035B48, place arm
    const uint TalkFile = 3;                       // COM\TALK.T
    const uint SaveSlot = 0x8006E5D4;              // u8, 1-3, written by both card load and save
    const uint PlayerX = 0x801994EC, PlayerZ = 0x801994F4;
    const uint FloorHalf = 0x8019953C;             // u16
    const uint Objects = 0x80177714, ObjectSize = 0x44, ObjectCount = 396;
    const uint Definitions = 0x80175914, DefinitionSize = 0x18;
    const byte ZoneKind = 0xE2;
    const int TalkReach = 0x1400;                  // 2.5 tiles

    // Per list: 0 enemies, 1 people, 2 places, 3 equipment.
    const int CatEnemies = 0, CatPeople = 1, CatPlaces = 2, CatItems = 3;
    static readonly string[] CatKeys = ["enemies", "people", "places", "equipment"];
    static readonly bool[] _enc = [false, false, false, false];

    /// <summary>Which list a seen number belongs to.</summary>
    static int CatOf(int desc) =>
        desc >= ItemDescBase && desc < ItemDescBase + ItemCount ? CatItems
        : desc >= 510 ? CatPlaces
        : _peopleDescs.Contains(desc) ? CatPeople
        : CatEnemies;

    static readonly HashSet<int> _seen = [];
    static int _loadSlot;
    static string _lastFile = "";

    static readonly HashSet<int> _peopleDescs =
        Data.People.SelectMany(e => e.Desc).ToHashSet();

    static void LoadDiscoverySettings()
    {
        var view = RecompOne.Runtime.Runtime.View;
        for (int k = 0; k < 4; k++)
            _enc[k] = view.GetBool($"kf2.encyclopedia.encounter.{CatKeys[k]}", false);
        LoadNotifySettings();
    }

    // ---- the record ----

    static bool IsUnlocked(Entry e)
    {
        foreach (int d in e.Desc) if (_seen.Contains(d)) return true;
        foreach (int d in e.Also) if (_seen.Contains(d)) return true;
        return false;
    }

    static int UnlockedCount(Entry[] list)
    {
        int n = 0;
        foreach (var e in list) if (IsUnlocked(e)) n++;
        return n;
    }

    static void Mark(int desc)
    {
        if (_seen.Contains(desc)) return;
        var before = _quiet > 0 ? null : Locked();
        _seen.Add(desc);
        Console.WriteLine($"[encyclopedia] discovered {desc} ({NameOf(desc)})");
        if (before != null) Announce(desc, before);
    }

    /// <summary>Ticks left in which a discovery is not announced: the equipment you
    /// start with or load wearing is found on the first tick, and is no news.</summary>
    static int _quiet;
    const int QuietTicks = 3;

    static string NameOf(int desc)
    {
        foreach (var list in new[] { Data.Enemies, Data.People, Data.Places })
            foreach (var e in list)
                if (e.Desc.Contains(desc)) return e.Name;
        return "not listed";
    }

    static void UnlockAll()
    {
        foreach (var list in new[] { Data.Enemies, Data.People, Places })
            foreach (var e in list) _seen.Add(e.Desc[0]);
        foreach (int id in ItemIds()) _seen.Add(ItemDescBase + id);
    }

    // ---- discovering ----

    static void OnTruthGlass(uint entry)
    {
        if (entry >= 240 && entry < 548) Mark((int)entry);
    }

    const uint KillExpCall = 0x8003ADB4;   // func_8003A9CC -> func_80024CAC, the player's kill

    [PreHook("game", Address = 0x80024CAC)]
    static bool BeforeExpAward(CpuContext c, IMemory m)
    {
        if (c.RA != KillExpCall || !IsCreature(c.S4)) return true;
        int desc = DescOfCreature(m, c.S4);
        if (Array.IndexOf(Data.Parts, desc) >= 0) return true;   // a tail or a limb, not the creature
        if (_enc[CatOf(desc)]) Mark(desc);
        return true;
    }

    const uint PlayerBlow = 0x10;

    [PreHook("game", Address = 0x8003A9CC)]
    static bool BeforeBlow(CpuContext c, IMemory m)
    {
        if (!_enc[CatEnemies] || (m.ReadU32(c.SP + 0x2C) & 0x30) != PlayerBlow) return true;
        uint rec = Creatures + (c.A0 & 0xFFFF) * CreatureSize;
        if (!IsCreature(rec)) return true;
        int desc = DescOfCreature(m, rec);
        if (Array.IndexOf(Data.Struck, desc) >= 0) Mark(desc);
        return true;
    }

    static int DescOfCreature(IMemory m, uint rec)
    {
        int id = m.ReadU8(rec + 1);
        if (id == 2 && IsDeepSeaSigill(m, rec)) return 243;
        return 240 + id;
    }

    static void OnTalk(IMemory m)
    {
        if (!_enc[CatPeople]) return;
        int px = (int)m.ReadU32(PlayerX), pz = (int)m.ReadU32(PlayerZ);
        long best = (long)TalkReach * TalkReach;
        int found = -1;
        for (uint i = 0; i < CreatureCount; i++)
        {
            uint rec = Creatures + i * CreatureSize;
            if (m.ReadU8(rec) == 0xFF) continue;
            int desc = DescOfCreature(m, rec);
            if (!_peopleDescs.Contains(desc)) continue;
            long dx = (int)m.ReadU32(rec + 0x2C) - px, dz = (int)m.ReadU32(rec + 0x34) - pz;
            long d2 = dx * dx + dz * dz;
            if (d2 < best) { best = d2; found = desc; }
        }
        if (found >= 0) Mark(found);
    }

    const uint AreaByte = 0x8017E060;

    /// <summary>The place the player is standing in, as the Truth Glass names it,
    /// with this mod's corrections: an extra zone first, then the game's own zones
    /// in table order (the first match, as func_800474D0 takes it), relabelled
    /// where Zones.Fixes says the disc has it wrong. -1 if none.</summary>
    static int PlaceAt(IMemory m, bool extras = true)
    {
        int area = m.ReadU8(AreaByte);
        int tx = (int)m.ReadU32(PlayerX) >> 11, tz = (int)m.ReadU32(PlayerZ) >> 11;
        int half = m.ReadU16(FloorHalf) != 0 ? 2 : 1;

        if (extras) foreach (var e in Zones.Extra)
            if ((_ownPlaces || !Zones.IsOwn(e.Place)) && e.Area == area && (e.Floor == 0 || e.Floor == half)
                && tx >= e.X0 && tx <= e.X1 && tz >= e.Z0 && tz <= e.Z1)
                return e.Place;

        for (uint i = 0; i < ObjectCount; i++)
        {
            uint rec = Objects + i * ObjectSize;
            uint def = m.ReadU16(rec + 6);
            if (def >= 320) continue;                                  // free (0xFF) or out of range
            if (m.ReadU8(Definitions + def * DefinitionSize) != ZoneKind) continue;
            if (m.ReadU8(rec + 0x40) != half) continue;
            int x0 = (int)m.ReadU32(rec + 0x14) >> 11, z0 = (int)m.ReadU32(rec + 0x1C) >> 11;
            int w = m.ReadU8(rec + 0x38), h = m.ReadU8(rec + 0x39);
            if (tx < x0 || tx >= x0 + w || tz < z0 || tz >= z0 + h) continue;
            int place = m.ReadU16(rec + 0x3A);
            foreach (var f in Zones.Fixes)
                if (f.Area == area && f.Floor == half && f.X == x0 && f.Z == z0 && f.W == w && f.H == h && f.From == place)
                    return f.To;
            return place;
        }
        return -1;
    }

    static void CheckPlaces(IMemory m)
    {
        if (!_enc[CatPlaces]) return;
        int place = PlaceAt(m);
        if (place >= 0) Mark(510 + place);
    }

    // ---- saving ----

    static string SlotPath(int slot) => Path.GetFullPath($"encyclopedia_slot{slot}.txt");

    [PreHook("game", Address = 0x8004A628)]
    static bool OnGameStart(CpuContext c, IMemory m)
    {
        _seen.Clear();
        _quiet = QuietTicks;
        _lastFile = "new game";
        _lastArrows = _lastBolts = -1;
        return true;
    }

    // Dying with no Seath's Tear: the death handler (func_8002A550) resets the
    // character with func_80025B4C -- EXP 0, level 1 -- and respawns you at the
    // start with func_80024154(0, ...): a new game, so discovery starts over too.
    // (With the tear, the other branch revives you in place and nothing resets.)
    // If Auto Reload loads a save instead, the card-load hook below takes over.
    const uint DeathReset = 0x8002B064;   // func_8002A550 -> func_80025B4C

    [PreHook("game", Address = 0x80025B4C)]
    static bool BeforeCharacterReset(CpuContext c, IMemory m)
    {
        if (c.RA != DeathReset) return true;
        _seen.Clear();
        _quiet = QuietTicks;
        _lastFile = "new game (died)";
        Console.WriteLine("[encyclopedia] died with no save: discovery starts over");
        return true;
    }

    [PreHook("game", Address = 0x80023638)]
    static bool BeforeCardLoad(CpuContext c, IMemory m)
    {
        _loadSlot = (int)c.A0;
        return true;
    }

    [PostHook("game", Address = 0x80023638)]
    static void AfterCardLoad(CpuContext c, IMemory m)
    {
        if (c.V0 != 0) return;   // no file or a bad checksum: the game's state is untouched, so is ours
        ReadSlot(_loadSlot);
        _quiet = QuietTicks;
        _lastArrows = _lastBolts = -1;
    }

    static void ReadSlot(int slot)
    {
        _seen.Clear();
        string path = SlotPath(slot);
        try
        {
            if (File.Exists(path))
                foreach (var tok in File.ReadAllText(path).Split([',', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(tok, out int d)) _seen.Add(d);
            _lastFile = $"slot {slot}";
            Console.WriteLine($"[encyclopedia] slot {slot}: {_seen.Count} discovered");
        }
        catch (Exception e) { Console.WriteLine($"[encyclopedia] reading {path}: {e.Message}"); }
    }

    [PostHook("game", Address = 0x80023764)]
    static void AfterCardSave(CpuContext c, IMemory m)
    {
        int slot = m.ReadU8(SaveSlot);
        if (slot < 1 || slot > 3) return;
        string path = SlotPath(slot);
        try
        {
            File.WriteAllText(path, string.Join(",", _seen.OrderBy(x => x)) + "\n");
            _lastFile = $"slot {slot}";
            Console.WriteLine($"[encyclopedia] saved {_seen.Count} discovered to slot {slot}");
        }
        catch (Exception e) { Console.WriteLine($"[encyclopedia] writing {path}: {e.Message}"); }
    }

    // ---- settings ----

    static void DrawDiscoverySettings()
    {
        ImGui.Text("Discovery");
        ImGui.TextDisabled("The Truth Glass and the fortune teller always unlock what they describe. These unlock by encounter as well:");
        string[] encText = ["Enemies: killing them", "People: talking to them", "Places: entering them", "Equipment: using or equipping it"];
        for (int k = 0; k < 4; k++)
            if (ImGui.Checkbox(encText[k], ref _enc[k])) PersistBool($"kf2.encyclopedia.encounter.{CatKeys[k]}", _enc[k]);
        ImGui.TextDisabled("The Mage's Candlesticks, which are lit rather than killed, count once you strike one. "
                         + "A key counts once it has opened its door.");

        int total = Data.Enemies.Length + Data.People.Length + Places.Length + ItemIds().Count;
        int have = UnlockedCount(Data.Enemies) + UnlockedCount(Data.People) + UnlockedCount(Places) + ItemsUnlocked();
        ImGui.Text($"This game: {have}/{total}  (enemies {UnlockedCount(Data.Enemies)}/{Data.Enemies.Length}, "
                 + $"people {UnlockedCount(Data.People)}/{Data.People.Length}, places {UnlockedCount(Places)}/{Places.Length}, items {ItemsUnlocked()}/{ItemIds().Count})");
        ImGui.TextDisabled($"Progress is kept with each memory-card save. Loaded from: {(_lastFile.Length > 0 ? _lastFile : "nothing yet")}.");
        if (ImGui.Button("Unlock all")) UnlockAll();
        ImGui.SameLine();
        if (ImGui.Button("Lock all")) _seen.Clear();
        ImGui.TextDisabled("For this game only; kept when you next save.");
        ImGui.Separator();
        DrawNotifySettings();
    }

    static void PersistBool(string key, bool value)
    {
        RecompOne.Runtime.Runtime.View.SetBool(key, value);
        RecompOne.Runtime.Runtime.SaveView();
    }
}
