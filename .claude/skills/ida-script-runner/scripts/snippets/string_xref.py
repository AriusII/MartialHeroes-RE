# string_xref.py — find strings and their references in the legacy Martial Heroes client.
#
# Runs INSIDE IDA Pro 9.3 (IDAPython), via the IDA MCP script-exec tool.
# DIRTY-ROOM output: addresses are allowed only under Docs/RE/_dirty/.
#
# Useful for anchoring functions: error/log strings ("recv failed", "decrypt", "invalid
# packet", "%s.pak") usually sit in the very function you want to name.

# === CONFIG ===
# NEEDLE: case-insensitive substring to match within string literals.
NEEDLE = "packet"
MAX_STRINGS = 200       # cap how many matching strings to detail
OUT_DIR = r"Docs\RE\_dirty\queries"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import ida_funcs
    import ida_name
    import ida_bytes
except ImportError as exc:
    raise SystemExit("string_xref.py must run inside IDA Pro (IDAPython): %s" % exc)


def func_name(ea):
    f = ida_funcs.get_func(ea)
    if not f:
        return "(not in a function)", idaapi.BADADDR
    nm = ida_name.get_name(f.start_ea)
    return (nm if nm else "sub_%X" % f.start_ea), f.start_ea


def main():
    needle = NEEDLE.lower()
    matched = []
    for s in idautils.Strings():
        try:
            text = str(s)
        except Exception:
            continue
        if needle in text.lower():
            matched.append((s.ea, text))
        if len(matched) >= MAX_STRINGS:
            break

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# string_xref: NEEDLE=%r" % NEEDLE,
        "",
        "- generated: %s" % stamp,
        "- matching_strings: %d" % len(matched),
        "",
    ]
    for sea, text in matched:
        safe = text.replace("|", "\\|").replace("\n", "\\n")
        if len(safe) > 80:
            safe = safe[:77] + "..."
        lines.append("## `%s`  (str @ 0x%X)" % (safe, sea))
        refs = []
        for xref in idautils.XrefsTo(sea, 0):
            nm, fs = func_name(xref.frm)
            refs.append((xref.frm, fs, nm))
        if not refs:
            lines.append("- (no code references)")
        else:
            lines.append("")
            lines.append("| Ref EA | Func EA | Func Name |")
            lines.append("|---|---|---|")
            for site, fs, nm in sorted(refs, key=lambda r: r[0]):
                fsstr = "0x%X" % fs if fs != idaapi.BADADDR else "-"
                lines.append("| 0x%X | %s | %s |" % (site, fsstr, nm))
        lines.append("")
    report = "\n".join(lines)
    print(report)

    try:
        import os, re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", NEEDLE)[:50] or "match"
        path = os.path.join(OUT_DIR, "string_xref.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("\n[string_xref] wrote %s" % path)
    except Exception as exc:
        print("\n[string_xref] could not write file (%s); save the Markdown above via Write." % exc)


main()
