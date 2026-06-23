# struct_probe.py — recover a C++ object layout by observing this+offset access patterns in
# the legacy Martial Heroes client (Main.exe). Emits a NEUTRAL offset table — never pseudo-C.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool.
#
# IDEA: in __thiscall code the object pointer lives in ecx at entry; member accesses look like
# [ecx+0xNN] / [reg+0xNN] after ecx is copied to a base reg. This script approximates the layout
# by collecting displacement accesses based at THIS_REG (and copies of it) within each function:
# offset, access size (byte/word/dword/qword), read vs write, and the vtable signature at +0.
#
# DIRTY: offsets/addresses derived from the copyrighted binary; output belongs ONLY under
# Docs/RE/_dirty/structs/. Never commit; never copy into clean specs or C#. The artifact is an
# OFFSET TABLE, not a function body.

# === CONFIG ===
# Functions that operate on the object (names and/or hex addresses). More functions = better layout.
FUNCS = ["CActor::CActor", 0x004A1230]
THIS_REG = "ecx"        # register holding `this` at entry (x86 __thiscall convention)
STRUCT_NAME = "CObject"
MAX_OFFSET = 0x4000     # ignore absurd displacements (likely not this-relative)
OUT_DIR = r"Docs\RE\_dirty\structs"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_bytes
    import ida_ua
except ImportError as exc:
    raise SystemExit("struct_probe.py must run inside IDA Pro (IDAPython): %s" % exc)


_SIZE_NAME = {1: "byte", 2: "word", 4: "dword", 8: "qword"}


def resolve(t):
    if isinstance(t, int):
        f = ida_funcs.get_func(t)
        return f.start_ea if f else idaapi.BADADDR
    ea = ida_name.get_name_ea(idaapi.BADADDR, t)
    if ea != idaapi.BADADDR:
        f = ida_funcs.get_func(ea)
        return f.start_ea if f else ea
    matches = [e for e, n in idautils.Names() if t.lower() in n.lower()]
    return matches[0] if matches else idaapi.BADADDR


def op_size(ea, opn):
    """Infer access size (bytes) of a memory operand from its dtype."""
    insn = ida_ua.insn_t()
    if ida_ua.decode_insn(insn, ea) == 0:
        return 4
    op = insn.ops[opn]
    dt = op.dtype
    return {ida_ua.dt_byte: 1, ida_ua.dt_word: 2, ida_ua.dt_dword: 4,
            ida_ua.dt_qword: 8, ida_ua.dt_float: 4, ida_ua.dt_double: 8}.get(dt, 4)


def scan_func(start_ea, base_regs, fields, vtable_hit):
    """Collect [base+disp] accesses. base_regs is the set of registers currently aliasing `this`."""
    f = ida_funcs.get_func(start_ea)
    if not f:
        return
    for head in idautils.Heads(f.start_ea, f.end_ea):
        if not ida_bytes.is_code(ida_bytes.get_flags(head)):
            continue
        mnem = idc.print_insn_mnem(head).lower()
        # Track `mov <reg>, <base>` to follow aliases of this (very rough).
        if mnem == "mov" and idc.get_operand_type(head, 1) == idc.o_reg:
            src = idc.print_operand(head, 1).strip().lower()
            dst = idc.print_operand(head, 0).strip().lower()
            if src in base_regs and idc.get_operand_type(head, 0) == idc.o_reg:
                base_regs.add(dst)
        for opn in range(4):
            otype = idc.get_operand_type(head, opn)
            if otype != idc.o_displ:
                continue
            txt = idc.print_operand(head, opn).lower()
            # crude base-register extraction: "[ecx+1Ch]" -> base "ecx"
            base = None
            for br in base_regs:
                if "[%s" % br in txt or "+%s" % br in txt or "%s+" % br in txt:
                    base = br
                    break
            if base is None:
                continue
            disp = idc.get_operand_value(head, opn)
            if disp is None or disp < 0 or disp > MAX_OFFSET:
                continue
            sz = op_size(head, opn)
            is_write = (opn == 0)
            rec = fields.setdefault(disp, {"size": sz, "reads": 0, "writes": 0})
            rec["size"] = max(rec["size"], sz)
            if is_write:
                rec["writes"] += 1
            else:
                rec["reads"] += 1
            # vtable signature: call through [this+0] or [base+0]
            if disp == 0 and mnem.startswith("call"):
                vtable_hit[0] = True


def candidate_type(disp, rec, vtable):
    if disp == 0 and vtable:
        return "vtable* (virtual dispatch at +0)"
    sz = rec["size"]
    base = _SIZE_NAME.get(sz, "u%d" % (sz * 8))
    if sz == 4 and rec["reads"] and rec["writes"]:
        return "%s (pointer or 32-bit field)" % base
    return base


def main():
    fields = {}
    vtable_hit = [False]
    scanned = []
    for t in FUNCS:
        ea = resolve(t)
        if ea == idaapi.BADADDR:
            print("[struct_probe] could not resolve %r; skipping." % (t,))
            continue
        scanned.append((ea, ida_name.get_name(ea) or ("sub_%X" % ea)))
        scan_func(ea, set([THIS_REG.lower()]), fields, vtable_hit)

    if not fields:
        print("[struct_probe] no this-relative accesses found; check THIS_REG / FUNCS.")
        return

    size_lb = max(off + rec["size"] for off, rec in fields.items())
    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# struct_probe: %s (recovered offset table)" % STRUCT_NAME,
        "",
        "- generated: %s" % stamp,
        "- scanned_functions: " + ", ".join("%s@0x%X" % (n, e) for e, n in scanned),
        "- this_reg: %s   vtable_at_0: %s" % (THIS_REG, vtable_hit[0]),
        "- observed_size_lower_bound: 0x%X (%d) bytes" % (size_lb, size_lb),
        "",
        "> CANDIDATE layout inferred from access patterns — confirm before promotion. "
        "Offsets/sizes are hypotheses; a spec-author rewrites this into Docs/RE/structs/*.md.",
        "",
        "| Offset | Size | Reads | Writes | Candidate type |",
        "|---|---|---|---|---|",
    ]
    for off in sorted(fields):
        rec = fields[off]
        lines.append("| 0x%X | %d | %d | %d | %s |" % (
            off, rec["size"], rec["reads"], rec["writes"],
            candidate_type(off, rec, vtable_hit[0])))

    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", STRUCT_NAME)[:60] or "CObject"
        path = os.path.join(OUT_DIR, "%s.offsets.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[struct_probe] wrote %s" % path)
    except Exception as exc:
        print("\n[struct_probe] could not write file (%s); save the Markdown above via Write." % exc)


main()
