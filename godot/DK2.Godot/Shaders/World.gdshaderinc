// The one world shader (WorldScene.ShaderFor, migration plan step 3p): the texture times the colour, times DK2's light
// (WorldLight.gdshaderinc) unless at full light, then the environment pass and the hover's red added over it. The four
// World*.gdshader files differ only in their render state, which a uniform cannot change, and define one of
// DK2_BLEND_OPAQUE, DK2_BLEND_MIX or DK2_BLEND_ADD. The light step is per vertex, or per pixel with DK2_PER_PIXEL
// (--light-perpixel), and DK2_TORCH_SHADOWS (--light-shadows) adds the torch shadows; WorldScene defines those two.
// The colour buffer holds the stored bytes over 255 and blends them there, as DK2 and Unity's gamma space do.
uniform sampler2D albedo_texture : filter_linear_mipmap, repeat_disable;
// A Color set on a plain vec4 is turned linear (material_storage.cpp, _fill_std140_variant_ubo_value).
uniform vec4 base_colour : source_color, color_conversion_disabled = vec4(1.0);
uniform vec3 added_light = vec3(0.0);
// The look's switches: drawn at full white (no light step); a model's light inputs (CUSTOM0 the bound sphere, CUSTOM1
// the anchor and kind) rather than a terrain vertex's (CUSTOM0.xy the anchor); the instance's tint and fade apply; the
// KMF material blends (which red the hover asks of it).
uniform bool terrain_state = false;
uniform bool terrain_overlay_only = false;
uniform bool terrain_overlay_deferred = false;
uniform bool terrain_fog = false;
global uniform sampler2D dk2_terrain_state;
global uniform ivec2 dk2_terrain_grid;
global uniform vec3 dk2_tag_tint;
global uniform vec3 dk2_tag_overlay;
varying flat vec3 dk2_state_added;
varying flat vec3 dk2_state_overlay;
uniform bool full_light = false;
uniform bool model_vertex = false;
uniform bool instance_look = false;
uniform bool kmf_blended = false;
// The environment pass DK2 draws after an environment-mapped material (structure-drawing.md section 5): additive, lit
// without the added light, at the coordinates of 0x00582290 (the eye in camera space with z into the screen, the world
// normal in DK2's frame, halved).
uniform bool environment_pass = false;
uniform sampler2D environment_texture : filter_linear_mipmap, repeat_disable;
uniform vec4 environment_colour : source_color, color_conversion_disabled = vec4(0.0);
// An alpha-blended surface's additive passes (WorldAdd only): the environment pass and the red pass alone, over the whole
// triangle, as the material's next pass; blending them in its own pass would weigh them by the texture's alpha.
uniform bool extras_only = false;
// Per drawn thing (ModelInstanceLook): an object's terrain tint or a structure's instance tint added to its light, an
// object's fade, whether its environment passes draw (not while it fades: they draw black), and the hover's red pass on
// its surfaces made from opaque (x) and from blended (y) KMF materials, in RedTexture's colour.
instance uniform vec3 dk2_tint = vec3(0.0);
instance uniform float dk2_alpha = 1.0;
instance uniform float dk2_environment = 1.0;
instance uniform vec2 dk2_red = vec2(0.0);
global uniform vec3 dk2_hover_red;
#ifdef DK2_TORCH_SHADOWS
// A caster's per-slot fade (models only; the terrain's is the range).
instance uniform vec4 dk2_shadow_fade = vec4(0.0);
#endif
#include "res://Shaders/PossessionFade.gdshaderinc"
#include "res://Shaders/PossessionVisibility.gdshaderinc"
#include "res://Shaders/WorldLight.gdshaderinc"
varying vec2 dk2_env_uv;
#ifdef DK2_PER_PIXEL
// The vertex step's light inputs, handed to the fragment step: the position and normals interpolate; the anchor, bounds,
// kind and model transform are flat, so a tile index never drifts by interpolation and a model's table and basis are its own.
varying vec3 dk2_world;
varying vec3 dk2_nws;
varying vec3 dk2_vos;
varying vec3 dk2_nos;
varying flat vec4 dk2_bounds;
varying flat vec4 dk2_anchor_kind;
varying flat vec4 dk2_m0;
varying flat vec4 dk2_m1;
varying flat vec4 dk2_m2;
varying flat vec4 dk2_m3;
#else
varying vec3 dk2_diffuse;
varying vec3 dk2_env_diffuse;
#endif

vec2 dk2_environment_uv(vec3 world, vec3 nws, mat4 view_matrix) {
    vec3 view = (view_matrix * vec4(world, 1.0)).xyz;
    vec3 eye = normalize(vec3(view.x, view.y, -view.z));
    vec3 n = 0.5 * vec3(nws.x, nws.z, -nws.y);
    return vec2(n.z * eye.x - n.x * eye.z, n.z * eye.y - n.y * eye.z) + 0.5;
}

vec4 dk2_tile_state(int tile) {
    return texelFetch(dk2_terrain_state, ivec2(tile % dk2_terrain_grid.x, tile / dk2_terrain_grid.x), 0);
}
vec3 dk2_unpack_tint(float packed) {
    int v = int(packed);
    return vec3(float(v & 255), float((v >> 8) & 255), float((v >> 16) & 255)) / 255.0;
}
void vertex() {
    dk2_state_added = vec3(0.0);
    dk2_state_overlay = vec3(0.0);
    if (terrain_state) {
        int tag_source = int(CUSTOM0.z), tint_source = int(CUSTOM0.w);
        bool state_red = (int(dk2_tile_state(int(CUSTOM0.x)).b) & 2) != 0;
        bool state_tag = tag_source >= 0 && (int(dk2_tile_state(max(0, tag_source)).b) & 1) != 0;
        vec3 tint = vec3(0.0);
        if (tint_source >= 0) tint = dk2_unpack_tint(dk2_tile_state(tint_source).r);
        else if (tint_source <= -2) tint = dk2_unpack_tint(dk2_tile_state(-2 - tint_source).g);
        dk2_state_added = terrain_fog ? vec3(0.0) : state_red ? vec3(1.0, 0.0, 0.0) : (state_tag ? dk2_tag_tint : tint);
        dk2_state_overlay = state_red ? dk2_hover_red : (state_tag ? dk2_tag_overlay : vec3(0.0));
    }
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 nws = normalize(MODEL_NORMAL_MATRIX * NORMAL);
#ifdef DK2_PER_PIXEL
    dk2_world = world;
    dk2_nws = nws;
    dk2_vos = VERTEX;
    dk2_nos = NORMAL;
    dk2_bounds = model_vertex ? CUSTOM0 : vec4(0.0);
    dk2_anchor_kind = model_vertex ? CUSTOM1 : vec4(CUSTOM0.xy, 0.0, 0.0);
    dk2_m0 = MODEL_MATRIX[0];
    dk2_m1 = MODEL_MATRIX[1];
    dk2_m2 = MODEL_MATRIX[2];
    dk2_m3 = MODEL_MATRIX[3];
#else
    vec4 bounds = model_vertex ? CUSTOM0 : vec4(0.0);
    vec2 anchor = model_vertex ? CUSTOM1.xy : CUSTOM0.xy;
    vec2 kind = model_vertex ? CUSTOM1.zw : vec2(0.0);
    vec3 added = instance_look ? added_light + dk2_tint : added_light + dk2_state_added;
    if (!full_light)
        dk2_diffuse = dk2_light(anchor, bounds, kind, VERTEX, NORMAL, MODEL_MATRIX, world, nws, added, CAMERA_POSITION_WORLD);
    if (environment_pass && dk2_environment > 0.5)
        dk2_env_diffuse = dk2_light(anchor, bounds, kind, VERTEX, NORMAL, MODEL_MATRIX, world, nws, vec3(0.0), CAMERA_POSITION_WORLD);
#endif
    if (environment_pass) dk2_env_uv = dk2_environment_uv(world, nws, VIEW_MATRIX);
    // Every world shader places its vertex itself (skip_vertex_transform), after the light step (VERTEX and NORMAL stay
    // model-space before it): with Godot placing it, the lit shaders drew whole triangles of moved models in the clear
    // colour, as if they failed the depth test against the prepass (the cause is not established; docs/TRAPS.md).
    NORMAL = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    VERTEX = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
    dk2_possession_brightness = dk2_possession_fade(VERTEX, PROJECTION_MATRIX);
    dk2_possession_tile = int(CUSTOM0.x);
}

void fragment() {
    if (!model_vertex && !dk2_possession_tile_visible(dk2_possession_tile)) discard;
#ifdef DK2_TORCH_SHADOWS
    // In a shadow view an opaque lit surface on the view's side writes its distance from the torch over the range, its
    // strength and whether it is a model; every other surface draws nothing.
    if ((CAMERA_VISIBLE_LAYERS & 1u) == 0u) {
#ifdef DK2_BLEND_OPAQUE
        if (full_light) discard;
        int t = int(findLSB(CAMERA_VISIBLE_LAYERS >> 12u));
        vec3 l = dk2_shadow_light(t).xyz;
        vec3 d = dk2_world - l;
        float strength;
        if (model_vertex) {
            vec3 centre = (mat4(dk2_m0, dk2_m1, dk2_m2, dk2_m3) * vec4(dk2_bounds.xyz, 1.0)).xyz;
            float fade = dk2_shadow_fade[t];
            strength = fade > 0.0 ? min(0.7, 1.5 * (fade - length(centre.xz - l.xz)) / fade) : 0.0;
        } else strength = min(0.7, 1.5 * (2.5 - length(d.xz)) / 2.5);
        if (strength <= 0.0) discard;
        ALBEDO = vec3(length(d) / DK2_SHADOW_RANGE, strength, model_vertex ? 1.0 : 0.0);
#else
        discard;
#endif
    } else {
#endif
    bool red = kmf_blended ? dk2_red.y > 0.5 : dk2_red.x > 0.5;
    bool environment = environment_pass && dk2_environment > 0.5;
#ifdef DK2_PER_PIXEL
    vec3 added = instance_look ? added_light + dk2_tint : added_light + dk2_state_added;
    mat4 dk2_model = mat4(dk2_m0, dk2_m1, dk2_m2, dk2_m3);
    vec3 dk2_nos_px = normalize(dk2_nos), dk2_nws_px = normalize(dk2_nws);
#ifdef DK2_TORCH_SHADOWS
    // The creature and object shadows darken the whole per-pixel light, as DK2's receiver quad darkens the frame under
    // it, rather than only the torch's share (owner, stronger shadows). Not looked up where no light is evaluated.
    float dk2_caster = 1.0;
    vec4 dk2_svis = vec4(1.0);
    if (!full_light || environment) dk2_svis = dk2_shadow_all(dk2_world, normalize(dk2_nws), dk2_caster);
#endif
#endif
    // The environment pass's colour, as DK2 draws it after the material: additive, lit without the added light.
    vec3 env = vec3(0.0);
    if (environment) {
        vec4 e = texture(environment_texture, dk2_env_uv) * environment_colour;
#ifdef DK2_PER_PIXEL
#ifdef DK2_TORCH_SHADOWS
        vec3 dk2_env_px = dk2_light(dk2_anchor_kind.xy, dk2_bounds, dk2_anchor_kind.zw, dk2_vos, dk2_nos_px, dk2_model, dk2_world, dk2_nws_px, vec3(0.0), CAMERA_POSITION_WORLD, dk2_svis);
        dk2_env_px *= dk2_caster;
#else
        vec3 dk2_env_px = dk2_light(dk2_anchor_kind.xy, dk2_bounds, dk2_anchor_kind.zw, dk2_vos, dk2_nos_px, dk2_model, dk2_world, dk2_nws_px, vec3(0.0), CAMERA_POSITION_WORLD);
#endif
        e.rgb *= dk2_env_px;
#else
        e.rgb *= dk2_env_diffuse;
#endif
        env = e.rgb;
    }
#ifdef DK2_BLEND_ADD
    if (extras_only) {
        // An alpha-blended surface's next pass: its environment pass and its red, added whole.
        vec3 extra = env;
        if (red) extra += dk2_hover_red;
        ALBEDO = extra * dk2_possession_brightness;
        ALPHA = 1.0;
    } else {
#endif
    vec4 c = texture(albedo_texture, UV) * base_colour;
    if (instance_look) c.a *= dk2_alpha;
#ifdef DK2_PER_PIXEL
#ifdef DK2_TORCH_SHADOWS
    if (!full_light) {
        vec3 dk2_diffuse_px = dk2_light(dk2_anchor_kind.xy, dk2_bounds, dk2_anchor_kind.zw, dk2_vos, dk2_nos_px, dk2_model, dk2_world, dk2_nws_px, added, CAMERA_POSITION_WORLD, dk2_svis);
        dk2_diffuse_px *= dk2_caster;
        c.rgb *= dk2_diffuse_px;
    }
#else
    if (!full_light) {
        vec3 dk2_diffuse_px = dk2_light(dk2_anchor_kind.xy, dk2_bounds, dk2_anchor_kind.zw, dk2_vos, dk2_nos_px, dk2_model, dk2_world, dk2_nws_px, added, CAMERA_POSITION_WORLD);
        c.rgb *= dk2_diffuse_px;
    }
#endif
#else
    if (!full_light) c.rgb *= dk2_diffuse;
#endif
#ifndef DK2_BLEND_MIX
    // An opaque or additive surface's environment pass and red pass, which DK2 draws additively over the same triangles,
    // added here; an alpha-blended surface takes them as its next pass (extras_only).
    if (environment || red) {
        vec3 extra = env;
        if (red) extra += dk2_hover_red;
        c.rgb += extra;
    }
#endif
    if (terrain_state && !terrain_overlay_deferred) c.rgb += dk2_state_overlay;
    if (terrain_overlay_only) c.rgb = dk2_state_overlay;
    c.rgb *= dk2_possession_brightness;
    ALBEDO = c.rgb;
#ifdef DK2_BLEND_MIX
    ALPHA = c.a;
#endif
#ifdef DK2_BLEND_ADD
    ALPHA = 1.0;
    }
#endif
#ifdef DK2_TORCH_SHADOWS
    }
#endif
}
