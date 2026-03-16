using Godot;
using Core.Auth;
using Core.Auth.Application;

public partial class Login : Control
{
	private AuthUseCase _auth = null!;
	private LineEdit _emailField = null!;
	private LineEdit _usernameField = null!;
	private LineEdit _passwordField = null!;
	private Button _loginButton = null!;
	private Button _signUpButton = null!;
	private Label? _errorLabel;

	public override void _Ready()
	{
		_auth = AuthServiceProvider.Instance;

		_emailField = GetNode<LineEdit>("VBoxContainer/EmailField");
		_usernameField = GetNode<LineEdit>("VBoxContainer/UsernameField");
		_passwordField = GetNode<LineEdit>("VBoxContainer/PasswordField");
		_loginButton = GetNode<Button>("VBoxContainer/LoginButton");
		_signUpButton = GetNode<Button>("VBoxContainer/SignUpButton");
		_errorLabel = GetNodeOrNull<Label>("VBoxContainer/ErrorLabel");

		_loginButton.Pressed += OnLoginPressed;
		_signUpButton.Pressed += OnSignUpPressed;
	}

	private async void OnLoginPressed()
	{
		SetLoading(true);
		ClearError();
		var result = await _auth.SignInAsync(_emailField.Text, _passwordField.Text);
		SetLoading(false);

		if (result.Success)
		{
			GetTree().ChangeSceneToFile("res://Core/Dev/world_jim.tscn");
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
			GetTree().ChangeSceneToFile("res://Core/Dev/world_jim.tscn");
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
