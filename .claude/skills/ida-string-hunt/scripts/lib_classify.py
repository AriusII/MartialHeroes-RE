# lib_classify.py
# Reusable IDAPython snippet — classifies unnamed functions as third-party library
# vs game code by scanning string references, RTTI vtable membership, and address-band fill.
#
# READ-ONLY on the IDB. Writes three output files under _dirty/campaign6/lib/.
# Idempotent: safe to re-run any number of times.
#
# === CONFIG ===

REPO_ROOT = r"C:\Users\Arius\RiderProjects\MartialHeroes"

# Absolute output paths (never rely on IDA's CWD)
OUT_DIR         = REPO_ROOT + r"\Docs\RE\_dirty\campaign6\lib"
OUT_PROPOSALS   = OUT_DIR + r"\lib_proposals.yaml"
OUT_EAS_JSON    = OUT_DIR + r"\lib_eas.json"
OUT_SUMMARY     = OUT_DIR + r"\summary.md"

# Maximum gap (bytes) allowed in a band-fill run before we stop propagating the library tag
BAND_MAX_GAP_BYTES = 0x4000   # 16 KB

# Minimum fraction of HIGH-confidence hits in a window to trigger band-fill
BAND_MIN_DENSITY  = 0.40      # 40% of functions in the window must be HIGH to fill the rest

# Sliding window size (number of functions) for density check
BAND_WINDOW       = 30

# === LIBRARY MARKER DEFINITIONS ===
# Each entry: (lib_name, [substring patterns]) — case-insensitive match against referenced strings.
# Pattern is matched against the raw IDA string content (bytes decoded as latin-1).

LIB_STRING_MARKERS = [
    ("CxImage",  ["cximage", "cxfile", "cxmemfile", "cximagejpg", "cximagebmp",
                  "cximagepng", "cximagetga", "cximagegif", "cximageico",
                  "cx_image", "cximageraw"]),
    ("zlib",     ["inflate", "deflate", "incorrect data check",
                  "invalid distance", "invalid block type",
                  "1.2.3", "1.2.5", "1.2.8", "1.1.4", "1.1.3",
                  "zlib version", "zlib.h", "zlib error",
                  "unknown compression method", "invalid stored block lengths",
                  "too many length or distance symbols"]),
    ("libpng",   ["png warning", "png error", "png_", "libpng version",
                  "libpng error", "png: ", "invalid png"]),
    ("libjpeg",  ["jpeg", "jfif", "exif", "jerror", "libjpeg",
                  "not a jpeg", "invalid jpeg"]),
    ("Lua",      ["lua_", "lual_", "[string \"", "attempt to ",
                  "stack overflow", "'for'", "lua5", "lua 5.",
                  "luaopen_", "cannot open", "bad argument",
                  "value expected", "table expected",
                  "function expected", "number expected",
                  "string expected", "attempt to index",
                  "attempt to call", "attempt to perform",
                  "attempt to concatenate", "attempt to get length",
                  "attempt to compare", "attempt to yield"]),
    ("FLINT",    ["flintpp", "lint.cpp", "lint.h", "bignum", "flint",
                  "montgomery", "modular exponentiation"]),
    ("XTrap",    ["xtrap", "x-trap", "antihack", "xmag_", "mhack"]),
    ("BugTrap",  ["bugtrap", "bt_", "btsendreport", "btsetapplicationname",
                  "btsetreportformat"]),
    ("Boost",    ["boost::", "boost/", "boost_", "bad_format_string",
                  "bad_lexical_cast", "too_few_args", "too_many_args"]),
    ("STL",      ["std::bad_alloc", "std::exception", "std::bad_cast",
                  "bad_alloc", "bad_cast", "bad_typeid",
                  "std::length_error", "std::out_of_range",
                  "std::invalid_argument", "std::runtime_error",
                  "std::logic_error", "vector<T> too long",
                  "map/set<T> too long", "string too long"]),
]

# RTTI vtable EA -> library mapping (seeded from classes.json analysis)
# Format: (lib_name, [vtable_ea_hex_strings])
LIB_RTTI_VTABLES = [
    ("CxImage", [
        "0x007414A4",  # CxFile
        "0x00741834",  # CxMemFile
        "0x007414E0",  # CxIOFile
        "0x0072C9D8",  # CxImage
        "0x0074151C",  # CxImageBMP
        "0x00741524",  # CxImageTGA
        "0x0074176C",  # CxImageJPG
    ]),
    ("Boost", [
        "0x0071EFA4",  # boost::detail::sp_counted_base
        "0x0071EF94",  # boost::exception_detail::clone_base
        "0x0071F328",  # boost::exception
        "0x0071F2EC",  # boost::io altstringbuf
        "0x0071F35C",  # boost::io bad_format_string error_info_injector
        "0x0071F394",  # boost::io too_few_args error_info_injector
        "0x0071F3CC",  # boost::io too_many_args error_info_injector
        "0x00729C58",  # boost::bad_lexical_cast error_info_injector
        "0x0071F29C",  # boost::detail sp_counted_impl_pd
        "0x0071F338",  # boost::io bad_format_string error_info_injector (alt)
    ]),
]

# === END CONFIG ===

import os
import json
import re
import idaapi
import idautils
import idc
import ida_bytes
import ida_name
import ida_funcs
import ida_segment
import ida_nalt

print("[lib_classify] Starting library classification sweep...")

# ─────────────────────────────────────────────────────────────
# Step 0: Helper — detect default (unnamed) IDA function name
# ─────────────────────────────────────────────────────────────

_DEFAULT_RE = re.compile(r'^(sub_|nullsub_|locret_|unknown_libname_|j_|__|\?\?|_|@)([0-9A-Fa-f]+)$')

def is_default_name(name: str) -> bool:
    """Return True if the IDA name looks auto-generated (not an analyst rename)."""
    if not name:
        return True
    if _DEFAULT_RE.match(name):
        return True
    return False

# CRT / runtime names to skip
_CRT_PREFIXES = (
    "__", "_CRT", "_MSC", "?_", "?A", "_purecall",
    "operator new", "operator delete",
)
def is_crt(name: str) -> bool:
    if not name:
        return False
    for p in _CRT_PREFIXES:
        if name.startswith(p):
            return True
    return False

# ─────────────────────────────────────────────────────────────
# Step 1: Build string index — ea -> list[str]
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Building string index...")

string_index = {}  # string_ea -> raw_string (latin-1)
for s in idautils.Strings():
    try:
        raw = idc.get_strlit_contents(s.ea, s.length, s.strtype)
        if raw:
            txt = raw.decode("latin-1", errors="replace")
            string_index[s.ea] = txt
    except Exception:
        pass

print(f"[lib_classify]   {len(string_index)} strings indexed")

# ─────────────────────────────────────────────────────────────
# Step 2: Build func -> [string_refs] map via xrefs
# Only process DATA xrefs from code to string EAs
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Building function->string xrefs map...")

# index: func_start_ea -> set of string texts
func_strings = {}  # func_ea -> list[str]

import ida_xref
for s_ea, s_txt in string_index.items():
    for xref in idautils.DataRefsTo(s_ea):
        fn = ida_funcs.get_func(xref)
        if fn is None:
            continue
        fea = fn.start_ea
        if fea not in func_strings:
            func_strings[fea] = []
        func_strings[fea].append(s_txt)

print(f"[lib_classify]   {len(func_strings)} functions have string refs")

# ─────────────────────────────────────────────────────────────
# Step 3: Build vtable -> member functions map via xrefs
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Building vtable->member function map...")

vtable_lib_map = {}  # vtable_ea -> lib_name
for lib_name, vtable_eas in LIB_RTTI_VTABLES:
    for vt_ea_str in vtable_eas:
        vt_ea = int(vt_ea_str, 16)
        vtable_lib_map[vt_ea] = lib_name

# For each vtable, walk the pointer array to collect member function EAs
vtable_func_to_lib = {}  # func_ea -> lib_name
for vt_ea, lib_name in vtable_lib_map.items():
    # Read up to 64 slots (4 bytes each, 32-bit binary)
    for slot in range(64):
        ptr_ea = vt_ea + slot * 4
        fn_ea = idc.get_wide_dword(ptr_ea)
        if fn_ea == 0 or fn_ea == 0xFFFFFFFF:
            break
        fn = ida_funcs.get_func(fn_ea)
        if fn is None:
            break
        if fn.start_ea not in vtable_func_to_lib:
            vtable_func_to_lib[fn.start_ea] = lib_name

print(f"[lib_classify]   {len(vtable_func_to_lib)} functions identified via vtable membership")

# ─────────────────────────────────────────────────────────────
# Step 4: HIGH-confidence classification via string matching
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Running string-based HIGH-confidence classification...")

# Pre-compile marker patterns
compiled_markers = []
for lib_name, patterns in LIB_STRING_MARKERS:
    compiled_pats = [p.lower() for p in patterns]
    compiled_markers.append((lib_name, compiled_pats))

def classify_by_strings(strs):
    """Return (lib_name, 'high') or None."""
    for s in strs:
        sl = s.lower()
        for lib_name, pats in compiled_markers:
            for pat in pats:
                if pat in sl:
                    return (lib_name, "high")
    return None

# Also precompute already-named lib associations from analyst names
NAMED_LIB_PREFIXES = {
    "Flint_": "FLINT",
    "CxImage": "CxImage",
    "CxFile": "CxImage",
    "CxMem": "CxImage",
    "CxIO": "CxImage",
    "DiskFile_": "CxImage",   # CxImage's IOFile subclass
    "CxX_": "CxImage",
}

# ─────────────────────────────────────────────────────────────
# Step 5: Enumerate ALL functions — classify each
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Classifying all functions...")

all_funcs = list(idautils.Functions())
all_funcs.sort()

# Structure: { func_ea: { 'name': str, 'lib': str|None, 'confidence': str, 'is_named': bool } }
func_info = {}

for fea in all_funcs:
    raw_name = idc.get_func_name(fea)
    is_named = not is_default_name(raw_name) and not is_crt(raw_name)

    # Check if already named with a known library prefix
    named_lib = None
    if is_named:
        for prefix, lib in NAMED_LIB_PREFIXES.items():
            if raw_name.startswith(prefix):
                named_lib = lib
                break

    # String-based classification (HIGH)
    lib_hit = None
    strs = func_strings.get(fea, [])
    if strs:
        lib_hit = classify_by_strings(strs)

    # RTTI vtable membership (HIGH)
    if lib_hit is None and fea in vtable_func_to_lib:
        lib_hit = (vtable_func_to_lib[fea], "high")

    func_info[fea] = {
        'name': raw_name,
        'lib': named_lib or (lib_hit[0] if lib_hit else None),
        'confidence': 'named' if is_named else (lib_hit[1] if lib_hit else None),
        'is_named': is_named,
    }

# Count HIGH before band fill
high_count = sum(1 for fi in func_info.values() if fi['confidence'] == 'high')
print(f"[lib_classify]   {high_count} HIGH-confidence library hits (strings/RTTI)")

# ─────────────────────────────────────────────────────────────
# Step 6: Address-band fill — propagate within dense lib windows
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Running band-fill propagation...")

def band_fill(all_funcs_sorted, func_info):
    """Sliding window: if BAND_MIN_DENSITY of functions in a window are the same lib,
    fill the unnamed ones as 'med' confidence. Do NOT cross named game functions."""
    n = len(all_funcs_sorted)
    filled = 0

    for center_idx in range(n):
        fea = all_funcs_sorted[center_idx]
        fi = func_info[fea]

        # Only consider filling unnamed, unclassified functions
        if fi['lib'] is not None or fi['is_named']:
            continue

        # Look at surrounding window
        half = BAND_WINDOW // 2
        start_idx = max(0, center_idx - half)
        end_idx = min(n, center_idx + half + 1)

        window = all_funcs_sorted[start_idx:end_idx]

        # Count lib hits in window
        lib_counts = {}
        named_game_count = 0
        for wea in window:
            wfi = func_info[wea]
            if wfi['is_named'] and wfi['lib'] is None:
                named_game_count += 1  # Named game function — barrier
            elif wfi['lib'] is not None:
                lib_counts[wfi['lib']] = lib_counts.get(wfi['lib'], 0) + 1

        if not lib_counts:
            continue

        # Find dominant library
        dominant_lib = max(lib_counts, key=lib_counts.get)
        dom_count = lib_counts[dominant_lib]
        win_size = len(window)
        density = dom_count / win_size

        # Only fill if density is high enough AND no significant named-game barrier nearby
        if density >= BAND_MIN_DENSITY and named_game_count == 0:
            # Also check address gap — don't fill across huge gaps
            prev_lib_ea = None
            next_lib_ea = None
            for wea in reversed(all_funcs_sorted[:center_idx]):
                if func_info[wea]['lib'] == dominant_lib:
                    prev_lib_ea = wea
                    break
            for wea in all_funcs_sorted[center_idx+1:]:
                if func_info[wea]['lib'] == dominant_lib:
                    next_lib_ea = wea
                    break

            gap_ok = True
            if prev_lib_ea and (fea - prev_lib_ea) > BAND_MAX_GAP_BYTES:
                gap_ok = False
            if next_lib_ea and (next_lib_ea - fea) > BAND_MAX_GAP_BYTES:
                gap_ok = False

            if gap_ok:
                func_info[fea]['lib'] = dominant_lib
                func_info[fea]['confidence'] = 'med'
                filled += 1

    return filled

filled = band_fill(all_funcs, func_info)
print(f"[lib_classify]   {filled} functions filled via address-band propagation")

# ─────────────────────────────────────────────────────────────
# Step 7: Compute statistics and address ranges
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Computing statistics...")

lib_stats = {}  # lib_name -> { 'high': int, 'med': int, 'named': int, 'eas': [] }
game_unnamed_count = 0
game_named_count = 0

for fea, fi in func_info.items():
    lib = fi['lib']
    conf = fi['confidence']
    is_named = fi['is_named']

    if lib is None:
        if is_named:
            game_named_count += 1
        else:
            game_unnamed_count += 1
    else:
        if lib not in lib_stats:
            lib_stats[lib] = {'high': 0, 'med': 0, 'named': 0, 'eas': []}
        lib_stats[lib]['eas'].append(fea)
        if conf == 'high':
            lib_stats[lib]['high'] += 1
        elif conf == 'med':
            lib_stats[lib]['med'] += 1
        elif conf == 'named':
            lib_stats[lib]['named'] += 1

# Compute address ranges per library
lib_ranges = {}
for lib, stats in lib_stats.items():
    eas = sorted(stats['eas'])
    if eas:
        lib_ranges[lib] = (min(eas), max(eas))

# ─────────────────────────────────────────────────────────────
# Step 8: Build proposal entries (unnamed library functions only)
# ─────────────────────────────────────────────────────────────
print("[lib_classify] Building proposals...")

proposals = {}
eas_by_lib = {lib: [] for lib in lib_stats}

for fea, fi in func_info.items():
    lib = fi['lib']
    if lib is None:
        continue

    ea_hex = hex(fea)
    # Add to lib EA set (includes named members)
    eas_by_lib[lib].append(ea_hex)

    # Only propose for unnamed functions (skip named, skip CRT)
    if fi['is_named']:
        continue
    if is_crt(fi['name']):
        continue

    conf = fi['confidence']
    if conf not in ('high', 'med'):
        continue

    # Proposed name: Lib__hexaddr
    lib_tag = lib.replace("::", "_").replace(" ", "_")
    proposed_name = f"{lib_tag}__{fea:08X}"

    proposals[ea_hex] = {
        'name': proposed_name,
        'comment': f"Third-party {lib} (out of game-RE scope). Confidence: {conf}.",
        'confidence': conf,
        'cluster': f"LIB-{lib}",
    }

print(f"[lib_classify]   {len(proposals)} proposal entries (unnamed lib functions)")

# ─────────────────────────────────────────────────────────────
# Step 9: Write output files
# ─────────────────────────────────────────────────────────────
os.makedirs(OUT_DIR, exist_ok=True)

# 9a: lib_proposals.yaml
print("[lib_classify] Writing lib_proposals.yaml...")
with open(OUT_PROPOSALS, 'w', encoding='utf-8') as f:
    f.write("# lib_proposals.yaml — auto-generated by lib_classify.py\n")
    f.write("# Proposed names for unnamed third-party library functions.\n")
    f.write("# READ-ONLY analysis; apply via ida-naming-sync workflow only.\n")
    f.write("proposals:\n")
    for ea_hex in sorted(proposals.keys(), key=lambda x: int(x, 16)):
        p = proposals[ea_hex]
        f.write(f'  "{ea_hex}":\n')
        f.write(f'    name: {p["name"]}\n')
        f.write(f'    comment: "{p["comment"]}"\n')
        f.write(f'    confidence: {p["confidence"]}\n')
        f.write(f'    cluster: {p["cluster"]}\n')

# 9b: lib_eas.json
print("[lib_classify] Writing lib_eas.json...")
with open(OUT_EAS_JSON, 'w', encoding='utf-8') as f:
    json.dump({lib: sorted(eas, key=lambda x: int(x, 16))
               for lib, eas in eas_by_lib.items()}, f, indent=2)

# 9c: summary.md
print("[lib_classify] Writing summary.md...")
total_classified = sum(
    s['high'] + s['med'] + s['named'] for s in lib_stats.values()
)
total_funcs = len(all_funcs)

with open(OUT_SUMMARY, 'w', encoding='utf-8') as f:
    f.write("# Library Classification Summary\n\n")
    f.write(f"Generated by `lib_classify.py` (read-only IDB sweep).\n\n")
    f.write(f"**Total functions:** {total_funcs}  \n")
    f.write(f"**Total classified as library:** {total_classified}  \n")
    f.write(f"**Game-code named (resolved):** {game_named_count}  \n")
    f.write(f"**Game-code unnamed (still unresolved):** {game_unnamed_count}  \n\n")
    f.write("## Per-Library Counts\n\n")
    f.write("| Library | HIGH | MED (band) | Named (analyst) | Total | EA Range |\n")
    f.write("|---------|------|------------|-----------------|-------|----------|\n")
    for lib in sorted(lib_stats.keys()):
        s = lib_stats[lib]
        rng = lib_ranges.get(lib, (0, 0))
        f.write(f"| {lib} | {s['high']} | {s['med']} | {s['named']} | "
                f"{s['high']+s['med']+s['named']} | "
                f"0x{rng[0]:08X}–0x{rng[1]:08X} |\n")

    f.write("\n## Band Address Ranges\n\n")
    for lib in sorted(lib_stats.keys()):
        rng = lib_ranges.get(lib, (0, 0))
        total_lib = lib_stats[lib]['high'] + lib_stats[lib]['med'] + lib_stats[lib]['named']
        f.write(f"- **{lib}**: 0x{rng[0]:08X} – 0x{rng[1]:08X} ({total_lib} functions)\n")

    f.write("\n## Sample Proposals (first 20)\n\n")
    f.write("```yaml\n")
    count = 0
    for ea_hex in sorted(proposals.keys(), key=lambda x: int(x, 16)):
        if count >= 20:
            break
        p = proposals[ea_hex]
        f.write(f'{ea_hex}:\n')
        f.write(f'  name: {p["name"]}\n')
        f.write(f'  confidence: {p["confidence"]}\n')
        f.write(f'  cluster: {p["cluster"]}\n')
        count += 1
    f.write("```\n")

# ─────────────────────────────────────────────────────────────
# Step 10: Print Markdown result to stdout
# ─────────────────────────────────────────────────────────────

print("\n## Library Classification Results\n")
print(f"**Binary:** doida.exe | **Total functions:** {total_funcs}\n")
print(f"**Total classified as library:** {total_classified}")
print(f"**Remaining game-code unnamed:** {game_unnamed_count}")
print(f"**Game-code named (analyst-resolved):** {game_named_count}\n")

print("### Per-Library Breakdown\n")
print("| Library | HIGH | MED | Named | Total | EA Range |")
print("|---------|------|-----|-------|-------|----------|")
for lib in sorted(lib_stats.keys()):
    s = lib_stats[lib]
    rng = lib_ranges.get(lib, (0, 0))
    print(f"| {lib} | {s['high']} | {s['med']} | {s['named']} | "
          f"{s['high']+s['med']+s['named']} | "
          f"0x{rng[0]:08X}–0x{rng[1]:08X} |")

print(f"\n### Output files written:")
print(f"- {OUT_PROPOSALS}")
print(f"- {OUT_EAS_JSON}")
print(f"- {OUT_SUMMARY}")
print("\n[lib_classify] DONE.")
