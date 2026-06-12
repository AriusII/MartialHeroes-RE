#!/usr/bin/env python
"""PreToolUse(Bash) hook — warn when a `git add` would stage quarantined or copyrighted
content based on the command text itself.

Advisory only: never denies. This is the cheap, pre-index sibling of git_commit_guard — it
catches the staging step (often `git add -f ...`) before anything reaches the index, by
pattern-matching the command string for a `_dirty` path or a copyrighted-original extension.
Because `Docs/RE/_dirty/` and those extensions are gitignored, staging them at all requires a
force-add, which is exactly the mistake this nudge exists to surface.
"""
import re
import _hooklib as h

# Same forbidden set as git_commit_guard, matched against the command text (with optional
# surrounding quotes) rather than the index.
_FORBIDDEN_EXT_RE = re.compile(
    r"\.(?:pak|vfs|exe|dll|pcapng|scr|mot|ted|bud)\b", re.I
)
_DIRTY_RE = re.compile(r"_dirty\b", re.I)
_GIT_ADD = re.compile(r"\bgit\b[^\n;&|]*\badd\b")


def main():
    ev = h.read_event()
    if h.tool_name(ev) != "Bash":
        h.ok()
        return

    command = h.tool_input(ev).get("command", "") or ""
    if not _GIT_ADD.search(command):
        h.ok()
        return

    hits = []
    if _DIRTY_RE.search(command):
        hits.append("a _dirty/ path (tainted, decompiler-derived)")
    if _FORBIDDEN_EXT_RE.search(command):
        m = _FORBIDDEN_EXT_RE.search(command)
        hits.append("a copyrighted/captured original ({} extension)".format(m.group(0)))

    if hits:
        h.system_message(
            "⚠ clean-room firewall: this `git add` references {}. Those are gitignored by "
            "policy and must never be staged — drop the path (and any `-f`/`--force`). If you "
            "truly need it tracked, reconsider: the firewall depends on these staying out of "
            "git. Advisory only — nothing was blocked.".format(" and ".join(hits))
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
