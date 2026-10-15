// The dead zone's column of dark air: plain black, unlit and sharp-edged, so from afar the dead zone reads as a
// black hole. Only its outside faces are drawn, so from inside it the lit Store around it shows.
shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;

void fragment() {
	ALBEDO = vec3(0.0);
}
