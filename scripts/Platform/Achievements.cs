namespace ClownSquad.Services;

using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Progression;
using ClownSquad.UI;

/// <summary>One achievement: its id (the same id is used in Steamworks), name and how to earn it.</summary>
public sealed record Achievement(string Id, string Name, string Description, bool Hidden = false);

/// <summary>
/// The achievement list, and the one call that awards them. The ids are the
/// API names to enter in Steamworks (docs/STEAM.md lists them all). Awarding
/// is a no-op in practice and tests, and for anything already earned; a
/// new one pops a small card in the corner with a ding.
/// </summary>
public static class Achievements
{
    public static readonly Achievement[] All = Build();

    private static Achievement[] Build()
    {
        var list = new List<Achievement>
        {
            new("first_clear", "OPENING NIGHT", "Clear your first stage."),
            new("bonus_clear", "PINATA PARTY", "Clear a bonus stage."),
            new("flawless", "NOT A SCRATCH", "Clear a stage without anyone taking a hit."),
            new("secret_shop", "BEHIND THE CURTAIN", "Find the secret shop."),
            new("legendary", "ONCE IN A BLUE MOON", "Pick up a Legendary item."),
            new("combo_25", "CROWD PLEASER", "Land a 25-hit combo."),
            new("combo_50", "STANDING OVATION", "Land a 50-hit combo."),
            new("level_10", "UP AND COMING", "Reach level 10."),
            new("level_25", "HEADLINER", "Reach level 25."),
            new("level_50", "LEGEND OF THE RING", "Reach level 50."),
            new("full_tree", "MASTER OF ONE", "Learn every skill in a weapon's tree."),
            new("all_acts", "SEEN IT ALL", "Meet every act in the circus."),
            new("hard_boss", "NO SAFETY NET", "Beat a boss on Hard."),
            new("rush_clear", "THE FULL SHOW", "Beat the boss rush."),
            new("rush_fast", "QUICK CHANGE", "Beat the boss rush in under 20 minutes."),
            new("together", "SEND IN THE CLOWNS", "Play an online game with a friend."),
            new("ring_bell", "RING THE BELL", "Ring the High Striker with Big Bertha."),
            new("daily_clear", "SAME TIME TOMORROW", "Clear a Daily Circus."),
        };
        string[] bosses = { "BELLYFLOP BERTIE", "BOBO THE BALANCER", "MADAME STRIPES", "THE JALOPY", "STRETCH",
                            "THE FLYING FRATELLI", "PYRO PETE", "THE TOTEM TOTS", "IRON IVAN", "THE RINGMASTER" };
        for (int w = 1; w <= bosses.Length; w++)
            list.Add(new($"boss_{w}", w == 10 ? "THE GREATEST SHOW" : $"WORLD {w} TAMED", $"Beat {bosses[w - 1]}.", Hidden: w == 10));
        foreach (WeaponDef wd in Weapons.All)
            list.Add(new($"win_{wd.Kind.ToString().ToLowerInvariant()}", $"{wd.Name} CHAMP", $"Beat a boss holding the {wd.Name.ToLowerInvariant()}."));
        return list.ToArray();
    }

    public static Achievement? Find(string id) => System.Array.Find(All, a => a.Id == id);

    /// <summary>Award an achievement if this kind of play counts and it is not already earned.</summary>
    public static void Award(string id)
    {
        if (!Platform.Counts || Platform.Current.IsUnlocked(id) || Find(id) is not Achievement a) return;
        Platform.Current.Unlock(id);
        GD.Print($"Achievement: {a.Name} ({id}).");
        Toast(a);
    }

    private static void Toast(Achievement a)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null) return;
        var layer = new CanvasLayer { Layer = 30, ProcessMode = Node.ProcessModeEnum.Always };
        var panel = new PanelContainer { Position = new Vector2(-400, 90) };
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.09f, 0.24f, 0.96f), BorderColor = new Color(1f, 0.85f, 0.3f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 10,
        });
        var box = new VBoxContainer();
        panel.AddChild(box);
        box.AddChild(new Label { Text = "ACHIEVEMENT!", LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = 14, FontColor = new Color(1f, 0.85f, 0.3f), OutlineSize = 3, OutlineColor = Colors.Black } });
        box.AddChild(new Label { Text = a.Name, LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = 22, FontColor = Colors.White, OutlineSize = 4, OutlineColor = Colors.Black } });
        box.AddChild(new Label { Text = a.Description, CustomMinimumSize = new Vector2(360, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart, LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = 13, FontColor = new Color(0.85f, 0.8f, 0.95f), OutlineSize = 2, OutlineColor = Colors.Black } });
        layer.AddChild(panel);
        tree.Root.CallDeferred(Node.MethodName.AddChild, layer);
        Sfx.Play("tada", 1.3f, -8f);
        Tween t = panel.CreateTween();
        t.TweenInterval(3.5);
        t.TweenProperty(panel, "modulate:a", 0f, 0.5);
        t.TweenCallback(Callable.From(layer.QueueFree));
    }
}
