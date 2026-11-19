// The world shader (World.gdshaderinc) for one render state. An opaque look: writes depth, no alpha.
shader_type spatial;
render_mode unshaded, cull_back, skip_vertex_transform, blend_mix, depth_draw_opaque;
#define DK2_BLEND_OPAQUE
// DK2_LIGHT_MODE: WorldScene puts the light mode's defines here (WorldScene.ShaderFor).
#include "res://Shaders/World.gdshaderinc"
