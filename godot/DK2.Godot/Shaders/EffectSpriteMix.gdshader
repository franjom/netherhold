// An alpha-blended effect sprite (EffectSprite.gdshaderinc): SRCALPHA, INVSRCALPHA.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;
#define DK2_SPRITE_ALPHA
#include "res://Shaders/EffectSprite.gdshaderinc"
