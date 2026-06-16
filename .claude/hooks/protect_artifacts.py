#!/usr/bin/env python
"""PreToolUse(Bash|Write|Edit|MultiEdit) hook — warn before copyrighted/legacy artifacts
or captures get staged or written into the tree.

Advisory only: it never cancels the command. The real risk it leads with is that *.exe/*.dll
were historically NOT in .gitignore (now they are), and blanket `git add` can sweep them in.
"""
import re
import _hooklib as h

_FORBIDDEN = re.compile(
    r"(\.pak\b|\.pcapng\b|\.tsv\b|\.exe\b|\.dll\b|\bmain\.exe\b|(?:^|[\\/])\.godot[\\/])",
    re.I,
)
_GIT_MUTATOR = re.compile(r"\bgit\s+(add|commit|stash)\b", re.I)
_GIT_SAFE = re.compile(r"\bgit\s+(rm|restore|reset)\b", re.I)
_BLANKET = re.compile(r"\bgit\s+add\s+(-A\b|--all\b|\.\s|\.$|:/)", re.I)
_COMMIT_ALL = re.compile(r"\bgit\s+commit\s+.*(-a\b|--all\b)", re.I)


def _check_bash(ev):
    cmd = h.tool_input(ev).get("command", "") or ""
    if not cmd:
        h.ok()
        return
    segments = re.split(r"&&|\|\||;|\|", cmd)
    warnings = []
    for seg in segments:
        if not _GIT_MUTATOR.search(seg) or _GIT_SAFE.search(seg):
            continue
        m = _FORBIDDEN.search(seg)
        if m:
            warnings.append("names a copyrighted/legacy artifact ({})".format(m.group(0).strip()))
        elif _BLANKET.search(seg) or _COMMIT_ALL.search(seg):
            warnings.append("blanket add/commit — may sweep in un-tracked binaries")
    if warnings:
        h.system_message(
            "⚠ artifacts: git command {}. The legacy binaries (doida.exe / Main.exe / *.dll) and "
            "captures (*.pak/*.pcapng/*.tsv) are the TAINTED source — they stay out of git; only "
            "the committed Docs/RE specs (the clean truth) and fresh C# are tracked. Stage "
            "explicit source paths instead. Heads up only — nothing was blocked.".format(
                "; ".join(sorted(set(warnings)))
            )
        )
        return
    h.ok()


def _check_write(ev):
    path = h.file_path(ev)
    if path and _FORBIDDEN.search(path):
        h.system_message(
            "⚠ artifacts: writing '{}' — this looks like a copyrighted/legacy artifact (the "
            "tainted source), which must stay out of the repo (bring-your-own-assets policy); only "
            "the committed Docs/RE specs and fresh C# belong in git. Heads up only.".format(path)
        )
        return
    h.ok()


def main():
    ev = h.read_event()
    if h.tool_name(ev) == "Bash":
        _check_bash(ev)
    else:
        _check_write(ev)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
