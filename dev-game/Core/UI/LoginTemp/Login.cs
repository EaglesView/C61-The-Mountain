using Godot;
using Core.Auth;
using Core.Auth.Application;
using Core.Network.Rooms;
using Core.Network;

public partial class Login : Control
{
	private AuthUseCase _auth = null!;
	private LineEdit _emailField = null!;
	private LineEdit _usernameField = null!;
	private LineEdit _passwordField = null!;
	private Button _loginButton = null!;
	private Button _signUpButton = null!;
	private Label? _errorLabel;

	private LineEdit _devIpField = null!;
	private Button? _devConnectBtn;

	private const string GameScene = "res://Core/World/world.tscn";

	public override void _Ready()
	{
		if (OS.HasFeature("dedicated_server") || DisplayServer.GetName() == "headless")
		{
			GetTree().ChangeSceneToFile(GameScene);
			return;
		}

		var net = NetworkManager.Instance;
		if (net.IsAutoConnecting)
		{
			net.LocalConnected += GoToGame;
			return;
		}

		_auth = AuthServiceProvider.Instance;

		_emailField = GetNode<LineEdit>("VBoxContainer/EmailField");
		_usernameField = GetNode<LineEdit>("VBoxContainer/UsernameField");
		_passwordField = GetNode<LineEdit>("VBoxContainer/PasswordField");
		_loginButton = GetNode<Button>("VBoxContainer/LoginButton");
		_signUpButton = GetNode<Button>("VBoxContainer/SignUpButton");
		_errorLabel = GetNodeOrNull<Label>("VBoxContainer/ErrorLabel");

		_loginButton.Pressed += OnLoginPressed;
		_signUpButton.Pressed += OnSignUpPressed;

		BuildDevPanel();
	}

	// ── Dev quick-start panel (MVP only) ─────────────────────────────────────

	private void BuildDevPanel()
	{
		var root = GetNode<VBoxContainer>("VBoxContainer");

		root.AddChild(new HSeparator());

		var title = new Label();
		title.Text = "── Dev Quick Start ──";
		root.AddChild(title);

		var ipRow = new HBoxContainer();
		_devIpField = new LineEdit();
		_devIpField.PlaceholderText = "Server IP (leave empty for local)";
		_devIpField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		ipRow.AddChild(_devIpField);
		root.AddChild(ipRow);

		_devConnectBtn = new Button();
		_devConnectBtn.Text = "Connect to IP";
		_devConnectBtn.Pressed += OnDevConnectPressed;
		root.AddChild(_devConnectBtn);

		var localBtn = new Button();
		localBtn.Text = "Play Local (offline)";
		localBtn.Pressed += OnDevLocalPressed;
		root.AddChild(localBtn);
	}

	private void OnDevConnectPressed()
	{
		var ip = _devIpField.Text.Trim();
		if (ip.Length == 0) ip = "127.0.0.1";
		LobbyState.SetDirect(ip);
		if (_devConnectBtn != null) _devConnectBtn.Disabled = true;
		var net = NetworkManager.Instance;
		net.LocalConnected += GoToGame;
		net.ConnectToServer(ip, 7777);
	}

	private void GoToGame(int _)
	{
		NetworkManager.Instance.LocalConnected -= GoToGame;
		GetTree().ChangeSceneToFile(GameScene);
	}

	private void OnDevLocalPressed()
	{
		LobbyState.Clear();
		GetTree().ChangeSceneToFile(GameScene);
	}

	private async void OnLoginPressed()
	{
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

	private void SetLoading(bool loading)
	{
		_loginButton.Disabled = loading;
		_signUpButton.Disabled = loading;
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
