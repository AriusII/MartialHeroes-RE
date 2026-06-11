#!/usr/bin/env python
"""PreToolUse(Write|Edit|MultiEdit) hook — warn when C# being written looks transcribed
from IDA/Hex-Rays output.

Advisory only: it NEVER cancels the write. It strips comments/strings first (documenting
decompiler artifacts in comments is allowed) and only warns when >=2 distinct strong
signatures appear, to avoid false positives.
"""
import re
import _hooklib as h

# Only guard source code; .md documentation of Hex-Rays is the sanctioned workflow.
_CODE_EXT = (".cs", ".cpp", ".cxx", ".cc", ".h", ".hpp")

# Each entry: (label, compiled regex). The vN-cascade is handled separately.
_STRONG = [
    ("sub_xxxx", re.compile(r"\bsub_[0-9A-Fa-f]{4,}\b")),
    ("loc_xxxx", re.compile(r"\bloc_[0-9A-Fa-f]+\b")),
    ("dword_/byte_/off_", re.compile(r"\b(?:dword|byte|word|qword|off|unk|flt|dbl|stru|asc)_[0-9A-Fa-f]+\b")),
    ("LABEL_n", re.compile(r"\bLABEL_\d+\b")),
    ("_DWORD/_QWORD", re.compile(r"\b_(?:DWORD|QWORD|BYTE|WORD)\b")),
    ("__intN", re.compile(r"\b__int(?:8|16|32|64)\b")),
    ("__thiscall/__fastcall", re.compile(r"\b__(?:thiscall|fastcall|cdecl)\b")),
    ("*(_DWORD *)cast", re.compile(r"\*\(_(?:DWORD|QWORD|BYTE|WORD) \*\)")),
    ("qmemcpy", re.compile(r"\bqmemcpy\b")),
    ("__readfsdword", re.compile(r"\b__readfsdword\b")),
    ("HIDWORD/LODWORD", re.compile(r"\b(?:HIDWORD|LODWORD|HIWORD|LOWORD)\b")),
    ("__ROLn__/__RORn__", re.compile(r"\b__RO[LR]\d__\b")),
    ("MEMORY[]", re.compile(r"\bMEMORY\[")),
    ("MSVC-mangled", re.compile(r"\?[A-Za-z0-9_@$?]+@@[A-Za-z0-9_@$?]+")),
]
_VN_LINE = re.compile(r"^\s*v\d+ = ", re.M)


def _detect(text):
    found = []
    for label, rx in _STRONG:
        if rx.search(text):
            found.append(label)
    if len(_VN_LINE.findall(text)) >= 4:
        found.append("vN= cascade")
    return found


def main():
    ev = h.read_event()
    path = h.file_path(ev).lower()
    if not path.endswith(_CODE_EXT):
        h.ok()
        return

    text = h.strip_comments_strings(h.added_text(ev))
    if not text.strip():
        h.ok()
        return

    found = _detect(text)
    if len(found) >= 2:
        h.system_message(
            "⚠ clean-room: this C# looks transcribed from a decompiler "
            "(signals: {}). {} Heads up only — the write was not blocked.".format(
                ", ".join(found[:6]), h.CLEAN_ROOM_BLURB
            )
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
