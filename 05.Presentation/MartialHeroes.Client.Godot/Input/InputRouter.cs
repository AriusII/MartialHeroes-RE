using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Helpers;
using MartialHeroes.Shared.Kernel.Numerics;
using GodotInputMap = Godot.InputMap;
using GodotInput = Godot.Input;
using GodotDictionary = Godot.Collections.Dictionary;
using AppMouseButton = MartialHeroes.Client.Application.Input.MouseButton;
using AppInputEvent = MartialHeroes.Client.Application.Input.InputEvent;

namespace MartialHeroes.Client.Godot.Input;

/// <summary>
/// Captures raw Godot input events (_Input / _UnhandledInput) and translates them into
/// <see cref="AppInputEvent"/> values pushed into the <see cref="InputBus"/>. The bus then
/// walks the UI → world chain of responsibility; this router is deliberately passive.
///
/// Input routing priority (spec: Docs/RE/specs/input_ui.md §3 "UI is the gate"):
///   1. The HUD <see cref="IInputHandler"/> is registered FIRST in the InputBus (added at
///      composition root in <see cref="ClientContext"/>). It consumes UI panel clicks.
///   2. The WorldInputHandler is registered SECOND and handles entity-pick / click-to-move.
///
/// Godot events translated:
///   - InputEventMouseMotion      → InputType.MouseMove
///   - InputEventMouseButton      → InputType.MouseButtonDown / MouseButtonUp
///   - Mouse wheel                → InputType.MouseWheel
///   - InputEventKey              → InputType.KeyDown / KeyUp
///   - Hotbar actions (press)     → use-case call bypassing the bus (direct intent).
///
/// Threading contract: all Godot input callbacks run on the Godot main thread.
/// The InputBus.Enqueue is MPSC-safe, so the enqueue here is safe even if the loop drains
/// from a different thread in a future integration. For this wave the loop is driven from
/// SyntheticWorldFeeder / GameEngineLoop on a background Task, but all node mutations stay
/// on main thread.
///
/// spec: Docs/RE/specs/input_ui.md §6 ("Godot captures raw input and pushes it into the bus").
/// spec: Docs/RE/specs/input_ui.md §3 (UI before world chain of responsibility).
/// </summary>
public sealed partial class InputRouter : Node
{
    private ClientContext _clientContext = null!;
    private InputBus _inputBus = null!;

    // Camera reference for screen→world raycasting (click-to-move).
    private Camera3D? _camera;

    // Modifier flag constants.
    // spec: Docs/RE/specs/input_ui.md §2 — "modifier flags (Shift/Ctrl/Alt; bit mapping UNVERIFIED)".
    // We use the same convention as the original 20-byte normalised record at +16.
    // Exact bit mapping UNVERIFIED per spec §7; we document our convention here.
    private const int ModShift = 1; // bit 0 — UNVERIFIED exact bit; convention for .NET port.
    private const int ModCtrl = 2; // bit 1 — UNVERIFIED.
    private const int ModAlt = 4; // bit 2 — UNVERIFIED.

    /// <summary>Called by GameLoop._Ready before any input is processed.</summary>
    public void Initialise(ClientContext context, InputBus inputBus)
    {
        _clientContext = context;
        _inputBus = inputBus;
    }

    /// <summary>Kept for backward-compat signature used before the InputBus wave.</summary>
    public void Initialise(ClientContext context)
    {
        _clientContext = context;
        // InputBus will be set separately via InitialiseBus when the composition root wires it.
    }

    /// <summary>Provides the InputBus after construction (when Initialise(context) is called first).</summary>
    public void InitialiseBus(InputBus inputBus)
    {
        _inputBus = inputBus;
    }

    public override void _Ready()
    {
        // Defer camera lookup and world-handler wiring until after the scene is fully loaded.
        CallDeferred(MethodName.LateInitialise);
    }

    private void LateInitialise()
    {
        LookupCamera();
        WireWorldHandler();
    }

    private void WireWorldHandler()
    {
        // Register the world input handler into the InputBus.
        // spec: Docs/RE/specs/input_ui.md §3 — world handler is the second / last link.
        if (_clientContext is not null && _inputBus is not null)
        {
            IInputHandler worldHandler = CreateWorldInputHandler();
            // Reach the ClientContext to set the relay target.
            _clientContext.SetWorldInputHandler(worldHandler);
        }
    }

    private void LookupCamera() =>
        _camera = GetViewport().GetCamera3D();

    // -------------------------------------------------------------------------
    // Godot input callbacks — translate and push to InputBus
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles all input events; translates them to Application-layer <see cref="AppInputEvent"/>
    /// and enqueues them on the <see cref="InputBus"/> for deterministic dispatch at the next
    /// fixed tick. spec: Docs/RE/specs/input_ui.md §6.
    /// </summary>
    public override void _Input(global::Godot.InputEvent evt)
    {
        if (_inputBus is null)
        {
            return;
        }

        // Only route inputs when we are in World state (non-world states use their own UI).
        if (_clientContext?.StateMachine.Current != ClientState.World)
        {
            // Still handle hotbar in World state check below — done in _UnhandledInput.
            return;
        }

        switch (evt)
        {
            case InputEventMouseMotion motion:
            {
                int x = (int)motion.Position.X;
                int y = (int)motion.Position.Y;
                int mods = BuildModifiers(motion);
                // spec: Docs/RE/specs/input_ui.md §2 — type 3 = move.
                var e = new AppInputEvent(InputType.MouseMove, x, y, 0, mods);
                _inputBus.Enqueue(in e);
                break;
            }

            case InputEventMouseButton mouseBtn:
            {
                int x = (int)mouseBtn.Position.X;
                int y = (int)mouseBtn.Position.Y;
                int button = MapMouseButton(mouseBtn.ButtonIndex);
                int mods = BuildModifiers(mouseBtn);
                InputType type = mouseBtn.Pressed
                    ? InputType.MouseButtonDown // spec: input_ui.md §2 — type 5 = button press.
                    : InputType.MouseButtonUp;

                // Wheel events are encoded as button press in Godot.
                if (mouseBtn.ButtonIndex == global::Godot.MouseButton.WheelUp ||
                    mouseBtn.ButtonIndex == global::Godot.MouseButton.WheelDown)
                {
                    int delta = mouseBtn.ButtonIndex == global::Godot.MouseButton.WheelUp ? 1 : -1;
                    // spec: Docs/RE/specs/input_ui.md §2 — type 8 = wheel.
                    var wheelEvt = new AppInputEvent(InputType.MouseWheel, x, y, delta, mods);
                    _inputBus.Enqueue(in wheelEvt);
                    break;
                }

                var e = new AppInputEvent(type, x, y, button, mods);
                _inputBus.Enqueue(in e);
                break;
            }

            case InputEventKey key:
            {
                InputType type = key.Pressed ? InputType.KeyDown : InputType.KeyUp;
                int vk = (int)key.Keycode;
                int mods = BuildModifiers(key);
                var e = new AppInputEvent(type, 0, 0, vk, mods);
                _inputBus.Enqueue(in e);
                break;
            }
        }
    }

    // Chat input state: true while the chat bar is active.
    // spec: Docs/RE/specs/input_ui.md §4 — "Enter key toggles the chat input bar".
    private bool _chatActive;
    private string _chatDraft = string.Empty;

    /// <summary>
    /// Handles unhandled input — specifically the hotbar action-press path, click-to-move
    /// fallback, and the chat-input bar (Enter to toggle).
    /// spec: Docs/RE/specs/input_ui.md §3 — UI consumes first; world second.
    /// spec: Docs/RE/specs/input_ui.md §4 — Enter key → chat input; hotbar 1–9.
    /// </summary>
    public override void _UnhandledInput(global::Godot.InputEvent evt)
    {
        if (_clientContext?.StateMachine.Current != ClientState.World)
        {
            return;
        }

        // Handle key events for chat and hotbar.
        if (evt is InputEventKey key && key.Pressed)
        {
            // Enter key: toggle chat input bar or send current draft.
            // spec: Docs/RE/specs/input_ui.md §4 — "Enter toggles chat input".
            if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter)
            {
                if (_chatActive && !string.IsNullOrWhiteSpace(_chatDraft))
                {
                    // Send the current draft on general channel (0).
                    // spec: IApplicationUseCases.SendChatAsync; Docs/RE/packets/3-21_chat_channel.yaml.
                    _ = _clientContext.UseCases.SendChatAsync(0u, _chatDraft);
                    _chatDraft = string.Empty;
                    _chatActive = false;
                    GD.Print("[InputRouter] Chat sent.");
                }
                else
                {
                    _chatActive = !_chatActive;
                    if (!_chatActive) _chatDraft = string.Empty;
                    GD.Print($"[InputRouter] Chat input {(_chatActive ? "opened" : "closed")}.");
                }

                GetViewport().SetInputAsHandled();
                return;
            }

            // Escape: close chat without sending.
            if (_chatActive && key.Keycode == Key.Escape)
            {
                _chatActive = false;
                _chatDraft = string.Empty;
                GetViewport().SetInputAsHandled();
                return;
            }

            // While chat is active: accumulate printable keys into the draft.
            if (_chatActive)
            {
                if (key.Keycode == Key.Backspace && _chatDraft.Length > 0)
                {
                    _chatDraft = _chatDraft[..^1];
                }
                else if (key.Unicode > 0 && key.Unicode < 128)
                {
                    _chatDraft += (char)key.Unicode;
                }

                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Hotbar: skill slots 1–9 mapped to input actions "use_skill_1" … "use_skill_9".
        // Direct use-case calls — not dispatched through the InputBus (no spatial component).
        // spec: Docs/RE/specs/input_ui.md §4 — "hotbar 1–9 bind to CastSkillAsync".
        // We use UseSkillAsync (the lightweight path without the full gate chain) because
        // the full CastSkillAsync requires a SkillDefinition, caster state, and targeting query
        // that are not available here in the presentation layer (those are Application/Domain concerns).
        // TODO: if the gate chain is exposed via a simpler facade, replace UseSkillAsync.
        for (uint slot = 0; slot < 9; slot++)
        {
            string actionName = $"use_skill_{slot + 1}";
            if (GodotInputMap.HasAction(actionName) && GodotInput.IsActionJustPressed(actionName))
            {
                // spec: IApplicationUseCases.UseSkillAsync; Docs/RE/packets/2-52_use_skill.yaml.
                _ = _clientContext.UseCases.UseSkillAsync(
                    (byte)slot,
                    ReadOnlyMemory<uint>.Empty,
                    ReadOnlyMemory<uint>.Empty);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }

    // -------------------------------------------------------------------------
    // WorldInputHandler — implements IInputHandler for the "world" slot in the bus.
    //
    // The composition root registers:
    //   1. HudInputHandler (created by GameHud) — UI hit-test first.
    //   2. WorldInputHandler (created here) — click-to-move second.
    // spec: Docs/RE/specs/input_ui.md §3 (UI before world).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the world-side <see cref="IInputHandler"/> that handles click-to-move.
    /// Registered AFTER any UI handlers in the <see cref="InputBus"/>.
    /// spec: Docs/RE/specs/input_ui.md §3 — "world entity pick / ground move — only when UI fails".
    /// </summary>
    public IInputHandler CreateWorldInputHandler()
        => new WorldInputHandler(this);

    private sealed class WorldInputHandler : IInputHandler
    {
        private readonly InputRouter _router;

        public WorldInputHandler(InputRouter router)
        {
            _router = router;
        }

        /// <summary>
        /// Handles left-button-down events as click-to-move. Returns true to consume.
        /// spec: Docs/RE/specs/input_ui.md §3 — "(c) Otherwise, ground action — click-to-move".
        /// </summary>
        public bool TryHandle(in AppInputEvent e)
        {
            if (!e.IsLeftButtonDown)
            {
                return false;
            }

            return _router.HandleClickToMove(e.X, e.Y);
        }
    }

    // -------------------------------------------------------------------------
    // Click-to-move
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raycasts from the click position into the scene and forwards the world-space hit position
    /// to <see cref="MartialHeroes.Client.Application.UseCases.IApplicationUseCases.RequestMoveAsync"/>.
    /// Returns true if the raycast hit geometry (event consumed), false if it missed.
    /// spec: Vector3Fixed.FromFloat() — presentation boundary only.
    /// spec: WorldCoordinates helpers — coordinate system bridging.
    /// </summary>
    private bool HandleClickToMove(int screenX, int screenY)
    {
        if (_camera is null || _clientContext is null)
        {
            return false;
        }

        World3D world3D = _camera.GetWorld3D();
        PhysicsDirectSpaceState3D spaceState = world3D.DirectSpaceState;

        var screenPos = new Vector2(screenX, screenY);
        Vector3 from = _camera.ProjectRayOrigin(screenPos);
        Vector3 to = from + _camera.ProjectRayNormal(screenPos) * 1000f;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;

        GodotDictionary result = spaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return false;
        }

        Vector3 hitPoint = result["position"].AsVector3();

        // Convert Godot world space → legacy world space, then → Q16.16.
        // spec: WorldCoordinates helpers — coordinate system bridging.
        (float lx, float ly, float lz) = WorldCoordinates.ToLegacy(hitPoint.X, hitPoint.Y, hitPoint.Z);
        Vector3Fixed fixedTarget = Vector3Fixed.FromFloat(lx, ly, lz);

        // Forward the intent to the Application layer. Running = false (walk on click).
        _ = _clientContext.UseCases.RequestMoveAsync(fixedTarget, running: false);
        return true;
    }

    // -------------------------------------------------------------------------
    // Input translation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a Godot <see cref="Godot.MouseButton"/> to the legacy button index.
    /// spec: Docs/RE/specs/input_ui.md §1b — "1 = left, 2 = right, 3 = middle": CONFIRMED.
    /// </summary>
    private static int MapMouseButton(global::Godot.MouseButton button) => button switch
    {
        global::Godot.MouseButton.Left => AppMouseButton.Left, // spec: input_ui.md §1b — 1 = left. CONFIRMED.
        global::Godot.MouseButton.Right => AppMouseButton.Right, // spec: input_ui.md §1b — 2 = right. CONFIRMED.
        global::Godot.MouseButton.Middle => AppMouseButton.Middle, // spec: input_ui.md §1b — 3 = middle. CONFIRMED.
        _ => (int)button,
    };

    /// <summary>
    /// Builds a modifier bitmask from a Godot input event.
    /// Exact bit mapping UNVERIFIED per spec §7; our convention: Shift=bit0, Ctrl=bit1, Alt=bit2.
    /// spec: Docs/RE/specs/input_ui.md §2 (+16 modifier flags; bit mapping UNVERIFIED).
    /// </summary>
    private static int BuildModifiers(global::Godot.InputEvent evt)
    {
        int mods = 0;
        if (evt is InputEventWithModifiers m)
        {
            if (m.ShiftPressed) mods |= ModShift;
            if (m.CtrlPressed) mods |= ModCtrl;
            if (m.AltPressed) mods |= ModAlt;
        }

        return mods;
    }
}