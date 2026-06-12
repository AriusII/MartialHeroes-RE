#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — warn when a Godot `.tscn` attaches a script
the wrong way.

In a Godot 4 scene file, `script` is a PROPERTY LINE that belongs UNDER the node header:

    [node name="Root" type="Node3D"]
    script = ExtResource("1")

Writing it as a header attribute instead —

    [node name="Root" type="Node3D" script=ExtResource("1")]

— parses without error but is SILENTLY IGNORED. The node ends up with no script, so its
`_Ready()` never runs and you get a gray screen with no obvious cause (this has cost hours).

Advisory only: it NEVER cancels the write. It inspects only the text just written and warns
once when the broken inline form is detected.
"""
import _hooklib as h


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    if not h.is_tscn(path):
        h.ok()
        return

    added = h.added_text(ev)
    if not added.strip():
        h.ok()
        return

    if h.tscn_has_inline_script(added):
        h.system_message(
            "⚠ Godot .tscn: `script` written as a node-header attribute "
            "([node ... script=ExtResource(...)]) is SILENTLY IGNORED — the node ends up "
            "script-less (no _Ready -> gray screen). Move it to a PROPERTY LINE under the "
            "node header instead:\n"
            "    [node name=\"Root\" type=\"Node3D\"]\n"
            "    script = ExtResource(\"1\")\n"
            "Heads up only — the write was not blocked."
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
