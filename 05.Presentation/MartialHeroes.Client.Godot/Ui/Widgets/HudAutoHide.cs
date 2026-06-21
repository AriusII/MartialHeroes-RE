// Ui/Widgets/HudAutoHide.cs
//
// Shared auto-hide timer for HUD widgets — faithful to the legacy GUComponent
// auto-hide block at field offsets +0x95..+0xA0.
//
// MECHANISM (CODE-CONFIRMED, re-walked 2026-06-21 / IDB 263bd994):
//   +0x95 (byte) — auto-hide ENABLE flag (opt-in; OFF by default).
//   +0x98 (u32)  — arm-START timestamp (ms); 0 = not armed.
//                  Set on Show() only when: widget being shown AND enable AND timeout > 0.
//                  The timer is NOT re-armed while already running.
//   +0x9C (u32)  — timeout duration (ms); constructor default 3000 ms; per-instance override.
//   +0xA0 (ptr)  — on-timeout callback; null when unused.
//                  Fire order: callback → HideInstant (the alpha-chase begins) → disarm.
//
// PORT CONTRACT:
//   1. Attach one HudAutoHide node as a child of the Control to be auto-hidden.
//      Use HudAutoHide.For(ctrl) to get-or-add; or new it directly.
//   2. Call Enable() / Enable(timeoutMs) to arm the timer; the timer arms itself
//      each time Show() is called on the parent Control while enabled.
//   3. Call Arm() manually to start the countdown from "now" (replicates the
//      vtable-slot-1 show-method arming described in the spec).
//   4. _Process ticks the timer; on expiry: fires OnTimeout (if set), hides
//      the parent Control, and disarms.
//
// spec: Docs/RE/specs/ui_system.md — auto-hide block +0x95..+0xA0, CODE-CONFIRMED.
// spec: Docs/RE/specs/ui_system.md — "+0x98 = arm-START timestamp (NOT expiry)".
// spec: Docs/RE/specs/ui_system.md — "+0x9C default 3000 ms; per-instance overridable".
// spec: Docs/RE/specs/ui_system.md — "+0xA0 callback fires first, then hide, then disarm".

using Godot;

namespace MartialHeroes.Client.Godot.Ui.Widgets;

/// <summary>
///     Auto-hide timer for HUD <see cref="Control" /> nodes, faithful to the legacy GUComponent
///     +0x95..+0xA0 block (CODE-CONFIRMED, re-walked 2026-06-21).
///     <para>
///         Attach as a child of the target <see cref="Control" />; use
///         <see cref="For" /> to get-or-add. Call <see cref="EnableAutoHide" /> to opt in;
///         the timer arms itself each time the parent becomes visible (replicating vtable-slot-1).
///         On timeout: <see cref="OnTimeout" /> fires first, then the parent hides, then
///         the timer disarms (+0x98 → 0).
///     </para>
///     spec: Docs/RE/specs/ui_system.md — auto-hide block +0x95..+0xA0: CODE-CONFIRMED.
/// </summary>
internal sealed partial class HudAutoHide : Node
{
    // Default timeout (ms) — constructor default 3000 ms.
    // spec: Docs/RE/specs/ui_system.md +0x9C — "constructor default 3000".
    public const uint DefaultTimeoutMs = 3000; // spec: Docs/RE/specs/ui_system.md +0x9C

    // Node name used by the factory so we can find-or-add safely.
    private static readonly StringName AutoHideNodeName = new("__HudAutoHide");

    // +0x98 — arm-start timestamp (ms since engine start; 0 = not armed).
    // spec: Docs/RE/specs/ui_system.md — "+0x98 = arm-START timestamp (NOT expiry); 0 = not armed".
    private ulong _armStartMs; // spec: +0x98

    // +0x95 — auto-hide enable flag (OFF by default).
    // spec: Docs/RE/specs/ui_system.md — "+0x95 = auto-hide enable; zero-initialised by both base ctors".
    private bool _enabled; // spec: +0x95

    // +0x9C — timeout duration (ms).
    // spec: Docs/RE/specs/ui_system.md — "+0x9C = timeout duration; constructor default 3000 ms".
    private uint _timeoutMs = DefaultTimeoutMs; // spec: +0x9C

    // -------------------------------------------------------------------------
    // Optional callback (mirrors +0xA0 on-timeout callback)
    // spec: Docs/RE/specs/ui_system.md — "+0xA0 = on-timeout callback; null when unused".
    // Fire order: callback first, then hide, then disarm.
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Optional callback fired when the timer expires, BEFORE the parent is hidden.
    ///     Corresponds to the +0xA0 function pointer.
    ///     spec: Docs/RE/specs/ui_system.md — "+0xA0 callback fires → hides → disarms (+0x98=0)".
    /// </summary>
    public Action? OnTimeout { get; set; } // spec: +0xA0

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns the <see cref="HudAutoHide" /> node attached to <paramref name="control" />,
    ///     creating and adding it on the first call.
    /// </summary>
    internal static HudAutoHide For(Control control)
    {
        var existing = control.FindChild(AutoHideNodeName, owned: false);
        if (existing is HudAutoHide h) return h;

        var newNode = new HudAutoHide { Name = AutoHideNodeName };
        control.AddChild(newNode);
        return newNode;
    }

    // -------------------------------------------------------------------------
    // Enable / configure API
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Enables auto-hide with the spec default 3000 ms timeout.
    ///     Corresponds to writing 1 to field +0x95 on a transient/auto-dismissing panel.
    ///     spec: Docs/RE/specs/ui_system.md — "+0x95 = auto-hide enable; transient panels write 1".
    ///     spec: Docs/RE/specs/ui_system.md — "+0x9C constructor default 3000".
    /// </summary>
    internal void EnableAutoHide()
    {
        EnableAutoHide(DefaultTimeoutMs);
    }

    /// <summary>
    ///     Enables auto-hide with a per-instance timeout override.
    ///     spec: Docs/RE/specs/ui_system.md — "+0x9C per-instance overridable".
    /// </summary>
    internal void EnableAutoHide(uint timeoutMs)
    {
        _enabled = true; // spec: +0x95 = 1
        _timeoutMs = timeoutMs; // spec: +0x9C
    }

    /// <summary>
    ///     Arms the auto-hide timer from "now" (replicates the show-method / vtable-slot-1 arm path).
    ///     Guards: must be enabled (+0x95) AND timeout > 0 (+0x9C) AND not already armed (+0x98 == 0).
    ///     spec: Docs/RE/specs/ui_system.md — "Arm: show method records timestamp into +0x98 only when
    ///     widget being shown AND auto-hide enabled AND non-zero timeout; timer NOT re-armed while running."
    /// </summary>
    internal void Arm()
    {
        if (!_enabled) return; // spec: +0x95 gate
        if (_timeoutMs == 0) return; // spec: +0x9C > 0 required
        if (_armStartMs != 0) return; // spec: timer not re-armed while already running

        // Record arm-start timestamp (floored to 1 so 0 = not-armed is always distinct).
        // spec: Docs/RE/specs/ui_system.md — "+0x98: floored to 1; 0 = not armed".
        _armStartMs = Time.GetTicksMsec(); // spec: +0x98
        if (_armStartMs == 0) _armStartMs = 1; // floor to 1 — spec: "+0x98 floored to 1"
    }

    /// <summary>Disarms the timer without hiding the parent (exposed for manual cancel).</summary>
    internal void Disarm()
    {
        _armStartMs = 0; // spec: +0x98 → 0
    }

    // -------------------------------------------------------------------------
    // Godot lifecycle — per-frame tick
    // -------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        // Early-out: not enabled or not armed.
        if (!_enabled || _armStartMs == 0) return;

        // Unsigned millisecond elapsed comparison.
        // spec: Docs/RE/specs/ui_system.md — "tick checks (now - start) >= timeout as unsigned ms".
        var nowMs = Time.GetTicksMsec();
        var elapsedMs = nowMs - _armStartMs; // unsigned wraparound-safe (both u64)

        if (elapsedMs < _timeoutMs) return; // not yet expired

        // Fire: callback → hide → disarm.
        // spec: Docs/RE/specs/ui_system.md — "on fire: call +0xA0 callback, then hide, then +0x98 = 0".
        OnTimeout?.Invoke(); // spec: +0xA0 fires first

        // Hide the parent Control (begins the ±64/tick alpha fade-out via AlphaFade).
        if (GetParent() is Control parent)
        {
            AlphaFade.For(parent).Hide(); // fade-out; spec: show/hide = alpha chase, §7.1
            GD.Print($"[HudAutoHide] Auto-hide fired after {elapsedMs} ms (timeout={_timeoutMs} ms). " +
                     "spec: Docs/RE/specs/ui_system.md +0x9C / +0x98 / +0xA0.");
        }

        _armStartMs = 0; // disarm — spec: "+0x98 reset to 0"
    }
}