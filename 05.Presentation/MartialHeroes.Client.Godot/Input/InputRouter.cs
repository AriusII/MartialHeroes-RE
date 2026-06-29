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

public sealed partial class InputRouter : Node
{
    private const int
        ClickDragTolerance = 2;

    private const int ModShift = 0x8;
    private const int ModCtrl = 0x4;
    private const int ModAlt = 0x2;

    private Camera3D? _camera;
    private ClientContext _clientContext = null!;
    private InputBus _inputBus = null!;
    private int _pressButton = -1;

    private int _pressX = -1;
    private int _pressY = -1;

    public void Initialise(ClientContext context)
    {
        _clientContext = context;
    }

    public void InitialiseBus(InputBus inputBus)
    {
        _inputBus = inputBus;
    }

    public override void _Ready()
    {
        GD.Print("[InputRouter] _Ready start");

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
        if (_clientContext is not null && _inputBus is not null)
        {
            var worldHandler = CreateWorldInputHandler();
            _clientContext.SetWorldInputHandler(worldHandler);
        }
    }

    private void LookupCamera()
    {
        _camera = GetViewport().GetCamera3D();
    }


    public override void _Input(InputEvent evt)
    {
        if (_inputBus is null) return;

        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame)
            return;

        try
        {
            switch (evt)
            {
                case InputEventMouseMotion motion:
                {
                    var x = (int)motion.Position.X;
                    var y = (int)motion.Position.Y;
                    var mods = BuildModifiers(motion);
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

                    if (mouseBtn.ButtonIndex == MouseButton.WheelUp ||
                        mouseBtn.ButtonIndex == MouseButton.WheelDown)
                    {
                        var delta = mouseBtn.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                        var wheelEvt = new AppInputEvent(InputType.MouseWheel, x, y, delta, mods);
                        _inputBus.Enqueue(in wheelEvt);
                        break;
                    }

                    if (mouseBtn.Pressed)
                    {
                        _pressX = x;
                        _pressY = y;
                        _pressButton = button;
                        var downEvt = new AppInputEvent(InputType.MouseButtonDown, x, y, button, mods);
                        _inputBus.Enqueue(in downEvt);
                    }
                    else
                    {
                        var upEvt = new AppInputEvent(InputType.MouseButtonUp, x, y, button, mods);
                        _inputBus.Enqueue(in upEvt);

                        if (_pressButton == button &&
                            Math.Abs(x - _pressX) <= ClickDragTolerance &&
                            Math.Abs(y - _pressY) <= ClickDragTolerance)
                        {
                            var clickEvt = new AppInputEvent(InputType.MouseButtonClick, x, y, button, mods);
                            _inputBus.Enqueue(in clickEvt);
                        }

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
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[InputRouter] _Input error: {ex.Message}");
        }
    }

    public override void _UnhandledInput(InputEvent evt)
    {
        if (_clientContext?.SceneMachine.Current.State != EngineSceneState.InGame) return;

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
        for (uint slot = 0; slot < 9; slot++)
        {
            var actionName = $"use_skill_{slot + 1}";
            if (GodotInputMap.HasAction(actionName) && GodotInput.IsActionJustPressed(actionName))
            {
                _ = _clientContext.UseCases.UseSkillAsync(
                    (byte)slot,
                    ReadOnlyMemory<uint>.Empty,
                    ReadOnlyMemory<uint>.Empty);
                GetViewport().SetInputAsHandled();
                return;
            }
        }
    }


    public IInputHandler CreateWorldInputHandler()
    {
        return new WorldInputHandler(this);
    }


    private static int MapMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => AppMouseButton.Left,
            MouseButton.Right => AppMouseButton.Right,
            MouseButton.Middle => AppMouseButton.Middle,
            _ => (int)button
        };
    }

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

        public bool TryHandle(in AppInputEvent e)
        {
            return _router.EnqueueWorldClick(in e);
        }
    }
}