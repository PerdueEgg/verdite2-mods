using System;
using System.Collections.Generic;
using System.Text;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// The fourth list: every item, with the description the fortune teller gives.
///
/// The list is built from the game itself: item id i's name is the font-index
/// record at 0x80065B24 + i * 0x18, so the names follow whatever disc is in, and
/// an unused id is the placeholder record 00 FF, which is exactly the ids with no
/// picture (Item Info's rule). The description is ITEM.T 0x168 + id.
///
/// Discovering an item (recorded as 360 + id, beside the creatures and places):
///
/// - the fortune teller, always: func_80047000 is the
///   only caller that shows an item's card, func_80035B48(6, 0x168 + id),
///   returning to 0x800473AC. Picking an item up does not show it.
/// - using it, with "Unlock by encounter":
///   - on the world (keys, crystals, the flute, ids 82-116): func_800474D0(a0,
///     a1, id), counted only when it sets its "something happened" flag at
///     0x801B3084 -- the flag behind "Nothing happens" -- so a key counts once it
///     has opened its door;
///   - on yourself (71-80): func_800197D4(id), with one in the inventory;
///   - a map (67-69): func_80019B54(id), which shows it;
///   - equipment: equipping it -- the weapon at 0x801994AF and the seven slots at
///     0x801994D4-DA (func_800244CC's own reads; 0xFF is empty);
///   - arrows (117, 118): firing them -- their count dropping, outside a menu.
/// </summary>
public sealed partial class EncyclopediaMod
{
    const uint ItemNames = 0x80065B24, ItemNameSize = 0x18;
    const int ItemCount = 120;
    const int ItemDescBase = 0x168;               // 360
    const uint Inventory = 0x8009B52C;            // u8 per id
    const uint FortuneTellerCall = 0x800473AC;    // func_80047000 -> func_80035B48(6, 0x168 + id)
    const uint EquippedWeapon = 0x801994AF;
    const uint EquippedSlots = 0x801994D4;        // seven bytes
    const int Arrows = 117, Bolts = 118;
    const int GoldCoin = 70;

    static List<int>? _itemIds;
    static readonly Dictionary<int, string> _itemNames = new();
    static int _lastArrows = -1, _lastBolts = -1;
    static uint _useItem;
    static bool _inMenu;

    /// <summary>The ids that are real items, in id order (the inventory's own
    /// order). Read once from the name table, which never changes.</summary>
    static List<int> ItemIds()
    {
        if (_itemIds != null) return _itemIds;
        var m = RecompOne.Runtime.Runtime.Mem;
        var ids = new List<int>();
        if (m == null) return ids;
        for (int id = 0; id < ItemCount; id++)
        {
            uint rec = ItemNames + (uint)id * ItemNameSize;
            if (m.ReadU8(rec) == 0x00 && m.ReadU8(rec + 1) == 0xFF) continue;   // unused
            if (id == GoldCoin) continue;                                       // money, not an item: no description
            ids.Add(id);
            _itemNames[id] = ItemName(m, rec);
        }
        if (ids.Count > 0) _itemIds = ids;
        return ids;
    }

    /// <summary>A name record in the menu font's cells, back to text, through the
    /// port's lettering (so lower case and any cell it adds read back too), or the
    /// disc's capitals without it.</summary>
    static string ItemName(IMemory m, uint rec)
    {
        var sb = new StringBuilder();
        for (uint i = 0; i < ItemNameSize; i++)
        {
            int g = m.ReadU8(rec + i);
            if (g == 0xFF) break;
            char c = TextApi.Present ? TextApi.MenuChar(g) : DiscChar(g);
            sb.Append(c == '\0' ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>An item's name as it is in RAM now: a translation may rewrite the
    /// name table after this mod first read it.</summary>
    static string ItemNameOf(int id)
    {
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null) return _itemNames.TryGetValue(id, out var n) ? n : "";
        return ItemName(m, ItemNames + (uint)id * ItemNameSize);
    }

    static bool ItemUnlocked(int id) => _seen.Contains(ItemDescBase + id);
    static int ItemsUnlocked() { int n = 0; foreach (int id in ItemIds()) if (ItemUnlocked(id)) n++; return n; }
    static void MarkItem(int id) { if (id >= 0 && id < ItemCount) Mark(ItemDescBase + id); }

    // ---- discovering ----

    /// <summary>The fortune teller's card. Called from the message pre-hook.</summary>
    static void OnItemCard(uint ra, uint entry)
    {
        if (ra == FortuneTellerCall && entry >= ItemDescBase && entry < ItemDescBase + ItemCount)
            MarkItem((int)entry - ItemDescBase);
    }

    [PreHook("game", Address = 0x800474D0)]
    static bool BeforeWorldUse(CpuContext c, IMemory m)
    {
        _useItem = c.A2;
        return true;
    }

    [PostHook("game", Address = 0x800474D0)]
    static void AfterWorldUse(CpuContext c, IMemory m)
    {
        if (_enc[CatItems] && m.ReadU32(UseHappened) != 0) MarkItem((int)_useItem);
    }

    [PreHook("game", Address = 0x800197D4)]
    static bool BeforeSelfUse(CpuContext c, IMemory m)
    {
        int id = (int)c.A0;
        if (_enc[CatItems] && id >= 0 && id < ItemCount && m.ReadU8(Inventory + (uint)id) != 0) MarkItem(id);
        return true;
    }

    [PreHook("game", Address = 0x80019B54)]
    static bool BeforeMapView(CpuContext c, IMemory m)
    {
        if (_enc[CatItems]) MarkItem((int)c.A0);
        return true;
    }

    // The in-game menu, for the arrow count: selling arrows in a shop is not firing them.
    [PreHook("game", Address = 0x80022754)] static bool MenuIn(CpuContext c, IMemory m) { _inMenu = true; return true; }
    [PostHook("game", Address = 0x800228C8)] static void MenuOut(CpuContext c, IMemory m) { _inMenu = false; ResetArrowCount(m); }

    static void ResetArrowCount(IMemory m)
    {
        _lastArrows = m.ReadU8(Inventory + Arrows);
        _lastBolts = m.ReadU8(Inventory + Bolts);
    }

    /// <summary>Each tick: equipped items count as used, and arrows as used once
    /// one has been fired.</summary>
    static void CheckItems(IMemory m)
    {
        if (!_enc[CatItems]) return;
        int w = m.ReadU8(EquippedWeapon);
        if (w != 0xFF) MarkItem(w);
        for (uint i = 0; i < 7; i++)
        {
            int s = m.ReadU8(EquippedSlots + i);
            if (s != 0xFF) MarkItem(s);
        }
        if (_inMenu) return;
        int a = m.ReadU8(Inventory + Arrows), b = m.ReadU8(Inventory + Bolts);
        if (_lastArrows >= 0 && a < _lastArrows) MarkItem(Arrows);
        if (_lastBolts >= 0 && b < _lastBolts) MarkItem(Bolts);
        _lastArrows = a; _lastBolts = b;
    }
}
