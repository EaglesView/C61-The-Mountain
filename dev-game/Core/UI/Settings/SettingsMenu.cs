using Godot;
using System.Collections.Generic;

public partial class SettingsMenu : Control
{
	[Export] private TabContainer _tabs;
	[Export] private VBoxContainer _audioContainer;
	[Export] private VBoxContainer _controlsContainer;
	[Export] private Button _backButton;

	private static readonly string[] RebindableActions =
	{
		"move_up", "move_down", "move_left", "move_right",
		"jump", "run", "interact", "show_sign", "change_view",
		"create_sign", "pause_menu"
	};

	private static readonly Dictionary<string, string> ActionLabels = new()
	{
		{ "move_up",     "Avancer" },
		{ "move_down",   "Reculer" },
		{ "move_left",   "Gauche" },
		{ "move_right",  "Droite" },
		{ "jump",        "Sauter" },
		{ "run",         "Courir" },
		{ "interact",    "Interagir" },
		{ "show_sign",   "Montrer signe" },
		{ "change_view", "Changer vue" },
		{ "create_sign", "Créer signe" },
		{ "pause_menu",  "Pause" },
	};

	private const string ConfigPath = "user://settings.cfg";
	private readonly ConfigFile _config = new();
	private readonly Dictionary<string, Button> _actionButtons = new();

	private bool _waitingForRebind = false;
	private string _rebindAction = "";
	private Button? _rebindButton = null;
	private InputEvent? _pendingEvent = null;
	private string _conflictAction = "";

	public override void _Ready()
	{
		_config.Load(ConfigPath);
		_backButton.Pressed += () => QueueFree();
		_BuildAudioTab();
		_BuildControlsTab();
	}

	// ── Audio ─────────────────────────────────────────────────────────────────

	private void _BuildAudioTab()
	{
		for (int i = 0; i < AudioServer.BusCount; i++)
		{
			string busName = AudioServer.GetBusName(i);
			float savedLinear = (float)_config.GetValue("audio", busName.ToLower(), 1.0f);

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 12);

			var lbl = new Label();
			lbl.Text = busName;
			lbl.CustomMinimumSize = new Vector2(80, 0);
			lbl.SizeFlagsHorizontal = Control.SizeFlags.Fill;

			var slider = new HSlider();
			slider.MinValue = 0.0;
			slider.MaxValue = 1.0;
			slider.Step = 0.01;
			slider.Value = savedLinear;
			slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_ApplyBusVolume(i, savedLinear);

			var pctLabel = new Label();
			pctLabel.Text = $"{(int)(savedLinear * 100)}%";
			pctLabel.CustomMinimumSize = new Vector2(44, 0);
			pctLabel.HorizontalAlignment = HorizontalAlignment.Right;

			int busIdx = i;
			Label capturedPctLabel = pctLabel;
			slider.ValueChanged += val =>
			{
				_ApplyBusVolume(busIdx, (float)val);
				capturedPctLabel.Text = $"{(int)(val * 100)}%";
				_config.SetValue("audio", AudioServer.GetBusName(busIdx).ToLower(), val);
				_config.Save(ConfigPath);
			};

			row.AddChild(lbl);
			row.AddChild(slider);
			row.AddChild(capturedPctLabel);
			_audioContainer.AddChild(row);
		}
	}

	private static void _ApplyBusVolume(int busIdx, float linear)
	{
		AudioServer.SetBusMute(busIdx, linear <= 0f);
		AudioServer.SetBusVolumeDb(busIdx, linear > 0f ? Mathf.LinearToDb(linear) : -80f);
	}

	// ── Controls ──────────────────────────────────────────────────────────────

	private void _BuildControlsTab()
	{
		foreach (string action in RebindableActions)
		{
			string displayName = ActionLabels.TryGetValue(action, out var n) ? n : action;

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 12);

			var lbl = new Label();
			lbl.Text = displayName;
			lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			var btn = new Button();
			btn.Text = _GetPrimaryKeyName(action);
			btn.CustomMinimumSize = new Vector2(120, 0);
			btn.TooltipText = "Cliquer pour reconfigurer";

			_actionButtons[action] = btn;

			string capturedAction = action;
			btn.Pressed += () => _StartRebind(capturedAction, btn);

			row.AddChild(lbl);
			row.AddChild(btn);
			_controlsContainer.AddChild(row);
		}
	}

	private static string _GetPrimaryKeyName(string action)
	{
		var events = InputMap.ActionGetEvents(action);
		foreach (var ev in events)
		{
			if (ev is InputEventKey key)
				return key.AsTextPhysicalKeycode();
			if (ev is InputEventJoypadButton joy)
				return $"Btn {joy.ButtonIndex}";
		}
		return "---";
	}

	private void _StartRebind(string action, Button btn)
	{
		if (_waitingForRebind) return;
		_waitingForRebind = true;
		_rebindAction = action;
		_rebindButton = btn;
		btn.Text = "...";
	}

	public override void _Input(InputEvent @event)
	{
		if (!_waitingForRebind) return;
		if (@event is not InputEventKey && @event is not InputEventJoypadButton) return;
		if (!@event.IsPressed() || @event.IsEcho()) return;

		string conflict = _FindConflict(@event, _rebindAction);
		if (conflict != "")
		{
			_pendingEvent = @event;
			_conflictAction = conflict;
			_waitingForRebind = false;
			_ShowConflictDialog(conflict);
		}
		else
		{
			_ApplyRebind(@event, clearConflict: false);
		}

		GetViewport().SetInputAsHandled();
	}

	private string _FindConflict(InputEvent @event, string excludeAction)
	{
		foreach (string action in RebindableActions)
		{
			if (action == excludeAction) continue;
			foreach (var ev in InputMap.ActionGetEvents(action))
			{
				if (@event is InputEventKey newKey && ev is InputEventKey existingKey
					&& newKey.PhysicalKeycode == existingKey.PhysicalKeycode)
					return action;
				if (@event is InputEventJoypadButton newBtn && ev is InputEventJoypadButton existingBtn
					&& newBtn.ButtonIndex == existingBtn.ButtonIndex)
					return action;
			}
		}
		return "";
	}

	private void _ShowConflictDialog(string conflictAction)
	{
		string conflictLabel = ActionLabels.TryGetValue(conflictAction, out var n) ? n : conflictAction;

		var dialog = new ConfirmationDialog();
		dialog.Title = "Conflit de touche";
		dialog.DialogText = $"Cette touche est déjà assignée à \"{conflictLabel}\".\nRetirer l'assignation existante ?";
		dialog.OkButtonText = "Remplacer";
		dialog.CancelButtonText = "Annuler";

		dialog.Confirmed += () =>
		{
			_ApplyRebind(_pendingEvent!, clearConflict: true);
			dialog.QueueFree();
		};
		dialog.Canceled += () =>
		{
			if (_rebindButton != null)
				_rebindButton.Text = _GetPrimaryKeyName(_rebindAction);
			_CancelRebind();
			dialog.QueueFree();
		};

		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void _ApplyRebind(InputEvent @event, bool clearConflict)
	{
		if (clearConflict && _conflictAction != "")
		{
			InputMap.ActionEraseEvents(_conflictAction);
			if (_actionButtons.TryGetValue(_conflictAction, out var conflictBtn))
				conflictBtn.Text = "---";
			_config.SetValue("controls", _conflictAction, "");
		}

		InputMap.ActionEraseEvents(_rebindAction);
		InputMap.ActionAddEvent(_rebindAction, @event);

		if (_rebindButton != null)
			_rebindButton.Text = _GetPrimaryKeyName(_rebindAction);

		_SaveRebind(_rebindAction, @event);
		_CancelRebind();
	}

	private void _CancelRebind()
	{
		_waitingForRebind = false;
		_rebindAction = "";
		_rebindButton = null;
		_pendingEvent = null;
		_conflictAction = "";
	}

	private void _SaveRebind(string action, InputEvent @event)
	{
		if (@event is InputEventKey key)
			_config.SetValue("controls", action, (int)key.PhysicalKeycode);
		_config.Save(ConfigPath);
	}

	// ── Static helpers ────────────────────────────────────────────────────────

	public static void ApplySettings()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(ConfigPath) != Error.Ok) return;

		for (int i = 0; i < AudioServer.BusCount; i++)
		{
			string busName = AudioServer.GetBusName(i);
			float linear = (float)cfg.GetValue("audio", busName.ToLower(), 1.0f);
			_ApplyBusVolume(i, linear);
		}

		foreach (string action in RebindableActions)
		{
			if (!cfg.HasSectionKey("controls", action)) continue;
			var stored = cfg.GetValue("controls", action);
			if (stored.VariantType != Variant.Type.Int) continue;
			int keycode = (int)stored;
			var ev = new InputEventKey { PhysicalKeycode = (Key)keycode };
			InputMap.ActionEraseEvents(action);
			InputMap.ActionAddEvent(action, ev);
		}
	}
}
