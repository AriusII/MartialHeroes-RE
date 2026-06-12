# ida_py_template.py — harness for an arbitrary IDAPython query against the legacy
# Martial Heroes client (Main.exe). The ida-py skill's escape hatch.
#
# RUN THIS INSIDE IDA PRO 9.3, via the IDA MCP script-execution tool (e.g.
# mcp__ida__py_exec_file / execute_script / run_python / py_eval). It imports
# idautils/idaapi/ida_* which only exist inside IDA — it does NOT run under a plain CPython.
#
# HOW TO USE
#   1. Set OUT_SLUG in the CONFIG block to a short name for this query.
#   2. Put your analysis in the "# === USER CODE ===" block and assign findings into `result`.
#   3. Run via the MCP exec tool; capture the single line prefixed "RESULT_JSON:".
#   Keep the serialization boilerplate (below USER CODE) untouched so the line stays parseable.
#
# DIRTY: anything emitted here is derived directly from the copyrighted binary. It belongs only
# under Docs/RE/_dirty/queries/ and must never be committed or copied into clean specs / C#.
# Populate `result` with METADATA (addresses, names, counts, bytes/strings you deliberately read)
# — never paste whole disassembly or Hex-Rays pseudo-C into it.

# === CONFIG ===
OUT_SLUG = "query"               # short descriptive slug -> ida_py.<slug>.json
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import json
import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_bytes
    import ida_nalt
except ImportError as exc:
    raise SystemExit("ida_py_template.py must run inside IDA Pro (IDAPython): %s" % exc)


# --- small helpers you may use inside the USER CODE block (all return JSON-safe values) ---

def hexa(ea):
    """Format an effective address as a fixed-width hex string."""
    return "0x%08X" % (ea & 0xFFFFFFFFFFFFFFFF)


def ea_of(symbol):
    """Resolve a symbol name to an EA, or idaapi.BADADDR if unknown."""
    return ida_name.get_name_ea(idaapi.BADADDR, symbol)


def func_name(ea):
    """Canonical/auto name of the function containing ea, or '' if not in a function."""
    f = ida_funcs.get_func(ea)
    if not f:
        return ""
    return ida_name.get_name(f.start_ea) or ("sub_%X" % f.start_ea)


def read_cstr(ea, maxlen=256):
    """Read a short C string at ea (UTF-8/bytes best-effort). Use sparingly; dirty output."""
    raw = ida_bytes.get_strlit_contents(ea, -1, ida_nalt.STRTYPE_C)
    if raw is None:
        return ""
    try:
        text = raw.decode("utf-8", "replace")
    except Exception:
        text = str(raw)
    return text[:maxlen]


def main():
    result = {
        "schema": "ida-py/1",
        "slug": OUT_SLUG,
        "ok": True,
        "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "binary": ida_nalt.get_root_filename() or "",
        # Populate the keys below from your analysis.
        "data": {},
    }
    try:
        # =====================================================================
        # === USER CODE ===  (replace this block; assign into result["data"])
        #
        # Example — count user functions and grab the first few names:
        #   names = []
        #   for ea in idautils.Functions():
        #       nm = ida_name.get_name(ea) or "sub_%X" % ea
        #       names.append({"ea": hexa(ea), "name": nm})
        #   result["data"]["func_count"] = len(names)
        #   result["data"]["first"] = names[:10]
        #
        result["data"]["note"] = "replace the USER CODE block"
        # === END USER CODE ===
        # =====================================================================
        pass
    except Exception as exc:
        result["ok"] = False
        result["error"] = "%s: %s" % (type(exc).__name__, exc)

    line = "RESULT_JSON:" + json.dumps(result, ensure_ascii=False)
    print(line)

    # Best-effort write so the analyst gets a file even if stdout capture is lossy.
    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", OUT_SLUG)[:60] or "query"
        path = os.path.join(OUT_DIR, "ida_py.%s.json" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
        print("[ida-py] wrote %s" % path)
    except Exception as exc:
        print("[ida-py] could not write file (%s); save the RESULT_JSON above via Write." % exc)


main()
