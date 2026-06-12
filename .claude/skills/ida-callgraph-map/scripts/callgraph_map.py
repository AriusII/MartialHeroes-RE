# callgraph_map.py — bounded call graph around a target function in the legacy Martial Heroes
# client (Main.exe): callers (up), callees (down), or both, out to a depth/node cap.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool. Fallback for when
# the typed mcp__ida__callgraph / callees tools are unavailable.
#
# DIRTY: addresses derived from the copyrighted binary; output belongs ONLY under
# Docs/RE/_dirty/static/. Never commit; never copy into clean specs or C#. A call graph is
# structure (edges + names), NOT function bodies — this script never emits disassembly/pseudo-C.

# === CONFIG ===
# TARGET: a function name (e.g. "RecvPacketDispatch") OR a hex address (0x004A1230).
TARGET = "RecvPacketDispatch"
# DIRECTION: "both" | "callers" | "callees"
DIRECTION = "both"
MAX_DEPTH = 2          # how many hops out from TARGET
MAX_NODES = 200        # hard cap so hub functions don't explode the graph
OUT_DIR = r"Docs\RE\_dirty\static"
# ==============

import datetime
from collections import deque

try:
    import idaapi
    import idautils
    import ida_funcs
    import ida_name
    import ida_xref
except ImportError as exc:
    raise SystemExit("callgraph_map.py must run inside IDA Pro (IDAPython): %s" % exc)


def resolve(target):
    if isinstance(target, int):
        f = ida_funcs.get_func(target)
        return f.start_ea if f else idaapi.BADADDR
    ea = ida_name.get_name_ea(idaapi.BADADDR, target)
    if ea != idaapi.BADADDR:
        f = ida_funcs.get_func(ea)
        return f.start_ea if f else ea
    matches = [(e, n) for e, n in idautils.Names() if target.lower() in n.lower()]
    if matches:
        if len(matches) > 1:
            print("[callgraph_map] ambiguous TARGET %r matched %d names; using first." % (target, len(matches)))
        f = ida_funcs.get_func(matches[0][0])
        return f.start_ea if f else matches[0][0]
    return idaapi.BADADDR


def fname(ea):
    return ida_name.get_name(ea) or ("sub_%X" % ea)


def is_lib(ea):
    f = ida_funcs.get_func(ea)
    return bool(f and (f.flags & ida_funcs.FUNC_LIB))


def callees_of(ea):
    """Functions called from within the function starting at ea."""
    f = ida_funcs.get_func(ea)
    if not f:
        return []
    out = set()
    for head in idautils.Heads(f.start_ea, f.end_ea):
        for xref in idautils.XrefsFrom(head, 0):
            if xref.type in (ida_xref.fl_CN, ida_xref.fl_CF):
                callee = ida_funcs.get_func(xref.to)
                if callee:
                    out.add(callee.start_ea)
    return sorted(out)


def callers_of(ea):
    """Functions that call into ea."""
    out = set()
    for xref in idautils.XrefsTo(ea, 0):
        if xref.type in (ida_xref.fl_CN, ida_xref.fl_CF):
            caller = ida_funcs.get_func(xref.frm)
            if caller:
                out.add(caller.start_ea)
    return sorted(out)


def bfs(root, direction):
    edges = set()
    nodes = {root}
    q = deque([(root, 0)])
    while q and len(nodes) < MAX_NODES:
        ea, depth = q.popleft()
        if depth >= MAX_DEPTH:
            continue
        neighbors = []
        if direction in ("both", "callees"):
            for c in callees_of(ea):
                edges.add((ea, c))
                neighbors.append(c)
        if direction in ("both", "callers"):
            for c in callers_of(ea):
                edges.add((c, ea))
                neighbors.append(c)
        for n in neighbors:
            if n not in nodes and len(nodes) < MAX_NODES:
                nodes.add(n)
                q.append((n, depth + 1))
    return nodes, edges


def main():
    root = resolve(TARGET)
    if root == idaapi.BADADDR:
        print("[callgraph_map] could not resolve TARGET=%r to a function." % (TARGET,))
        return

    nodes, edges = bfs(root, DIRECTION)
    capped = len(nodes) >= MAX_NODES

    indeg = {n: 0 for n in nodes}
    outdeg = {n: 0 for n in nodes}
    for a, b in edges:
        if a in outdeg:
            outdeg[a] += 1
        if b in indeg:
            indeg[b] += 1

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# callgraph_map: %s" % fname(root),
        "",
        "- root: `%s`  (ea `0x%X`)" % (fname(root), root),
        "- direction: %s   depth<=%d   node_cap=%d" % (DIRECTION, MAX_DEPTH, MAX_NODES),
        "- generated: %s" % stamp,
        "- nodes: %d   edges: %d%s" % (len(nodes), len(edges), "  (NODE CAP HIT — narrow scope)" if capped else ""),
        "",
        "## Nodes",
        "",
        "| Func EA | Name | In | Out | Kind |",
        "|---|---|---|---|---|",
    ]
    for n in sorted(nodes):
        kind = "lib" if is_lib(n) else ("root" if n == root else "user")
        lines.append("| 0x%X | %s | %d | %d | %s |" % (n, fname(n), indeg.get(n, 0), outdeg.get(n, 0), kind))

    lines += ["", "## Edges (caller -> callee)", "", "| From | To |", "|---|---|"]
    for a, b in sorted(edges):
        lines.append("| %s | %s |" % (fname(a), fname(b)))

    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", fname(root))[:60]
        path = os.path.join(OUT_DIR, "callgraph.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[callgraph_map] wrote %s" % path)
    except Exception as exc:
        print("\n[callgraph_map] could not write file (%s); save the Markdown above via Write." % exc)


main()
