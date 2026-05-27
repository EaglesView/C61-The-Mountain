using Godot;
using Core.Auth;
using Core.Auth.Application;
using Core.Network.Rooms;
using Core.Network;

public partial class Login : Control
{
	[Export] public LineEdit EmailField;
	[Export] public LineEdit UsernameField;
	[Export] public LineEdit PasswordField;
	[Export] public Button LoginButton;
	[Export] public Button SignUpButton;
	[Export] public Label ErrorLabel;
	[Export] public LineEdit GuestName;
	[Export] public Button GuestStart;
	private AuthUseCase _auth = null!;
	private LineEdit _emailField = null!;
	private LineEdit _usernameField = null!;
	private LineEdit _passwordField = null!;
	private Button _loginButton = null!;
	private Button _signUpButton = null!;
	private LineEdit _guestName = null;
	private Button _guestStart = null;
	private Label? _errorLabel;

	private string _emailGuestStart = "invite";
	private string _emailGuestEnd = "@outilsenligne.ca";
	private string _emailPWDGuest = "guest0";

	public override void _Ready()
	{
		if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
		{
			// Defer&#160;: changer de scène depuis _Ready essaie un remove_child sur un
			// parent encore en train d'ajouter ses enfants (l'erreur «&#160;Parent node
			// is busy adding/removing children&#160;»). On laisse la frame se terminer.
			CallDeferred(MethodName.GoToMainController);
			return;
		}

		_auth = AuthServiceProvider.Instance;

		_emailField = EmailField;
		_usernameField = UsernameField;
		_passwordField = PasswordField;
		_loginButton = LoginButton;
		_signUpButton = SignUpButton;
		_errorLabel = ErrorLabel;
		_guestName = GuestName;
		_guestStart = GuestStart;
		_loginButton.Pressed += OnLoginPressed;
		_signUpButton.Pressed += OnSignUpPressed;
		_guestStart.Pressed += OnGuestStartPressed;

	}

	private void GoToMainController()
	{
		GetTree().ChangeSceneToFile("res://Core/Logic/main_controller.tscn");
	}

	private async void OnLoginPressed()
	{
		if (_auth.IsAuthenticated)
		{
			GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
			return;
		}

		SetLoading(true);
		ClearError();
		var result = await _auth.SignInAsync(_emailField.Text, _passwordField.Text);
		SetLoading(false);

		if (result.Success)
		{
			GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
		}
		else
		{
			ShowError(result.ErrorMessage ?? "Sign-in failed.");
		}
	}

	private async void OnSignUpPressed()
	{
		SetLoading(true);
		ClearError();
		var result = await _auth.SignUpAsync(_emailField.Text, _passwordField.Text, _usernameField.Text);
		SetLoading(false);

		if (result.Success)
		{
			GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
		}
		else
		{
			ShowError(result.ErrorMessage ?? "Sign-up failed.");
		}
	}

	private async void OnGuestStartPressed()
	{
		if (_auth.IsAuthenticated)
		{
			GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
			return;
		}

		SetLoading(true);
		ClearError();

		// Flux invité piloté par le serveur&#160;: on ouvre ENet d'abord, on
		// demande au serveur le prochain slot libre (GuestSlotRegistry), puis
		// on signe à Firebase avec les creds correspondants. Le serveur est
		// la seule source de vérité pour "ce slot est-il pris&#160;?" — Firebase
		// accepterait sans broncher la même session depuis plusieurs clients.
		try
		{
			await NetworkManager.Instance.ConnectAndAwaitAsync(Room.HardcodedServerIp, Room.HardcodedServerPort);
			int slot = await NetworkManager.Instance.RequestGuestSlotAsync();
			if (slot < 1)
			{
				SetLoading(false);
				ShowError("Aucun slot invité disponible. Réessayez dans un instant.");
				return;
			}

			string email = $"{_emailGuestStart}{slot}{_emailGuestEnd}";
			string password = $"{_emailPWDGuest}{slot}";
			var result = await _auth.SignInAsync(email, password);
			if (!result.Success)
			{
				SetLoading(false);
				// La réservation côté serveur expirera d'elle-même (TTL) — pas
				// besoin de RPC de libération explicite. Slot disponible à
				// nouveau dans quelques secondes.
				ShowError(result.ErrorMessage ?? "Guest sign-in failed.");
				return;
			}

			Core.Auth.GuestSession.LocalSlotIndex = slot;
			SetLoading(false);
			GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
		}
		catch (System.Exception ex)
		{
			SetLoading(false);
			ShowError($"Connexion invité impossible&#160;: {ex.Message}");
		}
	}

	private void SetLoading(bool loading)
	{
		_loginButton.Disabled = loading;
		_signUpButton.Disabled = loading;
		if (_guestStart != null) _guestStart.Disabled = loading;
		_loginButton.Text = loading ? "Signing in…" : "Login";
		_signUpButton.Text = loading ? "…" : "Sign Up";
	}

	private void ShowError(string message)
	{
		if (_errorLabel is not null)
		{
			_errorLabel.Text = message;
			_errorLabel.Visible = true;
		}
	}

	private void ClearError()
	{
		if (_errorLabel is not null)
		{
			_errorLabel.Text = "";
			_errorLabel.Visible = false;
		}
	}
}
