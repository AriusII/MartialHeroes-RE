using Godot;

namespace MartialHeroes.Explorer.Viewer;

public sealed partial class OrbitCamera : Camera3D
{
    private const float OrbitSensitivity = 0.005f;
    private const float PanSensitivity = 0.002f;
    private const float ZoomGrowth = 1.12f;
    private const float MinDistance = 0.05f;
    private const float MaxDistance = 50000f;
    private float _distance = 10f;
    private Vector2 _lastMouse = Vector2.Zero;

    private bool _orbiting;
    private bool _panning;
    private float _pitch = 0.5f;

    private Vector3 _pivot = Vector3.Zero;
    private float _yaw = 0.4f;

    public override void _Ready()
    {
        ApplyTransform();
    }

    public override void _Process(double delta)
    {
        if (GetViewport().GuiGetFocusOwner() is LineEdit) return;

        var moveX = 0f;
        var moveZ = 0f;
        var moveY = 0f;
        if (Input.IsKeyPressed(Key.W)) moveZ -= 1f;
        if (Input.IsKeyPressed(Key.S)) moveZ += 1f;
        if (Input.IsKeyPressed(Key.A)) moveX -= 1f;
        if (Input.IsKeyPressed(Key.D)) moveX += 1f;
        if (Input.IsKeyPressed(Key.E)) moveY += 1f;
        if (Input.IsKeyPressed(Key.Q)) moveY -= 1f;
        if (moveX == 0f && moveZ == 0f && moveY == 0f) return;

        var sinYaw = Mathf.Sin(_yaw);
        var cosYaw = Mathf.Cos(_yaw);
        var forward = new Vector3(-sinYaw, 0f, -cosYaw);
        var right = new Vector3(cosYaw, 0f, -sinYaw);
        var fast = Input.IsKeyPressed(Key.Shift) ? 3.5f : 1f;
        var speed = Mathf.Max(_distance, 1f) * 0.9f * fast * (float)delta;

        _pivot += (right * moveX + forward * moveZ) * speed;
        _pivot.Y += moveY * speed;
        ApplyTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            _orbiting = mb.Pressed;
            _lastMouse = mb.Position;
        }
        else if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle)
        {
            _panning = mb.Pressed;
            _lastMouse = mb.Position;
        }
        else if (mb.ButtonIndex == MouseButton.WheelUp)
        {
            _distance /= ZoomGrowth;
            _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
            ApplyTransform();
        }
        else if (mb.ButtonIndex == MouseButton.WheelDown)
        {
            _distance *= ZoomGrowth;
            _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
            ApplyTransform();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && !mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left) _orbiting = false;
            else if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle) _panning = false;
            return;
        }

        if (@event is not InputEventMouseMotion mm || (!_orbiting && !_panning)) return;

        var delta = mm.Position - _lastMouse;
        _lastMouse = mm.Position;

        if (_orbiting)
        {
            _yaw -= delta.X * OrbitSensitivity;
            _pitch -= delta.Y * OrbitSensitivity;
            _pitch = Mathf.Clamp(_pitch, -1.5f, 1.5f);
            ApplyTransform();
        }
        else if (_panning)
        {
            var right = GlobalTransform.Basis.X;
            var up = GlobalTransform.Basis.Y;
            _pivot -= right * delta.X * PanSensitivity * _distance;
            _pivot += up * delta.Y * PanSensitivity * _distance;
            ApplyTransform();
        }
    }

    public void FrameAabb(Aabb aabb)
    {
        if (aabb.Size == Vector3.Zero) return;
        _pivot = aabb.GetCenter();
        var radius = Mathf.Max(aabb.Size.Length() * 0.5f, 0.001f);
        var fovRad = Mathf.DegToRad(Fov);
        _distance = radius / Mathf.Sin(fovRad * 0.5f) * 1.15f;
        _distance = Mathf.Clamp(_distance, MinDistance, MaxDistance);
        _pitch = 0.5f;
        _yaw = 0.5f;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        var sinYaw = Mathf.Sin(_yaw);
        var cosYaw = Mathf.Cos(_yaw);
        var sinPitch = Mathf.Sin(_pitch);
        var cosPitch = Mathf.Cos(_pitch);

        GlobalPosition = _pivot + new Vector3(
            sinYaw * cosPitch,
            sinPitch,
            cosYaw * cosPitch) * _distance;

        if (_pivot.DistanceTo(GlobalPosition) > 0.0001f)
            LookAt(_pivot, Vector3.Up);
    }
}