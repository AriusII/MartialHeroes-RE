#!/usr/bin/env python
"""gen_packet.py -- generate a C# packet struct skeleton from a Docs/RE/packets/*.yaml spec.

Emits a zero-allocation, blittable wire struct into MartialHeroes.Network.Protocol:
  - [StructLayout(LayoutKind.Sequential, Pack = 1)] on the packet struct
  - one field per spec entry, in wire order (offsets implied by Pack=1)
  - a `public const byte Opcode` mirroring the spec opcode
  - a generated [InlineArray(N)] buffer struct for each bytes[N] field (no managed strings)
  - `enum:<Name>` fields typed against MartialHeroes.Shared.Kernel
  - a header comment citing the source spec path (every magic constant is traceable)

CLEAN ROOM: this script REFUSES to read anything under a `_dirty/` path and never calls IDA.
The packets/*.yaml spec is the only input. Stdlib only (argparse/os/re/sys); no pip, no PyYAML
-- the spec is parsed by a small purpose-built reader.

Usage:
  python gen_packet.py --spec Docs/RE/packets/move.yaml \
      --out 02.Network.Layer/MartialHeroes.Network.Protocol/Packets
"""
from __future__ import annotations

import argparse
import os
import re
import sys

NAMESPACE = "MartialHeroes.Network.Protocol.Packets"
KERNEL_NS = "MartialHeroes.Shared.Kernel"

# spec scalar type -> (C# type, byte width). Width None => variable / not size-checkable here.
SCALARS: dict[str, tuple[str, int]] = {
    "u8": ("byte", 1), "i8": ("sbyte", 1),
    "u16": ("ushort", 2), "i16": ("short", 2),
    "u32": ("uint", 4), "i32": ("int", 4),
    "u64": ("ulong", 8), "i64": ("long", 8),
    "f32": ("float", 4), "f64": ("double", 8),
}
ENUM_RE = re.compile(r"^enum:([A-Za-z_][A-Za-z0-9_]*)$")
BYTES_RE = re.compile(r"^bytes\[(\d+)\]$")
FIELD_RE = re.compile(r"^\s*-\s*\{\s*name:\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*type:\s*([^},]+?)\s*\}")


def die(msg: str) -> None:
    sys.exit(f"error: {msg}")


def guard_clean_room(path: str) -> None:
    """Hard refuse to read decompiler-tainted material."""
    norm = path.replace("\\", "/").lower()
    if "_dirty/" in norm or norm.endswith("/_dirty") or "/_dirty/" in norm:
        die("REFUSED: '_dirty/' is decompiler-tainted and off-limits to clean-room codegen. "
            "Promote the layout into a Docs/RE/packets/*.yaml spec first.")


def parse_spec(path: str) -> dict:
    """Minimal line-oriented reader for the packet YAML-ish spec (stdlib only)."""
    guard_clean_room(path)
    if not os.path.isfile(path):
        die(f"spec not found: {path}")

    spec: dict = {"fields": []}
    in_fields = False
    in_block = None  # name of a `key: |` block being consumed (notes/summary)
    block_lines: list[str] = []

    with open(path, encoding="utf-8") as fh:
        raw_lines = fh.readlines()

    def end_block():
        nonlocal in_block, block_lines
        if in_block is not None:
            spec[in_block] = "".join(block_lines).rstrip("\n")
            in_block = None
            block_lines = []

    for line in raw_lines:
        stripped = line.rstrip("\n")

        # Inside a block scalar (key: |), consume indented lines.
        if in_block is not None:
            if stripped.strip() == "" or stripped.startswith((" ", "\t")):
                block_lines.append(stripped.strip() + "\n")
                continue
            end_block()

        # Strip whole-line comments and blank lines (outside blocks).
        if not stripped.strip() or stripped.lstrip().startswith("#"):
            continue

        if in_fields:
            m = FIELD_RE.match(stripped)
            if m:
                spec["fields"].append({"name": m.group(1), "type": m.group(2).strip()})
                continue
            # A non-list, non-indented line ends the fields section.
            if not stripped.startswith((" ", "\t", "-")):
                in_fields = False
            else:
                continue

        if stripped.startswith(("fields:",)):
            in_fields = True
            continue

        if ":" in stripped:
            key, _, val = stripped.partition(":")
            key = key.strip()
            val = val.split("#", 1)[0].strip()  # drop trailing comment
            if val == "|":
                in_block = key
                block_lines = []
            else:
                spec[key] = val.strip().strip('"').strip("'")

    end_block()
    return spec


def csharp_type(field_type: str) -> tuple[str, bool, int | None]:
    """Map a spec type to (C# type, is_inline_buffer, byte_width)."""
    if field_type in SCALARS:
        cs, width = SCALARS[field_type]
        return cs, False, width
    m = ENUM_RE.match(field_type)
    if m:
        # Enum width unknown here (depends on its backing type); not size-checked.
        return m.group(1), False, None
    m = BYTES_RE.match(field_type)
    if m:
        return f"{{buffer}}", True, int(m.group(1))
    die(f"unsupported field type: {field_type!r} "
        "(allowed: u8/i8/u16/i16/u32/i32/u64/i64/f32/f64, enum:<Name>, bytes[N])")
    return "", False, None  # unreachable


def pascal(name: str) -> str:
    return name[:1].upper() + name[1:] if name else name


def gen(spec: dict, spec_path: str) -> str:
    name = spec.get("name") or die("spec missing required key: name")
    name = pascal(name)
    opcode_raw = spec.get("opcode")
    direction = spec.get("direction", "?")
    size = spec.get("size", "var")
    cite = spec.get("spec", spec_path).replace("\\", "/")
    summary = spec.get("summary", "")
    fields = spec["fields"]
    if not fields:
        die("spec has no fields")

    opcode_int = None
    if opcode_raw is not None:
        try:
            opcode_int = int(str(opcode_raw), 0)
        except ValueError:
            die(f"opcode {opcode_raw!r} is not a valid hex/int literal")

    # Resolve each field, collect inline-array buffers and total fixed width.
    members: list[tuple[str, str]] = []   # (C# type, field name)
    buffers: list[tuple[str, int]] = []   # (buffer struct name, N)
    needs_kernel = False
    total_width = 0
    width_known = True

    for f in fields:
        fname = pascal(f["name"])
        ftype = f["type"]
        cs, is_buf, width = csharp_type(ftype)
        if ENUM_RE.match(ftype):
            needs_kernel = True
        if is_buf:
            buf_name = f"{fname}Buffer"
            cs = buf_name
            buffers.append((buf_name, width))
            total_width += width
        elif width is not None:
            total_width += width
        else:
            width_known = False  # enum of unknown backing width
        members.append((cs, fname))

    # Build the file.
    L: list[str] = []
    L.append("// <auto-generated>")
    L.append(f"//   Generated by the packet-codegen skill from {cite}")
    L.append(f"//   spec: {cite}")
    if opcode_int is not None:
        L.append(f"//   opcode: 0x{opcode_int:02x}   direction: {direction}   size: {size}")
    L.append("//   CLEAN ROOM: re-implemented from a neutral spec; no decompiler output. Do not edit by hand.")
    L.append("// </auto-generated>")
    L.append("using System.Runtime.CompilerServices;")
    L.append("using System.Runtime.InteropServices;")
    if needs_kernel:
        L.append(f"using {KERNEL_NS};")
    L.append("")
    L.append(f"namespace {NAMESPACE};")
    L.append("")
    if summary:
        L.append("/// <summary>")
        L.append(f"/// {summary}")
        L.append(f"/// Wire layout per <c>{cite}</c>.")
        L.append("/// </summary>")
    else:
        L.append("/// <summary>")
        L.append(f"/// Wire layout per <c>{cite}</c>.")
        L.append("/// </summary>")
    L.append("[StructLayout(LayoutKind.Sequential, Pack = 1)]")
    L.append(f"public struct {name}")
    L.append("{")
    if opcode_int is not None:
        # Const is named OpcodeId so it never collides with a wire field named `Opcode`.
        L.append(f"    /// <summary>The opcode value for this message. spec: {cite}</summary>")
        L.append(f"    public const byte OpcodeId = 0x{opcode_int:02x};")
        L.append("")
    if size != "var" and width_known and str(size).isdigit():
        if int(size) != total_width:
            L.append(f"    // WARNING: spec size={size} but summed field widths={total_width}. "
                     "Reconcile the spec.")
        L.append(f"    /// <summary>Declared wire size in bytes (spec: {cite}). Verify with "
                 f"<c>Unsafe.SizeOf&lt;{name}&gt;() == {total_width}</c> in a test.</summary>")
        L.append(f"    public const int Size = {total_width};")
        L.append("")
    for cs, fname in members:
        L.append(f"    public {cs} {fname};")
    L.append("}")
    L.append("")

    # Inline-array buffer structs.
    for buf_name, n in buffers:
        L.append(f"/// <summary>Fixed {n}-byte inline buffer (no managed string on the wire). "
                 f"spec: {cite}</summary>")
        L.append(f"[InlineArray({n})]")
        L.append(f"public struct {buf_name}")
        L.append("{")
        L.append("    private byte _element0;")
        L.append("}")
        L.append("")

    return "\n".join(L).rstrip("\n") + "\n"


def main() -> None:
    ap = argparse.ArgumentParser(description="Generate a C# packet struct from a packets/*.yaml spec.")
    ap.add_argument("--spec", required=True, help="path to Docs/RE/packets/<name>.yaml")
    ap.add_argument(
        "--out",
        default="02.Network.Layer/MartialHeroes.Network.Protocol/Packets",
        help="output dir (default: the Network.Protocol Packets folder)",
    )
    ap.add_argument("--print", action="store_true", help="print to stdout instead of writing a file")
    args = ap.parse_args()

    guard_clean_room(args.spec)
    guard_clean_room(args.out)

    spec = parse_spec(args.spec)
    code = gen(spec, args.spec)
    struct_name = pascal(spec["name"])

    if args.print:
        sys.stdout.write(code)
        return

    os.makedirs(args.out, exist_ok=True)
    out_path = os.path.join(args.out, f"{struct_name}.g.cs")
    with open(out_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(code)
    print(f"wrote {out_path}")
    print("note: this is a layout-only skeleton. Serialization + opcode->handler routing are "
          "the network-protocol-engineer's job.")


if __name__ == "__main__":
    main()
