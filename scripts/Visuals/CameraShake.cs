namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// Positional camera shake. Attach to a Camera3D; it stores the rest position
/// and offsets from it, so the camera framing is never permanently disturbed.
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

    public override void _Ready()
    {
        AddToGroup(Group);
        _camera = GetParent<Camera3D>();
        _rest = _camera.Position;
        _rng.Randomize();
    }

    /// <summary>Find the active shaker and request a shake. Safe if absent.</summary>
    public static void Request(Node from, float amplitude, float duration)
    {
        if (from.GetTree()?.GetFirstNodeInGroup(Group) is CameraShake s)
            s.Shake(amplitude, duration);
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
