#!/usr/bin/env python3
"""check_dag.py -- validate the MartialHeroes ProjectReference graph.

Stdlib-only. Walks the repo, reads every MartialHeroes.*.csproj, extracts its
<ProjectReference Include="..."/> edges, and asserts that the resulting graph:

  1. references only KNOWN projects (every node is in the layer map),
  2. is strictly downward -- a lower-numbered layer never references a higher
     one, and intra-layer references respect the established sub-order
     (a project may only reference a peer with a STRICTLY lower sub-order),
  3. is acyclic,
  4. never names the transport project ".Pipe" -- the real project is
     "Network.Transport.Pipelines".

Usage:
    python check_dag.py [REPO_ROOT]

REPO_ROOT defaults to the current working directory. Exit code 0 on success;
non-zero on any drift, with a human-readable diff printed to stdout.

Scope: governs the ProjectReference edges between the core class libraries
(layers 01-04). Client.Godot (layer 05) is owned by the godot-csproj-bootstrap
skill and is not *scanned* -- but it remains a valid *target* (layer 5) so that
any core->Godot edge is caught as upward. Package references are out of scope.
The 00.SourcesGenerators Roslyn analyzers sit at layer 0 (referenced only as
analyzers; every analyzer edge into them is downward). The Tools/ CLI utilities
are leaf CONSUMERS at a band above the core (layer 6): they reference down into
the libraries and nothing in the core references them.

Model: CAMPAIGN "STRICT 1:1 RECONSTRUCTION" re-architected the 12-project graph
into the maximal 34/35-project graph (Docs/ARCHITECTURE_TARGET.md). Rather than
hand-maintain an exact intended edge-set per project across a multi-wave reorg,
this checker enforces the load-bearing INVARIANTS (downward-only by layer +
sub-order, acyclic, known-projects-only, no ".Pipe") which are stable regardless
of which precise downward edges a wave wires. Unused/redundant downward edges are
pruned in the Phase-4 quality pass (build-level unused-reference detection), not
here.
"""

from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# --- The target graph: layer + intra-layer sub-order per project ------------
# Keys are short project names (the "MartialHeroes." prefix is implied).
# An edge src -> dst is legal iff LAYER[dst] < LAYER[src], OR
# (LAYER[dst] == LAYER[src] AND SUBORDER[dst] < SUBORDER[src]).
# Anything referenced but absent from this map is an "unknown project" error.

LAYER: dict[str, int] = {
    # 00.SourcesGenerators (Roslyn analyzers; layer 0 -- referenced only as analyzers, downward by all)
    "Network.Protocol.Generators": 0,        # PacketRouter source-gen
    "Shared.Kernel.Generators": 0,           # [StronglyTypedId] source-gen
    # 01.Infrastructure.Shared
    "Shared.Kernel": 1,
    "Shared.Diagnostics": 1,
    # 02.Network.Layer
    "Network.Abstractions": 2,
    "Network.Crypto": 2,
    "Network.Protocol.Core": 2,              # frame header, opcode attr, router base, enums
    "Network.Protocol.Packets.Login": 2,     # major 0/1/3 structs
    "Network.Protocol.Packets.World": 2,     # major 4/5/6 structs
    "Network.Protocol.Packets.Social": 2,    # major 2 + social/trade/guild structs
    "Network.Transport.Pipelines": 2,
    "Network.Protocol.Routing": 2,           # hosts the generated router (sees all packets); optional per source-gen resolution
    # 03.Storage.Assets
    "Assets.Vfs": 3,
    "Assets.Parsers.Core": 3,                # LenStrReader, Vec2/Vec3/Quat primitives
    "Assets.Parsers.Mesh": 3,
    "Assets.Parsers.Terrain": 3,
    "Assets.Parsers.Character": 3,
    "Assets.Parsers.DataTables": 3,
    "Assets.Parsers.Effects": 3,
    "Assets.Parsers.World": 3,
    "Assets.Parsers.Audio": 3,
    "Assets.Parsers.Texture": 3,
    "Assets.Mapping": 3,
    # 04.Client.Core
    "Client.Domain.Stats": 4,
    "Client.Domain.Simulation": 4,
    "Client.Domain.Progression": 4,
    "Client.Domain.Quests": 4,
    "Client.Domain.Social": 4,
    "Client.Domain.Skills": 4,
    "Client.Domain.Inventory": 4,
    "Client.Domain.Actors": 4,
    "Client.Application.Contracts": 4,
    "Client.Application": 4,
    "Client.Presentation": 4,                # engine-free presentation lib (NO using Godot;)
    "Client.Infrastructure": 4,
    # 05.Presentation (not scanned -- see IGNORED_PROJECTS -- but a valid target)
    "Client.Godot": 5,
    # Tools/ (CLI utilities; leaf CONSUMERS above the core -- reference down, referenced by none)
    "Tools.AssetChainTrace": 6,
    "Tools.AssetProbe": 6,
    "Tools.PacketInspect": 6,
    "Tools.VfsExplorer": 6,
    # Server/ (replica server; leaf CONSUMERS above the core -- reference down, referenced by none)
    "Server.Core": 7,
    "Server.Console": 7,
    # tests/ (xUnit suites; leaf CONSUMERS above the core -- reference down to their SUT, referenced by none)
    "Client.Domain.Stats.Tests": 6,
    "Client.Domain.Simulation.Tests": 6,
    "Client.Domain.Inventory.Tests": 6,
    "Network.Protocol.Packets.World.Tests": 6,
    # Explorer/ (asset explorer + 3D viewer; leaf CONSUMERS above the core)
    "Explorer.Files": 6,
    "Explorer.Viewer": 6,
}

# Intra-layer sub-order: a project may only reference a peer with a STRICTLY
# lower sub-order value (so Parsers.Mesh->Parsers.Core is fine; the reverse is
# upward). Equal sub-order means "no reference allowed between them".
SUBORDER: dict[str, int] = {
    # 00
    "Network.Protocol.Generators": 0,
    "Shared.Kernel.Generators": 0,
    # 01
    "Shared.Kernel": 0,
    "Shared.Diagnostics": 1,
    # 02
    "Network.Abstractions": 1,
    "Network.Crypto": 1,
    "Network.Protocol.Core": 1,
    "Network.Protocol.Packets.Login": 2,
    "Network.Protocol.Packets.World": 2,
    "Network.Protocol.Packets.Social": 2,
    "Network.Transport.Pipelines": 2,
    "Network.Protocol.Routing": 3,           # above the packet families it composes
    # 03
    "Assets.Vfs": 0,
    "Assets.Parsers.Core": 1,
    "Assets.Parsers.Mesh": 2,
    "Assets.Parsers.Terrain": 2,
    "Assets.Parsers.Character": 2,
    "Assets.Parsers.DataTables": 2,
    "Assets.Parsers.Effects": 2,
    "Assets.Parsers.World": 2,
    "Assets.Parsers.Audio": 2,
    "Assets.Parsers.Texture": 2,
    "Assets.Mapping": 3,                      # above the parser families it converts
    # 04
    "Client.Domain.Stats": 0,
    "Client.Domain.Simulation": 0,
    "Client.Domain.Progression": 0,
    "Client.Domain.Quests": 0,
    "Client.Domain.Social": 0,
    "Client.Domain.Skills": 1,               # -> Stats
    "Client.Domain.Inventory": 1,            # -> Stats
    "Client.Domain.Actors": 1,               # -> Stats, Simulation
    "Client.Application.Contracts": 2,       # -> selected Domain aggregates
    "Client.Application": 3,                  # -> Contracts, Domain, (down to layer 02 network)
    "Client.Presentation": 4,                # -> Contracts, (down to layer 03 Mapping/Parsers read-models)
    "Client.Infrastructure": 5,              # -> Application, (down to layer 03 Parsers/Vfs)
    # 05
    "Client.Godot": 0,
    # Tools
    "Tools.AssetChainTrace": 0,
    "Tools.AssetProbe": 0,
    "Tools.PacketInspect": 0,
    "Tools.VfsExplorer": 0,
    # Server/
    "Server.Core": 0,
    "Server.Console": 1,
    # tests/
    "Client.Domain.Stats.Tests": 0,
    "Client.Domain.Simulation.Tests": 0,
    "Client.Domain.Inventory.Tests": 0,
    "Network.Protocol.Packets.World.Tests": 0,
    # Explorer/
    "Explorer.Files": 0,
    "Explorer.Viewer": 0,
}

# Layer 05 (Godot) is not scanned as a source (its csproj is owned elsewhere and
# legitimately holds the documented 05->02 transport + 05->04.x presentation
# edges, which this checker does not police). It stays a valid reference TARGET.
IGNORED_PROJECTS = {"Client.Godot"}

PREFIX = "MartialHeroes."


def short_name(csproj_path: Path) -> str:
    """'.../MartialHeroes.Network.Crypto.csproj' -> 'Network.Crypto'."""
    stem = csproj_path.stem
    if stem.startswith(PREFIX):
        return stem[len(PREFIX):]
    return stem


def find_csprojs(root: Path) -> list[Path]:
    found: list[Path] = []
    for dirpath, dirnames, filenames in os.walk(root):
        # Skip build artefacts, editor caches, and the tooling/quarantine trees
        # (the .claude/.agents skill harnesses are not "MartialHeroes.*" anyway).
        dirnames[:] = [
            d for d in dirnames
            if d not in {"bin", "obj", ".git", ".godot", "_dirty", ".claude", ".agents"}
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
    for el in root.iter():
        tag = el.tag.split("}")[-1]  # strip any namespace
        if tag != "ProjectReference":
            continue
        include = el.get("Include")
        if not include:
            continue
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
                continue  # edge to something outside the scanned graph
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

    # --- Presence: every mapped (non-ignored) project should exist on disk ----
    for name in LAYER:
        if name in IGNORED_PROJECTS:
            continue
        if name not in actual:
            warnings.append(f"  mapped project not found on disk: {PREFIX}{name}")

    # --- Unknown scanned projects --------------------------------------------
    for name in sorted(actual):
        if name not in LAYER:
            errors.append(
                f"  UNKNOWN core project '{name}' (not in the layer map); "
                f"add it to LAYER/SUBORDER or fix the name. refs: {sorted(actual[name])}"
            )

    # --- Edge legality: downward-only by layer + sub-order --------------------
    for name, refs in sorted(actual.items()):
        if name not in LAYER:
            continue  # already reported as unknown
        src_l, src_s = LAYER[name], SUBORDER.get(name, 0)
        for dst in sorted(refs):
            if dst not in LAYER:
                errors.append(f"  edge to UNKNOWN project: {name} -> {dst}")
                continue
            dst_l, dst_s = LAYER[dst], SUBORDER.get(dst, 0)
            if dst_l > src_l:
                errors.append(f"  UPWARD edge (layer {src_l} -> {dst_l}): {name} -> {dst}")
            elif dst_l == src_l and dst_s >= src_s:
                errors.append(
                    f"  SIDEWAYS/UPWARD intra-layer edge "
                    f"(sub-order {src_s} -> {dst_s}): {name} -> {dst}"
                )
            # else: strictly downward -> legal

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
        f"\nOK: {len(actual)} core projects, graph downward-only "
        "(by layer + sub-order), acyclic, no '.Pipe' naming."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
