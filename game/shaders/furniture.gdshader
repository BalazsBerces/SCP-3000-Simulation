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

void vertex() {
	UV = UV * uv_scale;
}

void fragment() {
	vec3 albedo = albedo_color.rgb;
	float surface_roughness = roughness;
	vec3 normal = NORMAL;
	if (textured) {
		albedo *= texture(albedo_texture, UV).rgb;
		surface_roughness *= texture(roughness_texture, UV).r;
		if (normal_enabled) {
			vec3 normal_map = texture(normal_texture, UV).rgb;
			NORMAL_MAP = normal_map;
			normal = lamp_mapped_normal(NORMAL, TANGENT, BINORMAL, normal_map, 1.0);
		}
	}
	if (tinted) {
		albedo *= COLOR.rgb;
	}
	ALBEDO = albedo;
	ROUGHNESS = surface_roughness;
	METALLIC = metallic;
	EMISSION = emission_color * emission_energy
			+ lamp_pools(VERTEX, normal, INV_VIEW_MATRIX, albedo, surface_roughness, metallic, 0.5);
}
