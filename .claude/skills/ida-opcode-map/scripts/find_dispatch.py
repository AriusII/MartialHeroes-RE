# find_dispatch.py — locate the packet dispatch table in the legacy Martial Heroes client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), typically pasted into the IDA MCP script-exec tool.
# Standalone of the repo: it only uses idautils/idaapi/ida_* APIs available in IDA.
#
# What it does:
#   1. Enumerates every compiler-emitted switch (idaapi.get_switch_info) AND scans
#      .rdata/.text for dense pointer arrays that look like hand-rolled handler tables.
#   2. Scores each candidate: case count, density, and proximity to the socket-recv path
#      (functions named/strings like recv, WSARecv, ProcessPacket, OnPacket, Dispatch).
#   3. Resolves every case/slot target to a function start + name.
#   4. Emits a Markdown report (header provenance + opcode|handler table) to the IDA
#      output window and, if possible, to Docs/RE/_dirty/opcodes.raw.md.
#
# DIRTY-ROOM OUTPUT ONLY. The promotion to the clean, address-free Docs/RE/opcodes.md is
# the opcode-catalog skill's job. Do not put this output anywhere committed.

import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_bytes
    import ida_name
    import ida_nalt
    import ida_segment
    import ida_xref
except ImportError as exc:  # not inside IDA
    raise SystemExit("find_dispatch.py must run inside IDA Pro (IDAPython): %s" % exc)

# ----------------------------------------------------------------------------------------
# CONFIG — tweak if the heuristics miss.
# ----------------------------------------------------------------------------------------
OUT_PATH = r"Docs\RE\_dirty\opcodes.raw.md"   # relative to the repo root / IDA cwd
MIN_CASES = 8                                  # ignore tiny switches
PTR_SCAN_MIN_RUN = 12                          # min consecutive code pointers for a hand-rolled table
# Names/strings that mark the network read/dispatch path. Lowercased substring match.
RECV_HINTS = (
    "recv", "wsarecv", "readpacket", "read_packet", "processpacket", "process_packet",
    "onpacket", "on_packet", "dispatch", "handlepacket", "handle_packet", "parsepacket",
    "packethandler", "netmsg", "msgproc", "cnetwork", "decrypt",
)

PTR_SIZE = 8 if idaapi.get_inf_structure().is_64bit() else 4


def _ea_name(ea):
    n = ida_name.get_name(ea)
    return n if n else ""


def _func_start(ea):
    f = ida_funcs.get_func(ea)
    return f.start_ea if f else idaapi.BADADDR


def _is_code_ptr(value):
    """True if `value` points at the start of (or inside) a known function."""
    if value == 0 or value == idaapi.BADADDR:
        return False
    seg = ida_segment.getseg(value)
    if seg is None:
        return False
    if not (seg.perm & ida_segment.SEGPERM_EXEC):
        return False
    return ida_funcs.get_func(value) is not None


def _read_ptr(ea):
    if PTR_SIZE == 8:
        return ida_bytes.get_qword(ea)
    return ida_bytes.get_dword(ea)


def _func_name_for_ea(ea):
    fs = _func_start(ea)
    if fs == idaapi.BADADDR:
        return ""
    n = _ea_name(fs)
    return n


def _near_recv_path(func_ea, depth=2):
    """Cheap reachability: is func_ea, or a near caller, named like the recv/dispatch path,
    or does it reference a recv-ish string/import? Returns a confidence bump 0..2."""
    if func_ea == idaapi.BADADDR:
        return 0
    score = 0
    name = _func_name_for_ea(func_ea).lower()
    if any(h in name for h in RECV_HINTS):
        score += 2
    # Walk a couple of caller levels looking for recv-ish names.
    frontier = {func_ea}
    seen = set()
    for _ in range(depth):
        nxt = set()
        for ea in frontier:
            if ea in seen:
                continue
            seen.add(ea)
            for xref in idautils.CodeRefsTo(ea, 0):
                cn = _func_name_for_ea(xref).lower()
                if any(h in cn for h in RECV_HINTS):
                    score += 1
                nxt.add(_func_start(xref))
        frontier = nxt
    return min(score, 3)


# ----------------------------------------------------------------------------------------
# Candidate collection
# ----------------------------------------------------------------------------------------
def collect_switch_candidates():
    """Compiler-emitted switches via get_switch_info."""
    out = []
    for func_ea in idautils.Functions():
        f = ida_funcs.get_func(func_ea)
        if not f:
            continue
        ea = f.start_ea
        while ea < f.end_ea and ea != idaapi.BADADDR:
            si = idaapi.get_switch_info(ea)
            if si and si.ncases >= MIN_CASES:
                cases = []
                try:
                    results = idaapi.calc_switch_cases(ea, si)
                    if results:
                        for i in range(len(results.targets)):
                            target = results.targets[i]
                            vals = list(results.cases[i])
                            for v in vals:
                                cases.append((v, target))
                except Exception:
                    # Fallback: enumerate jump table slots directly.
                    jt = si.jumps
                    for idx in range(si.get_jtable_size()):
                        tgt = _read_ptr(jt + idx * (si.get_jtable_element_size() or PTR_SIZE))
                        cases.append((si.lowcase + idx, tgt))
                if cases:
                    out.append({
                        "kind": "switch",
                        "dispatch_ea": func_ea,
                        "table_ea": si.jumps,
                        "base": si.lowcase,
                        "cases": cases,
                    })
            ea = idc.next_head(ea, f.end_ea)
    return out


def collect_ptr_table_candidates():
    """Hand-rolled function-pointer arrays in data/code segments."""
    out = []
    for seg_ea in idautils.Segments():
        seg = ida_segment.getseg(seg_ea)
        if seg is None:
            continue
        name = ida_segment.get_segm_name(seg) or ""
        if name not in (".rdata", ".data", ".text", "CONST", "_const"):
            continue
        ea = seg.start_ea
        run_start = None
        run = []
        while ea < seg.end_ea:
            val = _read_ptr(ea)
            if _is_code_ptr(val):
                if run_start is None:
                    run_start = ea
                run.append(val)
            else:
                if run_start is not None and len(run) >= PTR_SCAN_MIN_RUN:
                    out.append({
                        "kind": "ptr_table",
                        "dispatch_ea": idaapi.BADADDR,  # filled from xref below
                        "table_ea": run_start,
                        "base": 0,
                        "cases": [(i, run[i]) for i in range(len(run))],
                    })
                run_start = None
                run = []
            ea += PTR_SIZE
        if run_start is not None and len(run) >= PTR_SCAN_MIN_RUN:
            out.append({
                "kind": "ptr_table",
                "dispatch_ea": idaapi.BADADDR,
                "table_ea": run_start,
                "base": 0,
                "cases": [(i, run[i]) for i in range(len(run))],
            })
    # Attribute each ptr table to the function that references its base.
    for cand in out:
        for xref in idautils.DataRefsTo(cand["table_ea"]):
            fs = _func_start(xref)
            if fs != idaapi.BADADDR:
                cand["dispatch_ea"] = fs
                break
    return out


def score_candidate(cand):
    ncases = len(cand["cases"])
    distinct_targets = len({t for _, t in cand["cases"] if t != idaapi.BADADDR})
    recv = _near_recv_path(cand["dispatch_ea"])
    # Prefer big tables, many distinct handlers, near the recv path.
    return ncases + distinct_targets + recv * 10


# ----------------------------------------------------------------------------------------
# Report
# ----------------------------------------------------------------------------------------
def render(cand, rank):
    lines = []
    disp = cand["dispatch_ea"]
    disp_name = _ea_name(disp) if disp != idaapi.BADADDR else "(unknown)"
    width = "2 bytes" if cand["base"] > 0xFF or len(cand["cases"]) > 0x100 else "1 byte (assumed)"
    lines.append("## Candidate #%d (%s)  score=%d" % (rank, cand["kind"], score_candidate(cand)))
    lines.append("")
    lines.append("- table_ea: `0x%X`" % cand["table_ea"])
    lines.append("- dispatch_func: `0x%X` %s" % (disp, disp_name) if disp != idaapi.BADADDR
                 else "- dispatch_func: (unresolved)")
    lines.append("- opcode_base: `0x%X`" % cand["base"])
    lines.append("- opcode_width: %s" % width)
    lines.append("- case_count: %d" % len(cand["cases"]))
    lines.append("- recv_path_confidence: %d/3" % _near_recv_path(cand["dispatch_ea"]))
    lines.append("")
    lines.append("| Opcode | Handler EA | Handler Name |")
    lines.append("|---|---|---|")
    seen = set()
    for op, tgt in sorted(cand["cases"], key=lambda c: c[0]):
        if tgt == idaapi.BADADDR:
            hname = "(no target)"
            hea = "-"
        else:
            fs = _func_start(tgt)
            hea = "0x%X" % (fs if fs != idaapi.BADADDR else tgt)
            hname = _func_name_for_ea(tgt) or "sub_%X" % (fs if fs != idaapi.BADADDR else tgt)
        key = (op, hea)
        if key in seen:
            continue
        seen.add(key)
        lines.append("| 0x%02X | %s | %s |" % (op, hea, hname))
    lines.append("")
    return "\n".join(lines)


def main():
    print("[find_dispatch] scanning for switch/jump candidates...")
    candidates = collect_switch_candidates() + collect_ptr_table_candidates()
    if not candidates:
        print("[find_dispatch] NO dispatch-like tables found. "
              "Check that auto-analysis finished, or use the typed-tool fallback.")
        return
    candidates.sort(key=score_candidate, reverse=True)
    top = candidates[:5]

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    inp = ida_nalt.get_root_filename() or "(unknown binary)"
    header = [
        "# opcodes.raw.md — DIRTY-ROOM raw opcode -> handler map (gitignored)",
        "",
        "> Generated by skill `ida-opcode-map` (scripts/find_dispatch.py).",
        "> Contains IDA addresses: NEVER promote verbatim. The clean, address-free catalog",
        "> Docs/RE/opcodes.md is produced separately by the opcode-catalog skill.",
        "",
        "- binary: `%s`" % inp,
        "- generated: %s" % stamp,
        "- candidates_found: %d (showing top %d)" % (len(candidates), len(top)),
        "- partial: set true by the analyst if this is a sub-dispatcher, not the master table",
        "",
        "The highest-scoring candidate near the recv/decrypt path is most likely the master",
        "packet dispatcher. Lower-ranked tables may be category sub-dispatchers (e.g. a chat",
        "or trade secondary switch).",
        "",
        "---",
        "",
    ]
    body = [render(c, i + 1) for i, c in enumerate(top)]
    report = "\n".join(header) + "\n".join(body)

    print(report)

    # Best-effort direct write; if it fails, the analyst saves the printed text via Write.
    try:
        import os
        os.makedirs(os.path.dirname(OUT_PATH), exist_ok=True)
        with open(OUT_PATH, "a", encoding="utf-8") as fh:
            fh.write("\n\n<!-- run %s -->\n\n" % stamp)
            fh.write(report)
        print("\n[find_dispatch] appended report to %s" % OUT_PATH)
    except Exception as exc:
        print("\n[find_dispatch] could not write %s (%s). "
              "Copy the Markdown above and save it with the Write tool." % (OUT_PATH, exc))


main()
