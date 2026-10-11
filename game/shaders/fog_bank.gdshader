shader_type fog;

// One Fog bank. The density across the floor follows the core's falloff (FogBank.Falloff): full
// within core_fraction of the radius, easing to nothing at the edge. The bank is a low dome, thick
// near the floor and thinning towards its top, which sinks towards the edge. A drifting 3D noise
// breaks up the surface so it reads as rolling fog rather than a smooth blob.

uniform float radius = 100.0;
uniform float core_fraction = 0.35;
// Fog density at the centre, for the eye (FogMaterial units): the bank's density times its look.
uniform float density = 0.06;
// Height of the dome at the centre, in metres.
uniform float top = 18.0;
uniform vec3 albedo : source_color = vec3(0.8);
uniform vec3 emission : source_color = vec3(0.0);
uniform sampler3D noise : repeat_enable;
// World-space offset of the noise, so it rolls with the wind.
uniform vec3 noise_offset = vec3(0.0);
uniform float noise_scale = 70.0;

void fog() {
	float across = length(WORLD_POSITION.xz - OBJECT_POSITION.xz) / radius;
	float t = clamp((across - core_fraction) / (1.0 - core_fraction), 0.0, 1.0);
	float falloff = 1.0 - t * t * (3.0 - 2.0 * t);
	float dome = top * mix(0.35, 1.0, sqrt(falloff));
	float height = clamp(WORLD_POSITION.y / dome, 0.0, 1.0);
	float vertical = 1.0 - smoothstep(0.35, 1.0, height);
	float n = texture(noise, (WORLD_POSITION - noise_offset) / noise_scale).r;
	DENSITY = density * falloff * vertical * mix(0.45, 1.25, n);
	ALBEDO = albedo;
	EMISSION = emission;
}
