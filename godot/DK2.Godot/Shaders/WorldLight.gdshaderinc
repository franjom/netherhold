// DK2's light for the world shaders, copied from the tested ClassicVertexLight (keep the two equal) in Godot's frame
// (Z negated: every dot product and length is the same): the table a vertex reads, the bound skip, the per-flag sum
// and the packed diffuse; then Godot's fill and fade around it as Unity's shader composes them. The formula lines are
// pinned by ClassicVertexLightShaderTextTests, which also holds these three numbers to ClassicLightTableLayout's.
#define TEXELS_PER_TILE 49
#define TILES_PER_ROW 83
#define MAXIMUM_SOURCES 24
// With the torch shadows (DK2_TORCH_SHADOWS) each torch term is times its shadow visibility; without, it is the term.
#ifdef DK2_TORCH_SHADOWS
#define DK2_SHADOWED(term) (term) * vis
#else
#define DK2_SHADOWED(term) term
#endif
global uniform sampler2DArray dk2_light_tables;
global uniform ivec2 dk2_light_grid;
global uniform vec3 dk2_ambient;
global uniform vec3 dk2_fill_sky;
global uniform vec3 dk2_fill_equator;
global uniform vec3 dk2_fill_ground;
global uniform vec3 dk2_fill_sun;
global uniform vec3 dk2_fill_sun_direction;
global uniform vec3 dk2_fill_tint;
global uniform float dk2_unlit_fill;
global uniform float dk2_fill_floor;
global uniform vec3 dk2_fade;
vec4 dk2_texel(int tile, int texel) {
    int w = dk2_light_grid.x;
    int x = tile % w;
    return texelFetch(dk2_light_tables, ivec3((x % TILES_PER_ROW) * TEXELS_PER_TILE + texel, x / TILES_PER_ROW, tile / w), 0);
}
int dk2_bin(float d2) { return int(roundEven(d2 * 16.0 - 0.49999)); }
bool dk2_negative(float x) { return (floatBitsToUint(x) & 2147483648u) != 0u; }
bool dk2_point(vec3 v, vec3 n, vec4 pr, vec4 ci, out vec3 c) {
    c = vec3(0.0);
    vec3 to_light = pr.xyz - v;
    float d2 = dot(to_light, to_light);
    if (pr.w <= 0.0 || d2 >= pr.w) return true;
    float incidence = dot(n, to_light) * ci.w;
    if (dk2_negative(incidence)) return true;
    int index = dk2_bin(d2);
    if (index < 0 || index >= 256) return false;
    if (index == 0) {
        if (incidence == 0.0) return false;
        c = ci.rgb;
        return true;
    }
    float factor = incidence * (0.25 + 16.0 / float(index)) * (pr.w - d2) / pr.w;
    c = ci.rgb * min(factor, 1.0);
    return true;
}
bool dk2_instance(vec3 s, vec3 centre, vec3 vos, vec3 nos, mat3 basis, vec4 pr, vec4 ci, out vec3 c) {
    c = vec3(0.0);
    vec3 delta = pr.xyz - s;
    float d2 = (delta.x * delta.x + delta.y * delta.y) + delta.z * delta.z;
    if (pr.w <= 0.0 || d2 >= pr.w) return true;
    int index = dk2_bin(d2);
    if (index < 0 || index >= 256) return false;
    vec3 d = transpose(basis) * delta - (vos - centre);
    float incidence = ((d.x * nos.x + d.z * nos.z) + d.y * nos.y) * ci.w;
    if (dk2_negative(incidence)) return true;
    if (index == 0) {
        if (incidence == 0.0) return false;
        c = ci.rgb;
        return true;
    }
    float attenuation = (pr.w - d2) * (0.25 + 16.0 / float(index)) * (1.0 / pr.w);
    if (!(attenuation > 0.0)) return true;
    c = ci.rgb * min(incidence * attenuation, 1.0);
    return true;
}
vec3 dk2_radial(vec3 s, vec4 pr, vec4 ci) {
    vec3 delta = pr.xyz - s;
    float d2 = dot(delta, delta);
    if (pr.w <= 0.0 || d2 >= pr.w) return vec3(0.0);
    int index = dk2_bin(d2);
    if (index < 0 || index >= 256) return vec3(0.0);
    if (index == 0) return ci.rgb;
    float factor = (0.25 + 16.0 / float(index)) * (pr.w - d2) * ci.w * (1.0 / pr.w);
    return ci.rgb * min(factor, 1.0);
}
int dk2_tile(vec2 anchor, vec3 origin) {
    int w = dk2_light_grid.x;
    int count = w * dk2_light_grid.y;
    if (count <= 0 || w <= 0) return -1;
    int tile;
    if (anchor.y >= 1.0) tile = int(anchor.x);
    else {
        int x = int(floor(origin.x));
        int y = int(floor(origin.z));
        if (x < 0 || x >= w || y < 0) return -1;
        tile = y * w + x;
    }
    return (tile < 0 || tile >= count) ? -1 : tile;
}
#ifdef DK2_TORCH_SHADOWS
#include "res://Shaders/WorldShadows.gdshaderinc"
vec3 dk2_table_sum(vec2 anchor, vec4 bounds, vec2 kind, vec3 vos, vec3 nos, mat4 model, vec3 pws, vec3 nws, vec4 svis) {
#else
vec3 dk2_table_sum(vec2 anchor, vec4 bounds, vec2 kind, vec3 vos, vec3 nos, mat4 model, vec3 pws, vec3 nws) {
#endif
    vec3 origin = model[3].xyz;
    int tile = dk2_tile(anchor, origin);
    if (tile < 0) return vec3(0.0);
    int count = min(int(dk2_texel(tile, 0).r), MAXIMUM_SOURCES);
    mat3 basis = mat3(model);
    vec3 centre = basis * bounds.xyz;
    vec3 s = origin + centre;
    vec3 sum = vec3(0.0);
    for (int slot = 0; slot < count; slot++) {
        vec4 pr = dk2_texel(tile, 1 + slot * 2);
        vec4 cf = dk2_texel(tile, 2 + slot * 2);
        uint packed_flags = uint(cf.a);
        uint flags = packed_flags >> 8u;
        vec4 ci = vec4(cf.rgb, float(packed_flags & 255u) / 128.0);
        vec3 c;
#ifdef DK2_TORCH_SHADOWS
        float vis = dk2_shadow_visibility(pr.xyz, svis);
#endif
        if (kind.y == 1.0) {
            float scale = length(basis * vec3(1.0, 0.0, 0.0));
            float radius = sqrt(pr.w) + bounds.w * scale;
            vec3 delta = pr.xyz - s;
            if (dot(delta, delta) >= radius * radius) continue;
        }
        if (kind.x == 1.0) {
            if ((flags & 4u) == 4u) sum += DK2_SHADOWED(dk2_radial(origin, pr, ci));
            if ((flags & 256u) == 256u && dk2_instance(s, centre, vos, nos, basis, pr, ci, c)) sum += DK2_SHADOWED(c);
        } else if (dk2_point(pws, nws, pr, ci, c)) {
            if ((flags & 96u) == 96u) sum += DK2_SHADOWED(c);
            if ((flags & 160u) == 160u || (flags & 1u) == 1u) sum += DK2_SHADOWED(c);
        }
    }
    return sum;
}
vec3 dk2_packed(vec3 s) { return min(floor(max(s, vec3(0.0)) * 255.0 + 0.00001), vec3(255.0)) / 255.0; }
float dk2_fill_weight(vec2 anchor, vec3 world) {
    int w = dk2_light_grid.x;
    int count = w * dk2_light_grid.y;
    if (count <= 0 || w <= 0) return 1.0;
    int tile;
    if (anchor.y == 1.0) tile = int(anchor.x);
    else {
        int x = int(floor(world.x));
        int y = int(floor(world.z));
        if (x < 0 || x >= w || y < 0) return 1.0;
        tile = y * w + x;
    }
    return (tile >= 0 && tile < count) ? mix(dk2_unlit_fill, 1.0, dk2_texel(tile, 0).g) : 1.0;
}
#ifdef DK2_TORCH_SHADOWS
vec3 dk2_light(vec2 anchor, vec4 bounds, vec2 kind, vec3 vos, vec3 nos, mat4 model, vec3 world, vec3 nws,
        vec3 added, vec3 eye, vec4 svis) {
    vec3 sum = dk2_ambient + dk2_table_sum(anchor, bounds, kind, vos, nos, model, world, nws, svis);
#else
vec3 dk2_light(vec2 anchor, vec4 bounds, vec2 kind, vec3 vos, vec3 nos, mat4 model, vec3 world, vec3 nws,
        vec3 added, vec3 eye) {
    vec3 sum = dk2_ambient + dk2_table_sum(anchor, bounds, kind, vos, nos, model, world, nws);
#endif
    // Terrain tops carry DK2's inverted lighting normal; the fill wants the surface's own.
    vec3 fn = (anchor.y == 1.0 && nws.y < 0.0) ? -nws : nws;
    vec3 sky = fn.y >= 0.0 ? mix(dk2_fill_equator, dk2_fill_sky, fn.y) : mix(dk2_fill_equator, dk2_fill_ground, -fn.y);
    vec3 fill = (sky + dk2_fill_sun * max(dot(fn, dk2_fill_sun_direction), 0.0)) * dk2_fill_tint
        * dk2_fill_weight(anchor, world);
    if (anchor.y == 2.0) fill = vec3(0.0);
    if (dk2_fill_floor > 0.0 && kind.x != 1.0)
        fill *= clamp(1.0 - dot(sum, vec3(0.299, 0.587, 0.114)) / dk2_fill_floor, 0.0, 1.0);
    sum += fill + added;
    if (dk2_fade.y > dk2_fade.x) {
        float reach = length(world - eye) / dk2_fade.z;
        sum *= clamp((dk2_fade.y - reach) / (dk2_fade.y - dk2_fade.x), 0.0, 1.0);
    }
    return dk2_packed(sum);
}
