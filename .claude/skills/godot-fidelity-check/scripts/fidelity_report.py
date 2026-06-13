#!/usr/bin/env python3
"""fidelity_report.py — score a Godot-client check against the recovered-fact oracle.

Read-only. This script does NOT read any game asset, binary, or capture. It takes the
*observations* you gathered from a headless run + a screenshot (passed as plain flags),
compares them against the project's recovered facts (the expected oracle), and emits a
structured gap report grouped by gap CLASS with a likely fix + owning engineer per gap.

It enumerates EXPECTATIONS and KNOWN DEBTS so a fresh session does not re-report a known
debt as a new bug. It reads nothing from disk by default; pass --log to scan a captured
headless log (text only — Godot stdout, never asset bytes) for error signatures.

  # === CONFIG === (override via flags; no edits to the logic below needed)
"""
import argparse
import json
import re
import sys

# === CONFIG ===
# The recovered-fact oracle for the canonical demo area. Mirrors CLAUDE.md
# "Recovered asset mappings" + "Coordinate conventions". Override --area to skip
# the area-2 population counts when checking a different scene.
EXPECTED = {
    "area2_buildings": 779,   # populated walled town (area 2)
    "area2_actors": 40,       # monsters + NPCs in area 2
    "cell_units": 1024,       # a cell is 1024 world units
    "grid": 65,               # height grid is 65x65
    "spacing": 16,            # grid spacing (16 * 64 = 1024)
}

# Coordinate conventions (sign rules). A world that MIRRORS = one of these is wrong.
COORD_RULES = [
    "WORLD geometry negates Z: (x,y,z) -> (x,y,-z) (Helpers/WorldCoordinates.ToGodot).",
    "MESH-LOCAL .skn geometry negates X (handled inside SknMeshBuilder).",
]

# The asset-reproduction chains to eyeball, each cited to its committed spec.
ASSET_CHAINS = [
    ("terrain-texture",
     ".ted TextureIndexGrid byte -> .map TERRAIN/BUILDING TEXTURES[idx-1].intTexId "
     "-> bgtexture.txt[id] -> data/map000/texture/<rel>.dds (textures GLOBAL under map000)",
     "Docs/RE/formats/terrain.md"),
    ("char-skin",
     ".skn IdA -> data/char/skin.txt col4 -> col5 tex_id -> data/char/tex{res}/{id}.png",
     "Docs/RE/formats/mesh.md"),
    ("char-bind-idle",
     ".skn IdB -> data/char/bind/g{IdB}.bnd; idle via actormotion.txt col2==IdB -> col16 "
     "-> data/char/mot/g{id}.mot",
     "Docs/RE/formats/animation.md"),
]

# Known open debts — DO NOT re-report these as new gaps. (CLAUDE.md "Debts".)
KNOWN_DEBTS = [
    ("skinning", "Character skinning explodes the mesh; avatar rendered STATIC (no anim).",
     "godot-skinning-specialist"),
    ("npc-fallback-y", "NPCs spawn at a fallback Y before async terrain height resolves.",
     "godot-presentation-engineer"),
    ("env-dark", "EnvironmentNode is too dark (atmosphere/lighting needs tuning).",
     "godot-shader-specialist"),
    ("water", "Water is unwired.",
     "godot-shader-specialist"),
]

# Gap class -> default owning engineer (the godot engineer who FIXES it; this skill only reports).
CLASS_OWNER = {
    "visual": "godot-presentation-engineer",
    "coordinate": "godot-presentation-engineer / godot-input-engineer",
    "material": "godot-shader-specialist",
    "missing-asset": "assets-mapping-engineer / godot-presentation-engineer",
    "behavior": "godot-input-engineer / application-engineer",
}

# Error signatures to scan a captured headless log for (text only — never asset bytes).
LOG_SIGNATURES = [
    ("SCRIPT ERROR", "behavior", "A C# script threw; _Ready/_Process broke."),
    ("Unhandled exception", "behavior", "Managed exception in a node script."),
    ("Failed to load", "missing-asset", "An asset/scene path did not resolve."),
    ("Cannot open file", "missing-asset", "A referenced file is missing/misnamed."),
    ("does not exist", "missing-asset", "A res:// path is wrong."),
    ("Parse Error", "visual", "A malformed .tscn (see the silently-ignored-script trap)."),
]


def known_debt_for(text):
    t = text.lower()
    for key, desc, owner in KNOWN_DEBTS:
        for token in key.split("-"):
            if token in t:
                return desc
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--area", default="2",
                    help="area id under check (population counts apply to area 2 only)")
    ap.add_argument("--log", default=None,
                    help="optional path to a captured headless stdout log (TEXT only) to scan "
                         "for engine error signatures")
    ap.add_argument("--gap", action="append", default=[], metavar="CLASS:DESC:FILE_OR_NODE",
                    help="record an observed gap, e.g. "
                         "'visual:terrain patch (3,4) blank:World.tscn:TerrainNode'. Repeatable.")
    ap.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    args = ap.parse_args()

    out = {"area": args.area, "expectations": [], "coord_rules": COORD_RULES,
           "asset_chains": [], "known_debts": [], "gaps": [], "log_hits": []}

    if args.area == "2":
        out["expectations"].append(f"buildings present: {EXPECTED['area2_buildings']}")
        out["expectations"].append(f"mob/NPC actors present: {EXPECTED['area2_actors']}")
    out["expectations"].append(f"cell={EXPECTED['cell_units']}u, grid={EXPECTED['grid']}x"
                               f"{EXPECTED['grid']}, spacing={EXPECTED['spacing']}")
    out["expectations"].append("player upright + textured (NOT exploded)")
    for name, chain, spec in ASSET_CHAINS:
        out["asset_chains"].append({"name": name, "chain": chain, "spec": spec})
    for key, desc, owner in KNOWN_DEBTS:
        out["known_debts"].append({"id": key, "desc": desc, "owner": owner})

    # Scan a captured log (text) for engine error signatures.
    if args.log:
        try:
            with open(args.log, "r", encoding="utf-8", errors="replace") as fh:
                logtext = fh.read()
        except OSError as exc:
            print(f"WARN: could not read log {args.log}: {exc}", file=sys.stderr)
            logtext = ""
        for sig, klass, hint in LOG_SIGNATURES:
            for m in re.finditer(re.escape(sig), logtext):
                line = logtext.count("\n", 0, m.start()) + 1
                out["log_hits"].append({"signature": sig, "class": klass,
                                        "log_line": line, "hint": hint})

    # Record the operator-supplied observed gaps.
    for spec in args.gap:
        parts = spec.split(":", 2)
        if len(parts) < 2:
            print(f"WARN: bad --gap '{spec}' (need CLASS:DESC[:FILE_OR_NODE])", file=sys.stderr)
            continue
        klass = parts[0].strip()
        desc = parts[1].strip()
        loc = parts[2].strip() if len(parts) == 3 else ""
        debt = known_debt_for(klass + " " + desc + " " + loc)
        out["gaps"].append({
            "class": klass,
            "desc": desc,
            "location": loc,
            "owner": CLASS_OWNER.get(klass, "godot-render-reviewer"),
            "known_debt": debt,  # non-null => DO NOT report as new; it's a tracked debt
        })

    if args.json:
        print(json.dumps(out, indent=2))
        return

    # Human-readable report.
    print(f"=== godot-fidelity-check — area {args.area} (N2: 1:1 fidelity) ===\n")
    print("EXPECTED (oracle = recovered facts):")
    for e in out["expectations"]:
        print(f"  - {e}")
    print("\nCOORDINATE RULES (a mirrored world = a sign bug here):")
    for c in out["coord_rules"]:
        print(f"  - {c}")
    print("\nASSET CHAINS to eyeball (cite the spec):")
    for c in out["asset_chains"]:
        print(f"  - [{c['name']}] {c['chain']}   (spec: {c['spec']})")
    print("\nKNOWN DEBTS (DO NOT re-report as new gaps):")
    for d in out["known_debts"]:
        print(f"  - [{d['id']}] {d['desc']}  -> owner: {d['owner']}")
    if out["log_hits"]:
        print("\nLOG ERROR SIGNATURES found:")
        for h in out["log_hits"]:
            print(f"  - {h['signature']} (log:{h['log_line']}) [{h['class']}] {h['hint']}")
    print("\nOBSERVED GAPS:")
    if not out["gaps"]:
        print("  (none recorded — pass --gap CLASS:DESC:FILE_OR_NODE for each visual/coordinate/"
              "material/missing-asset/behavior gap you saw)")
    for g in out["gaps"]:
        tag = "  *** KNOWN DEBT (do not re-report new) ***" if g["known_debt"] else ""
        loc = f"  @ {g['location']}" if g["location"] else ""
        print(f"  - [{g['class']}] {g['desc']}{loc}{tag}")
        print(f"      owner: {g['owner']}")
        if g["known_debt"]:
            print(f"      tracked: {g['known_debt']}")
    print("\nREAD-ONLY: this skill REPORTS gaps; the godot engineers FIX them.")


if __name__ == "__main__":
    main()
