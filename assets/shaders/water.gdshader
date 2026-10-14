// The sea around the world-map islands: deep colour far from land, shallow
// turquoise over the shelves, a band of foam where the water meets the shore,
// and small rolling waves catching the sun. Shore distance comes from the
// depth buffer, so every island's coastline gets foam with no extra data.
shader_type spatial;
render_mode cull_disabled, depth_draw_always, specular_schlick_ggx;

uniform sampler2D depth_tex : hint_depth_texture, filter_linear_mipmap;
uniform vec4 deep : source_color = vec4(0.07, 0.22, 0.45, 1.0);
uniform vec4 shallow : source_color = vec4(0.25, 0.75, 0.85, 1.0);
uniform vec4 foam : source_color = vec4(0.95, 0.98, 1.0, 1.0);
uniform float depth_range = 1.6;
uniform float foam_range = 0.22;
uniform float wave_scale = 1.4;

float wave(vec2 p, float t) {
	return sin(p.x * 1.3 + t * 1.1) * 0.5 + sin(p.y * 1.7 - t * 0.9) * 0.35 + sin((p.x + p.y) * 2.9 + t * 1.7) * 0.15;
}

void vertex() {
	float t = TIME;
	VERTEX.y += wave(VERTEX.xz * 0.6, t) * 0.04;
}

void fragment() {
	float t = TIME;
	vec2 p = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz * wave_scale;
	// How far below the surface the ground behind this pixel is.
	float scene_depth = texture(depth_tex, SCREEN_UV).r;
	vec4 world = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, scene_depth, 1.0);
	world.xyz /= world.w;
	float under = clamp((VERTEX.z - world.z) / depth_range, 0.0, 1.0);

	vec3 col = mix(shallow.rgb, deep.rgb, smoothstep(0.0, 1.0, under));
	// Foam: a bright frothy band along the shore, broken up so it shimmers.
	float edge = 1.0 - clamp((VERTEX.z - world.z) / foam_range, 0.0, 1.0);
	float froth = step(0.55, fract(sin(dot(floor(p * 9.0), vec2(12.9898, 78.233))) * 43758.5453) * 0.5 + edge + 0.15 * sin(t * 3.0 + p.x * 4.0));
	col = mix(col, foam.rgb, clamp(edge * froth, 0.0, 1.0));

	float hx = wave(p + vec2(0.05, 0.0), t) - wave(p - vec2(0.05, 0.0), t);
	float hz = wave(p + vec2(0.0, 0.05), t) - wave(p - vec2(0.0, 0.05), t);
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(vec3(-hx * 0.6, 1.0, -hz * 0.6)), 0.0)).xyz);
	ALBEDO = col;
	ROUGHNESS = 0.12;
	METALLIC = 0.0;
	SPECULAR = 0.6;
}
