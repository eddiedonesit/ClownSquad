namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Map;
using ClownSquad.Services;

/// <summary>
/// Title > ACHIEVEMENTS: every achievement, earned or not, with how to earn it
/// and when it was earned. Hidden ones stay "???" until earned. The same list
/// unlocks on Steam once the Steam build runs (docs/STEAM.md).
/// </summary>
public partial class AchievementsMenu : CanvasLayer
{
    private const int RowsShown = 13;
    private readonly List<MapPad> _pads = new();
    private readonly List<Label> _rows = new();
    private Label _count = null!, _name = null!, _desc = null!, _when = null!;
    private int _cursor;

    public event Action? Closed;

    public AchievementsMenu(IEnumerable<int> devices)
    {
        foreach (int d in devices) _pads.Add(new MapPad(d));
        Layer = 24;
        ProcessMode = ProcessModeEnum.Always;
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
    };

    public override void _Ready()
    {
        foreach (MapPad p in _pads) p.Swallow();
        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.95f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);
        var title = new Label { Text = "ACHIEVEMENTS", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(40, new Color(1f, 0.85f, 0.3f), 8) };
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = 14;
        AddChild(title);
        _count = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(16, new Color(0.8f, 0.75f, 0.9f), 3) };
        _count.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _count.OffsetTop = 64;
        AddChild(_count);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 28);
        row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        row.OffsetTop = 100;
        row.OffsetBottom = -40;
        AddChild(row);
        var list = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        list.AddThemeConstantOverride("separation", 2);
        row.AddChild(list);
        for (int i = 0; i < RowsShown; i++)
        {
            var l = new Label { LabelSettings = Style(17, Colors.White, 4) };
            list.AddChild(l);
            _rows.Add(l);
        }
        var card = new PanelContainer { CustomMinimumSize = new Vector2(420, 200), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.97f), BorderColor = new Color(1f, 0.85f, 0.3f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14,
        });
        row.AddChild(card);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        card.AddChild(box);
        _name = new Label { LabelSettings = Style(26, new Color(1f, 0.85f, 0.3f), 5), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };
        _desc = new Label { LabelSettings = Style(18, Colors.White, 4), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };
        _when = new Label { LabelSettings = Style(14, new Color(0.6f, 1f, 0.6f), 3) };
        box.AddChild(_name);
        box.AddChild(_desc);
        box.AddChild(_when);
        var hint = new Label { Text = "UP/DOWN  BROWSE      BACK  DONE", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(13, new Color(0.75f, 0.7f, 0.85f), 3) };
        hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        hint.OffsetTop = -34;
        AddChild(hint);
        Refresh();
        Sfx.Play("pop", 0.9f);
    }

    private void Refresh()
    {
        Achievement[] all = Achievements.All;
        int earned = 0;
        foreach (Achievement a in all) if (Platform.Current.IsUnlocked(a.Id)) earned++;
        _count.Text = $"{earned} OF {all.Length} EARNED";
        int first = Mathf.Clamp(_cursor - RowsShown / 2, 0, Mathf.Max(0, all.Length - RowsShown));
        for (int i = 0; i < RowsShown; i++)
        {
            int at = first + i;
            Label l = _rows[i];
            l.Visible = at < all.Length;
            if (!l.Visible) continue;
            Achievement a = all[at];
            bool got = Platform.Current.IsUnlocked(a.Id);
            string name = got || !a.Hidden ? a.Name : "? ? ?";
            l.Text = (at == _cursor ? "> " : "  ") + (got ? "* " : "   ") + name;
            l.LabelSettings.FontColor = at == _cursor ? new Color(1f, 0.85f, 0.3f) : got ? Colors.White : new Color(0.55f, 0.5f, 0.65f);
        }
        Achievement sel = all[_cursor];
        bool have = Platform.Current.IsUnlocked(sel.Id);
        _name.Text = have || !sel.Hidden ? sel.Name : "? ? ?";
        _desc.Text = have || !sel.Hidden ? sel.Description : "A secret. Keep playing.";
        _when.Text = have ? (Platform.Local is OfflinePlatform off && off.UnlockedAt(sel.Id) is string at2 ? $"EARNED {at2.Replace('T', ' ')}" : "EARNED") : "NOT YET";
        _when.LabelSettings.FontColor = have ? new Color(0.6f, 1f, 0.6f) : new Color(0.75f, 0.7f, 0.85f);
    }

    public override void _Process(double delta)
    {
        int n = Achievements.All.Length;
        foreach (MapPad pad in _pads)
        {
            switch (pad.Poll())
            {
                case MapPad.Press.Up: _cursor = (_cursor + n - 1) % n; Sfx.Play("blip", 1.1f); Refresh(); return;
                case MapPad.Press.Down: _cursor = (_cursor + 1) % n; Sfx.Play("blip"); Refresh(); return;
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
