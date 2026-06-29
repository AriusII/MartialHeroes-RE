---
name: ida-debugger-drive
description: Use to PILOT the LIVE IDA debugger and confirm a static RE hypothesis against ground truth in the running Martial Heroes client (Main.exe / doida.exe). Drive the maintainer's already-F9-launched debug session via the dbg_* MCP tools — set a breakpoint at a hypothesized address/function, continue until it hits on a real event (received packet, login, asset load), then read registers, memory, and packet buffers (dbg_read reads THROUGH PAGE_NOACCESS) to confirm a cipher boundary, an opcode dispatch, a struct at a live pointer, or a buffer pre/post-transform. Surfaces the probe/trace/watch/appcall instrumentation family as the no-dbg_start runtime-capture path. NEVER calls dbg_start. All findings land as neutral prose under Docs/RE/_dirty/. Pairs with ida-crypto-hunt, ida-opcode-map, and ida-py.
allowed-tools: mcp__ida__*, Read, Write, Bash(claude mcp *)
model: sonnet
effort: high
---

# ida-debugger-drive — confirm a static hypothesis against ground truth

This is the **debugger half of N1** (clean-room RE). Static analysis in IDA *forms* the
hypothesis — "this function is the decrypt; this address is the recv buffer; this object at
ESI is the player struct." The live debugger **confirms it against ground truth**: you stop the
real client at the hypothesized address and read the actual registers, memory, and buffers.
A confirmed fact is worth far more than a plausible static guess, and it is the only way to
read a packet **before** the cipher or an object **at** its live pointer.

You **pilot a session the maintainer already launched** (F9 inside IDA, modal trust dialog
accepted). The MCP cannot dismiss that modal and a session is already active, so this skill
**never** starts the debugger — it drives the running one. Everything you observe is **dirty**
(derived directly from the copyrighted binary at runtime) and lands ONLY under
`Docs/RE/_dirty/` as neutral prose.

> **Project reality:** the original servers are dead, so live driving stops at the login wall —
> there is no live world to step through. But two ground-truth windows remain fully reachable:
> (a) **build-time / boot captures** (asset loads, VFS reads, the login send path), and
> (b) the **pre-encryption login packet**, readable in the buffer *before* the cipher transform.
> Aim breakpoints at those reachable events; do not assume a logged-in session.

## Preconditions (verify in order; STOP on any failure)

1. **A live debug session must already exist.** The maintainer F9-launches the client inside
   IDA and accepts the modal trust dialog. You do **not** launch it. Confirm liveness in Step 1
   below (a register read succeeds ⇒ a session is running). If it is not live, STOP and ask the
   maintainer to F9-launch — **never** call `dbg_start` to "fix" it (the call fails; the MCP
   cannot dismiss IDA's modal, and a session may already be mid-flight).
2. **MCP must be on the `?ext=dbg` endpoint.** List the `mcp__ida__*` tools and confirm the
   debugger tools (`mcp__ida__dbg_*` — registers, memory read, breakpoints, continue, step) are
   present. **If the `dbg_*` tools are absent you are on the BASE endpoint** (static tools only)
   — re-register on the debugger-extended endpoint and restart the session:

   `claude mcp add --transport http ida "http://127.0.0.1:13337/mcp?ext=dbg"`

3. **A concrete static hypothesis.** You have one specific target to breakpoint — an address or
   a function recovered from static analysis (`ida-recon`, `ida-opcode-map`, `ida-crypto-hunt`,
   `ida-xref-map`). Without a target, do the static pass first; do not breakpoint blindly.
4. **The right DB.** The IDB is the Martial Heroes client (`Main.exe` / `doida.exe`), analysis
   finished. STOP on a wrong or empty database.

## The debugger toolset (concrete tool roles)

Breakpoints + stepping + reads, discovered at runtime on the `?ext=dbg` endpoint:

- **Breakpoints:** `mcp__ida__dbg_add_bp`, `mcp__ida__dbg_set_bp_condition`,
  `mcp__ida__dbg_set_bp_hit_count`, `mcp__ida__dbg_delete_bp`, `mcp__ida__dbg_toggle_bp`,
  `mcp__ida__dbg_bps`.
- **Control flow:** `mcp__ida__dbg_continue`, `mcp__ida__dbg_run_to`, `mcp__ida__dbg_step_into`,
  `mcp__ida__dbg_step_over`, `mcp__ida__dbg_step_out`.
- **Registers:** `mcp__ida__dbg_gpregs`, `mcp__ida__dbg_regs_named`, `mcp__ida__dbg_regs_all`.
- **Memory:** `mcp__ida__dbg_read` (reads **THROUGH `PAGE_NOACCESS`**) — and `mcp__ida__dbg_write`
  **only** to deliberately recover a value (never to cheat/patch).
- **State:** `mcp__ida__dbg_stacktrace`, `mcp__ida__dbg_status`, `mcp__ida__dbg_threads` /
  `mcp__ida__dbg_select_thread`, `mcp__ida__exception_config`.

## The no-`dbg_start` runtime-capture family (prefer for bulk observation)

Beyond raw breakpoints, prefer the non-intrusive runtime-capture family for bulk observation — none
of these spawn the process, so they honor the never-`dbg_start` rule while the maintainer's F9 session
runs: `mcp__ida__probe_net` (auto-capture every socket send/recv), `mcp__ida__probe_api_call` (log a
specific API's calls+args), `mcp__ida__probe_add`/`mcp__ida__probe_arm`/`mcp__ida__probe_drain`/`mcp__ida__probe_stats`
(install/arm/harvest lightweight probes at an EA), `mcp__ida__trace_calls`/`mcp__ida__trace_summary`
(what got called and how often), `mcp__ida__watch_field`/`mcp__ida__watch_region` (catch WHO writes a
struct member/region), `mcp__ida__run_until` (advance to an interesting state),
`mcp__ida__appcall`/`mcp__ida__appcall_inspect` (invoke the client's own decryptor on captured
ciphertext — the definitive crypto confirmation), `mcp__ida__read_struct_live` (decode a struct at a
live pointer), `mcp__ida__hierarchy_runtime_overlay` (map observed runtime calls onto the static call
graph), `mcp__ida__memory_scan` (find a live buffer/key in RAM), `mcp__ida__stop_context` (snapshot
runtime state). HARD RULE unchanged: NEVER `dbg_start`/`dbg_attach`/`dbg_detach`/`dbg_exit` — those are
maintainer-only.

> **Anti-cheat caveat (XTrap).** An XTrap-protected client may resist a live debugger — prefer
> breakpoints on your own hypothesized handler EAs over API-level breakpoints, and lean on the capture
> oracle (`probe_net`/`appcall`/`pcap`) when dynamic stepping is hostile.

## Operating loop

`confirm-live → breakpoint → continue-to-event → read-ground-truth → (diff / step) → cleanup → record`.
Re-enter at *breakpoint* for the next hypothesis in the same live session. Record each confirmed
hypothesis in-IDB with `mcp__ida__journal_note` (paired with the `_dirty/` write below).

## Steps

Discover the exact `mcp__ida__dbg_*` tool names at runtime (they vary by build). The names used
below are the canonical roles; match them to what the connected server actually exposes.

1. **Confirm a live session — do NOT start one.** Call the general-purpose register read
   (`dbg_gpregs`). If it returns a register set, a session is **live** and you may proceed. If it
   errors / returns nothing, there is **no live session**: STOP and ask the maintainer to
   F9-launch the client and accept the trust dialog. Never call `dbg_start`.

2. **Set the breakpoint.** Add a breakpoint (`dbg_add_bp`) at the hypothesis target (the
   address/function from Precondition 3). For a function, breakpoint its entry; to read a buffer
   *after* it is filled, breakpoint the instruction just past the fill (use the static listing to
   pick the address — never paste the listing).

3. **Continue to the event.** Resume the process (`dbg_continue`, async) and trigger the real
   event from the running client — a received/sent packet, the login submit, an asset/VFS load
   during boot. Wait for the breakpoint to hit. If it never hits, the event is not reachable in
   this dead-server reality (see the project-reality note) or the hypothesis address is wrong —
   record that as a negative result and revise the hypothesis.

4. **Read ground truth at the break.** With the process stopped:
   - **Registers / pointers** — `dbg_gpregs` for the register file (the `this`/struct pointer,
     buffer pointer, length, return value).
   - **Memory / buffers** — `dbg_read` at the pointer of interest. **`dbg_read` reads THROUGH
     `PAGE_NOACCESS`**: a packet buffer the loader marked no-access after build is still readable
     here — this is exactly why the debugger sees what static analysis cannot.
   - **Advance** — `dbg_step_over` / `dbg_run_to` to move past a call or to the next address and
     re-read, so you can watch a value change across an operation.
   The bundled `${CLAUDE_SKILL_DIR}/scripts/dump_buffer.py` composes a one-shot
   "read register R, then `dbg_read` N bytes at [R]+offset, emit a hex/byte summary" via the
   `ida-py` exec path — fill its CONFIG block (register, offset, length) instead of hand-writing
   the read. Reach for `ida-py` for any richer one-shot read.

5. **Decision points — what to read for which hypothesis:**
   - **Cipher boundary (with `ida-crypto-hunt`):** read the buffer **immediately before** and
     **immediately after** the transform routine — breakpoint pre-call, `dbg_read` the buffer,
     `dbg_step_over` the transform, `dbg_read` the same buffer again. The **byte-diff is the
     ground-truth transform** (and the pre-image is the plaintext packet — the pre-encryption
     login packet is readable exactly here). Note state size, per-byte vs per-block, and where the
     key entered.
   - **Opcode dispatch (with `ida-opcode-map`):** breakpoint the dispatcher; on a hit, read the
     register/buffer holding the major/minor opcode and the resolved handler target. Confirms the
     static switch maps the opcode to the handler you predicted.
   - **Struct at a live pointer (with `ida-struct-recovery`):** breakpoint where the object is in
     hand, take its pointer from `dbg_gpregs`, and `dbg_read` the object — confirm field offsets
     and shapes against the static struct map at real values.

6. **Clean up.** Delete the breakpoint(s) you added (`dbg_delete_bp`) so the maintainer's session
   is left clean. Leave the process running — do not kill or detach the maintainer's session.

7. **Record — neutral, dirty-only.** Write the confirmation under `Docs/RE/_dirty/dbg/` (create
   it if absent). Begin the file with a `> DIRTY — runtime ground truth from <binary>; never
   commit; do not copy into specs.` banner and the binary's SHA-256 (from `ida-recon`/`names.yaml`
   if known). Describe what was confirmed in **neutral prose and math** — observed field offsets,
   buffer shapes, the byte-level transform, the confirmed dispatch — **never** pasted pseudo-C and
   **never** the in-game credentials used at the live login.

## Done when

- The breakpoint hit on the intended real event and ground truth (regs/memory/buffer) was read.
- The static hypothesis is **confirmed, refined, or refuted** with concrete observed values.
- Breakpoints you added are deleted and the session is left live and clean.
- The finding is written under `Docs/RE/_dirty/dbg/` as neutral prose, SHA-tagged, credential-free.

## Hard rules (the firewall — non-negotiable)

- **NEVER call `dbg_start`.** The maintainer F9-launches; you pilot the live session. If no
  session is live, STOP and ask — do not attempt to start one.
- **Read-only confirmation.** Do **not** `dbg_write` the target's memory or registers — except to
  *deliberately recover* a value (e.g. force a known input), and **never** to cheat, patch, or
  alter behavior. The default posture is observe, not mutate.
- **Credentials are session-only.** Any username/password/PIN typed at the live login is
  **SESSION-ONLY**: never written to `_dirty/`, never pasted into a spec, never echoed in a reply.
  When you read the pre-encryption login packet, redact the credential bytes in what you record.
- **No copyrighted bytes leave the quarantine.** Outputs go ONLY to `Docs/RE/_dirty/` (gitignored).
  Never write to a committed spec (`Docs/RE/opcodes.md`, `packets/`, `formats/`, `structs/`,
  `specs/`, `names.yaml`, `journal.md`) or any `0X.*` source folder, `.cs`, `.csproj`, or `.slnx`.
- **Neutral prose only.** Never paste Hex-Rays pseudo-C; never carry a decompiler autoname
  (`sub_xxxx`, `loc_xxxx`) as an identifier into a committed file or C#; raw addresses live in
  `_dirty/` only. Enumerate facts (offsets, shapes, byte diffs) in words and math.
- **STOP conditions.** Stop and report exactly what you got if: no live session, wrong/empty DB,
  or the `dbg_*` toolset is missing (you are on the base endpoint — re-register on `?ext=dbg`).
  Never invent register values, memory contents, or byte diffs.
- **Hand-off.** Confirmation crosses the firewall only when a **spec-author** rewrites it (never
  copies) into a committed spec with a `journal.md` entry. This skill does not author clean specs.
