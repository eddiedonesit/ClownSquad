namespace ClownSquad.Visuals;

using Godot;

/// <summary>
/// Material for a pixel sprite lit through its normal map (assets/shaders/
/// lit_sprite.gdshader). A sheet opts in by having a normal map beside it,
/// named sheet_n.png; the HD generators write one for every sheet. Sheets
/// without one (the classic art) keep Godot's default sprite material.
/// </summary>
public static class LitSprite
{
    private const string ShaderPath = "res://assets/shaders/lit_sprite.gdshader";
    private static Shader? _shader;

    public static ShaderMaterial? For(string sheetPath)
    {
        string normalPath = sheetPath.Replace(".png", "_n.png");
        if (!ResourceLoader.Exists(normalPath)) return null;
        _shader ??= GD.Load<Shader>(ShaderPath);
        var m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("albedo_tex", GD.Load<Texture2D>(sheetPath));
        m.SetShaderParameter("normal_tex", GD.Load<Texture2D>(normalPath));
        return m;
    }

    /// <summary>Push colour, hit flash and facing to a lit sprite's material.</summary>
    public static void Apply(ShaderMaterial m, Color tint, float flash, bool flipped)
    {
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("flash", flash);
        m.SetShaderParameter("flip", flipped);
    }
}
