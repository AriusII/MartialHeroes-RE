# find_bitops_loops.py — find crypto-shaped bit-twiddling loops (XOR/ROL/ROR/shift) in the
# legacy Martial Heroes client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), via the IDA MCP script-exec tool.
# DIRTY-ROOM output: addresses are allowed only under Docs/RE/_dirty/.
#
# Heuristic: a back-edge (loop) whose body is dense in xor/rol/ror/shl/shr/and/add and that
# walks a buffer with a small per-iteration stride is the classic shape of a stream/block
# cipher inner loop or a per-byte (de)obfuscation pass.

# === CONFIG ===
# SCOPE_FUNC: limit the scan to one function (name or hex EA) to focus on, e.g. the recv path.
# Set to None to scan the whole binary (slower).
SCOPE_FUNC = None
MIN_BITOPS = 3          # min distinct bit-op instructions in a loop body to report it
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import ida_funcs
    import ida_name
    import ida_ua
    import ida_gdl
except ImportError as exc:
    raise SystemExit("find_bitops_loops.py must run inside IDA Pro (IDAPython): %s" % exc)

BIT_MNEMS = ("xor", "rol", "ror", "shl", "shr", "sar", "and", "or", "not", "add", "sub")
ROT_MNEMS = ("rol", "ror")


def resolve_scope():
    if SCOPE_FUNC is None:
        return None
    if isinstance(SCOPE_FUNC, int):
        return ida_funcs.get_func(SCOPE_FUNC)
    ea = ida_name.get_name_ea(idaapi.BADADDR, SCOPE_FUNC)
    return ida_funcs.get_func(ea) if ea != idaapi.BADADDR else None


def func_iter(scope):
    if scope is not None:
        yield scope.start_ea
    else:
        for ea in idautils.Functions():
            yield ea


def analyze_func(func_ea):
    f = ida_funcs.get_func(func_ea)
    if not f:
        return []
    fc = ida_gdl.FlowChart(f, flags=ida_gdl.FC_PREDS)
    results = []
    for block in fc:
        # A block is a loop body if any successor is itself or a dominator-ish back-edge.
        is_loop = False
        for succ in block.succs():
            if succ.start_ea <= block.start_ea:   # back-edge heuristic
                is_loop = True
                break
        if not is_loop:
            continue
        mnem_counts = {}
        has_rot = False
        has_xor = False
        n = 0
        ea = block.start_ea
        while ea < block.end_ea:
            mnem = ida_ua.print_insn_mnem(ea).lower()
            if mnem in BIT_MNEMS:
                mnem_counts[mnem] = mnem_counts.get(mnem, 0) + 1
                n += 1
                if mnem in ROT_MNEMS:
                    has_rot = True
                if mnem == "xor":
                    # ignore "xor reg,reg" (zeroing) — same operand text.
                    if ida_ua.print_operand(ea, 0) != ida_ua.print_operand(ea, 1):
                        has_xor = True
            ea = idaapi.next_head(ea, block.end_ea)
        distinct = len([m for m in mnem_counts if mnem_counts[m] > 0])
        if n >= MIN_BITOPS and (has_xor or has_rot):
            fingerprint = ", ".join("%s:%d" % (m, c) for m, c in sorted(mnem_counts.items()))
            score = n + (5 if has_rot else 0) + (3 if has_xor else 0)
            results.append({
                "block_ea": block.start_ea,
                "func_ea": func_ea,
                "bitop_count": n,
                "distinct": distinct,
                "fingerprint": fingerprint,
                "score": score,
            })
    return results


def func_name(ea):
    nm = ida_name.get_name(ea)
    return nm if nm else "sub_%X" % ea


def main():
    scope = resolve_scope()
    if SCOPE_FUNC is not None and scope is None:
        print("[find_bitops_loops] could not resolve SCOPE_FUNC=%r" % (SCOPE_FUNC,))
        return
    found = []
    for fea in func_iter(scope):
        found.extend(analyze_func(fea))
    found.sort(key=lambda r: r["score"], reverse=True)
    top = found[:40]

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    scope_str = func_name(scope.start_ea) if scope else "ENTIRE BINARY"
    lines = [
        "# find_bitops_loops (scope: %s)" % scope_str,
        "",
        "- generated: %s" % stamp,
        "- candidate_loops: %d (showing top %d)" % (len(found), len(top)),
        "- heuristic: loop body dense in xor/rol/ror/shift; rotations strongly imply a cipher.",
        "",
        "| Loop Block EA | Func EA | Func | BitOps | Distinct | Fingerprint | Score |",
        "|---|---|---|---|---|---|---|",
    ]
    for r in top:
        lines.append("| 0x%X | 0x%X | %s | %d | %d | %s | %d |" % (
            r["block_ea"], r["func_ea"], func_name(r["func_ea"]),
            r["bitop_count"], r["distinct"], r["fingerprint"], r["score"]))
    report = "\n".join(lines)
    print(report)

    try:
        import os, re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", scope_str)[:50]
        path = os.path.join(OUT_DIR, "bitops.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[find_bitops_loops] wrote %s" % path)
    except Exception as exc:
        print("\n[find_bitops_loops] could not write file (%s); save the Markdown above via Write." % exc)


main()
