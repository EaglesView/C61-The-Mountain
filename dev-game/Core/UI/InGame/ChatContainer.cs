using Godot;
using System;
using Core.Auth;
using Core.Chat;
using Core.Chat.Domain;

public partial class ChatContainer : Panel
{
	[Export] private LineEdit _textBox;
	[Export] private Button _openChatButton;
	[Export] private VBoxContainer _messagesList;
	[Export] private PackedScene _chatItemScene;
	[Export] private string _roomId = "global";

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

		_textBox.TextSubmitted += _onTextSubmitted;
		ChatServiceProvider.Instance.MessageReceived += _onMessageReceived;
		ChatServiceProvider.Instance.Subscribe(_roomId);
	}

	public override void _ExitTree()
	{
		ChatServiceProvider.Instance.MessageReceived -= _onMessageReceived;
		ChatServiceProvider.Instance.Unsubscribe();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("open_chat")) _toggleChat();
		if (@event.IsActionPressed("instant_chat")) _focusChat();
	}

	private async void _onTextSubmitted(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		var user = AuthServiceProvider.Instance.CurrentUser;
		if (user is null) return;
		_textBox.Clear();
		try
		{
			await ChatServiceProvider.Instance.SendAsync(_roomId, user.Id, user.Username, text);
		}
		catch (Exception ex)
		{
			GD.PushError($"[Chat] Send failed: {ex.Message}");
		}
	}

	private void _onMessageReceived(ChatMessage msg)
	{
		if (_messagesList is null || _chatItemScene is null) return;
		var item = _chatItemScene.Instantiate();
		_messagesList.AddChild(item);

		var nameLabel = item.GetNodeOrNull<Label>("VBoxContainer/PlayerNameChat");
		var timeLabel = item.GetNodeOrNull<Label>("VBoxContainer/Label");
		var textLabel = item.GetNodeOrNull<Label>("Label");

		if (nameLabel is not null) nameLabel.Text = msg.Username;
		if (timeLabel is not null)
			timeLabel.Text = DateTimeOffset.FromUnixTimeMilliseconds(msg.TimestampMs).ToLocalTime().ToString("HH:mm");
		if (textLabel is not null) textLabel.Text = msg.Text;
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
