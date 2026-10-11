shader_type fog;
#include "res://shaders/fog_density.gdshaderinc"

// One Fog bank. The density across the floor follows the core's falloff (FogBank.Falloff): full
// within core_fraction of the radius, easing to nothing at the edge. The bank is a low dome, thick
// near the floor and thinning towards its top, which sinks towards the edge. A drifting 3D noise
// breaks up the surface so it reads as rolling fog rather than a smooth blob.

uniform float radius = 100.0;
// Fog density at the centre, for the eye (FogMaterial units): the bank's density times its look.
uniform float density = 0.06;
uniform vec3 albedo : source_color = vec3(0.8);
uniform vec3 emission : source_color = vec3(0.0);
void fog() {
	DENSITY = bank_density(WORLD_POSITION, OBJECT_POSITION.xz, radius, density);
	ALBEDO = albedo;
	EMISSION = emission;
}
