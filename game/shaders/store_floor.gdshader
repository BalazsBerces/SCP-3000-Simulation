// Store floor: each Department has its own CC0 PBR floor texture, chosen per department cell from
// a map of Department types, with a thin metal transition strip where two Departments meet. Every texture repeats a
// whole number of times per Store length, so the floor is seamless across the wrap.
// So that the few-metre repeat does not read as wallpaper, the floor varies on top of that: organic
// layers are sampled as randomly offset copies blended over a triangle grid, tiled layers vary per
// tile, and every layer gets a faint large-scale shade and a light wear (scuffed aisles along the
// department cell edges, scattered stains). All variation is periodic in the Store size, fixed by
// the world seed, and needs nothing but the floor position.
shader_type spatial;
#include "res://shaders/lamp_pools.gdshaderinc"
#include "res://shaders/ground.gdshaderinc"

// One layer per Department type, in DepartmentType order.
uniform sampler2DArray floor_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray floor_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray floor_roughness : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
// Metres covered by one repeat of each layer; each must divide store_size exactly.
uniform float layer_metres[11];
// Albedo multiplier per layer, to balance the textures against each other.
uniform vec3 layer_tint[11];
// Which atlas layer each Department type reads its textures from, and how far its albedo fades towards the layer's average.
uniform int layer_source[11];
uniform float layer_flatten[11];
// How each layer breaks its repeat: 0 large-scale shade and wear only, 1 randomly offset copies
// (organic layers with no visible joints), 2 per-tile variation (tiles on a regular grid).
uniform int layer_mode[11];
// For per-tile layers: tiles per repeat, and the grid's offset within the repeat (repeat units).
uniform vec3 layer_grid[11];
// Fixes the variation, so each world has its own and the same world always looks the same.
uniform int world_seed = 0;
global uniform float lod_scale;
uniform float macro_strength = 0.09;
uniform float tile_strength = 0.07;
uniform float wear_strength = 1.0;

// Department type index (0-10) per department cell, as R8 (value / 255).
uniform sampler2D department_map : filter_nearest, repeat_enable;
uniform float department_cell_size = 16.0;
uniform float store_size = 1344.0;
// The transition strip: metres on each Department's side of the edge, and its brushed-metal look.
uniform float border_width = 0.04;
uniform vec3 border_color : source_color = vec3(0.62, 0.62, 0.6);
uniform float border_roughness = 0.4;
uniform float border_metallic = 0.7;

// Wrap debug overlay: chunk boundaries and the seams where the Store meets itself.
uniform bool show_chunks = false;
uniform float chunk_size = 64.0;
uniform vec3 chunk_line_color : source_color = vec3(0.2, 0.45, 0.9);
uniform vec3 seam_line_color : source_color = vec3(1.0, 0.35, 0.1);

// Store overview only: the boundaries of the Sectors around the Pillars.
uniform bool show_sectors = false;
uniform float sector_size = 448.0;
uniform vec3 sector_line_color : source_color = vec3(1.0, 0.85, 0.35);

// Store overview from high up only: all Furniture seen from above (FurnitureMap.cs), premultiplied
// by coverage and linear (not sRGB), covering the Store once, in place of the Furniture meshes.
uniform sampler2D furniture_map : filter_linear_mipmap_anisotropic, repeat_disable;
uniform bool show_furniture_map = false;

// The Camping ground's turf map (CampTurf.cs) and its rectangle (centre X and Z, half size); zero size if the world has none.
uniform sampler2D camp_map : filter_linear, repeat_disable;
uniform vec4 camp_rect = vec4(0.0);

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

float hash(ivec2 c, int salt) {
	uint h = uint(c.x) * 73856093u ^ uint(c.y) * 19349663u ^ uint(salt) * 83492791u ^ uint(world_seed) * 2654435761u;
	h ^= h >> 16u;
	h *= 0x7feb352du;
	h ^= h >> 15u;
	h *= 0x846ca68bu;
	h ^= h >> 16u;
	return float(h) / 4294967295.0;
}

// Smooth value noise in [0, 1] on a lattice of about `scale` metres, rounded so that it repeats
// exactly once per Store length; q is the wrapped position.
float store_noise(vec2 q, float scale, int salt) {
	int n = max(1, int(round(store_size / scale)));
	vec2 x = q / store_size * float(n);
	ivec2 i = (ivec2(floor(x)) % n + n) % n;
	ivec2 j = (i + 1) % n;
	vec2 f = fract(x);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash(i, salt);
	float b = hash(ivec2(j.x, i.y), salt);
	float c = hash(ivec2(i.x, j.y), salt);
	float d = hash(j, salt);
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// The triangle of a one-repeat lattice that covers uv: its corners' random offsets and weights,
// sharpened so that mostly one copy shows and the blend stays crisp. `period` is the number of
// repeats per Store length, so the lattice wraps with the Store.
void stochastic_corners(vec2 uv_wrapped, int period, int layer, out vec2 o0, out vec2 o1, out vec2 o2, out vec3 w) {
	ivec2 i = ivec2(floor(uv_wrapped));
	vec2 f = fract(uv_wrapped);
	ivec2 c1 = f.x > f.y ? ivec2(1, 0) : ivec2(0, 1);
	w = f.x > f.y ? vec3(1.0 - f.x, f.x - f.y, f.y) : vec3(1.0 - f.y, f.y - f.x, f.x);
	w = w * w * w;
	w /= w.x + w.y + w.z;
	ivec2 v0 = (i % period + period) % period;
	ivec2 v1 = (i + c1) % period;
	ivec2 v2 = (i + 1) % period;
	int salt = 100 + layer;
	o0 = vec2(hash(v0, salt), hash(v0, salt + 50));
	o1 = vec2(hash(v1, salt), hash(v1, salt + 50));
	o2 = vec2(hash(v2, salt), hash(v2, salt + 50));
}

vec3 stochastic_sample(sampler2DArray tex, vec3 coord, vec2 dx, vec2 dy, vec2 o0, vec2 o1, vec2 o2, vec3 w) {
	return textureGrad(tex, coord + vec3(o0, 0.0), dx, dy).rgb * w.x
			+ textureGrad(tex, coord + vec3(o1, 0.0), dx, dy).rgb * w.y
			+ textureGrad(tex, coord + vec3(o2, 0.0), dx, dy).rgb * w.z;
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
	// lod_scale below 1 sharpens the mip choice to make up for a lower render resolution (FSR2).
	vec2 dx = dFdx(p) / layer_metres[layer] * lod_scale;
	vec2 dy = dFdy(p) / layer_metres[layer] * lod_scale;
	vec3 coord = vec3(uv, float(layer_source[layer]));
	vec2 uv_wrapped = wrapped / layer_metres[layer];
	int repeats = int(round(store_size / layer_metres[layer]));
	vec3 color;
	float roughness;
	vec3 normal_map;
	if (layer_mode[layer] == 1) {
		vec2 o0, o1, o2;
		vec3 w;
		stochastic_corners(uv_wrapped, repeats, layer, o0, o1, o2, w);
		color = stochastic_sample(floor_albedo, coord, dx, dy, o0, o1, o2, w);
		roughness = stochastic_sample(floor_roughness, coord, dx, dy, o0, o1, o2, w).r;
		normal_map = stochastic_sample(floor_normal, coord, dx, dy, o0, o1, o2, w);
	} else {
		color = textureGrad(floor_albedo, coord, dx, dy).rgb;
		roughness = textureGrad(floor_roughness, coord, dx, dy).r;
		normal_map = textureGrad(floor_normal, coord, dx, dy).rgb;
	}
	color *= layer_tint[layer];
	// The layer's average colour, what a worn surface fades towards.
	vec3 average = textureLod(floor_albedo, vec3(0.5, 0.5, float(layer_source[layer])), 12.0).rgb * layer_tint[layer];
	// A pattern too busy for the render resolution (the carpet's stripes) fades towards its average.
	color = mix(color, average, layer_flatten[layer]);

	if (layer_mode[layer] == 2) {
		vec3 grid = layer_grid[layer];
		int tiles = repeats * int(grid.x);
		ivec2 tile = (ivec2(floor((uv_wrapped - grid.yz) * grid.x)) % tiles + tiles) % tiles;
		float shade = hash(tile, 200 + layer) - 0.5;
		float warmth = hash(tile, 250 + layer) - 0.5;
		color *= (1.0 + shade * 2.0 * tile_strength) * vec3(1.0 + warmth * 0.04, 1.0, 1.0 - warmth * 0.04);
		roughness += (hash(tile, 300 + layer) - 0.5) * 0.12;
	}

	// A faint large-scale shade, as uneven lighting, fading and cleaning leave on a real floor.
	float macro = store_noise(wrapped, 40.0, 1) * 0.5 + store_noise(wrapped, 13.0, 2) * 0.3 + store_noise(wrapped, 4.0, 7) * 0.2 - 0.5;
	float hue = store_noise(wrapped, 60.0, 3) - 0.5;
	color *= (1.0 + macro * 2.0 * macro_strength) * vec3(1.0 + hue * 0.05, 1.0, 1.0 - hue * 0.05);
	roughness += macro * 0.08;

	// Wear: scuffed aisles along the department cell edges, broken up along their length, and
	// scattered stains. Worn floor loses its pattern's contrast and relief and darkens slightly.
	vec2 to_edge = min(in_cell, department_cell_size - in_cell);
	float aisle = 1.0 - smoothstep(0.3, 1.7, min(to_edge.x, to_edge.y));
	aisle *= smoothstep(0.35, 0.8, store_noise(wrapped, 6.0, 4));
	float wear = aisle * 0.65 * wear_strength;
	color = mix(color, average * 0.85, wear * 0.6);
	normal_map = mix(normal_map, vec3(0.5, 0.5, 1.0), wear * 0.5);
	roughness -= wear * 0.08;
	float stain = smoothstep(0.6, 0.75, store_noise(wrapped, 2.5, 5)) * smoothstep(0.55, 0.7, store_noise(wrapped, 17.0, 6)) * wear_strength;
	color = mix(color, color * vec3(0.8, 0.76, 0.7), stain * 0.6);
	roughness = clamp(roughness + stain * 0.05, 0.0, 1.0);

	// A strip along cell edges that face another Department.
	// Antialiased, so the thin strip fades out with distance instead of flickering.
	vec2 aa = fwidth(p);
	vec2 strip_low = 1.0 - smoothstep(border_width - aa, border_width + aa, in_cell);
	vec2 strip_high = 1.0 - smoothstep(border_width - aa, border_width + aa, department_cell_size - in_cell);
	float border = 0.0;
	if (department_at(cell + ivec2(-1, 0)) != layer) border = max(border, strip_low.x);
	if (department_at(cell + ivec2(1, 0)) != layer) border = max(border, strip_high.x);
	if (department_at(cell + ivec2(0, -1)) != layer) border = max(border, strip_low.y);
	if (department_at(cell + ivec2(0, 1)) != layer) border = max(border, strip_high.y);
	color = mix(color, border_color, border);
	normal_map = mix(normal_map, vec3(0.5, 0.5, 1.0), border);
	NORMAL_MAP = normal_map;

	float furniture = 0.0;
	if (show_furniture_map) {
		vec4 seen = texture(furniture_map, p / store_size);
		furniture = seen.a;
		color = color * (1.0 - furniture) + seen.rgb;
		roughness = mix(roughness, 0.8, furniture);
		normal_map = mix(normal_map, vec3(0.5, 0.5, 1.0), furniture);
		NORMAL_MAP = normal_map;
	}

	if (show_chunks) {
		color = mix(color, chunk_line_color, overlay_lines(p, chunk_size, 0.15));
		color = mix(color, seam_line_color, overlay_lines(p, store_size, 0.6));
	}
	if (show_sectors) {
		color = mix(color, sector_line_color, overlay_lines(p, sector_size, 1.5));
	}
	// The Camping ground: artificial turf over the floor with winding trodden paths (red: turf, green: path).
	if (camp_rect.z > 0.0) {
		vec2 camp_delta = mod(p - camp_rect.xy + store_size * 0.5, store_size) - store_size * 0.5;
		if (all(lessThan(abs(camp_delta), camp_rect.zw))) {
			vec2 camp = texture(camp_map, camp_delta / (camp_rect.zw * 2.0) + 0.5).rg;
			float grain = store_noise(wrapped, 0.35, 11) * 0.5 + store_noise(wrapped, 1.3, 12) * 0.3 + store_noise(wrapped, 6.0, 13) * 0.2;
			vec3 turf = mix(vec3(0.03, 0.2, 0.03), vec3(0.09, 0.33, 0.05), grain);
			vec3 trodden = mix(vec3(0.3, 0.22, 0.12), vec3(0.45, 0.36, 0.22), grain);
			vec3 ground = mix(turf, trodden, camp.g);
			color = mix(color, ground, camp.r);
			roughness = mix(roughness, mix(0.95, 0.85, camp.g), camp.r);
			normal_map = mix(normal_map, vec3(0.5, 0.5, 1.0), camp.r);
			NORMAL_MAP = normal_map;
		}
	}
	// The fountain court's floor is wet: darker, glossy and a little blue-green.
	bool wet = on_ground_rect(p, ground_water_rect);
	if (wet) {
		color *= vec3(0.55, 0.66, 0.7);
		roughness = 0.08;
		normal_map = vec3(0.5, 0.5, 1.0);
		NORMAL_MAP = normal_map;
	}
	ALBEDO = color;
	ROUGHNESS = wet ? roughness : mix(roughness, border_roughness, border);
	METALLIC = wet ? 0.0 : border_metallic * border;
	SPECULAR = wet ? 0.5 : 0.35;
	// The dead zone: no light ever falls on its floor.
	float dark = ground_dark_amount(p);
	EMISSION = lamp_pools(VERTEX, lamp_mapped_normal(NORMAL, TANGENT, BINORMAL, normal_map, 1.0), INV_VIEW_MATRIX,
			ALBEDO, ROUGHNESS, METALLIC, SPECULAR);
	ALBEDO *= 1.0 - dark;
	SPECULAR *= 1.0 - dark;
	EMISSION *= 1.0 - dark;
}
