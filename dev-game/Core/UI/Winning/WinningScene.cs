using Godot;

/// <summary>
/// Conteneur de l'écran Winning&#160;: possède les trois slides empilées
/// (<c>SCR_01_WHOWINS</c>, <c>SCR_02_SUBWINNERS</c>, <c>SCR_3_VOTEMAP</c>) et
/// pilote les transitions par tween entre elles. Re-émet les signaux UI
/// pertinents pour que <c>WinningController</c> n'ait à connaître que ce
/// contrat&#160;: <see cref="MapVoteCast"/>, <see cref="ContinuePressed"/>,
/// <see cref="VoteConfirmed"/>, <see cref="QuitPressed"/>,
/// <see cref="LaunchNewLobby"/>.
/// </summary>
public partial class WinningScene : Node3D
{
	/// <summary>Émis quand un joueur clique sur une carte (vote local, slide 3).</summary>
	[Signal] public delegate void MapVoteCastEventHandler(string InMapId);

	/// <summary>Émis quand l'hôte/joueur clique sur «&#160;Continue&#160;» (slide 2).</summary>
	[Signal] public delegate void ContinuePressedEventHandler();

	/// <summary>Émis quand l'hôte confirme le vote (slide 3, bouton CONFIRM).</summary>
	[Signal] public delegate void VoteConfirmedEventHandler(string InMapId);

	/// <summary>Émis quand l'utilisateur quitte volontairement (slide 3, QUIT).</summary>
	[Signal] public delegate void QuitPressedEventHandler();

	/// <summary>
	/// Signal historique pour rétro-compatibilité&#160;: déclenche le démarrage
	/// d'une nouvelle partie (le <c>WinningController</c> y écoute encore).
	/// </summary>
	[Signal] public delegate void LaunchNewLobbyEventHandler();

	/// <summary>Durée par défaut des tweens de slide (secondes).</summary>
	[Export] private float _slideTweenDuration = 0.6f;

	private WinningSlide1 _slide1;
	private WinningSlide2 _slide2;
	private WinningSlide3 _slide3;
	private Tween _slide1Tween;
	private Tween _slide2Tween;

	private const float ViewportHeight = 1080f;

	private static readonly Vector2 SlideOutUp = new(0f, -ViewportHeight);

	/// <summary>Identifiant de la map sélectionnée localement sur la slide 3.</summary>
	public string SelectedMapId => _slide3?.SelectedMapId ?? "";

	/// <summary>
	/// État initial de l'écran&#160;: les trois slides sont empilées à (0,0) dans
	/// l'ordre de tscn (slide 3 derrière, slide 1 devant). Rien à animer ici.
	/// </summary>
	public void TweenToSlide1() { }

	/// <summary>
	/// Fait glisser la slide 1 vers le haut hors-champ pour révéler la slide 2
	/// déjà positionnée derrière. Délègue la révélation du bouton
	/// «&#160;Continue&#160;» au <c>WinningController</c>
	/// (cf. <see cref="ShowSlide2ContinueButton"/>).
	/// </summary>
	public void TweenToSlide2()
	{
		TweenSlide(_slide1, ref _slide1Tween, SlideOutUp);
	}

	/// <summary>
	/// Fait glisser la slide 2 vers le haut pour révéler la slide 3 déjà
	/// positionnée derrière, puis la peuple via <see cref="WinningSlide3.Populate"/>.
	/// </summary>
	public void TweenToSlide3()
	{
		TweenSlide(_slide2, ref _slide2Tween, SlideOutUp);
		_slide3?.Populate();
	}

	/// <summary>
	/// Variante «&#160;skip slide 2&#160;»&#160;: utilisée quand il n'y a pas de
	/// sous-gagnants à montrer. On masque slide 2 (qui n'a jamais été révélée)
	/// pour éviter qu'elle ne flashe vide entre slide 1 et slide 3, puis on
	/// lève slide 1 pour révéler slide 3 directement.
	/// </summary>
	public void TweenSkipToSlide3()
	{
		if (_slide2 is not null) _slide2.Visible = false;
		TweenSlide(_slide1, ref _slide1Tween, SlideOutUp);
		_slide3?.Populate();
	}

	/// <summary>Révèle le bouton «&#160;Continue&#160;» de la slide 2.</summary>
	public void ShowSlide2ContinueButton() => _slide2?.ShowContinueButton();

	/// <summary>
	/// Pousse le résultat du décompte courant à la slide 3. Appelé par
	/// <c>WinningController</c> à chaque mise à jour reçue du serveur (ou
	/// localement en offline).
	/// </summary>
	public void ApplyVoteTally(Godot.Collections.Dictionary<string, int> InCounts, string InMajorityMapId)
		=> _slide3?.ApplyTally(InCounts, InMajorityMapId);

	/// <summary>
	/// Indique à la slide 3 si ce client est habilité à confirmer le vote
	/// (hôte du lobby, ou joueur unique en offline). Doit être appelé après
	/// <see cref="TweenToSlide3"/> (qui exécute <c>Populate</c>).
	/// </summary>
	public void SetIsHost(bool InIsHost) => _slide3?.SetIsHost(InIsHost);

	public override void _Ready()
	{
		_slide1 = GetNodeOrNull<WinningSlide1>("Winning_UI/SCR_01_WHOWINS");
		_slide2 = GetNodeOrNull<WinningSlide2>("Winning_UI/SCR_02_SUBWINNERS");
		_slide3 = GetNodeOrNull<WinningSlide3>("Winning_UI/SCR_3_VOTEMAP");

		if (_slide2 is not null) _slide2.ContinuePressed += () => EmitSignal(SignalName.ContinuePressed);
		if (_slide3 is not null)
		{
			_slide3.MapVoteCast += (string id) => EmitSignal(SignalName.MapVoteCast, id);
			_slide3.VoteConfirmed += OnSlide3VoteConfirmed;
			_slide3.QuitPressed += () => EmitSignal(SignalName.QuitPressed);
		}
	}

	public override void _ExitTree()
	{
		_slide1Tween?.Kill();
		_slide2Tween?.Kill();
	}

	private void OnSlide3VoteConfirmed(string InMapId)
	{
		EmitSignal(SignalName.VoteConfirmed, InMapId);
		EmitSignal(SignalName.LaunchNewLobby);
	}

	/// <summary>
	/// Anime le déplacement d'une slide en glissant son coin haut-gauche
	/// (<c>position</c>) vers <paramref name="InTarget"/>. On passe par
	/// <c>position</c> plutôt que par les 4 offsets parce que les slides sont
	/// des MarginContainer (Container), et leur re-sort interne peut écraser
	/// les offsets mis à jour individuellement&#160;: <c>position</c> est un point
	/// d'entrée plus haut niveau qui met à jour offsets+size de façon
	/// atomique pour Godot.
	/// </summary>
	private void TweenSlide(Control InSlide, ref Tween InOutTween, Vector2 InTarget)
	{
		if (InSlide is null) return;
		InOutTween?.Kill();
		InOutTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		InOutTween.TweenProperty(InSlide, "position", InTarget, _slideTweenDuration);
	}
}
