#!/usr/bin/env python
"""PostToolUse(Write) hook — remind about Godot 4.4+ UID requirements when a NEW Godot script
or scene file is created.

Godot 4.4+ tracks resources by a stable UID. A freshly authored C# script wants a matching
`<name>.cs.uid` companion, and a freshly authored scene needs its own `uid="uid://..."` on the
`[gd_scene ...]` header (and, for any node that gets a script, the `script` assignment written
as a PROPERTY LINE under the node header — not a header attribute, which is silently ignored).
The Godot editor normally generates the .uid on import; this is just a heads-up so a hand-
written file does not ship without one.

Fires only on Write (file creation), only for Godot .cs / .tscn files, to stay low-noise.

Advisory only: it NEVER cancels the write.
"""
import _hooklib as h


def main():
    ev = h.read_event()
    # Narrow to Write — Edit/MultiEdit operate on files that already exist.
    if h.tool_name(ev) != "Write":
        h.ok()
        return

    path = h.file_path(ev)
    is_godot_script = h.is_godot_cs(path)
    is_godot_scene = h.is_tscn(path)
    if not (is_godot_script or is_godot_scene):
        h.ok()
        return

    if is_godot_scene:
        h.system_message(
            "ℹ New Godot scene: Godot 4.4+ wants a stable uid — ensure the [gd_scene ...] "
            "header carries uid=\"uid://...\", and write any node's script as a PROPERTY LINE "
            "under the node header (script = ExtResource(\"...\")), never as a header "
            "attribute (silently ignored). The editor normally generates this on import. "
            "Reminder only — nothing was blocked."
        )
        return

    # New Godot C# script.
    h.system_message(
        "ℹ New Godot C# script: Godot 4.4+ references scripts by UID — it expects a companion "
        "<name>.cs.uid file (normally generated on the next editor import). Reminder only — "
        "nothing was blocked."
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
