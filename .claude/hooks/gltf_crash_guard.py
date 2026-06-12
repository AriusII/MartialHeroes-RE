#!/usr/bin/env python
"""PostToolUse(Write|Edit) hook — warn against GltfDocument.AppendFromBuffer in Godot C#.

The native Godot 4 glTF importer crashes (a hard, native fault with no managed stack trace)
on this project's generated GLBs when fed through `GltfDocument.AppendFromBuffer`. The known-
good path is to build a Godot `ArrayMesh` directly from the decoded geometry — see the
BudMeshBuilder / SknMeshBuilder pattern in the presentation project.

Advisory only: it NEVER cancels the write. Comments and string literals are stripped first so
documentation mentioning the API does not trip the detector.
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

    if h.uses_gltf_appendfrombuffer(stripped):
        h.system_message(
            "⚠ Godot C#: GltfDocument.AppendFromBuffer CRASHES natively on this project's "
            "generated GLBs (no managed stack trace — hard fault). Build a Godot ArrayMesh "
            "directly instead (see the BudMeshBuilder / SknMeshBuilder pattern). "
            "Heads up only — the write was not blocked."
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
