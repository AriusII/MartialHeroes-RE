#!/usr/bin/env python3
"""pak_index.py -- list the directory/index of a Martial Heroes .pak archive.

LISTING TOOL ONLY. This script reads the small index/directory region of a .pak
archive and prints, per logical entry, ONLY its name, payload offset, and payload
size. It NEVER seeks to a payload region or reads payload bytes -- by design there
is no extract mode. The .pak is a user-supplied, copyright-tainted original; the
payload stays inside it.

The exact index layout (magic, endianness, where the directory lives, per-entry
record fields) is defined by the committed spec Docs/RE/formats/pak.md and must be
passed in via flags / a layout preset. This script hardcodes no reverse-engineered
offsets of its own.

Stdlib only (struct, argparse, os, sys). No third-party dependencies.

Output: one line per entry to stdout:  <index>  <offset>  <size>  <name>
Redirect to a gitignored scratch file (e.g. Docs/RE/_dirty/scratch/) to save it.
"""

from __future__ import annotations

import argparse
import os
import struct
import sys

# Built-in layout presets. These are *templates* for the per-entry directory
# record, NOT authoritative reverse-engineered values -- the real field sizes
# come from Docs/RE/formats/pak.md and may be supplied with --layout to override.
# A preset/layout is an ordered list of (field, kind, size) where:
#   field in {name, offset, size, flags, crc, pad}
#   kind  in {u, str, skip}   (u = unsigned int of `size` bytes; str = fixed
#                              char[size] name field; skip = ignore `size` bytes)
LAYOUT_PRESETS: dict[str, list[tuple[str, str, int]]] = {
    # Fixed 64-byte name, then u32 offset, then u32 size. A common .pak shape;
    # confirm against Docs/RE/formats/pak.md before trusting it.
    "name64-off32-size32": [
        ("name", "str", 64),
        ("offset", "u", 4),
        ("size", "u", 4),
    ],
    # Fixed 32-byte name, u32 offset, u32 size, u32 flags.
    "name32-off32-size32-flags32": [
        ("name", "str", 32),
        ("offset", "u", 4),
        ("size", "u", 4),
        ("flags", "u", 4),
    ],
    # Fixed 128-byte name, u32 offset, u32 size, u32 crc.
    "name128-off32-size32-crc32": [
        ("name", "str", 128),
        ("offset", "u", 4),
        ("size", "u", 4),
        ("crc", "u", 4),
    ],
}


def _eprint(*args: object) -> None:
    print(*args, file=sys.stderr)


def parse_layout(spec: str) -> list[tuple[str, str, int]]:
    """Parse a --layout string like 'name=64,offset=4,size=4,flags=4'.

    Field 'name' is treated as a fixed-length char field (str). 'offset' and
    'size' are unsigned integers. 'flags'/'crc' are unsigned integers (printed
    only if you extend the output). 'pad'/'skip' bytes are ignored.
    """
    fields: list[tuple[str, str, int]] = []
    for token in spec.split(","):
        token = token.strip()
        if not token:
            continue
        if "=" not in token:
            raise ValueError(f"bad layout field {token!r}; expected name=size")
        key, _, raw = token.partition("=")
        key = key.strip().lower()
        try:
            n = int(raw.strip(), 0)
        except ValueError as exc:
            raise ValueError(f"bad byte count in layout field {token!r}") from exc
        if n <= 0:
            raise ValueError(f"layout field {token!r} must have a positive size")
        if key == "name":
            fields.append(("name", "str", n))
        elif key in ("offset", "size", "flags", "crc"):
            if n not in (1, 2, 4, 8):
                raise ValueError(f"integer field {key} size must be 1/2/4/8, got {n}")
            fields.append((key, "u", n))
        elif key in ("pad", "skip"):
            fields.append(("pad", "skip", n))
        else:
            raise ValueError(f"unknown layout field {key!r}")
    return fields


def resolve_layout(args: argparse.Namespace) -> list[tuple[str, str, int]]:
    if args.layout:
        return parse_layout(args.layout)
    if args.preset:
        if args.preset not in LAYOUT_PRESETS:
            raise ValueError(
                f"unknown preset {args.preset!r}; choose from {', '.join(LAYOUT_PRESETS)}"
            )
        return LAYOUT_PRESETS[args.preset]
    raise ValueError(
        "no entry layout given: pass --layout '<spec>' (from Docs/RE/formats/pak.md) "
        f"or --preset <{'|'.join(LAYOUT_PRESETS)}>"
    )


def record_size(layout: list[tuple[str, str, int]]) -> int:
    return sum(size for _, _, size in layout)


def magic_to_bytes(magic: str) -> bytes:
    """Accept either an ASCII signature ('PACK') or hex ('0x504B' / '50 4B')."""
    s = magic.strip()
    low = s.lower()
    if low.startswith("0x"):
        low = low[2:]
    hexish = low.replace(" ", "").replace("-", "").replace(":", "")
    is_hex = len(hexish) % 2 == 0 and hexish and all(c in "0123456789abcdef" for c in hexish)
    # Prefer literal ASCII when the value is printable and not obviously hex pairs
    if is_hex and not s.isprintable():
        return bytes.fromhex(hexish)
    if is_hex and s.lower().startswith("0x"):
        return bytes.fromhex(hexish)
    return s.encode("latin-1")


def read_uint(buf: bytes, off: int, size: int, endian: str) -> int:
    fmt = {1: "B", 2: "H", 4: "I", 8: "Q"}[size]
    prefix = "<" if endian == "little" else ">"
    return struct.unpack_from(prefix + fmt, buf, off)[0]


def decode_name(raw: bytes) -> str:
    """Decode a fixed-length name field: cut at first NUL, latin-1 decode."""
    nul = raw.find(b"\x00")
    if nul >= 0:
        raw = raw[:nul]
    return raw.decode("latin-1", errors="replace").strip()


def probe(path: str, peek: int = 16) -> int:
    """Report only the file size and first few header bytes. No payload reads."""
    size = os.path.getsize(path)
    with open(path, "rb") as fh:
        head = fh.read(peek)
    printable = "".join(chr(b) if 32 <= b < 127 else "." for b in head)
    _eprint(f"# probe: {path}")
    _eprint(f"#   file size : {size} bytes")
    _eprint(f"#   first {len(head):>2}  : {head.hex(' ')}")
    _eprint(f"#   ascii     : {printable}")
    _eprint("# (probe reads only the header; no payload. Document this in Docs/RE/formats/pak.md.)")
    return 0


def list_index(args: argparse.Namespace) -> int:
    path = args.pak
    if not os.path.isfile(path):
        _eprint(f"error: not a file: {path}")
        return 2

    file_size = os.path.getsize(path)
    layout = resolve_layout(args)
    rec_sz = record_size(layout)
    if rec_sz <= 0:
        _eprint("error: resolved entry record size is zero")
        return 2

    name_fields = [f for f in layout if f[0] == "name"]
    if len(name_fields) != 1:
        _eprint("error: layout must contain exactly one 'name' field")
        return 2

    magic = magic_to_bytes(args.magic) if args.magic else b""

    with open(path, "rb") as fh:
        # --- locate and validate the magic / header ---
        if args.index == "header":
            header = fh.read(max(len(magic), 4))
            if magic and not header.startswith(magic):
                _eprint(
                    f"error: magic mismatch: file starts {header[:len(magic)].hex(' ')}, "
                    f"spec expects {magic.hex(' ')}"
                )
                return 3
            # Directory base: after magic + the count field (--count-size bytes),
            # plus any explicit --dir-offset override from the spec.
            fh.seek(0)
            preamble = fh.read(len(magic) + args.count_size)
            if args.count is not None:
                count = args.count
            else:
                count = read_uint(
                    preamble, len(magic), args.count_size, args.endian
                ) if args.count_size else 0
            dir_base = args.dir_offset if args.dir_offset is not None else len(magic) + args.count_size
        else:  # footer index
            # Footer layouts store the directory at the end. The spec must give
            # either an explicit --dir-offset, or a --footer-size trailer that
            # contains the directory offset/count. We support an explicit count.
            if args.dir_offset is None:
                _eprint(
                    "error: --index footer requires --dir-offset <N> (the directory's "
                    "absolute byte offset from Docs/RE/formats/pak.md)"
                )
                return 2
            dir_base = args.dir_offset
            if args.count is None:
                # Derive count from the remaining bytes up to file end.
                remaining = file_size - dir_base
                if remaining < 0 or remaining % rec_sz != 0:
                    _eprint(
                        f"error: cannot derive entry count: {remaining} trailing bytes "
                        f"are not a whole multiple of the {rec_sz}-byte record. "
                        f"Pass --count <N> from the spec."
                    )
                    return 3
                count = remaining // rec_sz
            else:
                count = args.count

        if count < 0:
            _eprint(f"error: negative entry count ({count})")
            return 3
        if count > args.max_entries:
            _eprint(
                f"error: directory claims {count} entries (> --max-entries {args.max_entries}); "
                f"refusing as likely corrupt / wrong layout. Re-check the spec or raise the cap."
            )
            return 3

        dir_bytes_needed = count * rec_sz
        if dir_base + dir_bytes_needed > file_size:
            _eprint(
                f"error: directory ({count} x {rec_sz} = {dir_bytes_needed} bytes at offset "
                f"{dir_base}) runs past end of file ({file_size}). Wrong layout or count."
            )
            return 3

        # --- walk the directory, reading ONLY index bytes ---
        fh.seek(dir_base)
        dir_blob = fh.read(dir_bytes_needed)

    print(f"# pak index: {path}")
    print(f"# file size : {file_size} bytes")
    print(f"# entries   : {count}  (record {rec_sz} bytes, dir at offset {dir_base})")
    print("# columns   : index  offset  size  name")
    print("#" + "-" * 60)

    prev_end = 0
    monotonic = True
    total_payload = 0
    out_of_range = 0

    for i in range(count):
        base = i * rec_sz
        name = ""
        off = 0
        sz = 0
        pos = base
        for field, kind, fsize in layout:
            chunk = dir_blob[pos:pos + fsize]
            if kind == "str":
                name = decode_name(chunk)
            elif kind == "u":
                val = read_uint(dir_blob, pos, fsize, args.endian)
                if field == "offset":
                    off = val
                elif field == "size":
                    sz = val
            # 'skip'/other fields: index metadata we intentionally do not emit
            pos += fsize

        # Cheap integrity checks -- still no payload reads.
        if off < prev_end:
            monotonic = False
        prev_end = off + sz
        total_payload += sz
        if off + sz > file_size or off < 0:
            out_of_range += 1

        print(f"{i:<6} {off:<12} {sz:<12} {name}")

    print("#" + "-" * 60)
    print(f"# total payload covered : {total_payload} bytes across {count} entries")
    print(f"# offsets monotonic     : {'yes' if monotonic else 'NO (interleaved/unsorted)'}")
    if out_of_range:
        print(f"# WARNING: {out_of_range} entr{'y' if out_of_range == 1 else 'ies'} "
              f"point outside the file -- layout/endianness likely wrong")
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="pak_index.py",
        description=(
            "List a Martial Heroes .pak directory (name/offset/size only). "
            "LISTING ONLY -- never reads or extracts payload bytes. Layout comes "
            "from Docs/RE/formats/pak.md."
        ),
        epilog=(
            "Example:\n"
            "  pak_index.py --pak data.pak --magic PACK --endian little "
            "--index header --preset name64-off32-size32\n"
            "Save to a gitignored scratch file:\n"
            "  pak_index.py --pak data.pak ... > Docs/RE/_dirty/scratch/pak-index.txt"
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    p.add_argument("--pak", help="path to the .pak archive (user-supplied original)")
    p.add_argument(
        "--probe",
        action="store_true",
        help="report only file size + first header bytes (to help seed the spec); no payload",
    )
    p.add_argument("--magic", help="expected signature: ASCII (e.g. PACK) or hex (e.g. 0x504B)")
    p.add_argument(
        "--endian", choices=("little", "big"), default="little",
        help="integer byte order from the spec (default: little)",
    )
    p.add_argument(
        "--index", choices=("header", "footer"), default="header",
        help="where the directory lives: right after the header, or at end-of-file",
    )
    p.add_argument(
        "--preset", help=f"built-in entry layout: {', '.join(LAYOUT_PRESETS)}",
    )
    p.add_argument(
        "--layout",
        help="explicit entry record from the spec, e.g. 'name=64,offset=4,size=4,flags=4'",
    )
    p.add_argument(
        "--count", type=int, default=None,
        help="entry count if the spec fixes it (else derived from a count field or file tail)",
    )
    p.add_argument(
        "--count-size", type=int, default=4, choices=(0, 1, 2, 4, 8),
        help="bytes of the entry-count field after the magic, for --index header (default 4; 0=none)",
    )
    p.add_argument(
        "--dir-offset", type=int, default=None,
        help="absolute byte offset of the directory (required for --index footer)",
    )
    p.add_argument(
        "--max-entries", type=int, default=1_000_000,
        help="safety cap; refuse directories larger than this (default 1,000,000)",
    )
    return p


def main(argv: list[str]) -> int:
    args = build_parser().parse_args(argv)

    if not args.pak:
        _eprint("error: --pak <path> is required")
        return 2
    if not os.path.isfile(args.pak):
        _eprint(f"error: not a file: {args.pak}")
        return 2

    if args.probe:
        return probe(args.pak)

    try:
        return list_index(args)
    except ValueError as exc:
        _eprint(f"error: {exc}")
        return 2
    except struct.error as exc:
        _eprint(f"error: binary unpack failed ({exc}); layout/endianness likely wrong")
        return 3


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
