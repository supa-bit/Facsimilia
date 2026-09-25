shader_type canvas_item;

// TEXTURE holds ownership colors; relief never participates in border detection.
uniform vec2 texel_size;
uniform sampler2D terrain_texture : source_color, filter_linear_mipmap, repeat_disable;
uniform vec3 sea_owner_color : source_color = vec3(0.08, 0.16, 0.24);
uniform vec3 wild_owner_color : source_color = vec3(0.12, 0.12, 0.12);
// Province of every cell, as a 16-bit id in red (low byte) and green (high byte).
uniform sampler2D province_texture : filter_nearest, repeat_disable;
uniform float province_strength = 0.0;  // 0 hides province borders, 1 shows them fully
uniform int selected_province = 0;
// The "Regions" overlay: region id per population node (the same lon/lat
// projection at a coarser grid), a colour per id, and 0 (off) or 1 (on).
uniform sampler2D region_texture : filter_nearest, repeat_disable;
uniform sampler2D region_palette : filter_nearest, repeat_disable;
uniform float region_strength = 0.0;
// Land views: one value per population node (0-255) coloured by a ramp
// whose alpha lets the terrain show through; 0 (off) or 1 (on).
uniform sampler2D land_texture : filter_linear, repeat_disable;
uniform sampler2D land_ramp : filter_linear, repeat_disable;
uniform float land_strength = 0.0;
// Conquest mode: which population nodes the player's army can reach this
// year (1) or not (0); land out of reach is dimmed while reach_strength is 1.
uniform sampler2D reach_texture : filter_nearest, repeat_disable;
uniform float reach_strength = 0.0;

int region_at(vec2 uv) {
	return int(round(texture(region_texture, uv).r * 255.0));
}

int province_at(vec2 uv) {
	vec4 c = texture(province_texture, uv);
	return int(round(c.r * 255.0)) + int(round(c.g * 255.0)) * 256;
}

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
	int province = province_at(UV);
	bool selected = province != 0 && province == selected_province;
	// Owned land outside every province (unorganized) shows paler: less of
	// the realm's colour, more bare terrain.
	if (!sea && !wild && province == 0)
		fill = mix(fill, terrain, 0.45);
	int region = region_at(UV);
	bool show_regions = region_strength > 0.0;
	bool show_land = land_strength > 0.0;
	bool overlay = show_regions || show_land;
	// With the Regions overlay on, land shows its region's colour over the
	// terrain instead of the realms'.
	if (show_regions && !sea) {
		// Islets too small for the population grid have no region: bare terrain.
		vec3 rc = region > 0 ? texelFetch(region_palette, ivec2(region, 0), 0).rgb : vec3(0.0);
		vec3 shown = region > 0 ? mix(terrain, rc * (0.40 + relief), 0.58) : terrain;
		fill = mix(fill, shown, region_strength);
	}
	if (show_land && !sea) {
		// Land views show bare terrain under the ramp, not realm colours.
		float v = texture(land_texture, UV).r;
		vec4 rc = texture(land_ramp, vec2(v, 0.5));
		fill = mix(fill, mix(terrain, rc.rgb * (0.55 + 0.6 * relief), rc.a), land_strength);
	}
	if (reach_strength > 0.0 && !sea && texture(reach_texture, UV).r < 0.5)
		fill = mix(fill, fill * 0.45, reach_strength);
	if (selected && !overlay)
		fill = mix(fill, vec3(1.0, 0.93, 0.75), 0.22);
	// Border sample offset uses whichever is larger of a fixed texel
	// distance or the screen-space derivative (fwidth) of UV - the
	// latter shrinks/grows with zoom, so borders stay a consistent
	// ~1-pixel visual thickness whether zoomed out to the whole map or
	// in close, instead of a fixed number of TEXTURE texels that would
	// look razor-thin zoomed in or aliased zoomed out.
	vec2 px = max(texel_size * 0.65, fwidth(UV) * 0.65);
	vec2 offsets[4] = vec2[](vec2(px.x, 0.0), vec2(-px.x, 0.0), vec2(0.0, px.y), vec2(0.0, -px.y));
	float edge = 0.0;          // realm borders and coasts
	float province_edge = 0.0; // borders between provinces of the same realm
	float selected_edge = 0.0; // the selected province's outline
	float region_edge = 0.0;   // borders between regions (overlay only)
	for (int i = 0; i < 4; i++) {
		vec2 uv = UV + offsets[i];
		vec3 other = texture(TEXTURE, uv).rgb;
		float owner_change = step(0.02, distance(owner.rgb, other));
		if (overlay) {
			// Only coasts stay; realm borders give way to the overlay.
			bool other_sea = distance(other, sea_owner_color) < 0.015;
			owner_change = (sea != other_sea) ? 1.0 : 0.0;
		}
		if (show_regions) {
			int other_region = region_at(uv);
			if (!sea && other_region != region && other_region > 0 && region > 0)
				region_edge += 1.0;
		}
		edge += owner_change;
		int neighbour = province_at(uv);
		if (neighbour != province) {
			province_edge += 1.0 - owner_change;
			if (selected || (selected_province != 0 && neighbour == selected_province))
				selected_edge += 1.0;
		}
	}
	vec3 ink = sea ? vec3(0.09, 0.18, 0.20) : vec3(0.18, 0.17, 0.13);
	// Accumulating a soft step per direction (0-4) instead of a hard
	// boolean OR gives an anti-aliased border instead of a stark
	// single-texel black line. Province borders are fainter than realm ones.
	float provinces_shown = province_strength * (1.0 - max(region_strength, land_strength));
	vec3 color = mix(fill, ink, min(province_edge * 0.16 * provinces_shown, 0.4));
	color = mix(color, ink, min(edge * 0.26, 0.72));
	color = mix(color, ink, min(region_edge * 0.3 * region_strength, 0.75));
	if (!overlay)
		color = mix(color, vec3(1.0, 0.86, 0.45), min(selected_edge * 0.45, 0.9));
	COLOR = vec4(color, owner.a);
}
