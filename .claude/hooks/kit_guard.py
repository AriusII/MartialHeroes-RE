#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — keep the .claude/ kit self-consistent (merges
agent_md_guard + skill_md_guard + hook_advisory_guard + settings_wiring_nudge).

Advisory only: it NEVER blocks the edit. It dispatches by the written file's path (the four
kit file types are mutually exclusive, so at most one fires) and re-reads the FULL file from
disk (an Edit only carries a fragment):

  * .claude/agents/*.md   -> frontmatter drift vs KIT.md §1/§2/§4 (model/effort/tools, unknown
    skills, unknown Agent() roster members).
  * .claude/skills/<n>/SKILL.md -> frontmatter drift + missing bundled scripts (KIT.md §0/§5).
  * .claude/hooks/*.py    -> a BLOCKING signature (violates the advisory-only invariant);
    _hooklib.py is skipped (it defines the block regex).
  * .claude/settings.json -> hook wiring consistency (wired-but-missing / present-but-unwired)
    + a reminder to run the tooling-auditor.

Any read/parse error fails open (stays silent). Each sub-check keeps its source hook's wording.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h


def _split_list(raw):
    """Split a frontmatter list value ('[a, b]' or 'a, b') into clean names."""
    raw = (raw or "").strip().strip("[]")
    return [n.strip() for n in raw.split(",") if n.strip()]


# ------------------------------------------------------------------ agent .md guard
# tools: ... Agent(a, b, c) ... — capture the parenthesized roster list.
_AGENT_ROSTER = re.compile(r"\bAgent\s*\(\s*([^)]*)\)", re.I)


def _agent_md_msg(ev):
    path = h.file_path(ev)
    if not h.is_agent_md(path):
        return None

    # Validate the FULL file (the Edit fragment is not enough for frontmatter checks).
    try:
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
    except Exception:
        return None

    issues = list(h.agent_frontmatter_issues(text))

    pdir = h.project_dir(ev)
    fm = h.parse_frontmatter(text)

    # skills: each named skill must resolve to a .claude/skills/<name>/ directory.
    for name in _split_list(fm.get("skills", "")):
        skill_dir = os.path.join(pdir, ".claude", "skills", name)
        if not os.path.isdir(skill_dir):
            issues.append("skills: unknown skill '{}'".format(name))

    # tools: Agent(...) roster — each member must be a .claude/agents/<member>.md file.
    tools_val = fm.get("tools", "")
    m = _AGENT_ROSTER.search(tools_val)
    if m:
        for member in _split_list(m.group(1)):
            agent_file = os.path.join(pdir, ".claude", "agents", member + ".md")
            if not os.path.isfile(agent_file):
                issues.append("Agent(): unknown agent '{}'".format(member))

    if issues:
        name = os.path.basename(path)
        return (
            "⚠ agent kit: {} has frontmatter drift — {}. See .claude/KIT.md §1/§2/§4. "
            "Heads up only — the write was not blocked.".format(name, "; ".join(issues[:8]))
        )
    return None


# ------------------------------------------------------------------ SKILL.md guard
# Body references to a bundled script, e.g. ${CLAUDE_SKILL_DIR}/scripts/run.py
_SCRIPT_REF = re.compile(r"\$\{CLAUDE_SKILL_DIR\}/scripts/([^\s`\"'<>)]+)")


def _skill_md_msg(ev):
    path = h.file_path(ev)
    if not h.is_skill_md(path):
        return None

    # Read the FULL SKILL.md from disk; on any error, stay silent (fail-open).
    try:
        with open(path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except Exception:
        return None

    issues = list(h.skill_frontmatter_issues(text))

    skill_dir = os.path.dirname(path)
    seen = set()
    for m in _SCRIPT_REF.finditer(text):
        rel = m.group(1)
        if rel in seen:
            continue
        seen.add(rel)
        if not os.path.isfile(os.path.join(skill_dir, "scripts", rel)):
            issues.append("bundled script not found: scripts/{}".format(rel))

    if not issues:
        return None

    skill_name = os.path.basename(os.path.dirname(path)) or "skill"
    return (
        "⚠ SKILL.md '{}': {}. See .claude/KIT.md §0/§5. "
        "Heads up only — the edit was not blocked.".format(
            skill_name, "; ".join(issues[:6])
        )
    )


# --------------------------------------------------------------- hook advisory guard

def _hook_advisory_msg(ev):
    path = h.file_path(ev)
    if not h.is_hook_py(path):
        return None

    # _hooklib.py is the canonical home of the block-signature regex; never flag it.
    if os.path.basename(path) == "_hooklib.py":
        return None

    # An Edit/MultiEdit only carries its changed slice; re-read the whole file so a
    # blocking line elsewhere is still seen. Any read error -> stay silent (fail-open).
    try:
        with open(path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except Exception:
        return None

    if h.hook_can_block(text):
        return (
            "⚠ advisory-only: {} contains a BLOCKING signature "
            "(sys.exit(2) / {{\"decision\": \"block\"}} / a deny|ask permissionDecision). "
            "On this project every hook MUST be advisory-only — it may only warn via "
            "h.system_message / h.additional_context and must always exit 0. Blocking is "
            "the orchestrator's call in settings.json, never the hook's. Heads up only — "
            "the edit was not blocked.".format(os.path.basename(path))
        )
    return None


# -------------------------------------------------------------- settings wiring nudge
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


def _settings_wiring_msg(ev):
    path = h.file_path(ev)
    if not h.is_claude_settings(path):
        return None

    # Read from disk (the edit already landed; PostToolUse runs after). Any failure here
    # — including a mid-edit invalid JSON — is fail-open: say nothing rather than crash.
    try:
        with open(path, "r", encoding="utf-8") as fh:
            settings = json.loads(h.strip_bom(fh.read()))
    except Exception:
        return None
    if not isinstance(settings, dict):
        return None

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

    return " ".join(lines)


# ------------------------------------------------------------------------- dispatch

def main():
    ev = h.read_event()
    if h.tool_name(ev) not in ("Write", "Edit", "MultiEdit"):
        h.ok()
        return

    msgs = []
    for fn in (_agent_md_msg, _skill_md_msg, _hook_advisory_msg, _settings_wiring_msg):
        m = fn(ev)
        if m:
            msgs.append(m)

    if msgs:
        h.system_message("\n\n".join(msgs))
    else:
        h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
