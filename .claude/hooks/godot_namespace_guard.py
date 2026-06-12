#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — warn about bare Godot class references that
collide with sibling project namespaces.

Inside `namespace MartialHeroes.Client.Godot.*`, an unqualified `Input.`, `Environment.`,
or `Time.` does NOT resolve to the Godot class — the C# compiler binds it to the sibling
`MartialHeroes.Client.Godot.<Input|Environment|Time>` namespace first, yielding a confusing
CS0234 ("the type or namespace name '...' does not exist"). The fix is to fully qualify:

    global::Godot.Input.IsActionPressed(...)
    global::Godot.Environment ...
    global::Godot.Time.GetTicksMsec()

Advisory only: it NEVER cancels the write. Comments and string literals are stripped first
so documentation mentioning these names does not trip the detector.
"""
import _hooklib as h


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    if not h.is_godot_cs(path):
        h.ok()
        return

    stripped = h.strip_comments_strings(h.added_text(ev))
    if not stripped.strip():
        h.ok()
        return

    cols = h.godot_ns_collisions(stripped)
    if cols:
        h.system_message(
            "⚠ Godot C#: bare reference(s) {} collide with the sibling "
            "MartialHeroes.Client.Godot.* namespace (CS0234 — they resolve to the project "
            "namespace, not the Godot class). Qualify them as global::Godot.{} etc. "
            "Heads up only — the write was not blocked.".format(
                ", ".join(cols), cols[0]
            )
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
