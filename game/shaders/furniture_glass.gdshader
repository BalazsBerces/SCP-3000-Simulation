// The glass of the Aquarium aisle's tanks (and their water surface): faintly blue-green and clear when seen straight on,
// more reflective towards grazing angles, lit by the Lamp pools like the other Furniture parts (furniture.gdshader),
// and unlit in the dead zone. Transparent, so a shader of its own: coarse meshes draw no glass.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, shadows_disabled;
#include "res://shaders/lamp_pools.gdshaderinc"
#include "res://shaders/ground.gdshaderinc"

uniform vec4 albedo_color : source_color = vec4(0.72, 0.9, 0.92, 0.1);
uniform float roughness : hint_range(0.0, 1.0) = 0.04;

void fragment() {
	float fresnel = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 4.0);
	ALBEDO = albedo_color.rgb;
	ALPHA = clamp(albedo_color.a + 0.6 * fresnel, 0.0, 0.8);
	ROUGHNESS = roughness;
	METALLIC = 0.0;
	SPECULAR = 0.7;
	float dark = ground_dark_amount((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz);
	EMISSION = lamp_pools(VERTEX, NORMAL, INV_VIEW_MATRIX, ALBEDO, roughness, 0.0, 0.7);
	ALBEDO *= 1.0 - dark;
	SPECULAR *= 1.0 - dark;
	EMISSION *= 1.0 - dark;
}
