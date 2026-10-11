// Shared by native FogVolumes and the Lamp attenuation pass.
uniform float core_fraction = 0.35;
uniform float top = 18.0;
uniform sampler3D noise : repeat_enable;
uniform vec3 noise_offset = vec3(0.0);
uniform float noise_scale = 70.0;

float bank_density(vec3 p, vec2 centre, float bank_radius, float peak) {
	float across = length(p.xz - centre) / bank_radius;
	if (across >= 1.0 || p.y < 0.0 || p.y >= top) {
		return 0.0;
	}
	float t = clamp((across - core_fraction) / (1.0 - core_fraction), 0.0, 1.0);
	float falloff = 1.0 - t * t * (3.0 - 2.0 * t);
	float dome = top * mix(0.35, 1.0, sqrt(falloff));
	float height = clamp(p.y / dome, 0.0, 1.0);
	float vertical = 1.0 - smoothstep(0.35, 1.0, height);
	float n = texture(noise, (p - noise_offset) / noise_scale).r;
	return peak * falloff * vertical * mix(0.45, 1.25, n);
}
