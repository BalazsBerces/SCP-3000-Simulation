#include "res://shaders/fog_density.gdshaderinc"
// Additive contributions need only transmittance: scattering is already in the opaque scene.
// Bank records are centre.xz, radius, peak density. A texture avoids a fixed bank-count limit.
uniform sampler2D banks : filter_nearest, repeat_disable;
uniform int bank_count = 0;
uniform float store_size = 1344.0;
// Edge fade: halos dissolve over the last stretch before the draw distance. No base fog or haze.
uniform float edge_begin = 480.0;
uniform float edge_end = 550.0;
uniform float fog_length = 640.0;
uniform vec3 lamp_color : source_color = vec3(1.0, 0.78, 0.52);

float lamp_transmittance(vec3 surface, vec3 eye, mat4 view_matrix) {
    vec3 ray = surface - eye;
    float ray_length = length(ray);
    float view_depth = max(0.001, -(view_matrix * vec4(surface, 1.0)).z);
    float end = min(1.0, fog_length / view_depth);
    float optical_depth = 0.0;
    // Clip to the low fog layer before integration, retaining detail at the farthest zoom.
    float begin = 0.0;
    if (abs(ray.y) > 0.0001) {
        float a = (0.0 - eye.y) / ray.y;
        float b = (top - eye.y) / ray.y;
        begin = max(0.0, min(a, b));
        end = min(end, max(a, b));
    } else if (eye.y < 0.0 || eye.y > top) {
        end = 0.0;
    }
    if (end > begin) {
        const int STEPS = 12;
        float step_length = view_depth * (end - begin) / float(STEPS);
        for (int bank = 0; bank < bank_count; bank++) {
            vec4 data = texelFetch(banks, ivec2(bank, 0), 0);
            vec3 end_point = eye + ray * end;
            vec2 delta = mod(data.xy - end_point.xz + store_size * 0.5, store_size) - store_size * 0.5;
            // The triangle inequality cheaply rejects banks that cannot touch this ray segment.
            if (length(delta) > data.z + length(ray.xz) * (end - begin)) {
                continue;
            }
            for (int i = 0; i < STEPS; i++) {
                vec3 p = eye + ray * mix(begin, end, (float(i) + 0.5) / float(STEPS));
                // Sample the wrapping density at each point, including intervening banks.
                vec2 centre = p.xz + mod(data.xy - p.xz + store_size * 0.5, store_size) - store_size * 0.5;
                optical_depth += bank_density(p, centre, data.z, data.w) * step_length;
            }
        }
    }
    return exp(-optical_depth) * (1.0 - smoothstep(edge_begin, edge_end, ray_length));
}
