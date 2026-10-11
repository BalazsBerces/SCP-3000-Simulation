shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never;

void vertex() {
    // Preserve the instance scale while facing the active camera.
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    VERTEX *= length(MODEL_MATRIX[0].xyz);
}

void fragment() {
    float radius = length(UV * 2.0 - 1.0);
    float soft = exp(-radius * radius * 7.0) * (1.0 - smoothstep(0.7, 1.0, radius));
    ALBEDO = vec3(1.0, 0.7, 0.35) * 2.0;
    ALPHA = soft * 0.22;
}
