#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — after .claude/settings.json is written/edited,
check the hook wiring for internal consistency and nudge.

Advisory only: it NEVER blocks the edit and NEVER fails the event. A settings edit is rare
and high-leverage, so we always speak — but briefly. We flag the one real problem (a hook
WIRED in settings whose .py file is MISSING on disk), list any PRESENT-BUT-UNWIRED hooks for
information, and always remind to run the tooling-auditor (which confirms every hook parses
and stays advisory-only / fail-open). A transiently-invalid mid-edit settings.json must never
crash anything: any read/parse error -> silent h.ok() (fail-open).
"""
import json
import os
import re

import _hooklib as h

# Pull the referenced .py basename out of a hook command string, e.g.
#   python "${CLAUDE_PROJECT_DIR}/.claude/hooks/session_primer.py"
# -> "session_primer.py". Tolerant of quoting / path separators / extra args.
_HOOK_PY_REF = re.compile(r"[\\/]\.claude[\\/]hooks[\\/]([A-Za-z0-9_.-]+\.py)", re.I)


def _wired_hook_files(settings):
    """Set of hook .py basenames referenced anywhere in settings['hooks']."""
    refs = set()
    hooks = settings.get("hooks")
    if not isinstance(hooks, dict):
        return refs
    for matcher_objs in hooks.values():
        if not isinstance(matcher_objs, list):
            continue
        for mo in matcher_objs:
            if not isinstance(mo, dict):
                continue
            for entry in mo.get("hooks", []) or []:
                if not isinstance(entry, dict):
                    continue
                cmd = entry.get("command")
                if not isinstance(cmd, str):
                    continue
                m = _HOOK_PY_REF.search(cmd)
                if m:
                    refs.add(m.group(1))
    return refs


def _present_hook_files(hooks_dir):
    """Hook .py files on disk worth wiring (excludes _hooklib.py and __-prefixed)."""
    present = set()
    try:
        for f in os.listdir(hooks_dir):
            if not f.endswith(".py"):
                continue
            if f == "_hooklib.py" or f.startswith("__"):
                continue
            present.add(f)
    except Exception:
        pass
    return present


def main():
    ev = h.read_event()
    if h.tool_name(ev) not in ("Write", "Edit", "MultiEdit"):
        h.ok()
        return

    path = h.file_path(ev)
    if not h.is_claude_settings(path):
        h.ok()
        return

    # Read from disk (the edit already landed; PostToolUse runs after). Any failure here
    # — including a mid-edit invalid JSON — is fail-open: say nothing rather than crash.
    try:
        with open(path, "r", encoding="utf-8") as fh:
            settings = json.loads(h.strip_bom(fh.read()))
    except Exception:
        h.ok()
        return
    if not isinstance(settings, dict):
        h.ok()
        return

    pdir = h.project_dir(ev)
    hooks_dir = os.path.join(pdir, ".claude", "hooks")

    wired = _wired_hook_files(settings)
    present = _present_hook_files(hooks_dir)

    missing = sorted(wired - present)      # wired but the .py is absent -> real problem
    unwired = sorted(present - wired)       # present on disk, never referenced -> info only

    lines = []
    if missing:
        lines.append(
            "⚠ settings.json wires {} hook(s) with NO matching file in .claude/hooks/: {}. "
            "A missing target fails that event silently — add the file or fix the path.".format(
                len(missing), ", ".join(missing)
            )
        )
    else:
        lines.append("settings.json touched — hook wiring resolves on disk.")
    if unwired:
        lines.append("Present-but-unwired (info): {}.".format(", ".join(unwired)))
    lines.append(
        "Run @tooling-auditor to confirm every wired hook parses and stays advisory-only / fail-open. "
        "Heads up only — nothing was blocked."
    )

    h.system_message(" ".join(lines))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
