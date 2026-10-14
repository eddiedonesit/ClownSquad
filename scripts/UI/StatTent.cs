namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Input;
using ClownSquad.Progression;

/// <summary>
/// The Stat Tent: where level-up points are spent. One card per player, each
/// driven by that player's own controller, so a co-op group spends side by
/// side, Guardian Heroes style. The game is paused while it is open.
///
/// Points are applied as they are spent, so the card always shows the real
/// numbers. Points spent this visit can be taken back until the card is
/// closed; after that they are committed, and the save is written.
///
/// Controls on each card: up/down to pick a stat, ATTACK (or right) to add a
/// point, DODGE (or left) to take one back, JUMP to finish.
/// </summary>
public partial class StatTent : CanvasLayer
{
    private static readonly Stat[] Stats = { Stat.Pow, Stat.Guts, Stat.Zip, Stat.Luck };
    private static readonly Color Gold = new(1f, 0.85f, 0.3f);
    private static readonly Color Cream = new(1f, 0.97f, 0.9f);
    private static readonly Color Dim = new(0.75f, 0.7f, 0.85f);

    private readonly Func<IReadOnlyList<PlayerFighter>> _players;
    private readonly List<Card> _cards = new();
    private Font _font = null!;
    private Control _root = null!;
    private HBoxContainer _row = null!;
    private ulong _closedAtMs;

    public bool IsOpen { get; private set; }

    /// <summary>Wall-clock milliseconds since the tent last closed, for input guards.</summary>
    public ulong MsSinceClosed => Time.GetTicksMsec() - _closedAtMs;

    public event Action? Closed;

    public StatTent(Func<IReadOnlyList<PlayerFighter>> players)
    {
        _players = players;
        Layer = 20;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        _font = new SystemFont { FontNames = new[] { "Comic Sans MS", "Segoe UI Black", "Arial Black" }, FontWeight = 800 };

        _root = new Control { Visible = false };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var shade = new ColorRect { Color = new Color(0.06f, 0.02f, 0.1f, 0.72f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(shade);

        var title = new Label
        {
            Text = "THE STAT TENT",
            HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(46, Gold, 10),
        };
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 34;
        _root.AddChild(title);

        _row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 18);
        _row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _row.OffsetTop = 120;
        _row.OffsetBottom = -40;
        _root.AddChild(_row);
    }

    // ------------------------------------------------------------ open / close

    /// <summary>Open for every player driven by a local device. Returns false if nobody can use it.</summary>
    public bool Open()
    {
        if (IsOpen) return true;
        foreach (Card c in _cards) c.Panel.QueueFree();
        _cards.Clear();

        foreach (PlayerFighter p in _players())
        {
            if (p.Record is null || p.Source is not LocalDeviceInput device) continue;
            var card = new Card(p, p.Record, new MenuPad(device.Device));
            BuildCard(card);
            _cards.Add(card);
        }
        if (_cards.Count == 0) return false;

        IsOpen = true;
        _root.Visible = true;
        GetTree().Paused = true;
        Sfx.Play("boing_small", 0.8f);
        foreach (Card c in _cards) Refresh(c);
        return true;
    }

    private void Close()
    {
        IsOpen = false;
        _root.Visible = false;
        GetTree().Paused = false;
        _closedAtMs = Time.GetTicksMsec();
        SaveSystem.Save();
        Sfx.Play("ding");
        Closed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (!IsOpen) return;

        bool allDone = true;
        foreach (Card c in _cards)
        {
            if (!c.Done) Drive(c);
            allDone &= c.Done;
        }
        if (allDone) Close();
    }

    // -------------------------------------------------------------- one card

    private void Drive(Card c)
    {
        MenuPad.Press press = c.Pad.Poll();
        if (press == MenuPad.Press.None) return;

        Stat stat = Stats[c.Cursor];
        switch (press)
        {
            case MenuPad.Press.Up:
                c.Cursor = (c.Cursor + Stats.Length - 1) % Stats.Length;
                Sfx.Play("blip", 1.1f);
                break;
            case MenuPad.Press.Down:
                c.Cursor = (c.Cursor + 1) % Stats.Length;
                Sfx.Play("blip");
                break;
            case MenuPad.Press.Add:
                if (c.Record.StatPoints <= 0) { Sfx.Play("squeak", 0.7f); break; }
                c.Record.StatPoints--;
                c.Record.Set(stat, c.Record.Get(stat) + 1);
                c.Added[(int)stat]++;
                c.Player.ApplyProgression(c.Record);
                Sfx.Play("coin", 1f + 0.04f * c.Added[(int)stat]);
                Bounce(c.Rows[c.Cursor].value);
                break;
            case MenuPad.Press.Undo:
                if (c.Added[(int)stat] <= 0) { Sfx.Play("squeak", 0.7f); break; }
                c.Added[(int)stat]--;
                c.Record.Set(stat, c.Record.Get(stat) - 1);
                c.Record.StatPoints++;
                c.Player.ApplyProgression(c.Record);
                Sfx.Play("pop");
                break;
            case MenuPad.Press.Done:
                c.Done = true;
                Sfx.Play("clack");
                break;
        }
        Refresh(c);
    }

    private void Refresh(Card c)
    {
        CharacterRecord r = c.Record;
        c.Title.Text = r.Name;
        c.Level.Text = $"LV {r.Level}";
        int need = Leveling.XpToNext(r.Level);
        c.Xp.Text = need > 0 ? $"XP {r.Xp} / {need}" : "MAX LEVEL";
        c.Points.Text = r.StatPoints > 0 ? $"{r.StatPoints} POINT{(r.StatPoints == 1 ? "" : "S")} TO SPEND" : "NO POINTS LEFT";
        c.Points.LabelSettings.FontColor = r.StatPoints > 0 ? Gold : Dim;

        for (int i = 0; i < Stats.Length; i++)
        {
            (string name, string blurb, string effect) = Leveling.Describe(Stats[i], r);
            (Label nameL, Label value, Label detail, Panel bg) = c.Rows[i];
            bool sel = i == c.Cursor && !c.Done;
            nameL.Text = (sel ? "> " : "  ") + name;
            nameL.LabelSettings.FontColor = sel ? Gold : Cream;
            int added = c.Added[(int)Stats[i]];
            value.Text = added > 0 ? $"{r.Get(Stats[i])} (+{added})" : r.Get(Stats[i]).ToString();
            detail.Text = $"{blurb}  -  {effect}";
            bg.Visible = sel;
        }

        c.Footer.Text = c.Done ? "READY!" : "ATTACK +1    DODGE undo    JUMP done";
        c.Footer.LabelSettings.FontColor = c.Done ? new Color(0.55f, 1f, 0.6f) : Dim;
    }

    private void BuildCard(Card c)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(330, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        panel.AddThemeStyleboxOverride("panel", Plate(new Color(0.2f, 0.12f, 0.3f, 0.97f), Gold));
        _row.AddChild(panel);
        c.Panel = panel;

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);

        var head = new HBoxContainer();
        box.AddChild(head);
        c.Title = new Label { LabelSettings = Style(26, Gold, 6), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        c.Level = new Label { LabelSettings = Style(22, Cream, 5) };
        head.AddChild(c.Title);
        head.AddChild(c.Level);

        c.Xp = new Label { LabelSettings = Style(14, Dim, 4) };
        box.AddChild(c.Xp);
        c.Points = new Label { LabelSettings = Style(18, Gold, 5) };
        box.AddChild(c.Points);

        for (int i = 0; i < Stats.Length; i++)
        {
            var row = new Control { CustomMinimumSize = new Vector2(300, 52) };
            box.AddChild(row);
            var bg = new Panel { Size = new Vector2(306, 52), Position = new Vector2(-3, 0), Visible = false };
            bg.AddThemeStyleboxOverride("panel", Plate(new Color(0.86f, 0.2f, 0.25f, 0.55f), new Color(1f, 0.85f, 0.3f, 0.9f), 8, 2));
            row.AddChild(bg);
            var name = new Label { Position = new Vector2(4, 2), LabelSettings = Style(20, Cream, 5) };
            var value = new Label
            {
                Position = new Vector2(170, 2), Size = new Vector2(126, 28),
                HorizontalAlignment = HorizontalAlignment.Right,
                LabelSettings = Style(20, Cream, 5),
                PivotOffset = new Vector2(100, 14),
            };
            var detail = new Label { Position = new Vector2(22, 28), LabelSettings = Style(13, Dim, 3) };
            row.AddChild(name);
            row.AddChild(value);
            row.AddChild(detail);
            c.Rows[i] = (name, value, detail, bg);
        }

        c.Footer = new Label { LabelSettings = Style(14, Dim, 4), HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(c.Footer);
    }

    private void Bounce(Control c)
    {
        c.Scale = new Vector2(1.5f, 1.5f);
        CreateTween().TweenProperty(c, "scale", Vector2.One, 0.25f)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private LabelSettings Style(int size, Color color, int outline) => new()
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

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 14, int width = 4) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 12, ContentMarginBottom = 12,
    };

    private sealed class Card
    {
        public readonly PlayerFighter Player;
        public readonly CharacterRecord Record;
        public readonly MenuPad Pad;
        public readonly int[] Added = new int[4];
        public readonly (Label name, Label value, Label detail, Panel bg)[] Rows = new (Label, Label, Label, Panel)[4];
        public int Cursor;
        public bool Done;
        public PanelContainer Panel = null!;
        public Label Title = null!, Level = null!, Xp = null!, Points = null!, Footer = null!;

        public Card(PlayerFighter player, CharacterRecord record, MenuPad pad)
        {
            Player = player;
            Record = record;
            Pad = pad;
        }
    }
}

/// <summary>
/// Menu presses from one device, edge-detected. Whatever is held when the
/// menu opens is ignored until released, so the button that opened a menu
/// never also acts inside it.
/// </summary>
public sealed class MenuPad
{
    public enum Press { None, Up, Down, Add, Undo, Done }

    private readonly int _device;
    private readonly bool[] _held = new bool[6];

    public MenuPad(int device)
    {
        _device = device;
        Read(_held);
    }

    /// <summary>Whether the device's "open the stat menu" button is down: Tab, or Back/Select.</summary>
    public static bool OpenHeld(int device) => device == LocalDeviceInput.Keyboard
        ? Godot.Input.IsKeyPressed(Key.Tab)
        : Godot.Input.IsJoyButtonPressed(device, JoyButton.Back);

    public Press Poll()
    {
        var now = new bool[6];
        Read(now);
        Press result = Press.None;
        for (int i = 1; i < now.Length; i++)
            if (now[i] && !_held[i] && result == Press.None) result = (Press)i;
        Array.Copy(now, _held, now.Length);
        return result;
    }

    private void Read(bool[] into)
    {
        if (_device == LocalDeviceInput.Keyboard)
        {
            into[(int)Press.Up] = K(Key.W) || K(Key.Up);
            into[(int)Press.Down] = K(Key.S) || K(Key.Down);
            into[(int)Press.Add] = K(Key.J) || K(Key.D) || K(Key.Right);
            into[(int)Press.Undo] = K(Key.K) || K(Key.A) || K(Key.Left);
            into[(int)Press.Done] = K(Key.Space) || K(Key.Tab) || K(Key.Enter) || K(Key.Escape);
            return;
        }
        float y = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftY);
        float x = Godot.Input.GetJoyAxis(_device, JoyAxis.LeftX);
        into[(int)Press.Up] = y < -0.6f || B(JoyButton.DpadUp);
        into[(int)Press.Down] = y > 0.6f || B(JoyButton.DpadDown);
        into[(int)Press.Add] = B(JoyButton.X) || B(JoyButton.DpadRight) || x > 0.75f;
        into[(int)Press.Undo] = B(JoyButton.B) || B(JoyButton.DpadLeft) || x < -0.75f;
        into[(int)Press.Done] = B(JoyButton.A) || B(JoyButton.Back) || B(JoyButton.Start);
    }

    private static bool K(Key k) => Godot.Input.IsKeyPressed(k);
    private bool B(JoyButton b) => Godot.Input.IsJoyButtonPressed(_device, b);
}
