// Dirt over a machine (its meshes' material overlay, see WashableView): mud splashed thick near the ground and dust on
// what faces up, in blotches, as dirty as the machine's washable says. Wet weather darkens it and makes it shine.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back;

/// 0 clean … 1 caked.
uniform float dirt : hint_range(0.0, 1.0) = 0.0;
/// The world height of the machine's origin, on the ground: the mud is thickest near it.
uniform float ground = 0.0;
uniform vec3 mud : source_color = vec3(0.23, 0.18, 0.13);
uniform vec3 dust : source_color = vec3(0.47, 0.42, 0.34);

global uniform float g_wetness;

varying vec3 wpos;
varying vec3 wnormal;

float hash(vec3 p) {
	vec3 q = fract(p * 0.3183099 + 0.1) * 17.0;
	return fract(q.x * q.y * q.z * (q.x + q.y + q.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1.0, 0.0, 0.0)), f.x), mix(hash(i + vec3(0.0, 1.0, 0.0)), hash(i + vec3(1.0, 1.0, 0.0)), f.x), f.y),
			mix(mix(hash(i + vec3(0.0, 0.0, 1.0)), hash(i + vec3(1.0, 0.0, 1.0)), f.x), mix(hash(i + vec3(0.0, 1.0, 1.0)), hash(i + vec3(1.0, 1.0, 1.0)), f.x), f.y), f.z);
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnormal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	// Splashed up from the wheels, thinning out by 2 m up; dust settling on what faces up.
	float low = 1.0 - smoothstep(0.2, 2.2, wpos.y - ground);
	float up = smoothstep(0.5, 0.95, wnormal.y);
	float blotches = vnoise(wpos * 2.5) * 0.6 + vnoise(wpos * 9.0) * 0.4;
	float cover = clamp(dirt * 1.7 * (low * 0.8 + up * 0.35 + 0.12) - (1.0 - blotches) * 0.65, 0.0, 1.0);
	float wet = clamp(g_wetness, 0.0, 1.0);
	ALBEDO = mix(dust, mud, low) * (1.0 - 0.35 * wet);
	ROUGHNESS = mix(1.0, 0.45, wet);
	ALPHA = cover * 0.92;
}
