#!/usr/bin/env python
"""UserPromptSubmit hook — inject targeted orientation when a prompt is RE-flavored and/or
about the Godot presentation layer (merges re_intent_primer + godot_render_state_primer).

Advisory only: it injects context via additionalContext and never blocks the prompt. Three
independent triggers, each preserving its source hook's behavior:

  * RE keywords (ida / decompile / opcode / packet / crypto / capture / …) -> the RE-context
    block: IDA status, the Ground-Truth Doctrine, the local capture count, the dirty->spec
    pipeline, the unbridled-fan-out note, the clean-room rule, and useful agents/skills.
  * Godot/render keywords (godot / render / scene / terrain / character / screenshot) -> the
    Godot render-state note: a pointer to Docs/ROADMAP.md (live state, not a hard-coded
    snapshot), the headless/screenshot verify loop, and the coordinate conventions.
  * Planning-scale keywords (campaign / roadmap / multi-phase / orchestrate / rearchitect / …)
    -> a pointer to route the mandate through the planning-orchestrator (O1) + /plan-campaign.

Any subset may fire; their blocks are concatenated. When none fire, the hook stays silent.
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
    "[Godot] The layer-05 client renders the world LIVE against the replica (login → "
    "enter-world → world render). For the CURRENT render state, open debts, and where to "
    "resume, read Docs/ROADMAP.md + Docs/RE/journal.md — don't trust a hard-coded snapshot.\n"
    "Headless verify loop (no user needed): the godot-run-headless skill builds the layer-05 "
    "assembly then runs the Godot 4.6.3 console exe `--headless --path <godotproj> "
    "--quit-after 150` (GD.Print/errors to stdout); for a real screenshot it runs WINDOWED "
    "with a temporary GDScript autoload that grabs the viewport to a PNG.\n"
    "Coordinate conventions (get them wrong and the world mirrors): WORLD geometry negates Z "
    "(Helpers/WorldCoordinates.ToGodot); MESH-LOCAL .skn geometry negates X; cells 1024 / "
    "65×65 grid / spacing 16."
)

# Big multi-phase mandate -> route through the planning domain (O1). Strong scale signals only,
# to stay low-noise (bare 'plan' is too common to trigger on).
_PLAN_TRIGGER = re.compile(
    r"\b(campaign|roadmap|giga[- ]?workflow|multi[- ]?(?:step|phase)|several phases|"
    r"reverse[- ]?engineer all|refactor (?:the )?whole|re[- ]?architect\w*|orchestrat\w*)\b",
    re.I,
)
_PLAN_NOTE = (
    "[Planning] This looks like a multi-phase mandate. Consider routing it through the "
    "planning-orchestrator (O1): it reformulates the mandate, fans out refining workers, and "
    "authors a FINAL PLAN as a precise PHASE/OBJECTIVE workflow (the /plan-campaign skill). "
    "Doctrine: Tier-1 session → Tier-2 domain orchestrator (planning-/re-/csharp-port-/godot-/"
    "docs-tooling-) → Tier-3 worker; two levels of orchestration max."
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
        "RE pipeline (gates): G0 /re-brainstorm (plan the attack + pick the mcp__ida__* tool per angle) -> G1 recover static into _dirty/ -> G2 CONFIRM end-to-end on the ?ext=dbg debugger for every load-bearing fact (re-validator; never dbg_start; static is only a hypothesis until confirmed) -> G3 spec-author promotes a neutral spec -> G4 /re-handoff stamps it implementation-ready -> ONLY THEN does C# port it. IDA = the strict truth; /ida-mcp-connect carries the toolbox mapping which tool serves each angle.",
        "Useful: agents @re-function-analyst @re-protocol-analyst @re-crypto-analyst; skills /ida-mcp-connect /re-brainstorm /ida-recon /ida-explore /ida-py /ida-crypto-hunt /pcap-extract /re-handoff.",
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
    if _PLAN_TRIGGER.search(prompt):
        blocks.append(_PLAN_NOTE)

    if not blocks:
        h.ok()
        return

    h.additional_context("UserPromptSubmit", "\n\n".join(blocks))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
