// The world shader (World.gdshaderinc) for the water and lava surfaces: WorldOpaque's render state, each vertex waved in
// the vertex step from its grid point and the frame (DK2_LIQUID; LiquidSurfaceWave.Vertex).
shader_type spatial;
render_mode unshaded, cull_back, blend_mix, depth_draw_opaque;
#define DK2_BLEND_OPAQUE
#define DK2_LIQUID
// DK2_LIGHT_MODE: WorldScene puts the light mode's defines here (WorldScene.ShaderFor).
#include "res://Shaders/World.gdshaderinc"
