# annotate_batch.py — atomically annotate ONE Campaign 2 cluster in the legacy Martial Heroes IDB.
#
# For each manifest entry it can, in one re-runnable pass:
#   - set_name      : rename a function/global (canonical role-word name)
#   - comment       : set a function comment (functions) or repeatable comment (data) — NEUTRAL prose
#   - type          : optionally apply a declared struct/enum type to the item
# keyed by address. Dry-run first; apply only on confirmation; idempotent (already-applied -> noop).
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool. It imports ida_* modules
# that exist only inside IDA. No PyYAML dependency: a tiny stdlib parser reads the flat manifest shape.
#
# FIREWALL: the manifest is a slice of the CLEAN, gate-passed campaign glossary (role-word names +
# neutral interop comments), so writing it into the IDB is firewall-safe (same posture as
# ida-naming-sync). Comments MUST be neutral interop documentation — never Hex-Rays pseudo-C, never
# mangled names, never "copy to C#". This script never emits decompiler output anywhere. The only
# repo artifact is the dirty applied-report the calling skill stages under _dirty/campaign2/applied/.

# === CONFIG ===
MODE = "dry-run"          # "dry-run" -> report verdicts only ; "apply" -> perform the annotations
CLUSTER = "unnamed-cluster"   # short cluster slug, echoed into the result (e.g. "network-dispatch")

# Paste the cluster's slice of Docs/RE/_dirty/campaign2/glossary.yaml here. Address keys are quoted
# strings. Each entry is a one-line inline map; any of name/comment/type may be omitted.
# Minimal flat shape understood by the tiny parser below:
#   functions:
#     "0x004A1230": { name: "RecvPacketDispatch", comment: "Dispatches a decrypted inbound packet by opcode.", type: "" }
#   globals:
#     "0x006C1200": { name: "g_RollingKey", comment: "Session rolling cipher key state.", type: "" }
# Declared composite types (struct/enum) referenced by entries' `type:` field. C-declaration text,
# applied once via ida_typeinf so field/member names propagate to every site the type is applied.
#   types:
#     - "struct VfsIndexHeader { unsigned int magic; unsigned int count; unsigned int dataOffset; };"
MANIFEST = r"""
functions: {}
globals: {}
types: []
"""
# ==============

import re
import json
import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_name
    import ida_funcs
    import ida_bytes
    import ida_nalt
    import ida_typeinf
except ImportError as exc:  # pragma: no cover - only meaningful outside IDA
    raise SystemExit("annotate_batch.py must run inside IDA Pro (IDAPython): %s" % exc)


# --- tiny, dependency-free parser for the minimal flat manifest shape (no PyYAML in IDA) ---
def parse_manifest(text):
    out = {"functions": {}, "globals": {}, "types": []}
    section = None
    # captures: addr, then optional name/comment/type fields anywhere in the inline body
    entry_re = re.compile(r'^\s*"(0x[0-9A-Fa-f]+)"\s*:\s*\{(.*)\}\s*$')
    name_re = re.compile(r'name\s*:\s*"((?:[^"\\]|\\.)*)"')
    comment_re = re.compile(r'comment\s*:\s*"((?:[^"\\]|\\.)*)"')
    type_re = re.compile(r'\btype\s*:\s*"((?:[^"\\]|\\.)*)"')
    type_item_re = re.compile(r'^\s*-\s*"((?:[^"\\]|\\.)*)"\s*$')

    def _unescape(s):
        return s.replace('\\"', '"').replace("\\\\", "\\")

    for raw in text.splitlines():
        s = raw.rstrip()
        if re.match(r'^\s*functions\s*:', s):
            section = "functions"; continue
        if re.match(r'^\s*globals\s*:', s):
            section = "globals"; continue
        if re.match(r'^\s*types\s*:', s):
            section = "types"; continue
        if section is None:
            continue
        if section == "types":
            mt = type_item_re.match(s)
            if mt:
                out["types"].append(_unescape(mt.group(1)))
            continue
        m = entry_re.match(s)
        if not m:
            continue
        addr = m.group(1).lower()
        body = m.group(2)
        rec = {"name": None, "comment": None, "type": None}
        nm = name_re.search(body)
        cm = comment_re.search(body)
        tm = type_re.search(body)
        if nm:
            rec["name"] = _unescape(nm.group(1))
        if cm:
            rec["comment"] = _unescape(cm.group(1))
        if tm and tm.group(1).strip():
            rec["type"] = _unescape(tm.group(1))
        out[section][addr] = rec
    return out


# --- runtime / compiler symbol guard (mirrors names_sync.py / rename_batch.py) ---
_RUNTIME_RE = re.compile(
    r'^(__|_imp_|j_|\?\?|\?|_RTC_|__security_|mainCRTStartup|wmainCRTStartup|'
    r'WinMainCRTStartup|std::|_acmdln|_wcmdln|_CxxThrow|__scrt|__crt)')
_RUNTIME_NAMES = {"_initterm", "_initterm_e", "atexit", "__report_gsfailure"}


def is_runtime(name):
    if not name:
        return False
    if name in _RUNTIME_NAMES:
        return True
    return bool(_RUNTIME_RE.match(name))


def is_lib_func(ea):
    f = ida_funcs.get_func(ea)
    return bool(f and (f.flags & ida_funcs.FUNC_LIB))


def sha256_of_input():
    try:
        sha = ida_nalt.retrieve_input_file_sha256()
        if sha:
            return sha.hex()
    except Exception:
        pass
    return ""


def current_comment(ea, is_func):
    if is_func:
        f = ida_funcs.get_func(ea)
        if f is not None:
            c = ida_funcs.get_func_cmt(f, False)  # non-repeatable
            if c:
                return c
            c = ida_funcs.get_func_cmt(f, True)   # repeatable
            if c:
                return c
            return ""
    c = idc.get_cmt(ea, False)
    if c:
        return c
    c = idc.get_cmt(ea, True)
    return c or ""


def set_comment(ea, is_func, text):
    if is_func:
        f = ida_funcs.get_func(ea)
        if f is not None:
            return bool(ida_funcs.set_func_cmt(f, text, True))  # repeatable function comment
        return False
    return bool(idc.set_cmt(ea, text, 1))  # repeatable data comment


def apply_named_type(ea, type_name):
    """Apply a declared struct/enum type (by name) to the item at ea. Returns (ok, detail)."""
    try:
        til = ida_typeinf.get_idati()
        tif = ida_typeinf.tinfo_t()
        if not tif.get_named_type(til, type_name):
            return False, "type-not-declared"
        if ida_typeinf.apply_tinfo(ea, tif, ida_typeinf.TINFO_DEFINITE):
            return True, "applied"
        return False, "apply-failed"
    except Exception as exc:
        return False, "type-error:%s" % exc


def declare_types(decls):
    """Parse C struct/enum declarations into the local type library once. Returns a status list."""
    out = []
    if not decls:
        return out
    til = ida_typeinf.get_idati()
    for decl in decls:
        try:
            # PT_TYP_ONLY: parse one declaration; populates the til with the named type.
            r = ida_typeinf.parse_decls(til, decl, None, ida_typeinf.HTI_DCL)
            out.append({"decl": decl, "errors": int(r)})
        except Exception as exc:
            out.append({"decl": decl, "errors": -1, "exc": str(exc)})
    return out


def verdict_for(addr_str, want, is_func):
    """Compute a verdict for one entry. want = {name, comment, type}."""
    rec = {"section": "functions" if is_func else "globals",
           "addr": addr_str, "cur_name": "", "cur_comment": "",
           "want_name": want.get("name"), "want_comment": want.get("comment"),
           "want_type": want.get("type"), "verdict": "", "result": "",
           "name_result": "", "comment_result": "", "type_result": ""}
    try:
        ea = int(addr_str, 16)
    except (ValueError, TypeError):
        rec["verdict"] = "skip-missing"
        return rec, idaapi.BADADDR
    if not ida_bytes.is_loaded(ea):
        rec["verdict"] = "skip-missing"
        return rec, ea
    if is_func and ida_funcs.get_func(ea) is None:
        rec["verdict"] = "skip-missing"
        return rec, ea
    cur_name = ida_name.get_name(ea) or ""
    rec["cur_name"] = cur_name
    rec["cur_comment"] = current_comment(ea, is_func)
    if is_runtime(cur_name) or (is_func and is_lib_func(ea)):
        rec["verdict"] = "skip-runtime"
        return rec, ea
    want_name = want.get("name")
    want_comment = want.get("comment")
    # conflict: desired name already used at a different address
    if want_name and want_name != cur_name:
        other = ida_name.get_name_ea(idaapi.BADADDR, want_name)
        if other != idaapi.BADADDR and other != ea:
            rec["verdict"] = "conflict"
            rec["conflict_at"] = "0x%08X" % other
            return rec, ea
    name_ok = (not want_name) or (cur_name == want_name)
    comment_ok = (not want_comment) or (rec["cur_comment"] == want_comment)
    type_pending = bool(want.get("type"))  # type idempotency is best-effort; re-apply is safe/noop
    if name_ok and comment_ok and not type_pending:
        rec["verdict"] = "noop"
        return rec, ea
    rec["verdict"] = "apply"
    return rec, ea


def do_apply(rec, ea, is_func):
    """Apply name/comment/type for an 'apply'-verdict entry. Records per-field results."""
    failed = False
    want_name = rec["want_name"]
    want_comment = rec["want_comment"]
    want_type = rec["want_type"]
    if want_name and rec["cur_name"] != want_name:
        ok = ida_name.set_name(ea, want_name, ida_name.SN_NOWARN | ida_name.SN_FORCE)
        rec["name_result"] = "renamed" if ok else "FAILED"
        failed = failed or (not ok)
    elif want_name:
        rec["name_result"] = "noop"
    if want_comment and rec["cur_comment"] != want_comment:
        ok = set_comment(ea, is_func, want_comment)
        rec["comment_result"] = "commented" if ok else "FAILED"
        failed = failed or (not ok)
    elif want_comment:
        rec["comment_result"] = "noop"
    if want_type:
        ok, detail = apply_named_type(ea, want_type)
        rec["type_result"] = "typed" if ok else ("FAILED:" + detail)
        failed = failed or (not ok)
    rec["result"] = "failed" if failed else "applied"


def main():
    man = parse_manifest(MANIFEST)
    sha = sha256_of_input()
    apply_now = (MODE.strip().lower() == "apply")

    type_decls = []
    if apply_now:
        type_decls = declare_types(man.get("types", []))

    rows = []
    for section, is_func in (("functions", True), ("globals", False)):
        for addr_str, want in sorted(man[section].items()):
            rec, ea = verdict_for(addr_str, want, is_func)
            if apply_now and rec["verdict"] == "apply":
                do_apply(rec, ea, is_func)
            rows.append(rec)

    tally = {}
    for r in rows:
        tally[r["verdict"]] = tally.get(r["verdict"], 0) + 1

    applied = sum(1 for r in rows if r["result"] == "applied")
    failed_rows = [r for r in rows if r["result"] == "failed"]

    doc = {
        "schema": "ida-annotate-batch/1",
        "cluster": CLUSTER,
        "mode": "apply" if apply_now else "dry-run",
        "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "sha256": sha,
        "entries": len(rows),
        "tally": tally,
        "applied": applied,
        "failed": len(failed_rows),
        "type_decls": type_decls,
        "results": rows,
    }
    print("RESULT_JSON:" + json.dumps(doc, ensure_ascii=False))


main()
