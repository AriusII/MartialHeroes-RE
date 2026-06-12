#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — gentle zero-allocation reminder for hot-path code
in the Network (layer 02) and Assets (layer 03) projects.

Advisory only; never blocks. The blueprint mandates a zero-allocation pipeline: socket framing,
in-place decryption, and binary parsing all run on `Span<byte>`/`ReadOnlyMemory<byte>` slices
with no per-message heap traffic. This nudge flags the common allocation smells that creep into
those layers — array/list allocation inside a loop, LINQ materialization on a parse path, and
`.ToArray()`/`.ToList()` in tight code. Kept deliberately low-noise: it only fires for files in
layers 2-3, and a lone `.ToArray()` outside any loop will not trip the loop-scoped checks.
"""
import re
import _hooklib as h

# Allocation smells that are nearly always wrong on a hot path, regardless of loop context.
_ALWAYS = [
    ("LINQ .Select/.Where/.OrderBy/.GroupBy", re.compile(r"\.(?:Select|Where|OrderBy|GroupBy|Aggregate|ToDictionary)\(")),
    (".ToArray()/.ToList()", re.compile(r"\.To(?:Array|List)\(\)")),
    ("string.Split (allocates)", re.compile(r"\.Split\(")),
]
_LINQ_USING = re.compile(r"using\s+System\.Linq\b")

# Loop-scoped allocation smells: `new T[...]` or `new List<...>` appearing on a line that sits
# inside a for/foreach/while body. We approximate "inside a loop" by requiring a loop keyword
# somewhere in the added hunk plus an allocation — cheap, and good enough for a gentle nudge.
_LOOP_KW = re.compile(r"\b(?:for|foreach|while)\s*\(")
_NEW_ARRAY = re.compile(r"\bnew\s+\w[\w<>,. ]*\[")          # new byte[len], new Foo[n]
_NEW_LIST = re.compile(r"\bnew\s+(?:List|Dictionary|HashSet|StringBuilder)\s*<")


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    layer, _proj = h.layer_of(path)
    if layer not in (2, 3) or not path.lower().endswith(".cs"):
        h.ok()
        return

    text = h.strip_comments_strings(h.added_text(ev))
    if not text.strip():
        h.ok()
        return

    has_linq_using = bool(_LINQ_USING.search(text))
    hits = []
    for label, rx in _ALWAYS:
        # Only count LINQ operators when System.Linq is actually imported in the hunk, to avoid
        # flagging a method that merely happens to be named Select/Where on a non-LINQ type.
        if label.startswith("LINQ") and not has_linq_using:
            continue
        if rx.search(text):
            hits.append(label)

    in_loop = bool(_LOOP_KW.search(text))
    if in_loop and (_NEW_ARRAY.search(text) or _NEW_LIST.search(text)):
        hits.append("array/collection allocation inside a loop")

    if not hits:
        h.ok()
        return

    h.system_message(
        "ℹ zero-alloc nudge (layer 0{}, hot path) — {}. Prefer Span<byte>/ReadOnlySpan<byte>, "
        "`stackalloc` hoisted OUT of the loop (or a pooled/reused buffer), "
        "`SequenceReader`/`BinaryPrimitives` for parsing, and no LINQ on parse/decrypt paths. "
        "Advisory only.".format(layer, "; ".join(dict.fromkeys(hits)))
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
