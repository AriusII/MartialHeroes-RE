#!/usr/bin/env python3
"""hexdump_annotate.py -- annotated hexdump to seed a Docs/RE/formats/<ext>.md spec.

Prints, for a sample of a legacy Martial Heroes binary asset:
  * a per-row hexdump:  offset | hex bytes | ASCII | guessed-field annotation
  * a heuristic header summary: probable magic, candidate u16/u32 header fields,
    embedded printable strings, and a guessed repeating-record stride.

The point is to help a human translate the byte layout into NEUTRAL PROSE in the
committed spec. The dump itself is copyright-tainted sample content: write it to a
gitignored quarantine path (Docs/RE/_dirty/ or /.work/), NEVER into Docs/RE/formats/.

Stdlib only (struct, argparse, os, sys, collections). No third-party dependencies.
"""

from __future__ import annotations

import argparse
import os
import struct
import sys
from collections import Counter

ENDIAN_PREFIX = {"little": "<", "big": ">"}


def _printable(b: int) -> str:
    return chr(b) if 32 <= b < 127 else "."


def looks_like_magic(data: bytes) -> str:
    """Return a description of the first 4 bytes as a probable magic, if printable."""
    head = data[:4]
    if not head:
        return "(empty)"
    ascii_part = "".join(_printable(b) for b in head)
    hex_part = head.hex(" ")
    printable_run = all(32 <= b < 127 for b in head)
    if printable_run:
        return f"ASCII {ascii_part!r} = {hex_part}  (likely a magic signature)"
    return f"{hex_part}  (ASCII {ascii_part!r}; non-printable -> binary magic or no magic)"


def candidate_ints(data: bytes, endian: str, span: int = 32) -> list[str]:
    """Interpret the first `span` bytes as u16/u32 to surface plausible header fields."""
    out: list[str] = []
    pre = ENDIAN_PREFIX[endian]
    limit = min(span, len(data))
    # u32 fields on 4-byte boundaries
    for off in range(0, limit - 3, 4):
        (v,) = struct.unpack_from(pre + "I", data, off)
        note = ""
        if 0 < v < 1_000_000:
            note = " <- small; plausible count/length/version"
        out.append(f"  0x{off:02X}  u32 = {v}{note}")
    # u16 fields on 2-byte boundaries within the first 16 bytes
    for off in range(0, min(16, len(data) - 1), 2):
        (v,) = struct.unpack_from(pre + "H", data, off)
        out.append(f"  0x{off:02X}  u16 = {v}")
    return out


def embedded_strings(data: bytes, min_len: int = 4) -> list[tuple[int, str]]:
    """Find runs of printable ASCII -- often names/paths inside asset headers."""
    found: list[tuple[int, str]] = []
    start = -1
    run: list[str] = []
    for i, b in enumerate(data):
        if 32 <= b < 127:
            if start < 0:
                start = i
            run.append(chr(b))
        else:
            if start >= 0 and len(run) >= min_len:
                found.append((start, "".join(run)))
            start = -1
            run = []
    if start >= 0 and len(run) >= min_len:
        found.append((start, "".join(run)))
    return found


def guess_record_stride(data: bytes) -> str:
    """Very rough: if a small u32 near the top equals (len-header)/k for tidy k,
    suggest a stride. Heuristic only -- the spec must confirm it."""
    n = len(data)
    candidates: Counter[int] = Counter()
    # Try common header sizes and small u32 counts read little-endian.
    for hdr in (4, 8, 12, 16, 20, 24, 32):
        if hdr + 4 > n:
            continue
        for cnt_off in range(0, min(hdr, n - 3), 4):
            (cnt,) = struct.unpack_from("<I", data, cnt_off)
            if 0 < cnt < 100_000:
                body = n - hdr
                if body > 0 and body % cnt == 0:
                    stride = body // cnt
                    if 1 <= stride <= 4096:
                        candidates[stride] += 1
    if not candidates:
        return "  (no clean (file-header)/count division found in this sample window)"
    lines = ["  candidate strides (count*stride == body for some header size):"]
    for stride, votes in candidates.most_common(5):
        lines.append(f"    stride {stride} bytes  (consistent in {votes} header/count combos)")
    lines.append("  NOTE: heuristic only -- confirm against a 2nd sample and the real count field.")
    return "\n".join(lines)


def annotate_field(off: int, row: bytes, data: bytes) -> str:
    """Lightweight per-row annotation hint."""
    notes: list[str] = []
    if off == 0:
        notes.append("header start")
    printable = sum(1 for b in row if 32 <= b < 127)
    if printable == len(row) and len(row) > 0:
        notes.append("all-ASCII (string/magic?)")
    if all(b == 0 for b in row):
        notes.append("all zero (padding/reserved?)")
    return "; ".join(notes)


def hexdump(data: bytes, base: int, width: int, do_guess: bool) -> None:
    for line_off in range(0, len(data), width):
        row = data[line_off:line_off + width]
        abs_off = base + line_off
        hex_cells = " ".join(f"{b:02x}" for b in row)
        hex_cells = hex_cells.ljust(width * 3 - 1)
        ascii_cells = "".join(_printable(b) for b in row)
        annotation = annotate_field(abs_off, row, data) if do_guess else ""
        line = f"{abs_off:08X}  {hex_cells}  |{ascii_cells}|"
        if annotation:
            line += f"  <- {annotation}"
        print(line)


def summarize(data: bytes, endian: str) -> None:
    print()
    print("# ---- heuristic header summary (guesses; confirm before writing the spec) ----")
    print(f"# magic    : {looks_like_magic(data)}")
    print(f"# endian   : {endian} (pass --endian to flip)")
    print("# candidate integer fields in the first 32 bytes:")
    for line in candidate_ints(data, endian):
        print("#" + line)
    strings = embedded_strings(data)
    if strings:
        print("# embedded printable strings (offset: text):")
        for off, s in strings[:20]:
            print(f"#   0x{off:04X}: {s!r}")
    else:
        print("# embedded printable strings: none >= 4 chars in this window")
    print("# record stride guess:")
    print(guess_record_stride(data))
    print("# ---------------------------------------------------------------------------")
    print("# Reminder: this dump is sample content -> keep it in Docs/RE/_dirty/ or /.work/.")
    print("# Translate the layout into NEUTRAL PROSE in Docs/RE/formats/<ext>.md; do NOT")
    print("# paste these rows into the committed spec.")


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="hexdump_annotate.py",
        description=(
            "Annotated hexdump (offset/hex/ASCII/guessed-field) to seed a "
            "Docs/RE/formats/<ext>.md spec. Output is sample content -> keep it gitignored."
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Example:\n"
            "  hexdump_annotate.py --file sample.msh --length 512 "
            "> Docs/RE/_dirty/scratch/msh.hexdump.txt"
        ),
    )
    p.add_argument("--file", required=True, help="path to the sample asset (gitignored original)")
    p.add_argument("--offset", type=int, default=0, help="start byte offset (default 0)")
    p.add_argument(
        "--length", type=int, default=256,
        help="number of bytes to dump (default 256; keep small -- this is header inspection)",
    )
    p.add_argument("--width", type=int, default=16, help="bytes per row (default 16)")
    p.add_argument(
        "--endian", choices=("little", "big"), default="little",
        help="byte order for integer-field guesses (default little)",
    )
    guess = p.add_mutually_exclusive_group()
    guess.add_argument(
        "--guess", dest="guess", action="store_true", default=True,
        help="show per-row annotations + header summary (default)",
    )
    guess.add_argument(
        "--no-guess", dest="guess", action="store_false",
        help="plain hexdump only, no heuristics",
    )
    return p


def main(argv: list[str]) -> int:
    args = build_parser().parse_args(argv)

    if not os.path.isfile(args.file):
        print(f"error: not a file: {args.file}", file=sys.stderr)
        return 2
    if args.offset < 0 or args.length <= 0 or args.width <= 0:
        print("error: --offset must be >= 0, --length and --width must be > 0", file=sys.stderr)
        return 2

    file_size = os.path.getsize(args.file)
    with open(args.file, "rb") as fh:
        fh.seek(args.offset)
        data = fh.read(args.length)

    print(f"# annotated hexdump: {args.file}")
    print(f"# file size: {file_size} bytes; dumping {len(data)} bytes from offset {args.offset}")
    print("# columns: OFFSET  HEX  |ASCII|  <- guessed field")
    print("#" + "-" * 70)
    hexdump(data, args.offset, args.width, args.guess)
    if args.guess:
        summarize(data, args.endian)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
