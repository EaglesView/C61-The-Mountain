using Godot;
using System;

public partial class ChatContainer : Panel
{
	[Export] private LineEdit _textBox;
	[Export] private Button _openChatButton;

	private bool _isOpen = false;
	private bool _isChatFocused = false;
	private float _posYClosed = 0f;
	private float _posYOpen = 0f;
	private float _offsetButtonSize = 48f;

	private Tween _tween;

	public override void _Ready()
	{
		_posYOpen = Position.Y;
		_posYClosed = _posYOpen + (Size.Y - _offsetButtonSize);
		Position = new Vector2(Position.X, _posYClosed);
		_openChatButton.Text = $"Ouvrir le chat [{_getKeyName("open_chat")}]";
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("open_chat")) _toggleChat();
		if (@event.IsActionPressed("instant_chat")) _focusChat();
	}
	private string _getKeyName(string ActionName)
	{
		var actions = InputMap.ActionGetEvents("open_chat");
		if (actions.Count > 0)
		{
			return (string)actions[0].AsText().Substring(0, 1);
		}
		return "";
	}
	private void _toggleChat()
	{
		_isOpen = !_isOpen;
		float targetY = _isOpen ? _posYOpen : _posYClosed;
		GD.Print("Toggle Chat");
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(this, "position", new Vector2(Position.X, targetY), 1.0f);
	}
	private void _focusChat()
	{
		GD.Print("focus chat");
		if (!_isOpen) _toggleChat();

		_textBox.GrabFocus();
	}
}
