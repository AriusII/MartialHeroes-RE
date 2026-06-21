#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — consolidated Godot advisories (merges
godot_tscn_guard + godot_namespace_guard + gltf_crash_guard + godot_uid_nudge +
coordinate_convention_nudge).

Advisory only: it NEVER cancels the write. On a `.tscn` or a Godot (05.Presentation) `.cs`
file it runs each relevant detector and combines every one that fires into a SINGLE
systemMessage:

  * .tscn inline `script=` header attribute (silently ignored -> gray screen).
  * bare Input./Environment./Time. that collide with the sibling project namespace (CS0234).
  * GltfDocument.AppendFromBuffer (native crash on this project's GLBs).
  * a freshly Written Godot script/scene needing a Godot 4.4+ UID.
  * coordinate math in World/ code that does not route through WorldCoordinates.

Each detector keeps its source hook's gating and exact wording; fail-open.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h


# --------------------------------------------------------------------- .tscn script

def _tscn_msg(ev):
    path = h.file_path(ev)
    if not h.is_tscn(path):
        return None

    added = h.added_text(ev)
    if not added.strip():
        return None

    if h.tscn_has_inline_script(added):
        return (
            "⚠ Godot .tscn: `script` written as a node-header attribute "
            "([node ... script=ExtResource(...)]) is SILENTLY IGNORED — the node ends up "
            "script-less (no _Ready -> gray screen). Move it to a PROPERTY LINE under the "
            "node header instead:\n"
            "    [node name=\"Root\" type=\"Node3D\"]\n"
            "    script = ExtResource(\"1\")\n"
            "Heads up only — the write was not blocked."
        )
    return None


# -------------------------------------------------------------- namespace collision

def _namespace_msg(ev):
    path = h.file_path(ev)
    if not h.is_godot_cs(path):
        return None

    stripped = h.strip_comments_strings(h.added_text(ev))
    if not stripped.strip():
        return None

    cols = h.godot_ns_collisions(stripped)
    if cols:
        return (
            "⚠ Godot C#: bare reference(s) {} collide with the sibling "
            "MartialHeroes.Client.Godot.* namespace (CS0234 — they resolve to the project "
            "namespace, not the Godot class). Qualify them as global::Godot.{} etc. "
            "Heads up only — the write was not blocked.".format(", ".join(cols), cols[0])
        )
    return None


# ------------------------------------------------------------------- glTF crash

def _gltf_msg(ev):
    path = h.file_path(ev)
    if not h.is_godot_cs(path):
        return None

    stripped = h.strip_comments_strings(h.added_text(ev))
    if not stripped.strip():
        return None

    if h.uses_gltf_appendfrombuffer(stripped):
        return (
            "⚠ Godot C#: GltfDocument.AppendFromBuffer CRASHES natively on this project's "
            "generated GLBs (no managed stack trace — hard fault). Build a Godot ArrayMesh "
            "directly instead (see the BudMeshBuilder / SknMeshBuilder pattern). "
            "Heads up only — the write was not blocked."
        )
    return None


# ----------------------------------------------------------------------- UID nudge

def _uid_msg(ev):
    # Narrow to Write — Edit/MultiEdit operate on files that already exist.
    if h.tool_name(ev) != "Write":
        return None

    path = h.file_path(ev)
    is_godot_script = h.is_godot_cs(path)
    is_godot_scene = h.is_tscn(path)
    if not (is_godot_script or is_godot_scene):
        return None

    if is_godot_scene:
        return (
            "ℹ New Godot scene: Godot 4.4+ wants a stable uid — ensure the [gd_scene ...] "
            "header carries uid=\"uid://...\", and write any node's script as a PROPERTY LINE "
            "under the node header (script = ExtResource(\"...\")), never as a header "
            "attribute (silently ignored). The editor normally generates this on import. "
            "Reminder only — nothing was blocked."
        )

    # New Godot C# script.
    return (
        "ℹ New Godot C# script: Godot 4.4+ references scripts by UID — it expects a companion "
        "<name>.cs.uid file (normally generated on the next editor import). Reminder only — "
        "nothing was blocked."
    )


# -------------------------------------------------------------- coordinate convention

def _is_world_cs(path):
    """C# under the Godot presentation project's World/ directory."""
    p = (path or "").replace("\\", "/").lower()
    return (
        p.endswith(".cs")
        and "/05.presentation/martialheroes.client.godot/" in p
        and "/world/" in p
    )


def _coordinate_msg(ev):
    path = h.file_path(ev)
    if not _is_world_cs(path):
        return None

    stripped = h.strip_comments_strings(h.added_text(ev))
    if not stripped.strip():
        return None

    # Low-noise: only fire when there's real coordinate math AND no ToGodot/WorldCoordinates
    # already in the written text (which would mean the convention is being honored).
    if h.has_coordinate_math(stripped) and "WorldCoordinates" not in stripped:
        return (
            "ℹ Godot World coordinates: remember WORLD geometry negates Z — route positions "
            "through Helpers/WorldCoordinates.ToGodot ((x,y,z) -> (x,y,-z)) rather than raw "
            "new Vector3(...). MESH-LOCAL .skn geometry negates X instead. "
            "Reminder only — nothing was blocked."
        )
    return None


# ----------------------------------------------------------- layer-05 authority leak
# Game-rule authority leaking into a passive Godot node: a stat field being MUTATED (compound
# assignment) inside layer 05. Authority belongs in Client.Domain/Application (04); the Godot
# node should only RENDER the result it receives over an Application channel.
_AUTHORITY_MUT = re.compile(
    r"\b(?:Health|Hp|Mp|Mana|Stamina|Exp|Experience|Gold)\s*(?:\+=|-=|\*=|/=)"
)


def _authority_msg(ev):
    path = h.file_path(ev)
    if not h.is_godot_cs(path):
        return None
    stripped = h.strip_comments_strings(h.added_text(ev))
    if not stripped.strip():
        return None
    if _AUTHORITY_MUT.search(stripped):
        return (
            "ℹ layer boundary: this Godot (05) node mutates what looks like a game-state/stat "
            "field (compound assignment on Health/Hp/Mp/Exp/Gold/…). Layer 05 is PASSIVE "
            "rendering with ZERO game-rule authority — compute the rule in Client.Domain/"
            "Application (layer 04) and let the node render the result via an Application "
            "channel/intent. Reminder only — nothing was blocked."
        )
    return None


# ------------------------------------------------------------------------- dispatch

def main():
    ev = h.read_event()
    if h.tool_name(ev) not in ("Write", "Edit", "MultiEdit"):
        h.ok()
        return

    msgs = []
    for fn in (_tscn_msg, _namespace_msg, _gltf_msg, _uid_msg, _coordinate_msg, _authority_msg):
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
