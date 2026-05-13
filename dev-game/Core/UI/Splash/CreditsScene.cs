using Godot;

public partial class CreditsScene : Control
{
	[Export] private Control _creditsContent;
	[Export] private Button _backButton;

	private const float ScrollSpeed = 45.0f;
	private float _viewportHeight;

	public override void _Ready()
	{
		_viewportHeight = GetViewportRect().Size.Y;
		_creditsContent.Position = new Vector2(_creditsContent.Position.X, _viewportHeight);
		_backButton.Pressed += GoBack;
	}

	public override void _Process(double delta)
	{
		var pos = _creditsContent.Position;
		pos.Y -= ScrollSpeed * (float)delta;
		_creditsContent.Position = pos;

		if (pos.Y + _creditsContent.Size.Y < 0)
			_creditsContent.Position = new Vector2(pos.X, _viewportHeight);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("pause_menu"))
			GoBack();
	}

	private void GoBack()
	{
		GetTree().ChangeSceneToFile("res://Core/UI/MainMenu/main_menu.tscn");
	}
}
