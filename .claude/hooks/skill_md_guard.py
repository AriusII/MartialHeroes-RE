#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — nudge on SKILL.md frontmatter drift and
missing bundled scripts (see .claude/KIT.md §0/§5).

Advisory only: it NEVER blocks the edit. After a Write/Edit/MultiEdit to a
.claude/skills/<name>/SKILL.md it reads the file from disk and warns when the
frontmatter uses `tools:` instead of `allowed-tools:`, omits a when-to-use
`description:`, or when the body references a `${CLAUDE_SKILL_DIR}/scripts/<file>`
that is not actually bundled under the skill dir. Heads up only — fail-open.
"""
import os
import re

import _hooklib as h

_WRITE_TOOLS = ("Write", "Edit", "MultiEdit")

# Body references to a bundled script, e.g. ${CLAUDE_SKILL_DIR}/scripts/run.py
_SCRIPT_REF = re.compile(r"\$\{CLAUDE_SKILL_DIR\}/scripts/([^\s`\"'<>)]+)")


def main():
    ev = h.read_event()
    if h.tool_name(ev) not in _WRITE_TOOLS:
        h.ok()
        return

    path = h.file_path(ev)
    if not h.is_skill_md(path):
        h.ok()
        return

    # Read the FULL SKILL.md from disk; on any error, stay silent (fail-open).
    try:
        with open(path, "r", encoding="utf-8") as fh:
            text = fh.read()
    except Exception:
        h.ok()
        return

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
        h.ok()
        return

    skill_name = os.path.basename(os.path.dirname(path)) or "skill"
    h.system_message(
        "⚠ SKILL.md '{}': {}. See .claude/KIT.md §0/§5. "
        "Heads up only — the edit was not blocked.".format(
            skill_name, "; ".join(issues[:6])
        )
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
