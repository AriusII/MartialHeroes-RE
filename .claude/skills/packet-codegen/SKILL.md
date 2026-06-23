---
name: packet-codegen
description: Use to author the clean-room wire layer from committed specs — two modes. PACKET-STRUCT generates a C# packet struct skeleton from a Docs/RE/packets/*.yaml field spec ([StructLayout(LayoutKind.Sequential, Pack=1)] into Network.Protocol, fixed byte buffers as [InlineArray], enum fields resolved to Shared.Kernel enums, header comment citing the source spec). OPCODE-CATALOG curates the authoritative Docs/RE/opcodes.md catalog (no IDA addresses) — add/curate entries (opcode + name + direction + size + packet-yaml link + status), schema-validate the table, detect duplicate opcode ids, scan for address leakage, and flag opcodes seen in captures but missing from the catalog. Both REFUSE to read anything under _dirty/ and never call IDA — clean-room only; the committed spec is the only oracle.
allowed-tools: Read Write Bash(dotnet build *) Bash(python *)
model: sonnet
effort: high
---

# packet-codegen

Author the clean-room wire layer from committed specs — **two modes** that share one firewall stance
(read only `Docs/RE/` specs + the C# tree; never `_dirty/`, never IDA):

- **Mode A — PACKET-STRUCT codegen:** generate a zero-allocation C# packet struct from a
  `Docs/RE/packets/*.yaml` spec into `Network.Protocol`.
- **Mode B — OPCODE-CATALOG curation:** maintain `Docs/RE/opcodes.md`, the authoritative opcode table
  (no addresses), and validate it.

**Ground-truth doctrine.** The original's wire format + opcode dispatch table inside `doida.exe`
(corroborated by the Wireshark capture oracle) is the absolute truth; the `packets/*.yaml` specs and
`opcodes.md` are the **derived truth** that captured it across the firewall, and they are the *only*
oracle these modes read. The C# / catalog rows are measured against the spec, never the reverse — if a
field or id looks wrong, the fix is in the spec (escalate to a spec-author / `re-promote`), never a
guess here.

> CLEAN ROOM. These modes read ONLY `Docs/RE/packets/*.yaml`, `Docs/RE/opcodes.md`,
> `Docs/RE/structs/*.md`, `Docs/RE/specs/*.md`, `Docs/RE/names.yaml`, plus the C# source tree. It is
> FORBIDDEN to read any path containing `_dirty/` and they never call IDA. If a spec field is missing
> or ambiguous, ask a spec-author to fix the YAML — do NOT consult the decompiler.

# Mode A — PACKET-STRUCT codegen

Generate a zero-allocation C# packet struct from a committed `Docs/RE/packets/*.yaml` spec — the
clean-room bridge from the documented wire layout to `Network.Protocol`, with every magic offset
traceable back to the citing spec.

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
   routing (source-generated), and validation are the `network-engineer`'s job — this skill only
   lays down the layout-correct struct.

# Mode B — OPCODE-CATALOG curation

Curate `Docs/RE/opcodes.md` — the clean-room source of truth for the wire protocol's message opcodes.
This catalog carries **only promoted, neutral facts**: address-level discovery stays in
`_dirty/opcodes.raw.md` (gitignored) and never crosses into this file. It is consumed by the
`network-engineer` and by Mode A.

## The catalog schema (must not drift)

`Docs/RE/opcodes.md` holds one Markdown table. Every data row has exactly these columns:

| Column | Rule |
|---|---|
| `Opcode` | Hex, lowercase `0x` form, e.g. `0x42`. **No IDA addresses, ever.** Unique. |
| `Name` | Canonical message name. Convention: `Cmsg*`/`Smsg*` (client→/server→) e.g. `SmsgMovePlayer`. Must agree with `Docs/RE/names.yaml` `opcodes:`. |
| `Direction` | `C2S` or `S2C`, from the **client's** point of view. |
| `Size (bytes)` | Fixed integer (e.g. `12`) or `var` for variable-length payloads. |
| `Packet spec` | Link to the field spec, e.g. `packets/move.yaml`, or `—` if none yet. |
| `Status` | `draft` · `observed` · `confirmed` · `implemented` (legend kept in the file). |
| `Notes` | Short neutral prose. No pseudo-code, no addresses. |

Status legend: `draft` (hypothesized) · `observed` (seen in a capture) · `confirmed` (cross-checked
against the binary's dispatch table — the neutral fact crosses via `re-promote`, never an address) ·
`implemented` (C# struct + handler exist).

## Workflow (Mode B)

1. **Read the current catalog.** Open `Docs/RE/opcodes.md`; preserve its header prose, column order,
   and the status legend exactly.
2. **Add or update an entry**, keeping the table sorted by opcode ascending: newly seen in a capture →
   `Status = observed`, `Packet spec = —`; a written field spec exists → link `packets/<name>.yaml`,
   bump to `draft`+; confirmed against the binary's dispatch table (by an analyst, in `_dirty/`) →
   `confirmed` (copy only the neutral fact — it exists, its size — never the address); C# struct +
   handler exist → `implemented`.
3. **Validate before saving/committing:**
   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/validate_opcodes.py Docs/RE/opcodes.md
   ```
   The validator parses the table and checks every row has all 7 columns; normalizes + validates each
   opcode is a unique hex literal (**fails on duplicate ids**); checks `Direction in {C2S,S2C}`,
   `Status in {draft,observed,confirmed,implemented}`, `Size` is an int or `var`; **scans for address
   leakage** — any `sub_`, `loc_`, `0x004…`-style absolute address, or `.text:` token is a hard error;
   and confirms each non-`—` `Packet spec` link points under `packets/`.
4. **Cross-check against captures (recommended).** Pass an opcode inventory from `pcap-extract` so the
   validator flags opcodes **seen in captures but missing from the catalog**:
   ```powershell
   python ${CLAUDE_SKILL_DIR}/scripts/validate_opcodes.py Docs/RE/opcodes.md --seen 0x01,0x02,0x42
   python ${CLAUDE_SKILL_DIR}/scripts/validate_opcodes.py Docs/RE/opcodes.md --seen-file _dirty/captures/seen_opcodes.txt
   ```
   Missing-from-catalog opcodes are reported as warnings to add as `observed` rows.
5. **Keep `names.yaml` in sync.** When you add/rename an opcode, mirror it in `Docs/RE/names.yaml`
   under `opcodes:` (same id, name, direction) — the catalog is the human-facing table, `names.yaml`
   is the machine glossary `ida-annotate`'s names-sync mode uses.
6. **Journal it.** Any committed change to `opcodes.md` gets a one-line entry in `Docs/RE/journal.md`
   (which specs changed, by whom, no pseudo-code) — via the `preservation` session-log mode.

**Mode B notes:** this protocol's opcodes are `(major<<16)|minor` — a message is a `(major, minor)`
pair (melee `2/52`, chat `2/7`, storage `2/142`, buff `4/102`); record the catalog id in the file's
existing single-hex convention but keep the pair legible in `Name`/`Notes` so it round-trips to the
wire frame `[u16 major][u16 minor]`. The `OnUnhandled` fallback set (0/0, 3/1, 3/7, 3/4, 3/6, 3/23) is
`implemented`. A validator failure on an address token means dirty material leaked in — strip it; the
catalog never carries `sub_`/`loc_`/absolute addresses.

**Mode B — Verify / Done when:** `validate_opcodes.py` passes (all 7 columns, unique ids, valid
direction/status/size, **zero address tokens**, every `Packet spec` link resolves under `packets/`);
capture-`--seen` warnings are triaged; `names.yaml` `opcodes:` mirrors the row; the change is journaled.

## Decision points (Mode A)

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

- **Never** read anything under `_dirty/` or call IDA — the committed spec is the only oracle (both modes).
- **Never** emit a managed `string` in a wire struct, drop `Pack = 1`, or reorder fields — any of
  these breaks blittability and wire parity (Mode A).
- **Never** guess a missing/ambiguous field — bounce it to the spec-author.
- **Never** store an address, `sub_`/`loc_`, or any decompiler output in `opcodes.md` — the validator
  fails the file and the firewall is breached (Mode B).
- **Never** duplicate an opcode id — duplicates are a hard validation failure (Mode B).
- Don't add project references beyond the mandated `Protocol → Kernel`; don't reformat `opcodes.md`'s
  prose, legend, or column order — edit table rows only.

North star: serves **N2** — every generated struct is a byte-exact, citation-traced
re-implementation of the original wire layout.

## Hard rules

- **Never read `_dirty/`. Never call IDA.** The spec YAML / `opcodes.md` are the only oracles (both modes).
- Every emitted offset/constant is traceable: the Mode A header comment cites the spec path and the
  opcode const mirrors `opcode:`; Mode B carries **no addresses, ever** — only *what* a message is,
  never *where* it lives (the validator fails the file on an address-shaped token).
- Hot-path correctness (Mode A): `Pack = 1`, no managed strings, `bytes[N]` → `[InlineArray]`. The
  struct must be blittable. Output only under
  `02.Network.Layer/MartialHeroes.Network.Protocol/Packets/`; files end in `.g.cs`.
- Catalog discipline (Mode B): one opcode id appears at most once; neutral prose only; do not reformat
  the file's prose, legend, or column order — edit table rows only.
