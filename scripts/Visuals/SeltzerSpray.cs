namespace ClownSquad.Visuals;

using Godot;
using ClownSquad.Actors;

/// <summary>
/// What comes out of the seltzer bottle (drawn in Aseprite:
/// art/aseprite/seltzer_spray.aseprite): a squirt for a combo tap, a burst for
/// the big third hit, a long blast for the held SOAK. The nozzle is the
/// sheet's left edge; the spray is sized so it reaches as far as the hit does,
/// rides along with the fighter and is gone in a fraction of a second.
/// </summary>
public partial class SeltzerSpray : Node3D
{
    private const string Sheet = "res://assets/sprites/fx/seltzer_spray.png";
    private const int SheetWidth = 224;
    private string _clip = "squirt";
    private float _pixel;
    private bool _flip;

    /// <summary>How far each clip's spray reaches on its sheet, in pixels.</summary>
    private static int ReachPx(string clip) => clip switch { "burst" => 150, "blast" => 222, _ => 118 };

    /// <summary>A spray from a fighter's spout: reach in world units, height of the spout above the floor.</summary>
    public static void Spawn(Fighter f, string clip, float reach, float height)
    {
        float px = reach / ReachPx(clip);
        var s = new SeltzerSpray { _clip = clip, _pixel = px, _flip = f.Facing.X < 0f };
        f.AddChild(s);
        // the sheet is centred on the node: put its left edge (the nozzle) just ahead of the clown
        s.Position = new Vector3(f.Facing.X * (SheetWidth * px * 0.5f + 0.3f), height, 0.05f);
    }

    public override void _Ready()
    {
        var sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(Sheet, ArtStyle.FramesFor(Sheet)),
            PixelSize = _pixel,
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
        GetTree().CreateTimer(0.6).Timeout += () => { if (IsInstanceValid(this)) QueueFree(); };
    }
}
