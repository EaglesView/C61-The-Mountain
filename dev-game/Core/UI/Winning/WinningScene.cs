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
	private Tween _slide3Tween;

	private const float ViewportWidth = 1920f;
	private const float ViewportHeight = 1080f;

	/// <summary>Identifiant de la map sélectionnée localement sur la slide 3.</summary>
	public string SelectedMapId => _slide3?.SelectedMapId ?? "";

	/// <summary>
	/// Anime l'arrivée de la slide 1 (état initial de l'écran). Les autres slides
	/// sont supposées être hors-écran à ce point.
	/// </summary>
	public void TweenToSlide1()
	{
		TweenSlideOffsets(_slide1, ref _slide1Tween, 0f, 0f, 0f, 0f);
	}

	/// <summary>
	/// Fait glisser la slide 1 vers le haut hors-champ et la slide 2 depuis le
	/// haut jusqu'au centre. Délègue la révélation du bouton «&#160;Continue&#160;»
	/// au <c>WinningController</c> (cf. <see cref="ShowSlide2ContinueButton"/>).
	/// </summary>
	public void TweenToSlide2()
	{
		TweenSlideOffsets(_slide1, ref _slide1Tween, 0f, -ViewportHeight, 0f, -ViewportHeight);
		TweenSlideOffsets(_slide2, ref _slide2Tween, 0f, 0f, 0f, 0f);
	}

	/// <summary>
	/// Fait glisser la slide 2 vers le haut et la slide 3 depuis la droite. À
	/// l'entrée, la slide 3 est peuplée par <see cref="WinningSlide3.Populate"/>.
	/// </summary>
	public void TweenToSlide3()
	{
		TweenSlideOffsets(_slide2, ref _slide2Tween, 0f, -ViewportHeight, 0f, -ViewportHeight);
		TweenSlideOffsets(_slide3, ref _slide3Tween, 0f, 0f, 0f, 0f);
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
		_slide3Tween?.Kill();
	}

	private void OnSlide3VoteConfirmed(string InMapId)
	{
		EmitSignal(SignalName.VoteConfirmed, InMapId);
		EmitSignal(SignalName.LaunchNewLobby);
	}

	private void TweenSlideOffsets(Control InSlide, ref Tween InOutTween, float InOffsetLeft, float InOffsetTop, float InOffsetRight, float InOffsetBottom)
	{
		if (InSlide is null) return;
		InOutTween?.Kill();
		InOutTween = CreateTween().SetParallel(true).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
		InOutTween.TweenProperty(InSlide, "offset_left", InOffsetLeft, _slideTweenDuration);
		InOutTween.TweenProperty(InSlide, "offset_top", InOffsetTop, _slideTweenDuration);
		InOutTween.TweenProperty(InSlide, "offset_right", InOffsetRight, _slideTweenDuration);
		InOutTween.TweenProperty(InSlide, "offset_bottom", InOffsetBottom, _slideTweenDuration);
	}
}
