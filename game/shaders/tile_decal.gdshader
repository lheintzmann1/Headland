// Road / track / path / stream decals laid on the terrain (atlases from tools/convert_tiles.py).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform int kind = 0; // 0 = asphalt, 1 = dirt/path, 2 = water

global uniform float g_wetness;
global uniform float g_snow;
global uniform float g_cloud_cover;
global uniform vec2 g_cloud_offset;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec4 c = texture(atlas, UV);
	vec3 col = c.rgb;
	float rough = 0.85;
	vec2 p = wpos.xz;

	if (kind == 0) {
		// Asphalt: wet roads darken and turn glossy.
		col *= 1.0 - 0.35 * g_wetness;
		rough = mix(0.8, 0.12, g_wetness);
		col = mix(col, vec3(0.86, 0.88, 0.9), g_snow * 0.45);
	} else if (kind == 1) {
		col *= 1.0 - 0.3 * g_wetness;
		rough = mix(0.9, 0.4, g_wetness);
		col = mix(col, vec3(0.88, 0.9, 0.92), g_snow * 0.8);
	} else {
		// Water: darker, glossy, with slow ripples in the normal.
		float t = TIME * 0.25;
		float n1 = vnoise(p * 1.3 + vec2(t, t * 0.7));
		float n2 = vnoise(p * 2.1 - vec2(t * 0.8, t));
		NORMAL_MAP = normalize(vec3((n1 - 0.5) * 0.6, (n2 - 0.5) * 0.6, 1.0)) * 0.5 + 0.5;
		col *= 0.55;
		rough = 0.06;
		SPECULAR = 0.6;
	}

	float cl = vnoise((p + g_cloud_offset) / 90.0 * 2.0);
	float broken = 4.0 * g_cloud_cover * (1.0 - g_cloud_cover);
	col *= 1.0 - broken * 0.22 * smoothstep(0.45, 0.72, cl);

	ALBEDO = col;
	ALPHA = c.a;
	ROUGHNESS = rough;
}
