// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

// A front-end glow at quarter 1, 2 or 3 of full strength (audit item 11). TEXTURE is the full glow, whose colour every
// texel shares; the normal map holds quarters 1 to 3's alphas in red, green and blue, filtered by the same sampler.
shader_type canvas_item;

uniform int quarter = 1;

varying vec4 tint;

void vertex() {
	tint = COLOR;
}

void fragment() {
	COLOR.a = tint.a * texture(NORMAL_TEXTURE, UV)[quarter - 1];
}
