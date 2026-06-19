using Godot;
using MartialHeroes.Client.Application.Events;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Helpers;
using MartialHeroes.Shared.Kernel.Enums;
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

    // Click-synthesis latch state: records the press position and button so that on release at the
    // same-ish position we can emit a MouseButtonClick (type 6) event.
    // spec: Docs/RE/specs/input_ui.md §2b — "latch widget on press, synthesise click on same-widget release".
    // The 2-px drag tolerance is byte-confirmed in input_ui.md §2b.
    private int _pressX = -1;
    private int _pressY = -1;
    private int _pressButton = -1;

    private const int
        ClickDragTolerance = 2; // spec: Docs/RE/specs/input_ui.md §2b (drag tolerance = 2, byte-confirmed).

    // Modifier flag constants — recovered bit positions.
    // spec: Docs/RE/specs/input_ui.md §2c — "Modifier-flag encoding (recovered bit positions)".
    //   bit3 (0x8) = key slot 1014 — confirmed Alt.
    //   bit2 (0x4) = key slot 1012  (Ctrl-vs-Shift label capture/debugger-pending).
    //   bit1 (0x2) = key slot 1013  (Ctrl-vs-Shift label capture/debugger-pending).
    //   bit0 (0x1) = keyboard auto-repeat flag (NOT a Shift/Ctrl/Alt modifier) — reserved, not emitted.
    // The bit POSITIONS are confirmed; only the Ctrl/Shift identity of slots 1012/1013 is pending,
    // so we bind Shift→0x4 (slot 1012) and Ctrl→0x2 (slot 1013) as the current best-effort mapping.
    // spec: Docs/RE/specs/input_ui.md §2c.
    private const int ModShift = 0x4; // bit2 — key slot 1012. spec: input_ui.md §2c.
    private const int ModCtrl = 0x2; // bit1 — key slot 1013. spec: input_ui.md §2c.
    private const int ModAlt = 0x8; // bit3 — key slot 1014, confirmed Alt. spec: input_ui.md §2c.

    /// <summary>
    /// Called by GameLoop._Ready before any input is processed.
    /// The InputBus is supplied separately via <see cref="InitialiseBus"/> when the composition
    /// root wires it (see GameLoop._Ready: Initialise(context) then InitialiseBus(context.InputBus)).
    /// </summary>
    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    /// <summary>Provides the InputBus after construction (when Initialise(context) is called first).</summary>
    public void InitialiseBus(InputBus inputBus)
    {
        _inputBus = inputBus;
    }

    public override void _Ready()
    {
        GD.Print("[InputRouter] _Ready start");

        // Defer camera lookup and world-handler wiring until after the scene is fully loaded.
        // Guard: LateInitialise exceptions must not crash the scene.
        CallDeferred(MethodName.LateInitialise);
    }

    private void LateInitialise()
    {
        try
        {
            LookupCamera();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InputRouter] LookupCamera failed: {ex.Message}");
        }

        try
        {
            WireWorldHandler();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InputRouter] WireWorldHandler failed: {ex.Message}");
        }
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

        // Only route inputs when we are in-game (other scenes use their own UI).
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
        {
            // Still handle hotbar in the in-game check below — done in _UnhandledInput.
            return;
        }

        // Guard: input translation errors must not crash the frame.
        try
        {
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

                    if (mouseBtn.Pressed)
                    {
                        // Press: emit type 4 (MouseButtonDown) and latch position for click synthesis.
                        // spec: Docs/RE/specs/input_ui.md §2a — type 4 = press.
                        _pressX = x;
                        _pressY = y;
                        _pressButton = button;
                        var downEvt = new AppInputEvent(InputType.MouseButtonDown, x, y, button, mods);
                        _inputBus.Enqueue(in downEvt);
                    }
                    else
                    {
                        // Release: emit type 5 (MouseButtonUp).
                        // spec: Docs/RE/specs/input_ui.md §2a — type 5 = release.
                        var upEvt = new AppInputEvent(InputType.MouseButtonUp, x, y, button, mods);
                        _inputBus.Enqueue(in upEvt);

                        // Click synthesis: if release is within 2 px of the press, emit type 6 (Click).
                        // spec: Docs/RE/specs/input_ui.md §2b — "synthesise click on same-widget release".
                        // spec: input_ui.md §2b — drag tolerance = 2, byte-confirmed.
                        if (_pressButton == button &&
                            Math.Abs(x - _pressX) <= ClickDragTolerance &&
                            Math.Abs(y - _pressY) <= ClickDragTolerance)
                        {
                            var clickEvt = new AppInputEvent(InputType.MouseButtonClick, x, y, button, mods);
                            _inputBus.Enqueue(in clickEvt);
                        }

                        // Clear the latch.
                        _pressButton = -1;
                    }

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
        } // end try
        catch (Exception ex)
        {
            GD.PrintErr($"[InputRouter] _Input error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles unhandled input — specifically the hotbar action-press path.
    /// Chat input is owned exclusively by the ChatWindow LineEdit (IME/CP949-capable).
    /// F2 fix: the ASCII-only InputRouter chat draft path has been removed to eliminate the
    /// dual-owner contention; ChatWindow.SendChatRequested → UseCases.SendChatAsync is the
    /// single chat send path. spec: Docs/RE/specs/chat.md §2.2 / §4.1.
    /// spec: Docs/RE/specs/input_ui.md §3 — UI consumes first; world second.
    /// spec: Docs/RE/specs/input_ui.md §4 — hotbar 1–9.
    /// </summary>
    public override void _UnhandledInput(global::Godot.InputEvent evt)
    {
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
        {
            return;
        }

        // Guard: unhandled-input processing errors must not crash the frame.
        try
        {
            UnhandledInputInternal(evt);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InputRouter] _UnhandledInput error: {ex.Message}");
        }
    }

    private void UnhandledInputInternal(global::Godot.InputEvent evt)
    {
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
    /// Builds a modifier bitmask from a Godot input event using the recovered bit positions:
    /// Alt=bit3 (0x8), and the two slot modifiers 0x4 (slot 1012) / 0x2 (slot 1013).
    /// Bit0 (0x1) is the keyboard auto-repeat flag and is NOT a Shift/Ctrl/Alt modifier — never set here.
    /// The bit positions are confirmed; only the Ctrl/Shift label of slots 1012/1013 is pending.
    /// spec: Docs/RE/specs/input_ui.md §2c (recovered modifier-flag bit positions).
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