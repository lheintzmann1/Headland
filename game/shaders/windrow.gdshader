// What lies cut on the fields (MultiMesh ridges). INSTANCE_CUSTOM = (fill type color r, g, b, random 0..1).
// Stalks run along the ridge (its local z), so a windrow reads as cut grass or straw rather than a heap.
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

global uniform float g_wetness;
global uniform float g_snow;
global uniform float g_cloud_cover;
global uniform vec2 g_cloud_offset;

varying vec4 custom;
varying vec3 local;
varying vec3 wpos;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
	custom = INSTANCE_CUSTOM;
	local = VERTEX;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// Stalks: thin streaks along the ridge, broken up, a few darker and lighter.
	vec2 p = vec2(local.x * 34.0 + custom.w * 17.0, local.z * 3.0);
	float stalks = vnoise(p) * 0.6 + vnoise(p * vec2(2.3, 1.7) + 5.1) * 0.4;
	vec3 col = custom.rgb * (0.72 + 0.5 * stalks);
	// Lower down the ridge lies in its own shade.
	col *= mix(0.7, 1.0, smoothstep(0.0, 0.8, local.y));
	col *= 0.92 + 0.16 * custom.w;
	col *= 1.0 - 0.25 * g_wetness;
	col = mix(col, vec3(0.9, 0.92, 0.94), g_snow * 0.7);

	float cl = vnoise((wpos.xz + g_cloud_offset) / 90.0 * 2.0);
	float broken = 4.0 * g_cloud_cover * (1.0 - g_cloud_cover);
	col *= 1.0 - broken * 0.22 * smoothstep(0.45, 0.72, cl);

	ALBEDO = col;
	ROUGHNESS = mix(0.95, 0.5, g_wetness);
	BACKLIGHT = vec3(0.15);
}
