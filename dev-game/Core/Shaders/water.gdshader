shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

// === Couleurs ===
uniform vec3 shallow_color : source_color = vec3(0.10, 0.55, 0.65);
uniform vec3 deep_color : source_color = vec3(0.00, 0.12, 0.30);
uniform vec3 foam_color : source_color = vec3(0.95, 0.97, 1.00);

// === Vagues (somme de sinus directionnels) ===
uniform float wave_speed = 1.0;
uniform float wave_height = 0.2;     // amplitude de la vague principale
uniform float wave_length = 1.0;     // fréquence (inverse de la longueur, comme avant)
uniform vec2  wave_dir_a = vec2( -1.0,  0.3);
uniform vec2  wave_dir_b = vec2(-0.6,  0.6);
uniform vec2  wave_dir_c = vec2( 0.2, -1.7);

// === Apparence ===
uniform float fresnel_power : hint_range(0.5, 8.0) = 6.0;
uniform float depth_distance : hint_range(0.1, 20.0) = -0.00;   // profondeur où l'eau devient pleinement opaque
uniform float foam_thickness : hint_range(0.0, 2.0) = 0.01;   // épaisseur de la mousse au bord des objets
uniform float roughness_value : hint_range(0.0, 1.0) = 0.18;
uniform float min_alpha : hint_range(0.0, 1.0) = 0.55;

uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;

varying float v_wave_height;

// Accumule la hauteur et les dérivées partielles d'une vague directionnelle
// pour reconstruire la normale analytiquement (plus stable qu'un sample voisin).
float sample_wave(vec2 InPos, vec2 InDir, float InAmp, float InFreq, float InTime, inout vec2 InOutDeriv)
{
	vec2 d = normalize(InDir);
	float phase = dot(d, InPos) * InFreq + InTime;
	InOutDeriv += d * (InAmp * InFreq * cos(phase));
	return sin(phase) * InAmp;
}

void vertex() {
	vec2 deriv = vec2(0.0);
	float h = 0.0;
	float t = TIME * wave_speed;

	// Trois vagues décalées en fréquence/amplitude/vitesse pour casser le motif sinusoïdal trop visible.
	h += sample_wave(VERTEX.xz, wave_dir_a, wave_height,        wave_length,        t,       deriv);
	h += sample_wave(VERTEX.xz, wave_dir_b, wave_height * 0.55, wave_length * 1.7,  t * 1.3, deriv);
	h += sample_wave(VERTEX.xz, wave_dir_c, wave_height * 0.30, wave_length * 2.6,  t * 0.7, deriv);

	VERTEX.y += h;
	NORMAL = normalize(vec3(-deriv.x, 1.0, -deriv.y));
	v_wave_height = h;
}

void fragment() {
	// Profondeur de la scène derrière l'eau (en espace vue, valeur positive)
	float scene_depth_raw = texture(depth_texture, SCREEN_UV).r;
	vec3  ndc  = vec3(SCREEN_UV * 2.0 - 1.0, scene_depth_raw);
	vec4  view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	view.xyz /= view.w;
	float scene_depth = -view.z;
	float frag_depth  = -VERTEX.z;
	float water_depth = max(0.0, scene_depth - frag_depth);

	// Couleur graduée selon la profondeur d'eau perçue
	float depth_t = clamp(water_depth / depth_distance, 0.0, 1.0);
	vec3  col = mix(shallow_color, deep_color, depth_t);

	// Mousse au contact des géométries immergées + sur les crêtes des vagues
	float shore_foam = 1.0 - smoothstep(0.0, foam_thickness, water_depth);
	float crest_foam = smoothstep(wave_height * 0.55, wave_height, v_wave_height) * 0.35;
	float foam = clamp(shore_foam + crest_foam, 0.0, 1.0);
	col = mix(col, foam_color, foam);

	// Fresnel : vue rasante => plus réfléchissant, vue plombante => plus transparent/coloré
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), fresnel_power);

	ALBEDO    = col;
	ROUGHNESS = roughness_value;
	METALLIC  = 0.0;
	SPECULAR  = mix(0.5, 1.0, fresnel);
	ALPHA     = clamp(mix(min_alpha, 1.0, depth_t) + foam, 0.0, 1.0);
}