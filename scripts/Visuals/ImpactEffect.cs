namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// One-shot ground impact: flash, shock ring, dust. Frees itself when the clip
/// finishes. Exempt from the directional-sprite rule (ADR 0004) because the
/// burst is radially symmetric and reads the same from any heading.
/// </summary>
public partial class ImpactEffect : Node3D
{
    private const string Sheet = "res://assets/effects/impact.png";
    private const string Frames = "res://assets/effects/impact.frames.json";

    private float _scale = 1f;

    public static void Spawn(Node parent, Vector3 globalPosition, float scale = 1f)
    {
        if (parent is null || !GodotObject.IsInstanceValid(parent)) return;
        var fx = new ImpactEffect { _scale = scale };
        parent.AddChild(fx);
        fx.GlobalPosition = globalPosition;
    }

    public override void _Ready()
    {
        var sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(Sheet, Frames),
            PixelSize = 0.04f * _scale,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            // Blended, not alpha-cut: the later frames fade out, and discarding
            // would pop them off instead of letting the dust thin away.
            AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
            Transparent = true,
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            NoDepthTest = false,
        };
        AddChild(sprite);
        sprite.AnimationFinished += QueueFree;
        sprite.Play("burst");
    }
}
