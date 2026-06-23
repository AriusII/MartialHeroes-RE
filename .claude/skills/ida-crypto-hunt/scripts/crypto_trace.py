# crypto_trace.py — recover the packet cipher of the legacy Martial Heroes client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), via the IDA MCP script-exec tool.
# DIRTY-ROOM output: addresses are allowed only under Docs/RE/_dirty/.
#
# Pipeline:
#   1. Anchor on the network read/decrypt path (imports recv/WSARecv; names/strings with
#      recv/decrypt/cipher/crypt).
#   2. From each anchor, walk callees a few levels and score functions whose loops are
#      cipher-shaped (xor / rol / ror / shift / add, per-byte stride).
#   3. For each cipher candidate, look for nearby/referenced 256-byte or 256-dword constant
#      tables (S-box / round constants / CRC) and flag 0..255 permutations.
#   4. Emit a ranked crypto report combining all three signals.
#
# The committed deliverable for Network.Crypto is a NEUTRAL algorithm description authored
# by a spec-author into Docs/RE/specs/crypto.md — NOT this file, and NEVER pasted pseudo-code.

import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_ua
    import ida_bytes
    import ida_gdl
    import ida_segment
    import ida_nalt
except ImportError as exc:
    raise SystemExit("crypto_trace.py must run inside IDA Pro (IDAPython): %s" % exc)

# ----------------------------------------------------------------------------------------
# CONFIG
# ----------------------------------------------------------------------------------------
OUT_PATH = r"Docs\RE\_dirty\crypto\crypto_trace.md"
WALK_DEPTH = 3                 # callee levels to descend from each recv/decrypt anchor
MIN_BITOPS = 3                 # min bit-ops in a loop body to call it cipher-shaped
RECV_IMPORTS = ("recv", "wsarecv")
ANCHOR_NAME_HINTS = (
    "recv", "decrypt", "encrypt", "cipher", "crypt", "xor", "decode", "unscramble",
    "deobfuscate", "processpacket", "readpacket", "netmsg", "session", "key",
)
PTR_SIZE = 8 if idaapi.get_inf_structure().is_64bit() else 4
BIT_MNEMS = ("xor", "rol", "ror", "shl", "shr", "sar", "and", "or", "add", "sub")
ROT_MNEMS = ("rol", "ror")


def fname(ea):
    f = ida_funcs.get_func(ea)
    if not f:
        return ""
    nm = ida_name.get_name(f.start_ea)
    return nm if nm else "sub_%X" % f.start_ea


def func_start(ea):
    f = ida_funcs.get_func(ea)
    return f.start_ea if f else idaapi.BADADDR


# ----------------------------------------------------------------------------------------
# 1. Anchors on the recv/decrypt path
# ----------------------------------------------------------------------------------------
def import_anchor_funcs():
    """Functions that call recv/WSARecv (any module)."""
    anchors = set()
    nimps = idaapi.get_import_module_qty()

    def cb(ea, name, ordinal):
        if name and name.lower() in RECV_IMPORTS:
            for xref in idautils.CodeRefsTo(ea, 0):
                fs = func_start(xref)
                if fs != idaapi.BADADDR:
                    anchors.add(fs)
        return True

    for i in range(nimps):
        idaapi.enum_import_names(i, cb)

    # Some binaries reference the IAT slot by address, not the thunk.
    for ea, name in idautils.Names():
        low = name.lower()
        if any(low == imp or low.endswith("_" + imp) for imp in RECV_IMPORTS):
            for xref in idautils.CodeRefsTo(ea, 0):
                fs = func_start(xref)
                if fs != idaapi.BADADDR:
                    anchors.add(fs)
    return anchors


def name_anchor_funcs():
    anchors = set()
    for ea in idautils.Functions():
        nm = (ida_name.get_name(ea) or "").lower()
        if any(h in nm for h in ANCHOR_NAME_HINTS):
            anchors.add(ea)
    return anchors


def collect_anchors():
    a = import_anchor_funcs() | name_anchor_funcs()
    return a


# ----------------------------------------------------------------------------------------
# 2. Cipher-shaped loop detection within a function
# ----------------------------------------------------------------------------------------
def cipher_loop_fingerprint(func_ea):
    f = ida_funcs.get_func(func_ea)
    if not f:
        return None
    fc = ida_gdl.FlowChart(f, flags=ida_gdl.FC_PREDS)
    best = None
    for block in fc:
        is_loop = any(s.start_ea <= block.start_ea for s in block.succs())
        if not is_loop:
            continue
        counts = {}
        has_rot = False
        has_xor = False
        n = 0
        ea = block.start_ea
        while ea < block.end_ea:
            mnem = ida_ua.print_insn_mnem(ea).lower()
            if mnem in BIT_MNEMS:
                if mnem == "xor" and ida_ua.print_operand(ea, 0) == ida_ua.print_operand(ea, 1):
                    pass  # zeroing idiom, ignore
                else:
                    counts[mnem] = counts.get(mnem, 0) + 1
                    n += 1
                    if mnem in ROT_MNEMS:
                        has_rot = True
                    if mnem == "xor":
                        has_xor = True
            ea = idaapi.next_head(ea, block.end_ea)
        if n >= MIN_BITOPS and (has_xor or has_rot):
            score = n + (5 if has_rot else 0) + (3 if has_xor else 0)
            fp = ", ".join("%s:%d" % (m, c) for m, c in sorted(counts.items()))
            if best is None or score > best["score"]:
                best = {"block_ea": block.start_ea, "score": score,
                        "fingerprint": fp, "bitops": n,
                        "has_rot": has_rot, "has_xor": has_xor}
    return best


def walk_callees(roots, depth):
    seen = set()
    frontier = set(roots)
    order = []
    for _ in range(depth + 1):
        nxt = set()
        for fe in frontier:
            if fe in seen:
                continue
            seen.add(fe)
            order.append(fe)
            for callee in idautils.CodeRefsFrom(fe, 0):
                fs = func_start(callee)
                if fs != idaapi.BADADDR and fs not in seen:
                    nxt.add(fs)
        frontier = nxt
    return order


# ----------------------------------------------------------------------------------------
# 3. Constant tables referenced by / near a function
# ----------------------------------------------------------------------------------------
def referenced_const_tables(func_ea):
    """Data refs out of the function that point at 256-entry constant tables."""
    f = ida_funcs.get_func(func_ea)
    if not f:
        return []
    tables = []
    ea = f.start_ea
    checked = set()
    while ea < f.end_ea:
        for dref in idautils.DataRefsFrom(ea):
            if dref in checked:
                continue
            checked.add(dref)
            t = classify_table(dref)
            if t:
                tables.append(t)
        ea = idaapi.next_head(ea, f.end_ea)
    return tables


def classify_table(ea):
    seg = ida_segment.getseg(ea)
    if seg is None:
        return None
    # 256-byte permutation / substitution?
    try:
        bvals = [ida_bytes.get_byte(ea + i) for i in range(256)]
    except Exception:
        bvals = []
    if len(bvals) == 256:
        distinct = len(set(bvals))
        perm = sorted(bvals) == list(range(256))
        if perm or distinct >= 200:
            return {"ea": ea, "elem": "byte", "distinct": distinct, "perm": perm}
    # 256-dword (CRC / T-table)?
    try:
        dvals = [ida_bytes.get_dword(ea + i * 4) for i in range(256)]
    except Exception:
        dvals = []
    if len(dvals) == 256 and len(set(dvals)) >= 250:
        return {"ea": ea, "elem": "dword", "distinct": len(set(dvals)), "perm": False}
    return None


# ----------------------------------------------------------------------------------------
# Report
# ----------------------------------------------------------------------------------------
def main():
    print("[crypto_trace] collecting recv/decrypt anchors...")
    anchors = collect_anchors()
    if not anchors:
        print("[crypto_trace] no recv/decrypt anchors found; widen ANCHOR_NAME_HINTS or "
              "use ida-script-runner snippets manually.")
        return

    candidates = walk_callees(anchors, WALK_DEPTH)
    results = []
    for fe in candidates:
        fp = cipher_loop_fingerprint(fe)
        if not fp:
            continue
        tables = referenced_const_tables(fe)
        near_anchor = fe in anchors
        total = fp["score"] + (8 if tables else 0) + (6 if near_anchor else 0)
        results.append({"func_ea": fe, "fp": fp, "tables": tables,
                        "near_anchor": near_anchor, "score": total})
    results.sort(key=lambda r: r["score"], reverse=True)
    top = results[:25]

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    binname = ida_nalt.get_root_filename() or "(unknown)"
    lines = [
        "# crypto_trace.md — DIRTY-ROOM packet-cipher recon (gitignored)",
        "",
        "> Generated by skill `ida-crypto-hunt` (scripts/crypto_trace.py).",
        "> Contains IDA addresses. The committed deliverable is a NEUTRAL algorithm",
        "> description in Docs/RE/specs/crypto.md, authored by a spec-author — never",
        "> transcribed code. Constants promote only via a reviewed, interoperability-",
        "> justified spec.",
        "",
        "- binary: `%s`" % binname,
        "- generated: %s" % stamp,
        "- recv/decrypt anchors: %d" % len(anchors),
        "- cipher-shaped candidates: %d (showing top %d)" % (len(results), len(top)),
        "",
        "Rank = bit-op loop tightness + has constant table + on the recv path.",
        "The top candidate(s) with a tight XOR/rotate loop AND a referenced 256-entry table",
        "are the most likely cipher / key-schedule routines. Symmetric pairs are likely",
        "encrypt + decrypt.",
        "",
        "---",
        "",
        "| Rank | Func EA | Func | On recv path | Loop @ | Loop fingerprint | Const tables | Score |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for i, r in enumerate(top):
        fp = r["fp"]
        tbl = "; ".join(
            "0x%X(%s%s)" % (t["ea"], t["elem"], ",perm" if t["perm"] else "")
            for t in r["tables"]) or "-"
        lines.append("| %d | 0x%X | %s | %s | 0x%X | %s | %s | %d |" % (
            i + 1, r["func_ea"], fname(r["func_ea"]),
            "yes" if r["near_anchor"] else "no",
            fp["block_ea"], fp["fingerprint"], tbl, r["score"]))

    # Detail the const tables once more for the spec-author.
    all_tables = {}
    for r in top:
        for t in r["tables"]:
            all_tables[t["ea"]] = t
    if all_tables:
        lines.append("")
        lines.append("## Constant tables found near cipher candidates")
        lines.append("")
        lines.append("| Table EA | Elem | Distinct | Permutation 0..255? |")
        lines.append("|---|---|---|---|")
        for ea in sorted(all_tables):
            t = all_tables[ea]
            lines.append("| 0x%X | %s | %d | %s |" % (
                ea, t["elem"], t["distinct"], "YES" if t["perm"] else "no"))

    lines.append("")
    lines.append("## Analyst next steps (neutral characterization)")
    lines.append("")
    lines.append("- Operation per unit: XOR / add-mod-256 / rotate-by-key? stream vs. block?")
    lines.append("- State size and how the key seeds it (key schedule).")
    lines.append("- Where the key enters: which handshake/opcode delivers it?")
    lines.append("- Is decrypt == encrypt (same routine), or a symmetric pair?")
    lines.append("- Endianness of any multi-byte words.")
    lines.append("- Use ida-script-runner callers_of.py / touches_global.py on the key setter.")

    report = "\n".join(lines)
    print(report)

    try:
        import os
        os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
        with open(OUT_PATH, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[crypto_trace] wrote %s" % OUT_PATH)
    except Exception as exc:
        print("\n[crypto_trace] could not write %s (%s); save the Markdown above via Write." % (OUT_PATH, exc))


main()
