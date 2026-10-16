namespace ClownSquad.Visuals;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;

/// <summary>
/// The Daily Circus's LIGHTS OUT: the whole screen goes dark except a soft
/// pool of light round each clown, wherever they are. A full-screen overlay
/// under the HUD, its holes following the players' screen positions.
/// </summary>
public partial class LightsOut : CanvasLayer
{
    private const string Code = @"
shader_type canvas_item;
uniform vec2 centres[4];
uniform int count = 0;
uniform float radius = 220.0;
uniform vec2 screen = vec2(1152.0, 648.0);
void fragment() {
    vec2 p = UV * screen;
    float light = 0.0;
    for (int i = 0; i < 4; i++) {
        if (i >= count) break;
        vec2 d = (p - centres[i]) * vec2(1.0, 1.25);
        light = max(light, 1.0 - smoothstep(radius * 0.45, radius, length(d)));
    }
    COLOR = vec4(0.02, 0.01, 0.05, 0.9 * (1.0 - light));
}";

    private readonly IReadOnlyList<PlayerFighter> _players;
    private ShaderMaterial _mat = null!;

    public LightsOut(IReadOnlyList<PlayerFighter> players)
    {
        _players = players;
        Layer = -1;         // over the 3D scene, under every HUD layer
    }

    public override void _Ready()
    {
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        var dark = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
        dark.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dark);
    }

    public override void _Process(double delta)
    {
        Camera3D? cam = GetViewport().GetCamera3D();
        if (cam is null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        var centres = new Vector2[4];
        int n = 0;
        foreach (PlayerFighter p in _players)
        {
            if (n >= 4 || !IsInstanceValid(p) || p.IsDead) continue;
            Vector3 chest = p.GlobalPosition + new Vector3(0f, 0.9f, 0f);
            if (cam.IsPositionBehind(chest)) continue;
            centres[n++] = cam.UnprojectPosition(chest);
        }
        _mat.SetShaderParameter("centres", centres);
        _mat.SetShaderParameter("count", n);
        _mat.SetShaderParameter("screen", size);
        _mat.SetShaderParameter("radius", size.Y * 0.34f);
    }
}
