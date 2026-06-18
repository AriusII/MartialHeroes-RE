# c01_manifest_gen.py — READ-ONLY C01 "rtti-class-core" annotation PROPOSAL generator
#
# Reads classes.json produced by rtti_harvest.py, then for every non-boost/std class:
#   1. Reads vtable slot function EAs directly from IDA memory (ida_bytes, not decompiler)
#   2. Deduplicates: each EA is attributed to the base-most class that exposes it
#   3. Proposes names: <SanitizedClass>__VFunc_<NN>  (NN = zero-padded 2 digit slot index)
#   4. Detects ctor (xref writes vtable ptr into [this+0] near func entry) and dtor patterns
#   5. Emits C struct declarations per class (single-inheritance prefix + size padding)
#
# Outputs (DIRTY — under Docs/RE/_dirty/ only):
#   Docs/RE/_dirty/campaign6/comprehension/C01/names.proposed.yaml
#   Docs/RE/_dirty/campaign6/comprehension/C01/types.proposed.md
#   Docs/RE/_dirty/campaign6/comprehension/C01/summary.md
#
# RUN INSIDE IDA PRO 9.3 (IDAPython) via mcp__ida__py_exec_file.
# READ-ONLY: no rename / set_name / set_type / patch calls anywhere.
#
# DIRTY: derived from copyrighted binary — output goes ONLY under Docs/RE/_dirty/.

# === CONFIG ===
CLASSES_JSON  = r"C:\Users\Arius\RiderProjects\MartialHeroes\Docs\RE\_dirty\campaign6\rtti\classes.json"
OUT_DIR       = r"C:\Users\Arius\RiderProjects\MartialHeroes\Docs\RE\_dirty\campaign6\comprehension\C01"
MAX_SLOTS     = 128   # max vtable slots to read per class (safety cap)
MAX_CTOR_SCAN = 60    # max instructions to scan in a function for vtable-write pattern
SHA256_PIN    = "63fcaf8e81a61097c68d22ae82514dded54e59c41c480850a568a0f0d79eb9df"
# Namespaces to skip entirely (runtime / third-party)
SKIP_PREFIXES = ("boost::", "std::", "__")
# ==============

import json
import os
import re
import datetime
import hashlib

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_bytes
    import ida_nalt
except ImportError as exc:
    raise SystemExit("c01_manifest_gen.py must run inside IDA Pro (IDAPython): %s" % exc)

PTR = 4  # 32-bit image


# ── helpers ──────────────────────────────────────────────────────────────────

def _verify_sha():
    try:
        path = ida_nalt.get_input_file_path()
        if path:
            h = hashlib.sha256()
            with open(path, "rb") as fh:
                for chunk in iter(lambda: fh.read(1 << 20), b""):
                    h.update(chunk)
            return h.hexdigest().lower()
    except Exception:
        pass
    try:
        return ida_nalt.retrieve_input_file_sha256().hex().lower()
    except Exception:
        return ""


def _sanitize_id(name):
    """Convert a C++ class name to a valid C identifier prefix."""
    # Replace :: with __ (scope separator)
    s = name.replace("::", "__")
    # Strip template arguments <...>
    s = re.sub(r"<[^>]*>", "", s)
    # Replace remaining non-identifier chars
    s = re.sub(r"[^A-Za-z0-9_]+", "_", s)
    # Collapse multiple underscores
    s = re.sub(r"_+", "_", s)
    s = s.strip("_")
    return s[:60] or "Unknown"


def _is_default_name(name):
    """True if IDA auto-generated the name (i.e., analyst hasn't named it)."""
    if not name:
        return True
    for pfx in ("sub_", "loc_", "locret_", "off_", "dword_", "word_", "byte_",
                "unk_", "asc_", "stru_", "flt_", "dbl_", "tbyte_", "jpt_", "algn_",
                "nullsub_", "j_"):
        if name.startswith(pfx):
            return True
    return False


def _is_runtime_or_crt(name):
    """True for MSVC runtime / boost / STL / import-stub symbols."""
    if not name:
        return False
    for pfx in ("__", "_imp_", "j___", "_RTC_", "_CxxThrow", "__security_",
                 "_acmdln", "_wcmdln", "__scrt", "__crt"):
        if name.startswith(pfx):
            return True
    # MSVC mangled
    if name.startswith("?"):
        return True
    if name in ("mainCRTStartup", "wmainCRTStartup", "WinMainCRTStartup",
                "_initterm", "_initterm_e", "atexit", "__report_gsfailure"):
        return True
    f = ida_funcs.get_func(idc.get_name_ea_simple(name) if isinstance(name, str) else idaapi.BADADDR)
    return False


def _read_vtable_slots_raw(vtable_ea, slot_count):
    """Read up to slot_count function EAs from vtable (raw dword reads, no decompiler)."""
    slots = []
    for i in range(min(slot_count, MAX_SLOTS)):
        slot_ea = vtable_ea + i * PTR
        target = idc.get_wide_dword(slot_ea)
        if not ida_bytes.is_loaded(target):
            break
        f = ida_funcs.get_func(target)
        if not (f and f.start_ea == target):
            break
        slots.append((i, slot_ea, target))
    return slots


def _get_func_name(ea):
    return ida_name.get_name(ea) or ("sub_%X" % ea)


def _detect_scalar_deleting_dtor(func_ea, func_name):
    """True if the function looks like a scalar-deleting destructor."""
    low = (func_name or "").lower()
    if "scalar_deleting" in low or "vector_deleting" in low:
        return True
    if "destructor" in low:
        return True
    # Heuristic: tiny function at slot 0 that calls another function and then operator delete
    return False


def _detect_dtor(func_ea, func_name, slot_idx):
    """True if the function is a non-deleting destructor."""
    low = (func_name or "").lower()
    if "destructor" in low and "deleting" not in low:
        return True
    return False


def _find_ctor_xrefs(vtable_ea):
    """
    Find functions that write vtable_ea into [ecx/this+0] near their entry.
    These are constructor candidates.
    Returns list of (func_ea, confidence).
    """
    ctors = []
    for xref in idautils.XrefsTo(vtable_ea, 0):
        ref_ea = xref.frm
        f = ida_funcs.get_func(ref_ea)
        if not f:
            continue
        func_ea = f.start_ea
        # Only consider xrefs that are near the function entry (within first ~80 bytes)
        if ref_ea - func_ea > 80:
            continue
        # Check instruction: should be a MOV [reg], vtable_ea or MOV [reg+0], vtable_ea
        mnem = idc.print_insn_mnem(ref_ea).lower()
        if mnem != "mov":
            continue
        op0_type = idc.get_operand_type(ref_ea, 0)
        # dst must be memory operand ([reg] or [reg+0])
        if op0_type not in (idc.o_phrase, idc.o_displ):
            continue
        # Confirm it's actually storing the vtable_ea
        op1_val = idc.get_operand_value(ref_ea, 1)
        if op1_val != vtable_ea:
            continue
        fname = _get_func_name(func_ea)
        # Skip if already well-named (not a default name)
        conf = "high" if _is_default_name(fname) else "med"
        ctors.append((func_ea, conf))
    # Deduplicate by func_ea
    seen = set()
    out = []
    for ea, conf in ctors:
        if ea not in seen:
            seen.add(ea)
            out.append((ea, conf))
    return out


# ── build base-index for dedupe ───────────────────────────────────────────────

def _build_depth_index(classes):
    """
    For each class, compute its depth in the inheritance chain
    (index in base_chain; 0 = deepest base). Lower depth = more base.
    Returns {class_name: depth} where depth = len(base_chain) - index_of_self.
    Actually: depth = position in base_chain (0 = self = most derived context,
    last = root base). We want base-most = smallest position in base_chain.

    The dedupe rule: for a given func EA appearing in multiple vtables,
    attribute it to the class whose vtable_ea it comes from, AND that class
    is the one appearing earliest (most-base) in any other class's base_chain.

    Simplified: assign to the class that has it at the smallest base_chain depth
    across all classes that include it. base_chain[0] = self, base_chain[-1] = root.
    So "most base" = last in base_chain = largest index = deepest ancestor.
    """
    # class_name -> depth score (number of derived classes that have it as a base)
    derived_count = {}
    for cls in classes:
        name = cls["class"]
        for i, base in enumerate(cls["base_chain"]):
            if base != name:
                derived_count[base] = derived_count.get(base, 0) + 1
    return derived_count


# ── main ──────────────────────────────────────────────────────────────────────

def main():
    # SHA pin check
    sha = _verify_sha()
    if SHA256_PIN and sha and sha != SHA256_PIN.lower():
        print("[C01] ERROR: SHA256 mismatch — wrong binary. Expected %s got %s" % (SHA256_PIN, sha))
        return
    print("[C01] SHA256 OK: %s" % sha)

    # Load classes.json
    with open(CLASSES_JSON, encoding="utf-8") as fh:
        all_classes = json.load(fh)
    print("[C01] loaded %d classes from classes.json" % len(all_classes))

    # Filter: skip boost:: / std:: / __ namespaces
    classes = [c for c in all_classes
               if not any(c["class"].startswith(p) for p in SKIP_PREFIXES)]
    skipped_ns = len(all_classes) - len(classes)
    print("[C01] skipping %d boost/std/__ classes; processing %d" % (skipped_ns, len(classes)))

    os.makedirs(OUT_DIR, exist_ok=True)

    # Build derived_count to help with dedupe (how many classes inherit each class)
    derived_count = _build_depth_index(classes)

    # Map: class_name -> sanitized_id
    sanitized = {c["class"]: _sanitize_id(c["class"]) for c in classes}

    # ── Pass 1: collect ALL (func_ea -> list of (class_name, slot_idx, vtable_ea)) ──
    # Then for each func_ea, pick the attribution to the "most base" class
    # (the one that appears in the most other classes' base_chains as a base)
    print("[C01] pass 1: reading vtable slots from IDA memory...")
    func_appearances = {}  # func_ea -> [(class_name, slot_idx)]
    class_slots = {}       # class_name -> [(slot_idx, slot_ea, func_ea)]
    class_ctor_dtor = {}   # class_name -> {ctors: [], dtor_ea: int, sdd_ea: int}

    for cls in classes:
        cname = cls["class"]
        vtable_ea_str = cls["vtable_ea"]
        vtable_ea = int(vtable_ea_str, 16)
        slot_count = cls.get("slot_count", 0)

        slots = _read_vtable_slots_raw(vtable_ea, slot_count)
        class_slots[cname] = slots

        for (slot_idx, slot_ea, func_ea) in slots:
            if func_ea not in func_appearances:
                func_appearances[func_ea] = []
            func_appearances[func_ea].append((cname, slot_idx))

        # Detect ctors via xref scan
        ctors = _find_ctor_xrefs(vtable_ea)
        class_ctor_dtor[cname] = {"ctors": ctors, "dtor_ea": 0, "sdd_ea": 0}

    print("[C01] unique function EAs across all vtables: %d" % len(func_appearances))

    # ── Pass 2: attribute each func_ea to its most-base class ──
    print("[C01] pass 2: deduplication / base attribution...")

    def _attribution_score(class_name):
        """Higher score = more-derived = we prefer lower score for attribution."""
        # More derived classes have a longer base_chain
        # We want the class with the most derived classes using it as base
        return derived_count.get(class_name, 0)

    func_to_class = {}  # func_ea -> (chosen_class_name, slot_idx, shared_count)
    for func_ea, appearances in func_appearances.items():
        # Pick the class with the highest derived_count (most base-like)
        # In case of tie, pick the one with smallest slot_idx, then class_name alphabetically
        best = max(appearances,
                   key=lambda x: (_attribution_score(x[0]), -x[1], x[0]))
        func_to_class[func_ea] = (best[0], best[1], len(appearances))

    # ── Pass 3: build proposals ──
    print("[C01] pass 3: building name proposals...")

    functions_proposals = {}   # "0xADDR" -> {name, comment, confidence, cluster}
    skipped_already_named = 0
    skipped_runtime = 0
    ctors_found = 0
    dtors_found = 0
    sdd_found = 0
    total_proposed = 0
    failed_classes = []
    structs_data = []  # list of (class_name, sanitized, base_chain, obj_size_hint)

    for cls in classes:
        cname = cls["class"]
        san = sanitized[cname]
        vtable_ea_str = cls["vtable_ea"]
        vtable_ea = int(vtable_ea_str, 16)
        base_chain = cls.get("base_chain", [])
        obj_size_hint = cls.get("object_size_hint", 0)

        slots = class_slots.get(cname, [])
        if not slots and cls.get("slot_count", 0) > 0:
            failed_classes.append(cname)

        # Propose vtable slot names
        for (slot_idx, slot_ea, func_ea) in slots:
            fname = _get_func_name(func_ea)
            addr_key = "0x%08X" % func_ea

            # Skip if already meaningfully named
            if not _is_default_name(fname):
                skipped_already_named += 1
                continue

            # Skip runtime/CRT
            if _is_runtime_or_crt(fname):
                skipped_runtime += 1
                continue

            # Check attribution: is this func attributed to this class?
            attribution = func_to_class.get(func_ea)
            if attribution is None:
                continue
            attr_class, attr_slot, shared_count = attribution
            if attr_class != cname:
                # This func is inherited — attributed to base class, skip here
                continue

            # Already proposed at this address?
            if addr_key in functions_proposals:
                continue

            # Detect role
            low_fname = fname.lower()
            is_sdd = ("scalar_deleting" in low_fname or "vector_deleting" in low_fname)
            is_dtor = (not is_sdd and ("destructor" in low_fname or ("??1" in fname and "@@" in fname)))

            if is_sdd:
                proposed_name = "%s__VFunc_ScalarDeletingDtor" % san
                role_comment = "scalar-deleting destructor"
                confidence = "high"
                sdd_found += 1
            elif is_dtor:
                proposed_name = "%s__dtor" % san
                role_comment = "destructor"
                confidence = "high"
                dtors_found += 1
            else:
                proposed_name = "%s__VFunc_%02d" % (san, slot_idx)
                role_comment = "vtable slot %d" % slot_idx
                confidence = "med"

            shared_note = ""
            if shared_count > 1:
                shared_note = " (shared by %d classes)" % shared_count

            comment = "%s %s%s." % (cname, role_comment, shared_note)
            if len(comment) > 160:
                comment = comment[:157] + "..."

            functions_proposals[addr_key] = {
                "name":       proposed_name,
                "comment":    comment,
                "confidence": confidence,
                "cluster":    "C01",
            }
            total_proposed += 1

        # Ctor proposals
        ctor_dtor = class_ctor_dtor.get(cname, {})
        for (ctor_ea, conf) in ctor_dtor.get("ctors", []):
            addr_key = "0x%08X" % ctor_ea
            fname = _get_func_name(ctor_ea)
            if not _is_default_name(fname):
                skipped_already_named += 1
                continue
            if addr_key in functions_proposals:
                continue
            proposed_name = "%s__ctor" % san
            comment = "%s constructor (vtable-write xref pattern)." % cname
            if len(comment) > 160:
                comment = comment[:157] + "..."
            functions_proposals[addr_key] = {
                "name":       proposed_name,
                "comment":    comment,
                "confidence": conf,
                "cluster":    "C01",
            }
            ctors_found += 1
            total_proposed += 1

        # Struct record
        structs_data.append((cname, san, base_chain, obj_size_hint))

    print("[C01] proposals: %d unique function EAs" % total_proposed)
    print("[C01] skipped already-named: %d, runtime: %d" % (skipped_already_named, skipped_runtime))
    print("[C01] ctors found: %d, dtors found: %d, SDD found: %d" % (ctors_found, dtors_found, sdd_found))

    # ── Emit names.proposed.yaml ──
    yaml_lines = [
        "# C01 rtti-class-core annotation PROPOSAL",
        "# Generated: %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "# Binary SHA256: %s" % sha,
        "# READ-ONLY proposal — Phase D applies these; do NOT rename manually.",
        "# Confidence: high=pattern-certain, med=heuristic, low=weak-signal",
        "#",
        "functions:",
    ]
    for addr_key in sorted(functions_proposals.keys()):
        entry = functions_proposals[addr_key]
        # Escape quotes in comment
        safe_comment = entry["comment"].replace('"', "'")
        yaml_lines.append(
            '  %s: { name: %s, comment: "%s", confidence: %s, cluster: %s }' % (
                addr_key, entry["name"], safe_comment, entry["confidence"], entry["cluster"]
            )
        )
    yaml_path = os.path.join(OUT_DIR, "names.proposed.yaml")
    with open(yaml_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(yaml_lines) + "\n")
    print("[C01] wrote %s (%d entries)" % (yaml_path, total_proposed))

    # ── Emit types.proposed.md ──
    md_lines = [
        "# C01 — rtti-class-core: C Struct Declarations (PROPOSAL)",
        "",
        "> Generated %s from RTTI harvest. DIRTY — do not commit." % datetime.datetime.now().strftime("%Y-%m-%d"),
        "> These are proposed layouts derived from base_chain + object_size_hint.",
        "> Phase D (struct-recovery) must verify offsets before spec promotion.",
        "",
        "## Base → Derived Table",
        "",
        "| Class | Sanitized ID | Direct Base | Chain Depth | Size Hint | Slots |",
        "|---|---|---|---|---|---|",
    ]
    for (cname, san, base_chain, obj_size) in structs_data:
        direct_base = base_chain[1] if len(base_chain) > 1 else "(root)"
        depth = len(base_chain) - 1
        slot_count = len(class_slots.get(cname, []))
        md_lines.append("| %s | %s | %s | %d | %d | %d |" % (
            cname, san, direct_base, depth, obj_size, slot_count))

    md_lines += ["", "## C Struct Declarations", ""]

    for (cname, san, base_chain, obj_size) in structs_data:
        direct_base = base_chain[1] if len(base_chain) > 1 else None
        base_san = _sanitize_id(direct_base) if direct_base else None

        md_lines.append("```c")
        if base_san and base_san != san:
            md_lines.append("/* %s : %s */" % (cname, direct_base))
            md_lines.append("struct %s {" % san)
            md_lines.append("    struct %s __base;  /* inherited prefix */" % base_san)
        else:
            md_lines.append("/* %s (root) */" % cname)
            md_lines.append("struct %s {" % san)
            md_lines.append("    void *__vftable;")

        pad = obj_size - PTR if obj_size > PTR else 0
        if pad > 0:
            md_lines.append("    char _pad[%d];  /* size_hint=%d, %d after vptr/base */" % (
                pad, obj_size, pad))
        md_lines.append("};")
        md_lines.append("```")
        md_lines.append("")

    types_path = os.path.join(OUT_DIR, "types.proposed.md")
    with open(types_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(md_lines) + "\n")
    print("[C01] wrote %s" % types_path)

    # ── Emit summary.md ──
    total_classes_in_json = len(all_classes)
    classes_processed = len(classes)
    zero_slot_classes = sum(1 for c in classes if c.get("slot_count", 0) == 0)
    total_unique_func_eas = len(func_appearances)
    inherited_only = sum(1 for c in classes
                         for (si, sea, fea) in class_slots.get(c["class"], [])
                         if func_to_class.get(fea, (None,))[0] != c["class"])

    summary_lines = [
        "# C01 — rtti-class-core: Summary",
        "",
        "Generated: %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "Binary SHA256: `%s`" % sha,
        "",
        "## Counts",
        "",
        "| Metric | Value |",
        "|---|---|",
        "| Classes in classes.json | %d |" % total_classes_in_json,
        "| boost/std/__ skipped | %d |" % skipped_ns,
        "| Classes processed | %d |" % classes_processed,
        "| Classes with 0 slots | %d |" % zero_slot_classes,
        "| Unique function EAs across all vtables | %d |" % total_unique_func_eas,
        "| Functions proposed (named in YAML) | %d |" % total_proposed,
        "| Ctors detected | %d |" % ctors_found,
        "| Dtors detected | %d |" % dtors_found,
        "| Scalar-deleting dtors detected | %d |" % sdd_found,
        "| Slots skipped (already named) | %d |" % skipped_already_named,
        "| Slots skipped (runtime/CRT) | %d |" % skipped_runtime,
        "| Inherited-only slot appearances | %d |" % inherited_only,
        "| Failed classes (slot read error) | %d |" % len(failed_classes),
        "| Struct declarations emitted | %d |" % len(structs_data),
        "",
        "## Output Files",
        "",
        "- `%s`" % yaml_path,
        "- `%s`" % types_path,
        "",
    ]
    if failed_classes:
        summary_lines += ["## Failed Classes", ""]
        for fc in failed_classes:
            summary_lines.append("- %s" % fc)
        summary_lines.append("")

    summary_path = os.path.join(OUT_DIR, "summary.md")
    with open(summary_path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(summary_lines) + "\n")
    print("[C01] wrote %s" % summary_path)

    # Sample output for verification
    sample = list(functions_proposals.items())[:15]
    print("\n[C01] SAMPLE (first 15 proposals):")
    for addr, entry in sample:
        print("  %s -> %s  [%s]  # %s" % (
            addr, entry["name"], entry["confidence"], entry["comment"][:80]))

    print("\n[C01] DONE.")
    print("C01_RESULT:" + json.dumps({
        "ok": True,
        "classes_processed": classes_processed,
        "skipped_ns": skipped_ns,
        "unique_func_eas": total_unique_func_eas,
        "proposed": total_proposed,
        "ctors": ctors_found,
        "dtors": dtors_found,
        "sdd": sdd_found,
        "skipped_named": skipped_already_named,
        "skipped_runtime": skipped_runtime,
        "failed_classes": failed_classes,
        "structs_emitted": len(structs_data),
        "yaml_path": yaml_path,
        "types_path": types_path,
        "summary_path": summary_path,
    }, ensure_ascii=False))


main()
