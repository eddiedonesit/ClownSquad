// A comic-book panel background: speed rays bursting from a centre and a
// halftone dot screen whose dots swell toward the panel's edges. Works in the
// polygon's own coordinates, so the pattern travels with a sliding panel.
shader_type canvas_item;

uniform vec4 base : source_color = vec4(0.3, 0.45, 0.9, 1.0);
uniform vec4 ink : source_color = vec4(0.1, 0.05, 0.15, 1.0);
uniform vec2 centre = vec2(0.0, 0.0);
uniform float cell = 13.0;
uniform float reach = 520.0;

varying vec2 local;

void vertex() {
	local = VERTEX;
}

void fragment() {
	vec2 d = local - centre;
	float ang = atan(d.y, d.x);
	float rays = step(0.5, fract(ang * 22.0 / 6.2831853 + TIME * 0.12));
	vec3 col = mix(base.rgb * 0.92, min(base.rgb * 1.22 + 0.06, vec3(1.0)), rays);
	vec2 g = mod(local, cell) - cell * 0.5;
	float r = cell * 0.5 * clamp(length(d) / reach, 0.08, 0.92);
	float dotm = 1.0 - smoothstep(r - 0.8, r + 0.2, length(g));
	col = mix(col, ink.rgb, dotm * 0.42);
	COLOR = vec4(col, 1.0);
}
