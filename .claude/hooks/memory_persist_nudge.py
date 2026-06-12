#!/usr/bin/env python
"""Stop / PreCompact hook — one-line nudge to persist freshly-discovered knowledge before the
context that holds it is lost.

Advisory only; never blocks a stop or a compaction. A single script handles both events,
branching on hook_event_name: at Stop it reminds you to write down anything learned this turn;
at PreCompact (where loss is imminent) it phrases the same nudge more urgently. The destinations
are fixed by the clean-room firewall — raw RE findings go to Docs/RE/_dirty/, promoted neutral
specs to Docs/RE/packets|formats|structs|specs|opcodes.md, and durable project facts to the
auto-memory.
"""
import _hooklib as h


def main():
    ev = h.read_event()

    # Defensive anti-loop guard for Stop (we never block, but mirror the house pattern).
    if ev.get("stop_hook_active"):
        h.ok()
        return

    event = ev.get("hook_event_name", "")
    if event == "PreCompact":
        h.system_message(
            "ℹ context is about to compact — persist NOW any new opcodes / struct layouts / "
            "asset mappings: raw → Docs/RE/_dirty/, promoted → specs (packets|formats|structs|"
            "specs|opcodes.md), durable facts → auto-memory. Advisory only."
        )
    else:  # Stop / SubagentStop
        h.system_message(
            "ℹ before this fades: if you discovered any opcode / struct layout / asset mapping, "
            "save it (raw → Docs/RE/_dirty/, promoted → specs, durable facts → auto-memory). "
            "Advisory only."
        )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
