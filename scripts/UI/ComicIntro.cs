namespace ClownSquad.UI;

using System;
using System.Collections.Generic;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Input;
using ClownSquad.Map;
using ClownSquad.Progression;

/// <summary>
/// The opening, comic-book style. Four slanted panels slam in one after
/// another, a clown close-up in each (tools/gen_portrait.py), split by zigzag
/// ink lines. Then a flash, and the CLOWN SQUAD logo drops onto a starburst
/// as the title song kicks in. The panels fly away and the logo settles where
/// the title screen keeps it. Any button skips.
/// </summary>
public partial class ComicIntro : CanvasLayer
{
    private const float FirstPanel = 0.25f;
    private const float PanelGap = 0.42f;
    private const float Zig = 15f;           // how far a divider's teeth stick out
    private const float Tooth = 46f;         // height of one tooth
    private const float Slant = 70f;         // the dividers lean: top this far right of bottom

    private readonly Func<Rect2> _logoTarget;
    private readonly List<MapPad> _pads = new();
    private Node2D _root = null!;
    private Label _logo = null!;
    private Polygon2D _burst = null!;
    private ColorRect _flash = null!;
    private readonly List<Node2D> _panels = new();
    private readonly List<Line2D> _dividers = new();
    private Tween? _show;
    private float _shake;
    private bool _done;
    private Vector2 _size;

    /// <summary>Raised once when the intro has finished or been skipped.</summary>
    public event Action? Finished;
    /// <summary>Raised when the logo lands: the cue for the title song.</summary>
    public event Action? LogoLanded;

    /// <param name="logoTarget">Where the title screen's own logo sits, in screen space.</param>
    public ComicIntro(Func<Rect2> logoTarget)
    {
        _logoTarget = logoTarget;
        Layer = 30;
    }

    public override void _Ready()
    {
        _size = GetViewport().GetVisibleRect().Size;
        _pads.Add(new MapPad(LocalDeviceInput.Keyboard));
        foreach (int pad in Godot.Input.GetConnectedJoypads()) _pads.Add(new MapPad(pad));

        var backdrop = new ColorRect { Color = new Color(0.07f, 0.04f, 0.1f) };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(backdrop);
        _root = new Node2D();
        AddChild(_root);

        for (int i = 0; i < Roster.All.Length; i++) _panels.Add(BuildPanel(i));
        for (int k = 1; k < Roster.All.Length; k++) _dividers.Add(BuildDivider(k));
        BuildLogo();
        _flash = new ColorRect { Color = new Color(1f, 1f, 1f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_flash);
        Play();
    }

    // ---------------------------------------------------------------- build

    private float Quarter => _size.X / Roster.All.Length;

    /// <summary>The zigzag line between panel k-1 and panel k, top to bottom.</summary>
    private List<Vector2> DividerPath(int k)
    {
        var pts = new List<Vector2>();
        float midX = Quarter * k;
        int teeth = Mathf.CeilToInt((_size.Y + 80f) / Tooth);
        for (int t = 0; t <= teeth * 2; t++)
        {
            float y = -40f + t * Tooth / 2f;
            float lean = Mathf.Lerp(Slant / 2f, -Slant / 2f, y / _size.Y);
            pts.Add(new Vector2(midX + lean + (t % 2 == 0 ? -Zig : Zig), y));
        }
        return pts;
    }

    private List<Vector2> EdgePath(int k)
    {
        if (k > 0 && k < Roster.All.Length) return DividerPath(k);
        float x = k == 0 ? -60f : _size.X + 60f;
        return new List<Vector2> { new(x, -40f), new(x, _size.Y + 40f) };
    }

    private Node2D BuildPanel(int i)
    {
        Roster.Clown clown = Roster.All[i];
        var holder = new Node2D { Visible = false };
        _root.AddChild(holder);

        var shape = new List<Vector2>(EdgePath(i));
        List<Vector2> right = EdgePath(i + 1);
        right.Reverse();
        shape.AddRange(right);
        var poly = new Polygon2D
        {
            Polygon = shape.ToArray(),
            ClipChildren = CanvasItem.ClipChildrenMode.AndDraw,
            Material = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://assets/shaders/comic_panel.gdshader"),
            },
        };
        float cx = Quarter * (i + 0.5f);
        var mat = (ShaderMaterial)poly.Material;
        mat.SetShaderParameter("base", clown.Color.Darkened(0.12f));
        mat.SetShaderParameter("ink", clown.Color.Darkened(0.65f));
        mat.SetShaderParameter("centre", new Vector2(cx, _size.Y * 0.42f));
        mat.SetShaderParameter("reach", _size.Y * 0.9f);
        holder.AddChild(poly);

        // The close-up: big enough that the face fills the panel, the rest cropped.
        var tex = GD.Load<Texture2D>($"res://assets/sprites/portrait_{clown.Id}.png");
        float scale = _size.Y * 0.9f / tex.GetHeight();
        bool flip = i % 2 == 1;
        var face = new Sprite2D
        {
            Texture = tex, Centered = false, FlipH = flip, Scale = Vector2.One * scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        float faceX = (flip ? tex.GetWidth() - 122f : 122f) * scale;  // the head's centre in the art
        face.Position = new Vector2(cx - faceX, _size.Y - tex.GetHeight() * scale + 8f);
        face.SetMeta("rest", face.Position);
        poly.AddChild(face);

        // The caption box, comic style: a yellow plate, slightly askew.
        var caption = new PanelContainer { RotationDegrees = i % 2 == 0 ? -4f : 3f };
        caption.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1f, 0.9f, 0.35f), BorderColor = new Color(0.08f, 0.04f, 0.1f),
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 2, ContentMarginBottom = 2,
            ShadowColor = new Color(0f, 0f, 0f, 0.45f), ShadowSize = 0, ShadowOffset = new Vector2(5f, 6f),
        });
        caption.AddChild(new Label
        {
            Text = clown.Name + "!",
            LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = Mathf.RoundToInt(_size.Y * 0.06f), FontColor = new Color(0.12f, 0.06f, 0.16f) },
        });
        holder.AddChild(caption);
        caption.Position = new Vector2(cx - Quarter * 0.36f, i % 2 == 0 ? _size.Y * 0.07f : _size.Y * 0.8f);
        return holder;
    }

    private Line2D BuildDivider(int k)
    {
        Vector2[] pts = DividerPath(k).ToArray();
        var under = new Line2D { Points = pts, Width = 16f, DefaultColor = new Color(0.07f, 0.04f, 0.1f), JointMode = Line2D.LineJointMode.Sharp };
        var over = new Line2D { Points = pts, Width = 7f, DefaultColor = new Color(1f, 0.97f, 0.9f), JointMode = Line2D.LineJointMode.Sharp };
        under.AddChild(over);
        under.Modulate = new Color(1f, 1f, 1f, 0f);
        _root.AddChild(under);
        return under;
    }

    private void BuildLogo()
    {
        // A jagged starburst behind the logo: red with a yellow heart.
        var star = new List<Vector2>();
        var inner = new List<Vector2>();
        int points = 18;
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (int p = 0; p < points * 2; p++)
        {
            float a = Mathf.Tau * p / (points * 2);
            float r = p % 2 == 0 ? rng.RandfRange(1.0f, 1.25f) : 0.62f;
            star.Add(new Vector2(Mathf.Cos(a) * 1.7f, Mathf.Sin(a)) * r * _size.Y * 0.3f);
            inner.Add(new Vector2(Mathf.Cos(a) * 1.7f, Mathf.Sin(a)) * r * _size.Y * 0.24f);
        }
        _burst = new Polygon2D { Polygon = star.ToArray(), Color = new Color(0.07f, 0.04f, 0.1f), Position = _size * 0.5f, Scale = Vector2.Zero };
        _burst.AddChild(new Polygon2D { Polygon = star.ToArray(), Color = new Color(0.9f, 0.18f, 0.24f), Scale = Vector2.One * 0.95f });
        _burst.AddChild(new Polygon2D { Polygon = inner.ToArray(), Color = new Color(1f, 0.85f, 0.3f), Scale = Vector2.One * 0.95f });
        AddChild(_burst);

        Rect2 target = _logoTarget();
        _logo = new Label
        {
            Text = "CLOWN SQUAD", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Size = target.Size, PivotOffset = target.Size / 2f,
            LabelSettings = new LabelSettings
            {
                Font = UiFont.Display, FontSize = 112, FontColor = new Color(1f, 0.85f, 0.3f), OutlineSize = 18,
                OutlineColor = new Color(0.08f, 0.04f, 0.1f), ShadowSize = 3, ShadowColor = new Color(0f, 0f, 0f, 0.5f), ShadowOffset = new Vector2(3f, 4f),
            },
        };
        _logo.Position = new Vector2(_size.X / 2f - target.Size.X / 2f, -target.Size.Y * 2f);
        _logo.Scale = Vector2.One * 1.35f;
        AddChild(_logo);
    }

    // ------------------------------------------------------------- the show

    private void Play()
    {
        _show = CreateTween();
        for (int i = 0; i < _panels.Count; i++)
        {
            int n = i;
            float at = FirstPanel + i * PanelGap;
            float from = (i % 2 == 0 ? -1f : 1f) * _size.Y * 1.1f;
            Node2D p = _panels[i];
            _show.Parallel().TweenCallback(Callable.From(() =>
            {
                p.Position = new Vector2(0f, from);
                p.Visible = true;
                Sfx.Play("whoosh", 1.1f + n * 0.08f, -2f);
            })).SetDelay(at);
            _show.Parallel().TweenProperty(p, "position", Vector2.Zero, 0.32f).From(new Vector2(0f, from))
                 .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out).SetDelay(at + 0.01f);
            _show.Parallel().TweenCallback(Callable.From(() => { Sfx.Play("slap", 0.9f + n * 0.07f); _shake = 9f; })).SetDelay(at + 0.2f);
            // The face drifts in its frame: a slow push, so a held panel never sits dead.
            var face = (Sprite2D)p.GetChild(0).GetChild(0);
            Vector2 rest = (Vector2)face.GetMeta("rest");
            _show.Parallel().TweenProperty(face, "position", rest, 1.6f).From(rest + new Vector2(i % 2 == 0 ? -36f : 36f, 14f))
                 .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out).SetDelay(at);
            if (i > 0)
                _show.Parallel().TweenProperty(_dividers[i - 1], "modulate:a", 1f, 0.08f).SetDelay(at + 0.18f);
        }

        float drop = FirstPanel + _panels.Count * PanelGap + 0.25f;
        _show.Parallel().TweenCallback(Callable.From(() =>
        {
            _flash.Color = new Color(1f, 1f, 1f, 0.85f);
            Sfx.Play("kaboom", 1.05f);
            Sfx.Play("crowd_cheer", 1f, -4f);
            _shake = 16f;
            LogoLanded?.Invoke();
        })).SetDelay(drop + 0.22f);
        _show.Parallel().TweenProperty(_flash, "color:a", 0f, 0.45f).SetDelay(drop + 0.24f);
        _show.Parallel().TweenProperty(_burst, "scale", Vector2.One, 0.4f).From(Vector2.Zero)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out).SetDelay(drop + 0.2f);
        Vector2 centre = new(_size.X / 2f - _logo.Size.X / 2f, _size.Y / 2f - _logo.Size.Y / 2f);
        _show.Parallel().TweenProperty(_logo, "position", centre, 0.42f)
             .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out).SetDelay(drop);
        _show.Parallel().TweenProperty(_logo, "rotation", -0.06f, 0.42f).From(-0.25f).SetDelay(drop);

        // Hold, then clear the stage: panels away, the logo up to its place.
        float exit = drop + 1.9f;
        for (int i = 0; i < _panels.Count; i++)
        {
            float to = (i % 2 == 0 ? 1f : -1f) * _size.Y * 1.2f;
            _show.Parallel().TweenProperty(_panels[i], "position", new Vector2(0f, to), 0.38f)
                 .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In).SetDelay(exit + i * 0.06f);
        }
        foreach (Line2D d in _dividers)
            _show.Parallel().TweenProperty(d, "modulate:a", 0f, 0.15f).SetDelay(exit);
        _show.Parallel().TweenCallback(Callable.From(() => Sfx.Play("whoosh", 0.8f))).SetDelay(exit);
        Node bg = GetChild(0);
        _show.Parallel().TweenProperty(bg, "modulate:a", 0f, 0.45f).SetDelay(exit + 0.2f);
        _show.Parallel().TweenProperty(_burst, "scale", Vector2.Zero, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In).SetDelay(exit + 0.1f);
        Rect2 target = _logoTarget();
        _show.Parallel().TweenProperty(_logo, "position", target.Position, 0.5f)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.InOut).SetDelay(exit + 0.1f);
        _show.Parallel().TweenProperty(_logo, "scale", Vector2.One, 0.5f).SetDelay(exit + 0.1f);
        _show.Parallel().TweenProperty(_logo, "rotation", 0f, 0.5f).SetDelay(exit + 0.1f);
        _show.Parallel().TweenCallback(Callable.From(Finish)).SetDelay(exit + 0.65f);
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        _shake = Mathf.MoveToward(_shake, 0f, (float)delta * 40f);
        _root.Position = _shake > 0f ? new Vector2((float)GD.RandRange(-_shake, _shake), (float)GD.RandRange(-_shake, _shake)) : Vector2.Zero;
        foreach (MapPad pad in _pads)
            if (pad.Poll() is MapPad.Press.Enter or MapPad.Press.Back) { Skip(); return; }
        if (Godot.Input.IsKeyPressed(Key.Escape) || Godot.Input.IsKeyPressed(Key.Space)) Skip();
    }

    /// <summary>Jump to the end: the title screen with its logo, music running.</summary>
    public void Skip()
    {
        if (_done) return;
        _show?.Kill();
        LogoLanded?.Invoke();
        Finish();
    }

    private void Finish()
    {
        if (_done) return;
        _done = true;
        Finished?.Invoke();
        QueueFree();
    }
}
