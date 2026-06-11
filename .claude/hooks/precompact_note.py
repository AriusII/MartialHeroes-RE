#!/usr/bin/env python
"""PreCompact hook — remind to flush in-flight RE findings before context is compacted.

Advisory only. Never blocks compaction.
"""
import _hooklib as h


def main():
    ev = h.read_event()
    h.system_message(
        "ℹ context is about to compact. If you discovered opcodes, struct layouts, or crypto "
        "details this turn, write them into Docs/RE (raw → _dirty/, promoted specs → packets/"
        "formats/specs/opcodes.md) now so they survive. " + h.CLEAN_ROOM_BLURB
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
