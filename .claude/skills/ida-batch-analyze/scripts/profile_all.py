# profile_all.py — full-database READ-ONLY function profiler for the legacy Martial Heroes client.
# Paginates over all unnamed sub_* functions, emits one JSONL record per function.
# Output: Docs/RE/_dirty/campaign6/profile/functions.<offset>-<end>.jsonl
#         Docs/RE/_dirty/campaign6/profile/profile.summary.json
#
# RUN INSIDE IDA PRO 9.3 (IDAPython) via mcp__ida__py_exec_file.
# DIRTY: derived from copyrighted binary — output goes ONLY under Docs/RE/_dirty/.
# This script is READ-ONLY: it never calls set_name/rename/patch/set_type.

# === CONFIG ===
PAGE_OFFSET  = 0          # skip first N functions (0-based index into the full func list)
PAGE_SIZE    = 200        # number of functions to process in this run (use 200 for validation, ~2000 for sweep)
INCLUDE_NAMED = False     # False = skip already-named functions (sub_* only)
OUT_DIR      = r"Docs\RE\_dirty\campaign6\profile"
MAX_STRINGS  = 8          # max string snippets per function
MAX_IMPORTS  = 8          # max import names per function
MAX_STR_LEN  = 48         # max chars per string snippet
WRITE_SUMMARY = True      # write/update the summary JSON at the end (set False for partial pages)
# ==============

import json
import os
import re
import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_bytes
    import ida_gdl
    import ida_xref
    import ida_frame
    import ida_nalt
except ImportError as exc:
    raise SystemExit("profile_all.py must run inside IDA Pro (IDAPython): %s" % exc)


# ── runtime / library guard (verbatim from names_sync.py) ─────────────────────
_RUNTIME_PREFIXES = ("__", "_imp_", "j_", "_RTC_", "_CxxThrow", "__security_",
                     "_acmdln", "_wcmdln", "std::", "__scrt", "__crt")
_RUNTIME_NAMES = {"mainCRTStartup", "wmainCRTStartup", "WinMainCRTStartup",
                  "_initterm", "_initterm_e", "atexit", "__report_gsfailure"}


def _is_runtime(name, ea):
    if not name:
        return False
    if name in _RUNTIME_NAMES:
        return True
    for p in _RUNTIME_PREFIXES:
        if name.startswith(p):
            return True
    if name.startswith("?"):
        return True
    f = ida_funcs.get_func(ea)
    if f is not None and (f.flags & ida_funcs.FUNC_LIB):
        return True
    return False


def _is_default_name(name):
    if not name:
        return True
    for pfx in ("sub_", "loc_", "locret_", "off_", "dword_", "word_", "byte_",
                "unk_", "asc_", "stru_", "flt_", "dbl_", "tbyte_", "jpt_", "algn_",
                "nullsub_", "j_"):
        if name.startswith(pfx):
            return True
    return False


# ── import name cache ──────────────────────────────────────────────────────────
def _build_import_cache():
    """Map ea -> import name for every thunk/import entry in the IDB."""
    cache = {}

    def imp_cb(ea, name, ordinal):
        if name:
            cache[ea] = name
        return True

    nimods = idaapi.get_import_module_qty()
    for i in range(nimods):
        idaapi.enum_import_names(i, imp_cb)

    # also collect __imp_ stubs in the names table
    for ea, nm in idautils.Names():
        if nm.startswith("__imp_"):
            clean = nm[6:]
            cache[ea] = clean
        elif nm.startswith("imp_"):
            cache[ea] = nm[4:]
    return cache


# ── vtable membership cache ────────────────────────────────────────────────────
def _build_vtable_cache():
    """
    Returns (vtable_funcs, vtable_map):
      vtable_funcs: set of function EAs that appear as vtable slots
      vtable_map: ea -> (vtable_ea, slot_index)
    Heuristic: scan .rdata for runs of pointer-sized code pointers (MSVC vtable pattern).
    """
    PTR = 4  # 32-bit image
    vtable_funcs = {}  # ea -> (vtable_ea, slot_idx)

    rdata_segs = [s for s in idautils.Segments()
                  if (idc.get_segm_name(s) or "").lower() in (".rdata", "rdata", ".rodata")]

    for seg_start in rdata_segs:
        seg_end = idc.get_segm_end(seg_start)
        ea = seg_start
        while ea + PTR <= seg_end:
            # Look for a run of at least 2 valid func-start pointers
            run_start = ea
            slot = 0
            while ea + PTR <= seg_end:
                ptr_val = idc.get_wide_dword(ea)
                if not ida_bytes.is_loaded(ptr_val):
                    break
                f = ida_funcs.get_func(ptr_val)
                if not (f and f.start_ea == ptr_val):
                    break
                slot += 1
                ea += PTR
            if slot >= 2:
                # record each slot
                cur = run_start
                for i in range(slot):
                    fptr = idc.get_wide_dword(cur)
                    if fptr not in vtable_funcs:
                        vtable_funcs[fptr] = (run_start, i)
                    cur += PTR
            else:
                ea = run_start + PTR  # advance by one pointer
    return vtable_funcs


# ── SEH detection ──────────────────────────────────────────────────────────────
def _has_seh(func):
    """Detect SEH: presence of __except_handler3/4 call or fs:[0] references."""
    for head in idautils.Heads(func.start_ea, func.end_ea):
        if not ida_bytes.is_code(ida_bytes.get_flags(head)):
            continue
        mnem = idc.print_insn_mnem(head).lower()
        # fs:[0] is the SEH chain head
        disasm = idc.generate_disasm_line(head, 0) or ""
        if "fs:[0]" in disasm or "fs:0" in disasm:
            return True
        for xr in idautils.XrefsFrom(head, 0):
            nm = ida_name.get_name(xr.to) or ""
            if "except_handler" in nm.lower() or "__C_specific_handler" in nm:
                return True
    return False


# ── klass heuristic ───────────────────────────────────────────────────────────
def _klass(size, bb_count, insn_count, out_degree, loop_count):
    # thunk: tiny (<=16 bytes), single jmp/call+ret, 1-2 instructions
    if size <= 16 and insn_count <= 4 and out_degree <= 1:
        return "thunk"
    # leaf: no callees
    if out_degree == 0:
        return "leaf"
    # dispatcher: large fan-out or switch
    if out_degree >= 8 or (loop_count == 0 and out_degree >= 5 and bb_count >= 6):
        return "dispatcher"
    # wrapper: small, exactly one real callee
    if insn_count <= 20 and out_degree == 1:
        return "wrapper"
    return "complex"


# ── per-function metrics ───────────────────────────────────────────────────────
def _profile(ea, import_cache, vtable_cache):
    f = ida_funcs.get_func(ea)
    if not f:
        return None

    size = f.end_ea - f.start_ea
    insn_count = 0
    loop_count = 0
    import_calls = []
    string_refs = []
    callees = set()
    has_seh = False

    # bb/loop via FlowChart
    try:
        fc = ida_gdl.FlowChart(f)
        blocks = list(fc)
        bb_count = len(blocks)
        for b in blocks:
            for succ in b.succs():
                if succ.start_ea <= b.start_ea:
                    loop_count += 1
    except Exception:
        bb_count = 0

    imp_set = set()
    str_set = set()

    for head in idautils.Heads(f.start_ea, f.end_ea):
        if not ida_bytes.is_code(ida_bytes.get_flags(head)):
            continue
        insn_count += 1
        # SEH check via fs:[0]
        if not has_seh:
            disasm = idc.generate_disasm_line(head, 0) or ""
            if "fs:[0]" in disasm or "fs:0" in disasm:
                has_seh = True

        for xr in idautils.XrefsFrom(head, 0):
            if xr.type in (ida_xref.fl_CN, ida_xref.fl_CF):
                # call xref
                target = xr.to
                # resolve through jmp thunk
                cf = ida_funcs.get_func(target)
                if cf:
                    callees.add(cf.start_ea)
                    # direct import?
                if target in import_cache:
                    imp_set.add(import_cache[target])
                else:
                    nm = ida_name.get_name(target) or ""
                    if nm.startswith("__imp_"):
                        imp_set.add(nm[6:])
                    elif cf:
                        # check if the called function is itself a single-jmp to import
                        cfnm = ida_name.get_name(cf.start_ea) or ""
                        if cfnm.startswith("__imp_"):
                            imp_set.add(cfnm[6:])
                        elif cf.start_ea in import_cache:
                            imp_set.add(import_cache[cf.start_ea])
            elif xr.type in (ida_xref.dr_R, ida_xref.dr_W, ida_xref.dr_O):
                # data ref: string?
                if ida_bytes.is_strlit(ida_bytes.get_flags(xr.to)):
                    raw = idc.get_strlit_contents(xr.to, -1, idc.STRTYPE_C)
                    if raw:
                        try:
                            s = raw.decode("utf-8", "replace")[:MAX_STR_LEN]
                        except Exception:
                            s = repr(raw)[:MAX_STR_LEN]
                        str_set.add(s)

    # in-degree (callers)
    in_degree = sum(1 for x in idautils.XrefsTo(ea, 0)
                    if x.type in (ida_xref.fl_CN, ida_xref.fl_CF))

    out_degree = len(callees)

    import_calls = sorted(imp_set)[:MAX_IMPORTS]
    string_refs  = sorted(str_set)[:MAX_STRINGS]

    # vtable membership
    vt_info = vtable_cache.get(ea)
    is_vtable = vt_info is not None
    vtable_ea_hex = ("0x%08X" % vt_info[0]) if vt_info else None
    vtable_slot   = vt_info[1] if vt_info else None

    # stack frame size
    try:
        fr = ida_frame.get_frame(ea)
        frame_size = idc.get_struc_size(fr.id) if fr else 0
    except Exception:
        frame_size = 0

    # arg bytes (purged args)
    arg_bytes = getattr(f, "argsize", 0)

    # import-category flags
    imp_lower = set(s.lower() for s in import_calls)
    alloc_kws   = {"malloc","calloc","realloc","new","globalalloc","localalloc","heapalloc","virtualalloc"}
    free_kws    = {"free","delete","globalfree","localfree","heapfree","virtualfree"}
    string_kws  = {"strlen","strcpy","strcat","strcmp","sprintf","printf","lstrcpy","lstrcmp","wcslen","wsprintf","strncpy","strncat","strncmp"}
    socket_kws  = {"socket","connect","send","recv","wsasend","wsarecv","closesocket","accept","bind","listen","select","wsagetlasterror","wsastartup"}
    crypto_kws  = {"cryptacquirecontext","cryptcreatehash","crypthashdata","cryptderivekey","cryptencrypt","cryptdecrypt","md5","sha1","sha256"}
    file_kws    = {"createfile","readfile","writefile","closefile","getfilesize","setfilepointer","findfile","openfile","fopen","fread","fwrite","fclose"}

    flags = {
        "calls_alloc":   bool(imp_lower & alloc_kws),
        "calls_free":    bool(imp_lower & free_kws),
        "calls_string":  bool(imp_lower & string_kws),
        "calls_socket":  bool(imp_lower & socket_kws),
        "calls_crypto":  bool(imp_lower & crypto_kws),
        "calls_file":    bool(imp_lower & file_kws),
    }

    klass = _klass(size, bb_count, insn_count, out_degree, loop_count)

    return {
        "ea":              "0x%08X" % ea,
        "name":            ida_name.get_name(ea) or ("sub_%X" % ea),
        "size":            size,
        "bb_count":        bb_count,
        "insn_count":      insn_count,
        "in_degree":       in_degree,
        "out_degree":      out_degree,
        "import_calls":    import_calls,
        "string_refs":     string_refs,
        "is_vtable_member": is_vtable,
        "vtable_ea":       vtable_ea_hex,
        "vtable_slot":     vtable_slot,
        "rtti_class":      None,
        "has_seh":         has_seh,
        "loop_count":      loop_count,
        "stack_frame_size": frame_size,
        "arg_bytes":       arg_bytes,
        "flags":           flags,
        "klass":           klass,
    }


# ── main ───────────────────────────────────────────────────────────────────────
def main():
    print("[profile_all] building import cache...")
    import_cache = _build_import_cache()
    print("[profile_all] import cache: %d entries" % len(import_cache))

    print("[profile_all] building vtable cache...")
    vtable_cache = _build_vtable_cache()
    print("[profile_all] vtable cache: %d func entries" % len(vtable_cache))

    # gather target function list
    print("[profile_all] enumerating functions...")
    all_eas = []
    for ea in idautils.Functions():
        nm = ida_name.get_name(ea) or ("sub_%X" % ea)
        if not INCLUDE_NAMED and not _is_default_name(nm):
            continue  # skip named functions
        if _is_runtime(nm, ea):
            continue  # skip library/runtime
        all_eas.append(ea)

    total_eligible = len(all_eas)
    page_eas = all_eas[PAGE_OFFSET : PAGE_OFFSET + PAGE_SIZE]
    end_idx  = PAGE_OFFSET + len(page_eas)

    print("[profile_all] total eligible: %d; this page: [%d..%d) = %d funcs" % (
        total_eligible, PAGE_OFFSET, end_idx, len(page_eas)))

    os.makedirs(OUT_DIR, exist_ok=True)

    records = []
    for i, ea in enumerate(page_eas):
        if i % 50 == 0:
            print("[profile_all] profiling %d/%d..." % (i, len(page_eas)))
        try:
            rec = _profile(ea, import_cache, vtable_cache)
            if rec:
                records.append(rec)
        except Exception as exc:
            records.append({"ea": "0x%08X" % ea, "error": str(exc), "klass": "error"})

    # write JSONL shard
    shard_name = "functions.%d-%d.jsonl" % (PAGE_OFFSET, end_idx)
    shard_path = os.path.join(OUT_DIR, shard_name)
    with open(shard_path, "w", encoding="utf-8") as fh:
        for rec in records:
            fh.write(json.dumps(rec, ensure_ascii=False) + "\n")
    print("[profile_all] wrote shard: %s (%d records)" % (shard_path, len(records)))

    # emit one machine-readable summary line
    klass_hist = {}
    flag_counts = {k: 0 for k in ("calls_alloc","calls_free","calls_string","calls_socket","calls_crypto","calls_file")}
    api_counts  = {}
    str_count   = 0
    vtable_count = 0
    rtti_count   = 0
    for rec in records:
        klass = rec.get("klass","?")
        klass_hist[klass] = klass_hist.get(klass, 0) + 1
        for k in flag_counts:
            if rec.get("flags",{}).get(k):
                flag_counts[k] += 1
        for api in rec.get("import_calls",[]):
            api_counts[api] = api_counts.get(api, 0) + 1
        if rec.get("string_refs"):
            str_count += 1
        if rec.get("is_vtable_member"):
            vtable_count += 1
        if rec.get("rtti_class"):
            rtti_count += 1

    top_apis = sorted(api_counts.items(), key=lambda x: -x[1])[:60]

    page_summary = {
        "schema":         "profile_all/1",
        "generated":      datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "binary":         idaapi.get_root_filename() or "",
        "total_eligible": total_eligible,
        "page_offset":    PAGE_OFFSET,
        "page_end":       end_idx,
        "total_profiled": len(records),
        "klass_histogram": klass_hist,
        "flag_counts":    flag_counts,
        "top_apis":       top_apis,
        "with_strings":   str_count,
        "with_vtable":    vtable_count,
        "with_rtti":      rtti_count,
        "shard_file":     shard_path,
    }

    if WRITE_SUMMARY:
        summary_path = os.path.join(OUT_DIR, "profile.summary.json")
        # merge with existing if present (accumulate across pages)
        merged = page_summary.copy()
        if os.path.exists(summary_path):
            try:
                with open(summary_path, "r", encoding="utf-8") as fh:
                    existing = json.load(fh)
                # merge: accumulate klass/flag/api counts, keep total_eligible from first page
                for k, v in existing.get("klass_histogram",{}).items():
                    merged["klass_histogram"][k] = merged["klass_histogram"].get(k,0) + v
                for k in flag_counts:
                    merged["flag_counts"][k] += existing.get("flag_counts",{}).get(k,0)
                # merge api counts
                merged_apis = dict(top_apis)
                for nm, cnt in existing.get("top_apis",[]):
                    merged_apis[nm] = merged_apis.get(nm,0) + cnt
                merged["top_apis"] = sorted(merged_apis.items(), key=lambda x: -x[1])[:60]
                merged["total_profiled"] += existing.get("total_profiled",0)
                merged["with_strings"]   += existing.get("with_strings",0)
                merged["with_vtable"]    += existing.get("with_vtable",0)
                merged["with_rtti"]      += existing.get("with_rtti",0)
                merged["total_eligible"]  = existing.get("total_eligible", total_eligible)
                existing_shards = existing.get("shard_files", [])
                if not isinstance(existing_shards, list):
                    existing_shards = [existing_shards]
                merged["shard_files"] = existing_shards + [shard_path]
                del merged["shard_file"]
            except Exception:
                merged["shard_files"] = [shard_path]
                if "shard_file" in merged:
                    del merged["shard_file"]
        else:
            merged["shard_files"] = [shard_path]
            if "shard_file" in merged:
                del merged["shard_file"]

        with open(summary_path, "w", encoding="utf-8") as fh:
            json.dump(merged, fh, ensure_ascii=False, indent=2)
        print("[profile_all] wrote summary: %s" % summary_path)

    print("PROFILE_JSON:" + json.dumps(page_summary, ensure_ascii=False))


main()
