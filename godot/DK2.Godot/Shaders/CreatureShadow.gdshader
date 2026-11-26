// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled, fog_disabled;

uniform sampler2D silhouette : filter_linear, repeat_disable;
instance uniform float strength;

void fragment() {
    ALBEDO = vec3(0.0);
    ALPHA = texture(silhouette, UV).r * strength;
}
