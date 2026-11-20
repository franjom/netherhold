// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

shader_type canvas_item;
render_mode unshaded, blend_disabled;
#include "MinimapMath.gdshaderinc"
#include "MinimapTiles.gdshaderinc"
uniform sampler2D marker_data : filter_nearest, repeat_disable;
varying flat int marker_index;

void vertex() {
    marker_index = int(INSTANCE_CUSTOM.x);
    vec4 shape = texelFetch(marker_data, ivec2(0, marker_index), 0);
    vec2 raster = shape.xy + VERTEX * (abs(shape.zw) + 512.0 / diameter) - 64.0;
    VERTEX = (vec2(turn.y * raster.x + turn.x * raster.y, -turn.x * raster.x + turn.y * raster.y) / 128.0 + 0.5) * target_size;
}
float marker_fill(vec2 raster, vec4 shape, int kind) {
    vec2 local = vec2(div32(raster.x - shape.x, shape.z * 0.5), div32(raster.y - shape.y, shape.w * 0.5));
    float fill = 1.0;
    if (any(lessThan(local, vec2(-1.0))) || any(greaterThan(local, vec2(1.0)))) return 0.0;
    if (kind == 1 || kind == 2) {
        float length = sqrt32(fp32(local.x * local.x) + fp32(local.y * local.y));
        float width = length > 0.0 ? raster_width(vec2(div32(div32(local.x, length) * 2.0, shape.z), div32(div32(local.y, length) * 2.0, shape.w))) : 0.0;
        if (kind == 1) fill = 1.0 - step_between(1.0 - width * 0.5, 1.0 + width * 0.5, length);
        else {
            float radius = shape.z * 0.5 - 1.0;
            float ring = abs(fp32(length * (radius + 1.0)) - radius) - 0.5;
            float edge = max(width * (radius + 1.0) * 0.5, 0.001);
            fill = 1.0 - step_between(-edge, edge, ring);
        }
    } else if (kind == 3) {
        vec2 distance = raster - (shape.xy - shape.zw * 0.5);
        float border = min(min(distance.x, shape.z - distance.x), min(distance.y, shape.w - distance.y));
        float edge = max(raster_width(vec2(1.0, 0.0)) * 0.5, 0.001);
        fill = 1.0 - step_between(1.0 - edge, 1.0 + edge, border);
    }
    return fp32(fill);
}
void fragment() {
    vec2 p = disc_point(logical_pixel(FRAGCOORD.xy)), raster = raster_point(p);
    float coverage = disc_coverage(p);
    vec4 own_shape = texelFetch(marker_data, ivec2(0, marker_index), 0);
    int own_kind = int(texelFetch(marker_data, ivec2(1, marker_index), 0).a);
    if (coverage * marker_fill(raster, own_shape, own_kind) <= 0.0) discard;
    vec3 colour = base_colour(p, raster);
    // Each quad recomposes its preceding markers, retaining byte rounding and the disc alpha without framebuffer blending.
    for (int i = 0; i <= marker_index; i++) {
        vec4 shape = texelFetch(marker_data, ivec2(0, i), 0);
        vec4 marker = texelFetch(marker_data, ivec2(1, i), 0);
        float alpha = fp32(coverage * marker_fill(raster, shape, int(marker.a)));
        if (alpha > 0.0) colour = floor(fp_rgb(colour + fp_rgb((marker.rgb - colour) * alpha)) + 0.5);
    }
    COLOR = vec4(colour, floor(fp32(coverage * 255.0) + 0.5)) / 255.0;
}
