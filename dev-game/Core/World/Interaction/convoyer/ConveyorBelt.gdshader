shader_type spatial;

render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform float scroll_offset = 0.0;
uniform float stripe_density = 18.0;
uniform float stripe_strength = 0.18;
uniform float edge_width = 0.16;

uniform vec4 center_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform vec4 edge_color : source_color = vec4(1.0, 0.9, 0.0, 1.0);

void fragment()
{
	float u = UV.x + scroll_offset;

	float stripe_wave = sin(u * stripe_density * 6.28318530718);
	float stripe_mask = 0.5 + 0.5 * stripe_wave;

	float edge_mask = step(UV.y, edge_width) + step(1.0 - edge_width, UV.y);
	edge_mask = clamp(edge_mask, 0.0, 1.0);

	vec3 base = mix(center_color.rgb, edge_color.rgb, edge_mask);

	float motionPattern = mix(1.0 - stripe_strength, 1.0 + stripe_strength, stripe_mask);

	ALBEDO = base * motionPattern;
	ROUGHNESS = 0.9;
}