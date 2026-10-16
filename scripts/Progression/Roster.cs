namespace ClownSquad.Progression;

using Godot;
using ClownSquad.Visuals;

/// <summary>
/// The four clowns of the squad. Same moves, same body, each in their own
/// colours (tools/gen_sprite_hd.py draws a sheet per clown). Picked on the
/// character select screen; a character record remembers which it is.
/// </summary>
public static class Roster
{
    public sealed record Clown(string Id, string Name, string Blurb, Color Color);

    public static readonly Clown[] All =
    {
        new("pip", "PIP", "Blue suit, big heart, bigger mallet.", new Color(0.3f, 0.45f, 0.9f)),
        new("poppy", "POPPY", "Pink polka-dot powerhouse.", new Color(0.92f, 0.44f, 0.67f)),
        new("bingo", "BINGO", "Orange, loud, and always first in.", new Color(0.93f, 0.55f, 0.22f)),
        new("dot", "DOT", "Sunny yellow. Never stops smiling.", new Color(0.96f, 0.8f, 0.27f)),
    };

    public static Clown Get(string id)
    {
        foreach (Clown c in All) if (c.Id == id) return c;
        return All[0];
    }

    /// <summary>The clown a player slot gets when nobody has chosen.</summary>
    public static Clown DefaultFor(int slot) => All[slot % All.Length];

    /// <summary>The clown bare-headed and barefoot, for wearing gear in play (GearOverlay). HD art only.</summary>
    public static string BareSheet(string id, WeaponKind weapon = WeaponKind.Mallet) =>
        $"res://assets/sprites/{Get(id).Id}{Weapons.SheetSuffix(weapon)}_bare_hd.png";

    /// <summary>A clown's sprite sheet, holding a weapon. The classic art has only one clown, with a mallet.</summary>
    public static string Sheet(string id, WeaponKind weapon = WeaponKind.Mallet) =>
        ArtStyle.Sheet("res://assets/sprites/clown_16bit.png", $"res://assets/sprites/{Get(id).Id}{Weapons.SheetSuffix(weapon)}_hd.png");
}
