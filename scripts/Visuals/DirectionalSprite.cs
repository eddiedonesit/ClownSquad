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
    private ShaderMaterial? _lit;

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
        set { _tint = value; if (_sprite is not null && _flash <= 0f) _sprite.Modulate = value; }
    }

    /// <summary>
    /// Selects an alternate clip set, such as "empty_" while a thrown weapon is
    /// away. A state falls back to its unprefixed clip when the set lacks it.
    /// </summary>
    public string ClipPrefix { get; set; } = "";

    /// <summary>Forces mirroring regardless of heading. Used to fake a whirl.</summary>
    public bool? FlipOverride { get; set; }

    public string CurrentClip => _sprite is null ? "-" : _sprite.Animation.ToString();
    public bool Flipped => _sprite is not null && _sprite.FlipH;

    /// <summary>Colour this frame, hit flash included, for attached props to match.</summary>
    public Color CurrentModulate => _sprite?.Modulate ?? _tint;

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

        // Sheets with a normal map are drawn lit by the scene's lights. The
        // material replaces the sprite's own, so tint, flash and mirroring are
        // pushed to it in Tick rather than through Modulate.
        _lit = LitSprite.For(_sheetPath);
        if (_lit is not null) _sprite.MaterialOverride = _lit;

        // Stand the sprite so its feet row lands on the node origin. Derived
        // from the sheet rather than hardcoded: the offset differs with sprite
        // scale and with any padding the sheet keeps below the feet.
        FrameLibrary.SheetInfo info = FrameLibrary.Info(_sheetPath);
        float rowsAboveCentre = info.Baseline - info.FrameHeight * 0.5f;
        _sprite.Position = new Vector3(0f, rowsAboveCentre * _pixelSize, 0f);

        Play("idle");
    }

    /// <summary>
    /// Set heading. Only the horizontal component matters: depth movement never
    /// turns the sprite, which is what keeps this a side-on brawler.
    /// </summary>
    public void SetFacing(Vector3 facing)
    {
        if (Mathf.Abs(facing.X) > 0.01f) _facing = new Vector3(Mathf.Sign(facing.X), 0f, 0f);
        if (_sprite is not null) _sprite.FlipH = FlipOverride ?? (_facing.X < 0f);
    }

    public bool HasClip(string state) => _sprite is not null && _sprite.SpriteFrames.HasAnimation(state);

    private string Resolve(string state)
    {
        SpriteFrames frames = _sprite.SpriteFrames;
        if (ClipPrefix.Length > 0 && frames.HasAnimation(ClipPrefix + state)) return ClipPrefix + state;
        // Not every sheet authors every clip; degrade rather than throw.
        return frames.HasAnimation(state) ? state : "idle";
    }

    /// <summary>Play a state by name.</summary>
    public void Play(string state, bool restart = false)
    {
        if (_sprite is null) return;
        string clip = Resolve(state);
        if (!restart && _sprite.Animation == clip && _sprite.IsPlaying()) return;
        _sprite.SpeedScale = 1f;
        _sprite.Play(clip);
    }

    /// <summary>Hold an exact frame, so a swing matches its attack phase.</summary>
    public void Pin(string state, int frame)
    {
        if (_sprite is null) return;
        string clip = Resolve(state);
        if (_sprite.Animation != clip) _sprite.Animation = clip;
        _sprite.Pause();
        _sprite.SpeedScale = 0f;
        _sprite.Frame = Mathf.Clamp(frame, 0, _sprite.SpriteFrames.GetFrameCount(clip) - 1);
    }

    public void Flash(float seconds = 0.12f) => _flash = seconds;

    /// <summary>The sheet frame on show right now.</summary>
    public int SheetFrame => _sprite is null ? 0 : FrameLibrary.FrameIndex(_sheetPath, _sprite.Animation, _sprite.Frame);

    /// <summary>The sheet this sprite draws from.</summary>
    public string SheetPath => _sheetPath;

    /// <summary>
    /// A named point of the frame on show (the weapon's tip, a hand, the head),
    /// as an offset from this node in the world, mirrored with the sprite; null
    /// where the frame has none (a somersault, lying down).
    /// </summary>
    public Vector3? Anchor(string name)
    {
        if (_sprite is null || FrameLibrary.Anchor(_sheetPath, SheetFrame, name) is not Vector2 a) return null;
        FrameLibrary.SheetInfo info = FrameLibrary.Info(_sheetPath);
        float x = (a.X - info.FrameWidth * 0.5f) * _pixelSize;
        if (_sprite.FlipH) x = -x;
        return new Vector3(x, (info.Baseline - a.Y) * _pixelSize, 0f);
    }

    /// <summary>Whether the sprite is drawn mirrored (facing left) right now.</summary>
    public bool Mirrored => _sprite is not null && _sprite.FlipH;

    public void Squash(float amount) => _squash = Mathf.Max(_squash, amount);

    /// <summary>Decay the impact effects. Call once per physics frame.</summary>
    public void Tick(float dt)
    {
        if (_sprite is null) return;

        if (_flash > 0f) _flash -= dt;
        bool flashing = _flash > 0f;
        _sprite.Modulate = flashing ? new Color(3f, 2f, 2f) : _tint;
        if (_lit is not null) LitSprite.Apply(_lit, _tint, flashing ? 1.1f : 0f, _sprite.FlipH);

        _squash = Mathf.MoveToward(_squash, 0f, 4f * dt);
        _sprite.Scale = new Vector3(1f + _squash * 0.5f, 1f - _squash * 0.5f, 1f);
    }
}
