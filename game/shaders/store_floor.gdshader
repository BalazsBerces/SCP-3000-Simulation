// Flat Store floor: world-space tiles with grout lines, so pan and zoom read clearly.
shader_type spatial;

uniform vec3 tile_color : source_color = vec3(0.58, 0.56, 0.52);
uniform vec3 grout_color : source_color = vec3(0.36, 0.35, 0.33);
uniform float tile_size = 1.2;
uniform float grout_width = 0.025;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// 1.0 on a grid line, 0.0 elsewhere, anti-aliased and faded out once the lines get thinner than a pixel.
float grid_lines(vec2 p, float cell_size, float half_width) {
	vec2 distance_to_line = abs(fract(p / cell_size + 0.5) - 0.5) * cell_size;
	vec2 pixel = fwidth(p);
	vec2 line = 1.0 - smoothstep(vec2(half_width), vec2(half_width) + pixel, distance_to_line);
	float visibility = 1.0 - smoothstep(half_width, half_width * 6.0, max(pixel.x, pixel.y));
	return max(line.x, line.y) * visibility;
}

void fragment() {
	vec2 p = world_position.xz;
	vec2 tile_id = floor(p / tile_size);
	float variation = fract(sin(dot(tile_id, vec2(12.9898, 78.233))) * 43758.5453);
	float lines = max(grid_lines(p, tile_size, grout_width), grid_lines(p, tile_size * 8.0, grout_width * 4.0));

	ALBEDO = mix(tile_color * (0.95 + 0.05 * variation), grout_color, lines);
	ROUGHNESS = 0.6;
	SPECULAR = 0.35;
}
