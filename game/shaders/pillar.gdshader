// Concrete and painted markings share one instanced mesh. No emission: paint reflects light.
shader_type spatial;
uniform sampler2D numbers : filter_linear_mipmap, repeat_disable;
varying vec3 local_position;
varying vec3 local_normal;
varying vec3 marking;

void vertex() {
    local_position = VERTEX;
    local_normal = NORMAL;
    marking = COLOR.rgb;
}

void fragment() {
    float height = local_position.y + 0.5;
    float across = abs(local_normal.z) > 0.5 ? local_position.x + 0.5 : local_position.z + 0.5;
    // Keep numerals readable clockwise around all four faces.
    if (local_normal.z < -0.5 || local_normal.x > 0.5) { across = 1.0 - across; }
    float grain = fract(sin(dot(local_position * vec3(25.0, 400.0, 25.0), vec3(12.9898, 78.233, 45.164))) * 43758.5453);
    vec3 concrete = vec3(0.39, 0.40, 0.39) * mix(0.85, 1.1, grain);
    vec3 bands[5] = vec3[](vec3(0.58, 0.38, 0.08), vec3(0.46, 0.08, 0.06),
        vec3(0.08, 0.22, 0.44), vec3(0.10, 0.31, 0.16), vec3(0.73, 0.71, 0.63));
    bool side = abs(local_normal.y) < 0.5;
    bool band = (height > 0.64 && height < 0.68) || (height > 0.06 && height < 0.10);
    vec3 paint = bands[int(round(marking.r * 4.0))];
    ALBEDO = side && band ? paint : concrete;
    if (side) {
        float panel_y = height > 0.5 ? (height - 0.70) / 0.12 : (height - 0.12) / 0.12;
        if (panel_y > 0.0 && panel_y < 1.0) {
            float number = round(marking.b * 512.0);
            vec2 atlas_cell = vec2(mod(number, 32.0), floor(number / 32.0));
            float digit = texture(numbers, (atlas_cell + vec2(across, 1.0 - panel_y)) / vec2(32.0, 16.0)).a;
            ALBEDO = mix(ALBEDO, vec3(0.82, 0.80, 0.72), digit);
        }
        float damage = round(marking.g * 2.0);
        float crack_x = 0.32 + sin(height * 53.0) * 0.025;
        if (damage > 0.5) { crack_x += (height - 0.4) * 0.7; }
        bool crack = height > 0.26 && height < 0.56 && abs(across - crack_x) < 0.009;
        bool chip = damage > 1.5 && across < 0.10 && height > 0.30 && height < 0.43;
        if (crack || chip) { ALBEDO *= 0.28; }
    }
    ROUGHNESS = 0.94;
}
