// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

shader_type canvas_item;
render_mode unshaded, blend_disabled;
#include "MinimapMath.gdshaderinc"
#include "MinimapTiles.gdshaderinc"
void fragment() {
    vec2 p = disc_point(logical_pixel(FRAGCOORD.xy));
    COLOR = vec4(base_colour(p, raster_point(p)), floor(fp32(disc_coverage(p) * 255.0) + 0.5)) / 255.0;
}
