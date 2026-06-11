#!/usr/bin/env python
"""byte_diff.py -- differential byte analysis of captured Martial Heroes packets.

Given two or more messages that share an opcode but differ in one observable game variable,
align them and diff byte-column by byte-column to localize which offsets carry that variable.
Emits an offset->hypothesis table you paste under `notes:` in a Docs/RE/packets/*.yaml spec.

The bytes come from the capture ORACLE (observed wire bytes), so this is clean-room-safe: it
never touches IDA, never reads decompiler output, and never asserts a meaning the diff cannot
support -- width/endianness are labeled hypotheses for a human to confirm.

Stdlib only (argparse/csv/sys). No pip.

Examples:
  python byte_diff.py --hex aa010002 --hex aa020003
  python byte_diff.py --tsv stream_3.tsv --opcode 0x42 --limit 8
"""
from __future__ import annotations

import argparse
import csv
import sys


def parse_hex(s: str) -> bytes:
    """Accept loose hex: spaces, colons, 0x prefixes, mixed case."""
    s = s.strip().lower().replace("0x", "").replace(":", "").replace(" ", "").replace("\t", "")
    if len(s) % 2 != 0:
        sys.exit(f"error: hex string has odd length: {s!r}")
    try:
        return bytes.fromhex(s)
    except ValueError as exc:
        sys.exit(f"error: not valid hex ({exc}): {s!r}")


def load_from_tsv(path: str, col: str, opcode: int | None,
                  opcode_offset: int, limit: int | None) -> list[bytes]:
    """Pull payload hex from a pcap-extract .tsv, optionally filtered to one opcode."""
    out: list[bytes] = []
    with open(path, newline="", encoding="utf-8") as fh:
        reader = csv.DictReader(fh, delimiter="\t")
        if reader.fieldnames is None or col not in reader.fieldnames:
            sys.exit(f"error: column {col!r} not in {path} (have: {reader.fieldnames})")
        for row in reader:
            raw = (row.get(col) or "").strip()
            if not raw:
                continue
            try:
                payload = parse_hex(raw)
            except SystemExit:
                continue
            if opcode is not None:
                if opcode_offset >= len(payload) or payload[opcode_offset] != opcode:
                    continue
            out.append(payload)
            if limit is not None and len(out) >= limit:
                break
    return out


def column_view(samples: list[bytes]) -> list[dict]:
    """Build a per-offset record across all samples."""
    width = max(len(s) for s in samples)
    cols = []
    for off in range(width):
        present = [s[off] for s in samples if off < len(s)]
        all_present = len(present) == len(samples)
        distinct = set(present)
        if not all_present:
            marker = "."          # length-variant region
        elif len(distinct) == 1:
            marker = "="          # constant
        else:
            marker = "*"          # varies
        cols.append({
            "offset": off,
            "values": [s[off] if off < len(s) else None for s in samples],
            "present": present,
            "all_present": all_present,
            "distinct": distinct,
            "marker": marker,
        })
    return cols


def group_fields(cols: list[dict]) -> list[dict]:
    """Group contiguous non-constant offsets into candidate fields."""
    fields: list[dict] = []
    run: list[dict] = []

    def flush():
        if run:
            fields.append({
                "start": run[0]["offset"],
                "end": run[-1]["offset"],
                "cols": list(run),
            })
            run.clear()

    for c in cols:
        if c["marker"] in ("*", "."):
            run.append(c)
        else:
            flush()
    flush()
    return fields


def le_value(samples: list[bytes], start: int, width: int) -> list[int | None]:
    """Read a little-endian unsigned int of `width` bytes at `start` from each sample."""
    out: list[int | None] = []
    for s in samples:
        if start + width <= len(s):
            out.append(int.from_bytes(s[start:start + width], "little"))
        else:
            out.append(None)
    return out


def classify(field: dict, samples: list[bytes]) -> str:
    """Heuristic, clearly-labeled hypothesis for a candidate field."""
    start = field["start"]
    span = field["end"] - field["start"] + 1
    has_missing = any(c["marker"] == "." for c in field["cols"])
    if has_missing:
        return "length-variant tail (string / variable array?) -> size: var"

    # Pick the smallest natural width that covers the span (1,2,4) for the LE reading.
    width = 1 if span == 1 else 2 if span <= 2 else 4 if span <= 4 else span
    vals = [v for v in le_value(samples, start, width) if v is not None]
    if len(vals) >= 2:
        diffs = [b - a for a, b in zip(vals, vals[1:])]
        if all(d == diffs[0] for d in diffs) and diffs[0] != 0:
            return f"le u{width * 8}, monotonic step {diffs[0]} -> sequence/counter"
        small = all(0 <= v < 256 for v in vals)
        if width == 1 and small and len(set(vals)) <= 4:
            return "u8, few distinct values -> small enum / flags?"
    if width >= 4:
        return f"candidate value ({width}B) -- confirm endianness (coordinate/id/timestamp?)"
    return f"le u{width * 8} candidate -- confirm semantics with more samples"


def fmt_bytes(values: list[int | None]) -> str:
    return " ".join("--" if v is None else f"{v:02x}" for v in values)


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Diff captured packets of one opcode to localize field byte offsets."
    )
    ap.add_argument("--hex", action="append", default=[], help="a raw payload hex string (repeatable)")
    ap.add_argument("--tsv", default=None, help="pcap-extract .tsv to pull payloads from")
    ap.add_argument("--col", default="data.data", help="tsv column holding payload hex")
    ap.add_argument("--opcode", default=None, help="keep only tsv rows whose opcode byte matches (e.g. 0x42)")
    ap.add_argument("--opcode-offset", type=int, default=0, help="byte offset of the opcode (default 0)")
    ap.add_argument("--limit", type=int, default=None, help="max packets to compare")
    args = ap.parse_args()

    samples: list[bytes] = [parse_hex(h) for h in args.hex]
    if args.tsv:
        opcode = int(args.opcode, 0) if args.opcode is not None else None
        samples += load_from_tsv(args.tsv, args.col, opcode, args.opcode_offset, args.limit)

    if len(samples) < 2:
        sys.exit("error: need at least 2 packets to diff. Provide more --hex or a richer --tsv filter.")
    if args.limit is not None:
        samples = samples[: args.limit]

    n = len(samples)
    lengths = sorted({len(s) for s in samples})
    print(f"comparing {n} packets; lengths seen: {lengths}\n")

    cols = column_view(samples)

    # Per-offset column dump.
    print("off    " + "  ".join(f"#{i}" for i in range(n)) + "   mark")
    for c in cols:
        vals = fmt_bytes(c["values"])
        print(f"0x{c['offset']:02x}   {vals}   {c['marker']}")

    n_const = sum(1 for c in cols if c["marker"] == "=")
    n_var = sum(1 for c in cols if c["marker"] == "*")
    n_len = sum(1 for c in cols if c["marker"] == ".")
    print(f"\nlegend: = constant({n_const})  * varies({n_var})  . length-variant({n_len})")

    fields = group_fields(cols)
    print("\noffset -> hypothesis (paste under `notes:` in Docs/RE/packets/<name>.yaml):\n")
    print("offset  width  bytes(per sample)            hypothesis")
    # Always surface offset 0 as the opcode anchor if it is constant.
    if cols and cols[0]["marker"] == "=":
        op = cols[0]["present"][0]
        print(f"0x00    1      {fmt_bytes(cols[0]['values']):<28} opcode (constant) = 0x{op:02x}")
    for f in fields:
        span = f["end"] - f["start"] + 1
        width = 1 if span == 1 else 2 if span <= 2 else 4 if span <= 4 else span
        sample_bytes = fmt_bytes([s[f["start"]] if f["start"] < len(s) else None for s in samples])
        print(f"0x{f['start']:02x}    {width:<6} {sample_bytes:<28} {classify(f, samples)}")

    if not fields:
        print("(no varying offsets -- your samples are identical in payload; vary the game "
              "variable more and re-capture.)")


if __name__ == "__main__":
    main()
