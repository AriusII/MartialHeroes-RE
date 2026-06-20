using System;
using System.Collections.Generic;
using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Scene;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Scene.Controllers;
using MartialHeroes.Shared.Kernel.Enums;

namespace MartialHeroes.Client.Godot.Scene;

/// <summary>
/// The presentation root — the faithful Godot counterpart of the legacy application entry point
/// that <b>is</b> the scene state machine. It owns the live engine state (via
/// <see cref="SceneStateMachine"/>) and keeps exactly one <see cref="ISceneController"/> node in
/// the tree, swapping it whenever the engine state changes — the dispatch half of the legacy
/// bounds-checked <c>switch</c>.
/// spec: Docs/RE/specs/client_runtime.md §7 (the entry point is the state machine; each case
/// builds + runs one scene; commit → re-dispatch).
/// </summary>
/// <remarks>
/// PASSIVE: this node holds zero game rules. It reads the engine state and instantiates the
/// matching scene controller; all transition policy lives in the engine-free
/// <see cref="SceneStateMachine"/>. The spine advances on real signals only
/// (<see cref="SceneStateChangedEvent"/> from the <see cref="SceneStateMachine"/>).
/// </remarks>
public sealed partial class SceneHost : Node
{
	private readonly Dictionary<EngineSceneState, Func<ISceneController>> _factories = new()
	{
		[EngineSceneState.Init] = static () => new InitScene(),
		[EngineSceneState.Login] = static () => new LoginScene(),
		[EngineSceneState.Load] = static () => new LoadScene(),
		[EngineSceneState.Opening] = static () => new OpeningScene(),
		[EngineSceneState.Select] = static () => new SelectScene(),
		[EngineSceneState.InGame] = static () => new InGameScene(),
		[EngineSceneState.Quit] = static () => new QuitScene(),
		[EngineSceneState.Error] = static () => new ErrorScene(),
	};

	private SceneStateMachine _machine = null!;
	private ISceneController? _current;
	private EngineSceneState? _currentState;

	/// <summary>The live engine state currently rendered.</summary>
	public EngineSceneState CurrentState => _machine.Current.State;

	public override void _Ready()
	{
		_machine = ResolveSceneMachine();
		GD.Print($"[SceneHost] ready — boot state {(int)_machine.Current.State} {_machine.Current.State}.");
		ShowSceneFor(_machine.Current.State);
	}

	/// <summary>
	/// Performs the engine-internal advance and re-syncs the live scene node to the new state.
	/// No-op (and logs the terminal stop) when the machine is terminal or has nothing to advance.
	/// spec: Docs/RE/specs/client_runtime.md §7.2 / §7.5.1.
	/// </summary>
	public void Advance()
	{
		EngineSceneState before = _machine.Current.State;

		if (!_machine.AdvanceScene())
		{
			GD.Print($"[SceneHost] state {(int)before} {before} — no further advance (spine settled).");
			return;
		}

		ShowSceneFor(_machine.Current.State);
	}

	/// <summary>
	/// Re-syncs the live scene node to the engine state WITHOUT advancing the machine — the
	/// convergence hook for OUT-OF-BAND transitions. Some transitions are pre-committed on the
	/// engine-free machine by a packet handler (e.g. the 3/5 EnterGameAck commits Login→Load and
	/// publishes <see cref="SceneStateChangedEvent"/>); because the <c>IClientEventBus</c> is
	/// SingleReader, only the active scene controller drains it, so that controller must drive the
	/// controller swap. Idempotent — safe to call on every out-of-band event.
	/// spec: Docs/RE/specs/world_entry.md (out-of-band scene transition → controller convergence).
	/// </summary>
	public void SyncToCurrentState() => ShowSceneFor(_machine.Current.State);

	private void ShowSceneFor(EngineSceneState state)
	{
		// Idempotent: if the controller for this state is already live, do nothing. This makes
		// SyncToCurrentState() cheap to call on every out-of-band SceneStateChangedEvent.
		if (_current is not null && _currentState == state)
		{
			return;
		}

		if (_current is not null)
		{
			_current.Node.QueueFree();
			_current = null;
			_currentState = null;
		}

		if (!_factories.TryGetValue(state, out Func<ISceneController>? factory))
		{
			GD.PushError($"[SceneHost] no controller registered for state {(int)state} {state}.");
			return;
		}

		ISceneController scene = factory();
		AddChild(scene.Node);
		_current = scene;
		_currentState = state;
		scene.OnEnter(this);
	}

	private SceneStateMachine ResolveSceneMachine()
	{
		ClientContext? ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
		if (ctx?.SceneMachine is { } machine)
		{
			return machine;
		}

		throw new InvalidOperationException(
			"[SceneHost] ClientContext.SceneMachine is unavailable. " +
			"The ClientContext autoload must be present and fully initialised before SceneHost._Ready runs.");
	}
}
