#!/usr/bin/env python3
"""Find magic numeric constants in MartialHeroes layer C# that lack a // spec: citation.

Project rule: every magic numeric constant in C# (byte offset, record size, opcode,
hex constant) must cite its source spec with `// spec: Docs/RE/...` on the same line
or within a few lines above. This scanner reports the uncited ones as file:line.

It is narrower than the clean-room-audit leak scanner: the only question here is
"does this magic number cite a spec?". Stdlib only. Read-only. Exit code is always 0
(this is a report, not a CI gate).

Design goal: LOW false positives. Tiny structural constants (0,1,-1,2,...), literals in
comments/strings, enum members, test [InlineData] attributes, and allocation sizes are
ignored. A literal passes if a `// spec: Docs/RE/...` citation sits on its line or within
the lookback window above it (so a block of offsets under one header passes).

Usage:
    python citation_scan.py --root .
    python citation_scan.py --root 02.Network.Layer --format json
    python citation_scan.py --min-decimal 32 --lookback 5
"""

import argparse
import json
import os
import re
import sys

# Citation that satisfies a magic constant.
SPEC_CITATION = re.compile(r"//\s*spec:\s*Docs/RE/", re.IGNORECASE)

# Hex literal of >= 2 digits: 0x90, 0x2C, 0x1F4 — classic offsets/sizes/opcodes.
HEX_LITERAL = re.compile(r"\b0x[0-9A-Fa-f]{2,}\b")

# Numeric indexer / slice / pointer-arithmetic offset shapes.
INDEXER = re.compile(
    r"\[\s*(?:0x[0-9A-Fa-f]+|\d{2,})\s*\]"          # [40] or [0x14]
    r"|\bSlice\s*\(\s*\d+"                            # Slice(112 ...
    r"|\bSlice\s*\(\s*0x[0-9A-Fa-f]+"                # Slice(0x70 ...
    r"|[+\-]\s*0x[0-9A-Fa-f]+"                       # + 0x18
    r"|\bReadInt\d+LittleEndian\b"                    # endian reads usually carry an offset
)

# A bare decimal >= threshold standing as a literal (record size etc.).
BARE_DECIMAL = re.compile(r"(?<![\w.])(\d{2,})(?![\w.])")

# Lines we never treat as offset code (cut false positives).
ATTR_LINE = re.compile(r"^\s*\[\s*(?:Theory|InlineData|Fact|MemberData|ClassData)\b")
ENUM_MEMBER = re.compile(r"^\s*[A-Za-z_]\w*\s*=\s*-?\d+\s*,?\s*(?://.*)?$")
ALLOC_SIZE = re.compile(r"new\s+\w+\s*\[\s*\d+\s*\]")  # new byte[256] — size, not offset
VERSION_STR = re.compile(r"""Version\s*=\s*["']""")

SKIP_DIR_NAMES = {"obj", "bin", ".git", ".vs", ".idea", ".godot", "node_modules"}
SKIP_FILE_SUFFIXES = (".g.cs", ".designer.cs", ".generated.cs", ".assemblyinfo.cs",
                      ".globalusings.g.cs")
SKIP_PATH_FRAGMENTS = ("/Docs/RE/_dirty/", "\\Docs\\RE\\_dirty\\")


def is_skippable_file(path: str) -> bool:
    low = path.lower()
    if low.endswith(SKIP_FILE_SUFFIXES):
        return True
    norm = path.replace("\\", "/")
    return any(frag.replace("\\", "/") in norm for frag in SKIP_PATH_FRAGMENTS)


def iter_cs_files(root: str):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIR_NAMES]
        if "_dirty" in dirpath.replace("\\", "/").split("/"):
            continue
        for name in filenames:
            if name.lower().endswith(".cs"):
                full = os.path.join(dirpath, name)
                if not is_skippable_file(full):
                    yield full


def strip_comment_and_strings(line: str) -> str:
    """Return the code portion of a line, with // comment and string/char literals blanked.

    Crude but effective: removes a trailing // comment (not inside a string) and replaces
    "..." / '...' / @"..." spans with spaces so literals inside text are not flagged.
    """
    out = []
    i, n = 0, len(line)
    in_str = None  # '"' or "'" while inside a string/char literal
    while i < n:
        c = line[i]
        if in_str:
            out.append(" ")
            if c == "\\" and i + 1 < n:  # skip escaped char
                out.append(" ")
                i += 2
                continue
            if c == in_str:
                in_str = None
            i += 1
            continue
        if c == "/" and i + 1 < n and line[i + 1] == "/":
            break  # rest is a line comment
        if c in ('"', "'"):
            in_str = c
            out.append(" ")
            i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def is_cited(lines: list[str], idx: int, lookback: int) -> bool:
    start = max(0, idx - lookback)
    for j in range(start, idx + 1):
        if SPEC_CITATION.search(lines[j]):
            return True
    return False


def line_has_magic(code: str, min_decimal: int) -> str | None:
    """Return a short label of the magic literal found in `code`, or None."""
    m = HEX_LITERAL.search(code)
    if m:
        return m.group(0)
    m = INDEXER.search(code)
    if m:
        return m.group(0).strip()
    # Bare decimal >= threshold, but not an allocation size or enum value.
    if ALLOC_SIZE.search(code):
        return None
    for m in BARE_DECIMAL.finditer(code):
        if int(m.group(1)) >= min_decimal:
            return m.group(1)
    return None


def scan_file(path: str, min_decimal: int, lookback: int) -> list[dict]:
    findings: list[dict] = []
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            lines = fh.read().splitlines()
    except OSError as exc:
        return [{"file": path, "line": 0, "literal": "", "text": str(exc)}]

    for i, raw in enumerate(lines):
        if ATTR_LINE.match(raw) or ENUM_MEMBER.match(raw) or VERSION_STR.search(raw):
            continue
        code = strip_comment_and_strings(raw)
        if not code.strip():
            continue
        literal = line_has_magic(code, min_decimal)
        if literal is None:
            continue
        if is_cited(lines, i, lookback):
            continue
        findings.append({"file": path, "line": i + 1, "literal": literal,
                         "text": raw.strip()[:160]})
    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Find uncited magic numeric constants in layer C# (read-only).")
    parser.add_argument("--root", default=".", help="root directory to scan (default: cwd)")
    parser.add_argument("--format", choices=("text", "json"), default="text")
    parser.add_argument("--min-decimal", type=int, default=16,
                        help="flag bare decimals >= this value (default 16)")
    parser.add_argument("--lookback", type=int, default=3,
                        help="lines above a literal a // spec: may sit (default 3)")
    args = parser.parse_args(argv)

    all_findings: list[dict] = []
    scanned = 0
    for path in iter_cs_files(args.root):
        scanned += 1
        all_findings.extend(scan_file(path, args.min_decimal, args.lookback))

    all_findings.sort(key=lambda f: (f["file"], f["line"]))
    files_with = len({f["file"] for f in all_findings})

    if args.format == "json":
        print(json.dumps({"scanned_files": scanned, "uncited": len(all_findings),
                          "files_with_findings": files_with,
                          "findings": all_findings}, indent=2))
        return 0

    print(f"spec-citation-audit: scanned {scanned} .cs file(s) — "
          f"{len(all_findings)} uncited magic constant(s) across {files_with} file(s)")
    if not all_findings:
        print("Every magic constant cites a spec. (Citations not verified for correctness.)")
        return 0
    print("")
    for f in all_findings:
        rel = os.path.relpath(f["file"], args.root)
        print(f"{rel}:{f['line']} — {f['literal']} — uncited magic constant")
        if f["text"]:
            print(f"    {f['text']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
