// Store floor: each Department has its own CC0 PBR floor texture, chosen per department cell from
// a map of Department types, with a dark strip where two Departments meet. Every texture repeats a
// whole number of times per Store length, so the floor is seamless across the wrap.
shader_type spatial;
#include "res://shaders/lamp_pools.gdshaderinc"

// One layer per Department type, in DepartmentType order.
uniform sampler2DArray floor_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray floor_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray floor_roughness : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres covered by one repeat of each layer; each must divide store_size exactly.
uniform float layer_metres[11];
// Albedo multiplier per layer, to balance the textures against each other.
uniform vec3 layer_tint[11];

// Department type index (0-10) per department cell, as R8 (value / 255).
uniform sampler2D department_map : filter_nearest, repeat_enable;
uniform float department_cell_size = 16.0;
uniform float store_size = 1344.0;
uniform float border_width = 0.12;
uniform vec3 border_color : source_color = vec3(0.12, 0.12, 0.12);

// Wrap debug overlay: chunk boundaries and the seams where the Store meets itself.
uniform bool show_chunks = false;
uniform float chunk_size = 64.0;
uniform vec3 chunk_line_color : source_color = vec3(0.2, 0.45, 0.9);
uniform vec3 seam_line_color : source_color = vec3(1.0, 0.35, 0.1);

// Store overview only: the boundaries of the Sectors around the Pillars.
uniform bool show_sectors = false;
uniform float sector_size = 448.0;
uniform vec3 sector_line_color : source_color = vec3(1.0, 0.85, 0.35);

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

int department_at(ivec2 cell) {
	ivec2 size = textureSize(department_map, 0);
	// Cells are at most one step off the floor, and GLSL leaves % of negative numbers undefined.
	ivec2 wrapped = (cell + size) % size;
	return int(texelFetch(department_map, wrapped, 0).r * 255.0 + 0.5);
}

// Grid lines that never fade out, so the overlay stays readable when zoomed out.
float overlay_lines(vec2 p, float cell_size, float half_width) {
	vec2 distance_to_line = abs(fract(p / cell_size + 0.5) - 0.5) * cell_size;
	vec2 width = max(vec2(half_width), fwidth(p) * 1.5);
	vec2 line = 1.0 - smoothstep(width * 0.5, width, distance_to_line);
	return max(line.x, line.y);
}

void fragment() {
	vec2 p = world_position.xz;
	vec2 wrapped = mod(p, store_size);
	ivec2 cell = ivec2(floor(wrapped / department_cell_size));
	vec2 in_cell = wrapped - vec2(cell) * department_cell_size;
	int layer = department_at(cell);

	// Gradients from the continuous position keep mip selection smooth across cell edges.
	vec2 uv = p / layer_metres[layer];
	vec2 dx = dFdx(p) / layer_metres[layer];
	vec2 dy = dFdy(p) / layer_metres[layer];
	vec3 coord = vec3(uv, float(layer));
	vec3 color = textureGrad(floor_albedo, coord, dx, dy).rgb * layer_tint[layer];
	float roughness = textureGrad(floor_roughness, coord, dx, dy).r;
	vec3 normal_map = textureGrad(floor_normal, coord, dx, dy).rgb;
	NORMAL_MAP = normal_map;

	// A strip along cell edges that face another Department.
	float border = 0.0;
	if (in_cell.x < border_width && department_at(cell + ivec2(-1, 0)) != layer) border = 1.0;
	if (in_cell.x > department_cell_size - border_width && department_at(cell + ivec2(1, 0)) != layer) border = 1.0;
	if (in_cell.y < border_width && department_at(cell + ivec2(0, -1)) != layer) border = 1.0;
	if (in_cell.y > department_cell_size - border_width && department_at(cell + ivec2(0, 1)) != layer) border = 1.0;
	color = mix(color, border_color, border);

	if (show_chunks) {
		color = mix(color, chunk_line_color, overlay_lines(p, chunk_size, 0.15));
		color = mix(color, seam_line_color, overlay_lines(p, store_size, 0.6));
	}
	if (show_sectors) {
		color = mix(color, sector_line_color, overlay_lines(p, sector_size, 1.5));
	}
	ALBEDO = color;
	ROUGHNESS = mix(roughness, 0.9, border);
	SPECULAR = 0.35;
	EMISSION = lamp_pools(VERTEX, lamp_mapped_normal(NORMAL, TANGENT, BINORMAL, normal_map, 1.0), INV_VIEW_MATRIX,
			ALBEDO, ROUGHNESS, 0.0, SPECULAR);
}
