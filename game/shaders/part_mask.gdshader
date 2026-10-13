// Dev check only (CoarseColourCheck): marks up to three Furniture parts in the red, green and blue
// channels, unlit, so a render shows exactly which pixels each part covers. Coarse meshes carry
// their part in UV2.x; a full mesh surface is one part, given as surface_part.
shader_type spatial;
render_mode unshaded, cull_back;

uniform ivec3 parts = ivec3(-1);
uniform bool coarse_parts = false;
uniform int surface_part = -1;

varying flat int part;

void vertex() {
	part = coarse_parts ? int(UV2.x + 0.5) : surface_part;
}

void fragment() {
	ALBEDO = vec3(part == parts.x ? 1.0 : 0.0, part == parts.y ? 1.0 : 0.0, part == parts.z ? 1.0 : 0.0);
}
