# vtable_recover.py — walk a C++ vtable in the legacy Martial Heroes client (Main.exe) and emit
# a NEUTRAL slot table: index, slot EA, target function EA/name, in-degree, heuristic role.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool.
#
# DIRTY: addresses derived from the copyrighted binary; output belongs ONLY under
# Docs/RE/_dirty/structs/. Never commit; never copy into clean specs or C#. The artifact is a SLOT
# TABLE (index/target/role), NOT function bodies — never emit disassembly or pseudo-C.

# === CONFIG ===
# EITHER give the vtable's data EA directly ...
VTABLE_EA = 0x006C1200
# ... OR set VTABLE_EA = 0 and give the constructor (name or hex addr) to auto-find the table.
CTOR = ""
CLASS_NAME = "CObject"
MAX_SLOTS = 128
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
    raise SystemExit("vtable_recover.py must run inside IDA Pro (IDAPython): %s" % exc)


PTR = 8 if idaapi.inf_is_64bit() else 4


def get_ptr(ea):
    return idc.get_qword(ea) if PTR == 8 else idc.get_wide_dword(ea)


def resolve(t):
    if isinstance(t, int):
        return t
    ea = ida_name.get_name_ea(idaapi.BADADDR, t)
    if ea != idaapi.BADADDR:
        return ea
    matches = [e for e, n in idautils.Names() if t.lower() in n.lower()]
    return matches[0] if matches else idaapi.BADADDR


def find_vtable_from_ctor(ctor_ea):
    """Heuristic: the first pointer-sized immediate stored into [this+0] near the top of the ctor."""
    f = ida_funcs.get_func(ctor_ea)
    if not f:
        return idaapi.BADADDR
    count = 0
    for head in idautils.Heads(f.start_ea, f.end_ea):
        count += 1
        if count > 40:
            break
        if not ida_bytes.is_code(ida_bytes.get_flags(head)):
            continue
        mnem = idc.print_insn_mnem(head).lower()
        if mnem != "mov":
            continue
        # dst must be a [base+0] memory operand, src an immediate that points into a data segment
        if idc.get_operand_type(head, 0) in (idc.o_phrase, idc.o_displ) and \
           idc.get_operand_type(head, 1) == idc.o_imm:
            disp = idc.get_operand_value(head, 0)
            if disp in (0, idaapi.BADADDR):  # [base] or [base+0]
                cand = idc.get_operand_value(head, 1)
                if ida_bytes.is_loaded(cand):
                    return cand
    return idaapi.BADADDR


def is_func_start(ea):
    f = ida_funcs.get_func(ea)
    return bool(f and f.start_ea == ea)


def in_degree(ea):
    return sum(1 for _ in idautils.XrefsTo(ea, 0))


def role_tag(target_ea, idx, name):
    low = (name or "").lower()
    if "destructor" in low or low.startswith("??1") or "~" in low or "vector_deleting" in low:
        return "destructor"
    if idx == 0 and ("ctor" in low or "??0" in low):
        return "constructor"
    f = ida_funcs.get_func(target_ea)
    if f and (f.end_ea - f.start_ea) < 0x18:
        return "getter / thunk (tiny)"
    return "virtual method"


def main():
    vt = VTABLE_EA
    if not vt and CTOR:
        ce = resolve(CTOR)
        if ce != idaapi.BADADDR:
            vt = find_vtable_from_ctor(ce)
    if not vt or not ida_bytes.is_loaded(vt):
        print("[vtable_recover] could not determine VTABLE_EA (set it, or give a valid CTOR).")
        return

    slots = []
    ea = vt
    for idx in range(MAX_SLOTS):
        # stop if a new symbol/xref starts here after slot 0 (likely the next vtable or data)
        if idx > 0:
            nm = ida_name.get_name(ea)
            if nm and not nm.startswith("off_") and any(True for _ in idautils.XrefsTo(ea, 0)):
                break
        target = get_ptr(ea)
        if not ida_bytes.is_loaded(target) or not is_func_start(target):
            break
        tname = ida_name.get_name(target) or ("sub_%X" % target)
        slots.append((idx, ea, target, tname, in_degree(target),
                      role_tag(target, idx, tname)))
        ea += PTR

    if not slots:
        print("[vtable_recover] no function-pointer slots read at 0x%X; check VTABLE_EA." % vt)
        return

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# vtable_recover: %s" % CLASS_NAME,
        "",
        "- vtable_ea: `0x%X`   ptr_size: %d   slots: %d" % (vt, PTR, len(slots)),
        "- generated: %s" % stamp,
        "",
        "> Slot roles are HEURISTIC hypotheses — confirm important slots via ida-explore (DECOMPILE-ONE mode) "
        "before a spec-author promotes this into Docs/RE/structs/*.md.",
        "",
        "| Slot | Slot EA | Target EA | Target Name | In-deg | Role (HYPOTHESIS) |",
        "|---|---|---|---|---|---|",
    ]
    for idx, sea, tea, tname, indeg, role in slots:
        lines.append("| %d | 0x%X | 0x%X | %s | %d | %s |" % (idx, sea, tea, tname, indeg, role))

    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", CLASS_NAME)[:60] or "CObject"
        path = os.path.join(OUT_DIR, "%s.vtable.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[vtable_recover] wrote %s" % path)
    except Exception as exc:
        print("\n[vtable_recover] could not write file (%s); save the Markdown above via Write." % exc)


main()
