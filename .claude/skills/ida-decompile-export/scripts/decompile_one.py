# decompile_one.py — IDAPython: decompile ONE function + its xrefs from the legacy Main.exe.
#
# RUN THIS INSIDE IDA PRO 9.3 via the IDA MCP script-execution tool. Requires the Hex-Rays
# decompiler. It imports ida_hexrays/idautils/ida_* which exist only inside IDA.
#
# Edit the TARGET line below before sending the source to the exec tool:
#   - a function name:  TARGET = "RecvPacketDispatch"
#   - or an address:    TARGET = 0x004A1230
#
# Output: builds one JSON document and prints it to stdout on a single line prefixed with
# "DECOMP_JSON:". The calling skill captures that line and writes it to
# Docs/RE/_dirty/functions/<name>.dirty.md.
#
# DIRTY: the emitted pseudo-C is verbatim Hex-Rays output — copyright-tainted derived work.
# It belongs ONLY under Docs/RE/_dirty/ and must never be committed or copied into clean specs.

# ── set me ──────────────────────────────────────────────────────────────────
TARGET = "ENTER_FUNCTION_NAME_OR_ADDRESS"
# ────────────────────────────────────────────────────────────────────────────

import json
import hashlib

import idaapi
import idautils
import idc

import ida_funcs
import ida_name
import ida_nalt
import ida_hexrays
import ida_typeinf


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


def _resolve(target):
    """Resolve TARGET (name str or int address) to an effective address inside a function."""
    if isinstance(target, int):
        ea = target
    else:
        ea = idc.get_name_ea_simple(str(target))
        if ea == idaapi.BADADDR:
            # Try as a hex/dec string.
            try:
                ea = int(str(target), 0)
            except (ValueError, TypeError):
                ea = idaapi.BADADDR
    return ea


def _func_name(ea):
    name = ida_funcs.get_func_name(ea) or idc.get_func_name(ea) or ""
    return name


def _callers(func_ea):
    """xrefs TO the function entry — who calls it."""
    out = []
    seen = set()
    for xref in idautils.XrefsTo(func_ea, 0):
        frm = xref.frm
        if frm in seen:
            continue
        seen.add(frm)
        out.append({
            "from": "0x%08X" % frm,
            "in_func": _func_name(frm) or "<none>",
            "type": idautils.XrefTypeName(xref.type),
        })
    return out


def _callees(func):
    """xrefs FROM the function body — what it calls (code refs to other funcs)."""
    out = []
    seen = set()
    for item_ea in idautils.FuncItems(func.start_ea):
        for xref in idautils.XrefsFrom(item_ea, 0):
            to = xref.to
            tgt_func = ida_funcs.get_func(to)
            if tgt_func is None or tgt_func.start_ea == func.start_ea:
                continue
            if tgt_func.start_ea in seen:
                continue
            # only call/jump-style code references
            if not (xref.type in (idaapi.fl_CN, idaapi.fl_CF, idaapi.fl_JN, idaapi.fl_JF)):
                continue
            seen.add(tgt_func.start_ea)
            out.append({
                "to": "0x%08X" % tgt_func.start_ea,
                "name": _func_name(tgt_func.start_ea) or "<unnamed>",
            })
    return out


def _prototype(func_ea):
    try:
        tif = ida_typeinf.tinfo_t()
        if ida_nalt.get_tinfo(tif, func_ea):
            return tif._print() or idc.get_type(func_ea) or ""
    except Exception:
        pass
    return idc.get_type(func_ea) or ""


def main():
    ea = _resolve(TARGET)
    if ea == idaapi.BADADDR:
        print("DECOMP_JSON:" + json.dumps({
            "ok": False, "error": "could not resolve target", "target": str(TARGET),
        }))
        return

    func = ida_funcs.get_func(ea)
    if func is None:
        print("DECOMP_JSON:" + json.dumps({
            "ok": False, "error": "address is not inside a function",
            "target": str(TARGET), "ea": "0x%08X" % ea,
        }))
        return

    if not ida_hexrays.init_hexrays_plugin():
        print("DECOMP_JSON:" + json.dumps({
            "ok": False, "error": "Hex-Rays decompiler not available",
            "target": str(TARGET), "ea": "0x%08X" % func.start_ea,
        }))
        return

    try:
        cfunc = ida_hexrays.decompile(func.start_ea)
        pseudo = str(cfunc) if cfunc is not None else ""
    except ida_hexrays.DecompilationFailure as exc:
        print("DECOMP_JSON:" + json.dumps({
            "ok": False, "error": "decompilation failure: %s" % str(exc),
            "target": str(TARGET), "ea": "0x%08X" % func.start_ea,
        }))
        return

    # Local variable types (names + declared types), for struct/layout context.
    locals_out = []
    try:
        if cfunc is not None:
            for lv in cfunc.get_lvars():
                locals_out.append({
                    "name": lv.name or "",
                    "type": lv.type()._print() if lv.type() else "",
                    "arg": bool(lv.is_arg_var),
                })
    except Exception:
        pass

    doc = {
        "ok": True,
        "schema": "ida-decompile-export/1",
        "sha256": _sha256_of_input(),
        "target": {
            "requested": str(TARGET),
            "name": _func_name(func.start_ea) or ("sub_%08X" % func.start_ea),
            "start": "0x%08X" % func.start_ea,
            "end": "0x%08X" % func.end_ea,
            "size": func.end_ea - func.start_ea,
            "prototype": _prototype(func.start_ea),
        },
        "callers": _callers(func.start_ea),
        "callees": _callees(func),
        "locals": locals_out,
        "pseudocode": pseudo,
    }
    print("DECOMP_JSON:" + json.dumps(doc, ensure_ascii=False))


main()
