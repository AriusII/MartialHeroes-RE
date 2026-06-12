#!/usr/bin/env python
"""PostToolUse(Write|Edit) hook — gentle reminder of the coordinate conventions when editing
Godot World code.

Two distinct, easy-to-confuse axis flips are in play:

  * WORLD geometry negates Z — use Helpers/WorldCoordinates.ToGodot: (x, y, z) -> (x, y, -z).
  * MESH-LOCAL .skn geometry negates X instead.

Hand-rolling `new Vector3(...)` / `RotationDegrees` in World code without going through
`WorldCoordinates` is how things end up mirrored or in the wrong sector. This nudge fires only
for C# under 05.Presentation/.../World/ that does coordinate math AND does not already mention
`WorldCoordinates`, to stay low-noise.

Advisory only: it NEVER cancels the write.
"""
import _hooklib as h


def _is_world_cs(path):
    """C# under the Godot presentation project's World/ directory."""
    p = (path or "").replace("\\", "/").lower()
    return (
        p.endswith(".cs")
        and "/05.presentation/martialheroes.client.godot/" in p
        and "/world/" in p
    )


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    if not _is_world_cs(path):
        h.ok()
        return

    stripped = h.strip_comments_strings(h.added_text(ev))
    if not stripped.strip():
        h.ok()
        return

    # Low-noise: only fire when there's real coordinate math AND no ToGodot/WorldCoordinates
    # already in the written text (which would mean the convention is being honored).
    if h.has_coordinate_math(stripped) and "WorldCoordinates" not in stripped:
        h.system_message(
            "ℹ Godot World coordinates: remember WORLD geometry negates Z — route positions "
            "through Helpers/WorldCoordinates.ToGodot ((x,y,z) -> (x,y,-z)) rather than raw "
            "new Vector3(...). MESH-LOCAL .skn geometry negates X instead. "
            "Reminder only — nothing was blocked."
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
