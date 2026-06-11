#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — consolidated post-edit advisories (one process).

Advisory only. Handles, in order: (a) touched-file breadcrumb; (b) placeholder Class1.cs note;
(c) zero-allocation perf nudges for Network.*/Assets.* hot paths; (d) packet-struct layout nudge;
(e) slnx-sync note for new .csproj files; (f) optional owning-project build check, gated behind
env MH_BUILD_ON_EDIT=1 and debounced (off by default during the greenfield phase).
"""
import os
import re
import time
import _hooklib as h

_ALLOC = [
    ("new byte[]", re.compile(r"\bnew\s+byte\s*\[")),
    ("new T[]", re.compile(r"\bnew\s+\w+\[\]")),
    (".ToArray()/.ToList()", re.compile(r"\.To(?:Array|List)\(\)")),
    ("new List<>", re.compile(r"\bnew\s+List<")),
    ("BitConverter", re.compile(r"\bBitConverter\.(?:To\w+|GetBytes)\b")),
]
_LINQ = re.compile(r"\.(?:Select|Where|Aggregate|OrderBy|GroupBy|ToDictionary)\(")
_LINQ_USING = re.compile(r"using\s+System\.Linq\b")
_PACKET_STRUCT = re.compile(r"\bstruct\s+\w*(?:Packet|Header|Msg|Payload)\b")
_HAS_LAYOUT = re.compile(r"StructLayout")


def _owning_csproj(start_dir, pdir):
    d = os.path.abspath(start_dir)
    root = os.path.abspath(pdir)
    while True:
        try:
            for f in os.listdir(d):
                if f.lower().endswith(".csproj"):
                    return os.path.join(d, f)
        except Exception:
            pass
        parent = os.path.dirname(d)
        if parent == d or len(parent) < len(root):
            return None
        d = parent


def _build_check(csproj, pdir, advisories):
    import subprocess
    import hashlib
    marker = os.path.join(h.state_dir(pdir), "build_" + hashlib.sha1(csproj.encode("utf-8")).hexdigest()[:12] + ".txt")
    try:
        if os.path.exists(marker) and (time.time() - os.path.getmtime(marker)) < 20:
            return
    except Exception:
        pass
    try:
        with open(marker, "w") as fh:
            fh.write(str(time.time()))
    except Exception:
        pass
    try:
        out = subprocess.run(
            ["dotnet", "build", csproj, "-nologo", "-clp:NoSummary", "-v", "q", "--no-restore"],
            cwd=pdir, capture_output=True, text=True, timeout=60, errors="replace",
        )
        errs = [l.strip() for l in (out.stdout + out.stderr).splitlines() if "error " in l.lower()]
        if errs:
            advisories.append("build errors in {}:\n  {}".format(os.path.basename(csproj), "\n  ".join(errs[:15])))
    except Exception:
        pass


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    if not path:
        h.ok()
        return
    pdir = h.project_dir(ev)
    low = path.lower()
    advisories = []

    # (a) breadcrumb
    if low.endswith((".cs", ".csproj")):
        h.append_jsonl(pdir, "touched.jsonl", {"path": path, "session": ev.get("session_id", "")})

    # (e) slnx-sync for new .csproj
    if low.endswith(".csproj"):
        try:
            slnx = os.path.join(pdir, "MartialHeroes.slnx")
            base = os.path.basename(path)
            with open(slnx, "r", encoding="utf-8") as fh:
                slnx_txt = h.strip_bom(fh.read())
            if base not in slnx_txt:
                advisories.append("'{}' is not registered in MartialHeroes.slnx — add a <Project Path=\"...\"/> under the matching numbered folder and wire its ProjectReferences.".format(base))
        except Exception:
            pass

    if not low.endswith(".cs"):
        if advisories:
            h.system_message(" / ".join(advisories))
        else:
            h.ok()
        return

    # (b) placeholder note
    if os.path.basename(path) == "Class1.cs":
        advisories.append("editing the placeholder Class1.cs — delete the stub and name files after their types; this project still has no ProjectReferences wired.")

    text = h.strip_comments_strings(h.added_text(ev))
    layer, _proj = h.layer_of(path)

    # (c) perf nudges in Network.*/Assets.* (layers 2-3)
    if layer in (2, 3) and text.strip():
        hits = [label for label, rx in _ALLOC if rx.search(text)]
        if _LINQ_USING.search(text) and _LINQ.search(text):
            hits.append("LINQ on a hot path")
        if hits:
            advisories.append("zero-alloc nudge ({}): prefer Span<byte>/stackalloc/SequenceReader/BinaryPrimitives on Network/Assets hot paths.".format(", ".join(hits[:6])))

    # (d) packet-struct layout nudge
    if _PACKET_STRUCT.search(text) and not _HAS_LAYOUT.search(h.added_text(ev)):
        advisories.append("a packet/header struct should carry [StructLayout(LayoutKind.Sequential, Pack = 1)] and use [InlineArray] for fixed buffers (no managed strings on the wire).")

    # (f) optional build check
    if os.environ.get("MH_BUILD_ON_EDIT") == "1":
        csproj = _owning_csproj(os.path.dirname(path), pdir)
        if csproj:
            _build_check(csproj, pdir, advisories)

    if advisories:
        h.system_message("ℹ " + " / ".join(advisories))
    else:
        h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
