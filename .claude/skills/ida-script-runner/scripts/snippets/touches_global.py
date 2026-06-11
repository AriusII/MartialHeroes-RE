# touches_global.py — list functions that read/write a global/static in the legacy client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), via the IDA MCP script-exec tool.
# DIRTY-ROOM output: addresses are allowed only under Docs/RE/_dirty/.

# === CONFIG ===
# Provide EITHER a hex address OR a name. If GLOBAL_EA is set (non-None) it wins.
GLOBAL_EA = None                 # e.g. 0x00B12340
GLOBAL_NAME = "g_pNetwork"       # e.g. "g_SessionKey" — used when GLOBAL_EA is None
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import ida_funcs
    import ida_name
    import ida_bytes
    import ida_xref
    import ida_ua
except ImportError as exc:
    raise SystemExit("touches_global.py must run inside IDA Pro (IDAPython): %s" % exc)


def resolve():
    if GLOBAL_EA is not None:
        return GLOBAL_EA
    ea = ida_name.get_name_ea(idaapi.BADADDR, GLOBAL_NAME)
    return ea


def func_name(ea):
    f = ida_funcs.get_func(ea)
    if not f:
        return "(not in a function)", idaapi.BADADDR
    nm = ida_name.get_name(f.start_ea)
    return (nm if nm else "sub_%X" % f.start_ea), f.start_ea


def access_kind(insn_ea, target):
    """Best-effort read/write classification by checking which operand is the target and
    whether the instruction is a store-shaped mnemonic."""
    insn = ida_ua.insn_t()
    if not ida_ua.decode_insn(insn, insn_ea):
        return "?"
    mnem = ida_ua.print_insn_mnem(insn_ea).lower()
    # Store-shaped: target is the first operand (destination) of a mov/lea-free write.
    store_mnems = ("mov", "add", "sub", "or", "and", "xor", "inc", "dec", "shl", "shr",
                   "stos", "mul", "imul")
    # Operand 0 = destination on x86.
    if mnem in store_mnems:
        op0 = insn.ops[0]
        if op0.type in (ida_ua.o_mem, ida_ua.o_displ) and op0.addr == target:
            if mnem == "mov":
                return "write"
            return "rmw"
    if mnem == "lea":
        return "addr-of"
    return "read"


def main():
    ea = resolve()
    if ea is None or ea == idaapi.BADADDR:
        print("[touches_global] could not resolve global (EA=%r NAME=%r)" % (GLOBAL_EA, GLOBAL_NAME))
        return
    gname = ida_name.get_name(ea) or ("0x%X" % ea)
    rows = []
    for xref in idautils.XrefsTo(ea, 0):
        nm, fs = func_name(xref.frm)
        kind = access_kind(xref.frm, ea)
        rows.append((xref.frm, fs, nm, kind))
    rows.sort(key=lambda r: r[0])

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# touches_global: %s" % gname,
        "",
        "- global_ea: `0x%X`" % ea,
        "- generated: %s" % stamp,
        "- access_sites: %d" % len(rows),
        "- note: read/write classification is heuristic (x86 operand-0 = destination).",
        "",
        "| Site EA | Func EA | Func Name | Access |",
        "|---|---|---|---|",
    ]
    for site, fs, nm, kind in rows:
        fsstr = "0x%X" % fs if fs != idaapi.BADADDR else "-"
        lines.append("| 0x%X | %s | %s | %s |" % (site, fsstr, nm, kind))
    report = "\n".join(lines)
    print(report)

    try:
        import os, re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", gname)[:60]
        path = os.path.join(OUT_DIR, "touches_global.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[touches_global] wrote %s" % path)
    except Exception as exc:
        print("\n[touches_global] could not write file (%s); save the Markdown above via Write." % exc)


main()
