#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — consolidated C# advisories (merges cp949_nudge +
unsafe_alloc_nudge + spec_citation_guard + test_after_core_edit).

Advisory only: it NEVER blocks the edit and NEVER runs anything. On a written/edited C# file it
runs each relevant sub-advisory and combines every one that fires into a SINGLE systemMessage:

  * CP949 — Assets.Parsers code that decodes legacy text without the CP949 provider/encoding.
  * zero-alloc — allocation smells (LINQ / ToArray / Split / in-loop new[]) in Network/Assets
    hot paths (layers 02-03).
  * spec citation — a numbered-layer file that gains a magic numeric constant with no nearby
    `// spec:` citation.
  * test slice (opt-in, env MH_TEST_ON_EDIT=1) — the exact `dotnet test --filter` command for
    the core project (layers 01-04) you just edited.

Each sub-advisory keeps its own gating and its source hook's exact wording; fail-open.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import _hooklib as h

# ----------------------------------------------------------------------------- CP949

def _cp949_msg(ev):
    path = h.file_path(ev)
    if not h.is_parser_cs(path):
        return None

    # NOTE: pass the RAW added text here, not the comment/string-stripped form. The table
    # filenames the detector keys on (".txt"/".csv"/".scr") live inside C# string literals,
    # which strip_comments_strings deliberately removes — stripping would blind this check.
    raw = h.added_text(ev)
    if not raw.strip():
        return None

    if h.mentions_korean_or_txt_read(raw):
        return (
            "ℹ CP949: all Martial Heroes game text is Korean in code page 949. Register the "
            "provider once (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`) and "
            "decode with `Encoding.GetEncoding(949)` — the .NET default will mangle the bytes. "
            "Advisory only."
        )
    return None


# ------------------------------------------------------------------------ zero-alloc
_ALWAYS = [
    ("LINQ .Select/.Where/.OrderBy/.GroupBy", re.compile(r"\.(?:Select|Where|OrderBy|GroupBy|Aggregate|ToDictionary)\(")),
    (".ToArray()/.ToList()", re.compile(r"\.To(?:Array|List)\(\)")),
    ("string.Split (allocates)", re.compile(r"\.Split\(")),
]
_LINQ_USING = re.compile(r"using\s+System\.Linq\b")
_LOOP_KW = re.compile(r"\b(?:for|foreach|while)\s*\(")
_NEW_ARRAY = re.compile(r"\bnew\s+\w[\w<>,. ]*\[")          # new byte[len], new Foo[n]
_NEW_LIST = re.compile(r"\bnew\s+(?:List|Dictionary|HashSet|StringBuilder)\s*<")


def _alloc_msg(ev):
    path = h.file_path(ev)
    layer, _proj = h.layer_of(path)
    if layer not in (2, 3) or not (path or "").lower().endswith(".cs"):
        return None

    text = h.strip_comments_strings(h.added_text(ev))
    if not text.strip():
        return None

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
        return None

    return (
        "ℹ zero-alloc nudge (layer 0{}, hot path) — {}. Prefer Span<byte>/ReadOnlySpan<byte>, "
        "`stackalloc` hoisted OUT of the loop (or a pooled/reused buffer), "
        "`SequenceReader`/`BinaryPrimitives` for parsing, and no LINQ on parse/decrypt paths. "
        "Advisory only.".format(layer, "; ".join(dict.fromkeys(hits)))
    )


# -------------------------------------------------------------------- spec citation

def _spec_citation_msg(ev):
    path = h.file_path(ev)
    if not h.is_layer_cs(path):
        return None

    raw = h.added_text(ev)
    if not raw.strip():
        return None

    # uncited_magic_hits returns [] when the hunk is already `// spec:`-cited or has no magic
    # constants (benign 0x0/0x01/0xFF ignored); else the distinct genuinely-magic literals.
    hits = h.uncited_magic_hits(raw)
    if not hits:
        return None

    return (
        "ℹ clean-room: magic constant(s) without a `// spec:` citation ({}). "
        "Cite the Docs/RE spec — the IDA-derived truth and the only thing C# reads — for this "
        "constant (e.g. `// spec: Docs/RE/packets/<name>.yaml`) so the literal traces to the "
        "committed record, not to memory. Gentle nudge only — nothing was blocked.".format(
            ", ".join(hits[:8])
        )
    )


# ----------------------------------------------------------- test-after-core (opt-in)

def _is_test_path(low):
    """A path that is itself test code (so editing it should not re-suggest its own slice)."""
    return (
        "/tests/" in low
        or low.endswith(".tests.cs")
        or ".tests/" in low
        or "/test/" in low
    )


def _test_after_core_msg(ev):
    if h.tool_input(ev) is None:
        return None

    # Opt-in only.
    if os.environ.get("MH_TEST_ON_EDIT") != "1":
        return None

    path = h.file_path(ev)
    low = (path or "").replace("\\", "/").lower()
    if not low.endswith(".cs"):
        return None

    layer, proj = h.layer_of(path)
    if layer is None or layer < 1 or layer > 4:
        return None
    if _is_test_path(low):
        return None

    # proj is the project suffix, e.g. 'Network.Protocol' -> assembly MartialHeroes.Network.Protocol,
    # test class names live under MartialHeroes.Network.Protocol.Tests. A namespace filter scopes
    # the run to just that project's tests.
    filter_expr = "FullyQualifiedName~MartialHeroes.{}.Tests".format(proj)
    return (
        "ℹ MH_TEST_ON_EDIT: you edited core layer 0{} ({}). Suggested targeted run:\n"
        "  dotnet test --filter \"{}\"\n"
        "Not run automatically — kept advisory/fast.".format(layer, proj, filter_expr)
    )


# ------------------------------------------------------------------------- dispatch

def main():
    ev = h.read_event()
    if h.tool_name(ev) not in ("Write", "Edit", "MultiEdit"):
        h.ok()
        return

    msgs = []
    for fn in (_cp949_msg, _alloc_msg, _spec_citation_msg, _test_after_core_msg):
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
