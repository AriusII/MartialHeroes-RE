#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — flag policy drift in a written agent definition.

Advisory only: it NEVER blocks the write. After an agent `.md` under .claude/agents/ is
written or edited, it re-reads the WHOLE file from disk (an Edit only carries a fragment, but
frontmatter validation needs the full block) and nudges on §1/§2/§4 drift from .claude/KIT.md:
missing/invalid `model:` or `effort:`, an `allowed-tools:` field (that is a skill field), a
stale `claude-3-*` id, a `skills:` entry with no skill dir, or an `Agent(...)` roster member
with no agent file. Any read error fails open (h.ok()).
"""
import os
import re

import _hooklib as h

_WRITE_TOOLS = ("Write", "Edit", "MultiEdit")

# tools: ... Agent(a, b, c) ... — capture the parenthesized roster list.
_AGENT_ROSTER = re.compile(r"\bAgent\s*\(\s*([^)]*)\)", re.I)


def _split_list(raw):
    """Split a frontmatter list value ('[a, b]' or 'a, b') into clean names."""
    raw = (raw or "").strip().strip("[]")
    return [n.strip() for n in raw.split(",") if n.strip()]


def main():
    ev = h.read_event()
    if h.tool_name(ev) not in _WRITE_TOOLS:
        h.ok()
        return

    path = h.file_path(ev)
    if not h.is_agent_md(path):
        h.ok()
        return

    # Validate the FULL file (the Edit fragment is not enough for frontmatter checks).
    try:
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
    except Exception:
        h.ok()
        return

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
        h.system_message(
            "⚠ agent kit: {} has frontmatter drift — {}. See .claude/KIT.md §1/§2/§4. "
            "Heads up only — the write was not blocked.".format(name, "; ".join(issues[:8]))
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
