using Godot;
using System.Collections.Generic;

public partial class SettingsMenu : Control
{
	[Export] private TabContainer _tabs;
	[Export] private VBoxContainer _audioContainer;
	[Export] private VBoxContainer _controlsContainer;
	[Export] private VBoxContainer _graphicsContainer;
	[Export] private Button _backButton;
	[Export] private Button _applyButton;

	private const float DesignHeight = 1080f;

	// Staged graphics changes — only committed on Apply. Sentinel values mean "no pending change".
	private int _pendingWindowMode = -1;
	private Vector2I _pendingResolution = Vector2I.Zero;
	private float _pendingUiScale = float.NaN;

	private static readonly string[] RebindableActions =
	{
		"move_up", "move_down", "move_left", "move_right",
		"jump", "run", "interact", "show_sign", "change_view",
		"create_sign", "pause_menu"
	};
	private static readonly Vector2I[] Resolutions =
	{
	  new(1280, 720),   // Steam Deck
	  new(1366, 768),
	  new(1600, 900),
	  new(1920, 1080),
	  new(2560, 1440),
	  new(3840, 2160),  // ecole
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
		_applyButton.Pressed += _OnApplyPressed;
		_applyButton.Disabled = true;
		_BuildAudioTab();
		_BuildControlsTab();
		_BuildGraphicsTab();
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

	// -- Graphics --
	private void _BuildGraphicsTab()
	{
		// Display mode
		var modeRow = new HBoxContainer();
		modeRow.AddChild(new Label { Text = "Affichage", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var modeOpt = new OptionButton();
		modeOpt.AddItem("Fenêtre", 0);
		modeOpt.AddItem("Plein écran borderless", 1);
		modeOpt.AddItem("Plein écran", 2);
		modeOpt.Selected = (int)_config.GetValue("graphics", "window_mode", 0);
		modeOpt.ItemSelected += idx =>
		{
			_pendingWindowMode = (int)idx;
			_MarkDirty();
		};
		modeRow.AddChild(modeOpt);
		_graphicsContainer.AddChild(modeRow);

		// Resolution
		var resRow = new HBoxContainer();
		resRow.AddChild(new Label { Text = "Résolution", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var resOpt = new OptionButton();
		var saved = (Vector2I)_config.GetValue("graphics", "resolution", new Vector2I(1920, 1080));
		var screenSize = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
		int visualIdx = 0;
		int selectedVisual = 0;
		for (int i = 0; i < Resolutions.Length; i++)
		{
			var r = Resolutions[i];
			if (r.X > screenSize.X || r.Y > screenSize.Y) continue;
			resOpt.AddItem($"{r.X} × {r.Y}", i);   // id = Resolutions index
			if (r == saved) selectedVisual = visualIdx;
			visualIdx++;
		}
		resOpt.Selected = selectedVisual;
		resOpt.ItemSelected += idx =>
		{
			int id = resOpt.GetItemId((int)idx);
			_pendingResolution = Resolutions[id];
			_MarkDirty();
		};
		resRow.AddChild(resOpt);
		_graphicsContainer.AddChild(resRow);

		// VSync
		var vsRow = new HBoxContainer();
		vsRow.AddChild(new Label { Text = "VSync", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var vsCheck = new CheckButton { ButtonPressed = (bool)_config.GetValue("graphics", "vsync", true) };
		vsCheck.Toggled += on =>
		{
			DisplayServer.WindowSetVsyncMode(on ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
			_config.SetValue("graphics", "vsync", on);
			_config.Save(ConfigPath);
		};
		vsRow.AddChild(vsCheck);
		_graphicsContainer.AddChild(vsRow);

		// UI scale — multiplies the visual size of every UI element (accessibility).
		// Layout adaptivity is already handled by stretch=canvas_items+aspect=expand;
		// this slider is for users who want the HUD physically bigger or smaller.
		var scaleRow = new HBoxContainer();
		scaleRow.AddChild(new Label { Text = "Échelle UI", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var scaleSlider = new HSlider
		{
			MinValue = 0.75, MaxValue = 1.5, Step = 0.05,
			Value = (float)_config.GetValue("graphics", "ui_scale", 1.0f),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(160, 0),
		};
		var scalePct = new Label
		{
			Text = $"{(int)(scaleSlider.Value * 100)}%",
			CustomMinimumSize = new Vector2(48, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		scaleSlider.ValueChanged += val =>
		{
			_pendingUiScale = (float)val;
			scalePct.Text = $"{(int)(val * 100)}%";
			_MarkDirty();
		};
		scaleRow.AddChild(scaleSlider);
		scaleRow.AddChild(scalePct);
		_graphicsContainer.AddChild(scaleRow);
	}

	private void _ApplyContentScale(float factor)
	{
		GetTree().Root.ContentScaleFactor = factor;
	}

	private void _MarkDirty()
	{
		_applyButton.Disabled = false;
	}

	private void _OnApplyPressed()
	{
		bool needsRestart = false;

		// Window mode stays live (no restart). Apply before any other graphics
		// change in case the user combines it with a resolution swap on relaunch.
		if (_pendingWindowMode >= 0)
		{
			_ApplyWindowMode(_pendingWindowMode);
			_config.SetValue("graphics", "window_mode", _pendingWindowMode);
			_pendingWindowMode = -1;
		}

		// Resolution and UI scale are launch-locked: persist now, take effect
		// on next launch via SettingsMenu.ApplySettings.
		if (_pendingResolution != Vector2I.Zero)
		{
			var current = (Vector2I)_config.GetValue("graphics", "resolution", new Vector2I(1920, 1080));
			if (current != _pendingResolution) needsRestart = true;
			_config.SetValue("graphics", "resolution", _pendingResolution);
			_pendingResolution = Vector2I.Zero;
		}

		if (!float.IsNaN(_pendingUiScale))
		{
			var current = (float)_config.GetValue("graphics", "ui_scale", 1.0f);
			if (!Mathf.IsEqualApprox(current, _pendingUiScale)) needsRestart = true;
			_config.SetValue("graphics", "ui_scale", _pendingUiScale);
			_pendingUiScale = float.NaN;
		}

		_config.Save(ConfigPath);
		_applyButton.Disabled = true;

		if (needsRestart)
		{
			ErrorDialog.Show(
				GetTree(),
				"Le changement de résolution et/ou d'échelle UI sera appliqué au prochain démarrage du jeu.",
				"Redémarrage requis",
				"OK");
		}
	}
	private static void _ApplyWindowMode(int mode)
	{
		DisplayServer.WindowSetMode(mode switch
		{
			1 => DisplayServer.WindowMode.Fullscreen,            // borderless fullscreen
			2 => DisplayServer.WindowMode.ExclusiveFullscreen,
			_ => DisplayServer.WindowMode.Windowed,
		});
	}

	private static void _ApplyResolution(Vector2I size)
	{
		if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) return;
		DisplayServer.WindowSetSize(size);
		var screen = DisplayServer.ScreenGetSize();
		DisplayServer.WindowSetPosition((screen - size) / 2);
	}
	// ── Static helpers ────────────────────────────────────────────────────────

	public static void ApplySettings(SceneTree tree)
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

		// Graphics — order matters: set window mode first (fullscreen ignores
		// size requests), then resolution if windowed, then vsync, then ui scale.
		int windowMode = (int)cfg.GetValue("graphics", "window_mode", 0);
		_ApplyWindowMode(windowMode);

		if (windowMode == 0 && cfg.HasSectionKey("graphics", "resolution"))
		{
			var res = (Vector2I)cfg.GetValue("graphics", "resolution", new Vector2I(1920, 1080));
			_ApplyResolution(res);
		}

		bool vsync = (bool)cfg.GetValue("graphics", "vsync", true);
		DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

		float uiScale = (float)cfg.GetValue("graphics", "ui_scale", 1.0f);
		tree.Root.ContentScaleFactor = uiScale;
	}
}
