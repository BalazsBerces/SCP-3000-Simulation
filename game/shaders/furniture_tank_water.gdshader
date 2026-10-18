// The water in a tank of the Aquarium aisle: a faint blue body that picks up the backlight's glow and softly shimmers,
// clear enough to see the gravel, plants and fish through. Transparent, so a shader of its own, and drawn by full meshes
// only (a coarse tank is one block of glowing water, see FurnitureMeshes.BuildAquariumAisle). Lit by the Lamp pools like
// the other Furniture parts (furniture.gdshader) and dark in the dead zone.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, shadows_disabled;
#include "res://shaders/lamp_pools.gdshaderinc"
#include "res://shaders/ground.gdshaderinc"

uniform vec4 albedo_color : source_color = vec4(0.1, 0.42, 0.62, 0.16);
uniform vec3 emission_color : source_color = vec3(0.12, 0.45, 0.8);
uniform float emission_energy = 0.35;
uniform float roughness : hint_range(0.0, 1.0) = 0.05;

void fragment() {
	ALBEDO = albedo_color.rgb;
	ALPHA = albedo_color.a;
	ROUGHNESS = roughness;
	if (on_ground_rect((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz, ground_dark_rect)) {
		ALBEDO = vec3(0.0);
		SPECULAR = 0.0;
	} else {
		float shimmer = sin(UV.x * 17.0 + TIME * 0.9) * sin(UV.y * 13.0 - TIME * 0.7);
		EMISSION = emission_color * emission_energy * (0.8 + 0.2 * shimmer)
				+ lamp_pools(VERTEX, NORMAL, INV_VIEW_MATRIX, ALBEDO, roughness, 0.0, 0.5);
	}
}
