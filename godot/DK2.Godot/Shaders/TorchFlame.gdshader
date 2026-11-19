// The wall flames (WorldScene.TorchFlames, migration plan step 3k): every flame one instance of one batch, its frame the
// layer in the instance's custom data (x), the frames one texture array. Additive (source alpha, one), unshaded, both
// faces, no depth write, turned as the camera is, as Unity sets the sprite's rotation to the camera's.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
uniform sampler2DArray frames : filter_linear, repeat_disable;
#include "res://Shaders/PossessionFade.gdshaderinc"
varying flat float dk2_frame;
void vertex() {
MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
dk2_frame = INSTANCE_CUSTOM.x;
dk2_possession_brightness = dk2_possession_fade((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz, PROJECTION_MATRIX);
}
void fragment() {
vec4 c = texture(frames, vec3(UV, dk2_frame));
c.rgb *= dk2_possession_brightness;
ALBEDO = c.rgb;
ALPHA = c.a;
}
