// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

uniform sampler2D classes : filter_nearest, repeat_disable;
uniform sampler2D palette : filter_nearest, repeat_disable;
uniform sampler2D backdrop : filter_nearest, repeat_disable;
uniform ivec2 grid_size;
uniform ivec4 map_fit;
uniform ivec2 raster_origin;
uniform int zoom = -1;

ivec2 wrapped(ivec2 p, ivec2 size) { return ((p % size) + size) % size; }
vec3 rock(vec2 raster) {
    ivec2 size = textureSize(backdrop, 0);
    vec2 uv = fp_xy(fp_xy(raster / 128.0 * vec2(size)) - 0.5);
    vec2 low = floor(uv), weight = uv - low;
    ivec2 p = ivec2(low);
    vec3 a = floor(texelFetch(backdrop, wrapped(p, size), 0).rgb * 255.0 + 0.5);
    vec3 b = floor(texelFetch(backdrop, wrapped(p + ivec2(1, 0), size), 0).rgb * 255.0 + 0.5);
    vec3 c = floor(texelFetch(backdrop, wrapped(p + ivec2(0, 1), size), 0).rgb * 255.0 + 0.5);
    vec3 d = floor(texelFetch(backdrop, wrapped(p + ivec2(1, 1), size), 0).rgb * 255.0 + 0.5);
    vec3 top = fp_rgb(a + fp_rgb((b - a) * weight.x));
    vec3 bottom = fp_rgb(c + fp_rgb((d - c) * weight.x));
    return floor(fp_rgb(top + fp_rgb((bottom - top) * weight.y)) + 0.5) / 255.0;
}
vec3 base_colour(vec2 p, vec2 raster) {
    vec3 rgb = vec3(0.0);
    if (disc_coverage(p) > 0.0) {
        vec2 scale = vec2(div32(float(grid_size.x), float(max(map_fit.z, 1))), div32(float(grid_size.y), float(max(map_fit.w, 1))));
        vec2 tile = fp_xy(raster - vec2(map_fit.xy)) * scale;
        if (zoom >= 0) tile = (floor(raster) - vec2(raster_origin)) / float(1 << zoom);
        int colour_class = 1;
        if (all(greaterThanEqual(tile, vec2(0.0))) && all(lessThan(tile, vec2(grid_size)))) {
            ivec2 packed = ivec2(floor(texelFetch(classes, ivec2(tile), 0).rg * 255.0 + 0.5));
            colour_class = packed.x + packed.y * 256;
        }
        rgb = colour_class == 1 ? rock(raster) : texelFetch(palette, ivec2(colour_class, 0), 0).rgb;
    }
    return floor(rgb * 255.0 + 0.5);
}
