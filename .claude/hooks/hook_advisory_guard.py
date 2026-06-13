#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — the guard that guards the guards.

On this project EVERY hook must be ADVISORY-ONLY (it warns, it never blocks). This hook
watches for a freshly written/edited hook script under .claude/hooks/ and warns when that
script contains a BLOCKING signature (sys.exit(2) / {"decision": "block"} / a deny|ask
permissionDecision) — which would violate the advisory-only invariant.

Advisory only, fail-open: it NEVER cancels the edit. It re-reads the FULL file from disk
(an Edit only carries the changed slice, so the blocking line could live elsewhere) and
defers the actual detection to h.hook_can_block, which is tuned for low false positives.
The library itself (_hooklib.py) is skipped — it legitimately defines the block regex.
"""
import os
import _hooklib as h

_TOOLS = ("Write", "Edit", "MultiEdit")


def main():
    ev = h.read_event()
    if h.tool_name(ev) not in _TOOLS:
        h.ok()
        return

    path = h.file_path(ev)
    if not h.is_hook_py(path):
        h.ok()
        return

    # _hooklib.py is the canonical home of the block-signature regex; never flag it.
    if os.path.basename(path) == "_hooklib.py":
        h.ok()
        return

    # An Edit/MultiEdit only carries its changed slice; re-read the whole file so a
    # blocking line elsewhere is still seen. Any read error -> stay silent (fail-open).
    try:
        with open(path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except Exception:
        h.ok()
        return

    if h.hook_can_block(text):
        h.system_message(
            "⚠ advisory-only: {} contains a BLOCKING signature "
            "(sys.exit(2) / {{\"decision\": \"block\"}} / a deny|ask permissionDecision). "
            "On this project every hook MUST be advisory-only — it may only warn via "
            "h.system_message / h.additional_context and must always exit 0. Blocking is "
            "the orchestrator's call in settings.json, never the hook's. Heads up only — "
            "the edit was not blocked.".format(os.path.basename(path))
        )
        return

    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
