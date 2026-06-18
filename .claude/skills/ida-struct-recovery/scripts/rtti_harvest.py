# rtti_harvest.py — READ-ONLY MSVC RTTI harvester for the legacy Martial Heroes client (32-bit x86).
# Walks Complete Object Locators (COL) in .rdata, resolves TypeDescriptor names, demangles them,
# walks Class Hierarchy Descriptors, and emits:
#   Docs/RE/_dirty/campaign6/rtti/classes.json    — all recovered classes
#   Docs/RE/_dirty/campaign6/rtti/<Class>.vtable.md  — slot tables (top ~60 by xref count)
#
# RUN INSIDE IDA PRO 9.3 (IDAPython) via mcp__ida__py_exec_file.
# DIRTY: derived from copyrighted binary — output goes ONLY under Docs/RE/_dirty/.
# This script is READ-ONLY: no set_name/rename/patch/set_type calls.
#
# MSVC x86 RTTI layout (absolute pointers, 32-bit):
#   vtable[-1] == &COL  (dword before vtable first slot)
#   COL+0x00: signature (0 for x86)
#   COL+0x04: vptr offset in object
#   COL+0x08: cdOffset
#   COL+0x0C: ptr to TypeDescriptor (in .data on MSVC)
#   COL+0x10: ptr to ClassHierarchyDescriptor (in .rdata)
#   TypeDescriptor+0x00: ptr to type_info vtable
#   TypeDescriptor+0x08: mangled name string (e.g. ".?AVFoo@@")
#   CHD+0x08: numBaseClasses
#   CHD+0x0C: ptr to BaseClassArray

# === CONFIG ===
OUT_DIR       = r"Docs\RE\_dirty\campaign6\rtti"
MD_TOP_N      = 60    # emit .md slot-table for the top-N classes by vtable xref count
MAX_SLOTS     = 128   # max vtable slots to read per class
MAX_BASES     = 16    # max base classes to record per class
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
    import ida_nalt
    import ida_xref
except ImportError as exc:
    raise SystemExit("rtti_harvest.py must run inside IDA Pro (IDAPython): %s" % exc)


PTR = 4  # 32-bit image


def _seg_name(ea):
    return (idc.get_segm_name(ea) or "").lower()


def _in_rdata(ea):
    return _seg_name(ea) in (".rdata", "rdata", ".rodata", "const")


def _in_data_or_rdata(ea):
    return _seg_name(ea) in (".rdata", "rdata", ".rodata", "const", ".data", "data")


def _read_mangled_name(td_ea):
    name_ea = td_ea + 8
    raw = idc.get_strlit_contents(name_ea, -1, idc.STRTYPE_C)
    if raw:
        try:
            return raw.decode("ascii", "replace")
        except Exception:
            pass
    buf = []
    for i in range(256):
        b = idc.get_wide_byte(name_ea + i)
        if b == 0 or b < 0x20 or b > 0x7e:
            break
        buf.append(chr(b))
    return "".join(buf)


def _demangle(mangled):
    if not mangled:
        return ""
    to_demangle = mangled.lstrip(".")
    try:
        result = idc.demangle_name(to_demangle, idc.get_inf_attr(idc.INF_LONG_DN)) or ""
    except Exception:
        result = ""
    if result:
        result = re.sub(r"^(class|struct|enum|union)\s+", "", result)
        result = re.sub(r"\s*`[^']*'", "", result)
        return result.strip()
    # fallback: parse .?AVFoo@Bar@@ manually
    m = re.match(r"\.?\?A[UVTS](.+)@@", mangled)
    if m:
        parts = [p for p in m.group(1).split("@") if p]
        parts.reverse()
        return "::".join(parts) if parts else mangled
    return mangled


def _sanitize_id(name):
    s = re.sub(r"[<>:,\s]+", "_", name)
    s = re.sub(r"[^A-Za-z0-9_]+", "", s)
    return s[:80] or "Unknown"


def _read_base_chain(chd_ea):
    bases = []
    try:
        num_bases = idc.get_wide_dword(chd_ea + 8)
        if num_bases == 0 or num_bases > MAX_BASES:
            return bases
        bca_ea = idc.get_wide_dword(chd_ea + 0x0C)
        if not ida_bytes.is_loaded(bca_ea):
            return bases
        for i in range(min(int(num_bases), MAX_BASES)):
            bcd_ea = idc.get_wide_dword(bca_ea + i * PTR)
            if not ida_bytes.is_loaded(bcd_ea):
                break
            btd_ea = idc.get_wide_dword(bcd_ea)
            if not ida_bytes.is_loaded(btd_ea):
                break
            mname = _read_mangled_name(btd_ea)
            dname = _demangle(mname)
            if dname and dname not in bases:
                bases.append(dname)
    except Exception:
        pass
    return bases


def _read_vtable_slots(vt_ea, indeg_cache):
    slots = []
    ea = vt_ea
    for idx in range(MAX_SLOTS):
        if idx > 0:
            nm = ida_name.get_name(ea)
            if nm and not nm.startswith("off_") and not nm.startswith("dword_"):
                has_xr = any(True for _ in idautils.XrefsTo(ea, 0))
                if has_xr:
                    break
        target = idc.get_wide_dword(ea)
        if not ida_bytes.is_loaded(target):
            break
        f = ida_funcs.get_func(target)
        if not (f and f.start_ea == target):
            break
        tname = ida_name.get_name(target) or ("sub_%X" % target)
        indeg = indeg_cache.get(target, 0)
        tlow = tname.lower()
        if "scalar_deleting" in tlow or "vector_deleting" in tlow:
            role = "scalar-deleting-dtor"
        elif "destructor" in tlow or (tname.startswith("??1") and "@@" in tname):
            role = "dtor"
        elif "ctor" in tlow or (tname.startswith("??0") and "@@" in tname):
            role = "ctor"
        elif f and (f.end_ea - f.start_ea) < 0x10 and idx == 0:
            role = "ctor"
        else:
            role = "vfunc"
        slots.append({
            "idx": idx,
            "slot_ea": "0x%08X" % ea,
            "target_ea": "0x%08X" % target,
            "current_name": tname,
            "in_degree": indeg,
            "role": role,
        })
        ea += PTR
    return slots


def _scan_vtables():
    """
    Scan .rdata for dwords that point to valid COL structures.
    vtable[-1] == &COL means: dword at (vtable_ea - 4) == col_ea.
    So we scan .rdata; for each dword D, if D looks like a valid COL, then
    vtable_ea = current_ea + 4.
    Fix: TypeDescriptor lives in .data (not .rdata) on MSVC.
    """
    found = {}  # col_ea -> vtable_ea

    for seg_start in idautils.Segments():
        seg_nm = (idc.get_segm_name(seg_start) or "").lower()
        if seg_nm not in (".rdata", "rdata", ".rodata", "const"):
            continue
        seg_end = idc.get_segm_end(seg_start)
        ea = seg_start
        while ea + PTR <= seg_end:
            col_ea = idc.get_wide_dword(ea)
            # Fast filters (avoid expensive lookups on most iterations)
            if not ida_bytes.is_loaded(col_ea) or col_ea % 4 != 0:
                ea += PTR
                continue
            col_seg = (idc.get_segm_name(col_ea) or "").lower()
            if col_seg not in (".rdata", "rdata", ".rodata", "const"):
                ea += PTR
                continue
            # COL signature must be 0 (x86)
            if idc.get_wide_dword(col_ea) != 0:
                ea += PTR
                continue
            # vptr offset must be small
            offset_field = idc.get_wide_dword(col_ea + 4)
            if offset_field > 0x400:
                ea += PTR
                continue
            td_ea = idc.get_wide_dword(col_ea + 0x0C)
            chd_ea = idc.get_wide_dword(col_ea + 0x10)
            # TypeDescriptor is in .data on MSVC (runtime data)
            if not ida_bytes.is_loaded(td_ea):
                ea += PTR
                continue
            td_seg = (idc.get_segm_name(td_ea) or "").lower()
            if td_seg not in (".rdata", "rdata", ".rodata", "const", ".data", "data"):
                ea += PTR
                continue
            # CHD is in .rdata
            if not ida_bytes.is_loaded(chd_ea):
                ea += PTR
                continue
            chd_seg = (idc.get_segm_name(chd_ea) or "").lower()
            if chd_seg not in (".rdata", "rdata", ".rodata", "const"):
                ea += PTR
                continue
            # TypeDescriptor[0] = type_info vtable ptr, must be loaded
            ti_vft = idc.get_wide_dword(td_ea)
            if not ida_bytes.is_loaded(ti_vft):
                ea += PTR
                continue
            # Mangled name check at TypeDescriptor+8
            name_ea = td_ea + 8
            b0 = idc.get_wide_byte(name_ea)
            b1 = idc.get_wide_byte(name_ea + 1)
            b2 = idc.get_wide_byte(name_ea + 2)
            ok = (b0 == 46 and b1 == 63 and b2 == 65) or (b0 == 63 and b1 == 65)
            if not ok:
                ea += PTR
                continue
            if col_ea not in found:
                found[col_ea] = ea + PTR  # vtable starts at ea+4
            ea += PTR
    return found


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    print("[rtti_harvest] scanning .rdata for vtable->COL links...")
    col_to_vtable = _scan_vtables()
    print("[rtti_harvest] found %d COL candidates" % len(col_to_vtable))

    if not col_to_vtable:
        print("[rtti_harvest] ERROR: no COL candidates found; check .rdata segment name.")
        print("RTTI_JSON:" + json.dumps({"schema": "rtti_harvest/1", "ok": False,
              "error": "no COL found", "total_classes": 0}, ensure_ascii=False))
        return

    # collect all vtable function EAs for in-degree computation
    print("[rtti_harvest] collecting vtable function EAs...")
    all_vtable_funcs = set()
    for col_ea, vt_ea in col_to_vtable.items():
        ea = vt_ea
        for _ in range(MAX_SLOTS):
            target = idc.get_wide_dword(ea)
            if not ida_bytes.is_loaded(target):
                break
            f = ida_funcs.get_func(target)
            if f and f.start_ea == target:
                all_vtable_funcs.add(target)
            else:
                break
            ea += PTR

    print("[rtti_harvest] computing in-degrees for %d vtable funcs..." % len(all_vtable_funcs))
    indeg_cache = {}
    for fea in all_vtable_funcs:
        indeg_cache[fea] = sum(1 for _ in idautils.XrefsTo(fea, 0))

    classes = []
    errors = []

    for col_ea, vt_ea in col_to_vtable.items():
        try:
            td_ea    = idc.get_wide_dword(col_ea + 0x0C)
            chd_ea   = idc.get_wide_dword(col_ea + 0x10)
            vptr_off = idc.get_wide_dword(col_ea + 4)

            mangled   = _read_mangled_name(td_ea)
            demangled = _demangle(mangled)
            if not demangled:
                demangled = "Unknown_%08X" % col_ea

            base_chain = _read_base_chain(chd_ea)
            slots = _read_vtable_slots(vt_ea, indeg_cache)

            # rough object size hint from base class mdisp fields
            obj_size_hint = 0
            try:
                num_bases = idc.get_wide_dword(chd_ea + 8)
                bca_ea = idc.get_wide_dword(chd_ea + 0x0C)
                for i in range(min(int(num_bases), MAX_BASES)):
                    bcd_ea = idc.get_wide_dword(bca_ea + i * PTR)
                    if ida_bytes.is_loaded(bcd_ea):
                        mdisp = idc.get_wide_dword(bcd_ea + 8)
                        if 0 < mdisp < 0x10000:
                            obj_size_hint = max(obj_size_hint, mdisp + PTR)
            except Exception:
                pass

            classes.append({
                "class":            demangled,
                "mangled":          mangled,
                "vtable_ea":        "0x%08X" % vt_ea,
                "col_ea":           "0x%08X" % col_ea,
                "vptr_offset":      vptr_off,
                "base_chain":       base_chain,
                "object_size_hint": obj_size_hint,
                "slots":            slots,
            })
        except Exception as exc:
            errors.append({"col_ea": "0x%08X" % col_ea, "error": str(exc)})

    # sort by total slot in-degree (proxy for most-used classes)
    def _total_indeg(cls):
        return sum(s.get("in_degree", 0) for s in cls.get("slots", []))

    classes.sort(key=_total_indeg, reverse=True)

    # write classes.json (without full slot lists — slots in .md files)
    classes_light = []
    for cls in classes:
        entry = {k: v for k, v in cls.items() if k != "slots"}
        entry["slot_count"] = len(cls.get("slots", []))
        classes_light.append(entry)

    classes_path = os.path.join(OUT_DIR, "classes.json")
    with open(classes_path, "w", encoding="utf-8") as fh:
        json.dump(classes_light, fh, ensure_ascii=False, indent=2)
    print("[rtti_harvest] wrote %s (%d classes)" % (classes_path, len(classes)))

    # write per-class .md slot tables for top MD_TOP_N
    md_written = 0
    for cls in classes[:MD_TOP_N]:
        slug = _sanitize_id(cls["class"])
        md_path = os.path.join(OUT_DIR, "%s.vtable.md" % slug)
        lines = [
            "# RTTI vtable: %s" % cls["class"],
            "",
            "- mangled: `%s`" % cls["mangled"],
            "- vtable_ea: `%s`" % cls["vtable_ea"],
            "- col_ea: `%s`" % cls["col_ea"],
            "- vptr_offset: %d" % cls["vptr_offset"],
            "- base_chain: %s" % (", ".join(cls["base_chain"]) or "(none)"),
            "- object_size_hint: %d" % cls["object_size_hint"],
            "- slots: %d" % len(cls.get("slots", [])),
            "",
            "> Slot roles are HEURISTIC hypotheses. Confirm via ida-decompile-export before spec promotion.",
            "",
            "| Slot | Slot EA | Target EA | Current Name | In-deg | Role |",
            "|---|---|---|---|---|---|",
        ]
        for s in cls.get("slots", []):
            lines.append("| %d | %s | %s | %s | %d | %s |" % (
                s["idx"], s["slot_ea"], s["target_ea"],
                s["current_name"], s["in_degree"], s["role"]))
        with open(md_path, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines) + "\n")
        md_written += 1

    print("[rtti_harvest] wrote %d .vtable.md files" % md_written)

    sample_names = [c["class"] for c in classes[:30]]

    summary = {
        "schema":            "rtti_harvest/1",
        "generated":         datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "binary":            idaapi.get_root_filename() or "",
        "total_classes":     len(classes),
        "total_vtable_funcs": len(all_vtable_funcs),
        "errors":            len(errors),
        "classes_path":      classes_path,
        "md_files_written":  md_written,
        "sample_names":      sample_names,
    }

    print("RTTI_JSON:" + json.dumps(summary, ensure_ascii=False))


main()
