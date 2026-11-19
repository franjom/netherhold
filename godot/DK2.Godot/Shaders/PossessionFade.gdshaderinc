// PossessionFade.Distance and Brightness in the shader's view coordinates (Godot's depth is -z); the globals are set by
// WorldScene.PossessionRendering.cs.
global uniform bool dk2_possession_fade_enabled;
global uniform float dk2_possession_view_distance;
varying float dk2_possession_brightness;
float dk2_possession_fade(vec3 view, mat4 projection) {
    if (!dk2_possession_fade_enabled) return 1.0;
    float distance = -view.z + 0.5 * (abs(projection[0][0] * view.x) + abs(projection[1][1] * view.y));
    float reach = dk2_possession_view_distance;
    return reach <= 0.0 ? 0.0 : clamp(2.0 * (reach - distance) / reach, 0.0, 1.0);
}
