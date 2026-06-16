#!/usr/bin/env python3
"""check_dag.py -- validate the MartialHeroes ProjectReference graph.

Stdlib-only. Walks the repo, reads every MartialHeroes.*.csproj, extracts its
<ProjectReference Include="..."/> edges, and asserts that the resulting graph:

  1. matches the intended dependency DAG exactly (no missing, no unexpected edges),
  2. is strictly downward (a lower-numbered layer never references a higher one,
     and intra-layer references respect the established sub-order),
  3. is acyclic,
  4. never names the transport project ".Pipe" -- the real project is
     "Network.Transport.Pipelines".

Usage:
    python check_dag.py [REPO_ROOT]

REPO_ROOT defaults to the current working directory. Exit code 0 on success;
non-zero on any drift, with a human-readable diff printed to stdout.

This intentionally only governs ProjectReference edges between the 12 core class
libraries. Client.Godot (layer 05) is owned by the godot-csproj-bootstrap skill
and is ignored here. Package references are out of scope.
"""

from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# --- The intended graph -----------------------------------------------------
# Keys are short project names (the "MartialHeroes." prefix is implied).
# Values are the set of short names each project is allowed to reference.
# Anything not listed here must have NO ProjectReferences.

INTENDED: dict[str, set[str]] = {
    # 01.Infrastructure.Shared
    "Shared.Kernel": set(),
    "Shared.Diagnostics": set(),
    # 02.Network.Layer
    "Network.Abstractions": {"Shared.Kernel"},
    "Network.Protocol": {"Shared.Kernel"},
    "Network.Crypto": {"Shared.Kernel"},
    "Network.Transport.Pipelines": {"Network.Abstractions"},
    # 03.Storage.Assets
    "Assets.Vfs": set(),
    "Assets.Parsers": {"Assets.Vfs"},
    "Assets.Mapping": {"Assets.Parsers"},
    # 04.Client.Core
    "Client.Domain": {"Shared.Kernel"},
    # Application IS the packet-handling + login layer: its handlers consume the
    # wire structs/opcodes (Network.Protocol) and its login flow consumes the
    # session handshake (Network.Crypto). Both edges are downward (4->2) and
    # acyclic -- accepted as legitimate by-design edges (CAMPAIGN 11 Phase 3b,
    # "accept + document"; see CLAUDE.md architecture section).
    "Client.Application": {"Client.Domain", "Network.Abstractions", "Network.Protocol", "Network.Crypto"},
    # Infrastructure builds the local catalogues from the binary data tables: it
    # reads decoded records (Assets.Parsers) off the VFS (Assets.Vfs). Both edges
    # are downward (4->3) and acyclic -- accepted as legitimate by-design edges
    # (CAMPAIGN 11 Phase 3b).
    "Client.Infrastructure": {"Client.Application", "Assets.Parsers", "Assets.Vfs"},
}

# Layer number per project, used for the downward-only check. Within a layer,
# ties are broken by ORDER (a lower order may not reference a higher order).
LAYER: dict[str, int] = {
    "Shared.Kernel": 1,
    "Shared.Diagnostics": 1,
    "Network.Abstractions": 2,
    "Network.Protocol": 2,
    "Network.Crypto": 2,
    "Network.Transport.Pipelines": 2,
    "Assets.Vfs": 3,
    "Assets.Parsers": 3,
    "Assets.Mapping": 3,
    "Client.Domain": 4,
    "Client.Application": 4,
    "Client.Infrastructure": 4,
}

# Intra-layer sub-order: a project may only reference a peer with a STRICTLY
# lower order value (so Parsers->Vfs is fine, Vfs->Parsers is upward).
SUBORDER: dict[str, int] = {
    "Shared.Kernel": 0,
    "Shared.Diagnostics": 1,
    "Network.Abstractions": 0,
    "Network.Protocol": 0,
    "Network.Crypto": 0,
    "Network.Transport.Pipelines": 1,
    "Assets.Vfs": 0,
    "Assets.Parsers": 1,
    "Assets.Mapping": 2,
    "Client.Domain": 0,
    "Client.Application": 1,
    "Client.Infrastructure": 2,
}

# Project layer 05 (Godot) is deliberately excluded from this checker.
IGNORED_PROJECTS = {"Client.Godot"}

PREFIX = "MartialHeroes."


def short_name(csproj_path: Path) -> str:
    """'.../MartialHeroes.Network.Crypto.csproj' -> 'Network.Crypto'."""
    stem = csproj_path.stem  # MartialHeroes.Network.Crypto
    if stem.startswith(PREFIX):
        return stem[len(PREFIX):]
    return stem


def find_csprojs(root: Path) -> list[Path]:
    found: list[Path] = []
    for dirpath, dirnames, filenames in os.walk(root):
        # Skip build artefacts and editor caches.
        dirnames[:] = [
            d for d in dirnames
            if d not in {"bin", "obj", ".git", ".godot", "_dirty"}
        ]
        for fn in filenames:
            if fn.startswith(PREFIX) and fn.endswith(".csproj"):
                found.append(Path(dirpath) / fn)
    return found


def parse_references(csproj_path: Path) -> set[str]:
    """Return the set of short project names this csproj references."""
    refs: set[str] = set()
    try:
        tree = ET.parse(csproj_path)
    except ET.ParseError as exc:  # malformed csproj is itself drift
        print(f"  ! cannot parse {csproj_path}: {exc}")
        return refs
    root = tree.getroot()
    # csproj has no XML namespace under the modern SDK, but be defensive.
    for el in root.iter():
        tag = el.tag.split("}")[-1]  # strip any namespace
        if tag != "ProjectReference":
            continue
        include = el.get("Include")
        if not include:
            continue
        # Normalise Windows/Unix separators, take the file name.
        norm = include.replace("\\", "/")
        name = Path(norm).stem
        refs.add(name[len(PREFIX):] if name.startswith(PREFIX) else name)
    return refs


def has_cycle(graph: dict[str, set[str]]) -> list[str] | None:
    """Return a cycle as a list of nodes, or None if the graph is acyclic."""
    WHITE, GRAY, BLACK = 0, 1, 2
    color: dict[str, int] = {n: WHITE for n in graph}
    stack: list[str] = []

    def visit(node: str) -> list[str] | None:
        color[node] = GRAY
        stack.append(node)
        for nxt in graph.get(node, set()):
            if nxt not in color:
                continue  # edge to something outside the core graph
            if color[nxt] == GRAY:
                idx = stack.index(nxt)
                return stack[idx:] + [nxt]
            if color[nxt] == WHITE:
                cyc = visit(nxt)
                if cyc:
                    return cyc
        color[node] = BLACK
        stack.pop()
        return None

    for n in graph:
        if color[n] == WHITE:
            cyc = visit(n)
            if cyc:
                return cyc
    return None


def main(argv: list[str]) -> int:
    root = Path(argv[1]).resolve() if len(argv) > 1 else Path.cwd()
    if not root.exists():
        print(f"FAIL: repo root does not exist: {root}")
        return 2

    print(f"Scanning {root} for MartialHeroes.*.csproj ...")
    csprojs = find_csprojs(root)
    if not csprojs:
        print("FAIL: no MartialHeroes.*.csproj files found.")
        return 2

    actual: dict[str, set[str]] = {}
    pipe_hits: list[str] = []

    for path in csprojs:
        name = short_name(path)
        # Rule 4: the stale ".Pipe" name must never appear.
        if re.search(r"Network\.Transport\.Pipe(?!lines)", name):
            pipe_hits.append(str(path))
        if name in IGNORED_PROJECTS:
            continue
        refs = parse_references(path)
        for r in refs:
            if re.search(r"Network\.Transport\.Pipe(?!lines)", r):
                pipe_hits.append(f"{path} -> {r}")
        actual[name] = refs

    errors: list[str] = []
    warnings: list[str] = []

    # --- Rule 4: .Pipe naming -------------------------------------------------
    if pipe_hits:
        errors.append(
            "Forbidden '.Pipe' naming detected (use Network.Transport.Pipelines):"
        )
        errors.extend(f"    {h}" for h in pipe_hits)

    # --- Presence: every intended project should exist on disk ---------------
    for name in INTENDED:
        if name not in actual:
            warnings.append(f"  intended project not found on disk: {PREFIX}{name}")

    # --- Edge comparison + downward-only check -------------------------------
    for name, refs in sorted(actual.items()):
        intended = INTENDED.get(name)
        if intended is None:
            warnings.append(
                f"  unknown core project '{name}' (not in intended graph); "
                f"references: {sorted(refs)}"
            )
            continue

        missing = intended - refs
        extra = refs - intended
        for m in sorted(missing):
            errors.append(f"  MISSING edge: {name} -> {m}")
        for e in sorted(extra):
            # Classify the unexpected edge for a clearer message.
            if e in LAYER and name in LAYER:
                src_l, dst_l = LAYER[name], LAYER[e]
                if dst_l > src_l:
                    errors.append(
                        f"  UPWARD edge (layer {src_l} -> {dst_l}): {name} -> {e}"
                    )
                elif dst_l == src_l and SUBORDER.get(e, 0) >= SUBORDER.get(name, 0):
                    errors.append(
                        f"  SIDEWAYS/UPWARD intra-layer edge: {name} -> {e}"
                    )
                else:
                    errors.append(
                        f"  UNEXPECTED edge (downward but not in DAG): {name} -> {e}"
                    )
            else:
                errors.append(f"  UNEXPECTED edge: {name} -> {e}")

    # --- Acyclicity ----------------------------------------------------------
    cycle = has_cycle(actual)
    if cycle:
        errors.append("  CYCLE detected: " + " -> ".join(cycle))

    # --- Verdict -------------------------------------------------------------
    if warnings:
        print("\nWarnings:")
        for w in warnings:
            print(w)

    if errors:
        print("\nFAIL: dependency graph drift\n")
        for e in errors:
            print(e)
        print(f"\n{len(errors)} problem(s). Fix the csproj references and re-run.")
        return 1

    print(
        f"\nOK: {len(actual)} core projects, graph matches the intended DAG, "
        "acyclic, downward-only, no '.Pipe' naming."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
