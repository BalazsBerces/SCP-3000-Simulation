// Concrete Pillars with their grid coordinate painted on all four sides at several heights.
// No emission: the paint only reflects light. The shaft darkens upwards, away from the Store
// lights, so its top is never seen, and where it stands between the camera and the followed
// point it dissolves, so it never hides what the Observer is watching.
shader_type spatial;

// Coordinate labels ("A-1" ... "C-3"), one atlas cell per Pillar id.
uniform sampler2D labels : filter_linear_mipmap, repeat_disable;
uniform float label_count = 9.0;
// Bottom of each painted label, in metres above the floor; the first is for close views,
// the others stand clear of the Fog banks.
const float LABEL_HEIGHTS[4] = { 2.0, 28.0, 60.0, 100.0 };
// Label size as a fraction of the face width; the atlas cell is twice as wide as it is tall.
uniform float label_width = 0.7;

// The shaft fades into darkness between these heights (metres).
uniform float fade_start = 45.0;
uniform float fade_end = 300.0;

// See-through cut-out around the followed point, as a fraction of the screen height.
uniform bool see_through = true;
uniform vec3 focus_position;
uniform float see_through_radius = 0.2;

varying vec3 local_position;
varying vec3 local_normal;
varying float world_height;
varying float face_width;
varying float label_index;

void vertex() {
	local_position = VERTEX;
	local_normal = NORMAL;
	world_height = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
	face_width = length(MODEL_MATRIX[0].xyz);
	label_index = round(COLOR.r * (label_count - 1.0));
}

// 4 x 4 ordered dither, so the cut-out has a soft edge without blending (TAA smooths it).
float bayer(vec2 p) {
	ivec2 i = ivec2(mod(p, 4.0));
	int m[16] = int[](0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5);
	return (float(m[i.y * 4 + i.x]) + 0.5) / 16.0;
}

void fragment() {
	// Shadows keep the whole Pillar: the cut-out is for the Observer's eye only.
	if (see_through && !IN_SHADOW_PASS) {
		vec4 focus_view = VIEW_MATRIX * vec4(focus_position, 1.0);
		vec4 focus_clip = PROJECTION_MATRIX * focus_view;
		// Only the part between the camera and the followed point, not the far side.
		if (focus_clip.w > 0.0 && VERTEX.z > focus_view.z + 1.0) {
			vec2 focus_uv = focus_clip.xy / focus_clip.w * 0.5 + 0.5;
			vec2 offset = (SCREEN_UV - focus_uv) * VIEWPORT_SIZE / VIEWPORT_SIZE.y;
			float inside = 1.0 - smoothstep(see_through_radius * 0.7, see_through_radius, length(offset));
			if (inside > bayer(FRAGCOORD.xy)) {
				discard;
			}
		}
	}

	float across = abs(local_normal.z) > 0.5 ? local_position.x + 0.5 : local_position.z + 0.5;
	// Keep labels reading left to right on all four faces.
	if (local_normal.z < -0.5 || local_normal.x > 0.5) { across = 1.0 - across; }
	float grain = fract(sin(dot(local_position * vec3(60.0, 900.0, 60.0), vec3(12.9898, 78.233, 45.164))) * 43758.5453);
	vec3 albedo = vec3(0.40, 0.40, 0.39) * mix(0.88, 1.08, grain);
	// Formwork joints every 4 m make the scale of the block readable.
	float lift = world_height / 4.0;
	float to_joint = min(fract(lift), 1.0 - fract(lift));
	float joint = 1.0 - smoothstep(0.015, 0.015 + fwidth(lift), to_joint);
	albedo *= mix(1.0, 0.75, joint);

	bool side = abs(local_normal.y) < 0.5;
	if (side) {
		float label_height = label_width * face_width * 0.5;
		float u = (across - (1.0 - label_width) * 0.5) / label_width;
		for (int i = 0; i < 4; i++) {
			float v = (world_height - LABEL_HEIGHTS[i]) / label_height;
			if (u > 0.0 && u < 1.0 && v > 0.0 && v < 1.0) {
				float paint = texture(labels, vec2((label_index + u) / label_count, 1.0 - v)).a;
				albedo = mix(albedo, vec3(0.86, 0.84, 0.76), paint);
			}
		}
	}

	float fade = smoothstep(fade_start, fade_end, world_height);
	ALBEDO = albedo * mix(1.0, 0.02, fade);
	ROUGHNESS = 0.94;
	SPECULAR = 0.5 * (1.0 - fade);
}
