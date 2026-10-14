namespace ClownSquad.World;

using System.Collections.Generic;
using Godot;

/// <summary>
/// The playable area, defined as whatever the camera can see. Nothing that
/// fights may leave it: the screen edges are the walls, which is both the genre
/// convention and what makes wall bounces possible.
///
/// Bounds are derived from the camera frustum rather than typed in, so they
/// stay correct when the camera is moved or zoomed. A fighter is kept inside
/// with its whole body visible, so taller fighters get a shallower far limit.
/// </summary>
public sealed class Arena
{
    /// <summary>The arena for the running scene, if one has been established.</summary>
    public static Arena? Current { get; set; }

    /// <summary>Fraction of the view usable before the edge, leaving a small border.</summary>
    private const float Usable = 0.95f;

    private readonly Vector3 _eye;
    private readonly Vector3 _forward;
    private readonly Vector3 _up;
    private readonly float _tanV;
    private readonly float _tanH;
    private readonly float _nearZ;
    private readonly Dictionary<int, float> _farZByHeight = new();

    /// <summary>
    /// Where the camera is along the stage. A scrolling stage moves the camera
    /// sideways only, which slides the whole visible box with it; nothing else
    /// about the frustum changes, so the walls are the rest walls offset by this.
    /// </summary>
    public float CameraX { get; set; }

    /// <summary>
    /// An optional walkway: the depth band fighters may use, intersected with
    /// what the camera sees. Scrolling stages use it, since their low camera can
    /// see far further back than anyone should walk.
    /// </summary>
    public (float Far, float Near)? Band { get; set; }

    /// <summary>Capture the camera as it stands. Later shake does not move the walls.</summary>
    public Arena(Transform3D cameraRest, float fovDegrees, float aspect)
    {
        Transform3D t = cameraRest;
        _eye = t.Origin;
        _forward = -t.Basis.Z;
        _up = t.Basis.Y;
        _tanV = Mathf.Tan(Mathf.DegToRad(fovDegrees) * 0.5f);
        _tanH = _tanV * aspect;
        CameraX = _eye.X;

        // Nearest depth at which feet on the ground are still on screen.
        _nearZ = Bisect(z => InView(new Vector3(_eye.X, 0f, z)), 0f, 40f);
    }

    /// <summary>Highest point still on screen at a depth. Bounds how high anything may jump.</summary>
    public float MaxVisibleHeight(float z) => Bisect(h => InView(new Vector3(_eye.X, h, z)), 0f, 30f);

    /// <summary>Depth range a body of the given height may occupy: far (negative) to near.</summary>
    public (float Far, float Near) DepthRange(float height) => (FarLimit(height), NearLimit);

    private float NearLimit => Band is (float, float) b ? Mathf.Min(_nearZ, b.Near) : _nearZ;
    private float FarLimit(float height) => Band is (float, float) b ? Mathf.Max(FarZ(height), b.Far) : FarZ(height);

    private bool InView(Vector3 p)
    {
        Vector3 rel = p - _eye;
        float depth = rel.Dot(_forward);
        if (depth <= 0.01f) return false;
        return Mathf.Abs(rel.Dot(_up)) <= depth * _tanV * Usable;
    }

    /// <summary>Furthest value from inside toward outside that still passes.</summary>
    private static float Bisect(System.Func<float, bool> ok, float inside, float outside)
    {
        for (int i = 0; i < 24; i++)
        {
            float mid = (inside + outside) * 0.5f;
            if (ok(mid)) inside = mid; else outside = mid;
        }
        return inside;
    }

    private float FarZ(float height)
    {
        int key = Mathf.RoundToInt(height * 20f);
        if (_farZByHeight.TryGetValue(key, out float z)) return z;
        z = Bisect(v => InView(new Vector3(_eye.X, height, v)), 0f, -60f);
        _farZByHeight[key] = z;
        return z;
    }

    /// <summary>Half-width of the visible strip at a depth, for a body of the given height.</summary>
    public float HalfWidthAt(float z, float height)
    {
        // The head is nearer the camera than the feet, so it leaves the frame
        // first; measure the limit there.
        float depth = (new Vector3(_eye.X, height, z) - _eye).Dot(_forward);
        return Mathf.Max(0.5f, depth * _tanH * Usable);
    }

    /// <summary>
    /// Pull a position back inside the view. The wall result reports which side
    /// wall was hit: -1 left, +1 right, 0 neither.
    /// </summary>
    public Vector3 Clamp(Vector3 pos, float height, float halfWidth, out int wall)
    {
        float top = height + Mathf.Max(pos.Y, 0f);
        float z = Mathf.Clamp(pos.Z, FarLimit(top), NearLimit);
        float limit = HalfWidthAt(z, top) - halfWidth;
        float x = pos.X;
        wall = 0;
        if (x > CameraX + limit) { x = CameraX + limit; wall = 1; }
        else if (x < CameraX - limit) { x = CameraX - limit; wall = -1; }
        return new Vector3(x, pos.Y, z);
    }
}
