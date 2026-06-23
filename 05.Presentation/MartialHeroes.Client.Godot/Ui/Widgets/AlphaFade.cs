
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

internal sealed partial class AlphaFade : Node
{
    private const float StepFast = 64f / 255f;

    private const float StepSlow = 32f / 255f;


    private static readonly StringName DriverNodeName = new("__HudAlphaFade");
    private float _current = 1f;
    private float _step = StepFast;

    private float _target = 1f;


    internal void Show()
    {
        SyncCurrentFromParent();
        _target = 1f;
        if (GetParent() is Control ctrl) ctrl.Visible = true;
    }

    internal void Hide()
    {
        SyncCurrentFromParent();
        _target = 0f;
    }

    internal void ShowInstant()
    {
        _target = 1f;
        _current = 1f;
        ApplyAlpha(1f);
        if (GetParent() is Control ctrl) ctrl.Visible = true;
    }

    internal void HideInstant()
    {
        _target = 0f;
        _current = 0f;
        ApplyAlpha(0f);
        if (GetParent() is Control ctrl) ctrl.Visible = false;
    }

    internal void UseSlowFade()
    {
        _step = StepSlow;
    }


    public override void _Process(double delta)
    {
        if (Mathf.IsEqualApprox(_current, _target)) return;

        if (_current < _target)
            _current = Mathf.Min(_current + _step, _target);
        else
            _current = Mathf.Max(_current - _step, _target);

        ApplyAlpha(_current);

        if (_target < 0.01f && _current < 0.01f && GetParent() is Control ctrl)
            ctrl.Visible = false;
    }

    internal static AlphaFade For(Control control)
    {
        var existing = control.FindChild(DriverNodeName, owned: false);
        if (existing is AlphaFade fade) return fade;

        var newFade = new AlphaFade { Name = DriverNodeName };
        control.AddChild(newFade);
        newFade._current = control.Modulate.A;
        return newFade;
    }


    private void SyncCurrentFromParent()
    {
        if (GetParent() is CanvasItem item)
            _current = item.Modulate.A;
    }

    private void ApplyAlpha(float alpha)
    {
        if (GetParent() is not CanvasItem item) return;
        var c = item.Modulate;
        c.A = alpha;
        item.Modulate = c;
    }
}