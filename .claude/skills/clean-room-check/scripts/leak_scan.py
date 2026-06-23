#!/usr/bin/env python3
"""Scan the Martial Heroes C# tree for clean-room leakage smells.

Stdlib only. Read-only: never edits a file. Walks **/*.cs (skipping build output
and generated files) and flags patterns that indicate decompiler output leaked
into the reimplemented client, plus magic byte-offset literals that lack a
nearby `// spec: Docs/RE/...` citation.

Severity:
    high    decompiler autonames, MSVC pseudo-types, mangled symbols
    medium  decompiler default arg names (a1/v12), uncited offset literals

Exit code is always 0 (this is a report, not a gate; use the firewall-check
skill/script for CI failure semantics).

Usage:
    python leak_scan.py --root .
    python leak_scan.py --root 02.Network.Layer --format json
"""

import argparse
import json
import os
import re
import sys

# --- High-severity: pasted decompiler / MSVC artifacts -----------------------

HIGH_PATTERNS = [
    ("hexrays-autoname",
     re.compile(r"\b(?:sub|loc|locret|byte|word|dword|qword|unk|off|flt|dbl|stru|jpt|asc|nullsub)_[0-9A-Fa-f]{3,}\b")),
    ("msvc-pseudo-type",
     re.compile(r"\b(?:_BYTE|_WORD|_DWORD|_QWORD|__int8|__int16|__int32|__int64|"
                r"LODWORD|HIDWORD|LOBYTE|HIBYTE|BYTE1|BYTE2|WORD1|SLODWORD|SHIDWORD)\b")),
    ("msvc-callconv",
     re.compile(r"\b(?:__thiscall|__fastcall|__cdecl|__stdcall|__usercall|__userpurge)\b")),
    ("mangled-symbol",
     re.compile(r"\?[\w@?$]+@@[\w$]+|@@(?:QAE|YA|UAE|IAE|MAE)[\w$]*")),
]

# --- Medium-severity: decompiler default variable names ----------------------
# Hex-Rays emits a1,a2,... for args and v1,v2,... for locals. Match those tokens
# used as identifiers (declaration / assignment / call sites), not inside words.
MEDIUM_VARNAME = re.compile(
    r"(?<![\w.])(?:a|v)\d{1,3}\b"
    r"(?=\s*(?:[=;,)\].]|==|!=|\+\+|--|\)|<|>|\+|-|\*|/))")

# --- Magic offset literals (escalated only when uncited) ---------------------
# Hex/decimal literals that look like byte offsets in protocol/asset code:
# array index [0x2C], Slice(40, 4), span[i + 0x18], = packet[12], etc.
OFFSET_LITERAL = re.compile(
    r"(?:\[\s*0x[0-9A-Fa-f]+\s*\]"          # [0x2C]
    r"|\[\s*\d{2,}\s*\]"                      # [40]
    r"|\bSlice\s*\(\s*\d+"                    # Slice(40 ...
    r"|\bSlice\s*\(\s*0x[0-9A-Fa-f]+"        # Slice(0x18 ...
    r"|[+\-]\s*0x[0-9A-Fa-f]+"               # + 0x18
    r"|\bGetOffset\b|\bReadAt\s*\(\s*\d+)")
SPEC_CITATION = re.compile(r"//\s*spec:\s*Docs/RE/", re.IGNORECASE)
# Lines that are obviously not protocol offsets — skip to cut false positives.
OFFSET_IGNORE = re.compile(r"^\s*(?://|/\*|\*)")

SKIP_DIR_NAMES = {"obj", "bin", ".git", ".vs", ".idea", ".godot", "node_modules"}
SKIP_FILE_SUFFIXES = (".g.cs", ".designer.cs", ".generated.cs", ".assemblyinfo.cs")
# Never scan the dirty quarantine even if a stray .cs lands there.
SKIP_PATH_FRAGMENTS = (os.path.join("Docs", "RE", "_dirty"), "/Docs/RE/_dirty/", "\\Docs\\RE\\_dirty\\")


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


def offset_is_cited(lines: list[str], idx: int, lookback: int = 3) -> bool:
    """True if a `// spec: Docs/RE/...` citation sits on this line or just above."""
    start = max(0, idx - lookback)
    for j in range(start, idx + 1):
        if SPEC_CITATION.search(lines[j]):
            return True
    return False


def scan_file(path: str) -> list[dict]:
    findings: list[dict] = []
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            lines = fh.read().splitlines()
    except OSError as exc:
        return [{"file": path, "line": 0, "smell": "read-error",
                 "severity": "info", "text": str(exc)}]

    for i, line in enumerate(lines):
        for label, pat in HIGH_PATTERNS:
            if pat.search(line):
                findings.append({"file": path, "line": i + 1, "smell": label,
                                 "severity": "high", "text": line.strip()[:160]})
        if MEDIUM_VARNAME.search(line):
            findings.append({"file": path, "line": i + 1, "smell": "decompiler-varname",
                             "severity": "medium", "text": line.strip()[:160]})
        if (not OFFSET_IGNORE.match(line)
                and OFFSET_LITERAL.search(line)
                and not offset_is_cited(lines, i)):
            findings.append({"file": path, "line": i + 1, "smell": "uncited-offset",
                             "severity": "medium", "text": line.strip()[:160]})
    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Clean-room leakage scanner (read-only).")
    parser.add_argument("--root", default=".", help="root directory to scan (default: cwd)")
    parser.add_argument("--format", choices=("text", "json"), default="text")
    args = parser.parse_args(argv)

    all_findings: list[dict] = []
    scanned = 0
    for path in iter_cs_files(args.root):
        scanned += 1
        all_findings.extend(scan_file(path))

    order = {"high": 0, "medium": 1, "info": 2}
    all_findings.sort(key=lambda f: (order.get(f["severity"], 9), f["file"], f["line"]))
    highs = sum(1 for f in all_findings if f["severity"] == "high")
    meds = sum(1 for f in all_findings if f["severity"] == "medium")

    if args.format == "json":
        print(json.dumps({"scanned_files": scanned, "high": highs, "medium": meds,
                          "findings": all_findings}, indent=2))
        return 0

    print(f"clean-room-audit: scanned {scanned} .cs file(s) — {highs} high, {meds} medium")
    if not all_findings:
        print("No leakage smells found. (Absence of smells is not proof of a clean room.)")
        return 0
    print("")
    for f in all_findings:
        rel = os.path.relpath(f["file"], args.root)
        print(f"[{f['severity'].upper():6}] {rel}:{f['line']} — {f['smell']}")
        if f["text"]:
            print(f"            {f['text']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
