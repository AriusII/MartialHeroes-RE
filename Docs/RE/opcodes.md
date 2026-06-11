# Opcode Catalog (authoritative)

The clean-room source of truth for the wire protocol's message opcodes. Consumed by the
`network-protocol-engineer` and validated by the `opcode-catalog` skill.

**Clean-room rules for this file:**
- **No IDA addresses** here. Address-level discovery lives in `_dirty/opcodes.raw.md`; this catalog
  carries only the promoted, neutral facts.
- Direction is from the client's point of view: `C2S` (client→server) or `S2C` (server→client).
- Each opcode with a known payload links to its field spec under `packets/`.

| Opcode | Name | Direction | Size (bytes) | Packet spec | Status | Notes |
|---|---|---|---|---|---|---|
| _0x00_ | _example_ | _C2S_ | _var_ | _packets/example.yaml_ | _draft_ | _replace with real entries_ |

Status legend: `draft` (hypothesized) · `observed` (seen in a capture) · `confirmed` (cross-checked
against the binary's dispatch table) · `implemented` (C# struct + handler exist).
