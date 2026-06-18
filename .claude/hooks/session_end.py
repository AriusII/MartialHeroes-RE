#!/usr/bin/env python
"""Stop / SubagentStop / PreCompact hook — end-of-turn reminders (merges stop_loose_ends +
memory_persist_nudge).

Advisory only: it NEVER blocks a stop or a compaction. It dispatches on hook_event_name:

  * Stop        -> loose-ends note (NotImplementedException / TODO / FIXME left in touched C#)
                   AND the persist-knowledge nudge.
  * SubagentStop -> loose-ends note only.
  * PreCompact  -> the persist-knowledge nudge only, phrased urgently (loss is imminent). This
                   folds the old PreCompact double-fire into one reminder.

The loose-ends scan reads the session's touched-file breadcrumb (written by cs_post_edit) and
deduplicates against the last note so it does not repeat. Any error fails open (stays silent).
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h

_MARKER = re.compile(r"(throw new NotImplementedException|//\s*TODO|//\s*FIXME|//\s*HACK)", re.I)


def _loose_ends_msg(ev):
    """Note (do NOT block on) loose ends left in touched C# files. Returns a message or None,
    and dedup-writes the signature so an identical note does not repeat."""
    pdir = h.project_dir(ev)
    sdir = h.state_dir(pdir)
    breadcrumb = os.path.join(sdir, "touched.jsonl")
    if not os.path.exists(breadcrumb):
        return None

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
        return None

    flagged = []
    for p in paths[-50:]:
        try:
            with open(p, "r", encoding="utf-8", errors="replace") as fh:
                if _MARKER.search(fh.read()):
                    flagged.append(os.path.basename(p))
        except Exception:
            continue

    if not flagged:
        return None

    flagged = sorted(set(flagged))
    last_path = os.path.join(sdir, "loose_ends_last.txt")
    signature = ",".join(flagged)
    try:
        if os.path.exists(last_path):
            with open(last_path, "r", encoding="utf-8") as fh:
                if fh.read().strip() == signature:
                    return None
        with open(last_path, "w", encoding="utf-8") as fh:
            fh.write(signature)
    except Exception:
        pass

    return (
        "ℹ loose ends in touched files ({}): NotImplementedException / TODO / FIXME still present. "
        "Not blocking — note for next time.".format(", ".join(flagged[:10]))
    )


def _memory_persist_msg(event):
    """One-line nudge to persist freshly-discovered knowledge before the context holding it is
    lost. PreCompact phrases it urgently; Stop is the gentler reminder."""
    if event == "PreCompact":
        return (
            "ℹ context is about to compact — persist NOW any new opcodes / struct layouts / "
            "asset mappings: raw → Docs/RE/_dirty/, promoted → specs (packets|formats|structs|"
            "specs|opcodes.md), durable facts → auto-memory. " + h.CLEAN_ROOM_BLURB
        )
    return (
        "ℹ before this fades: if you discovered any opcode / struct layout / asset mapping, "
        "save it (raw → Docs/RE/_dirty/, promoted → specs, durable facts → auto-memory). "
        "Advisory only."
    )


def main():
    ev = h.read_event()

    # Defensive anti-loop guard for Stop (we never block, but mirror the house pattern).
    if ev.get("stop_hook_active"):
        h.ok()
        return

    event = ev.get("hook_event_name", "")
    msgs = []

    # Loose-ends fires for Stop and SubagentStop.
    if event in ("Stop", "SubagentStop"):
        m = _loose_ends_msg(ev)
        if m:
            msgs.append(m)

    # Persist-knowledge nudge fires for Stop and PreCompact.
    if event in ("Stop", "PreCompact"):
        msgs.append(_memory_persist_msg(event))

    if msgs:
        h.system_message("\n\n".join(msgs))
    else:
        h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
