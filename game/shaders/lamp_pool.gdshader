shader_type spatial;
render_mode unshaded, blend_add, cull_front, depth_draw_never, depth_test_disabled, fog_disabled;
#include "res://shaders/lamp_fog.gdshaderinc"

uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;
varying vec3 bulb;
varying float radius;
varying float weight;

void vertex() {
    bulb = MODEL_MATRIX[3].xyz;
    radius = length(MODEL_MATRIX[0].xyz);
    weight = COLOR.a;
}

void fragment() {
    float depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
    vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
    vec3 surface = (INV_VIEW_MATRIX * vec4(view.xyz / view.w, 1.0)).xyz;
    float falloff = 1.0 - smoothstep(0.0, radius, distance(surface, bulb));
    ALBEDO = lamp_color * 0.65;
    ALPHA = falloff * falloff * weight * lamp_transmittance(surface, CAMERA_POSITION_WORLD, VIEW_MATRIX);
}
