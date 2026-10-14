// Pixel sprites lit by real lights through a normal map: the Children of Morta /
// Sea of Stars recipe. The art keeps its own baked shading and crisp pixels;
// scene lights add smooth, full-resolution light on top, turning across the
// form the way the art says it turns, with a rim of light on the outline.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_lambert_wrap, specular_disabled;

uniform sampler2D albedo_tex : source_color, filter_nearest;
uniform sampler2D normal_tex : hint_normal, filter_nearest;
uniform vec4 tint = vec4(1.0);          // per-fighter colour, may exceed 1 (charge glow)
uniform float flash = 0.0;              // hit flash
uniform bool flip = false;              // mirrored for leftward facing
uniform float self_light = 0.42;        // how much the art shows by its own baked light

void fragment() {
	vec4 c = texture(albedo_tex, UV);
	if (c.a < 0.5) discard;
	vec3 col = c.rgb * tint.rgb;
	ALBEDO = col;
	vec3 n = texture(normal_tex, UV).rgb;
	if (flip) n.x = 1.0 - n.x;
	NORMAL_MAP = n;
	NORMAL_MAP_DEPTH = 1.0;
	ROUGHNESS = 1.0;
	// A floor of self-illumination keeps the painted shading readable in shadow;
	// the scene's lights then build on it rather than replacing it.
	EMISSION = col * self_light + vec3(flash);
}
