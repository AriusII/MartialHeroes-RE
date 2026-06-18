# names_sync.py — IDAPython: sync Docs/RE/names.yaml <-> the live IDA database.
#
# RUN THIS INSIDE IDA PRO 9.3 via the IDA MCP script-execution tool. It imports
# ida_name/idautils/ida_* which exist only inside IDA. It does NOT depend on PyYAML:
# it parses the simple, flat names.yaml shape with a tiny stdlib parser.
#
# Before sending: (1) paste the EXACT text of Docs/RE/names.yaml into the NAMES_YAML block
# below, and (2) set MODE to "dry-run" or "apply".
#
# Output: one JSON line prefixed "NAMES_JSON:" — per-entry verdicts, apply/pull lists, the
# binary SHA-256. The calling skill presents the dry-run diff, then re-runs with MODE="apply"
# only after the user confirms.
#
# names.yaml is the project's CLEAN glossary (a name map), so applying it to the IDB and reading
# symbol names back is firewall-safe. This script writes renames to the IDB but never emits
# decompiler output.

# ── set me ──────────────────────────────────────────────────────────────────
MODE = "dry-run"   # "dry-run" | "apply"

NAMES_YAML = r"""
# Paste the exact contents of Docs/RE/names.yaml here before running.
# Only the binary:, functions:, and globals: sections are read.
binary:
  name: "Main.exe"
  sha256: ""
functions: {}
globals: {}
"""
# ────────────────────────────────────────────────────────────────────────────

import json
import hashlib
import re

import idaapi
import idautils
import idc

import ida_nalt
import ida_name
import ida_funcs
import ida_bytes


# ── tiny YAML reader for the flat names.yaml shape ───────────────────────────
# Supports:
#   binary:
#     sha256: "abcd…"
#   functions:
#     "0x004A1230": { name: "RecvPacketDispatch", note: "…" }
#   globals:
#     "0x006F0040": { name: "g_RollingKey", note: "…" }
# Empty sections may be written as `functions: {}`.

_ADDR_ENTRY = re.compile(
    r'^\s*"(?P<addr>0[xX][0-9A-Fa-f]+)"\s*:\s*\{(?P<body>.*)\}\s*$'
)
_NAME_FIELD = re.compile(r'name\s*:\s*"(?P<name>[^"]+)"')
_SHA_FIELD = re.compile(r'^\s*sha256\s*:\s*"?(?P<sha>[0-9A-Fa-f]*)"?\s*$')


def parse_names_yaml(text):
    binary_sha = ""
    funcs = {}
    globs = {}
    section = None
    for raw in text.splitlines():
        line = raw.rstrip("\n")
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        # top-level section headers (no leading whitespace, end with ':')
        if re.match(r'^binary\s*:\s*$', line):
            section = "binary"; continue
        if re.match(r'^functions\s*:', line):
            section = "functions"
            # allow inline empty map
            continue
        if re.match(r'^globals\s*:', line):
            section = "globals"
            continue
        if re.match(r'^(crypto|opcodes)\s*:', line):
            section = "other"
            continue
        if section == "binary":
            m = _SHA_FIELD.match(line)
            if m:
                binary_sha = m.group("sha")
            continue
        if section in ("functions", "globals"):
            m = _ADDR_ENTRY.match(line)
            if not m:
                continue
            nm = _NAME_FIELD.search(m.group("body"))
            if not nm:
                continue
            addr = m.group("addr")
            target = funcs if section == "functions" else globs
            target[addr] = nm.group("name")
    return binary_sha, funcs, globs


# ── runtime / compiler symbol guard ──────────────────────────────────────────

_RUNTIME_PREFIXES = ("__", "_imp_", "j_", "_RTC_", "_CxxThrow", "__security_",
                     "_acmdln", "_wcmdln", "std::", "__scrt", "__crt")
_RUNTIME_NAMES = {"mainCRTStartup", "wmainCRTStartup", "WinMainCRTStartup",
                  "_initterm", "_initterm_e", "atexit", "__report_gsfailure"}


def _is_runtime_symbol(name, ea):
    if not name:
        return False
    if name in _RUNTIME_NAMES:
        return True
    for p in _RUNTIME_PREFIXES:
        if name.startswith(p):
            return True
    # MSVC C++ mangled names begin with '?'
    if name.startswith("?"):
        return True
    # functions IDA flagged as library/FLIRT-matched are runtime; never rename.
    f = ida_funcs.get_func(ea)
    if f is not None and (f.flags & ida_funcs.FUNC_LIB):
        return True
    return False


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


def _parse_addr(addr_str):
    try:
        return int(addr_str, 16)
    except (ValueError, TypeError):
        return idaapi.BADADDR


def _current_name(ea):
    n = ida_name.get_name(ea)
    return n or ""


def _name_in_use_elsewhere(name, ea):
    other = idc.get_name_ea_simple(name)
    return other != idaapi.BADADDR and other != ea


def _verdict(addr_str, desired, apply_now):
    ea = _parse_addr(addr_str)
    rec = {"addr": addr_str, "desired": desired, "current": "", "verdict": ""}
    if ea == idaapi.BADADDR:
        rec["verdict"] = "skip-missing"
        return rec
    flags = ida_bytes.get_flags(ea)
    if not ida_bytes.is_loaded(ea) or flags == 0:
        rec["verdict"] = "skip-missing"
        return rec
    cur = _current_name(ea)
    rec["current"] = cur
    if _is_runtime_symbol(cur, ea):
        rec["verdict"] = "skip-runtime"
        return rec
    if cur == desired:
        rec["verdict"] = "noop"
        return rec
    if _name_in_use_elsewhere(desired, ea):
        rec["verdict"] = "conflict"
        rec["conflict_at"] = "0x%08X" % idc.get_name_ea_simple(desired)
        return rec
    if not apply_now:
        rec["verdict"] = "apply"
        return rec
    # apply
    ok = ida_name.set_name(ea, desired, ida_name.SN_NOWARN | ida_name.SN_FORCE)
    rec["verdict"] = "applied" if ok else "failed"
    return rec


def _is_default_name(name):
    if not name:
        return True
    for pfx in ("sub_", "loc_", "locret_", "off_", "dword_", "word_", "byte_",
                "unk_", "asc_", "stru_", "flt_", "dbl_", "tbyte_", "jpt_", "algn_",
                "nullsub_", "j_"):
        if name.startswith(pfx):
            return True
    return False


def _pull_candidates(known_addrs):
    """IDB symbols that are analyst-named, non-runtime, and not already in names.yaml."""
    out = []
    known = set(a.lower() for a in known_addrs)
    # functions
    for ea in idautils.Functions():
        name = ida_funcs.get_func_name(ea) or ""
        if _is_default_name(name) or _is_runtime_symbol(name, ea):
            continue
        key = "0x%08X" % ea
        if key.lower() in known:
            continue
        out.append({"addr": key, "name": name, "kind": "function"})
    # named data globals
    for ea, name in idautils.Names():
        if _is_default_name(name) or _is_runtime_symbol(name, ea):
            continue
        if ida_funcs.get_func(ea) is not None:
            continue
        key = "0x%08X" % ea
        if key.lower() in known:
            continue
        out.append({"addr": key, "name": name, "kind": "global"})
    return out


def main():
    sha = _sha256_of_input()
    yaml_sha, funcs, globs = parse_names_yaml(NAMES_YAML)
    apply_now = (MODE.strip().lower() == "apply")

    func_results = [_verdict(a, n, apply_now) for a, n in sorted(funcs.items())]
    glob_results = [_verdict(a, n, apply_now) for a, n in sorted(globs.items())]

    known = list(funcs.keys()) + list(globs.keys())
    pulls = _pull_candidates(known)

    def _tally(results):
        t = {}
        for r in results:
            t[r["verdict"]] = t.get(r["verdict"], 0) + 1
        return t

    doc = {
        "schema": "ida-naming-sync/1",
        "mode": "apply" if apply_now else "dry-run",
        "sha256": sha,
        "yaml_sha256": yaml_sha,
        "sha_match": (bool(sha) and bool(yaml_sha) and sha.lower() == yaml_sha.lower()),
        "functions": {"results": func_results, "tally": _tally(func_results)},
        "globals": {"results": glob_results, "tally": _tally(glob_results)},
        "pull_candidates": pulls,
        "pull_count": len(pulls),
    }
    print("NAMES_JSON:" + json.dumps(doc, ensure_ascii=False))


main()
