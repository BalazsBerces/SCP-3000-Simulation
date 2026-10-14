shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;
#include "res://shaders/lamp_fog.gdshaderinc"
varying vec3 bulb;
// The instance scale is 0.9 * the Lamp's level, so a Lamp coming on grows and brightens its halo.
varying float level;

void vertex() {
    bulb = MODEL_MATRIX[3].xyz;
    level = length(MODEL_MATRIX[0].xyz) / 0.9;
    // Preserve the instance scale while facing the active camera.
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    VERTEX *= length(MODEL_MATRIX[0].xyz);
}

void fragment() {
    float radius = length(UV * 2.0 - 1.0);
    float soft = exp(-radius * radius * 7.0) * (1.0 - smoothstep(0.7, 1.0, radius));
    ALBEDO = lamp_color * 2.0;
    ALPHA = soft * 0.22 * level * lamp_transmittance(bulb, CAMERA_POSITION_WORLD, VIEW_MATRIX);
}
