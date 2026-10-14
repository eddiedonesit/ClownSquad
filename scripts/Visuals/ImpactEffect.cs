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
    private bool _glow = true;

    /// <summary>A burst; with glow, it also throws a brief flash of real light on whatever is near.</summary>
    public static void Spawn(Node parent, Vector3 globalPosition, float scale = 1f, bool glow = true)
    {
        if (parent is null || !GodotObject.IsInstanceValid(parent)) return;
        var fx = new ImpactEffect { _scale = scale, _glow = glow };
        parent.AddChild(fx);
        fx.GlobalPosition = globalPosition;
    }

    public override void _Ready()
    {
        var sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(Sheet, Frames),
            PixelSize = 0.034f * _scale,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            // Blended, not alpha-cut: the later frames fade out, and discarding
            // would pop them off instead of letting the dust thin away.
            AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
            Transparent = true,
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The burst is an upright billboard standing at the mallet, so its
            // lower half sits below y=0 and the ground plane depth-culls it,
            // slicing the blast flat and z-fighting into speckled holes where
            // the surfaces are near-coplanar. Impact VFX draw on top instead.
            NoDepthTest = true,
            RenderPriority = 10,
        };
        AddChild(sprite);
        sprite.AnimationFinished += QueueFree;
        sprite.Play("burst");

        if (!_glow) return;
        // The lighting half of the hit: lit sprites around the impact catch it
        // on the side that faces it, then it is gone. Sized by the hit.
        var light = new OmniLight3D
        {
            LightColor = new Color(1f, 0.82f, 0.55f),
            LightEnergy = 3.2f * Mathf.Min(_scale, 2.5f),
            OmniRange = 1.6f + 1.6f * _scale,
            ShadowEnabled = false,
            Position = new Vector3(0f, 0.35f, 0.35f),
        };
        AddChild(light);
        CreateTween().TweenProperty(light, "light_energy", 0f, 0.2f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }
}
