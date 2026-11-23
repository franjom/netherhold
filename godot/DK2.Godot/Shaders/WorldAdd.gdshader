// The world shader (World.gdshaderinc) for one render state. An additive look (ONE, ONE: blend_add weights the source by its alpha, held at 1); no depth write.
shader_type spatial;
render_mode unshaded, cull_back, blend_add, depth_draw_never;
#define DK2_BLEND_ADD
// DK2_LIGHT_MODE: WorldScene puts the light mode's defines here (WorldScene.ShaderFor).
#include "res://Shaders/World.gdshaderinc"
