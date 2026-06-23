# open_caller_census.py
# Reusable IDAPython snippet — caller→format census for VFS/DiskFile open entry points.
#
# For each configured open entry point, enumerate every call-site (xrefs_to), walk the
# caller's body to harvest:
#   - Format/path string literals reachable from the caller (printf-family arg or
#     immediate string reference before the open call).
#   - All string references in the caller (for context).
#   - Whether the caller is itself called by many other functions (wrapper heuristic).
#
# OUTPUT: structured Markdown table printed to stdout + best-effort write to
#   Docs/RE/_dirty/campaign8/cartography/open-callers.md
#
# READ-ONLY — zero IDB mutations (no rename/comment/type/patch).
# Idempotent — safe to re-run any number of times.
#
# === CONFIG ===
TARGET_FUNCS = {
    # name → EA (confirmed content-verified in build SHA 263bd994)
    "DiskFile_Open_ByValue":    0x609558,
    "DiskFile_Open_ByName":     0x60972D,
    "VFS_FindAndReadEntry":     0x60ABF2,
}

# Path templates / format strings are detected by scanning the caller's immediate
# string references for strings containing "/" or "%" or a known extension.
EXT_HINTS = (
    ".skn", ".bnd", ".mot", ".dds", ".png", ".tga",
    ".ogg", ".wav", ".map", ".ted", ".bud", ".sod",
    ".scr", ".txt", ".xdb", ".lst", ".xeff", ".xobj",
    ".box", ".lua", ".arr", ".bin", ".bge", ".bgm",
    ".run", ".wlk", ".eff", ".mud", ".tol", ".mi",
    ".do", ".csv", ".up", ".exd", ".sc", ".bmp",
    ".fx1", ".fx2", ".fx3", ".fx4", ".fx5", ".fx6", ".fx7",
    ".pre", ".post", ".ver",
)

# Wrapper heuristic: a function with ≤ WRAPPER_SIZE_THRESHOLD bytes and that has
# many xrefs-to is likely a thin wrapper.
WRAPPER_SIZE_THRESHOLD = 128  # bytes
WRAPPER_XREF_THRESHOLD = 8    # distinct callers

# Where to write the markdown output (best-effort)
OUT_MD = r"C:\Users\Arius\RiderProjects\MartialHeroes\Docs\RE\_dirty\campaign8\cartography\open-callers.md"

# Max string refs to list per caller (to keep output manageable)
MAX_STRINGS_PER_CALLER = 8

# === LOGIC (do not edit below) ===

import re
import os
import sys
import collections

import idaapi
import idautils
import idc
import ida_bytes
import ida_funcs
import ida_name
import ida_segment
import ida_xref

# ---- helpers ---------------------------------------------------------------

def get_func_size(ea):
    """Return the byte size of the function containing ea, or 0 if not in a function."""
    f = ida_funcs.get_func(ea)
    if f is None:
        return 0
    return f.size()


def get_func_name(ea):
    """Return the IDA name for the function at ea (or containing ea)."""
    f = ida_funcs.get_func(ea)
    if f:
        n = idc.get_func_name(f.start_ea)
        if n:
            return n
    n = idc.get_name(ea)
    return n if n else "sub_{:X}".format(ea)


def func_start(ea):
    """Return the start EA of the function containing ea."""
    f = ida_funcs.get_func(ea)
    return f.start_ea if f else ea


def is_string_looking(s):
    """Return True if the string looks like a path template or format string."""
    if not s or len(s) < 3:
        return False
    sl = s.lower()
    if "/" in s or "%" in s:
        return True
    for ext in EXT_HINTS:
        if ext in sl:
            return True
    return False


def get_string_at(ea):
    """Try to read a C-string from ea; return it or None."""
    try:
        s = idc.get_strlit_contents(ea, -1, idc.STRTYPE_C)
        if s:
            return s.decode("utf-8", errors="replace")
    except Exception:
        pass
    return None


def collect_string_refs_in_func(func_ea):
    """Walk all instructions in the function and collect string literals referenced."""
    strings = []
    f = ida_funcs.get_func(func_ea)
    if f is None:
        return strings
    for head in idautils.Heads(f.start_ea, f.end_ea):
        # Check each data xref FROM this instruction
        xref = ida_xref.xrefblk_t()
        ok = xref.first_from(head, ida_xref.XREF_DATA)
        while ok:
            target = xref.to
            seg = ida_segment.getseg(target)
            if seg is not None:
                seg_name = idc.get_segm_name(seg.start_ea)
                # Only pick up references into data/rodata segments
                if seg_name in (".rdata", ".data", "const", ".rodata", "data"):
                    s = get_string_at(target)
                    if s and len(s) >= 2:
                        strings.append(s)
            ok = xref.next_from()
    # Deduplicate preserving order
    seen = set()
    result = []
    for s in strings:
        if s not in seen:
            seen.add(s)
            result.append(s)
    return result


def classify_strings(strings):
    """Split into path-template strings and other strings."""
    path_templates = [s for s in strings if is_string_looking(s)]
    others = [s for s in strings if not is_string_looking(s)]
    return path_templates, others


def count_callers_of(ea):
    """Count distinct caller functions of the function at ea."""
    callers = set()
    for xref in idautils.CodeRefsTo(ea, 1):
        f = ida_funcs.get_func(xref)
        if f:
            callers.add(f.start_ea)
    return len(callers)


def is_wrapper(func_ea, caller_count):
    """Heuristic: small function + many callers."""
    size = get_func_size(func_ea)
    return size > 0 and size <= WRAPPER_SIZE_THRESHOLD and caller_count >= WRAPPER_XREF_THRESHOLD


# ---- main census -----------------------------------------------------------

def run_census():
    rows = []  # list of dicts

    for open_name, open_ea in TARGET_FUNCS.items():
        # Verify the EA lands on a function
        f_check = ida_funcs.get_func(open_ea)
        if f_check is None:
            print("[WARN] {} @ 0x{:X} is NOT at a function start — check EA".format(open_name, open_ea))

        # Walk every code xref TO this open function
        for xref_ea in idautils.CodeRefsTo(open_ea, 1):
            caller_start = func_start(xref_ea)
            caller_name = get_func_name(caller_start)
            func_sz = get_func_size(caller_start)

            # Collect strings
            all_strings = collect_string_refs_in_func(caller_start)
            path_templates, other_strings = classify_strings(all_strings)

            # Wrapper heuristic
            n_callers_of_caller = count_callers_of(caller_start)
            wrapper = is_wrapper(caller_start, n_callers_of_caller)

            rows.append({
                "caller_ea": caller_start,
                "caller_name": caller_name,
                "func_size": func_sz,
                "path_templates": path_templates,
                "other_strings": other_strings[:MAX_STRINGS_PER_CALLER],
                "open_router": open_name,
                "wrapper": wrapper,
                "n_callers_of_caller": n_callers_of_caller,
                "call_site_ea": xref_ea,
            })

    # De-duplicate by caller_ea × open_router (same caller may call multiple routers)
    # but keep each (caller, router) combination
    seen_pairs = set()
    deduped = []
    for row in rows:
        key = (row["caller_ea"], row["open_router"])
        if key not in seen_pairs:
            seen_pairs.add(key)
            deduped.append(row)
    rows = deduped

    # Sort: wrappers last, then by number of path templates descending, then by caller name
    rows.sort(key=lambda r: (r["wrapper"], -len(r["path_templates"]), r["caller_name"]))

    return rows


def format_markdown(rows):
    lines = []
    lines.append("# Open-Caller Census — VFS / DiskFile entry points")
    lines.append("")
    lines.append("Build SHA: 263bd994 | Date: 2026-06-15 | READONLY phase")
    lines.append("")
    lines.append("## How to re-run")
    lines.append("")
    lines.append("```")
    lines.append("# In IDA via mcp__ida__py_exec_file:")
    lines.append("# .claude/skills/ida-script-runner/scripts/snippets/open_caller_census.py")
    lines.append("# CONFIG block at top: TARGET_FUNCS dict maps name→EA.")
    lines.append("```")
    lines.append("")
    lines.append("## Caller→Format Census Table")
    lines.append("")

    # Header
    col_hdr = ("| Caller fn (name / ea) | Func size (B) "
               "| Path template / extension literal "
               "| Other string refs (sample) "
               "| Open router called "
               "| Wrapper? (y/n) |")
    col_sep = ("|---|---|---|---|---|---|")
    lines.append(col_hdr)
    lines.append(col_sep)

    for r in rows:
        name_ea = "{} / 0x{:X}".format(r["caller_name"], r["caller_ea"])
        size_s = str(r["func_size"]) if r["func_size"] else "?"
        templates = "; ".join(r["path_templates"][:6]) if r["path_templates"] else "—"
        others = "; ".join(r["other_strings"][:4]) if r["other_strings"] else "—"
        router = r["open_router"]
        wrapper = "y ({} callers)".format(r["n_callers_of_caller"]) if r["wrapper"] else "n"
        lines.append("| {} | {} | {} | {} | {} | {} |".format(
            name_ea, size_s, templates, others, router, wrapper))

    # Tally
    total_callers = len(set(r["caller_ea"] for r in rows))
    total_templates = len(set(
        t for r in rows for t in r["path_templates"]
    ))
    wrapper_count = sum(1 for r in rows if r["wrapper"])
    unresolved = sum(1 for r in rows if not r["path_templates"])

    lines.append("")
    lines.append("## Tally")
    lines.append("")
    lines.append("| Metric | Count |")
    lines.append("|---|---|")
    lines.append("| Distinct caller functions | {} |".format(total_callers))
    lines.append("| Distinct format/path templates | {} |".format(total_templates))
    lines.append("| Wrapper-only callers (heuristic) | {} |".format(wrapper_count))
    lines.append("| Callers with no path template found | {} |".format(unresolved))
    lines.append("| Total (caller × router) rows | {} |".format(len(rows)))
    lines.append("")
    lines.append("## Per-router breakdown")
    lines.append("")
    for open_name in TARGET_FUNCS:
        subset = [r for r in rows if r["open_router"] == open_name]
        lines.append("- **{}**: {} call sites".format(open_name, len(subset)))
    lines.append("")

    return "\n".join(lines)


# ---- entry point -----------------------------------------------------------

def main():
    print("=== open_caller_census.py starting ===")
    rows = run_census()
    md = format_markdown(rows)
    print(md)

    # Best-effort write
    try:
        os.makedirs(os.path.dirname(OUT_MD), exist_ok=True)
        with open(OUT_MD, "w", encoding="utf-8") as fh:
            fh.write(md)
        print("\n[OK] Written to", OUT_MD)
    except Exception as e:
        print("\n[WARN] Could not write to", OUT_MD, ":", e)

    print("=== done ===")


main()
