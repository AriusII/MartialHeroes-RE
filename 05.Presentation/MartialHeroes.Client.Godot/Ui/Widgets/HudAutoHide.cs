
using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

internal sealed partial class HudAutoHide : Node
{
    public const uint DefaultTimeoutMs = 3000;

    private static readonly StringName AutoHideNodeName = new("__HudAutoHide");

    private ulong _armStartMs;

    private bool _enabled;

    private uint _timeoutMs = DefaultTimeoutMs;


    public Action? OnTimeout { get; set; }


    internal static HudAutoHide For(Control control)
    {
        var existing = control.FindChild(AutoHideNodeName, owned: false);
        if (existing is HudAutoHide h) return h;

        var newNode = new HudAutoHide { Name = AutoHideNodeName };
        control.AddChild(newNode);
        return newNode;
    }


    internal void EnableAutoHide()
    {
        EnableAutoHide(DefaultTimeoutMs);
    }

    internal void EnableAutoHide(uint timeoutMs)
    {
        _enabled = true;
        _timeoutMs = timeoutMs;
    }

    internal void Arm()
    {
        if (!_enabled) return;
        if (_timeoutMs == 0) return;
        if (_armStartMs != 0) return;

        _armStartMs = Time.GetTicksMsec();
        if (_armStartMs == 0) _armStartMs = 1;
    }

    internal void Disarm()
    {
        _armStartMs = 0;
    }


    public override void _Process(double delta)
    {
        if (!_enabled || _armStartMs == 0) return;

        var nowMs = Time.GetTicksMsec();
        var elapsedMs = nowMs - _armStartMs;

        if (elapsedMs < _timeoutMs) return;

        OnTimeout?.Invoke();

        if (GetParent() is Control parent)
        {
            AlphaFade.For(parent).Hide();
            GD.Print($"[HudAutoHide] Auto-hide fired after {elapsedMs} ms (timeout={_timeoutMs} ms). " +
                     "spec: Docs/RE/specs/ui_system.md +0x9C / +0x98 / +0xA0.");
        }

        _armStartMs = 0;
    }
}