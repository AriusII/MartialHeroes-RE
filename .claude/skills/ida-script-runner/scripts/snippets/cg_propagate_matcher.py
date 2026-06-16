#!/usr/bin/env python3
"""
cg_propagate_matcher.py — BinDiff-style call-graph propagation matcher.

Re-anchors named functions from a prior build (C6) to a new build (C7) via:
  1. Seed: RTTI/D0 anchors + unique-string seeds + unique-import seeds
  2. Propagate to fixpoint: callee/caller neighbourhood matching (BinDiff algorithm)
  3. Gate: unique mutual best-match + margin + corroborator check

Reads only; never writes to any IDB, names.yaml, or committed spec.

Outputs:
  <OUT_DIR>/glossary/reanchor_cg_candidates.yaml  -- HIGH confidence
  <OUT_DIR>/glossary/reanchor_cg_med.yaml         -- MED confidence
  <OUT_DIR>/anchor/cg_audit_targets.json          -- all pairs for adversarial audit

Usage:
  python cg_propagate_matcher.py
  Edit the CONFIG block below to point at your C6/C7 data directories.
"""

# === CONFIG ===
C6_CALLGRAPH   = "Docs/RE/_dirty/campaign6/profile/callgraph.jsonl"
C6_PROFILE_DIR = "Docs/RE/_dirty/campaign6/profile"
C7_CALLGRAPH   = "Docs/RE/_dirty/campaign7/profile/callgraph.jsonl"
C7_PROFILE_DIR = "Docs/RE/_dirty/campaign7/profile"
NAMES_YAML     = "Docs/RE/names.yaml"
D0_YAML        = "Docs/RE/_dirty/campaign7/glossary/reanchor_candidates.yaml"
STRING_INDEX   = "Docs/RE/_dirty/campaign7/anchor/string_xref_index.json"
IMPORT_INDEX   = "Docs/RE/_dirty/campaign7/anchor/import_index.json"
OUT_DIR        = "Docs/RE/_dirty/campaign7"   # outputs land under OUT_DIR/glossary/ and OUT_DIR/anchor/

BINARY_SHA256  = "263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee"

# Propagation gating parameters
MIN_CALLEE_SCORE    = 0.20   # min callee-set intersection fraction to consider a match
MARGIN_HIGH         = 0.15   # runner-up must be at least this below winner for HIGH
MARGIN_MED          = 0.05   # runner-up below this -> MED
MAX_PROPAGATION_ROUNDS = 50  # safety cap (raise from 30 to recover long tail)

# Library name prefixes to skip (never anchor FLIRT/lib fns)
LIB_PREFIXES = (
    "__", "j__", "nullsub_", "sub_", "loc_", "byte_", "word_",
    "dword_", "qword_", "unk_", "off_", "stru_", "def_", "asc_",
    "<Lib>",
)
# === END CONFIG ===

import json
import glob
import os
import sys
import re
import yaml


def norm_ea(ea_str):
    try:
        return hex(int(str(ea_str), 16))
    except (ValueError, TypeError):
        return str(ea_str).lower()


def load_callgraph(path):
    cg = {}
    with open(path, encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            d = json.loads(line)
            ea = norm_ea(d["ea"])
            callees = {norm_ea(c) for c in d.get("callees", [])}
            cg[ea] = callees
    return cg


def load_profiles(profile_dir):
    profiles = {}
    for path in sorted(glob.glob(os.path.join(profile_dir, "functions.*.jsonl"))):
        with open(path, encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                d = json.loads(line)
                ea = norm_ea(d["ea"])
                profiles[ea] = d
    return profiles


def build_callers(callgraph):
    callers = {}
    for caller, callees in callgraph.items():
        for callee in callees:
            if callee not in callers:
                callers[callee] = set()
            callers[callee].add(caller)
    return callers


def is_lib_name(name):
    if not name:
        return True
    for pfx in LIB_PREFIXES:
        if name.startswith(pfx):
            return True
    if re.match(r'^sub_[0-9A-Fa-f]+$', name):
        return True
    return False


def score_pair(c6_ea, c7_ea, partial_map, partial_map_inv,
               c6_cg, c7_cg, c6_callers, c7_callers,
               c6_profiles, c7_profiles):
    score = 0.0
    contributors = 0

    c6_callees = c6_cg.get(c6_ea, set())
    c7_callees = c7_cg.get(c7_ea, set())
    if c6_callees or c7_callees:
        matched_callees = sum(
            1 for cc6 in c6_callees if partial_map.get(cc6) in c7_callees
        )
        score += matched_callees / max(len(c6_callees), len(c7_callees), 1)
        contributors += 1
    elif len(c6_callees) == 0 and len(c7_callees) == 0:
        score += 0.5
        contributors += 1

    c6_caller_set = c6_callers.get(c6_ea, set())
    c7_caller_set = c7_callers.get(c7_ea, set())
    if c6_caller_set or c7_caller_set:
        matched_callers = sum(
            1 for cc6 in c6_caller_set if partial_map.get(cc6) in c7_caller_set
        )
        score += matched_callers / max(len(c6_caller_set), len(c7_caller_set), 1)
        contributors += 1

    base_score = score / contributors if contributors > 0 else 0.0

    p6 = c6_profiles.get(c6_ea, {})
    p7 = c7_profiles.get(c7_ea, {})
    corroborators = 0

    if p6.get("klass") and p6.get("klass") == p7.get("klass"):
        corroborators += 1

    def degree_sim(a, b):
        if a == 0 and b == 0:
            return 1.0
        return 1.0 - abs(a - b) / max(a, b, 1)

    if degree_sim(p6.get("in_degree", 0), p7.get("in_degree", 0)) > 0.8 and \
       degree_sim(p6.get("out_degree", 0), p7.get("out_degree", 0)) > 0.8:
        corroborators += 1

    s6, s7 = p6.get("size", 0), p7.get("size", 0)
    if s6 > 0 and s7 > 0 and min(s6, s7) / max(s6, s7) > 0.7:
        corroborators += 1

    if set(p6.get("import_calls", [])) & set(p7.get("import_calls", [])):
        corroborators += 1

    str6, str7 = set(p6.get("string_refs", [])), set(p7.get("string_refs", []))
    if str6 and str7 and str6 & str7:
        corroborators += 2

    return base_score, corroborators


def sanitize_note(text):
    if not text:
        return ""
    text = re.sub(r'@0x[0-9a-fA-F]+', '', text)
    text = re.sub(r'\b(sub_[0-9a-fA-F]+|loc_[0-9a-fA-F]+|_DWORD|__thiscall|__cdecl|__stdcall)\b', '', text)
    return re.sub(r'  +', ' ', text).strip()


def write_glossary_yaml(path, entries, label, binary_sha256):
    lines = [
        f"# {label} -- generated by cg_propagate_matcher.py",
        f"# {len(entries)} entries",
        "binary:",
        "  name: doida.exe",
        f"  sha256: {binary_sha256}",
        "functions:",
    ]
    for ea in sorted(entries.keys(), key=lambda x: int(x, 16)):
        v = entries[ea]
        name = v["name"].replace("'", "\\'")
        comment = v.get("comment", "").replace("'", "\\'").replace("\n", " ")
        conf = v.get("confidence", "high")
        cluster = v.get("cluster", "C7-cg-propagate")
        evidence = v.get("evidence", "").replace("'", "\\'")
        lines.append(f"  '{ea}':")
        lines.append(f"    name: {name}")
        if comment:
            lines.append(f"    comment: '{comment}'")
        lines.append(f"    confidence: {conf}")
        lines.append(f"    cluster: {cluster}")
        lines.append(f"    evidence: '{evidence}'")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def run():
    print("[1/6] Loading C6 data...")
    c6_cg = load_callgraph(C6_CALLGRAPH)
    c6_profiles = load_profiles(C6_PROFILE_DIR)
    c6_callers = build_callers(c6_cg)

    print("[2/6] Loading C7 data...")
    c7_cg = load_callgraph(C7_CALLGRAPH)
    c7_profiles = load_profiles(C7_PROFILE_DIR)
    c7_callers = build_callers(c7_cg)

    print("[3/6] Loading names, D0 anchors, string/import indices...")
    with open(NAMES_YAML, encoding='utf-8') as f:
        names_data = yaml.safe_load(f)
    named_fns = names_data.get("functions", {})
    c6_name_map = {}
    for ea_str, v in named_fns.items():
        nea = norm_ea(ea_str)
        if isinstance(v, dict) and "name" in v:
            c6_name_map[nea] = v

    with open(D0_YAML, encoding='utf-8') as f:
        d0_data = yaml.safe_load(f)
    d0_fns = d0_data.get("functions", {})
    d0_by_name = {v["name"]: norm_ea(ea) for ea, v in d0_fns.items() if "name" in v}
    d0_new_eas = {norm_ea(ea) for ea in d0_fns.keys()}

    name_to_c6_ea = {}
    for ea, v in c6_name_map.items():
        name = v["name"]
        if name not in name_to_c6_ea:
            name_to_c6_ea[name] = ea

    with open(STRING_INDEX, encoding='utf-8') as f:
        c7_str_index = json.load(f)
    with open(IMPORT_INDEX, encoding='utf-8') as f:
        c7_import_index = json.load(f)

    # Build C6 indices
    c6_str_index = {}
    for ea, prof in c6_profiles.items():
        for s in prof.get("string_refs", []):
            c6_str_index.setdefault(s, []).append(ea)

    c6_import_index = {}
    for ea, prof in c6_profiles.items():
        for api in prof.get("import_calls", []):
            c6_import_index.setdefault(api, []).append(ea)

    partial_map = {}
    partial_map_inv = {}
    seed_rtti_count = seed_string_count = seed_import_count = 0

    # Seed A: RTTI/D0
    for name, c7_ea in d0_by_name.items():
        c6_ea = name_to_c6_ea.get(name)
        if c6_ea and c6_ea in c6_profiles and c7_ea in c7_profiles:
            if c6_ea not in partial_map and c7_ea not in partial_map_inv:
                partial_map[c6_ea] = c7_ea
                partial_map_inv[c7_ea] = c6_ea
                seed_rtti_count += 1

    # Seed B: unique-string
    for string_text, c7_entry in c7_str_index.items():
        c7_eas = c7_entry.get("eas", [])
        if len(c7_eas) != 1:
            continue
        c6_eas = c6_str_index.get(string_text, [])
        if len(c6_eas) != 1:
            continue
        c6_ea, c7_ea = norm_ea(c6_eas[0]), norm_ea(c7_eas[0])
        if c6_ea in c6_profiles and c7_ea in c7_profiles:
            if c6_ea not in partial_map and c7_ea not in partial_map_inv:
                partial_map[c6_ea] = c7_ea
                partial_map_inv[c7_ea] = c6_ea
                seed_string_count += 1

    # Seed C: unique-import
    for api_name, c7_entry in c7_import_index.items():
        c7_eas = c7_entry.get("eas", [])
        if len(c7_eas) != 1:
            continue
        c6_eas = c6_import_index.get(api_name, [])
        if len(c6_eas) != 1:
            continue
        c6_ea, c7_ea = norm_ea(c6_eas[0]), norm_ea(c7_eas[0])
        if c6_ea in c6_profiles and c7_ea in c7_profiles:
            if c6_ea not in partial_map and c7_ea not in partial_map_inv:
                partial_map[c6_ea] = c7_ea
                partial_map_inv[c7_ea] = c6_ea
                seed_import_count += 1

    total_seeds = len(partial_map)
    print(f"  Seeds: RTTI={seed_rtti_count}, string={seed_string_count}, import={seed_import_count}, total={total_seeds}")

    print("[4/6] Propagating to fixpoint...")
    c6_named_eas = set(c6_name_map.keys())
    propagation_rounds = 0
    new_matches_this_round = 1

    while new_matches_this_round > 0 and propagation_rounds < MAX_PROPAGATION_ROUNDS:
        propagation_rounds += 1
        new_matches_this_round = 0
        unmatched_c6 = [ea for ea in c6_named_eas if ea not in partial_map and ea in c6_profiles]

        round_proposals = {}
        for c6_ea in unmatched_c6:
            local_c7 = set()
            for callee_c6 in c6_cg.get(c6_ea, set()):
                callee_c7 = partial_map.get(callee_c6)
                if callee_c7:
                    local_c7.update(c7_callers.get(callee_c7, set()))
            for caller_c6 in c6_callers.get(c6_ea, set()):
                caller_c7 = partial_map.get(caller_c6)
                if caller_c7:
                    local_c7.update(c7_cg.get(caller_c7, set()))
            local_c7 -= set(partial_map_inv.keys())
            if not local_c7:
                continue

            scored = []
            for c7_ea in local_c7:
                if c7_ea not in c7_profiles:
                    continue
                base, corr = score_pair(c6_ea, c7_ea, partial_map, partial_map_inv,
                                        c6_cg, c7_cg, c6_callers, c7_callers,
                                        c6_profiles, c7_profiles)
                if base >= MIN_CALLEE_SCORE or corr >= 2:
                    scored.append((base + corr * 0.05, c7_ea, base, corr))

            if not scored:
                continue
            scored.sort(reverse=True)
            best_total, best_c7, best_base, best_corr = scored[0]
            runner_up = scored[1][0] if len(scored) > 1 else 0.0
            round_proposals[c6_ea] = (best_c7, best_total, runner_up, best_total - runner_up, best_base, best_corr)

        c7_best_c6 = {}
        for c6_ea, (best_c7, best_total, *_) in round_proposals.items():
            if best_c7 not in c7_best_c6 or best_total > c7_best_c6[best_c7][1]:
                c7_best_c6[best_c7] = (c6_ea, best_total)

        accepted = sorted(
            [(c6, c7, score, ru, margin, base, corr)
             for c6, (c7, score, ru, margin, base, corr) in round_proposals.items()
             if c7_best_c6.get(c7, (None,))[0] == c6 and score >= MIN_CALLEE_SCORE],
            key=lambda x: -x[2]
        )
        for c6_ea, c7_ea, *_ in accepted:
            if c6_ea in partial_map or c7_ea in partial_map_inv:
                continue
            partial_map[c6_ea] = c7_ea
            partial_map_inv[c7_ea] = c6_ea
            new_matches_this_round += 1

        print(f"  Round {propagation_rounds}: +{new_matches_this_round} (total {len(partial_map)})")

    print(f"  Fixpoint after {propagation_rounds} rounds, {len(partial_map)} total")

    print("[5/6] Classifying HIGH / MED...")
    high_entries = {}
    med_entries = {}
    pair_scores = {}

    for c6_ea, c7_ea in partial_map.items():
        local_c7 = set()
        for callee_c6 in c6_cg.get(c6_ea, set()):
            callee_c7 = partial_map.get(callee_c6)
            if callee_c7:
                local_c7.update(c7_callers.get(callee_c7, set()))
        for caller_c6 in c6_callers.get(c6_ea, set()):
            caller_c7 = partial_map.get(caller_c6)
            if caller_c7:
                local_c7.update(c7_cg.get(caller_c7, set()))
        local_c7.add(c7_ea)

        scored = []
        for cc7 in local_c7:
            if cc7 not in c7_profiles:
                continue
            base, corr = score_pair(c6_ea, cc7, partial_map, partial_map_inv,
                                    c6_cg, c7_cg, c6_callers, c7_callers,
                                    c6_profiles, c7_profiles)
            scored.append((base + corr * 0.05, cc7, base, corr))
        scored.sort(reverse=True)
        best_total, _, best_base, best_corr = scored[0] if scored else (0.5, c7_ea, 0.5, 0)
        runner_up = scored[1][0] if len(scored) > 1 else 0.0
        pair_scores[c6_ea] = (c7_ea, best_total - runner_up, best_base, best_corr, best_total)

    for c6_ea, c7_ea in partial_map.items():
        name_entry = c6_name_map.get(c6_ea)
        if not name_entry:
            continue
        name = name_entry.get("name", "")
        if is_lib_name(name):
            continue
        if c7_ea in d0_new_eas:
            continue

        clean_note = sanitize_note(name_entry.get("note", ""))
        _, margin, base, corr, total = pair_scores.get(c6_ea, (c7_ea, 1.0, 1.0, 3, 1.0))

        p6 = c6_profiles.get(c6_ea, {})
        p7 = c7_profiles.get(c7_ea, {})
        evidence_parts = [f"callee_score={base:.2f}", f"margin={margin:.2f}", f"corroborators={corr}"]
        if p6.get("klass") and p6.get("klass") == p7.get("klass"):
            evidence_parts.append(f"klass={p6['klass']}")

        entry = {
            "name": name,
            "comment": clean_note,
            "confidence": "high" if margin >= MARGIN_HIGH and corr >= 1 else "med",
            "cluster": "C7-cg-propagate",
            "evidence": " | ".join(evidence_parts),
        }
        if margin >= MARGIN_HIGH and corr >= 1:
            high_entries[c7_ea] = entry
        else:
            med_entries[c7_ea] = entry

    print(f"  HIGH={len(high_entries)}, MED={len(med_entries)}")

    print("[6/6] Writing outputs...")
    out_high = os.path.join(OUT_DIR, "glossary", "reanchor_cg_candidates.yaml")
    out_med  = os.path.join(OUT_DIR, "glossary", "reanchor_cg_med.yaml")
    out_audit = os.path.join(OUT_DIR, "anchor", "cg_audit_targets.json")

    write_glossary_yaml(out_high, high_entries, "HIGH call-graph propagation anchors", BINARY_SHA256)
    write_glossary_yaml(out_med, med_entries, "MED call-graph propagation anchors", BINARY_SHA256)

    audit_data = {}
    for c6_ea, c7_ea in partial_map.items():
        name_entry = c6_name_map.get(c6_ea, {})
        _, margin, base, corr, total = pair_scores.get(c6_ea, (c7_ea, 0, 0, 0, 0))
        audit_data[c7_ea] = {
            "c6_ea": c6_ea, "name": name_entry.get("name", ""),
            "margin": margin, "base_score": base, "corroborators": corr,
            "confidence": "high" if margin >= MARGIN_HIGH and corr >= 1 else "med",
            "note": name_entry.get("note", ""),
        }
    os.makedirs(os.path.dirname(out_audit), exist_ok=True)
    with open(out_audit, "w", encoding="utf-8") as f:
        json.dump(audit_data, f, indent=2)

    print(f"Wrote: {out_high}, {out_med}, {out_audit}")
    print(f"Seeds: RTTI={seed_rtti_count} string={seed_string_count} import={seed_import_count}")
    print(f"Rounds: {propagation_rounds}, Map size: {len(partial_map)}")


if __name__ == "__main__":
    run()
