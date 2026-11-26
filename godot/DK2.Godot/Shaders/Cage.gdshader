// The selection cage (WorldScene.CageRibbon) uses the colour times pow(centre, 2.15) x 1.6
// across the band, owner-matched (2026-10-19).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
uniform vec4 colour : source_color, color_conversion_disabled = vec4(0.0, 0.0, 0.0, 1.0);
#include "res://Shaders/PossessionFade.gdshaderinc"
void vertex() { dk2_possession_brightness = dk2_possession_fade((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz, PROJECTION_MATRIX); }
void fragment() {
// Clamp the centre weight where MSAA extrapolates the UV past the ribbon.
float centre = clamp(1.0 - abs(UV.y * 2.0 - 1.0), 0.0, 1.0);
ALBEDO = colour.rgb * pow(centre, 2.15) * 1.6 * dk2_possession_brightness;
ALPHA = 1.0;
}
