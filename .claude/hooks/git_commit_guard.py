#!/usr/bin/env python
"""PreToolUse(Bash) hook — warn before a `git commit` that would capture quarantined or
copyrighted content, and remind to verify the staged set first.

Advisory only: it NEVER denies the commit (this project keeps every hook non-blocking). When
the Bash command contains `git commit`, it inspects the git index and flags any staged path
that lives under Docs/RE/_dirty/ (tainted, decompiler-derived) or that matches a copyrighted
original extension (.pak/.vfs/.exe/.dll/.pcapng/.scr/.mot/.ted/.bud). Those files are all
gitignored by policy, so a staged hit usually means a `git add -f` slipped through. It also
reminds the author to run /clean-room-firewall-check and eyeball `git diff --cached`.
"""
import os
import re
import _hooklib as h

# Copyrighted-original / captured artifacts that must never be committed (all gitignored).
_FORBIDDEN_EXT = (
    ".pak", ".vfs", ".exe", ".dll", ".pcapng", ".scr", ".mot", ".ted", ".bud",
)

_GIT_COMMIT = re.compile(r"\bgit\b[^\n;&|]*\bcommit\b")


def main():
    ev = h.read_event()
    if h.tool_name(ev) != "Bash":
        h.ok()
        return

    command = h.tool_input(ev).get("command", "") or ""
    if not _GIT_COMMIT.search(command):
        h.ok()
        return

    pdir = h.project_dir(ev)
    staged = h.staged_files(pdir)
    if not staged:
        # Nothing staged (or git unavailable) — still nudge to verify, but don't alarm.
        h.system_message(
            "ℹ before committing: run /clean-room-firewall-check and confirm `git diff --cached` "
            "holds only neutral specs + fresh C# (no _dirty/, no originals). Advisory only."
        )
        return

    dirty = []
    originals = []
    for p in staged:
        low = p.replace("\\", "/").lower()
        if "/docs/re/_dirty/" in low or low.startswith("docs/re/_dirty/"):
            dirty.append(p)
        elif low.endswith(_FORBIDDEN_EXT):
            originals.append(p)

    problems = []
    if dirty:
        problems.append("tainted _dirty/ files staged ({})".format(", ".join(os.path.basename(p) for p in dirty[:6])))
    if originals:
        problems.append("copyrighted/captured originals staged ({})".format(", ".join(os.path.basename(p) for p in originals[:6])))

    if problems:
        h.system_message(
            "⚠ clean-room firewall: {}. These are gitignored by policy — unstage them "
            "(`git restore --staged <path>`) before committing. Then run "
            "/clean-room-firewall-check. Advisory only — the commit was NOT blocked.".format(
                "; ".join(problems)
            )
        )
        return

    h.system_message(
        "ℹ committing {} staged file(s). Reminder: run /clean-room-firewall-check and verify "
        "`git diff --cached` is all neutral specs + fresh code. Advisory only.".format(len(staged))
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
