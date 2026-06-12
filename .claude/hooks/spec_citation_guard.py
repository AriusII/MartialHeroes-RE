#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — nudge when a numbered-layer C# file gains a
magic numeric constant with no nearby `// spec:` citation.

Advisory only; never blocks. The clean-room rule is that every wire offset, opcode, table
size, or formula constant that originates from the legacy client must cite the neutral spec
it came from (e.g. `// spec: Docs/RE/packets/login.yaml`). This keeps the firewall auditable:
a reviewer can trace any literal back to a committed Docs/RE document rather than to IDA.

Low-noise by design: it ignores 0/1 and other tiny ints, common loop/shift constants, and
plain integers that are not "magic" (only hex >= 2 digits, 3+ digit decimals, and float
literals trip it). The magic scan runs on comment/string-stripped text so a literal quoted
in a comment or string never trips it; the citation check runs on the RAW added text so an
adjacent `// spec:` comment counts as satisfying the rule.
"""
import re
import _hooklib as h

# Numeric literals that are genuinely "magic" and usually encode a wire/format constant.
#   - hex with >= 2 digits (0xFF, 0x1A2B) — offsets, masks, opcodes
#   - float/double literals (1.5, 0.25f) — formulas, scale factors
#   - bare decimal integers with 3+ digits (949, 1024, 65535) — sizes, ids, code pages
_MAGIC = re.compile(r"\b0x[0-9A-Fa-f]{2,}\b|\b\d+\.\d+f?\b|(?<![\w.])\d{3,}\b")

# Small/common integers and shift counts that are self-evident from context and not worth a
# citation. We subtract these from the magic hit set before deciding to warn.
_BENIGN = re.compile(
    r"\b0x0+\b"          # 0x0, 0x00 …
    r"|\b0x0*1\b"        # 0x01 …
    r"|\b0x0*[Ff][Ff]\b" # 0xFF is borderline but extremely common as a byte mask — let it pass
)

_SPEC_CITE = re.compile(r"//\s*spec\s*:", re.I)


def _magic_hits(stripped):
    """Distinct genuinely-magic literals in already comment/string-stripped text."""
    hits = []
    for m in _MAGIC.finditer(stripped):
        tok = m.group(0)
        if _BENIGN.fullmatch(tok):
            continue
        hits.append(tok)
    # de-dup preserving order
    seen = set()
    out = []
    for t in hits:
        if t not in seen:
            seen.add(t)
            out.append(t)
    return out


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    if not h.is_layer_cs(path):
        h.ok()
        return

    raw = h.added_text(ev)
    if not raw.strip():
        h.ok()
        return

    # Already-cited edits are fine — a single nearby `// spec:` is enough to vouch for the hunk.
    if _SPEC_CITE.search(raw):
        h.ok()
        return

    hits = _magic_hits(h.strip_comments_strings(raw))
    if not hits:
        h.ok()
        return

    h.system_message(
        "ℹ clean-room: magic constant(s) without a `// spec:` citation ({}). "
        "If these come from the legacy format/protocol, cite the neutral spec they were read "
        "from (e.g. `// spec: Docs/RE/packets/<name>.yaml`) so the literal is auditable. "
        "Gentle nudge only — nothing was blocked.".format(", ".join(hits[:8]))
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
