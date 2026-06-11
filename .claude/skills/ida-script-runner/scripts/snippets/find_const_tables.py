# find_const_tables.py — find S-box-like constant tables in the legacy Martial Heroes client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), via the IDA MCP script-exec tool.
# DIRTY-ROOM output: addresses are allowed only under Docs/RE/_dirty/.
#
# Targets the classic crypto/asset shapes:
#   - 256-byte tables (AES/Blowfish S-boxes, permutation tables, substitution maps)
#   - 256-dword tables (CRC32 tables, T-tables)
# For each, reports EA, element size, a coarse byte-distribution hint, and whether a 256-byte
# table is a permutation of 0..255 (a very strong S-box signal).

# === CONFIG ===
# Segments to scan. Constant tables almost always live in .rdata; add .data if needed.
SCAN_SEGMENTS = (".rdata", ".data")
WANT_BYTE_256 = True
WANT_DWORD_256 = True
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import ida_bytes
    import ida_segment
    import ida_name
except ImportError as exc:
    raise SystemExit("find_const_tables.py must run inside IDA Pro (IDAPython): %s" % exc)


def seg_bounds():
    out = []
    for seg_ea in idautils.Segments():
        seg = ida_segment.getseg(seg_ea)
        if seg and (ida_segment.get_segm_name(seg) in SCAN_SEGMENTS):
            out.append((seg.start_ea, seg.end_ea))
    return out


def is_permutation_0_255(values):
    return len(values) == 256 and sorted(values) == list(range(256))


def byte_entropy_hint(values):
    """Coarse: count distinct byte values. 256 distinct in a 256 table => permutation-like."""
    return len(set(values))


def scan_byte_tables():
    hits = []
    for s, e in seg_bounds():
        ea = s
        # Slide a 256-byte window only at 4-byte-aligned starts to keep it fast.
        while ea + 256 <= e:
            vals = [ida_bytes.get_byte(ea + i) for i in range(256)]
            distinct = byte_entropy_hint(vals)
            perm = is_permutation_0_255(vals)
            # Report only "interesting" windows: high distinctness or a clean permutation.
            if perm or distinct >= 200:
                hits.append({
                    "ea": ea, "elem": "byte", "count": 256,
                    "distinct": distinct, "perm": perm,
                })
                ea += 256          # skip past a confirmed table
                continue
            ea += 4
    return hits


def scan_dword_tables():
    hits = []
    for s, e in seg_bounds():
        ea = s
        while ea + 256 * 4 <= e:
            vals = [ida_bytes.get_dword(ea + i * 4) for i in range(256)]
            distinct = len(set(vals))
            # CRC/T-tables are nearly all-distinct dwords with high spread.
            if distinct >= 250:
                hits.append({
                    "ea": ea, "elem": "dword", "count": 256,
                    "distinct": distinct, "perm": False,
                })
                ea += 256 * 4
                continue
            ea += 4
    return hits


def name_of(ea):
    nm = ida_name.get_name(ea)
    return nm if nm else ""


def main():
    hits = []
    if WANT_BYTE_256:
        hits.extend(scan_byte_tables())
    if WANT_DWORD_256:
        hits.extend(scan_dword_tables())
    hits.sort(key=lambda h: (not h["perm"], -h["distinct"]))

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# find_const_tables (S-box / lookup-table hunt)",
        "",
        "- generated: %s" % stamp,
        "- segments: %s" % ", ".join(SCAN_SEGMENTS),
        "- candidates: %d" % len(hits),
        "- a 256-byte permutation of 0..255 is a very strong S-box signal.",
        "- NOTE: a recovered S-box may be reused in C# only via a reviewed Network.Crypto spec;",
        "  do NOT transcribe it straight into source.",
        "",
        "| Table EA | Name | Elem | Count | Distinct | Permutation? |",
        "|---|---|---|---|---|---|",
    ]
    for h in hits:
        lines.append("| 0x%X | %s | %s | %d | %d | %s |" % (
            h["ea"], name_of(h["ea"]) or "-", h["elem"], h["count"],
            h["distinct"], "YES" if h["perm"] else "no"))
    report = "\n".join(lines)
    print(report)

    try:
        import os
        os.makedirs(OUT_DIR, exist_ok=True)
        path = os.path.join(OUT_DIR, "const_tables.md")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[find_const_tables] wrote %s" % path)
    except Exception as exc:
        print("\n[find_const_tables] could not write file (%s); save the Markdown above via Write." % exc)


main()
