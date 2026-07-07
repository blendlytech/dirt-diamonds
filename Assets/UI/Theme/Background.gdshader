shader_type canvas_item;

// Phase 10+ presentation backdrop: a deep-night gradient with two slow
// drifting glow fields (diamond teal low-left, dirt ember high-right), a
// soft vignette, and fine animated grain. Entirely procedural — no raster
// art ships, so the Steam AI-art posture is unchanged. Sits on a ColorRect
// behind ScreenContainer in Main.tscn with mouse_filter = Ignore.

uniform vec3 base_top : source_color = vec3(0.051, 0.043, 0.075);
uniform vec3 base_bottom : source_color = vec3(0.02, 0.016, 0.031);
uniform vec3 glow_a_color : source_color = vec3(0.325, 0.78, 0.643);
uniform vec3 glow_b_color : source_color = vec3(0.788, 0.416, 0.231);
uniform float glow_strength : hint_range(0.0, 0.3) = 0.085;
uniform float grain_strength : hint_range(0.0, 0.1) = 0.025;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.4;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void fragment() {
	vec2 uv = UV;
	vec3 col = mix(base_top, base_bottom, uv.y);

	// Two soft radial glows on slow, incommensurate drift periods so the
	// composition never visibly repeats.
	vec2 glow_a_center = vec2(0.2 + 0.06 * sin(TIME * 0.11), 0.75 + 0.05 * cos(TIME * 0.07));
	vec2 glow_b_center = vec2(0.82 + 0.05 * cos(TIME * 0.09), 0.18 + 0.06 * sin(TIME * 0.13));
	float glow_a = smoothstep(0.62, 0.0, distance(uv, glow_a_center));
	float glow_b = smoothstep(0.55, 0.0, distance(uv, glow_b_center));
	col += glow_a_color * glow_a * glow_strength;
	col += glow_b_color * glow_b * glow_strength * 0.75;

	// Vignette pulls the eye to the panels.
	float vin = distance(uv, vec2(0.5, 0.5));
	col *= 1.0 - vignette_strength * smoothstep(0.35, 0.85, vin);

	// Fine animated grain kills banding on the dark gradient.
	float grain = hash21(uv * vec2(1920.0, 1080.0) + vec2(fract(TIME * 7.0) * 43.7, 0.0));
	col += (grain - 0.5) * grain_strength;

	COLOR = vec4(col, 1.0);
}
