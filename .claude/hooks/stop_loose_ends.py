#!/usr/bin/env python
"""Stop / SubagentStop hook — note (do NOT block on) loose ends left in touched C# files.

Advisory only. Honors 'conseil uniquement': it never forces continuation. It scans the
session's touched-file breadcrumb for NotImplementedException / TODO / FIXME markers and
surfaces a one-line note, deduplicated against the last note so it does not repeat.
"""
import json
import os
import re
import _hooklib as h

_MARKER = re.compile(r"(throw new NotImplementedException|//\s*TODO|//\s*FIXME|//\s*HACK)", re.I)


def main():
    ev = h.read_event()
    # Anti-loop guard (defensive even though we never block).
    if ev.get("stop_hook_active"):
        h.ok()
        return

    pdir = h.project_dir(ev)
    sdir = h.state_dir(pdir)
    breadcrumb = os.path.join(sdir, "touched.jsonl")
    if not os.path.exists(breadcrumb):
        h.ok()
        return

    paths = []
    try:
        with open(breadcrumb, "r", encoding="utf-8") as fh:
            for line in fh:
                try:
                    rec = json.loads(line)
                    p = rec.get("path", "")
                    if p.lower().endswith(".cs") and p not in paths:
                        paths.append(p)
                except Exception:
                    continue
    except Exception:
        h.ok()
        return

    flagged = []
    for p in paths[-50:]:
        try:
            with open(p, "r", encoding="utf-8", errors="replace") as fh:
                if _MARKER.search(fh.read()):
                    flagged.append(os.path.basename(p))
        except Exception:
            continue

    if not flagged:
        h.ok()
        return

    flagged = sorted(set(flagged))
    last_path = os.path.join(sdir, "loose_ends_last.txt")
    signature = ",".join(flagged)
    try:
        if os.path.exists(last_path):
            with open(last_path, "r", encoding="utf-8") as fh:
                if fh.read().strip() == signature:
                    h.ok()
                    return
        with open(last_path, "w", encoding="utf-8") as fh:
            fh.write(signature)
    except Exception:
        pass

    h.system_message(
        "ℹ loose ends in touched files ({}): NotImplementedException / TODO / FIXME still present. "
        "Not blocking — note for next time.".format(", ".join(flagged[:10]))
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
