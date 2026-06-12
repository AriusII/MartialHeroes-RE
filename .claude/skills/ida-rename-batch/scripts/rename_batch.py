# rename_batch.py — apply ONLY glossary-approved names from Docs/RE/names.yaml to the legacy
# Martial Heroes IDB (functions + globals), keyed by address. Dry-run first; never invents names.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool.
#
# FIREWALL: names.yaml is the CLEAN, COMMITTED glossary (a name map, not pseudo-code), so writing
# its names into the IDB is firewall-safe. New names are NOT applied here — they go to a _dirty/
# proposal file for a maintainer. This script never writes any decompiler output anywhere.
#
# Inline the names.yaml functions:/globals: maps into NAMES_YAML below before running (so IDA need
# not reach the repo path). Then set MODE = "dry-run" (report) or "apply" (rename).

# === CONFIG ===
MODE = "dry-run"        # "dry-run" -> report verdicts only ; "apply" -> perform the renames
# Paste the relevant slices of Docs/RE/names.yaml here. Address keys are quoted strings.
# Minimal flat shape understood by the tiny parser below:
#   functions:
#     "0x004A1230": { name: "RecvPacketDispatch" }
#   globals:
#     "0x006C1200": { name: "g_RollingKey" }
NAMES_YAML = r"""
functions: {}
globals: {}
"""
# ==============

import re
import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_name
    import ida_funcs
    import ida_bytes
    import ida_nalt
except ImportError as exc:
    raise SystemExit("rename_batch.py must run inside IDA Pro (IDAPython): %s" % exc)


# --- tiny, dependency-free parser for the minimal flat names.yaml shape (no PyYAML in IDA) ---
def parse_names(text):
    out = {"functions": {}, "globals": {}}
    section = None
    line_re = re.compile(r'^\s*"(0x[0-9A-Fa-f]+)"\s*:\s*\{?.*?name\s*:\s*"([^"]+)"')
    for raw in text.splitlines():
        s = raw.rstrip()
        if re.match(r'^\s*functions\s*:', s):
            section = "functions"
            continue
        if re.match(r'^\s*globals\s*:', s):
            section = "globals"
            continue
        if section is None:
            continue
        m = line_re.match(s)
        if m:
            out[section][m.group(1).lower()] = m.group(2)
    return out


_RUNTIME_RE = re.compile(r'^(__|_imp_|j_|\?\?|\?|_RTC_|__security_|mainCRTStartup|std::|_acmdln|_CxxThrow)')


def is_runtime(name):
    if not name:
        return False
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


def verdict_for(addr_str, want, is_func):
    ea = int(addr_str, 16)
    if not ida_bytes.is_loaded(ea):
        return ea, None, "skip-missing"
    cur = ida_name.get_name(ea) or ""
    if is_func and ida_funcs.get_func(ea) is None:
        return ea, cur, "skip-missing"
    if cur == want:
        return ea, cur, "noop"
    if is_runtime(cur) or (is_func and is_lib_func(ea)):
        return ea, cur, "skip-runtime"
    # conflict: desired name already used at a different address
    other = ida_name.get_name_ea(idaapi.BADADDR, want)
    if other != idaapi.BADADDR and other != ea:
        return ea, cur, "conflict"
    return ea, cur, "apply"


def main():
    names = parse_names(NAMES_YAML)
    sha = sha256_of_input()
    rows = []
    for section, is_func in (("functions", True), ("globals", False)):
        for addr_str, want in names[section].items():
            ea, cur, verdict = verdict_for(addr_str, want, is_func)
            rows.append({"section": section, "addr": addr_str, "ea": ea,
                         "cur": cur, "want": want, "verdict": verdict, "result": ""})

    if MODE == "apply":
        for r in rows:
            if r["verdict"] != "apply":
                continue
            ok = ida_name.set_name(r["ea"], r["want"], ida_name.SN_NOWARN | ida_name.SN_FORCE)
            r["result"] = "renamed" if ok else "FAILED"

    counts = {}
    for r in rows:
        counts[r["verdict"]] = counts.get(r["verdict"], 0) + 1

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print("# rename_batch (%s)" % MODE)
    print("- generated: %s" % stamp)
    print("- idb_sha256: %s" % (sha or "(unknown)"))
    print("- entries: %d   verdicts: %s" % (len(rows), counts))
    print("")
    print("| Section | Addr | Current | Desired | Verdict | Result |")
    print("|---|---|---|---|---|---|")
    for r in rows:
        print("| %s | %s | %s | %s | %s | %s |" % (
            r["section"], r["addr"], r["cur"] or "-", r["want"], r["verdict"], r["result"] or "-"))

    if MODE == "apply":
        applied = sum(1 for r in rows if r["result"] == "renamed")
        failed = [r for r in rows if r["result"] == "FAILED"]
        print("\n[rename_batch] applied %d rename(s); %d failure(s)." % (applied, len(failed)))
        for r in failed:
            print("   FAILED %s -> %s" % (r["addr"], r["want"]))
    else:
        print("\n[rename_batch] dry-run only — no IDB changes. Re-run with MODE='apply' to rename "
              "the 'apply'-verdict entries. New (non-glossary) names must be staged for names.yaml.")


main()
