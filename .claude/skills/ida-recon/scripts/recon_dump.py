# recon_dump.py — IDAPython baseline census for the legacy Martial Heroes Main.exe.
#
# RUN THIS INSIDE IDA PRO 9.3, via the IDA MCP script-execution tool (e.g.
# mcp__ida__execute_script / run_python / eval). It does NOT run under a plain CPython:
# it imports idautils/idaapi/ida_* which only exist inside IDA.
#
# Output: builds one JSON document describing the binary and prints it to stdout on a single
# line prefixed with "RECON_JSON:". The calling skill captures that line and writes it to
# Docs/RE/_dirty/recon/. This script intentionally writes nothing to disk itself.
#
# DIRTY: the emitted census is derived directly from the copyrighted binary. It belongs only
# under Docs/RE/_dirty/ and must never be committed or copied into clean specs.

import json
import hashlib

import idaapi
import idautils
import idc

import ida_nalt
import ida_segment
import ida_entry
import ida_bytes
import ida_name
import ida_funcs


def _sha256_of_input():
    """SHA-256 of the original input file IDA loaded, so all output pins one exact build."""
    try:
        path = ida_nalt.get_input_file_path()
        if path:
            h = hashlib.sha256()
            with open(path, "rb") as fh:
                for chunk in iter(lambda: fh.read(1 << 20), b""):
                    h.update(chunk)
            return h.hexdigest(), path
    except Exception:
        pass
    # Fall back to IDA's stored MD5/SHA256 retval pointer if the file is not reachable.
    try:
        sha = ida_nalt.retrieve_input_file_sha256()
        if sha:
            return sha.hex(), (ida_nalt.get_input_file_path() or "")
    except Exception:
        pass
    return "", (ida_nalt.get_input_file_path() or "")


def _segments():
    out = []
    for ea in idautils.Segments():
        seg = ida_segment.getseg(ea)
        if not seg:
            continue
        name = ida_segment.get_segm_name(seg) or ""
        cls = ida_segment.get_segm_class(seg) or ""
        perm = seg.perm
        out.append({
            "name": name,
            "class": cls,
            "start": "0x%08X" % seg.start_ea,
            "end": "0x%08X" % seg.end_ea,
            "size": seg.end_ea - seg.start_ea,
            "readable": bool(perm & ida_segment.SEGPERM_READ),
            "writable": bool(perm & ida_segment.SEGPERM_WRITE),
            "executable": bool(perm & ida_segment.SEGPERM_EXEC),
            "bitness": (16, 32, 64)[seg.bitness],
        })
    return out


def _imports():
    out = []
    qty = idaapi.get_import_module_qty()
    for i in range(qty):
        mod = idaapi.get_import_module_name(i) or "<unnamed>"
        entries = []

        def _cb(ea, name, ordinal):
            entries.append({
                "addr": "0x%08X" % ea,
                "name": name or "",
                "ordinal": ordinal,
            })
            return True

        idaapi.enum_import_names(i, _cb)
        out.append({"module": mod, "count": len(entries), "entries": entries})
    return out


def _exports():
    out = []
    for index, ordinal, ea, name in idautils.Entries():
        out.append({
            "index": index,
            "ordinal": ordinal,
            "addr": "0x%08X" % ea,
            "name": name or "",
        })
    return out


def _entrypoints():
    out = []
    n = ida_entry.get_entry_qty()
    for i in range(n):
        ordinal = ida_entry.get_entry_ordinal(i)
        ea = ida_entry.get_entry(ordinal)
        out.append({
            "ordinal": ordinal,
            "addr": "0x%08X" % ea,
            "name": ida_entry.get_entry_name(ordinal) or "",
        })
    # IDA's notional program entry, if distinct.
    start = idc.get_inf_attr(idc.INF_START_IP) if hasattr(idc, "INF_START_IP") else idaapi.BADADDR
    if start not in (idaapi.BADADDR, 0):
        out.append({"ordinal": None, "addr": "0x%08X" % start, "name": "<inf_start_ip>"})
    return out


def _strings():
    """Full string table. Caps very long strings so the JSON stays manageable."""
    out = []
    try:
        sc = idautils.Strings()
        sc.setup(strtypes=[ida_nalt.STRTYPE_C, ida_nalt.STRTYPE_C_16])
    except TypeError:
        sc = idautils.Strings()
    for s in sc:
        try:
            text = str(s)
        except Exception:
            text = ""
        if len(text) > 512:
            text = text[:512] + "…"
        out.append({
            "addr": "0x%08X" % s.ea,
            "len": s.length,
            "type": int(s.strtype),
            "text": text,
        })
    return out


def _is_default_name(name):
    """Filter out IDA's auto-generated names (sub_, loc_, off_, dword_, byte_, unk_, etc.)."""
    if not name:
        return True
    for pfx in ("sub_", "loc_", "locret_", "off_", "dword_", "word_", "byte_",
                "unk_", "asc_", "stru_", "flt_", "dbl_", "tbyte_", "jpt_", "algn_", "nullsub_"):
        if name.startswith(pfx):
            return True
    return False


def _globals():
    """Named, non-default data symbols that are NOT functions (i.e. globals/statics)."""
    out = []
    for ea, name in idautils.Names():
        if _is_default_name(name):
            continue
        if ida_funcs.get_func(ea) is not None:
            continue  # skip code; this dump is about data globals
        flags = ida_bytes.get_flags(ea)
        if ida_bytes.is_code(flags):
            continue
        out.append({
            "addr": "0x%08X" % ea,
            "name": name,
            "size": idc.get_item_size(ea),
        })
    return out


def _named_functions():
    """Count of analyst-named functions (helps gauge analysis progress); names + addrs."""
    out = []
    for ea in idautils.Functions():
        name = ida_funcs.get_func_name(ea) or idc.get_func_name(ea) or ""
        if _is_default_name(name):
            continue
        out.append({"addr": "0x%08X" % ea, "name": name})
    return out


def main():
    sha256, input_path = _sha256_of_input()
    doc = {
        "schema": "ida-recon/1",
        "binary": {
            "name": ida_nalt.get_root_filename() or "",
            "input_path": input_path,
            "sha256": sha256,
            "bits": 64 if idaapi.inf_is_64bit() else 32,
            "image_base": "0x%08X" % idaapi.get_imagebase(),
            "image_type": ida_nalt.get_file_type_name() or "",
        },
        "segments": _segments(),
        "imports": _imports(),
        "exports": _exports(),
        "entrypoints": _entrypoints(),
        "strings": _strings(),
        "globals": _globals(),
        "named_functions": _named_functions(),
    }
    doc["counts"] = {
        "segments": len(doc["segments"]),
        "import_modules": len(doc["imports"]),
        "imports": sum(m["count"] for m in doc["imports"]),
        "exports": len(doc["exports"]),
        "entrypoints": len(doc["entrypoints"]),
        "strings": len(doc["strings"]),
        "globals": len(doc["globals"]),
        "named_functions": len(doc["named_functions"]),
    }
    # Single-line, prefixed so the skill can reliably extract it from tool stdout.
    print("RECON_JSON:" + json.dumps(doc, ensure_ascii=False))


main()
