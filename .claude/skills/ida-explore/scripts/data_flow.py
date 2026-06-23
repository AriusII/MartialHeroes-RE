# data_flow.py — bounded INTRA-FUNCTION def/use trace for a register value in the legacy
# Martial Heroes client (Main.exe). Fallback for when typed mcp__ida__trace_data_flow is absent.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool.
#
# SCOPE & HONESTY: this is a CONSERVATIVE, single-function walk. It does NOT model memory aliasing
# or follow values across calls/function boundaries — it STOPS there and says so. Prefer the typed
# mcp__ida__trace_data_flow tool for real interprocedural flow; use this only as a fallback.
#
# DIRTY: addresses derived from the copyrighted binary; output belongs ONLY under
# Docs/RE/_dirty/static/. Never commit; never copy into clean specs or C#. This emits a step
# table (EA + mnemonic + what happens to the value), NOT a transcribed function body.

# === CONFIG ===
START_EA = 0x004A1230   # an instruction inside the function of interest
DIRECTION = "forward"   # "forward" (uses downstream) | "backward" (defs upstream)
REG = "eax"             # register to follow (lowercase, e.g. eax/ebx/ecx/edx/esi/edi)
MAX_STEPS = 64          # bound the walk
OUT_DIR = r"Docs\RE\_dirty\static"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_ua
    import ida_bytes
    import ida_name
except ImportError as exc:
    raise SystemExit("data_flow.py must run inside IDA Pro (IDAPython): %s" % exc)


# Coarse register-family map so partial writes (al/ax/eax) count as touching the same value.
_FAMILIES = {
    "eax": {"eax", "ax", "al", "ah"}, "ebx": {"ebx", "bx", "bl", "bh"},
    "ecx": {"ecx", "cx", "cl", "ch"}, "edx": {"edx", "dx", "dl", "dh"},
    "esi": {"esi", "si"}, "edi": {"edi", "di"}, "ebp": {"ebp", "bp"}, "esp": {"esp", "sp"},
}


def family(reg):
    reg = reg.lower()
    for canon, members in _FAMILIES.items():
        if reg in members:
            return canon, members
    return reg, {reg}


def disasm(ea):
    return idc.generate_disasm_line(ea, idc.GENDSM_FORCE_CODE) or ""


def operands_touching(ea, members):
    """Return (writes, reads) booleans for whether the instruction at ea writes/reads the reg family."""
    writes = reads = False
    for opn in range(8):
        otype = idc.get_operand_type(ea, opn)
        if otype == idc.o_void:
            break
        if otype == idc.o_reg:
            rn = idc.print_operand(ea, opn).strip().lower()
            if rn in members:
                # Operand 0 of most x86 instrs is the destination; treat as write, others as read.
                if opn == 0:
                    writes = True
                else:
                    reads = True
    return writes, reads


def is_call(ea):
    return idc.print_insn_mnem(ea).lower().startswith("call")


def main():
    f = ida_funcs.get_func(START_EA)
    if not f:
        print("[data_flow] START_EA 0x%X is not inside a function." % START_EA)
        return
    canon, members = family(REG)
    fname = ida_name.get_name(f.start_ea) or ("sub_%X" % f.start_ea)

    heads = [h for h in idautils.Heads(f.start_ea, f.end_ea)
             if ida_bytes.is_code(ida_bytes.get_flags(h))]
    if START_EA not in heads:
        # snap to nearest head
        heads_le = [h for h in heads if h <= START_EA]
        start = heads_le[-1] if heads_le else f.start_ea
    else:
        start = START_EA
    idx = heads.index(start) if start in heads else 0

    steps = []
    note = None
    seq = heads[idx + 1:] if DIRECTION == "forward" else list(reversed(heads[:idx]))
    for ea in seq:
        if len(steps) >= MAX_STEPS:
            note = "stopped at MAX_STEPS"
            break
        writes, reads = operands_touching(ea, members)
        mnem = idc.print_insn_mnem(ea).lower()
        if is_call(ea):
            # The followed value may be passed/clobbered by the call — boundary.
            note = "reached a call at 0x%X (intra-function trace stops here)" % ea
            steps.append((ea, mnem, "call boundary", disasm(ea)))
            break
        if DIRECTION == "forward":
            if reads:
                steps.append((ea, mnem, "use", disasm(ea)))
            if writes and not reads:
                # value redefined without using it -> original value dies here
                steps.append((ea, mnem, "redefined (value dies)", disasm(ea)))
                note = "followed value overwritten at 0x%X" % ea
                break
        else:  # backward
            if writes:
                steps.append((ea, mnem, "def", disasm(ea)))
                note = "nearest definition found at 0x%X" % ea
                break
            if reads:
                steps.append((ea, mnem, "use (above start)", disasm(ea)))

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# data_flow: %s in %s" % (canon, fname),
        "",
        "- function: `%s`  (ea `0x%X`)" % (fname, f.start_ea),
        "- start_ea: `0x%X`   direction: %s   reg: %s" % (start, DIRECTION, canon),
        "- generated: %s" % stamp,
        "- steps: %d%s" % (len(steps), ("   note: %s" % note) if note else ""),
        "",
        "> INTRA-FUNCTION trace. Memory/struct stores and cross-call flow are NOT followed — "
        "boundaries are flagged. Use mcp__ida__trace_data_flow for interprocedural flow.",
        "",
        "| EA | Mnem | Role | Disasm |",
        "|---|---|---|---|",
    ]
    for ea, mnem, role, text in steps:
        safe = text.replace("|", r"\|")
        lines.append("| 0x%X | %s | %s | `%s` |" % (ea, mnem, role, safe))
    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", "%s_%s_%X" % (fname, canon, start))[:60]
        path = os.path.join(OUT_DIR, "dataflow.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[data_flow] wrote %s" % path)
    except Exception as exc:
        print("\n[data_flow] could not write file (%s); save the Markdown above via Write." % exc)


main()
