// Heaps on the ground (HeapRenderer): COLOR is the fill type's color. Grain is fine speckles of lighter and darker
// kernels, matte, darker where the rain wet it.
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

global uniform float g_wetness;
global uniform float g_snow;
global uniform float g_cloud_cover;
global uniform vec2 g_cloud_offset;

varying vec3 wpos;
varying vec3 wnormal;
varying vec3 col;

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
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnormal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	col = COLOR.rgb;
}

void fragment() {
	// Kernels: speckles over a slower mottling, on the slope as on the top (x and z mixed with the height).
	vec2 p = wpos.xz + vec2(wpos.y * 0.7, -wpos.y * 0.5);
	float speckle = hash(floor(p * 55.0));
	float mottle = vnoise(p * 2.3) * 0.6 + vnoise(p * 7.1 + 3.7) * 0.4;
	vec3 c = col * (0.78 + 0.22 * mottle) * (0.9 + 0.2 * speckle);
	c *= 1.0 - 0.3 * g_wetness;
	c = mix(c, vec3(0.9, 0.92, 0.94), g_snow * smoothstep(0.4, 0.9, normalize(wnormal).y) * 0.6);

	float cl = vnoise((wpos.xz + g_cloud_offset) / 90.0 * 2.0);
	float broken = 4.0 * g_cloud_cover * (1.0 - g_cloud_cover);
	c *= 1.0 - broken * 0.22 * smoothstep(0.45, 0.72, cl);

	ALBEDO = c;
	ROUGHNESS = mix(0.95, 0.6, g_wetness);
}
