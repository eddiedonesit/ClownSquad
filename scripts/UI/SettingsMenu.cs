namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Map;
using ClownSquad.Progression;

/// <summary>
/// Settings, from the title screen or either pause menu: music and sound
/// volume, screen shake, difficulty and fullscreen. Up/down picks a row,
/// left/right changes it (heard and seen at once), back or BACK closes and
/// saves. Any controller in the squad can drive it.
/// </summary>
public partial class SettingsMenu : CanvasLayer
{
    private enum Row { Music, Sound, Shake, Difficulty, Fullscreen, Back }

    private readonly List<MapPad> _pads = new();
    private readonly List<Label> _labels = new();
    private readonly List<Label> _values = new();
    private Label _note = null!;
    private int _cursor;

    public event Action? Closed;

    public SettingsMenu(IEnumerable<int> devices)
    {
        foreach (int d in devices) _pads.Add(new MapPad(d));
        Layer = 24;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        GameSettings.Load();
        foreach (MapPad p in _pads) p.Swallow();
        var shade = new ColorRect { Color = new Color(0.05f, 0.02f, 0.09f, 0.78f) };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(shade);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(centre);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.12f, 0.3f, 0.97f), BorderColor = new Color(1f, 0.85f, 0.3f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 16, ContentMarginBottom = 18,
        });
        centre.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        box.AddChild(new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center, LabelSettings = Style(40, new Color(1f, 0.85f, 0.3f), 8) });
        foreach (Row _ in Enum.GetValues<Row>())
        {
            var row = new HBoxContainer();
            box.AddChild(row);
            var l = new Label { LabelSettings = Style(24, Colors.White, 6), CustomMinimumSize = new Vector2(250, 0) };
            var v = new Label { LabelSettings = Style(24, Colors.White, 6), HorizontalAlignment = HorizontalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddChild(l);
            row.AddChild(v);
            _labels.Add(l);
            _values.Add(v);
        }
        _note = new Label { LabelSettings = Style(14, new Color(0.8f, 0.78f, 0.9f), 3), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 40) };
        box.AddChild(_note);
        box.AddChild(new Label
        {
            Text = "UP/DOWN  PICK     LEFT/RIGHT  CHANGE     BACK  DONE", HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = Style(13, new Color(0.75f, 0.7f, 0.85f), 3),
        });
        Refresh();
        Sfx.Play("pop", 0.9f);
    }

    private static LabelSettings Style(int size, Color color, int outline) => new()
    {
        Font = UiFont.Display, FontSize = size, FontColor = color, OutlineSize = outline,
        OutlineColor = new Color(0.08f, 0.04f, 0.1f), ShadowSize = 2, ShadowColor = new Color(0, 0, 0, 0.45f), ShadowOffset = new Vector2(2, 3),
    };

    private static string Bar(float v)
    {
        int n = Mathf.RoundToInt(v * 10f);
        return new string('|', n) + new string('.', 10 - n);
    }

    private void Refresh()
    {
        (string name, string value)[] rows =
        {
            ("MUSIC", Bar(GameSettings.Music)),
            ("SOUND", Bar(GameSettings.Sound)),
            ("SCREEN SHAKE", GameSettings.Shake <= 0f ? "OFF" : GameSettings.Shake < 0.9f ? "LOW" : "FULL"),
            ("DIFFICULTY", GameSettings.Difficulty.ToString().ToUpperInvariant()),
            ("FULLSCREEN", GameSettings.Fullscreen ? "ON" : "OFF"),
            ("BACK", ""),
        };
        for (int i = 0; i < _labels.Count; i++)
        {
            bool sel = i == _cursor;
            Color c = sel ? new Color(1f, 0.85f, 0.3f) : Colors.White;
            _labels[i].Text = (sel ? "> " : "  ") + rows[i].name;
            _values[i].Text = rows[i].value.Length == 0 ? "" : $"<  {rows[i].value}  >";
            _labels[i].LabelSettings.FontColor = c;
            _values[i].LabelSettings.FontColor = c;
        }
        _note.Text = (Row)_cursor switch
        {
            Row.Difficulty => GameSettings.Describe(GameSettings.Difficulty) + " Takes effect on the next enemies to arrive.",
            Row.Shake => "How much the camera shakes on big hits.",
            Row.Fullscreen => "Fill the screen, or play in a window.",
            _ => "",
        };
    }

    public override void _Process(double delta)
    {
        foreach (MapPad pad in _pads)
        {
            MapPad.Press p = pad.Poll();
            if (p == MapPad.Press.None) continue;
            Handle(p);
            return;
        }
    }

    private void Handle(MapPad.Press p)
    {
        int rows = _labels.Count;
        switch (p)
        {
            case MapPad.Press.Up: _cursor = (_cursor + rows - 1) % rows; Sfx.Play("blip", 1.1f); break;
            case MapPad.Press.Down: _cursor = (_cursor + 1) % rows; Sfx.Play("blip"); break;
            case MapPad.Press.Left: Change(-1); break;
            case MapPad.Press.Right: Change(1); break;
            case MapPad.Press.Enter:
                if ((Row)_cursor == Row.Back) { Close(); return; }
                Change(1);
                break;
            case MapPad.Press.Back: Close(); return;
        }
        Refresh();
    }

    private void Change(int dir)
    {
        switch ((Row)_cursor)
        {
            case Row.Music:
                GameSettings.Music = Mathf.Clamp(Mathf.Round(GameSettings.Music * 10f + dir) / 10f, 0f, 1f);
                break;
            case Row.Sound:
                GameSettings.Sound = Mathf.Clamp(Mathf.Round(GameSettings.Sound * 10f + dir) / 10f, 0f, 1f);
                break;
            case Row.Shake:
                float[] steps = { 0f, 0.5f, 1f };
                int at = Array.IndexOf(steps, GameSettings.Shake);
                GameSettings.Shake = steps[Mathf.PosMod((at < 0 ? 2 : at) + dir, 3)];
                break;
            case Row.Difficulty:
                GameSettings.Difficulty = (Difficulty)Mathf.PosMod((int)GameSettings.Difficulty + dir, 3);
                break;
            case Row.Fullscreen:
                GameSettings.Fullscreen = !GameSettings.Fullscreen;
                break;
            default:
                return;
        }
        GameSettings.Apply();
        Sfx.Play("clack", 1f + dir * 0.1f);      // heard at the new sound volume
    }

    private void Close()
    {
        GameSettings.Save();
        Sfx.Play("clack");
        Closed?.Invoke();
        QueueFree();
    }
}
