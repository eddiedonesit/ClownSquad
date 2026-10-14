namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// Positional camera shake, and the punch-in: a quick narrowing of the field of
/// view on a big hit that springs back, which reads as the camera leaning in.
/// Attach to a Camera3D; it stores the rest position and field of view and
/// offsets from them, so the framing is never permanently disturbed (and the
/// arena walls, which are derived from the rest framing, stay put).
/// </summary>
public partial class CameraShake : Node
{
    public const string Group = "camera_shake";

    private Camera3D _camera = null!;
    private Vector3 _rest;
    private float _amplitude;
    private float _remaining;
    private float _duration;
    private readonly RandomNumberGenerator _rng = new();
    private float _restFov;
    private float _punch;
    private const float PunchDegrees = 2.6f;
    private const float PunchDecay = 5.5f;

    public override void _Ready()
    {
        AddToGroup(Group);
        _camera = GetParent<Camera3D>();
        _rest = _camera.Position;
        _restFov = _camera.Fov;
        _rng.Randomize();
    }

    /// <summary>Find the active shaker and request a shake. Safe if absent.</summary>
    public static void Request(Node from, float amplitude, float duration)
    {
        if (from.GetTree()?.GetFirstNodeInGroup(Group) is CameraShake s)
            s.Shake(amplitude, duration);
    }

    /// <summary>Lean the camera in for a big hit; strength about 0.5 for a finisher, 1 for a knockout.</summary>
    public static void RequestPunch(Node from, float strength)
    {
        if (from.GetTree()?.GetFirstNodeInGroup(Group) is CameraShake s)
            s._punch = Mathf.Max(s._punch, Mathf.Clamp(strength, 0f, 1.5f));
    }

    public void Shake(float amplitude, float duration)
    {
        // Take the stronger of the two rather than summing, so rapid hits do not
        // compound into a camera that will not settle.
        if (amplitude <= _amplitude && _remaining > 0f) return;
        _amplitude = amplitude;
        _duration = Mathf.Max(duration, 0.01f);
        _remaining = _duration;
    }

    public override void _Process(double delta)
    {
        if (_punch > 0f)
        {
            _punch = Mathf.Max(0f, _punch - PunchDecay * (float)delta * Mathf.Max(_punch, 0.25f));
            _camera.Fov = _restFov - PunchDegrees * _punch;
        }
        else if (_camera is not null && _camera.Fov != _restFov)
        {
            _camera.Fov = _restFov;
        }

        if (_remaining <= 0f)
        {
            if (_camera is not null && _camera.Position != _rest) _camera.Position = _rest;
            return;
        }

        _remaining -= (float)delta;
        float falloff = Mathf.Max(_remaining, 0f) / _duration;
        float a = _amplitude * falloff * falloff;
        _camera.Position = _rest + new Vector3(
            _rng.RandfRange(-a, a), _rng.RandfRange(-a, a), _rng.RandfRange(-a, a));
    }
}
