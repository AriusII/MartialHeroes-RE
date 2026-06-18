#!/usr/bin/env python
"""UserPromptSubmit hook — inject targeted orientation when a prompt is RE-flavored and/or
about the Godot presentation layer (merges re_intent_primer + godot_render_state_primer).

Advisory only: it injects context via additionalContext and never blocks the prompt. Two
independent triggers, each preserving its source hook's behavior:

  * RE keywords (ida / decompile / opcode / packet / crypto / capture / …) -> the RE-context
    block: IDA status, the Ground-Truth Doctrine, the local capture count, the dirty->spec
    pipeline, the unbridled-fan-out note, the clean-room rule, and useful agents/skills.
  * Godot/render keywords (godot / render / scene / terrain / character / screenshot) -> the
    current Godot render-state note (the commit c266e7e demo, the headless-screenshot verify
    loop, and the four open render debts).

When both fire, both blocks are injected; when neither fires, the hook stays silent.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h

# RE intent — EN + FR keywords. Whole-word, case-insensitive.
_KEYWORDS = re.compile(
    r"\b("
    r"ida|hex[\- ]?rays|decompil\w*|d[ée]compil\w*|disassembl\w*|"
    r"opcode|packet|paquet|struct\w*|vtable|thiscall|mangl\w*|"
    r"pak|vfs|asset|capture|pcap\w*|tshark|wireshark|"
    r"crypto|chiffr\w*|cipher|xor|decrypt|d[ée]chiffr\w*|handshake|keyschedule|key schedule"
    r")\b",
    re.I,
)

# Godot render-state intent.
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


def _re_context(ev):
    """The RE-orientation block (re_intent_primer)."""
    pdir = h.project_dir(ev)
    pcap, tsv = h.find_captures(pdir)
    lines = [
        "[RE context] " + h.ida_status_line(),
        h.GROUND_TRUTH_BLURB,
        "Captures: {} .pcapng / {} .tsv local. .tsv are regenerable from .pcapng via tshark (see the pcap-extract skill) — never source, never committed.".format(pcap, tsv),
        "Dirty-room work writes ONLY to Docs/RE/_dirty/ (gitignored). Promote findings to clean specs (Docs/RE/specs|packets|formats|opcodes.md) before any C# is written.",
        "IDA fan-out is UNBRIDLED: run read analysts AND IDB writes massively in parallel — no ~3 cap, no one-writer-at-a-time rule; retry failed/conflicting calls. Goal = reverse ALL of doida.exe for the faithful 1:1 Godot port.",
        h.CLEAN_ROOM_BLURB,
        "Useful: agents @re-function-analyst @re-protocol-analyst @re-crypto-analyst; skills /ida-recon /ida-py /ida-crypto-hunt /pcap-extract.",
    ]
    return "\n".join(lines)


def main():
    ev = h.read_event()
    prompt = ev.get("prompt", "") or ""
    if not prompt.strip():
        h.ok()
        return

    blocks = []
    if _KEYWORDS.search(prompt):
        blocks.append(_re_context(ev))
    if _TRIGGER.search(prompt):
        blocks.append(_NOTE)

    if not blocks:
        h.ok()
        return

    h.additional_context("UserPromptSubmit", "\n\n".join(blocks))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
