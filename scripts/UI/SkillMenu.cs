namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Progression;

/// <summary>
/// The skill trees (GDD 5): one card per player, showing the tree of the weapon
/// in hand as three columns of three. Skill points are spent here, anywhere.
/// Changing weapon, and taking points back out of a tree, is only allowed
/// between stages: the menu opened from the world map allows it, the one
/// opened mid-fight does not.
///
/// Controls: up/down browse, ATTACK learn, DODGE unlearn (map), Q/E or a
/// shoulder button change weapon (map), JUMP done.
/// </summary>
public partial class SkillMenu : CanvasLayer
{
    private static readonly Color Gold = new(1f, 0.85f, 0.3f);
    private static readonly Color Cream = new(1f, 0.97f, 0.9f);
    private static readonly Color Dim = new(0.75f, 0.7f, 0.85f);
    private static readonly Color Ink = new(0.08f, 0.04f, 0.1f);

    private readonly List<Seat> _seats;
    private readonly bool _betweenStages;
    private readonly List<Card> _cards = new();
    private HBoxContainer _row = null!;

    public event Action? Closed;

    /// <param name="betweenStages">True on the world map: weapons can be swapped and skills unlearned.</param>
    public SkillMenu(IEnumerable<Seat> seats, bool betweenStages)
    {
        _seats = new List<Seat>(seats);
        _betweenStages = betweenStages;
        Layer = 21;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        GetTree().Paused = true;
        var shade = new ColorRect { Color = new Color(0.06f, 0.02f, 0.1f, 0.85f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var title = new Label { Text = "SKILL TREES", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(40, Gold, 10) };
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 12;
        AddChild(title);
        if (!_betweenStages)
        {
            var note = new Label { Text = "WEAPONS CAN ONLY BE CHANGED ON THE MAP, BETWEEN STAGES", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(13, Dim, 3) };
            note.SetAnchorsPreset(Control.LayoutPreset.TopWide);
            note.OffsetTop = 58;
            AddChild(note);
        }
        _row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _row.AddThemeConstantOverride("separation", 14);
        _row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _row.OffsetTop = 82;
        AddChild(_row);
        foreach (Seat s in _seats)
        {
            var c = new Card(s, new MenuPad(s.Device));
            Build(c);
            _cards.Add(c);
            Refresh(c);
        }
        Sfx.Play("boing_small", 0.9f);
    }

    public override void _Process(double delta)
    {
        bool allDone = _cards.Count > 0;
        foreach (Card c in _cards)
        {
            if (!c.Done) Drive(c);
            allDone &= c.Done;
        }
        if (!allDone) return;
        GetTree().Paused = false;
        SaveSystem.Save();
        Closed?.Invoke();
        QueueFree();
    }

    // Column by column, top to bottom: the order up/down walks through.
    private static Skill NodeAt(CharacterRecord r, int i)
    {
        Skill[] tree = Weapons.Get(r.Weapon).Tree;
        int col = i / 3, tier = i % 3 + 1;
        return Array.Find(tree, s => s.Column == col && s.Tier == tier)!;
    }

    private void Drive(Card c)
    {
        MenuPad.Press press = c.Pad.Poll();
        if (press == MenuPad.Press.None) return;
        CharacterRecord r = c.Seat.Record;
        Skill sel = NodeAt(r, c.Cursor);
        c.Message = "";
        switch (press)
        {
            case MenuPad.Press.Up: c.Cursor = (c.Cursor + 8) % 9; Sfx.Play("blip", 1.1f); break;
            case MenuPad.Press.Down: c.Cursor = (c.Cursor + 1) % 9; Sfx.Play("blip"); break;
            case MenuPad.Press.Add:
                if (Weapons.Learn(r, sel))
                {
                    c.Seat.Changed?.Invoke();
                    c.Message = $"LEARNED {sel.Name}!";
                    Sfx.Play("level_up", 1.3f, -6f);
                }
                else
                {
                    Weapons.CanLearn(r, sel, out string why);
                    c.Message = why;
                    Sfx.Play("honk_low", 1.3f, -8f);
                }
                break;
            case MenuPad.Press.Undo:
                if (!_betweenStages) { c.Message = "UNLEARN ON THE MAP"; Sfx.Play("honk_low", 1.3f, -8f); break; }
                if (Weapons.Unlearn(r, sel)) { c.Seat.Changed?.Invoke(); c.Message = $"{sel.Name}: POINTS BACK"; Sfx.Play("clack", 0.9f); }
                else { c.Message = "UNLEARN THE ONE BELOW FIRST"; Sfx.Play("honk_low", 1.3f, -8f); }
                break;
            case MenuPad.Press.Page:
                if (!_betweenStages) { c.Message = "CHANGE WEAPONS ON THE MAP"; Sfx.Play("honk_low", 1.3f, -8f); break; }
                r.Weapon = (WeaponKind)(((int)r.Weapon + 1) % Weapons.All.Length);
                c.Seat.Changed?.Invoke();
                c.Message = $"NOW HOLDING {Weapons.Get(r.Weapon).Name}";
                Sfx.Play(r.Weapon == WeaponKind.Chicken ? "squawk" : "clack", 1f);
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
        CharacterRecord r = c.Seat.Record;
        WeaponDef w = Weapons.Get(r.Weapon);
        List<string> known = Weapons.Learned(r, r.Weapon);
        c.Title.Text = r.Name;
        c.Points.Text = $"{r.SkillPoints} SP";
        c.Weapon.Text = _betweenStages ? $"<  {w.Name}  >" : w.Name;
        c.WeaponBlurb.Text = w.Blurb;
        string sheet = Roster.Sheet(r.Look, r.Weapon);
        c.Face.Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>(sheet), Region = WeaponFrame(sheet) };
        for (int col = 0; col < 3; col++) c.ColumnTitles[col].Text = w.Columns[col];

        for (int i = 0; i < 9; i++)
        {
            Skill s = NodeAt(r, i);
            bool have = known.Contains(s.Id);
            bool can = Weapons.CanLearn(r, s, out _);
            bool sel = i == c.Cursor && !c.Done;
            Color bg = have ? new Color(1f, 0.82f, 0.3f) : can ? new Color(0.45f, 0.3f, 0.65f) : new Color(0.22f, 0.16f, 0.3f);
            c.Nodes[i].AddThemeStyleboxOverride("panel", Plate(bg, sel ? new Color(0.95f, 0.22f, 0.28f) : Ink, 8, sel ? 4 : 2, 6));
            c.NodeLabels[i].Text = $"{s.Name}\n{(have ? "LEARNED" : $"{s.Cost} SP")}";
            c.NodeLabels[i].LabelSettings.FontColor = have ? Ink : can ? Cream : Dim;
            c.NodeLabels[i].LabelSettings.OutlineSize = have ? 0 : 3;
            c.NodeLabels[i].LabelSettings.ShadowColor = new Color(0, 0, 0, have ? 0f : 0.45f);
        }
        Skill cur = NodeAt(r, c.Cursor);
        c.Details.Text = $"{cur.Name}\n{cur.Blurb}";
        string keys = _betweenStages ? "ATTACK learn   DODGE unlearn   Q/E weapon   JUMP done" : "ATTACK learn   JUMP done";
        c.Footer.Text = c.Done ? "DONE!" : c.Message.Length > 0 ? c.Message : keys;
    }

    private static readonly Dictionary<string, Rect2> Frames = new();

    /// <summary>The wind-up frame, weapon raised, cropped to the figure: a portrait that shows what is in hand.</summary>
    private static Rect2 WeaponFrame(string sheet)
    {
        if (Frames.TryGetValue(sheet, out Rect2 cached)) return cached;
        SpriteFrames frames = ClownSquad.Visuals.FrameLibrary.Load(sheet, ClownSquad.Visuals.ArtStyle.FramesFor(sheet));
        if (!frames.HasAnimation("attack") || frames.GetFrameTexture("attack", 1) is not AtlasTexture a) return Portraits.FaceOf(sheet);
        Image img = GD.Load<Texture2D>(sheet).GetImage();
        if (img.IsCompressed()) img.Decompress();
        Rect2I f = (Rect2I)a.Region;
        int x0 = f.Size.X, y0 = f.Size.Y, x1 = 0, y1 = 0;
        for (int y = 0; y < f.Size.Y; y += 2)
            for (int x = 0; x < f.Size.X; x += 2)
                if (img.GetPixel(f.Position.X + x, f.Position.Y + y).A > 0.5f) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        Rect2 r = x1 > x0 ? new Rect2(f.Position.X + x0 - 2, f.Position.Y + y0 - 2, x1 - x0 + 5, y1 - y0 + 5) : a.Region;
        Frames[sheet] = r;
        return r;
    }

    private void Build(Card c)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(400, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        panel.AddThemeStyleboxOverride("panel", Plate(new Color(0.2f, 0.12f, 0.3f, 0.97f), Gold));
        _row.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        panel.AddChild(box);

        var head = new HBoxContainer();
        box.AddChild(head);
        c.Title = new Label { LabelSettings = Style(24, Gold, 6), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        c.Points = new Label { LabelSettings = Style(20, Gold, 5) };
        head.AddChild(c.Title);
        head.AddChild(c.Points);

        var weaponRow = new HBoxContainer();
        weaponRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(weaponRow);
        c.Face = new TextureRect
        {
            CustomMinimumSize = new Vector2(96, 110), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        weaponRow.AddChild(c.Face);
        var weaponText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        weaponRow.AddChild(weaponText);
        c.Weapon = new Label { LabelSettings = Style(20, Cream, 5) };
        c.WeaponBlurb = new Label { LabelSettings = Style(12, Dim, 3), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(270, 0) };
        weaponText.AddChild(c.Weapon);
        weaponText.AddChild(c.WeaponBlurb);

        var grid = new HBoxContainer();
        grid.AddThemeConstantOverride("separation", 6);
        box.AddChild(grid);
        var columns = new VBoxContainer[3];
        for (int col = 0; col < 3; col++)
        {
            columns[col] = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            columns[col].AddThemeConstantOverride("separation", 5);
            grid.AddChild(columns[col]);
            var t = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(13, Gold, 3) };
            columns[col].AddChild(t);
            c.ColumnTitles.Add(t);
        }
        for (int i = 0; i < 9; i++)
        {
            var node = new PanelContainer { CustomMinimumSize = new Vector2(120, 54) };
            columns[i / 3].AddChild(node);
            var l = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, LabelSettings = Style(11, Cream, 3),
            };
            node.AddChild(l);
            c.Nodes.Add(node);
            c.NodeLabels.Add(l);
        }

        c.Details = new Label
        {
            LabelSettings = Style(13, Cream, 3), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(370, 54),
        };
        box.AddChild(c.Details);
        c.Footer = new Label { LabelSettings = Style(12, Dim, 4), HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(c.Footer);
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = Ink, ShadowSize = 2, ShadowColor = new Color(0, 0, 0, 0.45f), ShadowOffset = new Vector2(2, 3),
    };

    private static StyleBoxFlat Plate(Color bg, Color border, int radius = 14, int width = 4, int margin = 14) => new()
    {
        BgColor = bg, BorderColor = border,
        BorderWidthLeft = width, BorderWidthRight = width, BorderWidthTop = width, BorderWidthBottom = width,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin * 0.7f, ContentMarginBottom = margin * 0.8f,
    };

    private sealed class Card
    {
        public readonly Seat Seat;
        public readonly MenuPad Pad;
        public readonly List<Label> ColumnTitles = new(), NodeLabels = new();
        public readonly List<PanelContainer> Nodes = new();
        public int Cursor;
        public bool Done;
        public string Message = "";
        public Label Title = null!, Points = null!, Weapon = null!, WeaponBlurb = null!, Details = null!, Footer = null!;
        public TextureRect Face = null!;

        public Card(Seat seat, MenuPad pad)
        {
            Seat = seat;
            Pad = pad;
        }
    }
}
