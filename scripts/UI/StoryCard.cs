namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Map;

/// <summary>
/// The story, told one card at a time on the world map: the first time the
/// squad reaches each world, a card says what has gone wrong there and who is
/// headlining; after the Ringmaster falls, the last card. A card is shown once
/// per save (CampaignState.StorySeen) and dismissed with ATTACK.
///
/// The plot: the Ringmaster has stolen the circus's laughter into his top hat
/// and hypnotised every act to perform forever, for no one. Only the Clown
/// Squad still remembers the punchline.
/// </summary>
public partial class StoryCard : CanvasLayer
{
    /// <summary>The finale's card number (one past the last world).</summary>
    public const int Ending = Campaign.Worlds + 1;

    private sealed record Page(string Title, string Text, string? Headliner);

    private static readonly Dictionary<int, Page> Pages = new()
    {
        [1] = new("THE BIG TOP WENT QUIET",
            "Overnight the laughter vanished from the circus. Every act wears the Ringmaster's glassy stare, and the crowd sits in silence. Only the Clown Squad still remembers the punchline.",
            "bertie"),
        [2] = new("SWEETS BY THE SEA",
            "The candy stalls on the coast stand empty. Bobo the Balancer hasn't stepped off his ball in three days, and he can't remember why he got on.",
            "ballrider"),
        [3] = new("LOST IN THE FUNHOUSE",
            "The forest path twists back on itself. Somewhere inside, Madame Stripes drills Rajah the tiger for a show with no audience.",
            "trainer"),
        [4] = new("EVERY ROAD, ONE CAR",
            "Every road crosses at the junction, and the Jalopy runs them all, honking, packed with clowns who can't get out.",
            "clowncar"),
        [5] = new("WHICH ONE IS REAL?",
            "Every mirror shows a different clown. Stretch has been pulled so thin by the Ringmaster's orders that he reaches halfway across the hall.",
            "elastic"),
        [6] = new("NO NET",
            "High above the clouds the Flying Fratelli swing without a net, and without stopping. Catch them when they land.",
            "trapeze"),
        [7] = new("HOT ENOUGH TO POP CORN",
            "Pyro Pete has been breathing fire since the show began, and the desert has only got hotter. Worse: he's starting to enjoy it.",
            "fireeater"),
        [8] = new("THE SONG THAT NEVER ENDS",
            "Deep underground the steam organ plays one tune on repeat. The Totem Tots stack, topple and stack again, in time.",
            "tots"),
        [9] = new("THE GATES OF THE FORTRESS",
            "The Ringmaster's gates are held by Iron Ivan, the strongest man under any big top. Nothing has ever made Ivan move. Yet.",
            "strongman"),
        [10] = new("THE GRAND FINALE",
            "In the centre ring the Ringmaster waits, his top hat stuffed with every laugh he stole. Ladies and gentlemen... it is time to end the show.",
            "ringmaster"),
        [Ending] = new("THE LAST LAUGH",
            "The top hat tips over and the laughter spills out across all ten worlds. The acts blink awake, the crowd roars, and the show goes on the way it should: with clowns.",
            null),
    };

    public static bool Has(int card) => Pages.ContainsKey(card);

    private readonly int _card;
    private readonly List<MapPad> _pads = new();
    private double _shownAt;

    public event Action? Closed;

    public StoryCard(int card, IEnumerable<int> devices)
    {
        _card = card;
        foreach (int d in devices) _pads.Add(new MapPad(d));
        Layer = 26;
        ProcessMode = ProcessModeEnum.Always;
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
    };

    public override void _Ready()
    {
        foreach (MapPad p in _pads) p.Swallow();
        Page page = Pages[_card];
        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.82f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.98f, 0.94f, 0.82f), BorderColor = new Color(0.12f, 0.06f, 0.14f),
            BorderWidthLeft = 6, BorderWidthRight = 6, BorderWidthTop = 6, BorderWidthBottom = 6,
            ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 18, ContentMarginBottom = 20,
            ShadowColor = new Color(0f, 0f, 0f, 0.5f), ShadowSize = 10, ShadowOffset = new Vector2(8, 8),
        });
        centre.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        string tag = _card == Ending ? "THE END" : $"WORLD {_card}: {Campaign.WorldNames[_card - 1]}";
        box.AddChild(new Label { Text = tag, HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(18, new Color(0.85f, 0.25f, 0.3f), 0) });
        box.AddChild(new Label { Text = page.Title, HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(44, new Color(1f, 0.82f, 0.25f), 9) });

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 22);
        box.AddChild(row);
        if (page.Headliner is string kind)
        {
            var who = new VBoxContainer();
            TextureRect face = Portraits.Make(EnemyCatalog.SheetOf(kind), 150);
            who.AddChild(face);
            who.AddChild(new Label
            {
                Text = "HEADLINING:\n" + EnemyCatalog.Info(kind).title, HorizontalAlignment = HorizontalAlignment.Center,
                LabelSettings = Style(15, new Color(0.2f, 0.1f, 0.25f), 0),
            });
            row.AddChild(who);
        }
        row.AddChild(new Label
        {
            Text = page.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(page.Headliner is null ? 680 : 500, 0),
            VerticalAlignment = VerticalAlignment.Center, LabelSettings = Style(21, new Color(0.15f, 0.08f, 0.2f), 0),
        });
        box.AddChild(new Label { Text = "ATTACK  continue", HorizontalAlignment = HorizontalAlignment.Right, LabelSettings = Style(14, new Color(0.45f, 0.35f, 0.5f), 0) });

        // In like a comic panel: a pop and a slight tilt.
        panel.PivotOffset = new Vector2(380, 150);
        panel.Scale = new Vector2(0.7f, 0.7f);
        panel.Rotation = -0.05f;
        Tween t = CreateTween().SetParallel();
        t.TweenProperty(panel, "scale", Vector2.One, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenProperty(panel, "rotation", -0.015f, 0.35f);
        Sfx.Play(_card == Ending ? "fanfare" : "drumroll", 1f, -4f);
        _shownAt = Time.GetTicksMsec() / 1000.0;
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() / 1000.0 - _shownAt < 0.6) return;     // a moment to read before a press can close it
        foreach (MapPad pad in _pads)
        {
            MapPad.Press p = pad.Poll();
            if (p is MapPad.Press.Enter or MapPad.Press.Back)
            {
                Sfx.Play("clack");
                Closed?.Invoke();
                QueueFree();
                return;
            }
        }
    }
}
