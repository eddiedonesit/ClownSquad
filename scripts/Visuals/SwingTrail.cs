namespace ClownSquad.Visuals;

using Godot;
using ClownSquad.Actors;

/// <summary>
/// The smear behind a swing (drawn in Aseprite: art/aseprite/swing_trail.aseprite):
/// a bright crescent from over the shoulder down to the front for a swing, a
/// streak for a thrust. Plays its three frames (about a tenth of a second) on
/// the frame the swing's hit window opens, then goes. Sized to the weapon's
/// reach and flipped with the fighter.
/// </summary>
public partial class SwingTrail : Node3D
{
    private const string Sheet = "res://assets/sprites/fx/swing_trail.png";
    private string _clip = "arc";
    private float _scale = 1f;
    private bool _flip;

    /// <summary>A trail on a fighter: "arc" for a swing, "thrust" for a straight stab.</summary>
    public static void Spawn(Fighter f, string clip, float scale, float height)
    {
        var t = new SwingTrail { _clip = clip, _scale = scale, _flip = f.Facing.X < 0f };
        f.AddChild(t);
        t.Position = new Vector3(0f, height, 0.04f);
    }

    public override void _Ready()
    {
        var sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(Sheet, ArtStyle.FramesFor(Sheet)),
            PixelSize = 0.01125f * _scale,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Disabled,
            Shaded = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            FlipH = _flip,
        };
        AddChild(sprite);
        sprite.Play(_clip);
        sprite.AnimationFinished += QueueFree;
        GetTree().CreateTimer(0.4).Timeout += () => { if (IsInstanceValid(this)) QueueFree(); };
    }
}
