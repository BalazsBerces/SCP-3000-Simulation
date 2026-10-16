// The fish of a tank in the Aquarium aisle, one school of one kind in its kind's colour (the instance colour), lit by the
// Lamp pools like the other Furniture parts (furniture.gdshader) and unlit in the dead zone. Each vertex carries in CUSTOM0
// its fish's phase (a fraction of a turn), the centre of its depth, the centre of its swim along the width and how far it
// swims each way; the tank's position shifts the phase, so tanks of a kind do not swim in step. They swim to and fro,
// turning about the vertical at each end, wagging their tails and bobbing. A shader of its own: reading CUSTOM0 in
// furniture.gdshader doubled the draw calls of every far view.
shader_type spatial;
render_mode cull_back;
#include "res://shaders/lamp_pools.gdshaderinc"
#include "res://shaders/ground.gdshaderinc"

uniform float roughness : hint_range(0.0, 1.0) = 0.35;
uniform float metallic : hint_range(0.0, 1.0) = 0.15;

void vertex() {
	float phase = fract(CUSTOM0.x + dot(MODEL_MATRIX[3].xz, vec2(0.37, 0.71))) * TAU;
	float t = TIME * (0.3 + 0.25 * fract(CUSTOM0.x * 7.0)) + phase;
	// Facing +X while swimming that way, turning about the vertical through the fish at each end of its swim.
	float turn = (1.0 - clamp(cos(t) * 3.0, -1.0, 1.0)) * PI * 0.5;
	vec2 local = vec2(VERTEX.x - CUSTOM0.z, VERTEX.z - CUSTOM0.y);
	local.y += max(0.0, -local.x) * 0.3 * sin(TIME * 7.0 + phase * 5.0);
	mat2 yaw = mat2(vec2(cos(turn), -sin(turn)), vec2(sin(turn), cos(turn)));
	local = yaw * local;
	VERTEX.x = CUSTOM0.z + CUSTOM0.w * sin(t) + local.x;
	VERTEX.z = CUSTOM0.y + local.y;
	VERTEX.y += 0.008 * sin(TIME * 0.9 + phase * 3.0);
	NORMAL.xz = yaw * NORMAL.xz;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = roughness;
	METALLIC = metallic;
	if (on_ground_rect((INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz, ground_dark_rect)) {
		ALBEDO = vec3(0.0);
		SPECULAR = 0.0;
	} else {
		EMISSION = lamp_pools(VERTEX, NORMAL, INV_VIEW_MATRIX, ALBEDO, roughness, metallic, 0.5);
	}
}
