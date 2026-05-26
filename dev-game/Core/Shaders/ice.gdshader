// Saw a few of these in Unreal Engine and Unity but no Godot :(
// So I made one, check other comments for free textures and more info on the shader
// Godot 4.6 port: refraction/parallax computed in tangent space so the effect
// is consistent on tilted, curved, or vertical ice surfaces (not only flat ground).
shader_type spatial;

uniform sampler2D over_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D under_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D surface_normalmap : hint_normal, filter_linear_mipmap, repeat_enable;
// Here's a free CC0 ice texture -> https://ambientcg.com/view?id=Ice003
// You can use that for the over_texture and surface_normalmap
// Here's another CC0 ice texture -> https://ambientcg.com/view?id=Ice002
// You can use the displacement map of this texture as the under_texture

uniform vec4 top_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float depth : hint_range(0.0, 1.0, 0.005) = 0.1;
uniform float normal_depth : hint_range(0.0, 4.0, 0.05) = 1.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.7;
uniform float refraction_intensity : hint_range(0.0, 1.0, 0.005) = 0.1;
// Évite l'étirement extrême du parallax aux angles rasants : limite l'offset
// quand le rayon de vue devient parallèle à la surface.
uniform float min_view_cosine : hint_range(0.01, 1.0, 0.01) = 0.15;
// Tiling du calque supérieur (over_texture + surface_normalmap). À monter
// quand le mesh est grand et que ses UVs étirent les textures.
uniform float surface_uv_scale : hint_range(0.1, 200.0, 0.1) = 1.0;
// Tiling du calque inférieur (under_texture). Indépendant pour permettre
// un grain "profondeur" différent du grain de surface.
uniform float under_uv_scale : hint_range(0.1, 200.0, 0.1) = 1.0;

// ========= ↓↓↓ From u/lexpartizan the GOAT ↓↓↓ ==============
float blendOverlay_f(float base, float blend) {
	float branchless = step (base,0.5);
	return (2.0*base*blend)*branchless + (1.0-2.0*(1.0-base)*(1.0-blend))*(1.0-branchless); //This is branchless version
}
// ======== ↑↑↑ From u/lexpartizan the GOAT ↑↑↑ ==============

vec3 blendOverlay(vec3 base, vec3 blend) {
	return vec3(blendOverlay_f(base.r,blend.r),blendOverlay_f(base.g,blend.g),blendOverlay_f(base.b,blend.b));
}

void fragment(){
	vec2 surface_uv = UV * surface_uv_scale;
	vec3 normal_sample = texture(surface_normalmap, surface_uv).xyz;
	NORMAL_MAP = normal_sample;
	NORMAL_MAP_DEPTH = normal_depth;

	// Base TBN en espace vue. La transposée projette un vecteur view-space
	// vers l'espace tangent local (où Z = normale de surface).
	mat3 to_tangent = transpose(mat3(TANGENT, BINORMAL, NORMAL));
	vec3 view_ts = normalize(to_tangent * VIEW); // pointe du fragment vers la caméra

	// Normale perturbée par la normal map, exprimée en tangent space.
	vec3 n_ts = normalize(normal_sample * 2.0 - 1.0);

	// Parallax offset classique : projection du rayon caméra sur le plan
	// tangent, mis à l'échelle par la profondeur virtuelle. Le clamp évite
	// l'explosion de l'offset à incidence rasante.
	float cos_view = max(view_ts.z, min_view_cosine);
	vec2 parallax = -(view_ts.xy / cos_view) * depth;

	// La distorsion par la normal map vit déjà dans le même tangent space,
	// donc elle reste cohérente quel que soit l'orientation de la surface.
	vec2 offset = parallax + n_ts.xy * refraction_intensity;

	vec3 over_color = texture(over_texture, surface_uv).rgb;
	vec3 color = blendOverlay(over_color, top_color.rgb);
	vec3 under_color = texture(under_texture, UV * under_uv_scale + offset).rgb;
	ALBEDO = blendOverlay(color, under_color);
	ROUGHNESS = roughness;
	METALLIC = metallic;
}
