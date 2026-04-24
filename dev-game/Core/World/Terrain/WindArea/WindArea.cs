using Core.Network;
using Godot;
using System;

public partial class WindArea : Node3D
{
	/// <summary> L'état du vent </summary>
	enum WindState { Aggressive, Normal }

	/// <summary>
	/// La node de Zone
	/// </summary>
	[Export] public Area3D WindZone;
	/// <summary>
	/// Taille de la CollisionBox
	/// </summary>
	[Export(PropertyHint.Range, "0.1f,1000.0f,0.1f,suffix:m")] public Vector2 BoxSize = new Vector2(1.0f, 1.0f);
	/// <summary>
	/// Durée de l'instance de la boite de vent totale
	/// </summary>
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f,suffix:sec")] public float DurationAreaTotal = 6.0f;
	/// <summary>
	/// La force du vent.
	/// </summary>
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f,suffix:sec")] public float WindForce = 6.0f;
	/// <summary>
	/// La direction du vent.
	/// </summary>
	[Export(PropertyHint.Range, "1.0f,120.0f,0.1f,suffix:sec")] public Vector3 WindDirection = new Vector3(0f, 0f, 6f);
	/// <summary>
	/// Durée de l'instance de la boite de vent aggressive, à la fin de la durée totale.
	/// </summary>
	[Export(PropertyHint.Range, "0.0f,120.0f,0.1f,suffix:sec")] public float DurationAreaAggressive = 1.0f;

	private float _timeToAggressive = 0f;
	private float _timeToStop = 0f;
	private bool _aggressive = false;
	private WindState _currentState;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_currentState = WindState.Normal;
		_timeToStop = DurationAreaTotal;
		WindZone.BodyEntered += OnZoneBodyEntered;
		WindZone.BodyExited += OnZoneBodyExited;
	}

	private void OnZoneBodyEntered(Node3D body)
	{
		if (body is Character character && character.GetCurrentState() != Utils.CharacterUtils.CharacterState.Paused)
		{
			switch (_currentState)
			{
				case WindState.Normal:
					character.Velocity += WindForce * WindDirection;
					break;
				case WindState.Aggressive:
					if (character.PeerId == NetworkManager.Instance.LocalPeerId)
					{
						character.PhysicsSkelton.ApplyRagdollKick(WindForce * WindDirection);
						character.TransitionTo(Utils.CharacterUtils.CharacterState.Ragdoll);
					}
					break;
			}
		}
		//Ajout des particules

		//TODO: Changer l'environnement ici pour rendre plus sombre
	}
	private void OnZoneBodyExited(Node3D body)
	{
		return;
	}


	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		_timeToStop -= (float)delta;
		//state changes avant l'action au joueur
		if (_timeToStop > DurationAreaAggressive)
		{
			_currentState = WindState.Aggressive;
		}
		if (_currentState == WindState.Aggressive)
		{
			//pitcher le joueur par les bones
		}
		else
		{
			//pousser le joueur par le characterbody3D
		}
	}
}
