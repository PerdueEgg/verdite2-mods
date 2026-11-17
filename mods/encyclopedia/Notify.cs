using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace Kf2.Mods.Encyclopedia;

/// <summary>
/// A notification for each entry unlocked in play, in the port's own pop-ups:
///
///     Encyclopedia updated
///     1. Kraken
///
/// with the entry's number and name as its list shows them (an item numbered on its
/// own equipment page, a place in its area's list). One switch per list, all on by
/// default. Only what is found in play: Unlock all and a loaded save say nothing.
/// One discovery can unlock more than one entry (Tarn brings Freeze Ball), and each
/// gets its own.
/// </summary>
public sealed partial class EncyclopediaMod
{
    static readonly bool[] _notify = [true, true, true, true];
    const string Title = "Encyclopedia updated";

    static void LoadNotifySettings()
    {
        var view = RecompOne.Runtime.Runtime.View;
        for (int k = 0; k < 4; k++)
            _notify[k] = view.GetBool($"kf2.encyclopedia.notify.{CatKeys[k]}", true);
    }

    /// <summary>The entries of every list that are still locked, before a discovery.</summary>
    static List<(int Cat, Entry E)> Locked()
    {
        var locked = new List<(int, Entry)>();
        if (_notify[CatEnemies]) foreach (var e in Data.Enemies) if (!IsUnlocked(e)) locked.Add((CatEnemies, e));
        if (_notify[CatPeople]) foreach (var e in Data.People) if (!IsUnlocked(e)) locked.Add((CatPeople, e));
        if (_notify[CatPlaces]) foreach (var e in Places) if (!IsUnlocked(e)) locked.Add((CatPlaces, e));
        return locked;
    }

    /// <summary>After a discovery: a pop-up for each entry it unlocked.</summary>
    static void Announce(int desc, List<(int Cat, Entry E)> before)
    {
        try
        {
            if (CatOf(desc) == CatItems)
            {
                if (_notify[CatItems]) AnnounceItem(desc - ItemDescBase);
                return;
            }
            foreach (var (cat, e) in before)
            {
                if (!IsUnlocked(e)) continue;
                int number = cat switch
                {
                    CatEnemies => Array.IndexOf(Data.Enemies, e) + 1,
                    CatPeople => Array.IndexOf(Data.People, e) + 1,
                    _ => Array.IndexOf(Places.Where(p => p.Area == e.Area).ToArray(), e) + 1,
                };
                Show(number, NoThe(L(e.Name)));
            }
        }
        catch (Exception ex) { Console.WriteLine($"[encyclopedia] notification: {ex.Message}"); }
    }

    static void AnnounceItem(int id)
    {
        foreach (var (_, holds) in EquipmentKinds)
        {
            if (!holds(id)) continue;
            var ids = ItemIds().Where(holds).ToList();
            int at = ids.IndexOf(id);
            if (at >= 0) Show(at + 1, TextApi.MenuShown(ItemNameOf(id)));
            return;
        }
    }

    static void Show(int number, string name) => ToastNotifications.ShowText(Title, $"{number}. {name}");

    static void DrawNotifySettings()
    {
        ImGui.Text("Notifications");
        ImGui.TextDisabled("A pop-up for each entry found in play (\"Encyclopedia updated: 1. Kraken\"):");
        string[] text = ["Enemies", "People", "Places", "Equipment"];
        for (int k = 0; k < 4; k++)
        {
            if (k > 0) ImGui.SameLine();
            if (ImGui.Checkbox(text[k] + "##notify", ref _notify[k])) PersistBool($"kf2.encyclopedia.notify.{CatKeys[k]}", _notify[k]);
        }
    }
}
