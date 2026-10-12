// Furniture parts: the StandardMaterial3D features the parts use (albedo, tint from the instance
// colour, normal and roughness maps, metallic, emission), plus the light of the Lamp pools.
shader_type spatial;
#include "res://shaders/lamp_pools.gdshaderinc"

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_texture : hint_roughness_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D roughness_texture : hint_roughness_r, filter_linear_mipmap, repeat_enable;
// Textured parts have all three maps; coarse meshes leave out the normal map.
uniform bool textured = false;
uniform bool normal_enabled = false;
// Take the albedo tint from the instance colour.
uniform bool tinted = false;
uniform vec2 uv_scale = vec2(1.0);
uniform float roughness : hint_range(0.0, 1.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform vec3 emission_color : source_color = vec3(0.0);
uniform float emission_energy = 0.0;
// Coarse meshes draw all their parts in one surface: UV2.x holds each vertex's part, whose look
// comes from these tables (linear albedo with the tint flag in alpha; roughness and metallic).
uniform bool coarse_parts = false;
uniform vec4 part_albedo[32];
uniform vec2 part_surface[32];
uniform int emissive_part = -1;

varying flat int part;

void vertex() {
	UV = UV * uv_scale;
	part = int(UV2.x + 0.5);
}

void fragment() {
	vec3 albedo = albedo_color.rgb;
	float surface_roughness = roughness;
	float surface_metallic = metallic;
	vec3 emission = emission_color * emission_energy;
	bool tint = tinted;
	vec3 normal = NORMAL;
	if (coarse_parts) {
		albedo = part_albedo[part].rgb;
		tint = part_albedo[part].a > 0.5;
		surface_roughness = part_surface[part].x;
		surface_metallic = part_surface[part].y;
		if (part != emissive_part) {
			emission = vec3(0.0);
		}
	} else if (textured) {
		albedo *= texture(albedo_texture, UV).rgb;
		surface_roughness *= texture(roughness_texture, UV).r;
		if (normal_enabled) {
			vec3 normal_map = texture(normal_texture, UV).rgb;
			NORMAL_MAP = normal_map;
			normal = lamp_mapped_normal(NORMAL, TANGENT, BINORMAL, normal_map, 1.0);
		}
	}
	if (tint) {
		albedo *= COLOR.rgb;
	}
	ALBEDO = albedo;
	ROUGHNESS = surface_roughness;
	METALLIC = surface_metallic;
	EMISSION = emission
			+ lamp_pools(VERTEX, normal, INV_VIEW_MATRIX, albedo, surface_roughness, surface_metallic, 0.5);
}
