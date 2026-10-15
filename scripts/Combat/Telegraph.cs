namespace ClownSquad.Combat;

using Godot;

/// <summary>
/// A warning painted on the floor where an attack is about to land: a circle,
/// a lane or a cone. It fades in, pulses faster as the moment approaches, and
/// is drawn exactly the size of the hit, so what you see is what you dodge.
/// Every enemy special and boss move uses one; Bertie's landing circle was the
/// first of them.
/// </summary>
public partial class Telegraph : Node3D
{
    private static readonly Color Warn = new(0.95f, 0.15f, 0.2f);

    private StandardMaterial3D _mat = null!;
    private float _age;
    private float _lead = 1f;
    private Color _color = Warn;

    /// <summary>0 when it appears, 1 at the moment of impact. Drives the pulse.</summary>
    public float Urgency { get; set; }
    /// <summary>When set, the telegraph tracks urgency itself over this many seconds, then frees itself after it.</summary>
    public float AutoSeconds { get; set; }

    private static StandardMaterial3D Paint(Color c) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = new Color(c, 0.2f),
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    private static Telegraph Make(Node parent, Mesh mesh, Vector3 at, float yaw, float autoSeconds, Color? color)
    {
        var t = new Telegraph { AutoSeconds = autoSeconds, _color = color ?? Warn };
        t._mat = Paint(t._color);
        t.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = t._mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        parent.AddChild(t);
        t.GlobalPosition = new Vector3(at.X, 0.03f, at.Z);
        t.Rotation = new Vector3(0f, yaw, 0f);
        return t;
    }

    /// <summary>A disc of the given radius on the floor.</summary>
    public static Telegraph Circle(Node parent, Vector3 at, float radius, float autoSeconds = 0f, Color? color = null) =>
        Make(parent, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.02f, RadialSegments = 48 }, at, 0f, autoSeconds, color);

    /// <summary>A lane from a point along a direction: length long, width wide.</summary>
    public static Telegraph Lane(Node parent, Vector3 from, Vector3 dir, float length, float width, float autoSeconds = 0f, Color? color = null)
    {
        dir.Y = 0f;
        dir = dir.LengthSquared() > 0.001f ? dir.Normalized() : Vector3.Right;
        var mesh = new BoxMesh { Size = new Vector3(length, 0.02f, width) };
        Telegraph t = Make(parent, mesh, from + dir * (length * 0.5f), -Mathf.Atan2(dir.Z, dir.X), autoSeconds, color);
        return t;
    }

    /// <summary>A wedge from a point: length long, spread degrees wide, facing dir.</summary>
    public static Telegraph Cone(Node parent, Vector3 from, Vector3 dir, float length, float spread, float autoSeconds = 0f, Color? color = null)
    {
        dir.Y = 0f;
        dir = dir.LengthSquared() > 0.001f ? dir.Normalized() : Vector3.Right;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int seg = 16;
        float half = Mathf.DegToRad(spread) * 0.5f;
        for (int i = 0; i < seg; i++)
        {
            float a0 = -half + 2f * half * i / seg, a1 = -half + 2f * half * (i + 1) / seg;
            st.AddVertex(Vector3.Zero);
            st.AddVertex(new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * length);
            st.AddVertex(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * length);
        }
        return Make(parent, st.Commit(), from, -Mathf.Atan2(dir.Z, dir.X), autoSeconds, color);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (AutoSeconds > 0f)
        {
            Urgency = Mathf.Clamp(_age / AutoSeconds, 0f, 1f);
            if (_age > AutoSeconds + 0.05f) { QueueFree(); return; }
        }
        float pulse = 0.5f + 0.5f * Mathf.Sin(_age * Mathf.Lerp(6f, 24f, Urgency));
        float fadeIn = Mathf.Clamp(_age / 0.12f, 0f, 1f);
        _mat.AlbedoColor = new Color(_color, (0.14f + 0.32f * Urgency + 0.1f * pulse) * fadeIn);
    }

    /// <summary>Remove it now (the attack landed, or was cancelled).</summary>
    public void Clear()
    {
        if (IsInstanceValid(this)) QueueFree();
    }

    /// <summary>Whether a ground point is inside a lane drawn from `from` along `dir`.</summary>
    public static bool InLane(Vector3 p, Vector3 from, Vector3 dir, float length, float width)
    {
        dir.Y = 0f;
        dir = dir.Normalized();
        Vector3 d = p - from;
        d.Y = 0f;
        float along = d.Dot(dir);
        float across = Mathf.Abs(d.Dot(new Vector3(-dir.Z, 0f, dir.X)));
        return along >= -0.3f && along <= length && across <= width * 0.5f;
    }

    /// <summary>Whether a ground point is inside a cone.</summary>
    public static bool InCone(Vector3 p, Vector3 from, Vector3 dir, float length, float spread)
    {
        Vector3 d = p - from;
        d.Y = 0f;
        float dist = d.Length();
        if (dist > length || dist < 0.01f) return dist < 0.01f;
        dir.Y = 0f;
        return Mathf.RadToDeg(dir.Normalized().AngleTo(d / dist)) <= spread * 0.5f;
    }
}
