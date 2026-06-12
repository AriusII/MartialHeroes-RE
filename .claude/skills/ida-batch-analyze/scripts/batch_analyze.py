# batch_analyze.py — per-function metrics + evidence for a set of functions in the legacy
# Martial Heroes client (Main.exe). Subsystem triage; fallback for typed mcp__ida__analyze_batch.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool.
#
# DIRTY: addresses derived from the copyrighted binary; output belongs ONLY under
# Docs/RE/_dirty/static/. Never commit; never copy into clean specs or C#. This collects METRICS
# and REFERENCE EVIDENCE (imports/strings touched), NOT function bodies — never emit pseudo-C.

# === CONFIG ===
# Provide EITHER an explicit TARGETS list (names and/or hex addresses) ...
TARGETS = ["RecvPacketDispatch", 0x004A1230]
# ... OR sweep an address range (set TARGETS = [] and fill these). Keep ranges bounded.
RANGE_START = 0
RANGE_END = 0
LABEL = "subsystem"     # used in the output filename: batch.<label>.md
MAX_FUNCS = 120         # hard cap so a wide range can't explode
OUT_DIR = r"Docs\RE\_dirty\static"
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
    import ida_gdl
except ImportError as exc:
    raise SystemExit("batch_analyze.py must run inside IDA Pro (IDAPython): %s" % exc)


def resolve_one(t):
    if isinstance(t, int):
        f = ida_funcs.get_func(t)
        return f.start_ea if f else idaapi.BADADDR
    ea = ida_name.get_name_ea(idaapi.BADADDR, t)
    if ea != idaapi.BADADDR:
        f = ida_funcs.get_func(ea)
        return f.start_ea if f else ea
    return idaapi.BADADDR


def collect_targets():
    out = []
    if TARGETS:
        for t in TARGETS:
            ea = resolve_one(t)
            if ea == idaapi.BADADDR:
                print("[batch_analyze] could not resolve %r; skipping." % (t,))
                continue
            out.append(ea)
    elif RANGE_END > RANGE_START > 0:
        for fea in idautils.Functions(RANGE_START, RANGE_END):
            out.append(fea)
            if len(out) >= MAX_FUNCS:
                print("[batch_analyze] MAX_FUNCS reached; truncating range sweep.")
                break
    # de-dup, keep order
    seen = set()
    uniq = []
    for ea in out:
        if ea not in seen:
            seen.add(ea)
            uniq.append(ea)
    return uniq


def metrics(ea):
    f = ida_funcs.get_func(ea)
    if not f:
        return None
    insn = 0
    loops = 0
    imports = set()
    strings = set()
    callees = set()
    for head in idautils.Heads(f.start_ea, f.end_ea):
        if not ida_bytes.is_code(ida_bytes.get_flags(head)):
            continue
        insn += 1
        for xref in idautils.XrefsFrom(head, 0):
            if xref.type in (ida_xref.fl_CN, ida_xref.fl_CF):
                cf = ida_funcs.get_func(xref.to)
                nm = ida_name.get_name(xref.to) or (cf and ida_name.get_name(cf.start_ea)) or ""
                if cf:
                    callees.add(cf.start_ea)
                if nm and (nm.startswith("__imp_") or "import" in (idc.get_segm_name(xref.to) or "").lower()):
                    imports.add(nm)
            else:
                # data ref: is it a string?
                if ida_bytes.is_strlit(ida_bytes.get_flags(xref.to)):
                    s = idc.get_strlit_contents(xref.to, -1, idc.STRTYPE_C)
                    if s:
                        try:
                            strings.add(s.decode("utf-8", "replace")[:48])
                        except Exception:
                            pass
    # loop count via back-edges in the flowchart
    try:
        fc = ida_gdl.FlowChart(f)
        blocks = list(fc)
        bb = len(blocks)
        for b in blocks:
            for succ in b.succs():
                if succ.start_ea <= b.start_ea:
                    loops += 1
    except Exception:
        bb = 0
    callers = sum(1 for x in idautils.XrefsTo(ea, 0) if x.type in (ida_xref.fl_CN, ida_xref.fl_CF))
    return {
        "name": ida_name.get_name(ea) or ("sub_%X" % ea),
        "ea": ea,
        "insn": insn,
        "bb": bb,
        "loops": loops,
        "callers": callers,
        "callees": len(callees),
        "imports": sorted(imports)[:8],
        "strings": sorted(strings)[:8],
    }


def role_hint(m):
    if m["strings"] and m["insn"] > 40:
        return "string/table-driven loader or parser (refs strings)"
    if m["loops"] >= 2 and m["callees"] <= 2:
        return "tight loop / per-element transform (parse or compute)"
    if m["callees"] >= 8:
        return "orchestrator / dispatch fan-out (many callees)"
    if m["insn"] < 20 and m["callees"] == 0:
        return "leaf helper (small, no calls)"
    return "general function (confirm by reading)"


def main():
    targets = collect_targets()
    if not targets:
        print("[batch_analyze] no targets resolved; set TARGETS or RANGE_START/RANGE_END.")
        return

    rows = []
    for ea in targets:
        m = metrics(ea)
        if m:
            rows.append(m)

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# batch_analyze: %s" % LABEL,
        "",
        "- generated: %s" % stamp,
        "- functions: %d" % len(rows),
        "",
        "| Func EA | Name | Insn | BB | Loops | In | Out | Role hint (HYPOTHESIS) |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for m in rows:
        lines.append("| 0x%X | %s | %d | %d | %d | %d | %d | %s |" % (
            m["ea"], m["name"], m["insn"], m["bb"], m["loops"],
            m["callers"], m["callees"], role_hint(m)))

    lines += ["", "## Reference evidence (imports / strings touched)", ""]
    for m in rows:
        ev = []
        if m["imports"]:
            ev.append("imports: " + ", ".join(m["imports"]))
        if m["strings"]:
            ev.append("strings: " + " | ".join(repr(s) for s in m["strings"]))
        lines.append("- **%s** (`0x%X`): %s" % (m["name"], m["ea"], "; ".join(ev) if ev else "(none observed)"))

    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", LABEL)[:60] or "subsystem"
        path = os.path.join(OUT_DIR, "batch.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[batch_analyze] wrote %s" % path)
    except Exception as exc:
        print("\n[batch_analyze] could not write file (%s); save the Markdown above via Write." % exc)


main()
