namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// A billboarded sprite drawn in profile and mirrored by heading. Project rule
/// (ADR 0004): anything that moves uses one of these.
///
/// Facing is horizontal only. Entities face left or right and never toward or
/// away from the camera, so each state is a single clip drawn facing right and
/// flipped for leftward travel. The clip list is read from a JSON frame map
/// emitted alongside the sheet, so adding frames never requires a code change.
/// </summary>
public partial class DirectionalSprite : Node3D
{
    private readonly string _sheetPath;
    private readonly string _framesPath;
    private readonly float _pixelSize;
    private AnimatedSprite3D _sprite = null!;
    private Vector3 _facing = Vector3.Right;
    private float _squash;
    private float _flash;
    private Color _tint = Colors.White;

    public DirectionalSprite(string sheetPath, string framesPath, float pixelSize = 0.0225f)
    {
        _sheetPath = sheetPath;
        _framesPath = framesPath;
        _pixelSize = pixelSize;
    }

    /// <summary>Per-entity tint, preserved across hit flashes.</summary>
    public Color Tint
    {
        get => _tint;
        set { _tint = value; if (_sprite is not null) _sprite.Modulate = value; }
    }

    public string CurrentClip => _sprite is null ? "-" : _sprite.Animation.ToString();
    public bool Flipped => _sprite is not null && _sprite.FlipH;

    public override void _Ready()
    {
        _sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(_sheetPath, _framesPath),
            PixelSize = _pixelSize,
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

    /// <summary>
    /// Set heading. Only the horizontal component matters: depth movement never
    /// turns the sprite, which is what keeps this a side-on brawler.
    /// </summary>
    public void SetFacing(Vector3 facing)
    {
        if (Mathf.Abs(facing.X) > 0.01f) _facing = new Vector3(Mathf.Sign(facing.X), 0f, 0f);
        if (_sprite is not null) _sprite.FlipH = _facing.X < 0f;
    }

    /// <summary>Play a state, resolving the direction prefix automatically.</summary>
    public void Play(string state, bool restart = false)
    {
        if (_sprite is null) return;
        if (!_sprite.SpriteFrames.HasAnimation(state)) state = "idle";
        if (!restart && _sprite.Animation == state && _sprite.IsPlaying()) return;
        _sprite.SpeedScale = 1f;
        _sprite.Play(state);
    }

    /// <summary>Hold an exact frame, so a swing matches its attack phase.</summary>
    public void Pin(string state, int frame)
    {
        if (_sprite is null) return;
        // Not every sheet authors every clip; fall back rather than throw.
        if (!_sprite.SpriteFrames.HasAnimation(state)) state = "idle";
        if (_sprite.Animation != state) _sprite.Animation = state;
        _sprite.Pause();
        _sprite.SpeedScale = 0f;
        _sprite.Frame = Mathf.Clamp(frame, 0, _sprite.SpriteFrames.GetFrameCount(state) - 1);
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
