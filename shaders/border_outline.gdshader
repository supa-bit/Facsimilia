shader_type canvas_item;

// Draws a border wherever the ownership texture changes color between
// neighboring texels. This is the whole trick that makes freeform,
// pixel-resolution borders possible: no polygon math, no geometry to
// corrupt on a territory change - just compare a cell to its neighbors.
// The source texture MUST use nearest-neighbor filtering, otherwise GPU
// texture interpolation blurs owner colors across the border and this
// stops detecting anything.

uniform vec2 texel_size;
uniform vec4 border_color : source_color = vec4(0.05, 0.05, 0.05, 1.0);
uniform float border_thickness = 1.0;

void fragment() {
	vec4 center = texture(TEXTURE, UV);
	vec2 px = texel_size * border_thickness;

	vec4 left  = texture(TEXTURE, UV - vec2(px.x, 0.0));
	vec4 right = texture(TEXTURE, UV + vec2(px.x, 0.0));
	vec4 up    = texture(TEXTURE, UV - vec2(0.0, px.y));
	vec4 down  = texture(TEXTURE, UV + vec2(0.0, px.y));

	bool differs = distance(center.rgb, left.rgb) > 0.01
		|| distance(center.rgb, right.rgb) > 0.01
		|| distance(center.rgb, up.rgb) > 0.01
		|| distance(center.rgb, down.rgb) > 0.01;

	COLOR = differs ? border_color : center;
}
