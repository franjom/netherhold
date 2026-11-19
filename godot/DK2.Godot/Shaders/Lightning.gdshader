// Shared LightningDrawList corners, one ribbon section per additive MultiMesh instance.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_add, skip_vertex_transform, fog_disabled;

uniform sampler2D lightning_texture : filter_linear, repeat_disable;

void vertex() {
    vec3 world_corner = MODEL_MATRIX[int(VERTEX.z)].xyz;
    VERTEX = (VIEW_MATRIX * vec4(world_corner, 1.0)).xyz;
}

void fragment() {
    ALBEDO = texture(lightning_texture, UV).rgb;
    ALPHA = 1.0;
}
