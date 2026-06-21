using Godot;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Presentation.Helpers;
using MartialHeroes.Shared.Kernel.Enums;
using MartialHeroes.Shared.Kernel.Numerics;
using GodotInputMap = Godot.InputMap;
using GodotInput = Godot.Input;
using AppMouseButton = MartialHeroes.Client.Application.Input.MouseButton;
using AppInputEvent = MartialHeroes.Client.Application.Input.InputEvent;
using InputEvent = Godot.InputEvent;
using MouseButton = Godot.MouseButton;

namespace MartialHeroes.Client.Godot.Input;

/// <summary>
///     Captures raw Godot input events (_Input / _UnhandledInput) and translates them into
///     <see cref="AppInputEvent" /> values pushed into the <see cref="InputBus" />. The bus then
///     walks the UI → world chain of responsibility; this router is deliberately passive.
///     Input routing priority (spec: Docs/RE/specs/input_ui.md §3 "UI is the gate"):
///     1. The HUD <see cref="IInputHandler" /> is registered FIRST in the InputBus (added at
///     composition root in <see cref="ClientContext" />). It consumes UI panel clicks.
///     2. The WorldInputHandler is registered SECOND and handles entity-pick / click-to-move.
///     Godot events translated:
///     - InputEventMouseMotion      → InputType.MouseMove
///     - InputEventMouseButton      → InputType.MouseButtonDown / MouseButtonUp
///     - Mouse wheel                → InputType.MouseWheel
///     - InputEventKey              → InputType.KeyDown / KeyUp
///     - Hotbar actions (press)     → use-case call bypassing the bus (direct intent).
///     Threading contract: all Godot input callbacks run on the Godot main thread.
///     The InputBus.Enqueue is MPSC-safe, so the enqueue here is safe even if the loop drains
///     from a different thread in a future integration. The loop is driven from
///     GameEngineLoop on a background Task, but all node mutations stay on main thread.
///     spec: Docs/RE/specs/input_ui.md §6 ("Godot captures raw input and pushes it into the bus").
///     spec: Docs/RE/specs/input_ui.md §3 (UI before world chain of responsibility).
/// </summary>
public sealed partial class InputRouter : Node
{
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

    // Camera reference for screen→world raycasting (click-to-move).
    private Camera3D? _camera;
    private ClientContext _clientContext = null!;
    private InputBus _inputBus = null!;
    private int _pressButton = -1;

    // Click-synthesis latch state: records the press position and button so that on release within the
    // drag tolerance we can emit a MouseButtonClick (type 6) event.
    // The full same-widget rule (§3 of ui_event_dispatch.md — "one process-global click-capture pointer")
    // is OWNED BY THE HudInputHandler in the Application layer, not here: InputRouter cannot perform a
    // widget hit-test because it operates below the UI container tree. InputRouter approximates the
    // same-widget check via the 2-px drag tolerance, which is sufficient at the Godot→Application boundary.
    // The HudInputHandler is responsible for the precise capture-pointer comparison (+0x88/+0x89 latch).
    // OUT-OF-LANE NOTE: the container reverse-child routing (§4), hover edge latch (+0x88/+0x89), and
    // active-child field (+0xB4) store all live in the Application-layer IInputHandler chain, not here.
    // spec: Docs/RE/specs/ui_event_dispatch.md §3 (same-widget click-capture, global capture pointer).
    // spec: Docs/RE/specs/input_ui.md §2b — latch widget on press, synthesise click on same-widget release.
    // The 2-px drag tolerance is byte-confirmed in input_ui.md §2b.
    private int _pressX = -1;
    private int _pressY = -1;

    /// <summary>
    ///     Called by GameLoop._Ready before any input is processed.
    ///     The InputBus is supplied separately via <see cref="InitialiseBus" /> when the composition
    ///     root wires it (see GameLoop._Ready: Initialise(context) then InitialiseBus(context.InputBus)).
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
            var worldHandler = CreateWorldInputHandler();
            // Reach the ClientContext to set the relay target.
            _clientContext.SetWorldInputHandler(worldHandler);
        }
    }

    private void LookupCamera()
    {
        _camera = GetViewport().GetCamera3D();
    }

    // -------------------------------------------------------------------------
    // Godot input callbacks — translate and push to InputBus
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Handles all input events; translates them to Application-layer <see cref="AppInputEvent" />
    ///     and enqueues them on the <see cref="InputBus" /> for deterministic dispatch at the next
    ///     fixed tick. spec: Docs/RE/specs/input_ui.md §6.
    /// </summary>
    public override void _Input(InputEvent evt)
    {
        if (_inputBus is null) return;

        // Only route inputs when we are in-game (other scenes use their own UI).
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
            // Still handle hotbar in the in-game check below — done in _UnhandledInput.
            return;

        // Guard: input translation errors must not crash the frame.
        try
        {
            switch (evt)
            {
                case InputEventMouseMotion motion:
                {
                    var x = (int)motion.Position.X;
                    var y = (int)motion.Position.Y;
                    var mods = BuildModifiers(motion);
                    // spec: Docs/RE/specs/input_ui.md §2 — type 3 = move.
                    var e = new AppInputEvent(InputType.MouseMove, x, y, 0, mods);
                    _inputBus.Enqueue(in e);
                    break;
                }

                case InputEventMouseButton mouseBtn:
                {
                    var x = (int)mouseBtn.Position.X;
                    var y = (int)mouseBtn.Position.Y;
                    var button = MapMouseButton(mouseBtn.ButtonIndex);
                    var mods = BuildModifiers(mouseBtn);

                    // Wheel events are encoded as button press in Godot.
                    if (mouseBtn.ButtonIndex == MouseButton.WheelUp ||
                        mouseBtn.ButtonIndex == MouseButton.WheelDown)
                    {
                        var delta = mouseBtn.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
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

                        // Clear the latch fully (button, position).
                        // spec: Docs/RE/specs/ui_event_dispatch.md §3 — "clears the capture" on release.
                        _pressButton = -1;
                        _pressX = -1;
                        _pressY = -1;
                    }

                    break;
                }

                case InputEventKey key:
                {
                    var type = key.Pressed ? InputType.KeyDown : InputType.KeyUp;
                    var vk = (int)key.Keycode;
                    var mods = BuildModifiers(key);
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
    ///     Handles unhandled input — specifically the hotbar action-press path.
    ///     Chat input is owned exclusively by the ChatWindow LineEdit (IME/CP949-capable).
    ///     F2 fix: the ASCII-only InputRouter chat draft path has been removed to eliminate the
    ///     dual-owner contention; ChatWindow.SendChatRequested → UseCases.SendChatAsync is the
    ///     single chat send path. spec: Docs/RE/specs/chat.md §2.2 / §4.1.
    ///     spec: Docs/RE/specs/input_ui.md §3 — UI consumes first; world second.
    ///     spec: Docs/RE/specs/input_ui.md §4 — hotbar 1–9.
    /// </summary>
    public override void _UnhandledInput(InputEvent evt)
    {
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame) return;

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

    private void UnhandledInputInternal(InputEvent evt)
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
            var actionName = $"use_skill_{slot + 1}";
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
    ///     Creates the world-side <see cref="IInputHandler" /> that handles click-to-move.
    ///     Registered AFTER any UI handlers in the <see cref="InputBus" />.
    ///     spec: Docs/RE/specs/input_ui.md §3 — "world entity pick / ground move — only when UI fails".
    /// </summary>
    public IInputHandler CreateWorldInputHandler()
    {
        return new WorldInputHandler(this);
    }

    // -------------------------------------------------------------------------
    // Click-to-move
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Raycasts from the click position into the scene and forwards the world-space hit position
    ///     to <see cref="MartialHeroes.Client.Application.UseCases.IApplicationUseCases.RequestMoveAsync" />.
    ///     Returns true if the raycast hit geometry (event consumed), false if it missed.
    ///     spec: Vector3Fixed.FromFloat() — presentation boundary only.
    ///     spec: WorldCoordinates helpers — coordinate system bridging.
    /// </summary>
    private bool HandleClickToMove(int screenX, int screenY)
    {
        if (_camera is null || _clientContext is null) return false;

        var world3D = _camera.GetWorld3D();
        var spaceState = world3D.DirectSpaceState;

        var screenPos = new Vector2(screenX, screenY);
        var from = _camera.ProjectRayOrigin(screenPos);
        var to = from + _camera.ProjectRayNormal(screenPos) * 1000f;

        var query = PhysicsRayQueryParameters3D.Create(from, to);
        query.CollideWithAreas = false;

        var result = spaceState.IntersectRay(query);
        if (result.Count == 0) return false;

        var hitPoint = result["position"].AsVector3();

        // Convert Godot world space → legacy world space, then → Q16.16.
        // spec: WorldCoordinates helpers — coordinate system bridging.
        var (lx, ly, lz) = WorldCoordinates.ToLegacy(hitPoint.X, hitPoint.Y, hitPoint.Z);
        var fixedTarget = Vector3Fixed.FromFloat(lx, ly, lz);

        // Forward the intent to the Application layer. Running = false (walk on click).
        _ = _clientContext.UseCases.RequestMoveAsync(fixedTarget, false);
        return true;
    }

    // -------------------------------------------------------------------------
    // Input translation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Maps a Godot <see cref="Godot.MouseButton" /> to the legacy button index.
    ///     spec: Docs/RE/specs/input_ui.md §1b — "1 = left, 2 = right, 3 = middle": CONFIRMED.
    /// </summary>
    private static int MapMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => AppMouseButton.Left, // spec: input_ui.md §1b — 1 = left. CONFIRMED.
            MouseButton.Right => AppMouseButton.Right, // spec: input_ui.md §1b — 2 = right. CONFIRMED.
            MouseButton.Middle => AppMouseButton.Middle, // spec: input_ui.md §1b — 3 = middle. CONFIRMED.
            _ => (int)button
        };
    }

    /// <summary>
    ///     Builds a modifier bitmask from a Godot input event using the recovered bit positions:
    ///     Alt=bit3 (0x8), and the two slot modifiers 0x4 (slot 1012) / 0x2 (slot 1013).
    ///     Bit0 (0x1) is the keyboard auto-repeat flag and is NOT a Shift/Ctrl/Alt modifier — never set here.
    ///     The bit positions are confirmed; only the Ctrl/Shift label of slots 1012/1013 is pending.
    ///     spec: Docs/RE/specs/input_ui.md §2c (recovered modifier-flag bit positions).
    /// </summary>
    private static int BuildModifiers(InputEvent evt)
    {
        var mods = 0;
        if (evt is InputEventWithModifiers m)
        {
            if (m.ShiftPressed) mods |= ModShift;
            if (m.CtrlPressed) mods |= ModCtrl;
            if (m.AltPressed) mods |= ModAlt;
        }

        return mods;
    }

    private sealed class WorldInputHandler : IInputHandler
    {
        private readonly InputRouter _router;

        public WorldInputHandler(InputRouter router)
        {
            _router = router;
        }

        /// <summary>
        ///     Handles a synthesised left-button CLICK (type 6) as click-to-move. Returns true to consume.
        ///     The world handler must react to the synthetic CLICK (type 6), not the raw press (type 4):
        ///     the click is the drag-vs-click discriminator — only a press+release on the same widget
        ///     produces it, so a drag that overshoots never triggers a spurious move.
        ///     spec: Docs/RE/specs/ui_event_dispatch.md §3 — "the synthetic CLICK event (type 6) is the
        ///     genuine click-vs-drag discriminator; a drag-off release never reaches the window switch".
        ///     spec: Docs/RE/specs/input_ui.md §3 — "(c) Otherwise, ground action — click-to-move".
        /// </summary>
        public bool TryHandle(in AppInputEvent e)
        {
            // React to the synthesised CLICK (type 6), not the raw press (type 4).
            // spec: Docs/RE/specs/ui_event_dispatch.md §3 (click-synth is the action discriminator).
            if (!e.IsLeftButtonClick) return false;

            return _router.HandleClickToMove(e.X, e.Y);
        }
    }
}