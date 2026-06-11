#!/usr/bin/env python
"""PreToolUse(Write|Edit|MultiEdit) hook — warn on architecture-layer violations.

Advisory only. Catches: a .csproj adding an UPWARD ProjectReference (lower layer referencing
a higher one), a rendering-engine PackageReference inside a core (layer 1-4) project, and
`using Godot;` inside any non-presentation project. Dependencies must flow strictly downward.
"""
import re
import _hooklib as h

_ENGINE_PKGS = re.compile(
    r"\b(GodotSharp|Godot\.NET\.Sdk|Silk\.NET\w*|Veldrid\w*|OpenTK\w*|SkiaSharp\w*|MonoGame\w*)\b",
    re.I,
)
_PROJ_REF = re.compile(r'<ProjectReference\s+Include\s*=\s*"([^"]+)"', re.I)
_PKG_REF = re.compile(r'<PackageReference\s+Include\s*=\s*"([^"]+)"', re.I)
_USING_GODOT = re.compile(r"(?:^|\n)\s*(?:global\s+)?using\s+Godot\b", re.M)


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    low = path.lower()
    cur_layer, cur_proj = h.layer_of(path)
    if cur_layer is None:
        h.ok()
        return

    added = h.added_text(ev)
    warnings = []

    if low.endswith(".csproj"):
        for inc in _PROJ_REF.findall(added):
            tgt = h.layer_of_reference(inc)
            if tgt is not None and tgt > cur_layer:
                warnings.append(
                    "upward ProjectReference: layer {} project references layer {} ({})".format(
                        cur_layer, tgt, inc.split('/')[-1].split('\\')[-1]
                    )
                )
        if cur_layer <= 4:
            for pkg in _PKG_REF.findall(added):
                if _ENGINE_PKGS.search(pkg):
                    warnings.append("rendering/engine package '{}' in engine-free core layer {}".format(pkg, cur_layer))
    else:
        if cur_layer < 5 and _USING_GODOT.search(h.strip_comments_strings(added)):
            warnings.append("`using Godot;` in layer {} ({}) — core must stay engine-free".format(cur_layer, cur_proj))

    if warnings:
        h.system_message(
            "⚠ architecture: {}. Dependencies flow downward only; the core below layer 05 is "
            "engine-free so it can back a headless server. Heads up only — nothing was blocked.".format(
                "; ".join(warnings)
            )
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
