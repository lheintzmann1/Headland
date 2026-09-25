// Crop clumps (MultiMesh of crossed alpha cards). INSTANCE_CUSTOM = (atlas column, atlas row, health 0..1, random 0..1).
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform sampler2D atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform float alpha_cut = 0.45;
uniform vec2 atlas_grid = vec2(8.0, 4.0);

global uniform float g_wind;
global uniform float g_wetness;
global uniform float g_snow;
global uniform float g_cloud_cover;
global uniform vec2 g_cloud_offset;

varying vec4 custom;
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
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Gusts roll across the field; tips move most.
	float gust = vnoise(wp.xz * 0.05 + vec2(TIME * 0.35 * (0.5 + g_wind), 0.0));
	float t = TIME * (1.2 + g_wind * 2.0) + wp.x * 0.35 + wp.z * 0.27 + custom.w * 6.28;
	float sway = (sin(t) * 0.6 + sin(t * 2.3) * 0.25) * (0.02 + g_wind * 0.1) * (0.5 + gust);
	float tip = pow(1.0 - UV.y, 2.0);
	VERTEX.x += sway * tip;
	VERTEX.z += sway * 0.6 * tip;
	wpos = wp;
}

void fragment() {
	vec2 uv = (vec2(custom.x, custom.y) + UV) / atlas_grid;
	vec4 c = texture(atlas, uv);

	// Mip-aware alpha: keep thin stems from vanishing when zoomed out.
	vec2 sz = vec2(textureSize(atlas, 0));
	vec2 dx = dFdx(uv * sz);
	vec2 dy = dFdy(uv * sz);
	float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-6));
	float a = c.a * (1.0 + max(lod, 0.0) * 0.35);

	// Stress (drought, frost, nitrogen) turns leaves yellow-brown.
	float health = custom.z;
	vec3 col = c.rgb;
	vec3 stressed = vec3(dot(col, vec3(0.4, 0.45, 0.15))) * vec3(1.12, 0.96, 0.62);
	col = mix(stressed, col, smoothstep(0.25, 0.9, health));
	col *= 0.88 + 0.24 * custom.w;
	col *= 1.0 - 0.12 * g_wetness;
	col = mix(col, vec3(0.9, 0.92, 0.94), g_snow * 0.5 * (1.0 - UV.y));

	float cl = vnoise((wpos.xz + g_cloud_offset) / 90.0 * 2.0);
	float broken = 4.0 * g_cloud_cover * (1.0 - g_cloud_cover);
	col *= 1.0 - broken * 0.22 * smoothstep(0.45, 0.72, cl);

	ALBEDO = col;
	ALPHA = a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	ROUGHNESS = 0.85;
	// Light foliage like a soft canopy rather than flat cards.
	NORMAL = normalize(mix(NORMAL, (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz, 0.6));
	BACKLIGHT = vec3(0.3, 0.32, 0.2);
}
