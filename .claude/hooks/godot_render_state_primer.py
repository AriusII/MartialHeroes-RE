#!/usr/bin/env python
"""UserPromptSubmit hook — inject a short reminder of the current Godot render state when the
user's prompt is about the presentation layer.

When the prompt touches godot / render / scene / terrain / character / screenshot, this adds a
2-3 line orientation note (the demo state at commit c266e7e, the headless-screenshot verify
loop, and the four open render debts) so work starts from the known baseline instead of
rediscovering it. Stays silent for unrelated prompts to avoid noise.

Advisory only: it injects context via additionalContext and never blocks the prompt.
"""
import re

import _hooklib as h

_TRIGGER = re.compile(r"\b(godot|render(?:ing)?|scene|terrain|character|screenshot)\b", re.I)

_NOTE = (
    "Godot render state (commit c266e7e): the working demo is AREA 2 — a walled town "
    "(779 buildings + 40 monsters/NPCs) on textured multi-texture terrain, with an upright "
    "textured humanoid player, free/orbital camera, and HUD (inventory=I, skills=K).\n"
    "Headless verify loop (no user needed): run the Godot console exe "
    "`--headless --path <godotproj> --quit-after 150` to dump GD.Print/errors to stdout; "
    "for a real screenshot run WINDOWED with a temporary GDScript autoload that calls "
    "get_viewport().get_texture().get_image().save_png(...).\n"
    "Open render debts: (1) character SKINNING explodes the mesh (legacy bind convention not "
    "recovered) so chars are rendered static; (2) NPCs spawn at a fallback Y before async "
    "terrain loads; (3) EnvironmentNode is too dark; (4) water is unwired."
)


def main():
    ev = h.read_event()
    prompt = ev.get("prompt") or ""
    if not prompt.strip():
        h.ok()
        return

    if _TRIGGER.search(prompt):
        h.additional_context("UserPromptSubmit", _NOTE)
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
