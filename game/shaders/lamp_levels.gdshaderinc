// Per-Lamp levels for the shade glow; needs lamp_pools.gdshaderinc. Only furniture includes it, so other materials carry no extra bindings.
// Per Lamp (RF): how far the Lamp has come on, from 0 (off) to 1 (fully lit); drives the shade glow.
global uniform sampler2D lamp_levels;
// The level every Lamp has while none is switching (0 or 1), or -1 while Lamps are switching.
global uniform float lamp_level_steady;

// The level of the Lamp whose bulb is nearest to a point, normally the origin of the Lamp's own
// Furniture piece; 0 if no Lamp is near. Gives each lit shade its own glow.
float lamp_level_near(vec3 world_point) {
	if (lamp_level_steady >= 0.0 || lamp_store_size <= 0.0) {
		return max(lamp_level_steady, 0.0);
	}
	vec2 wrapped = mod(world_point.xz, lamp_store_size);
	ivec2 cells = textureSize(lamp_cells, 0);
	vec2 entry = texelFetch(lamp_cells, min(ivec2(wrapped / lamp_cell_size), cells - 1), 0).rg;
	int first = int(entry.x);
	int index_width = textureSize(lamp_indices, 0).x;
	int lamp_width = textureSize(lamp_bulbs, 0).x;
	float nearest = 1e9;
	float level = 0.0;
	for (int i = 0; i < int(entry.y); i++) {
		int entry_index = first + i;
		int lamp = int(texelFetch(lamp_indices, ivec2(entry_index % index_width, entry_index / index_width), 0).r);
		ivec2 at = ivec2(lamp % lamp_width, lamp / lamp_width);
		vec3 bulb = texelFetch(lamp_bulbs, at, 0).xyz;
		vec2 across = mod(bulb.xz - wrapped + lamp_store_size * 0.5, lamp_store_size) - lamp_store_size * 0.5;
		float d = dot(across, across) + (bulb.y - world_point.y) * (bulb.y - world_point.y);
		if (d < nearest) {
			nearest = d;
			level = texelFetch(lamp_levels, at, 0).r;
		}
	}
	return level;
}
