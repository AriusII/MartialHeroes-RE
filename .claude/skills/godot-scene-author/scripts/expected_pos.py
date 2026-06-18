#!/usr/bin/env python3
"""Compute the EXPECTED Godot world position for a Martial Heroes asset, for coordinate checks.

Stdlib only. Applies the project's confirmed conventions:
  * cell grid: 1 cell = 1024 world units; 65x65 height grid with spacing 16 (16 * 64 = 1024)
  * WORLD handedness flip: (x, y, z) -> (x, y, -z)   (Helpers/WorldCoordinates.ToGodot)

This is a HYPOTHESIS generator for the godot-coordinate-check skill. The exact corner-vs-centre
offset of a cell, and whether a given placement uses cell corner or cell centre, depend on the
project's placement code — reconcile this output against that code; do not treat it as gospel.

Usage:
    # From cell indices (cx, cz). --corner gives the cell's min corner; default gives its centre.
    python expected_pos.py --cell 2,3
    python expected_pos.py --cell 2,3 --corner --y 12.5

    # From a legacy world-space position (left-handed Y-up); just applies the Z negate.
    python expected_pos.py --legacy 1536.0,0,2048.0
"""

import argparse
import sys

CELL_UNITS = 1024.0   # one cell edge, in world units
GRID = 65             # height samples per edge (65x65)
SPACING = 16.0        # world units between grid samples (16 * 64 = 1024)


def to_godot(x: float, y: float, z: float) -> tuple[float, float, float]:
    """WORLD handedness flip: negate Z. Mirrors Helpers/WorldCoordinates.ToGodot."""
    return (x, y, -z)


def cell_to_legacy(cx: float, cz: float, corner: bool) -> tuple[float, float]:
    """Cell indices -> legacy world X/Z. Corner = cell min; otherwise cell centre."""
    base_x = cx * CELL_UNITS
    base_z = cz * CELL_UNITS
    if corner:
        return base_x, base_z
    return base_x + CELL_UNITS / 2.0, base_z + CELL_UNITS / 2.0


def parse_triplet(s: str) -> tuple[float, float, float]:
    parts = [p.strip() for p in s.split(",")]
    if len(parts) != 3:
        raise argparse.ArgumentTypeError(f"expected 'x,y,z', got '{s}'")
    return tuple(float(p) for p in parts)  # type: ignore[return-value]


def parse_pair(s: str) -> tuple[float, float]:
    parts = [p.strip() for p in s.split(",")]
    if len(parts) != 2:
        raise argparse.ArgumentTypeError(f"expected 'cx,cz', got '{s}'")
    return float(parts[0]), float(parts[1])


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description="Expected Godot world position for a coordinate check.")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--cell", type=parse_pair, metavar="CX,CZ",
                   help="cell indices; maps via 1024-unit cells then Z-negate")
    g.add_argument("--legacy", type=parse_triplet, metavar="X,Y,Z",
                   help="legacy world-space position (left-handed Y-up); applies Z-negate")
    ap.add_argument("--corner", action="store_true",
                    help="with --cell, use the cell's MIN corner instead of its centre")
    ap.add_argument("--y", type=float, default=0.0,
                    help="legacy Y (height) to assume for --cell (default 0)")
    args = ap.parse_args(argv)

    print(f"conventions: cell={CELL_UNITS:g}u  grid={GRID}x{GRID}  spacing={SPACING:g}u  "
          f"world flip=(x,y,z)->(x,y,-z)")

    if args.cell is not None:
        cx, cz = args.cell
        lx, lz = cell_to_legacy(cx, cz, args.corner)
        ly = args.y
        which = "corner" if args.corner else "centre"
        gx, gy, gz = to_godot(lx, ly, lz)
        print(f"cell ({cx:g},{cz:g}) {which}:  legacy=({lx:g},{ly:g},{lz:g})  "
              f"-> EXPECTED Godot=({gx:g},{gy:g},{gz:g})")
    else:
        lx, ly, lz = args.legacy
        gx, gy, gz = to_godot(lx, ly, lz)
        print(f"legacy=({lx:g},{ly:g},{lz:g})  -> EXPECTED Godot=({gx:g},{gy:g},{gz:g})")

    print("Compare this against the AABB-PROBE centre from _aabb_probe.gd. A clean sign flip on one")
    print("axis = a handedness bug (Z dropped, or X negated instead of Z = the gray-world bug).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
