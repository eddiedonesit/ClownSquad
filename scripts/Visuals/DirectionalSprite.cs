namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// A billboarded sprite that picks its animation set from the direction it
/// faces. Project rule (ADR 0004): anything that moves uses one of these.
///
/// Clip naming contract: a state such as "walk" exists as "walk" (facing the
/// camera), "back_walk" (facing away) and "side_walk" (profile, mirrored for
/// leftward travel). The clip list is read from a JSON frame map emitted
/// alongside the sheet, so adding frames never requires a code change.
/// </summary>
public partial class DirectionalSprite : Node3D
{
    private readonly string _sheetPath;
    private readonly string _framesPath;
    private AnimatedSprite3D _sprite = null!;
    private Vector3 _facing = Vector3.Back;
    private float _squash;
    private float _flash;
    private Color _tint = Colors.White;

    public DirectionalSprite(string sheetPath, string framesPath)
    {
        _sheetPath = sheetPath;
        _framesPath = framesPath;
    }

    /// <summary>Per-entity tint, preserved across hit flashes.</summary>
    public Color Tint
    {
        get => _tint;
        set { _tint = value; if (_sprite is not null) _sprite.Modulate = value; }
    }

    public string CurrentClip => _sprite is null ? "-" : _sprite.Animation.ToString();
    public bool Flipped => _sprite is not null && _sprite.FlipH;

    /// <summary>
    /// Which clip set the current facing selects. Horizontal travel wins over
    /// depth: profile frames read far better than a front view sliding sideways.
    /// The camera looks down -Z, so +Z faces the viewer.
    /// </summary>
    public string Prefix =>
        Mathf.Abs(_facing.X) > 0.5f ? "side_" : _facing.Z < 0f ? "back_" : "";

    public override void _Ready()
    {
        _sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(_sheetPath, _framesPath),
            PixelSize = 0.0225f,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Modulate = _tint,
        };
        AddChild(_sprite);
        Play("idle");
    }

    public void SetFacing(Vector3 facing)
    {
        if (facing.LengthSquared() > 0.0001f) _facing = facing;
        if (_sprite is not null)
            // Only profile frames mirror; front and back are drawn face-on.
            _sprite.FlipH = Prefix == "side_" && _facing.X < 0f;
    }

    /// <summary>Play a state, resolving the direction prefix automatically.</summary>
    public void Play(string state, bool restart = false)
    {
        if (_sprite is null) return;
        string clip = Prefix + state;
        if (!_sprite.SpriteFrames.HasAnimation(clip)) clip = state;
        if (!restart && _sprite.Animation == clip && _sprite.IsPlaying()) return;
        _sprite.SpeedScale = 1f;
        _sprite.Play(clip);
    }

    /// <summary>Hold an exact frame, so a swing matches its attack phase.</summary>
    public void Pin(string state, int frame)
    {
        if (_sprite is null) return;
        string clip = Prefix + state;
        if (!_sprite.SpriteFrames.HasAnimation(clip)) clip = state;
        if (_sprite.Animation != clip) _sprite.Animation = clip;
        _sprite.Pause();
        _sprite.SpeedScale = 0f;
        _sprite.Frame = Mathf.Clamp(frame, 0, _sprite.SpriteFrames.GetFrameCount(clip) - 1);
    }

    public void Flash(float seconds = 0.12f) => _flash = seconds;

    public void Squash(float amount) => _squash = Mathf.Max(_squash, amount);

    /// <summary>Decay the impact effects. Call once per physics frame.</summary>
    public void Tick(float dt)
    {
        if (_sprite is null) return;

        if (_flash > 0f)
        {
            _flash -= dt;
            _sprite.Modulate = _flash > 0f ? new Color(3f, 2f, 2f) : _tint;
        }

        _squash = Mathf.MoveToward(_squash, 0f, 4f * dt);
        _sprite.Scale = new Vector3(1f + _squash * 0.5f, 1f - _squash * 0.5f, 1f);
    }
}
