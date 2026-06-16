# profile_all_sweep.py — runs the full profile sweep across all pages sequentially.
# Meant to be exec'd inside IDA once; handles all pages from START_OFFSET to end.
# Each page writes its JSONL shard and merges into profile.summary.json.
#
# === CONFIG ===
START_OFFSET = 2200   # resume from this offset (already profiled 0..2200)
PAGE_SIZE    = 2000
INCLUDE_NAMED = False
OUT_DIR      = r"Docs\RE\_dirty\campaign6\profile"
MAX_STRINGS  = 8
MAX_IMPORTS  = 8
MAX_STR_LEN  = 48
# ==============

import json, os, re, datetime

try:
    import idaapi, idautils, idc, ida_funcs, ida_name, ida_bytes
    import ida_gdl, ida_xref, ida_frame, ida_nalt
except ImportError as exc:
    raise SystemExit(str(exc))

_RUNTIME_PREFIXES = ("__","_imp_","j_","_RTC_","_CxxThrow","__security_","_acmdln","_wcmdln","std::","__scrt","__crt")
_RUNTIME_NAMES = {"mainCRTStartup","wmainCRTStartup","WinMainCRTStartup","_initterm","_initterm_e","atexit","__report_gsfailure"}

def _is_runtime(name, ea):
    if not name: return False
    if name in _RUNTIME_NAMES: return True
    for p in _RUNTIME_PREFIXES:
        if name.startswith(p): return True
    if name.startswith("?"): return True
    f = ida_funcs.get_func(ea)
    return bool(f and (f.flags & ida_funcs.FUNC_LIB))

def _is_default_name(name):
    if not name: return True
    for pfx in ("sub_","loc_","locret_","off_","dword_","word_","byte_","unk_","asc_","stru_","flt_","dbl_","tbyte_","jpt_","algn_","nullsub_","j_"):
        if name.startswith(pfx): return True
    return False

def _build_import_cache():
    cache = {}
    def cb(ea, name, ordinal):
        if name: cache[ea] = name
        return True
    for i in range(idaapi.get_import_module_qty()):
        idaapi.enum_import_names(i, cb)
    for ea, nm in idautils.Names():
        if nm.startswith("__imp_"): cache[ea] = nm[6:]
        elif nm.startswith("imp_"): cache[ea] = nm[4:]
    return cache

def _build_vtable_cache():
    PTR = 4
    vtable_funcs = {}
    for seg_start in idautils.Segments():
        if (idc.get_segm_name(seg_start) or "").lower() not in (".rdata","rdata",".rodata"):
            continue
        seg_end = idc.get_segm_end(seg_start)
        ea = seg_start
        while ea + PTR <= seg_end:
            run_start = ea; slot = 0
            while ea + PTR <= seg_end:
                pv = idc.get_wide_dword(ea)
                if not ida_bytes.is_loaded(pv): break
                f = ida_funcs.get_func(pv)
                if not (f and f.start_ea == pv): break
                slot += 1; ea += PTR
            if slot >= 2:
                cur = run_start
                for i in range(slot):
                    fp = idc.get_wide_dword(cur)
                    if fp not in vtable_funcs: vtable_funcs[fp] = (run_start, i)
                    cur += PTR
            else:
                ea = run_start + PTR
    return vtable_funcs

def _klass(size, bb, insn, out, loops):
    if size <= 16 and insn <= 4 and out <= 1: return "thunk"
    if out == 0: return "leaf"
    if out >= 8 or (loops == 0 and out >= 5 and bb >= 6): return "dispatcher"
    if insn <= 20 and out == 1: return "wrapper"
    return "complex"

def _profile(ea, ic, vc):
    f = ida_funcs.get_func(ea)
    if not f: return None
    size = f.end_ea - f.start_ea
    insn = loops = 0; bb = 0
    imp_set = set(); str_set = set(); callees = set()
    has_seh = False
    try:
        fc = ida_gdl.FlowChart(f); blocks = list(fc); bb = len(blocks)
        for b in blocks:
            for s in b.succs():
                if s.start_ea <= b.start_ea: loops += 1
    except: pass
    for head in idautils.Heads(f.start_ea, f.end_ea):
        if not ida_bytes.is_code(ida_bytes.get_flags(head)): continue
        insn += 1
        if not has_seh:
            d = idc.generate_disasm_line(head, 0) or ""
            if "fs:[0]" in d or "fs:0" in d: has_seh = True
        for xr in idautils.XrefsFrom(head, 0):
            if xr.type in (ida_xref.fl_CN, ida_xref.fl_CF):
                cf = ida_funcs.get_func(xr.to)
                if cf: callees.add(cf.start_ea)
                if xr.to in ic: imp_set.add(ic[xr.to])
                elif cf and cf.start_ea in ic: imp_set.add(ic[cf.start_ea])
                else:
                    nm = ida_name.get_name(xr.to) or ""
                    if nm.startswith("__imp_"): imp_set.add(nm[6:])
            elif xr.type in (ida_xref.dr_R, ida_xref.dr_W, ida_xref.dr_O):
                if ida_bytes.is_strlit(ida_bytes.get_flags(xr.to)):
                    raw = idc.get_strlit_contents(xr.to, -1, idc.STRTYPE_C)
                    if raw:
                        try: s = raw.decode("utf-8","replace")[:MAX_STR_LEN]
                        except: s = repr(raw)[:MAX_STR_LEN]
                        str_set.add(s)
    in_deg = sum(1 for x in idautils.XrefsTo(ea,0) if x.type in (ida_xref.fl_CN, ida_xref.fl_CF))
    out_deg = len(callees)
    try:
        fr = ida_frame.get_frame(ea); frame_sz = idc.get_struc_size(fr.id) if fr else 0
    except: frame_sz = 0
    arg_bytes = getattr(f, "argsize", 0)
    il = set(s.lower() for s in imp_set)
    flags = {
        "calls_alloc": bool(il & {"malloc","calloc","realloc","new","globalalloc","localalloc","heapalloc","virtualalloc"}),
        "calls_free":  bool(il & {"free","delete","globalfree","localfree","heapfree","virtualfree"}),
        "calls_string":bool(il & {"strlen","strcpy","strcat","strcmp","sprintf","printf","lstrcpy","lstrcmp","wcslen","wsprintf","strncpy","strncat","strncmp"}),
        "calls_socket":bool(il & {"socket","connect","send","recv","wsasend","wsarecv","closesocket","accept","bind","listen","select","wsagetlasterror","wsastartup"}),
        "calls_crypto":bool(il & {"cryptacquirecontext","cryptcreatehash","crypthashdata","cryptderivekey","cryptencrypt","cryptdecrypt","md5","sha1","sha256"}),
        "calls_file":  bool(il & {"createfile","readfile","writefile","closefile","getfilesize","setfilepointer","findfile","openfile","fopen","fread","fwrite","fclose"}),
    }
    vt = vc.get(ea)
    k = _klass(size, bb, insn, out_deg, loops)
    return {"ea":"0x%08X"%ea,"name":ida_name.get_name(ea) or ("sub_%X"%ea),"size":size,"bb_count":bb,"insn_count":insn,"in_degree":in_deg,"out_degree":out_deg,"import_calls":sorted(imp_set)[:MAX_IMPORTS],"string_refs":sorted(str_set)[:MAX_STRINGS],"is_vtable_member":bool(vt),"vtable_ea":("0x%08X"%vt[0]) if vt else None,"vtable_slot":vt[1] if vt else None,"rtti_class":None,"has_seh":has_seh,"loop_count":loops,"stack_frame_size":frame_sz,"arg_bytes":arg_bytes,"flags":flags,"klass":k}

def _merge_summary(sp, ps, shard):
    merged = ps.copy()
    if os.path.exists(sp):
        try:
            ex = json.load(open(sp, encoding="utf-8"))
            for k,v in ex.get("klass_histogram",{}).items(): merged["klass_histogram"][k]=merged["klass_histogram"].get(k,0)+v
            fc2 = merged["flag_counts"]
            for k in fc2: fc2[k]+=ex.get("flag_counts",{}).get(k,0)
            ma = dict(ps["top_apis"])
            for nm,cnt in ex.get("top_apis",[]): ma[nm]=ma.get(nm,0)+cnt
            merged["top_apis"]=sorted(ma.items(),key=lambda x:-x[1])[:60]
            merged["total_profiled"]+=ex.get("total_profiled",0)
            merged["with_strings"]+=ex.get("with_strings",0)
            merged["with_vtable"]+=ex.get("with_vtable",0)
            merged["total_eligible"]=ex.get("total_eligible",ps["total_eligible"])
            es=ex.get("shard_files",[])
            if not isinstance(es,list): es=[es]
            merged["shard_files"]=es+[shard]
            if "shard_file" in merged: del merged["shard_file"]
        except Exception as e:
            merged["shard_files"]=[shard]
            if "shard_file" in merged: del merged["shard_file"]
    else:
        merged["shard_files"]=[shard]
        if "shard_file" in merged: del merged["shard_file"]
    json.dump(merged, open(sp,"w",encoding="utf-8"), ensure_ascii=False, indent=2)

def main():
    print("[sweep] building caches...")
    ic = _build_import_cache(); vc = _build_vtable_cache()
    print("[sweep] import=%d vtable=%d" % (len(ic), len(vc)))
    all_eas = [ea for ea in idautils.Functions()
               if _is_default_name(ida_name.get_name(ea) or ("sub_%X"%ea))
               and not _is_runtime(ida_name.get_name(ea) or "", ea)]
    total = len(all_eas)
    print("[sweep] total eligible=%d, starting from offset=%d" % (total, START_OFFSET))
    os.makedirs(OUT_DIR, exist_ok=True)
    sp = os.path.join(OUT_DIR, "profile.summary.json")

    page_off = START_OFFSET
    grand_total_written = 0
    while page_off < total:
        page_eas = all_eas[page_off:page_off+PAGE_SIZE]
        end_idx = page_off + len(page_eas)
        print("[sweep] page [%d..%d)" % (page_off, end_idx))
        records = []
        for i, ea in enumerate(page_eas):
            if i % 500 == 0: print("[sweep]   %d/%d" % (i, len(page_eas)))
            try:
                r = _profile(ea, ic, vc)
                if r: records.append(r)
            except Exception as exc:
                records.append({"ea":"0x%08X"%ea,"error":str(exc),"klass":"error"})
        shard = os.path.join(OUT_DIR, "functions.%d-%d.jsonl" % (page_off, end_idx))
        with open(shard,"w",encoding="utf-8") as fh:
            for r in records: fh.write(json.dumps(r,ensure_ascii=False)+"\n")
        print("[sweep] wrote %s (%d)" % (shard, len(records)))
        grand_total_written += len(records)
        kh = {}; fc2 = {k:0 for k in ("calls_alloc","calls_free","calls_string","calls_socket","calls_crypto","calls_file")}; ac = {}; sc = vc_c = 0
        for r in records:
            kh[r.get("klass","?")] = kh.get(r.get("klass","?"),0)+1
            for k in fc2:
                if r.get("flags",{}).get(k): fc2[k]+=1
            for a in r.get("import_calls",[]): ac[a]=ac.get(a,0)+1
            if r.get("string_refs"): sc+=1
            if r.get("is_vtable_member"): vc_c+=1
        ps = {"schema":"profile_all/1","generated":datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),"binary":idaapi.get_root_filename() or "","total_eligible":total,"page_offset":page_off,"page_end":end_idx,"total_profiled":len(records),"klass_histogram":kh,"flag_counts":fc2,"top_apis":sorted(ac.items(),key=lambda x:-x[1])[:60],"with_strings":sc,"with_vtable":vc_c,"with_rtti":0}
        _merge_summary(sp, ps, shard)
        page_off = end_idx

    print("[sweep] DONE total_written=%d" % grand_total_written)
    final = json.load(open(sp, encoding="utf-8"))
    print("PROFILE_JSON:" + json.dumps(final, ensure_ascii=False))

main()
