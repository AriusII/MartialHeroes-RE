#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — advise on Python tooling quality (O5).

Advisory only: it NEVER blocks the write. On a Python file written/edited under the project's
Tools/ tree or under .claude/hooks/, it re-reads the FULL file and runs `ast.parse`; if the
file has a syntax error it will not run, so the hook surfaces the error (with line + message)
as a single systemMessage. Everything else stays silent.

This is the O5 tooling-engineer's safety net for the project's Python harnesses/scripts and the
advisory hooks themselves. The advisory-only + fail-open CONTRACT for hooks is enforced by
kit_guard (which reads the same files); this hook deliberately does NOT duplicate that check —
it owns the syntax-validity check kit_guard does not perform. Always exits 0; fail-open.
"""
import ast
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h


def _is_tooling_py(path):
    """A Python file the project owns as runnable tooling: under Tools/ or .claude/hooks/."""
    p = (path or "").replace("\\", "/").lower()
    if not p.endswith(".py"):
        return False
    return "/tools/" in p or p.startswith("tools/") or "/.claude/hooks/" in p or p.startswith(".claude/hooks/")


def main():
    ev = h.read_event()
    if h.tool_name(ev) not in ("Write", "Edit", "MultiEdit"):
        h.ok()
        return

    path = h.file_path(ev)
    if not _is_tooling_py(path):
        h.ok()
        return

    # Re-read the FULL file (an Edit only carries its slice). Any read error -> stay silent.
    try:
        with open(path, "r", encoding="utf-8") as fh:
            src = h.strip_bom(fh.read())
    except Exception:
        h.ok()
        return

    try:
        ast.parse(src)
    except SyntaxError as se:
        h.system_message(
            "⚠ Python: {} has a syntax error (line {}): {}. The script will not run — fix it "
            "before relying on this tool/hook. {}Advisory only — the write was not blocked.".format(
                os.path.basename(path),
                getattr(se, "lineno", "?"),
                se.msg,
                "Hooks must also stay std-lib-only + advisory-only (exit 0). "
                if h.is_hook_py(path) else "",
            )
        )
        return

    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
