namespace ClownSquad.Progression;

using System.Collections.Generic;
using Godot;

/// <summary>
/// What gear looks like when it is worn (tools/gen_gear.py draws it). Each
/// item's base picks a model (a top hat, roller skates, a bow tie...) and its
/// id picks one of six colourways, so two of the same hat seldom match;
/// legendary gear comes in gold. With nothing in a slot, a clown wears their
/// own party hat and clown shoes, in their colours.
/// </summary>
public static class GearLook
{
    private const string Folder = "res://assets/sprites/gear/";

    /// <summary>Every base that can drop, the slot it goes in, and the model it is drawn as.</summary>
    private static readonly (string Base, ItemSlot Slot, string Model)[] Models =
    {
        ("Party Hat", ItemSlot.Hat, "party"), ("Top Hat", ItemSlot.Hat, "top"), ("Jester Cap", ItemSlot.Hat, "jester"),
        ("Bowler Hat", ItemSlot.Hat, "bowler"), ("Propeller Beanie", ItemSlot.Hat, "propeller"),
        ("Paper Crown", ItemSlot.Hat, "crown"), ("Fez", ItemSlot.Hat, "fez"),
        ("Frilly Ruff", ItemSlot.Ruff, "ruff"), ("Bow Tie", ItemSlot.Ruff, "bowtie"), ("Polka Scarf", ItemSlot.Ruff, "scarf"),
        ("Big Necktie", ItemSlot.Ruff, "necktie"), ("Hero's Medal", ItemSlot.Ruff, "medal"),
        ("Floppy Shoes", ItemSlot.Shoes, "floppy"), ("Squeaky Sneakers", ItemSlot.Shoes, "sneaker"),
        ("Roller Skates", ItemSlot.Shoes, "skate"), ("Stripy Boots", ItemSlot.Shoes, "boot"), ("Bunny Slippers", ItemSlot.Shoes, "bunny"),
        ("Squeaky Mallet", ItemSlot.Mallet, "ribbon"), ("Rubber Bonker", ItemSlot.Mallet, "bell"), ("Giant Gavel", ItemSlot.Mallet, "star"),
    };

    private static string SlotName(ItemSlot s) => s switch
    {
        ItemSlot.Hat => "hat", ItemSlot.Ruff => "neck", ItemSlot.Shoes => "shoes", _ => "charm",
    };

    /// <summary>The model an item is drawn as (from the base in its name), or null.</summary>
    public static string? ModelOf(ItemData item)
    {
        // longest base first, so "Party Hat" wins over a shorter name inside it
        string? best = null;
        int len = 0;
        foreach ((string b, ItemSlot slot, string model) in Models)
            if (slot == item.Slot && item.Name.Contains(b) && b.Length > len) { best = model; len = b.Length; }
        return best;
    }

    /// <summary>An item's colourway: picked by its id (stable for the item's life), gold if legendary.</summary>
    public static string Colourway(ItemData item)
    {
        if (item.Rarity == ItemRarity.Legendary) return "6";
        uint h = 2166136261;
        foreach (char ch in item.Id) { h ^= ch; h *= 16777619; }
        return (h % 6).ToString();
    }

    /// <summary>
    /// The sheet for what shows in a slot: the item worn there, or the clown's
    /// own party hat and clown shoes with nothing in it. Null for an empty
    /// neck or weapon slot (nothing extra to draw).
    /// </summary>
    public static string? SheetFor(ItemData? item, ItemSlot slot, string look)
    {
        if (item is not null && ModelOf(item) is string model)
        {
            string path = $"{Folder}{SlotName(slot)}_{model}_{Colourway(item)}.png";
            if (ResourceLoader.Exists(path)) return path;
        }
        string id = Roster.Get(look).Id;
        return slot switch
        {
            ItemSlot.Hat => $"{Folder}hat_party_{id}.png",
            ItemSlot.Shoes => $"{Folder}shoes_clown_{id}.png",
            _ => null,
        };
    }

    /// <summary>The pieces a clown shows: where each hangs (a named point of the frame) and its sheet.</summary>
    public static List<(string anchor, string sheet, bool far)> Pieces(CharacterRecord r, ItemData? tryOn = null)
    {
        ItemData? Worn(ItemSlot s) => tryOn is not null && tryOn.Slot == s ? tryOn : r.EquippedIn(s);
        var list = new List<(string, string, bool)>();
        if (SheetFor(Worn(ItemSlot.Shoes), ItemSlot.Shoes, r.Look) is string shoes)
        {
            list.Add(("ffoot", shoes, true));
            list.Add(("nfoot", shoes, false));
        }
        if (SheetFor(Worn(ItemSlot.Ruff), ItemSlot.Ruff, r.Look) is string neck) list.Add(("neck", neck, false));
        if (SheetFor(Worn(ItemSlot.Hat), ItemSlot.Hat, r.Look) is string hat) list.Add(("brim", hat, false));
        if (SheetFor(Worn(ItemSlot.Mallet), ItemSlot.Mallet, r.Look) is string charm) list.Add(("nhand", charm, false));
        return list;
    }
}
