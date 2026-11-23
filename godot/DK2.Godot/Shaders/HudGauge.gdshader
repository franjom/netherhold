// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

// The HUD gauge mask (audit item 11): the catalogue image as it is where a texel's HudGaugeMask rank is below the
// lit count, black with its alpha kept elsewhere, written unblended into a sub-viewport the size of the image.
shader_type canvas_item;
render_mode blend_disabled, unshaded;

uniform sampler2D ranks : filter_nearest;
uniform float lit_count;

void fragment() {
	ivec2 texel = ivec2(UV * vec2(textureSize(TEXTURE, 0)));
	vec4 source = texelFetch(TEXTURE, texel, 0);
	COLOR = texelFetch(ranks, texel, 0).r < lit_count ? source : vec4(0.0, 0.0, 0.0, source.a);
}
