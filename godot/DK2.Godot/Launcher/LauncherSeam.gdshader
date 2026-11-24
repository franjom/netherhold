// The launcher's seam: a jagged crack down the right edge, from the top to the Play button's edge. It smoulders dim until
// an install is found; then lava runs down it once, `progress` going from 0 to 1 along the crack's length.
shader_type canvas_item;

const int MAX_POINTS = 16;

uniform vec2 points[MAX_POINTS];
uniform int point_count = 2;
uniform vec2 size = vec2(184.0, 680.0);
uniform float progress = 0.0;
uniform vec4 earth : source_color = vec4(0.118, 0.090, 0.078, 1.0);
uniform vec4 lava : source_color = vec4(1.0, 0.478, 0.102, 1.0);

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	vec2 p = UV * size;
	// The crack's edge wobbles between its points, so the straight segments read as split rock.
	vec2 q = p + vec2((noise(vec2(p.y / 5.0, 1.3)) - 0.5) * 3.5, 0.0);
	// The nearest point of the crack: its distance and its place along the crack's length.
	float nearest = 1e9;
	float along = 0.0;
	float total = 0.0;
	for (int i = 0; i < MAX_POINTS - 1; i++) {
		if (i + 1 >= point_count) {
			break;
		}
		vec2 a = points[i];
		vec2 ab = points[i + 1] - a;
		float len = length(ab);
		float t = clamp(dot(q - a, ab) / max(len * len, 1e-4), 0.0, 1.0);
		float d = length(q - a - ab * t);
		if (d < nearest) {
			nearest = d;
			along = total + t * len;
		}
		total += len;
	}
	float s = along / max(total, 1.0);
	float pixelsAlong = along;

	// Rock: the window's earth, darker towards the crack and grained.
	float grain = noise(p / 3.0) * 0.4 + noise(p / 11.0) * 0.35 + noise(p / 37.0) * 0.25;
	vec3 colour = earth.rgb * (0.78 + 0.3 * grain) * (0.82 + 0.18 * smoothstep(0.0, 28.0, nearest));
	// The rock fades into the window's plain earth at the column's left edge, so the column has no hard border.
	colour = mix(earth.rgb, colour, smoothstep(0.0, 36.0, p.x));

	// The crack's width wanders along its length; its edge is anti-aliased over a pixel.
	float width = 1.1 + 2.2 * noise(vec2(pixelsAlong / 9.0, 7.0));
	float core = 1.0 - smoothstep(width * 0.5, width * 0.5 + 1.0, nearest);
	colour = mix(colour, vec3(0.03, 0.018, 0.012), core);

	// Lava fills the crack up to the front; past it the crack only smoulders.
	float lit = 1.0 - smoothstep(progress - 0.015, progress, s);
	if (progress >= 1.0) {
		lit = 1.0;
	}
	float ember = 0.16 + 0.12 * noise(vec2(pixelsAlong / 6.0, 3.0));
	float heat = 0.75 + 0.35 * noise(vec2(pixelsAlong / 14.0, 11.0));
	vec3 hot = mix(lava.rgb, vec3(1.0, 0.86, 0.6), 0.45 * (1.0 - smoothstep(0.0, width * 0.5, nearest)));
	vec3 crack = mix(lava.rgb * ember, hot * heat, lit);
	colour += crack * core;

	// The glow around the crack, wider where it is lit, brightest at the running front.
	float halo = exp(-nearest / mix(5.0, 13.0, lit));
	float front = progress > 0.0 && progress < 1.0 ? exp(-abs(s - progress) * total / 10.0) : 0.0;
	colour += lava.rgb * halo * (1.0 - core) * mix(ember * 0.55, 0.42 * heat, lit);
	colour += vec3(1.0, 0.8, 0.5) * front * exp(-nearest / 7.0) * 0.6;
	COLOR = vec4(colour, 1.0);
}
