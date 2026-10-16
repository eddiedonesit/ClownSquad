namespace ClownSquad.Net;

using System.Collections.Generic;
using Godot;
using ClownSquad.UI;

/// <summary>
/// A guest's HUD in an online stage: every clown's name, level and health,
/// the boss's bar, and the host's banners (stage clear, game over). The
/// numbers come in each snapshot; the host's own HUD shows the same fight.
/// </summary>
public partial class NetHud : CanvasLayer
{
    private readonly List<(Label name, ProgressBar bar)> _rows = new();
    private VBoxContainer _box = null!;
    private Label _bossName = null!;
    private ProgressBar _bossBar = null!;
    private Label _banner = null!;
    private Label _hint = null!;

    public override void _Ready()
    {
        Layer = 10;
        _box = new VBoxContainer { Position = new Vector2(18, 14) };
        _box.AddThemeConstantOverride("separation", 4);
        AddChild(_box);

        var boss = new VBoxContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f, OffsetLeft = -260, OffsetRight = 260, OffsetTop = -74, OffsetBottom = -14 };
        AddChild(boss);
        _bossName = new Label { HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(22, new Color(1f, 0.85f, 0.3f)) };
        boss.AddChild(_bossName);
        _bossBar = Bar(new Color(0.9f, 0.25f, 0.35f), 520, 18);
        boss.AddChild(_bossBar);
        boss.Visible = false;

        _banner = new Label
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.38f, AnchorBottom = 0.38f, HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(64, new Color(1f, 0.85f, 0.3f), 12), Visible = false,
        };
        AddChild(_banner);
        _hint = new Label
        {
            AnchorLeft = 1f, AnchorRight = 1f, OffsetLeft = -420, OffsetRight = -16, OffsetTop = 12, HorizontalAlignment = HorizontalAlignment.Right,
            Text = "ONLINE: playing in the host's game", LabelSettings = Style(14, new Color(0.85f, 0.82f, 0.95f), 3),
        };
        AddChild(_hint);
    }

    private static LabelSettings Style(int size, Color color, int outline = 5) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline, OutlineColor = new Color(0.08f, 0.04f, 0.1f),
    };

    private static ProgressBar Bar(Color fill, int w, int h)
    {
        var bar = new ProgressBar { CustomMinimumSize = new Vector2(w, h), ShowPercentage = false, MaxValue = 1, Value = 1 };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.1f, 0.05f, 0.14f, 0.85f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
        return bar;
    }

    /// <summary>The HUD numbers from a snapshot: [players [[name, level, hp, max, down]...], boss [name, hp, max] or []].</summary>
    public void Show(Godot.Collections.Array hud)
    {
        if (hud.Count < 2) return;
        Godot.Collections.Array players = hud[0].AsGodotArray();
        while (_rows.Count < players.Count)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var name = new Label { CustomMinimumSize = new Vector2(220, 0), LabelSettings = Style(20, Colors.White) };
            ProgressBar bar = Bar(new Color(0.35f, 0.85f, 0.45f), 200, 16);
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(name);
            row.AddChild(bar);
            _box.AddChild(row);
            _rows.Add((name, bar));
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i < players.Count;
            _rows[i].name.GetParent<Control>().Visible = on;
            if (!on) continue;
            Godot.Collections.Array p = players[i].AsGodotArray();
            bool down = (bool)p[4];
            _rows[i].name.Text = $"{(string)p[0]}  LV {(int)p[1]}{(down ? "  (DOWN)" : "")}";
            _rows[i].bar.Value = (float)p[3] > 0 ? Mathf.Clamp((float)p[2] / (float)p[3], 0f, 1f) : 0;
        }
        Godot.Collections.Array boss = hud[1].AsGodotArray();
        _bossBar.GetParent<Control>().Visible = boss.Count == 3;
        if (boss.Count == 3)
        {
            _bossName.Text = (string)boss[0];
            _bossBar.Value = (float)boss[2] > 0 ? Mathf.Clamp((float)boss[1] / (float)boss[2], 0f, 1f) : 0;
        }
    }

    /// <summary>A big message from the host: STAGE CLEAR!, GAME OVER.</summary>
    public void ShowBanner(string text)
    {
        _banner.Text = text;
        _banner.Visible = text.Length > 0;
    }
}
