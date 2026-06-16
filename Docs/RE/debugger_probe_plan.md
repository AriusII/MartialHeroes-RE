# Debugger Probe Plan — open residuals (VFS-DEEP-II)

> Committed, neutral checklist for a **maintainer-armed** live IDA debugger session
> (the maintainer F9-launches the real client; the assistant pilots the live session but
> **never** calls `dbg_start`). Raw breakpoint addresses live in the gitignored dirty notes
> under `Docs/RE/_dirty/campaign-vfs-deep-ii/ida/` (in their `DEBUGGER PROBE` blocks); the
> located functions are named in `Docs/RE/names.yaml`. Each probe upgrades a residual from
> PLAUSIBLE / UNVERIFIED to CONFIRMED. See `Docs/RE/specs/ida-debugger-drive` methodology.

## 1. `mobinfo.mi` — 7-field widget semantics  (UNRESOLVED → CONFIRMED)
The `.mi` loader is **not statically locatable**: the file is opened through the generic by-name
VFS reader with a non-literal path (no `mobinfo`/`.mi` string in the binary), and the 28-byte record
stride is swamped by 28-byte MSVC `std::string` arrays.
- **Probe:** breakpoint the by-name VFS open router + the load-whole-file helper; filter for a path
  ending `mobinfo.mi` (trigger by **targeting a monster** in-game). The return frame is the real
  loader. Read the ~592-byte buffer (`4-byte count` + `21 × 28B` records, 7×u32 each) and single-step
  the consume loop to bind each of the 7 `u32` fields to the enriched hypotheses (ordinal /
  caption-couple / packed-icon-ids / kind-link / `0xFFFFFFFF` sentinel).
- **Spec:** `Docs/RE/formats/mi.md`.

## 2. environment — runtime `OPTION_BRIGHT` / `K_ambient`  (thin residual)
Static analysis proved `K_ambient` = static `0.0` (one reader, zero writers) and the `OPTION_BRIGHT`
INI default = `100`. The only open point is whether a user's on-disk `DoOption.ini` overrides the
`100` default at runtime.
- **Probe:** after option init, read the brightness value (i32, percent, default 100) from the option
  singleton; read the lighting-manager ambient bytes (expected `255` at default brightness).
  Functions: `Lighting_ApplyBrightnessAmbient`, `Renderer_SetDeviceAmbient` (`names.yaml`).
- **Spec:** `Docs/RE/specs/environment.md`, `Docs/RE/formats/environment_bins.md`.

## 3. `items.scr` — stat-field roles across item families  (framing CONFIRMED; roles UNVERIFIED)
The 548-byte record framing is confirmed; the roles of the numeric stat fields beyond
name / uid / desc / `item_type_tag` are not assigned.
- **Probe:** breakpoint the loader (`ItemsScr_LoadRecord` / `ItemsScrRecord_Ctor`, `names.yaml`);
  after the `0x224` block read, dump one full record across several item families (1H weapon, 2H
  weapon, armor, consumable) and correlate the numeric fields with the in-game tooltip stats.
- **Spec:** `Docs/RE/formats/items_scr.md §1.4`.

## 4. `.mud` bytes 0/1 — wlk/run footstep indices  (PLAUSIBLE → CONFIRMED)
The `.mud` tile has two currently-spare bytes (0,1) and exactly two extra sound-table families exist
(`.wlk`, `.run`). Strong but unverified pairing.
- **Probe:** while **walking vs running** in-game, watch the footstep-sound selection read the current
  tile and confirm bytes 0/1 index `soundtableNNN.wlk` / `.run`. (A pure harness footstep-trigger
  trace can also settle this without the debugger.)
- **Spec:** `Docs/RE/formats/mud.md`, `Docs/RE/formats/sound_tables.md`.

## Out of scope — capture-pending (no `.pcapng` in the tree)
Combat/chat on-wire action codes and several protocol framings remain `CAPTURE-UNVERIFIED`. They
require a packet capture, not the debugger, and are tracked in `Docs/RE/specs/combat.md` /
`Docs/RE/specs/chat.md`.
