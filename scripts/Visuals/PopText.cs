namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// Words that pop out of the fight and float away: "+15 XP", "LEVEL UP!",
/// "CRIT!". Drawn in the world so they sit on whoever earned them, and over
/// everything so nothing hides them.
/// </summary>
public static class PopText
{
    private static Font? _font;

    public static void Spawn(Node parent, Vector3 at, string text, Color color, float size = 1f, float rise = 0.9f)
    {
        _font ??= ClownSquad.UI.UiFont.Display;
        var label = new Label3D
        {
            Text = text,
            Font = _font,
            FontSize = (int)(56 * size),
            OutlineSize = (int)(16 * size),
            OutlineModulate = new Color(0.08f, 0.04f, 0.1f),
            Modulate = color,
            PixelSize = 0.006f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            RenderPriority = 10,
            OutlineRenderPriority = 9,
        };
        parent.AddChild(label);
        label.GlobalPosition = at;
        label.Scale = Vector3.One * 0.3f;

        Tween t = label.CreateTween().SetParallel();
        t.TweenProperty(label, "scale", Vector3.One, 0.22f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenProperty(label, "global_position", at + Vector3.Up * rise, 1.0f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.TweenProperty(label, "modulate:a", 0f, 0.35f).SetDelay(0.75f);
        t.TweenProperty(label, "outline_modulate:a", 0f, 0.35f).SetDelay(0.75f);
        t.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}
