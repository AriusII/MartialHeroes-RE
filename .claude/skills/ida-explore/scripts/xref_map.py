# xref_map.py — enumerate every cross-reference to a target in the legacy Martial Heroes
# client (Main.exe) and group it by the function that references it.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool. Fallback for when
# the typed mcp__ida__xref_query / xrefs_to / trace_data_flow tools are unavailable.
#
# DIRTY: addresses are derived from the copyrighted binary; output belongs ONLY under
# Docs/RE/_dirty/queries/. Never commit it; never copy into clean specs or C#.

# === CONFIG ===
# TARGET: a symbol name (e.g. "g_RollingKey"), a hex address (0x004A1230), or — when MODE="const" —
# an integer immediate to find (e.g. 0xDEADBEEF).
TARGET = "g_RollingKey"
# MODE: "auto"  -> resolve TARGET as a name/address and walk all xrefs to it
#       "code"  -> only code xrefs to TARGET (callers/jumps)
#       "data"  -> only data xrefs to TARGET (reads/writes)
#       "const" -> treat TARGET as an integer immediate; find instructions using it
MODE = "auto"
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_xref
    import ida_bytes
except ImportError as exc:
    raise SystemExit("xref_map.py must run inside IDA Pro (IDAPython): %s" % exc)


def resolve(target):
    if isinstance(target, int):
        return target
    ea = ida_name.get_name_ea(idaapi.BADADDR, target)
    if ea != idaapi.BADADDR:
        return ea
    matches = [(e, n) for e, n in idautils.Names() if target.lower() in n.lower()]
    if matches:
        if len(matches) > 1:
            print("[xref_map] ambiguous TARGET %r matched %d names; using first:" % (target, len(matches)))
            for e, n in matches[:20]:
                print("   0x%X  %s" % (e, n))
        return matches[0][0]
    return idaapi.BADADDR


def func_for(ea):
    f = ida_funcs.get_func(ea)
    if not f:
        return idaapi.BADADDR, "(not in a function)"
    nm = ida_name.get_name(f.start_ea) or ("sub_%X" % f.start_ea)
    return f.start_ea, nm


def xref_kind(xref):
    if xref.type in (ida_xref.fl_CN, ida_xref.fl_CF):
        return "call"
    if xref.type in (ida_xref.fl_JN, ida_xref.fl_JF):
        return "jump"
    if xref.iscode == 0:
        # Data: distinguish read vs write from the dref type.
        if xref.type == ida_xref.dr_W:
            return "write"
        if xref.type == ida_xref.dr_R:
            return "read"
        return "data"
    return "code"


def walk_xrefs(ea, mode):
    rows = []
    flags = 0
    for xref in idautils.XrefsTo(ea, flags):
        kind = xref_kind(xref)
        if mode == "code" and kind not in ("call", "jump", "code"):
            continue
        if mode == "data" and kind not in ("read", "write", "data"):
            continue
        fs, fn = func_for(xref.frm)
        rows.append((xref.frm, fs, fn, kind))
    return rows


def walk_const(value):
    """Find instructions whose immediate operand equals `value` (const xref-by-value)."""
    rows = []
    for fea in idautils.Functions():
        f = ida_funcs.get_func(fea)
        if not f:
            continue
        for head in idautils.Heads(f.start_ea, f.end_ea):
            if not ida_bytes.is_code(ida_bytes.get_flags(head)):
                continue
            for opn in range(8):
                try:
                    v = idc.get_operand_value(head, opn)
                except Exception:
                    break
                if v == idaapi.BADADDR:
                    continue
                if v == value and idc.get_operand_type(head, opn) in (idc.o_imm,):
                    fs, fn = func_for(head)
                    rows.append((head, fs, fn, "imm"))
                    break
    return rows


def main():
    if MODE == "const":
        value = TARGET if isinstance(TARGET, int) else int(str(TARGET), 0)
        rows = walk_const(value)
        anchor = "const 0x%X" % value
        anchor_ea = None
    else:
        ea = resolve(TARGET)
        if ea == idaapi.BADADDR:
            print("[xref_map] could not resolve TARGET=%r" % (TARGET,))
            return
        anchor = ida_name.get_name(ea) or ("0x%X" % ea)
        anchor_ea = ea
        rows = walk_xrefs(ea, MODE)

    # Group by referencing function.
    by_func = {}
    for site, fs, fn, kind in rows:
        key = (fs, fn)
        by_func.setdefault(key, []).append((site, kind))

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# xref_map: %s" % anchor,
        "",
        "- anchor: `%s`%s" % (anchor, ("  (ea `0x%X`)" % anchor_ea) if anchor_ea else ""),
        "- mode: %s" % MODE,
        "- generated: %s" % stamp,
        "- total_refs: %d  across %d function(s)" % (len(rows), len(by_func)),
        "",
        "| Caller Func EA | Caller Name | Refs | Kinds | Sites |",
        "|---|---|---|---|---|",
    ]
    for (fs, fn), refs in sorted(by_func.items(), key=lambda kv: (kv[0][0] if kv[0][0] != idaapi.BADADDR else 1 << 62)):
        kinds = ",".join(sorted({k for _, k in refs}))
        sites = " ".join("0x%X" % s for s, _ in refs[:8])
        if len(refs) > 8:
            sites += " …"
        fsstr = "0x%X" % fs if fs != idaapi.BADADDR else "-"
        lines.append("| %s | %s | %d | %s | %s |" % (fsstr, fn, len(refs), kinds, sites))

    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", anchor)[:60]
        path = os.path.join(OUT_DIR, "xref_map.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[xref_map] wrote %s" % path)
    except Exception as exc:
        print("\n[xref_map] could not write file (%s); save the Markdown above via Write." % exc)


main()
