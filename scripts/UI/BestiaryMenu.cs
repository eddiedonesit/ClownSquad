namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Map;
using ClownSquad.Progression;

/// <summary>
/// The bestiary: every act in the circus, the ones you have met shown with
/// their portrait, name and how to beat them; the rest a question mark.
/// Bosses also say whether they have been beaten. What counts as met is
/// everything met in any save slot on this machine.
/// </summary>
public partial class BestiaryMenu : CanvasLayer
{
    private readonly List<MapPad> _pads = new();
    private readonly List<Label> _rows = new();
    private readonly HashSet<string> _seen = new();
    private readonly HashSet<int> _beaten = new();
    private int _cursor;
    private TextureRect _face = null!;
    private Label _name = null!, _tip = null!, _status = null!;

    public event Action? Closed;

    private static readonly Dictionary<string, int> BossWorld = new()
    {
        ["bertie"] = 1, ["ballrider"] = 2, ["trainer"] = 3, ["clowncar"] = 4, ["elastic"] = 5,
        ["trapeze"] = 6, ["fireeater"] = 7, ["tots"] = 8, ["strongman"] = 9, ["ringmaster"] = 10,
    };

    public BestiaryMenu(IEnumerable<int> devices)
    {
        foreach (int d in devices) _pads.Add(new MapPad(d));
        Layer = 24;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        for (int s = 1; s <= SaveSystem.Slots; s++)
        {
            if (SaveSystem.Peek(s) is not SaveData d) continue;
            foreach (string k in d.Campaign.Seen) _seen.Add(k);
            for (int w = 1; w <= Campaign.Worlds; w++) if (d.Campaign.Cleared.Contains($"{w}-B")) _beaten.Add(w);
        }
        _seen.Add("grunt");
        foreach (MapPad p in _pads) p.Swallow();

        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.95f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var title = new Label { Text = "BESTIARY", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(40, new Color(1f, 0.85f, 0.3f), 8) };
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 14;
        AddChild(title);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.OffsetTop = 76;
        row.OffsetBottom = -40;
        AddChild(row);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 1);
        row.AddChild(list);
        foreach (string kind in EnemyCatalog.Bestiary)
        {
            var l = new Label { LabelSettings = Style(16, Colors.White, 4) };
            list.AddChild(l);
            _rows.Add(l);
        }

        var card = new PanelContainer { CustomMinimumSize = new Vector2(420, 420), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1f, 0.9f, 0.4f), BorderColor = new Color(0.08f, 0.04f, 0.1f),
            BorderWidthLeft = 5, BorderWidthRight = 5, BorderWidthTop = 5, BorderWidthBottom = 5,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        });
        row.AddChild(card);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        card.AddChild(box);
        _face = new TextureRect
        {
            CustomMinimumSize = new Vector2(220, 220), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        box.AddChild(_face);
        _name = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(30, new Color(0.12f, 0.06f, 0.16f), 0) };
        box.AddChild(_name);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(16, new Color(0.86f, 0.18f, 0.24f), 0) };
        box.AddChild(_status);
        _tip = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0), HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(17, new Color(0.25f, 0.12f, 0.2f), 0),
        };
        box.AddChild(_tip);

        var hint = new Label { Text = "UP/DOWN  BROWSE      BACK  DONE", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(13, new Color(0.75f, 0.7f, 0.85f), 3) };
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hint.OffsetTop = -34;
        AddChild(hint);
        Refresh();
        Sfx.Play("pop", 0.9f);
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
    };

    private void Refresh()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            string kind = EnemyCatalog.Bestiary[i];
            bool met = _seen.Contains(kind);
            bool boss = BossWorld.ContainsKey(kind);
            string name = met ? EnemyCatalog.Info(kind).title : "? ? ?";
            _rows[i].Text = (i == _cursor ? "> " : "  ") + (boss ? "* " : "") + name;
            _rows[i].LabelSettings.FontColor = i == _cursor ? new Color(1f, 0.85f, 0.3f) : met ? Colors.White : new Color(0.5f, 0.45f, 0.6f);
        }
        string k = EnemyCatalog.Bestiary[_cursor];
        bool seen = _seen.Contains(k);
        (string title, _, string tip) = EnemyCatalog.Info(k);
        string sheet = EnemyCatalog.SheetOf(k);
        _face.Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>(sheet), Region = Portraits.FaceOf(sheet) };
        _face.Modulate = seen ? Colors.White : new Color(0f, 0f, 0f, 0.85f);           // a silhouette until met
        _name.Text = seen ? title : "? ? ?";
        _tip.Text = seen ? tip : "Not met yet. Somewhere in the circus...";
        _status.Text = BossWorld.TryGetValue(k, out int w)
            ? (_beaten.Contains(w) ? $"BOSS OF WORLD {w}  -  BEATEN!" : $"BOSS OF WORLD {w}")
            : seen ? "MET" : "";
    }

    public override void _Process(double delta)
    {
        foreach (MapPad pad in _pads)
        {
            switch (pad.Poll())
            {
                case MapPad.Press.Up: _cursor = (_cursor + _rows.Count - 1) % _rows.Count; Sfx.Play("blip", 1.1f); Refresh(); return;
                case MapPad.Press.Down: _cursor = (_cursor + 1) % _rows.Count; Sfx.Play("blip"); Refresh(); return;
                case MapPad.Press.Back:
                case MapPad.Press.Enter:
                    Sfx.Play("clack");
                    Closed?.Invoke();
                    QueueFree();
                    return;
            }
        }
    }
}
