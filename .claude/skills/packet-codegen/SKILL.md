---
name: packet-codegen
description: Use to generate a C# packet struct skeleton from a Docs/RE/packets/*.yaml field spec. Emits a [StructLayout(LayoutKind.Sequential, Pack=1)] struct into 02.Network.Layer/MartialHeroes.Network.Protocol/, fixed byte buffers as C# [InlineArray] types, enum fields resolved to Shared.Kernel enums, and a header comment citing the source spec path. REFUSES to read anything under _dirty/ — clean-room only, never calls IDA.
allowed-tools: Read Write Bash(dotnet build *)
model: sonnet
effort: high
---

# packet-codegen

Generate a zero-allocation C# packet struct from a committed `Docs/RE/packets/*.yaml` spec.
This is the clean-room bridge from the documented wire layout to `Network.Protocol`: the spec
(neutral prose/table promoted across the firewall) is the *only* input; the generated struct
re-implements it fresh, with every magic offset traceable back to the citing spec.

**Ground-truth doctrine.** The original's wire format inside `doida.exe` (corroborated by the
Wireshark capture) is the absolute truth; the `packets/*.yaml` spec is the **derived truth** that
captured it across the firewall, and that spec is the *only* oracle this generator reads. The C# it
emits is measured against the spec, never the reverse — so if a field looks wrong, the fix is in the
spec (escalate to a spec-author), never a guess here. This skill stays clean-side and **never** reads
`_dirty/` or calls IDA.

> CLEAN ROOM. This skill reads ONLY `Docs/RE/packets/*.yaml` (and `Docs/RE/opcodes.md`,
> `Docs/RE/structs/*.md`, `Docs/RE/specs/*.md` for context) plus the C# source tree. It is
> FORBIDDEN to read any path containing `_dirty/` and it never calls IDA. If a spec field is
> missing or ambiguous, ask a spec-author to fix the YAML — do NOT consult the decompiler.

## Where output goes

Generated structs land in:

```
02.Network.Layer/MartialHeroes.Network.Protocol/Packets/<Name>.g.cs
```

`Network.Protocol` targets `net10.0`, `ImplicitUsings` + `Nullable` enabled, and depends on
`MartialHeroes.Shared.Kernel` (for enums / strongly-typed ids). The struct namespace is
`MartialHeroes.Network.Protocol.Packets`.

## The packet YAML spec

The generator consumes a small, line-oriented YAML-ish spec (parsed with the stdlib only — no
`pip install pyyaml`). See `references/sample-packet.yaml` for a fully commented template. The
shape:

```yaml
name: SmsgMovePlayer        # canonical message name -> struct name
opcode: 0x42                # hex; emitted as a const + a header comment
direction: S2C              # C2S | S2C (client POV)
size: 18                    # total bytes, or 'var'
spec: packets/move.yaml     # self-reference; cited in the generated header comment
fields:
  - { name: Opcode,   type: u8 }
  - { name: PlayerId, type: u32 }
  - { name: X,        type: f32 }
  - { name: Y,        type: f32 }
  - { name: Heading,  type: u16 }
  - { name: Flags,    type: enum:MoveFlags }   # enum from Shared.Kernel
  - { name: Name,     type: bytes[16] }        # fixed buffer -> [InlineArray]
```

### Supported field types

| Spec type | C# type emitted |
|---|---|
| `u8` `i8` | `byte` / `sbyte` |
| `u16` `i16` | `ushort` / `short` |
| `u32` `i32` | `uint` / `int` |
| `u64` `i64` | `ulong` / `long` |
| `f32` `f64` | `float` / `double` |
| `enum:<EnumName>` | `<EnumName>` (must exist in `MartialHeroes.Shared.Kernel`) |
| `bytes[N]` | a generated `[InlineArray(N)]` buffer struct `<Field>Buffer` |

No managed `string` fields are ever emitted in a wire struct — fixed text is `bytes[N]` →
`[InlineArray]`, per the blueprint.

## Workflow

1. **Refuse dirty input.** If asked to generate from anything under `_dirty/`, refuse and tell
   the user to promote it to a `packets/*.yaml` spec first. Only `Docs/RE/packets/*.yaml` is a
   valid source.

2. **Read the spec.** Open the target `Docs/RE/packets/<name>.yaml`. If a field type is
   unsupported, an `enum:` references an enum you cannot confirm in `Shared.Kernel`, or `size`
   disagrees with the summed field widths, stop and report — do not guess.

3. **Generate:**

   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/gen_packet.py --spec Docs/RE/packets/move.yaml `
       --out 02.Network.Layer/MartialHeroes.Network.Protocol/Packets
   ```

   The generator emits `<Name>.g.cs` with:
   - a header comment citing the source spec (`// spec: Docs/RE/packets/move.yaml`) and opcode;
   - `[StructLayout(LayoutKind.Sequential, Pack = 1)]` on the packet struct;
   - one field per spec entry in declared order (offsets are implied by `Pack = 1`);
   - a `public const byte OpcodeId = 0x..;` mirroring `opcode:` (named `OpcodeId` so it never
     collides with a wire field literally named `Opcode`);
   - a generated `[InlineArray(N)]` buffer struct for each `bytes[N]` field;
   - a compile-time size assertion comment when `size:` is fixed (so drift is caught).
   - `using MartialHeroes.Shared.Kernel;` when any `enum:` field is present.

4. **Wire the project reference if needed.** `Network.Protocol` must reference
   `Shared.Kernel` for `enum:` fields to compile. If the `ProjectReference` is not present yet,
   add it (the blueprint mandates `Protocol -> Kernel`) — do not invent other references.

5. **Build to verify (optional):**

   ```powershell
   dotnet build 02.Network.Layer/MartialHeroes.Network.Protocol/MartialHeroes.Network.Protocol.csproj
   ```

6. **Hand off.** The generated `.g.cs` is a *skeleton*. Serialization helpers, opcode→handler
   routing (source-generated), and validation are the `network-protocol-engineer`'s job — this
   skill only lays down the layout-correct struct.

## Decision points

- **`size:` disagrees with Σ field widths?** STOP — do not pad or truncate to make it fit. A
  mismatch means the spec is wrong or a field is missing; the byte total must equal the sum of
  the declared field widths for the struct to be wire-faithful. Send it back to the spec-author.
- **Fixed text field?** Always `bytes[N]` → `[InlineArray(N)]`, never a managed `string` — wire
  structs stay blittable so the zero-alloc `Span<byte>` decode path holds.
- **`enum:` field?** Confirm the named enum exists in `Shared.Kernel`. If it doesn't, stop and
  ask the spec-author/kernel-engineer to add it — never invent the enum or fall back to a raw int
  silently.
- **Variable-length payload (`size: var`)?** Generate the fixed head only and leave the tail to
  the engineer's serializer; do not fabricate a fixed buffer for a variable region.

Verify / Done when: `<Name>.g.cs` exists under `Packets/` with `[StructLayout(...Pack=1)]`, one
field per spec entry in declared order, `OpcodeId` mirroring `opcode:`, a header `// spec:`
citation, `[InlineArray]` buffers for every `bytes[N]`, and (optionally) the project builds. No
`_dirty/` path was read.

## Pitfalls (anti-patterns)

- **Never** read anything under `_dirty/` or call IDA — the `packets/*.yaml` spec is the only oracle.
- **Never** emit a managed `string` in a wire struct, drop `Pack = 1`, or reorder fields — any of
  these breaks blittability and wire parity.
- **Never** guess a missing/ambiguous field — bounce it to the spec-author.
- Don't add project references beyond the mandated `Protocol → Kernel`.

North star: serves **N2** — every generated struct is a byte-exact, citation-traced
re-implementation of the original wire layout.

## Hard rules

- **Never read `_dirty/`. Never call IDA.** The spec YAML is the only oracle.
- Every emitted offset/constant is traceable: the header comment cites the spec path, and the
  opcode const mirrors `opcode:`.
- Hot-path correctness: `Pack = 1`, no managed strings, `bytes[N]` → `[InlineArray]`. The
  struct must be blittable.
- Output only under `02.Network.Layer/MartialHeroes.Network.Protocol/Packets/`. Files end in
  `.g.cs` to mark them generated.
