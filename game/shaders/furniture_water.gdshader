// The backlight of a tank in the Aquarium aisle: a translucent panel on the back glass that glows steadily, day and night,
// its light rippling as if through moving water, seen from both sides so the fish still show through it from behind the
// tank. Lit by the Lamp pools like the other Furniture parts (furniture.gdshader) and dark in the dead zone. A shader of
// its own: the ripple in furniture.gdshader slowed every full Furniture part. The uniforms are furniture.gdshader's names,
// which the coarse material's tables read (FurnitureMeshes.CoarseMaterial); the coarse tank block is opaque.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
#include "res://shaders/lamp_pools.gdshaderinc"
#include "res://shaders/ground.gdshaderinc"

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform float roughness : hint_range(0.0, 1.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform vec3 emission_color : source_color = vec3(0.0);
uniform float emission_energy = 0.0;
uniform float panel_alpha : hint_range(0.0, 1.0) = 0.5;

void fragment() {
	ALBEDO = albedo_color.rgb;
	ROUGHNESS = roughness;
	METALLIC = metallic;
	ALPHA = panel_alpha;
	if (on_ground_rect((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz, ground_dark_rect)) {
		ALBEDO = vec3(0.0);
		SPECULAR = 0.0;
	} else {
		float ripple = sin(UV.x * 23.0 + TIME * 1.3) * sin(UV.y * 19.0 - TIME * 1.1) + sin((UV.x + UV.y) * 31.0 + TIME * 1.7);
		EMISSION = emission_color * emission_energy * (0.8 + 0.12 * ripple)
				+ lamp_pools(VERTEX, NORMAL, INV_VIEW_MATRIX, ALBEDO, roughness, metallic, 0.5);
	}
}
