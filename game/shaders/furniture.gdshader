// Furniture parts: the StandardMaterial3D features the parts use (albedo, tint from the instance
// colour, normal and roughness maps, metallic, emission), plus the light of the Lamp pools. The
// external models' materials (FurnitureModels.cs) use it too, with packed ORM maps and fabric tint.
shader_type spatial;
#include "res://shaders/lamp_pools.gdshaderinc"
#include "res://shaders/lamp_levels.gdshaderinc"
#include "res://shaders/ground.gdshaderinc"

uniform vec4 albedo_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_texture : hint_roughness_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D roughness_texture : hint_roughness_r, filter_linear_mipmap, repeat_enable;
// Textured parts have all three maps; coarse meshes leave out the normal map.
uniform bool textured = false;
uniform bool normal_enabled = false;
// Take the albedo tint from the instance colour.
uniform bool tinted = false;
// A model's packed map (glTF metallicRoughness): ambient occlusion in red, roughness in green and
// metallic in blue of roughness_texture, rather than roughness alone in red.
uniform bool orm = false;
// A tinted model material tints only its light fabric: texels lighter than tint_threshold (linear
// luminance) take the instance colour, keeping their shading relative to the fabric's average
// luminance tint_luminance; darker texels (wooden legs, frames) keep their own colour.
uniform bool tint_light = false;
uniform float tint_threshold = 0.12;
uniform float tint_luminance = 0.5;
uniform vec2 uv_scale = vec2(1.0);
uniform float roughness : hint_range(0.0, 1.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform vec3 emission_color : source_color = vec3(0.0);
uniform float emission_energy = 0.0;
// Lamp shades glow as far as their own Lamp has come on; other glowing parts (the EXIT sign, the
// aquarium water) glow steadily, day and night.
uniform bool lamp_glow = true;
// Coarse meshes draw all their parts in one surface: UV2.x holds each vertex's part, whose look
// comes from these tables (linear albedo with the tint flag in alpha; roughness and metallic).
// Their size is PartSlots in FurnitureMeshes.cs; change both together.
uniform bool coarse_parts = false;
uniform vec4 part_albedo[40];
uniform vec2 part_surface[40];
uniform int emissive_part = -1;
// The steady glow of each coarse part (linear colour times energy); black for parts that do not glow.
uniform vec3 part_glow[40];
// Dev aid (DevCapture --lod-debug): draws coarse meshes magenta, to show which level is drawn.
uniform bool lod_debug = false;
// A clock hand of the clock wall: 1 the hour hand, 2 the minute hand, 0 not a hand. A hand turns about the
// dial centre (height in UV2.y) to show the time left until Closing time plus the clock's own offset (the instance
// colour's alpha, as a fraction of a day), on a dial that counts down.
uniform int clock_hand = 0;

varying flat int part;
// A coarse model mesh marks the vertices on its light, tinted fabric with UV2.y 1 (see tint_light).
varying flat float coarse_tint;
// How far this piece's own Lamp has come on; read once per vertex, only for glowing shades.
varying flat float shade_level;

void vertex() {
	UV = UV * uv_scale;
	part = int(UV2.x + 0.5);
	coarse_tint = coarse_parts ? UV2.y : 0.0;
	shade_level = 1.0;
	if (clock_hand > 0) {
		float reading = mod(time_to_closing + COLOR.a * 86400.0, 86400.0);
		float angle = TAU * reading / (clock_hand == 1 ? 43200.0 : 3600.0);
		mat2 turn = mat2(vec2(cos(angle), -sin(angle)), vec2(sin(angle), cos(angle)));
		vec2 pivot = vec2(0.0, UV2.y);
		VERTEX.xy = turn * (VERTEX.xy - pivot) + pivot;
		NORMAL.xy = turn * NORMAL.xy;
	}
	if (coarse_parts ? part == emissive_part : emission_energy > 0.0 && lamp_glow) {
		shade_level = lamp_level_near(MODEL_MATRIX[3].xyz);
	}
}

void fragment() {
	vec3 albedo = albedo_color.rgb;
	float surface_roughness = roughness;
	float surface_metallic = metallic;
	vec3 emission = emission_color * emission_energy;
	// A glowing shade glows as far as its own Lamp has come on (shade_level stays 1 for steady glows).
	emission *= shade_level;
	bool tint = tinted;
	vec3 normal = NORMAL;
	if (coarse_parts) {
		albedo = part_albedo[part].rgb;
		tint = part_albedo[part].a > 0.5;
		if (coarse_tint > 0.5) {
			albedo = vec3(1.0);
			tint = true;
		}
		surface_roughness = part_surface[part].x;
		surface_metallic = part_surface[part].y;
		if (part != emissive_part) {
			emission = part_glow[part];
		}
		if (lod_debug) {
			albedo = vec3(0.8, 0.05, 0.8);
			tint = false;
		}
	} else if (textured) {
		vec3 texel = texture(albedo_texture, UV).rgb;
		if (tint && tint_light) {
			float luminance = dot(texel, vec3(0.2126, 0.7152, 0.0722));
			float fabric = smoothstep(0.6 * tint_threshold, tint_threshold, luminance);
			texel = mix(texel, COLOR.rgb * luminance / tint_luminance, fabric);
			tint = false;
		}
		albedo *= texel;
		if (orm) {
			vec3 packed = texture(roughness_texture, UV).rgb;
			AO = packed.r;
			surface_roughness *= packed.g;
			surface_metallic *= packed.b;
		} else {
			surface_roughness *= texture(roughness_texture, UV).r;
		}
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
	// The dead zone: no light falls on its Furniture and no shade glows.
	if (on_ground_rect((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz, ground_dark_rect)) {
		ALBEDO = vec3(0.0);
		SPECULAR = 0.0;
		EMISSION = vec3(0.0);
	} else {
		EMISSION = emission
				+ lamp_pools(VERTEX, normal, INV_VIEW_MATRIX, albedo, surface_roughness, surface_metallic, 0.5);
	}
}
