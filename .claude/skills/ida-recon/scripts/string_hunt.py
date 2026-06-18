# string_hunt.py — string-table census + subsystem tagging for the legacy Martial Heroes
# client (Main.exe). Buckets strings/imports by keyword and records the referencing function.
#
# RUN THIS INSIDE IDA PRO 9.3 (IDAPython), via the IDA MCP script-exec tool.
#
# DIRTY: string CONTENTS and addresses are derived from the copyrighted binary; output belongs
# ONLY under Docs/RE/_dirty/static/. Never commit; never copy into clean specs or C#.
# Game text is CP949 (Korean) — treat it as opaque data; the snippet flags non-ASCII so you do
# not misread mojibake. Record short markers only; never transcribe long runs of CP949 text.

# === CONFIG ===
LABEL = "census"        # output filename: strings.<label>.md
# Subsystem buckets: name -> list of lowercase substrings to match against strings & imports.
BUCKETS = {
    "network":   ["recv", "send", "socket", "connect", "wsastartup", "winsock", "packet", "tcp", "ip", "port"],
    "asset_io":  [".pak", ".vfs", ".skn", ".bud", ".ted", ".map", ".mot", ".dds", "createfile", "readfile", "data/"],
    "crypto":    ["crypt", "encrypt", "decrypt", "key", "cipher", "advapi32", "hash", "rsa", "aes", "sbox"],
    "ui":        ["button", "window", "dialog", "font", "render", "d3d", "directx", "widget", "cursor", "ime"],
    "scripting": [".lua", "lua_", "tinker", "script", "cpp_load"],
    "config":    [".scr", ".txt", ".csv", ".cfg", ".ini", "config", "exp", "userlevel", "items", "skills", "mobs"],
    "debug":     ["error", "fail", "assert", "warning", "log", "debug", "%s", "%d"],
}
MIN_LEN = 3
MAX_PER_BUCKET = 80     # cap rows per bucket so the report stays readable
OUT_DIR = r"Docs\RE\_dirty\static"
# ==============

import datetime

try:
    import idaapi
    import idautils
    import idc
    import ida_funcs
    import ida_name
    import ida_nalt
except ImportError as exc:
    raise SystemExit("string_hunt.py must run inside IDA Pro (IDAPython): %s" % exc)


def ref_func(ea):
    """Name of a function that references the data at ea (first code xref), or '-'."""
    for x in idautils.XrefsTo(ea, 0):
        f = ida_funcs.get_func(x.frm)
        if f:
            return ida_name.get_name(f.start_ea) or ("sub_%X" % f.start_ea)
    return "-"


def is_ascii(text):
    try:
        text.encode("ascii")
        return True
    except Exception:
        return False


def classify(text):
    low = text.lower()
    hits = []
    for bucket, kws in BUCKETS.items():
        if any(kw in low for kw in kws):
            hits.append(bucket)
    return hits


def gather_strings():
    out = []
    try:
        sc = idautils.Strings()
        sc.setup(strtypes=[ida_nalt.STRTYPE_C, ida_nalt.STRTYPE_C_16])
    except TypeError:
        sc = idautils.Strings()
    for s in sc:
        try:
            text = str(s)
        except Exception:
            continue
        if len(text) < MIN_LEN:
            continue
        out.append((s.ea, text))
    return out


def gather_imports():
    """Imports as pseudo-strings so API names land in their buckets too."""
    out = []
    for i in range(idaapi.get_import_module_qty()):
        mod = idaapi.get_import_module_name(i) or ""

        def _cb(ea, name, ordinal, _mod=mod):
            if name:
                out.append((ea, "%s!%s" % (_mod, name)))
            return True

        idaapi.enum_import_names(i, _cb)
    return out


def main():
    strings = gather_strings()
    imports = gather_imports()

    buckets = {b: [] for b in BUCKETS}
    for ea, text in strings + imports:
        for b in classify(text):
            if len(buckets[b]) < MAX_PER_BUCKET:
                disp = text if is_ascii(text) else ("<non-ascii/CP949 %dB>" % len(text))
                buckets[b].append((ea, disp, ref_func(ea)))

    stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    lines = [
        "# string_hunt: %s" % LABEL,
        "",
        "- generated: %s" % stamp,
        "- strings_scanned: %d   imports_scanned: %d" % (len(strings), len(imports)),
        "- buckets (matches): " + ", ".join("%s=%d" % (b, len(v)) for b, v in buckets.items()),
        "",
        "> CP949 game text shown as `<non-ascii/CP949 NB>` — treat as opaque data.",
        "",
    ]
    for b, rows in buckets.items():
        lines.append("## %s (%d)" % (b, len(rows)))
        lines.append("")
        if not rows:
            lines.append("_(no matches)_\n")
            continue
        lines.append("| Data EA | String / Import | Ref Func |")
        lines.append("|---|---|---|")
        for ea, disp, fn in rows:
            safe = disp.replace("|", r"\|")
            if len(safe) > 80:
                safe = safe[:80] + "…"
            lines.append("| 0x%X | `%s` | %s |" % (ea, safe, fn))
        lines.append("")

    report = "\n".join(lines)
    print(report)

    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", LABEL)[:60] or "census"
        path = os.path.join(OUT_DIR, "strings.%s.md" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(report + "\n")
        print("[string_hunt] wrote %s" % path)
    except Exception as exc:
        print("[string_hunt] could not write file (%s); save the Markdown above via Write." % exc)


main()
