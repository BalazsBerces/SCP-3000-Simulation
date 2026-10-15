// Lasting properties of the Store's ground under Landmarks (the core's GroundProperty), for the
// materials that draw them: the dead zone, where no light ever comes on, and the fountain court's water.
// Each is one rectangle (centre in X and Z, half size), wrapped round the Store; zero size if the world has none.
global uniform vec4 ground_dark_rect;
global uniform vec4 ground_water_rect;
global uniform float ground_store_size;
// The time left until Closing time, in seconds, for the hands of the clock wall's clocks.
global uniform float time_to_closing;

// Whether a world position (X and Z) lies on a ground rectangle, edges included, at any wrapped image.
bool on_ground_rect(vec2 world_xz, vec4 rect) {
	if (rect.z <= 0.0 || ground_store_size <= 0.0) {
		return false;
	}
	vec2 delta = mod(world_xz - rect.xy + ground_store_size * 0.5, ground_store_size) - ground_store_size * 0.5;
	return all(lessThanEqual(abs(delta), rect.zw));
}
