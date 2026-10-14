namespace ClownSquad.UI;

using Godot;

/// <summary>Lives shown as a bunch of party balloons, each on its own string.</summary>
public partial class BalloonRow : Control
{
    private static readonly Color[] Colors =
    {
        new(0.92f, 0.22f, 0.28f), new(0.98f, 0.8f, 0.2f), new(0.3f, 0.55f, 0.95f), new(0.4f, 0.8f, 0.4f),
    };

    private int _count;
    private float _time;

    public int Count
    {
        get => _count;
        set { if (_count != value) { _count = value; QueueRedraw(); } }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();   // they bob
    }

    public override void _Draw()
    {
        const float r = 11f, gap = 26f;
        Color outline = new(0.08f, 0.04f, 0.1f);
        float left = Size.X * 0.5f - (Count - 1) * gap * 0.5f;
        for (int i = 0; i < Count; i++)
        {
            float bob = Mathf.Sin(_time * 2.2f + i * 1.3f) * 2.5f;
            var c = new Vector2(left + i * gap, r + 3f + bob);
            Vector2 knot = c + new Vector2(0f, r + 1f);
            DrawPolyline(new[] { knot, knot + new Vector2(3f, 9f), knot + new Vector2(-2f, 18f) }, outline, 1.5f);
            DrawCircle(c, r + 2.5f, outline);
            DrawCircle(c, r, Colors[i % Colors.Length]);
            DrawCircle(c + new Vector2(-4f, -4f), 3.2f, new Color(1f, 1f, 1f, 0.75f));
            DrawColoredPolygon(new[] { knot + new Vector2(-3f, 2f), knot + new Vector2(3f, 2f), knot + new Vector2(0f, -2f) },
                               Colors[i % Colors.Length]);
        }
    }
}
