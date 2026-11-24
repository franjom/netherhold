// The launcher's hewn-stone fields: the panel's own colour, grained by two scales of value noise in screen pixels.
shader_type canvas_item;

uniform float grain = 0.2;

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
	vec2 p = FRAGCOORD.xy;
	float n = noise(p / 2.5) * 0.35 + noise(p / 9.0) * 0.4 + noise(p / 31.0) * 0.25;
	COLOR.rgb *= 1.0 - grain * 0.5 + grain * n;
}
