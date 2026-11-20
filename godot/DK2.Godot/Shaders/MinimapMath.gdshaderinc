// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

// A zero-valued uniform makes each requested intermediate's float bits observable: Godot's shader language has no
// precise qualifier. This prevents GPU contraction across the byte oracle's separate CPU float operations.
uniform uint rounding_mask = 0u;
float fp32(float value) { return uintBitsToFloat(floatBitsToUint(value) ^ rounding_mask); }
vec2 fp_xy(vec2 value) { return vec2(fp32(value.x), fp32(value.y)); }
vec3 fp_rgb(vec3 value) { return vec3(fp32(value.x), fp32(value.y), fp32(value.z)); }
uniform float diameter = 128.0;
uniform float target_size = 128.0;
vec2 logical_pixel(vec2 pixel) { return diameter == 1.0 ? vec2(0.5) : pixel; }
uniform vec2 turn = vec2(0.0, 1.0);

// Dekker-split products retain the residual even where the backend's mad/fma contracts differently.
float residual32(float a, float b, float c) {
    float product = fp32(a * b);
    float ah = uintBitsToFloat(floatBitsToUint(a) & 0xfffff000u);
    float bh = uintBitsToFloat(floatBitsToUint(b) & 0xfffff000u);
    float al = fp32(a - ah), bl = fp32(b - bh);
    float error = fp32(fp32(ah * bh) - product);
    error = fp32(error + fp32(ah * bl));
    error = fp32(error + fp32(al * bh));
    error = fp32(error + fp32(al * bl));
    return fp32(fp32(c - product) - error);
}
// Correct a reciprocal quotient with its product residual before rounding to the CPU float result.
float div32(float a, float b) {
    float q = fp32(a / b);
    return fp32(q + residual32(q, b, a) / b);
}
float sqrt32(float value) {
    float root = fp32(sqrt(value));
    return root > 0.0 ? fp32(root + div32(residual32(root, root, value), 2.0 * root)) : 0.0;
}
float step_between(float a, float b, float x) {
    a = fp32(a); b = fp32(b); x = fp32(x);
    if (b <= a) return x < a ? 0.0 : 1.0;
    float t = clamp(div32(fp32(x - a), fp32(b - a)), 0.0, 1.0);
    return fp32(fp32(t * t) * (3.0 - 2.0 * t));
}
vec2 disc_point(vec2 pixel) { return fp_xy(vec2(div32(pixel.x * 2.0, diameter), div32(pixel.y * 2.0, diameter)) - 1.0); }
vec2 raster_point(vec2 p) {
    return fp_xy(fp_xy(vec2(fp32(turn.y * p.x) - fp32(turn.x * p.y), fp32(turn.x * p.x) + fp32(turn.y * p.y))) * 64.0 + 64.0);
}
float disc_coverage(vec2 p) {
    float radius = sqrt32(fp32(p.x * p.x) + fp32(p.y * p.y));
    if (radius <= 0.0) return 1.0;
    float width = fp32(div32(abs(p.x) + abs(p.y), radius) * div32(2.0, diameter));
    return fp32(1.0 - step_between(1.0 - width, 1.0, radius));
}
float raster_width(vec2 g) {
    return (abs(fp32(turn.y * g.x) + fp32(turn.x * g.y)) + abs(fp32(-turn.x * g.x) + fp32(turn.y * g.y))) * div32(128.0, diameter);
}
