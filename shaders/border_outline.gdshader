shader_type canvas_item;

// TEXTURE holds ownership colors; relief never participates in border detection.
uniform vec2 texel_size;
uniform sampler2D terrain_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform vec3 sea_owner_color : source_color = vec3(0.08, 0.16, 0.24);
uniform vec3 wild_owner_color : source_color = vec3(0.12, 0.12, 0.12);

void fragment() {
	vec4 owner = texture(TEXTURE, UV);
	vec3 terrain = texture(terrain_texture, UV).rgb;
	bool sea = distance(owner.rgb, sea_owner_color) < 0.015;
	bool wild = distance(owner.rgb, wild_owner_color) < 0.015;
	float relief = dot(terrain, vec3(0.299, 0.587, 0.114));
	vec3 tint = owner.rgb * (0.40 + relief);
	// Sea/unclaimed land show the physical terrain as-is; owned
	// territory blends its political color with the terrain underneath
	// (52%) so relief/coastline detail still reads through the tint.
	vec3 fill = (sea || wild) ? terrain : mix(terrain, tint, 0.52);
	// Border sample offset uses whichever is larger of a fixed texel
	// distance or the screen-space derivative (fwidth) of UV - the
	// latter shrinks/grows with zoom, so borders stay a consistent
	// ~1-pixel visual thickness whether zoomed out to the whole map or
	// in close, instead of a fixed number of TEXTURE texels that would
	// look razor-thin zoomed in or aliased zoomed out.
	vec2 px = max(texel_size * 0.65, fwidth(UV) * 0.65);
	float edge = 0.0;
	edge += step(0.02, distance(owner.rgb, texture(TEXTURE, UV + vec2(px.x, 0.0)).rgb));
	edge += step(0.02, distance(owner.rgb, texture(TEXTURE, UV - vec2(px.x, 0.0)).rgb));
	edge += step(0.02, distance(owner.rgb, texture(TEXTURE, UV + vec2(0.0, px.y)).rgb));
	edge += step(0.02, distance(owner.rgb, texture(TEXTURE, UV - vec2(0.0, px.y)).rgb));
	vec3 ink = sea ? vec3(0.09, 0.18, 0.20) : vec3(0.18, 0.17, 0.13);
	// Accumulating a soft step per direction (0-4) instead of a hard
	// boolean OR gives an anti-aliased border instead of a stark
	// single-texel black line.
	COLOR = vec4(mix(fill, ink, min(edge * 0.26, 0.72)), owner.a);
}
