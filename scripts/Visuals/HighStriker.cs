namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// The carnival strength tester that springs up beside Big Bertha's ground
/// pound (drawn in Aseprite: art/aseprite/high_striker.aseprite). The puck
/// shoots up the pole as high as the pound was strong; full strength rings
/// the bell. Only the picture: what ringing it does is up to the fighter.
/// </summary>
public partial class HighStriker : Node3D
{
    private const string Sheet = "res://assets/sprites/fx/high_striker.png";
    private const float PixelSize = 0.0115f;
    private const int FrameH = 208, PuckStart = 180, PuckTop = 26, PuckBell = 24;
    public const float RiseTime = 0.32f;

    private float _strength;

    /// <summary>A tower at a spot on the floor; strength 0..1 (1 rings the bell).</summary>
    public static void Spawn(Node parent, Vector3 at, float strength)
    {
        var s = new HighStriker { _strength = Mathf.Clamp(strength, 0f, 1f) };
        parent.AddChild(s);
        s.GlobalPosition = at;
    }

    private static float Y(int pixel) => (FrameH * 0.5f - pixel) * PixelSize;

    public override void _Ready()
    {
        SpriteFrames frames = FrameLibrary.Load(Sheet, ArtStyle.FramesFor(Sheet));
        AnimatedSprite3D Make() => new()
        {
            SpriteFrames = frames,
            PixelSize = PixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        // The tower stands on a pivot at floor level, so it springs up out of the ground.
        var root = new Node3D();
        AddChild(root);
        var tower = Make();
        tower.Animation = "tower";
        tower.Position = new Vector3(0f, FrameH * 0.5f * PixelSize, 0f);
        root.AddChild(tower);
        var puck = Make();
        puck.Animation = "puck";
        puck.Position = new Vector3(0f, 0f, 0.01f);
        tower.AddChild(puck);

        bool rings = _strength >= 1f;
        int peak = rings ? PuckBell : (int)Mathf.Lerp(PuckStart, PuckTop + 14, _strength);
        puck.Position = new Vector3(0f, Y(PuckStart), 0.01f);

        // Pop up out of the floor, the puck flies, then the whole thing sinks away.
        root.Scale = new Vector3(1f, 0.1f, 1f);
        Tween t = CreateTween();
        t.TweenProperty(root, "scale", Vector3.One, 0.08f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenProperty(puck, "position:y", Y(peak), RiseTime).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenCallback(Callable.From(() =>
        {
            if (rings) tower.Animation = "ring";
            string word = rings ? "DING DING!" : _strength < 0.34f ? "WEAK!" : _strength < 0.67f ? "NOT BAD!" : "STRONG!";
            Color col = rings ? new Color(1f, 0.9f, 0.3f) : new Color(0.95f, 0.95f, 1f);
            PopText.Spawn(GetParent(), GlobalPosition + new Vector3(0f, FrameH * PixelSize + 0.3f, 0f), word, col, rings ? 1.1f : 0.8f, 0.6f);
        }));
        if (!rings) t.TweenProperty(puck, "position:y", Y(PuckStart), 0.28f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.TweenInterval(rings ? 0.6f : 0.25f);
        t.TweenProperty(root, "scale", new Vector3(1f, 0.05f, 1f), 0.12f);
        t.TweenCallback(Callable.From(QueueFree));
    }
}
