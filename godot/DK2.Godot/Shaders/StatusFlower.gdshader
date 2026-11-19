// A status flower's layer (WorldScene.Flowers.cs, migration plan step 3k), Unity's ClassicEffect.shader for an A_ icon:
// SRCALPHA, INVSRCALPHA, unshaded, both faces, depth-tested without writing. The tint is the instance's colour (the
// owner's, made linear on upload as a source_color uniform was), the custom data the experience wedge (x, -1 none) and
// whether the layer is the petal ring (y), which is coloured by twice its luminance, clamped.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;
uniform sampler2D art : source_color, filter_linear, repeat_disable;
varying flat vec2 dk2_flower;
void vertex() {
dk2_flower = INSTANCE_CUSTOM.xy;
}
void fragment() {
vec4 texel = texture(art, UV);
if (dk2_flower.y > 0.5) texel.rgb = vec3(clamp(dot(texel.rgb, vec3(0.299, 0.587, 0.114)) * 2.0, 0.0, 1.0));
vec4 colour = texel * COLOR;
if (dk2_flower.x >= 0.0) {
    // Half colour outside the wedge, clockwise from the top, as DK2's two arcs draw it.
    vec2 d = vec2(UV.x - 0.5, 0.5 - UV.y);
    float turn = atan(d.x, d.y) / 6.2831853;
    if (turn < 0.0) turn += 1.0;
    if (turn >= dk2_flower.x) colour.rgb *= 0.5;
}
ALBEDO = colour.rgb;
ALPHA = colour.a;
}
