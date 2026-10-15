namespace ClownSquad.UI;

using Godot;
using ClownSquad.Audio;

/// <summary>
/// A boss's entrance, comic-book style: a slanted yellow caption slams in
/// with the name, the tagline under it on a red strip, zigzag edges, then it
/// whips away. Plays over the fight without pausing it.
/// </summary>
public partial class BossCard : CanvasLayer
{
    private string _name = "";
    private string _tag = "";

    public static void Show(Node owner, string name, string tagline)
    {
        if (!owner.IsInsideTree() || string.IsNullOrEmpty(name)) return;
        owner.GetTree().Root.AddChild(new BossCard { _name = name, _tag = tagline, Layer = 18 });
    }

    public override void _Ready()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        var root = new Control { Position = new Vector2(-size.X, size.Y * 0.2f), RotationDegrees = -4f };
        AddChild(root);

        float w = size.X * 0.8f, h = size.Y * 0.2f;
        root.AddChild(Zig(new Color(0.07f, 0.04f, 0.1f), w + 16f, h + 16f, -8f, -8f));
        root.AddChild(Zig(new Color(1f, 0.85f, 0.3f), w, h, 0f, 0f));
        root.AddChild(Zig(new Color(0.86f, 0.18f, 0.24f), w * 0.7f, h * 0.36f, w * 0.18f, h * 0.86f));
        root.AddChild(new Label
        {
            Text = "BOSS!", Position = new Vector2(24f, 4f),
            LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = Mathf.RoundToInt(h * 0.22f), FontColor = new Color(0.86f, 0.18f, 0.24f) },
        });
        root.AddChild(new Label
        {
            Text = _name, Position = new Vector2(0f, h * 0.18f), Size = new Vector2(w, h * 0.6f),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings
            {
                Font = UiFont.Display, FontSize = Mathf.RoundToInt(h * 0.42f), FontColor = new Color(0.12f, 0.06f, 0.16f),
                OutlineSize = 6, OutlineColor = new Color(1f, 1f, 1f),
            },
        });
        root.AddChild(new Label
        {
            Text = _tag, Position = new Vector2(w * 0.18f, h * 0.86f), Size = new Vector2(w * 0.7f, h * 0.36f),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            LabelSettings = new LabelSettings { Font = UiFont.Display, FontSize = Mathf.RoundToInt(h * 0.15f), FontColor = Colors.White },
        });

        Sfx.Play("kaboom", 1.2f, -6f);
        Sfx.Play("crowd_cheer", 1f, -6f);
        Tween t = CreateTween();
        t.TweenProperty(root, "position:x", size.X * 0.1f, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenInterval(1.8f);
        t.TweenProperty(root, "position:x", size.X * 1.2f, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        t.TweenCallback(Callable.From(QueueFree));
    }

    /// <summary>A panel with zigzag top and bottom edges.</summary>
    private static Polygon2D Zig(Color c, float w, float h, float x, float y)
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        int teeth = Mathf.Max(6, (int)(w / 34f));
        for (int i = 0; i <= teeth; i++) pts.Add(new Vector2(x + w * i / teeth, y + (i % 2 == 0 ? 0f : 9f)));
        for (int i = teeth; i >= 0; i--) pts.Add(new Vector2(x + w * i / teeth, y + h - (i % 2 == 0 ? 0f : 9f)));
        return new Polygon2D { Polygon = pts.ToArray(), Color = c };
    }
}
