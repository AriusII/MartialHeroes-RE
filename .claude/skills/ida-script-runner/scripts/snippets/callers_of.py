# callers_of.py — list every caller of a function in the legacy Martial Heroes client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), via the IDA MCP script-exec tool.
# DIRTY-ROOM output: addresses are allowed only under Docs/RE/_dirty/.

# === CONFIG ===
# TARGET: a function name (e.g. "CNetwork::Recv") OR a hex address (e.g. 0x004A1230).
TARGET = "recv"
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import ida_funcs
    import ida_name
    import ida_xref
except ImportError as exc:
    raise SystemExit("callers_of.py must run inside IDA Pro (IDAPython): %s" % exc)


def resolve_target(t):
    if isinstance(t, int):
        return t
    ea = ida_name.get_name_ea(idaapi.BADADDR, t)
    if ea != idaapi.BADADDR:
        return ea
    # Substring search across all names if exact lookup failed.
    matches = []
    for ea2, nm in idautils.Names():
        if t.lower() in nm.lower():
            matches.append((ea2, nm))
    if len(matches) == 1:
        return matches[0][0]
    if matches:
        print("[callers_of] ambiguous TARGET '%s' matched %d names:" % (t, len(matches)))
        for ea2, nm in matches[:20]:
            print("   0x%X  %s" % (ea2, nm))
        return matches[0][0]  # use first; analyst can refine CONFIG
    return idaapi.BADADDR


def func_name(ea):
    f = ida_funcs.get_func(ea)
    if not f:
        return "(not in a function)"
    nm = ida_name.get_name(f.start_ea)
    return nm if nm else "sub_%X" % f.start_ea


def func_start(ea):
    f = ida_funcs.get_func(ea)
    return f.start_ea if f else idaapi.BADADDR


def main():
    ea = resolve_target(TARGET)
    if ea == idaapi.BADADDR:
        print("[callers_of] could not resolve TARGET=%r" % (TARGET,))
        return
    tname = ida_name.get_name(ea) or ("0x%X" % ea)
    rows = []
    seen = set()
    for xref in idautils.XrefsTo(ea, 0):
        kind = "call" if xref.type in (ida_xref.fl_CN, ida_xref.fl_CF) else \
               ("data" if xref.iscode == 0 else "jump")
        fs = func_start(xref.frm)
        key = (fs, xref.frm)
        if key in seen:
            continue
        seen.add(key)
        rows.append((xref.frm, fs, func_name(xref.frm), kind))

    rows.sort(key=lambda r: r[0])
    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# callers_of: %s" % tname,
        "",
        "- target_ea: `0x%X`" % ea,
        "- generated: %s" % stamp,
        "- caller_sites: %d" % len(rows),
        "",
        "| Site EA | Caller Func EA | Caller Name | Ref Kind |",
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
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", tname)[:60]
        path = os.path.join(OUT_DIR, "callers_of.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[callers_of] wrote %s" % path)
    except Exception as exc:
        print("\n[callers_of] could not write file (%s); save the Markdown above via Write." % exc)


main()
