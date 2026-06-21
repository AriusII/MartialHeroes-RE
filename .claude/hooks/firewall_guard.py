#!/usr/bin/env python
"""PreToolUse hook — the clean-room firewall guard (merges clean_room_guard +
protect_artifacts + git_commit_guard + git_add_dirty_guard + ida_provenance_guard).

Advisory only: it NEVER cancels a tool call. It dispatches on the tool name:

  * Write|Edit|MultiEdit -> (a) warn when C# being written looks transcribed from a
    decompiler (>=2 strong signatures), and (b) warn before a copyrighted/legacy artifact
    or capture is written into the tree.
  * Bash -> warn before a `git add`/`git commit`/`git stash` that names a copyrighted/legacy
    artifact or blanket-stages, before a `git commit` that would capture _dirty/ or
    copyrighted originals (and remind to verify the staged set), and before a `git add`
    that references a _dirty/ path or a forbidden extension. All co-firing advisories are
    combined into a single message.
  * mcp__ida__* -> log a provenance breadcrumb for the IDA call and (on the first call of
    the session, then occasionally) remind that IDA output is dirty-room.

Every path emits only systemMessage / ok and always exits 0 (fail-open).
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h

# --------------------------------------------------------------- clean-room paste guard
# Only guard source code; .md documentation of Hex-Rays is the sanctioned workflow.
_CODE_EXT = (".cs", ".cpp", ".cxx", ".cc", ".h", ".hpp")

_STRONG = [
    ("sub_xxxx", re.compile(r"\bsub_[0-9A-Fa-f]{4,}\b")),
    ("loc_xxxx", re.compile(r"\bloc_[0-9A-Fa-f]+\b")),
    ("dword_/byte_/off_", re.compile(r"\b(?:dword|byte|word|qword|off|unk|flt|dbl|stru|asc)_[0-9A-Fa-f]+\b")),
    ("LABEL_n", re.compile(r"\bLABEL_\d+\b")),
    ("_DWORD/_QWORD", re.compile(r"\b_(?:DWORD|QWORD|BYTE|WORD)\b")),
    ("__intN", re.compile(r"\b__int(?:8|16|32|64)\b")),
    ("__thiscall/__fastcall", re.compile(r"\b__(?:thiscall|fastcall|cdecl)\b")),
    ("*(_DWORD *)cast", re.compile(r"\*\(_(?:DWORD|QWORD|BYTE|WORD) \*\)")),
    ("qmemcpy", re.compile(r"\bqmemcpy\b")),
    ("__readfsdword", re.compile(r"\b__readfsdword\b")),
    ("HIDWORD/LODWORD", re.compile(r"\b(?:HIDWORD|LODWORD|HIWORD|LOWORD)\b")),
    ("__ROLn__/__RORn__", re.compile(r"\b__RO[LR]\d__\b")),
    ("MEMORY[]", re.compile(r"\bMEMORY\[")),
    ("MSVC-mangled", re.compile(r"\?[A-Za-z0-9_@$?]+@@[A-Za-z0-9_@$?]+")),
]
_VN_LINE = re.compile(r"^\s*v\d+ = ", re.M)


def _detect(text):
    found = []
    for label, rx in _STRONG:
        if rx.search(text):
            found.append(label)
    if len(_VN_LINE.findall(text)) >= 4:
        found.append("vN= cascade")
    return found


def _clean_room_msg(ev):
    path = h.file_path(ev).lower()
    if not path.endswith(_CODE_EXT):
        return None
    text = h.strip_comments_strings(h.added_text(ev))
    if not text.strip():
        return None
    found = _detect(text)
    if len(found) >= 2:
        return (
            "⚠ clean-room: this C# looks transcribed from a decompiler "
            "(signals: {}). Confirm the behavior in IDA — the single source of truth — then "
            "DESCRIBE it in neutral Docs/RE prose and re-implement fresh from that spec; never "
            "paste decompiler output. {} Heads up only — the write was not blocked.".format(
                ", ".join(found[:6]), h.CLEAN_ROOM_BLURB
            )
        )
    return None


# ----------------------------------------------------------------- artifact protection
# The canonical artifact set + path regex live in _hooklib (ONE source of truth shared by the
# write / bash / git-add / git-commit checks below — they used to drift apart).
_FORBIDDEN = h.FORBIDDEN_PATH_RE
_GIT_MUTATOR = re.compile(r"\bgit\s+(add|commit|stash)\b", re.I)
_GIT_SAFE = re.compile(r"\bgit\s+(rm|restore|reset)\b", re.I)
_BLANKET = re.compile(r"\bgit\s+add\s+(-A\b|--all\b|\.\s|\.$|:/)", re.I)
_COMMIT_ALL = re.compile(r"\bgit\s+commit\s+.*(-a\b|--all\b)", re.I)


def _artifact_bash_msg(ev):
    cmd = h.tool_input(ev).get("command", "") or ""
    if not cmd:
        return None
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
        return (
            "⚠ artifacts: git command {}. The legacy binaries (doida.exe / Main.exe / *.dll) and "
            "captures (*.pak/*.pcapng/*.tsv) are the TAINTED source — they stay out of git; only "
            "the committed Docs/RE specs (the clean truth) and fresh C# are tracked. Stage "
            "explicit source paths instead. Heads up only — nothing was blocked.".format(
                "; ".join(sorted(set(warnings)))
            )
        )
    return None


def _artifact_write_msg(ev):
    path = h.file_path(ev)
    if path and _FORBIDDEN.search(path):
        return (
            "⚠ artifacts: writing '{}' — this looks like a copyrighted/legacy artifact (the "
            "tainted source), which must stay out of the repo (bring-your-own-assets policy); only "
            "the committed Docs/RE specs and fresh C# belong in git. Heads up only.".format(path)
        )
    return None


# ------------------------------------------------------------------- git commit guard
# Copyrighted-original / captured artifacts that must never be committed (all gitignored) —
# shared canonical tuple in _hooklib.
_FORBIDDEN_EXT = h.FORBIDDEN_EXTS
_GIT_COMMIT = re.compile(r"\bgit\b[^\n;&|]*\bcommit\b")


def _git_commit_msg(ev):
    command = h.tool_input(ev).get("command", "") or ""
    if not _GIT_COMMIT.search(command):
        return None

    pdir = h.project_dir(ev)
    staged = h.staged_files(pdir)
    if not staged:
        # Nothing staged (or git unavailable) — still nudge to verify, but don't alarm.
        return (
            "ℹ before committing: run /clean-room-firewall-check and confirm `git diff --cached` "
            "holds only neutral specs + fresh C# (no _dirty/, no originals). Advisory only."
        )

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
        return (
            "⚠ clean-room firewall: {}. These are gitignored by policy — unstage them "
            "(`git restore --staged <path>`) before committing. Then run "
            "/clean-room-firewall-check. Advisory only — the commit was NOT blocked.".format(
                "; ".join(problems)
            )
        )

    return (
        "ℹ committing {} staged file(s). Reminder: run /clean-room-firewall-check and verify "
        "`git diff --cached` is all neutral specs + fresh code. Advisory only.".format(len(staged))
    )


# ----------------------------------------------------------------- git add dirty guard
_FORBIDDEN_EXT_RE = h.FORBIDDEN_EXT_RE
_DIRTY_RE = re.compile(r"_dirty\b", re.I)
_GIT_ADD = re.compile(r"\bgit\b[^\n;&|]*\badd\b")


def _git_add_dirty_msg(ev):
    command = h.tool_input(ev).get("command", "") or ""
    if not _GIT_ADD.search(command):
        return None

    hits = []
    if _DIRTY_RE.search(command):
        hits.append("a _dirty/ path (tainted, decompiler-derived)")
    if _FORBIDDEN_EXT_RE.search(command):
        m = _FORBIDDEN_EXT_RE.search(command)
        hits.append("a copyrighted/captured original ({} extension)".format(m.group(0)))

    if hits:
        return (
            "⚠ clean-room firewall: this `git add` references {}. That is the TAINTED source "
            "(decompiler-derived / copyrighted) — only the committed Docs/RE specs (the clean, "
            "IDA-derived truth) belong in git. Those paths are gitignored by policy and must never "
            "be staged — drop the path (and any `-f`/`--force`). The firewall depends on them "
            "staying out of git. Advisory only — nothing was blocked.".format(" and ".join(hits))
        )
    return None


# --------------------------------------------------------------- IDA provenance guard
_STATE_FILE = "ida_usage.jsonl"
_COUNTER_FILE = "ida_call_counter.txt"


def _bump_counter(pdir):
    """Return the new call count (1-based). Counter-only, never wall-clock — keeps the audit
    line stable/diffable and avoids embedding timestamps in the breadcrumb."""
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
    # Shared extractor in _hooklib so this breadcrumb matches re_provenance_logger's record.
    return h.ida_target_hint(ti)


def _ida_provenance(ev, name):
    """Log the IDA call and, on the first call of the session (then ~every 25th), nudge.
    Emits directly (this is the only check on the IDA path)."""
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
            "ℹ IDA call #{} logged. IDA/doida.exe is the single source of truth — confirm facts "
            "here, never guess; STOP if the MCP is down or on the wrong DB. Its output is "
            "dirty-room: write findings ONLY to Docs/RE/_dirty/, then have a spec-author rewrite "
            "(never copy) them into the neutral committed specs (the derived truth). {} "
            "Advisory only.".format(count, h.CLEAN_ROOM_BLURB)
        )
        return
    h.ok()


# ------------------------------------------------------------------------- dispatch

def main():
    ev = h.read_event()
    name = h.tool_name(ev)

    if name == "Bash":
        msgs = []
        for fn in (_artifact_bash_msg, _git_commit_msg, _git_add_dirty_msg):
            m = fn(ev)
            if m:
                msgs.append(m)
        if msgs:
            h.system_message("\n\n".join(msgs))
        else:
            h.ok()
        return

    if name.startswith("mcp__ida__"):
        _ida_provenance(ev, name)
        return

    if name in ("Write", "Edit", "MultiEdit"):
        # clean-room (code ext) and artifact (forbidden ext) are disjoint on the path, so at
        # most one fires — collect both anyway for uniformity.
        msgs = []
        for fn in (_clean_room_msg, _artifact_write_msg):
            m = fn(ev)
            if m:
                msgs.append(m)
        if msgs:
            h.system_message("\n\n".join(msgs))
        else:
            h.ok()
        return

    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
