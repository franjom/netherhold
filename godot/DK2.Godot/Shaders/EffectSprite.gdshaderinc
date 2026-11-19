// An effect sprite instance (WorldScene.Effects.cs, migration plan step 3k): the sheet's frame columns in the instance's
// custom data (u0, u1, v0, v1), its colour the instance's, unshaded, both faces, no depth write. The three
// EffectSprite*.gdshader files differ only in the blend (EffectDrawList.Blend): ONE, ONE (blend_add, alpha held at 1);
// SRCALPHA, INVSRCALPHA (blend_mix with DK2_SPRITE_ALPHA); ONE, ZERO (blend_mix, alpha held at 1).
uniform sampler2D frame_texture : filter_linear, repeat_disable;
#include "res://Shaders/PossessionFade.gdshaderinc"
void vertex() {
UV = vec2(mix(INSTANCE_CUSTOM.x, INSTANCE_CUSTOM.y, UV.x), mix(INSTANCE_CUSTOM.z, INSTANCE_CUSTOM.w, 1.0 - UV.y));
dk2_possession_brightness = dk2_possession_fade((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz, PROJECTION_MATRIX);
}
void fragment() {
vec4 c = texture(frame_texture, UV) * COLOR;
c.rgb *= dk2_possession_brightness;
ALBEDO = c.rgb;
#ifdef DK2_SPRITE_ALPHA
ALPHA = c.a;
#else
ALPHA = 1.0;
#endif
}
