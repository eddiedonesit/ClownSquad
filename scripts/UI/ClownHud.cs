namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Visuals;
using ClownSquad.Actors;
using ClownSquad.Progression;
using ClownSquad.World;

/// <summary>
/// The circus HUD. Laid out on the brawler conventions players already read
/// without thinking (Streets of Rage, Shredder's Revenge, Castle Crashers):
/// your portrait and bar top left, the enemy you are hitting named underneath
/// it, the boss bar across the bottom, a combo counter at the side, and big
/// banners only at the moments that matter. Dressed as a circus: candy-stripe
/// bars, a ticket for the wave count, balloons for lives, and combo ranks that
/// escalate from SILLY to RINGMASTER.
/// </summary>
public partial class ClownHud : CanvasLayer
{
    private static string PlayerSheet => ArtStyle.Sheet("res://assets/sprites/clown_16bit.png", "res://assets/sprites/pip_hd.png");
    private static string GruntSheet => ArtStyle.Sheet("res://assets/sprites/grunt_clown.png", "res://assets/sprites/grunt_hd.png");
    private static string BossSheet => ArtStyle.Sheet("res://assets/sprites/boss_clown.png", "res://assets/sprites/boss_hd.png");

    private static readonly (int at, string word, Color color)[] Ranks =
    {
        (3, "SILLY!", new(1f, 0.85f, 0.3f)),
        (6, "GOOFY!", new(0.5f, 0.9f, 0.5f)),
        (10, "BONKERS!", new(0.45f, 0.75f, 1f)),
        (15, "CIRCUS!", new(1f, 0.5f, 0.85f)),
        (25, "RINGMASTER!", new(1f, 0.35f, 0.3f)),
    };

    private readonly Func<IReadOnlyList<PlayerFighter>> _players;
    private readonly Func<WaveDirector?> _director;
    private readonly Func<bool> _canReplay;

    private Font _font = null!;
    private readonly List<(Control root, CandyBar bar, Label name)> _panels = new();
    private readonly List<(Label level, ColorRect xp, Label points)> _progress = new();
    private readonly List<Label> _bags = new();
    private readonly int[] _levelShown = new int[4];
    private Control _target = null!;
    private CandyBar _targetBar = null!;
    private Label _targetName = null!;
    private TextureRect _targetFace = null!;
    private Fighter? _targetFighter;
    private double _targetUntil;

    private Control _ticket = null!;
    private Label _ticketText = null!;
    private BalloonRow _balloons = null!;

    private Control _bossRoot = null!;
    private CandyBar _bossBar = null!;
    private Label _bossName = null!;

    private Label _combo = null!;
    private Label _rank = null!;
    private int _comboShown;

    private Label _banner = null!;
    private Label _subBanner = null!;
    private string _bannerShown = "";

    private Label _hint = null!;
    private double _clock;
    private bool _f1Down;

    public int Combo { get; set; }

    /// <summary>Items each player is carrying this stage, banked on a clear.</summary>
    public Func<PlayerFighter, int>? BagCount { get; set; }

    public ClownHud(Func<IReadOnlyList<PlayerFighter>> players, Func<WaveDirector?> director, Func<bool> canReplay)
    {
        _players = players;
        _director = director;
        _canReplay = canReplay;
    }

    public override void _Ready()
    {
        _font = new SystemFont
        {
            // Rounded and goofy where available, chunky everywhere else. A
            // licensed display font is the upgrade path; see STATUS.
            FontNames = new[] { "Comic Sans MS", "Segoe UI Black", "Arial Black" },
            FontWeight = 800,
        };

        for (int i = 0; i < 4; i++) _panels.Add(BuildPlayerPanel(i));
        BuildTarget();
        BuildTicket();
        BuildBoss();
        BuildCombo();
        BuildBanner();
        BuildHint();

        Fighter.HitLanded += OnHit;
    }

    public override void _ExitTree() => Fighter.HitLanded -= OnHit;

    // ------------------------------------------------------------ pieces

    private LabelSettings Style(int size, Color color, int outline = 6) => new()
    {
        Font = _font,
        FontSize = size,
        FontColor = color,
        OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f),
        ShadowSize = 2,
        ShadowColor = new Color(0f, 0f, 0f, 0.45f),
        ShadowOffset = new Vector2(2f, 3f),
    };

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 10, int width = 3) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ShadowColor = new Color(0f, 0f, 0f, 0.35f),
        ShadowSize = 4,
        ShadowOffset = new Vector2(2f, 3f),
    };

    private static TextureRect Face(string sheet, Rect2 region) => new()
    {
        Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>(sheet), Region = region },
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
    };

    // Head crops from the first idle frame of each sheet.
    private static Rect2 ClownHead => ArtStyle.Classic ? new(36, 45, 32, 34) : new(72, 90, 64, 68);
    private static Rect2 BossHead => ArtStyle.Classic ? new(26, 0, 40, 34) : new(52, 4, 80, 76);

    private (Control, CandyBar, Label) BuildPlayerPanel(int index)
    {
        var root = new Control { Position = new Vector2(14 + index * 286, 12), Size = new Vector2(270, 68), Visible = false };
        AddChild(root);

        var plate = new Panel { Position = new Vector2(30, 14), Size = new Vector2(236, 50) };
        plate.AddThemeStyleboxOverride("panel", Plate(new Color(0.18f, 0.12f, 0.28f, 0.92f), new Color(0.98f, 0.8f, 0.2f)));
        root.AddChild(plate);

        var ring = new Panel { Size = new Vector2(64, 64), Position = new Vector2(0, 4) };
        ring.AddThemeStyleboxOverride("panel", Plate(new Color(0.95f, 0.92f, 0.85f), new Color(0.86f, 0.2f, 0.25f), 32, 4));
        root.AddChild(ring);
        var face = Face(PlayerSheet, ClownHead);
        face.Position = new Vector2(6, 8);
        face.Size = new Vector2(52, 52);
        root.AddChild(face);

        var name = new Label { Position = new Vector2(74, 10), LabelSettings = Style(18, new Color(1f, 0.85f, 0.3f), 5) };
        root.AddChild(name);
        var bar = new CandyBar { Position = new Vector2(72, 36), Size = new Vector2(184, 20) };
        root.AddChild(bar);

        // Level at the right of the name, a thin XP bar under the health bar,
        // and a nudge to spend points whenever there are some.
        var level = new Label
        {
            Position = new Vector2(186, 12), Size = new Vector2(70, 22),
            HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(15, new Color(0.6f, 0.92f, 1f), 4),
            PivotOffset = new Vector2(55, 11),
        };
        root.AddChild(level);
        root.AddChild(new ColorRect { Position = new Vector2(74, 57), Size = new Vector2(180, 4), Color = new Color(0.08f, 0.05f, 0.12f) });
        var xp = new ColorRect { Position = new Vector2(74, 57), Size = new Vector2(0, 4), Color = new Color(0.5f, 0.9f, 1f) };
        root.AddChild(xp);
        // On its own little plate, or it is lost against whatever walks behind it.
        var points = new Label
        {
            Position = new Vector2(66, 64),
            LabelSettings = Style(13, new Color(1f, 0.85f, 0.3f), 4),
            Visible = false,
        };
        points.AddThemeStyleboxOverride("normal", Plate(new Color(0.18f, 0.12f, 0.28f, 0.92f), new Color(1f, 0.85f, 0.3f, 0.8f), 8, 2));
        root.AddChild(points);
        _progress.Add((level, xp, points));
        var bag = new Label
        {
            Position = new Vector2(186, 64), Size = new Vector2(70, 20),
            HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(13, new Color(0.6f, 1f, 0.7f), 4),
            Visible = false,
        };
        root.AddChild(bag);
        _bags.Add(bag);
        return (root, bar, name);
    }

    private void BuildTarget()
    {
        _target = new Control { Position = new Vector2(34, 100), Size = new Vector2(240, 44), Visible = false };
        AddChild(_target);
        _targetFace = Face(GruntSheet, ClownHead);
        _targetFace.Size = new Vector2(36, 36);
        _target.AddChild(_targetFace);
        _targetName = new Label { Position = new Vector2(42, -4), LabelSettings = Style(15, new Color(0.7f, 1f, 0.7f), 5) };
        _target.AddChild(_targetName);
        _targetBar = new CandyBar
        {
            Position = new Vector2(42, 20), Size = new Vector2(150, 14),
            StripeA = new Color(0.35f, 0.75f, 0.4f), StripeB = new Color(0.95f, 1f, 0.9f),
        };
        _target.AddChild(_targetBar);
    }

    private void BuildTicket()
    {
        _ticket = new Control();
        _ticket.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _ticket.Position = new Vector2(-110, 10);
        _ticket.Size = new Vector2(220, 74);
        AddChild(_ticket);

        var plate = new Panel { Size = new Vector2(220, 40) };
        plate.AddThemeStyleboxOverride("panel", Plate(new Color(0.86f, 0.2f, 0.25f), new Color(0.98f, 0.8f, 0.2f), 6, 4));
        _ticket.AddChild(plate);
        _ticketText = new Label
        {
            Size = new Vector2(220, 40),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = Style(20, new Color(1f, 0.97f, 0.9f), 5),
        };
        _ticket.AddChild(_ticketText);
        _balloons = new BalloonRow { Position = new Vector2(0, 42), Size = new Vector2(220, 40) };
        _ticket.AddChild(_balloons);
    }

    private void BuildBoss()
    {
        _bossRoot = new Control { Visible = false };
        _bossRoot.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _bossRoot.Position = new Vector2(-300, -78);
        _bossRoot.Size = new Vector2(600, 64);
        AddChild(_bossRoot);

        var face = Face(BossSheet, BossHead);
        face.Size = new Vector2(60, 56);
        _bossRoot.AddChild(face);
        _bossName = new Label { Position = new Vector2(66, -2), LabelSettings = Style(20, new Color(1f, 0.6f, 0.9f), 6) };
        _bossRoot.AddChild(_bossName);
        _bossBar = new CandyBar
        {
            Position = new Vector2(64, 28), Size = new Vector2(530, 24),
            StripeA = new Color(0.45f, 0.2f, 0.55f), StripeB = new Color(0.98f, 0.8f, 0.2f),
        };
        _bossRoot.AddChild(_bossBar);
    }

    private void BuildCombo()
    {
        _combo = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(46, new Color(1f, 0.85f, 0.3f), 9),
            Visible = false,
        };
        _combo.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        _combo.Position = new Vector2(-260, -90);
        _combo.Size = new Vector2(240, 60);
        _combo.PivotOffset = new Vector2(200, 30);
        AddChild(_combo);

        _rank = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(24, new Color(1f, 1f, 1f), 6),
            Visible = false,
        };
        _rank.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        _rank.Position = new Vector2(-260, -36);
        _rank.Size = new Vector2(240, 34);
        AddChild(_rank);
    }

    private void BuildBanner()
    {
        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(64, new Color(1f, 0.85f, 0.3f), 12),
        };
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.Position = new Vector2(-400, -150);
        _banner.Size = new Vector2(800, 90);
        _banner.PivotOffset = new Vector2(400, 45);
        AddChild(_banner);

        _subBanner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(22, new Color(1f, 1f, 1f), 6),
        };
        _subBanner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _subBanner.Position = new Vector2(-400, -60);
        _subBanner.Size = new Vector2(800, 40);
        AddChild(_subBanner);
    }

    private void BuildHint()
    {
        _hint = new Label
        {
            Text = "move WASD/stick   attack J/X   jump Space/A   dodge K/B\n" +
                   "hold attack: throw   +left/right: spin   attack in air: flip\n" +
                   "TAB/SELECT stats   F1 controls   F3 debug",
            HorizontalAlignment = HorizontalAlignment.Right,
            LabelSettings = Style(11, new Color(1f, 1f, 1f, 0.95f), 4),
        };
        // Top right: clear of the boss bar, the combo and the player panels.
        _hint.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _hint.Position = new Vector2(-442, 10);
        _hint.Size = new Vector2(430, 60);
        AddChild(_hint);
        // Shown for the first moments of a fight, then out of the way.
        CreateTween().TweenProperty(_hint, "modulate:a", 0f, 1.2f).SetDelay(9.0);
    }

    // ---------------------------------------------------------- updates

    private void OnHit(Fighter? attacker, Fighter victim, float amount)
    {
        if (attacker is null || attacker.Hostile || !victim.Hostile || victim is BossClown) return;
        _targetFighter = victim;
        _targetUntil = _clock + 2.5;
    }

    private void Pop(Control c, float from)
    {
        c.Scale = new Vector2(from, from);
        Tween t = CreateTween();
        t.TweenProperty(c, "scale", Vector2.One, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Process(double delta)
    {
        _clock += delta;

        bool f1 = Input.IsKeyPressed(Key.F1);
        if (f1 && !_f1Down) _hint.Modulate = new Color(1, 1, 1, _hint.Modulate.A > 0.5f ? 0f : 1f);
        _f1Down = f1;

        IReadOnlyList<PlayerFighter> players = _players();
        for (int i = 0; i < _panels.Count; i++)
        {
            (Control root, CandyBar bar, Label name) = _panels[i];
            root.Visible = i < players.Count;
            if (!root.Visible) continue;
            PlayerFighter p = players[i];
            bar.Value = p.Health / p.MaxHealth;
            name.Text = p.IsDead ? $"P{i + 1}  K.O.!" : $"P{i + 1}  {p.DisplayName}";

            (Label level, ColorRect xp, Label points) = _progress[i];
            if (p.Record is CharacterRecord r)
            {
                level.Text = $"LV {r.Level}";
                if (r.Level != _levelShown[i])
                {
                    if (_levelShown[i] > 0) Pop(level, 2.2f);
                    _levelShown[i] = r.Level;
                }
                int need = Leveling.XpToNext(r.Level);
                xp.Size = new Vector2(need > 0 ? 180f * r.Xp / need : 180f, 4f);
                points.Visible = r.StatPoints > 0;
                points.Text = $"+{r.StatPoints} PTS  TAB";
                int carried = BagCount?.Invoke(p) ?? 0;
                _bags[i].Visible = carried > 0;
                _bags[i].Text = $"LOOT {carried}";
                points.Modulate = new Color(1f, 1f, 1f, 0.65f + 0.35f * Mathf.Sin((float)_clock * 6f));
            }
        }

        UpdateTarget();
        UpdateTicketAndBoss();
        UpdateCombo();
        UpdateBanner();
    }

    private void UpdateTarget()
    {
        Fighter? t = _targetFighter;
        bool show = t is not null && GodotObject.IsInstanceValid(t) && _clock < _targetUntil;
        _target.Visible = show;
        if (!show) return;
        _targetName.Text = t!.IsDead ? $"{t.DisplayName}  K.O.!" : t.DisplayName;
        _targetBar.Value = t.Health / t.MaxHealth;
        if (_targetFace.Texture is AtlasTexture at)
            at.Atlas = GD.Load<Texture2D>(t is GruntClown ? GruntSheet : PlayerSheet);
    }

    private void UpdateTicketAndBoss()
    {
        WaveDirector? d = _director();
        _ticket.Visible = d is not null;
        if (d is not null)
        {
            _ticketText.Text = $"WAVE  {d.WaveNumber} / {d.WaveCount}";
            _balloons.Count = d.Lives;
        }

        BossClown? boss = null;
        foreach (Node n in GetTree().GetNodesInGroup(Fighter.Group))
            if (n is BossClown b) { boss = b; break; }
        _bossRoot.Visible = boss is not null;
        if (boss is null) return;
        _bossName.Text = boss.DisplayName;
        _bossBar.Value = boss.Health / boss.MaxHealth;
    }

    private void UpdateCombo()
    {
        bool show = Combo > 1;
        _combo.Visible = _rank.Visible = show;
        if (!show) { _comboShown = 0; return; }

        if (Combo != _comboShown)
        {
            _comboShown = Combo;
            _combo.Text = $"{Combo} HITS";
            Pop(_combo, 1.45f);

            string word = "";
            Color color = new(1f, 0.85f, 0.3f);
            foreach ((int at, string w, Color c) in Ranks)
                if (Combo >= at) { word = w; color = c; }
            if (word != _rank.Text) { _rank.Text = word; Pop(_rank, 1.8f); }
            _combo.LabelSettings.FontColor = color;
        }
    }

    private void UpdateBanner()
    {
        string text = _director()?.Banner ?? "";
        if (text != _bannerShown)
        {
            _bannerShown = text;
            _banner.Text = text;
            if (text.Length > 0) Pop(_banner, 0.2f);
        }
        _subBanner.Text = _canReplay() ? "press ATTACK to play again" : "";
    }
}
