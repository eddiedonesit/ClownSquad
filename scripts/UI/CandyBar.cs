namespace ClownSquad.UI;

using Godot;

/// <summary>
/// A candy-striped health bar. The stripes crawl like a barber pole, a pale
/// "damage trail" lingers where health just was and drains away after a beat
/// (the clearest way to show how much one hit took), and the bar flashes when
/// it drops low.
/// </summary>
public partial class CandyBar : Control
{
    private const string ShaderCode = @"
shader_type canvas_item;
uniform float fill = 1.0;
uniform float ghost = 1.0;
uniform float aspect = 10.0;
uniform float scroll = 0.0;
uniform float flash = 0.0;
uniform vec4 stripe_a : source_color = vec4(0.86, 0.2, 0.25, 1.0);
uniform vec4 stripe_b : source_color = vec4(1.0, 0.95, 0.92, 1.0);
uniform vec4 back_color : source_color = vec4(0.16, 0.10, 0.20, 1.0);
uniform vec4 ghost_color : source_color = vec4(1.0, 0.93, 0.72, 1.0);
void fragment() {
    vec4 c = back_color;
    if (UV.x < ghost) c = ghost_color;
    if (UV.x < fill) {
        float s = fract((UV.x * aspect + UV.y) * 0.5 - scroll);
        c = s < 0.5 ? stripe_a : stripe_b;
        c.rgb += 0.22 * (1.0 - smoothstep(0.0, 0.45, abs(UV.y - 0.28)));
        c.rgb *= 1.0 - 0.25 * smoothstep(0.6, 1.0, UV.y);
        c.rgb = mix(c.rgb, vec3(1.0), flash);
    }
    COLOR = c;
}";

    private static Shader? _shader;

    /// <summary>Health as a fraction, 0 to 1.</summary>
    public float Value { get; set; } = 1f;
    public Color StripeA { get; set; } = new(0.86f, 0.2f, 0.25f);
    public Color StripeB { get; set; } = new(1f, 0.95f, 0.92f);

    private ShaderMaterial _mat = null!;
    private float _ghost = 1f;
    private float _ghostHold;
    private float _scroll;
    private float _lastValue = 1f;
    private float _time;

    public override void _Ready()
    {
        _shader ??= new Shader { Code = ShaderCode };
        _mat = new ShaderMaterial { Shader = _shader };

        var frame = new Panel();
        frame.SetAnchorsPreset(LayoutPreset.FullRect);
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.06f, 0.12f),
            BorderColor = new Color(0.08f, 0.04f, 0.1f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7,
        });
        AddChild(frame);

        var fill = new ColorRect { Material = _mat };
        fill.SetAnchorsPreset(LayoutPreset.FullRect);
        fill.OffsetLeft = 4; fill.OffsetTop = 4; fill.OffsetRight = -4; fill.OffsetBottom = -4;
        AddChild(fill);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        float v = Mathf.Clamp(Value, 0f, 1f);

        // The trail holds briefly after a hit, then drains to meet the bar.
        if (v < _lastValue) _ghostHold = 0.45f;
        if (v > _ghost) _ghost = v;
        if (_ghostHold > 0f) _ghostHold -= dt;
        else _ghost = Mathf.MoveToward(_ghost, v, dt * 0.9f);
        _lastValue = v;

        _scroll += dt * 0.6f;
        float flash = v > 0f && v < 0.3f ? 0.35f * (0.5f + 0.5f * Mathf.Sin(_time * 12f)) : 0f;

        _mat.SetShaderParameter("fill", v);
        _mat.SetShaderParameter("ghost", _ghost);
        _mat.SetShaderParameter("aspect", Size.Y > 1f ? Size.X / Size.Y : 10f);
        _mat.SetShaderParameter("scroll", _scroll);
        _mat.SetShaderParameter("flash", flash);
        _mat.SetShaderParameter("stripe_a", StripeA);
        _mat.SetShaderParameter("stripe_b", StripeB);
    }
}
