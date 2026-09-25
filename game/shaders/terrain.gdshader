// Terrain: blends ground materials from a per-chunk data texture written by FarmSim.Core
// (WorldMap.FillChunkTexture): R = ground | soil << 4, G = moisture, B = work angle, A = crop cover.
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

// Layers of terrain_albedo / terrain_normal (tools/fetch_textures.py).
const int L_GRASS = 0;
const int L_MEADOW = 1;
const int L_SOIL = 2;
const int L_RESIDUE = 3;
const int L_GRAVEL = 4;
const int L_SNOW = 5;

// FarmSim.Core.World.GroundType
const int G_GRASS = 0;
const int G_CULTIVATED = 1;
const int G_SEEDED = 2;
const int G_STUBBLE = 3;
const int G_PLOWED = 4;
const int G_ROAD = 5;
const int G_YARD = 6;
const int G_DIRT = 7;
const int G_FOREST = 8;
const int G_WATER = 9;

uniform sampler2D data_tex : filter_nearest, repeat_disable;
uniform vec2 chunk_origin;
uniform float cell_size = 0.5;
uniform sampler2DArray albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_tex : filter_linear_mipmap_anisotropic, repeat_enable;

global uniform float g_wetness;
global uniform float g_snow;
global uniform float g_cloud_cover;
global uniform vec2 g_cloud_offset;
global uniform float g_season_green;

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

float fbm(vec2 p) {
	float s = 0.0;
	float a = 0.5;
	for (int i = 0; i < 3; i++) {
		s += a * vnoise(p);
		p *= 2.03;
		a *= 0.5;
	}
	return s / 0.875;
}

vec3 alb(int l, vec2 uv) {
	return texture(albedo_tex, vec3(uv, float(l))).rgb;
}

vec3 nrm(int l, vec2 uv) {
	return texture(normal_tex, vec3(uv, float(l))).rgb;
}

// Periodic profile across the work direction (rows run along the heading). 1 = ridge, 0 = groove.
float rows(vec2 p, float angle, float period) {
	vec2 across = vec2(cos(angle), -sin(angle));
	return 0.5 + 0.5 * cos(dot(p, across) / period * 6.2831853);
}

// Returns albedo in rgb and roughness in a; normal via out.
vec4 surface(int g, vec2 p, float angle, float moist, float cover, float macro, out vec3 n) {
	vec3 a;
	float rough = 0.95;
	float soil_like = 1.0;
	if (g == G_GRASS) {
		float m = smoothstep(0.3, 0.8, macro) * 0.65;
		a = mix(alb(L_GRASS, p / 3.0), alb(L_MEADOW, p / 4.5), m);
		n = mix(nrm(L_GRASS, p / 3.0), nrm(L_MEADOW, p / 4.5), m);
		// Dormant/dry season: olive-brown grass.
		a = mix(a * vec3(1.05, 0.92, 0.66), a, g_season_green);
		soil_like = 0.3;
	} else if (g == G_CULTIVATED) {
		float r = rows(p, angle, 0.33);
		a = alb(L_RESIDUE, p / 2.5) * mix(0.68, 1.06, r);
		n = nrm(L_RESIDUE, p / 2.5);
	} else if (g == G_SEEDED) {
		float r = rows(p, angle, 0.125);
		a = alb(L_SOIL, p / 2.5) * mix(0.8, 1.03, r);
		n = nrm(L_SOIL, p / 2.5);
	} else if (g == G_STUBBLE) {
		float r = rows(p, angle, 0.18);
		vec3 straw = vec3(0.62, 0.54, 0.37) * (0.8 + 0.35 * alb(L_MEADOW, p / 3.0).r);
		a = mix(alb(L_RESIDUE, p / 2.5), straw, 0.5 + 0.3 * r);
		n = nrm(L_RESIDUE, p / 2.5);
	} else if (g == G_PLOWED) {
		float r = rows(p, angle, 0.45);
		a = alb(L_SOIL, p / 2.0) * mix(0.55, 1.0, r);
		n = nrm(L_SOIL, p / 2.0);
	} else if (g == G_ROAD) {
		a = alb(L_GRAVEL, p / 2.0) * 0.7;
		n = nrm(L_GRAVEL, p / 2.0);
		soil_like = 0.2;
	} else if (g == G_YARD) {
		a = alb(L_GRAVEL, p / 2.5) * 0.88;
		n = nrm(L_GRAVEL, p / 2.5);
		soil_like = 0.2;
	} else if (g == G_DIRT) {
		a = mix(alb(L_RESIDUE, p / 2.5), alb(L_GRAVEL, p / 2.0), 0.35) * 1.05;
		n = nrm(L_RESIDUE, p / 2.5);
	} else if (g == G_FOREST) {
		a = mix(alb(L_MEADOW, p / 4.0), alb(L_RESIDUE, p / 3.0), 0.5) * 0.72;
		n = nrm(L_MEADOW, p / 4.0);
		soil_like = 0.6;
	} else {
		a = vec3(0.09, 0.12, 0.11);
		n = vec3(0.5, 0.5, 1.0);
		rough = 0.1;
		soil_like = 0.0;
	}
	// Moist soil is darker.
	a *= mix(1.0, 0.62, smoothstep(0.3, 0.9, moist) * soil_like);
	// Shade under the crop canopy.
	a *= 1.0 - 0.3 * cover;
	return vec4(a, rough);
}

void decode(vec4 d, out int g, out float moist, out float angle, out float cover) {
	int code = int(d.r * 255.0 + 0.5);
	g = code & 15;
	moist = d.g;
	angle = d.b * 3.14159265;
	cover = d.a;
}

void fragment() {
	vec2 p = wpos.xz;
	// Texel space of the padded (66x66) data texture, shifted for bilinear weights,
	// with a little noise so natural boundaries are not ruler-straight.
	vec2 c = (p - chunk_origin) / cell_size + 0.5;
	c += (vec2(vnoise(p * 1.7), vnoise(p * 1.7 + 13.1)) - 0.5) * 0.3;
	vec2 fl = floor(c);
	vec2 f = smoothstep(0.2, 0.8, c - fl);
	ivec2 i0 = clamp(ivec2(fl), ivec2(0), ivec2(64));

	int g00; int g10; int g01; int g11;
	float m00; float m10; float m01; float m11;
	float a00; float a10; float a01; float a11;
	float c00; float c10; float c01; float c11;
	decode(texelFetch(data_tex, i0, 0), g00, m00, a00, c00);
	decode(texelFetch(data_tex, i0 + ivec2(1, 0), 0), g10, m10, a10, c10);
	decode(texelFetch(data_tex, i0 + ivec2(0, 1), 0), g01, m01, a01, c01);
	decode(texelFetch(data_tex, i0 + ivec2(1, 1), 0), g11, m11, a11, c11);

	float w00 = (1.0 - f.x) * (1.0 - f.y);
	float w10 = f.x * (1.0 - f.y);
	float w01 = (1.0 - f.x) * f.y;
	float w11 = f.x * f.y;
	float macro = fbm(p / 23.0);

	vec4 s;
	vec3 n;
	if (g00 == g10 && g00 == g01 && g00 == g11) {
		float moist = m00 * w00 + m10 * w10 + m01 * w01 + m11 * w11;
		float cover = c00 * w00 + c10 * w10 + c01 * w01 + c11 * w11;
		s = surface(g00, p, a00, moist, cover, macro, n);
	} else {
		vec3 n00; vec3 n10; vec3 n01; vec3 n11;
		vec4 s00 = surface(g00, p, a00, m00, c00, macro, n00);
		vec4 s10 = surface(g10, p, a10, m10, c10, macro, n10);
		vec4 s01 = surface(g01, p, a01, m01, c01, macro, n01);
		vec4 s11 = surface(g11, p, a11, m11, c11, macro, n11);
		s = s00 * w00 + s10 * w10 + s01 * w01 + s11 * w11;
		n = n00 * w00 + n10 * w10 + n01 * w01 + n11 * w11;
	}

	vec3 albedo = s.rgb * (0.92 + 0.16 * macro);
	float rough = s.a;

	// Surface wetness after rain: darker and shinier.
	albedo *= 1.0 - 0.25 * g_wetness;
	rough = mix(rough, 0.35, g_wetness * 0.75);

	// Snow cover, patchy where thin.
	float snow = clamp(g_snow * 1.5 - fbm(p / 5.0) * 0.6, 0.0, 1.0);
	albedo = mix(albedo, alb(L_SNOW, p / 3.0), snow);
	n = mix(n, nrm(L_SNOW, p / 3.0), snow);
	rough = mix(rough, 0.7, snow);

	// Drifting cloud shadows.
	float cl = fbm((p + g_cloud_offset) / 90.0);
	float broken = 4.0 * g_cloud_cover * (1.0 - g_cloud_cover);
	albedo *= 1.0 - broken * 0.22 * smoothstep(0.45, 0.72, cl);

	ALBEDO = albedo;
	ROUGHNESS = rough;
	NORMAL_MAP = n;
	NORMAL_MAP_DEPTH = 0.9;
}
