// Screen-space precipitation and fog drawn over the 3D view (under the HUD).
shader_type canvas_item;

uniform float rain = 0.0;
uniform float snow = 0.0;
uniform float fog = 0.0;
uniform float wind = 0.3;

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
	vec2 uv = SCREEN_UV;
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec4 col = vec4(0.0);

	if (rain > 0.01) {
		for (int l = 0; l < 3; l++) {
			float fl = float(l);
			vec2 p = vec2(uv.x * aspect, uv.y);
			p.x += p.y * wind * 0.4;
			p *= vec2(70.0 + fl * 40.0, 7.0 + fl * 3.0);
			p.y -= TIME * (9.0 + fl * 4.0);
			vec2 cell = floor(p);
			vec2 f = fract(p);
			float h = hash(cell + fl * 17.0);
			float on = step(1.0 - rain * 0.45, h);
			float x = abs(f.x - fract(h * 7.3));
			float streak = smoothstep(0.07, 0.0, x) * smoothstep(0.0, 0.3, f.y) * smoothstep(1.0, 0.5, f.y);
			col += vec4(0.78, 0.82, 0.88, 0.28 - fl * 0.06) * on * streak;
		}
	}

	if (snow > 0.01) {
		for (int l = 0; l < 3; l++) {
			float fl = float(l);
			vec2 p = vec2(uv.x * aspect, uv.y) * (14.0 + fl * 9.0);
			p.y -= TIME * (0.6 + fl * 0.3);
			p.x += sin(TIME * 0.7 + p.y * 0.8 + fl) * 0.3 + TIME * wind * 0.5;
			vec2 cell = floor(p);
			vec2 f = fract(p) - 0.5;
			float h = hash(cell + fl * 31.0);
			float on = step(1.0 - snow * 0.6, h);
			vec2 off = vec2(hash(cell + 3.1), hash(cell + 7.7)) - 0.5;
			float d = length(f - off * 0.6);
			float flake = smoothstep(0.09 - fl * 0.02, 0.0, d);
			col += vec4(1.0, 1.0, 1.0, 0.8) * on * flake;
		}
	}

	if (fog > 0.01) {
		float n = vnoise(vec2(uv.x * aspect, uv.y) * 3.0 + vec2(TIME * 0.02, 0.0)) * 0.6
			+ vnoise(vec2(uv.x * aspect, uv.y) * 7.0 - vec2(TIME * 0.03, 0.0)) * 0.4;
		float a = fog * (0.25 + 0.3 * n);
		col = mix(col, vec4(0.78, 0.8, 0.8, 1.0), a);
		col.a = max(col.a, a);
	}

	COLOR = col;
}
