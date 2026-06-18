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
/// The CAMPAIGN-15 presentation root — the faithful Godot counterpart of the legacy application
/// entry point that <b>is</b> the scene state machine. It owns the live engine state (via
/// <see cref="SceneStateMachine"/>) and keeps exactly one <see cref="ISceneController"/> node in the
/// tree, swapping it whenever the engine state changes — the dispatch half of the legacy
/// bounds-checked <c>switch</c>. spec: Docs/RE/specs/client_runtime.md §7 (the entry point is the
/// state machine; each case builds + runs one scene; commit → re-dispatch).
/// </summary>
/// <remarks>
/// <para>
/// PASSIVE: this node holds zero game rules. It reads the engine state and instantiates the matching
/// scene controller; all transition policy lives in the engine-free <see cref="SceneStateMachine"/>.
/// </para>
/// <para>
/// During the scene-by-scene rebuild every controller is a thin stub
/// (<see cref="StubSceneController"/>); each is filled in its own increment. A developer
/// <em>auto-walk</em> (enabled headless or via <c>MH_SCENE_AUTOWALK=1</c>) advances the
/// non-interactive spine automatically so the headless verify loop can confirm every state
/// dispatches; it stops at In-game (5) to keep the walk finite (5 → 4 would otherwise loop).
/// </para>
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
    private bool _autoWalk;

    /// <summary>The live engine state currently rendered.</summary>
    public EngineSceneState CurrentState => _machine.Current.State;

    public override void _Ready()
    {
        _machine = ResolveSceneMachine();
        _autoWalk = DisplayServer.GetName() == "headless"
                    || OS.GetEnvironment("MH_SCENE_AUTOWALK") == "1";

        GD.Print($"[SceneHost] ready — boot state {(int)_machine.Current.State} "
                 + $"{_machine.Current.State} (auto-walk={_autoWalk}).");

        ShowSceneFor(_machine.Current.State);
    }

    /// <summary>
    /// Performs the engine-internal advance (the legacy case body's next-state write) and re-syncs
    /// the live scene node to the new state. No-op (and logs the terminal stop) when the machine is
    /// terminal or has nothing further to advance. spec: Docs/RE/specs/client_runtime.md §7.2 / §7.5.1.
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

    private void ShowSceneFor(EngineSceneState state)
    {
        if (_current is not null)
        {
            _current.Node.QueueFree();
            _current = null;
        }

        if (!_factories.TryGetValue(state, out Func<ISceneController>? factory))
        {
            GD.PushError($"[SceneHost] no controller registered for state {(int)state} {state}.");
            return;
        }

        ISceneController scene = factory();
        AddChild(scene.Node);
        _current = scene;
        scene.OnEnter(this);

        MaybeAutoWalk(state);
    }

    private void MaybeAutoWalk(EngineSceneState state)
    {
        // Stop the dev walk at In-game (5) — the deepest forward state — so it stays finite.
        // Init (0) is excluded: InitScene self-advances to Login in EVERY mode (the faithful automatic
        // state-0→1 transition), so auto-walking it too would double-advance via a stale timer.
        if (!_autoWalk || _machine.IsTerminal
                       || state == EngineSceneState.Init
                       || state == EngineSceneState.InGame)
        {
            return;
        }

        // In layout-dump mode, hold each scene much longer so its async layout dump completes before
        // the walk advances (the login dump snaps the curtain + opens the server-list & PIN sub-views).
        double delay = Dev.LayoutDump.Enabled ? 6.0 : 0.2;
        SceneTreeTimer timer = GetTree().CreateTimer(delay);
        timer.Timeout += Advance;
    }

    private SceneStateMachine ResolveSceneMachine()
    {
        ClientContext? ctx = GetNodeOrNull<ClientContext>("/root/ClientContext");
        if (ctx?.SceneMachine is { } machine)
        {
            return machine;
        }

        // Defensive fallback: if the composition root is unavailable, run a standalone machine on a
        // throwaway bus so the host still boots (mirrors ClientContext's own fallback policy).
        GD.PushWarning("[SceneHost] ClientContext.SceneMachine unavailable — using a standalone fallback machine.");
        return new SceneStateMachine(new ClientEventBus(ClientEventBus.DefaultCapacity));
    }
}