// The world shader (World.gdshaderinc) for one render state. A fading object's alpha-blended look, which writes depth (ObjectMaterialLook.Faded).
shader_type spatial;
render_mode unshaded, cull_back, blend_mix, depth_draw_always;
#define DK2_BLEND_MIX
// DK2_LIGHT_MODE: WorldScene puts the light mode's defines here (WorldScene.ShaderFor).
#include "res://Shaders/World.gdshaderinc"
