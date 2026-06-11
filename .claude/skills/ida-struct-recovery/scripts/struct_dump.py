# struct_dump.py — IDAPython: recover field & vtable layout of a legacy object in Main.exe.
#
# RUN THIS INSIDE IDA PRO 9.3 via the IDA MCP script-execution tool. It imports
# ida_typeinf/ida_struct/ida_* which exist only inside IDA.
#
# Edit the two TARGET lines below before sending the source to the exec tool:
#   TARGET_KIND = "type"      ; TARGET = "Actor"          # a named local type / struct
#   TARGET_KIND = "vtable"    ; TARGET = 0x006C1A40       # a vtable address
#   TARGET_KIND = "instance"  ; TARGET = 0x006F0120       # a global whose 1st DWORD is a vtbl ptr
#
# Output: builds one JSON document and prints it on a single line prefixed "STRUCT_JSON:".
# The calling skill writes the dirty detail + a promotable .offsets.h to Docs/RE/_dirty/structs/.
#
# DIRTY: the detail dump carries addresses and decompiler-derived symbols — copyright-tainted.
# Only the address-free layout may eventually be rewritten into a clean spec.

# ── set me ──────────────────────────────────────────────────────────────────
TARGET_KIND = "type"          # "type" | "vtable" | "instance"
TARGET = "ENTER_NAME_OR_ADDRESS"
# ────────────────────────────────────────────────────────────────────────────

import json
import hashlib

import idaapi
import idautils
import idc

import ida_nalt
import ida_bytes
import ida_funcs
import ida_typeinf

# ida_struct exists on classic IDB struct API; guard for type-info-only builds.
try:
    import ida_struct
    _HAS_IDA_STRUCT = True
except Exception:
    _HAS_IDA_STRUCT = False


def _sha256_of_input():
    try:
        path = ida_nalt.get_input_file_path()
        if path:
            h = hashlib.sha256()
            with open(path, "rb") as fh:
                for chunk in iter(lambda: fh.read(1 << 20), b""):
                    h.update(chunk)
            return h.hexdigest()
    except Exception:
        pass
    try:
        sha = ida_nalt.retrieve_input_file_sha256()
        if sha:
            return sha.hex()
    except Exception:
        pass
    return ""


def _ptr_size():
    return 8 if idaapi.inf_is_64bit() else 4


def _func_name(ea):
    return ida_funcs.get_func_name(ea) or idc.get_func_name(ea) or ""


# ── type / struct member walking ─────────────────────────────────────────────

def _members_from_tinfo(tif):
    """Walk members of a struct/class tinfo_t: offset, size, type, name."""
    out = []
    udt = ida_typeinf.udt_type_data_t()
    if not tif.get_udt_details(udt):
        return out, 0
    for m in udt:
        out.append({
            "offset": int(m.offset // 8),     # udt offsets are in bits
            "size": int(m.size // 8) if m.size else int(m.type.get_size()),
            "type": m.type._print() if m.type else "",
            "name": m.name or "",
        })
    return out, int(tif.get_size())


def _resolve_type(name):
    tif = ida_typeinf.tinfo_t()
    til = ida_typeinf.get_idati()
    if tif.get_named_type(til, name):
        return tif
    # try parsing a bare name
    if ida_typeinf.parse_decl(tif, til, name + ";", 0) is not None:
        return tif
    return None


def _dump_type(name):
    tif = _resolve_type(name)
    if tif is None and _HAS_IDA_STRUCT:
        # classic struct API fallback
        sid = ida_struct.get_struc_id(name)
        if sid != idaapi.BADADDR:
            sptr = ida_struct.get_struc(sid)
            members = []
            m = ida_struct.get_struc_first_member(sptr) if hasattr(ida_struct, "get_struc_first_member") else None
            for off, _name, _size in idautils.StructMembers(sid):
                members.append({
                    "offset": int(off),
                    "size": int(_size),
                    "type": idc.get_member_type_str(sid, off) if hasattr(idc, "get_member_type_str") else "",
                    "name": _name or "",
                })
            return {
                "ok": True, "kind": "type", "name": name,
                "total_size": int(ida_struct.get_struc_size(sptr)),
                "members": members, "vtable": [],
            }
    if tif is None:
        return {"ok": False, "error": "type not found: %s" % name}
    members, total = _members_from_tinfo(tif)
    return {
        "ok": True, "kind": "type", "name": name,
        "total_size": total, "members": members, "vtable": [],
    }


# ── vtable walking ───────────────────────────────────────────────────────────

def _read_ptr(ea):
    return ida_bytes.get_qword(ea) if _ptr_size() == 8 else ida_bytes.get_dword(ea)


def _dump_vtable(vtbl_ea, label):
    """Walk a vtable: each slot holds a function pointer until a non-code/zero ptr breaks it."""
    slots = []
    idx = 0
    ea = vtbl_ea
    psize = _ptr_size()
    while idx < 4096:  # hard cap
        ptr = _read_ptr(ea)
        if ptr in (0, idaapi.BADADDR):
            break
        tgt_func = ida_funcs.get_func(ptr)
        if tgt_func is None:
            # not a function pointer -> end of vtable (also stop if a name/xref boundary appears)
            break
        slots.append({
            "slot": idx,
            "addr": "0x%08X" % ea,
            "func": "0x%08X" % ptr,
            "name": _func_name(ptr) or ("sub_%08X" % ptr),
        })
        idx += 1
        ea += psize
        # stop early if the next slot is the start of a new named data object / xref'd label
        if idx > 0 and ida_bytes.has_user_name(ida_bytes.get_flags(ea)):
            # a fresh symbol typically marks the next object; keep going only if it is a func ptr
            nxt = _read_ptr(ea)
            if ida_funcs.get_func(nxt) is None:
                break
    return {
        "ok": True, "kind": "vtable", "name": label,
        "vtable_addr": "0x%08X" % vtbl_ea,
        "slot_count": len(slots), "members": [], "vtable": slots,
    }


def _dump_instance(inst_ea):
    """A global instance: its first pointer-sized word is the vtable pointer."""
    vtbl = _read_ptr(inst_ea)
    if vtbl in (0, idaapi.BADADDR) or ida_funcs.get_func(_read_ptr(vtbl)) is None:
        return {"ok": False, "error": "first word at instance is not a plausible vtable pointer"}
    label = idc.get_name(inst_ea) or ("inst_%08X" % inst_ea)
    res = _dump_vtable(vtbl, label)
    res["instance_addr"] = "0x%08X" % inst_ea
    return res


def main():
    sha = _sha256_of_input()
    kind = TARGET_KIND.strip().lower()

    if kind == "type":
        res = _dump_type(str(TARGET))
    elif kind == "vtable":
        ea = TARGET if isinstance(TARGET, int) else idc.get_name_ea_simple(str(TARGET))
        if ea == idaapi.BADADDR:
            try:
                ea = int(str(TARGET), 0)
            except (ValueError, TypeError):
                ea = idaapi.BADADDR
        res = _dump_vtable(ea, idc.get_name(ea) or ("vtbl_%08X" % ea)) if ea != idaapi.BADADDR \
            else {"ok": False, "error": "could not resolve vtable target"}
    elif kind == "instance":
        ea = TARGET if isinstance(TARGET, int) else idc.get_name_ea_simple(str(TARGET))
        if ea == idaapi.BADADDR:
            try:
                ea = int(str(TARGET), 0)
            except (ValueError, TypeError):
                ea = idaapi.BADADDR
        res = _dump_instance(ea) if ea != idaapi.BADADDR \
            else {"ok": False, "error": "could not resolve instance target"}
    else:
        res = {"ok": False, "error": "TARGET_KIND must be type|vtable|instance"}

    res["schema"] = "ida-struct-recovery/1"
    res["sha256"] = sha
    res["ptr_size"] = _ptr_size()
    print("STRUCT_JSON:" + json.dumps(res, ensure_ascii=False))


main()
