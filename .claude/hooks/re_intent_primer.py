#!/usr/bin/env python
"""UserPromptSubmit hook — when the prompt is RE-flavored, inject targeted context.

Advisory only. Fires extra orientation exactly when the transcription/clean-room risk
peaks (decompiling, opcodes, crypto, captures). Stays silent otherwise.
"""
import re
import _hooklib as h

# EN + FR keywords. Whole-word, case-insensitive.
_KEYWORDS = re.compile(
    r"\b("
    r"ida|hex[\- ]?rays|decompil\w*|d[ée]compil\w*|disassembl\w*|"
    r"opcode|packet|paquet|struct\w*|vtable|thiscall|mangl\w*|"
    r"pak|vfs|asset|capture|pcap\w*|tshark|wireshark|"
    r"crypto|chiffr\w*|cipher|xor|decrypt|d[ée]chiffr\w*|handshake|keyschedule|key schedule"
    r")\b",
    re.I,
)


def main():
    ev = h.read_event()
    prompt = ev.get("prompt", "") or ""
    if not _KEYWORDS.search(prompt):
        h.ok()
        return

    pdir = h.project_dir(ev)
    pcap, tsv = h.find_captures(pdir)
    lines = [
        "[RE context] " + h.ida_status_line(),
        "Captures: {} .pcapng / {} .tsv local. .tsv are regenerable from .pcapng via tshark (see the pcap-extract skill) — never source, never committed.".format(pcap, tsv),
        "Dirty-room work writes ONLY to Docs/RE/_dirty/ (gitignored). Promote findings to clean specs (Docs/RE/specs|packets|formats|opcodes.md) before any C# is written.",
        h.CLEAN_ROOM_BLURB,
        "Useful: agents @re-static-analyst @re-protocol-analyst @re-crypto-analyst; skills /ida-recon /ida-script-runner /ida-crypto-hunt /pcap-extract.",
    ]
    h.additional_context("UserPromptSubmit", "\n".join(lines))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
