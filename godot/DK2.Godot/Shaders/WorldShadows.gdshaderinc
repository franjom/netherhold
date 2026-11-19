// The torch shadow maps and their lookup (WorldScene.Shadows.cs, --light-shadows): a receiver's distance from the torch
// against the nearest caster's in the view's texel toward it (2x2 taps, bilinear), darkened by that caster's strength:
// the terrain's on that torch's term, a creature's or object's on the whole light. DK2_SHADOW_SIZE is
// WorldScene.ShadowMapSize, DK2_SHADOW_RANGE ShadowRange and DK2_SHADOW_TAN tan(ShadowFieldOfView / 2), held to them by
// ClassicVertexLightShaderTextTests.
global uniform sampler2D dk2_shadow_map0;
global uniform sampler2D dk2_shadow_map1;
global uniform vec4 dk2_shadow_light0;
global uniform vec4 dk2_shadow_light1;
const float DK2_SHADOW_RANGE = 4.0;
const float DK2_SHADOW_TAN = 3.7320508;
const int DK2_SHADOW_SIZE = 512;
vec4 dk2_shadow_light(int t) {
    return t == 0 ? dk2_shadow_light0 : dk2_shadow_light1;
}
vec3 dk2_shadow_texel(int t, ivec2 at) {
    if (t == 0) return texelFetch(dk2_shadow_map0, at, 0).rgb;
    return texelFetch(dk2_shadow_map1, at, 0).rgb;
}
// The occluding strength, split: x the terrain's (it takes the torch's own term), y a creature's or object's
// (it darkens the whole light, as DK2's ZERO/INVSRCCOLOR receiver does).
vec2 dk2_shadow_tap(int t, ivec2 at, float reach) {
    vec3 s = dk2_shadow_texel(t, clamp(at, ivec2(0), ivec2(DK2_SHADOW_SIZE - 1)));
    if (!(s.r * DK2_SHADOW_RANGE < reach)) return vec2(0.0);
    return s.b > 0.5 ? vec2(0.0, s.g) : vec2(s.g, 0.0);
}
vec2 dk2_shadow_sample(int t, vec3 l, vec3 p, vec3 n) {
    vec3 r = p - l;
    if (r.y > 0.0 || dot(r, r) >= DK2_SHADOW_RANGE * DK2_SHADOW_RANGE) return vec2(1.0);
    // Normal offset and slope bias by the texel's size at the receiver: a surface the light grazes covers many
    // distances in one texel. The normal is turned toward the light (DK2's terrain tops carry inverted ones).
    vec3 to_light = normalize(l - p);
    vec3 facing = dot(n, to_light) < 0.0 ? -n : n;
    float cosine = max(dot(facing, to_light), 0.1);
    float texel = length(p - l) * 2.0 * DK2_SHADOW_TAN / float(DK2_SHADOW_SIZE);
    vec3 d = p + facing * (1.5 * texel) - l;
    float len = length(d);
    if (len >= DK2_SHADOW_RANGE || d.y > -0.0001) return vec2(1.0);
    vec2 ndc = d.xz / (-d.y * DK2_SHADOW_TAN);
    if (any(greaterThan(abs(ndc), vec2(1.0)))) return vec2(1.0);
    vec2 at = (ndc * 0.5 + 0.5) * float(DK2_SHADOW_SIZE) - 0.5;
    ivec2 base = ivec2(floor(at));
    vec2 f = at - vec2(base);
    float reach = len - (0.02 + texel * (1.0 + min(sqrt(1.0 - cosine * cosine) / cosine, 8.0)));
    vec2 shade = mix(mix(dk2_shadow_tap(t, base, reach), dk2_shadow_tap(t, base + ivec2(1, 0), reach), f.x),
        mix(dk2_shadow_tap(t, base + ivec2(0, 1), reach), dk2_shadow_tap(t, base + ivec2(1, 1), reach), f.x), f.y);
    return 1.0 - shade;
}
// Each map sampled once a pixel: the terrain's visibility per slot, and in caster the creature and object
// shadows of every shadowed torch over the packed light (DK2's 1 - coverage x strength).
vec4 dk2_shadow_all(vec3 p, vec3 n, out float caster) {
    // Written out per slot, so each sample's map is known when the shader compiles.
    vec2 v0 = dk2_shadow_light0.w > 0.0 ? dk2_shadow_sample(0, dk2_shadow_light0.xyz, p, n) : vec2(1.0);
    vec2 v1 = dk2_shadow_light1.w > 0.0 ? dk2_shadow_sample(1, dk2_shadow_light1.xyz, p, n) : vec2(1.0);
    caster = v0.y * v1.y;
    return vec4(v0.x, v1.x, 1.0, 1.0);
}
float dk2_shadow_visibility(vec3 source, vec4 svis) {
    for (int t = 0; t < 2; t++) {
        vec4 l = dk2_shadow_light(t);
        if (l.w > 0.0 && all(lessThan(abs(l.xyz - source), vec3(0.001)))) return svis[t];
    }
    return 1.0;
}
