// Light pools: the share of every display Lamp's light that no real OmniLight3D gives, computed
// inside each opaque material (floor, Furniture, Pillars) and added as emission. They use the
// surface's own albedo, normal (normal map included), roughness and metallic with Godot's omni
// attenuation, Burley diffuse and Schlick-GGX specular, so a pool looks like the real light it
// complements. Godot fogs emission at the surface like any lit colour, so pools need no fog pass.
// The data comes from LampPools.cs as global shader parameters (declared in project.godot).

// Per 4 m cell of the wrapping Store (RGF): first entry in lamp_indices and the number of Lamps
// whose light reaches the cell.
global uniform sampler2D lamp_cells;
// Lamp numbers, cell after cell (RF, row-major).
global uniform sampler2D lamp_indices;
// Per Lamp (RGBAF, row-major): bulb x, y, z in Store coordinates and the light's range.
global uniform sampler2D lamp_bulbs;
// Per Lamp (RF): energy of its pool, the Lamp energy minus the share its real light gives.
global uniform sampler2D lamp_pool_energy;
global uniform float lamp_cell_size;
// Experiment: at most this many Lamps per pixel, and no pools beyond this view distance.
global uniform float lamp_pool_cap;
global uniform float lamp_pool_far;
// The Store size; 0 while there are no Lamps.
global uniform float lamp_store_size;
// Linear warm light colour, the same as the real Lamp lights.
global uniform vec3 lamp_light_color;

float lamp_schlick(float u) {
	float m = 1.0 - u;
	float m2 = m * m;
	return m2 * m2 * m;
}

// Godot's normal mapping, done here because the pools are computed before Godot applies NORMAL_MAP.
vec3 lamp_mapped_normal(vec3 normal, vec3 tangent, vec3 binormal, vec3 normal_map, float depth) {
	vec2 xy = normal_map.xy * 2.0 - 1.0;
	vec3 mapped = vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy))));
	return normalize(mix(normal, tangent * mapped.x + binormal * mapped.y + normal * mapped.z, depth));
}


// Light from every pool reaching this point; view-space inputs as in fragment(), result for EMISSION.
vec3 lamp_pools(vec3 vertex, vec3 normal, mat4 inv_view, vec3 albedo, float roughness, float metallic, float specular) {
	// Godot's screen-space roughness limiter with its default amount (0.25) and limit (0.18),
	// computed before any branch so the derivatives are defined.
	vec3 dndu = dFdx(normal);
	vec3 dndv = dFdy(normal);
	float kernel_roughness2 = min(2.0 * 0.25 * (dot(dndu, dndu) + dot(dndv, dndv)), 0.18);
	roughness = sqrt(min(1.0, roughness * roughness + kernel_roughness2));
	if (lamp_store_size <= 0.0 || -vertex.z > lamp_pool_far) {
		return vec3(0.0);
	}
	vec3 position = (inv_view * vec4(vertex, 1.0)).xyz;
	vec2 wrapped = mod(position.xz, lamp_store_size);
	ivec2 cells = textureSize(lamp_cells, 0);
	vec2 entry = texelFetch(lamp_cells, min(ivec2(wrapped / lamp_cell_size), cells - 1), 0).rg;
	int count = min(int(entry.y), int(lamp_pool_cap));
	if (count == 0) {
		return vec3(0.0);
	}
	int first = int(entry.x);
	int index_width = textureSize(lamp_indices, 0).x;
	int lamp_width = textureSize(lamp_bulbs, 0).x;

	vec3 n = normalize((inv_view * vec4(normal, 0.0)).xyz);
	vec3 v = normalize(-(inv_view * vec4(vertex, 0.0)).xyz);
	float n_dot_v = max(dot(n, v), 1e-4);
	float alpha = roughness * roughness;
	vec3 f0 = mix(vec3(0.16 * specular * specular), albedo, metallic);
	float f90 = clamp(dot(f0, vec3(50.0 * 0.33)), metallic, 1.0);
	vec3 diffuse = vec3(0.0);
	vec3 specular_light = vec3(0.0);
	for (int i = 0; i < count; i++) {
		int entry_index = first + i;
		int lamp = int(texelFetch(lamp_indices, ivec2(entry_index % index_width, entry_index / index_width), 0).r);
		ivec2 at = ivec2(lamp % lamp_width, lamp / lamp_width);
		float energy = texelFetch(lamp_pool_energy, at, 0).r;
		vec4 bulb = texelFetch(lamp_bulbs, at, 0);
		// The Lamp's nearest image across the Seams.
		vec2 across = mod(bulb.xz - wrapped + lamp_store_size * 0.5, lamp_store_size) - lamp_store_size * 0.5;
		vec3 to_light = vec3(across.x, bulb.y - position.y, across.y);
		float d = length(to_light);
		vec3 l = to_light / max(d, 1e-4);
		float n_dot_l = dot(n, l);
		if (energy <= 0.0 || d >= bulb.w || n_dot_l <= 0.0) {
			continue;
		}
		// get_omni_attenuation() with decay 1. Godot scales light energy by pi, and its BRDFs
		// divide by pi, so the two cancel.
		float nd = d / bulb.w;
		nd *= nd;
		nd *= nd;
		nd = max(1.0 - nd, 0.0);
		float light = energy * nd * nd / max(d, 1e-4);
		n_dot_l = min(n_dot_l, 1.0);
		vec3 h = normalize(v + l);
		float l_dot_h = clamp(dot(l, h), 0.0, 1.0);
		// Burley diffuse.
		float fd90_minus_1 = 2.0 * l_dot_h * l_dot_h * roughness - 0.5;
		diffuse += light * (1.0 + fd90_minus_1 * lamp_schlick(n_dot_v)) * (1.0 + fd90_minus_1 * lamp_schlick(n_dot_l)) * n_dot_l;
		// Schlick-GGX specular; Godot doubles the lights' default specular of 0.5 to an amount of 1.
		if (roughness > 0.0) {
			float n_dot_h = clamp(dot(n, h), 0.0, 1.0);
			float a = n_dot_h * alpha;
			float k = alpha / (1.0 - n_dot_h * n_dot_h + a * a);
			float ggx = k * k;
			float g = 0.5 / mix(2.0 * n_dot_l * n_dot_v, n_dot_l + n_dot_v, alpha);
			vec3 f = f0 + (f90 - f0) * lamp_schlick(l_dot_h);
			specular_light += light * n_dot_l * ggx * g * f;
		}
	}
	return lamp_light_color * (albedo * (1.0 - metallic) * diffuse + specular_light);
}
