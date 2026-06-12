#!/usr/bin/env python
"""PreToolUse(mcp__ida__*) hook — log a lightweight provenance breadcrumb before each IDA MCP
call and remind that whatever it returns is tainted (dirty-room) output.

Advisory only; never blocks the IDA call. This is the PRE-call companion to re_provenance_logger
(which hashes the POST-call response): it records that an IDA tool was invoked and which target
it was pointed at, then surfaces the clean-room rule so the result is routed correctly. To stay
fast and avoid noise it does NOT timestamp every line or repeat the reminder more than once per
short burst — it keeps a monotonically increasing counter and only speaks on the first call of a
session (and roughly every 25th thereafter).
"""
import _hooklib as h

_STATE_FILE = "ida_usage.jsonl"
_COUNTER_FILE = "ida_call_counter.txt"


def _bump_counter(pdir):
    """Return the new call count (1-based). Counter-only, never wall-clock — keeps the audit
    line stable/diffable and avoids embedding timestamps in the breadcrumb."""
    import os
    path = os.path.join(h.state_dir(pdir), _COUNTER_FILE)
    n = 0
    try:
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8") as fh:
                n = int((fh.read() or "0").strip() or "0")
    except Exception:
        n = 0
    n += 1
    try:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(str(n))
    except Exception:
        pass
    return n


def _target_hint(ti):
    if not isinstance(ti, dict):
        return None
    for k in ("name", "function_name", "function", "address", "ea", "offset", "symbol", "query"):
        if k in ti and ti[k] not in (None, ""):
            return {k: ti[k]}
    return None


def main():
    ev = h.read_event()
    name = h.tool_name(ev)
    if not name.startswith("mcp__ida__"):
        h.ok()
        return

    pdir = h.project_dir(ev)
    count = _bump_counter(pdir)
    h.append_jsonl(pdir, _STATE_FILE, {
        "seq": count,
        "session": ev.get("session_id", ""),
        "tool": name,
        "target": _target_hint(h.tool_input(ev)),
    })

    # Speak on the first call of the session, then only occasionally, to stay low-noise.
    if count == 1 or count % 25 == 0:
        h.system_message(
            "ℹ IDA call #{} logged. Its output is dirty-room: write findings ONLY to "
            "Docs/RE/_dirty/, then have a spec-author rewrite (never copy) them into the neutral "
            "committed specs. {} Advisory only.".format(count, h.CLEAN_ROOM_BLURB)
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
