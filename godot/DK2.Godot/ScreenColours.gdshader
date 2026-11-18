// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.
shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_image : hint_screen_texture, repeat_disable, filter_nearest;
uniform sampler2D ramps : repeat_disable, filter_nearest;
void fragment() {
    vec3 input_colour = textureLod(screen_image, SCREEN_UV, 0.0).rgb;
    vec3 index = clamp(floor(input_colour * 255.0 + 0.5), vec3(0.0), vec3(255.0));
    COLOR = vec4(
        texture(ramps, vec2((index.r + 0.5) / 256.0, 0.5)).r,
        texture(ramps, vec2((index.g + 0.5) / 256.0, 0.5)).g,
        texture(ramps, vec2((index.b + 0.5) / 256.0, 0.5)).b, 1.0);
}
