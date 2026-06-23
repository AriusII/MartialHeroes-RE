#!/usr/bin/env python
"""validate_opcodes.py -- schema-validate the clean-room opcode catalog.

Validates Docs/RE/opcodes.md: every row has the 7 required columns, opcode ids are unique hex
literals, Direction/Status/Size are in range, packet-spec links point under packets/, and --
critically -- the file contains NO IDA addresses or decompiler tokens (this catalog documents
WHAT a message is, never WHERE it lives in the binary).

Optionally cross-checks against opcodes seen in captures (--seen / --seen-file) and flags any
that are missing from the catalog so they can be added as `observed` rows.

Exit code 0 = clean, 1 = at least one error. Warnings never fail the build.

Stdlib only (argparse/re/sys/os). No pip.
"""
from __future__ import annotations

import argparse
import os
import re
import sys

REQUIRED_COLS = ["Opcode", "Name", "Direction", "Size (bytes)", "Packet spec", "Status", "Notes"]
VALID_DIR = {"C2S", "S2C"}
VALID_STATUS = {"draft", "observed", "confirmed", "implemented"}
EMPTY_LINK = {"-", "—", "", "n/a"}

# Tokens that betray decompiler/address leakage -- this file must carry none of them.
ADDRESS_PATTERNS = [
    (re.compile(r"\bsub_[0-9A-Fa-f]{4,}\b"), "IDA function label sub_xxxx"),
    (re.compile(r"\bloc_[0-9A-Fa-f]+\b"), "IDA location label loc_xxxx"),
    (re.compile(r"\b(?:dword|byte|word|qword|off|unk)_[0-9A-Fa-f]+\b"), "IDA data label"),
    (re.compile(r"\.text:[0-9A-Fa-f]+"), "segment:address reference"),
    (re.compile(r"\b0x00[0-9A-Fa-f]{6}\b"), "absolute address-shaped literal (0x00xxxxxx)"),
]

errors: list[str] = []
warnings: list[str] = []


def err(msg: str) -> None:
    errors.append(msg)


def warn(msg: str) -> None:
    warnings.append(msg)


def normalize_opcode(raw: str) -> int | None:
    """Parse an opcode cell (e.g. '0x42', '`0x42`', '_0x00_') into an int, or None."""
    s = raw.strip().strip("`").strip("_").strip()
    if not s:
        return None
    try:
        return int(s, 16) if s.lower().startswith("0x") else int(s, 0)
    except ValueError:
        return None


def split_table_row(line: str) -> list[str] | None:
    """Split a Markdown table row '| a | b |' into trimmed cells, or None if not a row."""
    if not line.lstrip().startswith("|"):
        return None
    parts = [c.strip() for c in line.strip().strip("|").split("|")]
    return parts


def is_separator_row(cells: list[str]) -> bool:
    return all(set(c) <= set("-: ") and c for c in cells)


def find_table(lines: list[str]) -> tuple[int, list[str]] | None:
    """Locate the first table whose header matches REQUIRED_COLS. Returns (header_index, header)."""
    for i, line in enumerate(lines):
        cells = split_table_row(line)
        if cells and [c for c in cells] == REQUIRED_COLS:
            return i, cells
    return None


def check_address_leakage(text: str) -> None:
    for rx, label in ADDRESS_PATTERNS:
        for m in rx.finditer(text):
            err(f"address/decompiler leakage: {label!r} -> {m.group(0)!r} "
                f"(addresses belong only in gitignored _dirty/, never in opcodes.md)")


def main() -> None:
    ap = argparse.ArgumentParser(description="Validate the clean-room opcode catalog.")
    ap.add_argument("path", help="path to Docs/RE/opcodes.md")
    ap.add_argument("--seen", default="", help="comma-separated opcodes seen in captures, e.g. 0x01,0x42")
    ap.add_argument("--seen-file", default=None, help="file with one hex opcode per line (e.g. from captures)")
    args = ap.parse_args()

    if not os.path.isfile(args.path):
        sys.exit(f"error: not found: {args.path}")

    with open(args.path, encoding="utf-8") as fh:
        text = fh.read()
    lines = text.splitlines()
    base_dir = os.path.dirname(os.path.abspath(args.path))

    # 1. Address leakage scan over the whole file.
    check_address_leakage(text)

    # 2. Locate the catalog table.
    found = find_table(lines)
    if found is None:
        err(f"no table with the required header found. Expected columns: {REQUIRED_COLS}")
        report(); return
    header_idx, _ = found

    # 3. Walk data rows.
    seen_ids: dict[int, int] = {}      # opcode int -> first line number
    catalog_ids: set[int] = set()
    row_no = header_idx + 1
    # Skip the separator row.
    if row_no < len(lines):
        sep = split_table_row(lines[row_no])
        if sep and is_separator_row(sep):
            row_no += 1

    for i in range(row_no, len(lines)):
        cells = split_table_row(lines[i])
        if cells is None:
            break  # table ended
        if len(cells) != len(REQUIRED_COLS):
            err(f"line {i + 1}: row has {len(cells)} columns, expected {len(REQUIRED_COLS)}: {cells}")
            continue

        op_raw, name, direction, size, link, status, _notes = cells
        op_clean = op_raw.strip().strip("`").strip("_").strip()

        # Skip the placeholder example row shipped in the template.
        if name.strip("_").strip().lower() == "example":
            continue

        opcode = normalize_opcode(op_raw)
        if opcode is None:
            err(f"line {i + 1}: opcode {op_raw!r} is not a hex literal (use 0xNN)")
        else:
            if not op_clean.lower().startswith("0x"):
                warn(f"line {i + 1}: opcode {op_clean!r} should use lowercase 0x form (0x{opcode:02x})")
            if opcode in seen_ids:
                err(f"line {i + 1}: DUPLICATE opcode 0x{opcode:02x} (first defined at line {seen_ids[opcode]})")
            else:
                seen_ids[opcode] = i + 1
                catalog_ids.add(opcode)

        if direction not in VALID_DIR:
            err(f"line {i + 1}: Direction {direction!r} not in {sorted(VALID_DIR)}")
        if status not in VALID_STATUS:
            err(f"line {i + 1}: Status {status!r} not in {sorted(VALID_STATUS)}")

        size_clean = size.strip().strip("`")
        if size_clean.lower() != "var" and not size_clean.isdigit():
            err(f"line {i + 1}: Size {size!r} must be an integer or 'var'")

        link_clean = link.strip().strip("`")
        if link_clean.lower() not in EMPTY_LINK:
            # Accept a bare path or a [text](path) markdown link.
            m = re.search(r"\(([^)]+)\)", link_clean)
            target = m.group(1) if m else link_clean
            if not target.startswith("packets/"):
                err(f"line {i + 1}: Packet spec {link_clean!r} must point under packets/ (or be '—')")
            else:
                full = os.path.join(base_dir, target)
                if not os.path.exists(full):
                    warn(f"line {i + 1}: Packet spec {target!r} does not exist yet")

    # 4. Cross-check opcodes seen in captures.
    seen_input: list[str] = []
    if args.seen:
        seen_input += [s for s in args.seen.split(",") if s.strip()]
    if args.seen_file:
        if os.path.isfile(args.seen_file):
            with open(args.seen_file, encoding="utf-8") as fh:
                seen_input += [ln.strip() for ln in fh if ln.strip() and not ln.startswith("#")]
        else:
            warn(f"--seen-file not found: {args.seen_file}")

    for s in seen_input:
        op = normalize_opcode(s)
        if op is None:
            warn(f"--seen value {s!r} is not a hex opcode; skipped")
        elif op not in catalog_ids:
            warn(f"opcode 0x{op:02x} seen in captures but MISSING from the catalog "
                 f"-- add it as an `observed` row")

    print(f"checked {len(catalog_ids)} opcode rows.")
    report()


def report() -> None:
    for w in warnings:
        print(f"  warn: {w}")
    for e in errors:
        print(f"  ERROR: {e}")
    if errors:
        print(f"\nFAILED: {len(errors)} error(s), {len(warnings)} warning(s).")
        sys.exit(1)
    print(f"\nOK: catalog valid ({len(warnings)} warning(s)).")
    sys.exit(0)


if __name__ == "__main__":
    main()
