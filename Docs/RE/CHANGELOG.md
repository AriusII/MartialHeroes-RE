# Docs/RE — Corpus Changelog

---

## 2026-06-30 — CYCLE 15: RE-unlock promotion (W→P) + conflict resolution

### Summary

CYCLE 15 statically recovered the load-bearing capture/debugger-pending facts (registry R-02..R-45 and
the HIGH §7 crypto/asset gaps) from `doida.exe` at IDB anchor **f61f66a9**, then promoted them into the
committed corpus. 22 facts recovered to dirty-room notes; 18 promoted implementation-ready; 4 remain
runtime-only (R-CAP, capture-pending, non-blocking). 18 committed files updated (1 new). All
consumer-confirmed (read from the code that uses each field/code); the firewall held — no decompiler
identifiers, raw addresses, or cipher key bytes entered any committed file.

### Files updated (18)

`specs/`: handlers, net_contracts, inventory_trade, social, world_exit, crafting, cash_shop_browser,
crypto, anticheat, skills, **cube_gamble (NEW)** · `structs/`: actor, stats, item · `formats/`:
terrain_scene, terrain_layers, shaders · `opcodes.md`.

### Tier-1 corrections (the binary disproved the prior spec)

- **crypto.md** — SEED-128 is NON-WIRE (two local on-disk file decoders, not the network gate or RSA
  chain); RC4 is DEFINITIVELY ABSENT (the candidate cluster is X-Trap/PE-integrity: bundled SSLeay
  SHA-1 + a pi-constant Blowfish-family block cipher with embedded literal-string keys); the RSA
  modexp entry corrected to the `mexpkm`/`Bignum_ModExp` wrapper (the prior named entry is unused).
- **anticheat.md** — the GXProtect override classes are compiled but NOT statically wired; the shipped
  client runs the base no-op singleton. The page-guard cadence is event-driven (PAGE_NOACCESS bracket
  per auth op), not timed.
- **shaders.md** — `dotoonshading.psh` is the normal-state cel pixel shader (2nd of 5), not an orphan;
  the actual orphan is the unused `.vsh`.
- **handlers.md** — char rename is opcode 3/6 (not 3/7); 3/7 Subtype 2 = delete.
- **item.md** — equip acks 4/12 & 4/22 are two-armed (≥2 = no-op); 4/16 applies on any non-zero.

### Conflicts resolved by IDA re-read (2026-06-30)

- **HP width (structs/actor.md vs structs/stats.md).** Two promotion lanes disagreed. Re-reading the
  5/53 and 5/32 handlers settled it: current HP is a single signed **i64 at Actor +0xB0** (8 bytes);
  current MP is an **i32 at +0xB8** (the prior +0xB4 reading was the HP high dword); there is no inline
  stamina slot (player-global mirror only). `actor.md`'s three vitals tables were corrected to match
  `stats.md`. (Resolves R-21.)
- **Guild Gate (specs/social.md §5.4).** The 4/65 full-sync Gate byte was inverted in CYCLE 14:
  `Gate == 1` = full-sync apply (50-member arrays); any other value = leave/no-guild. The leave-pending
  flag gates only the leave effects. CONSUMER-CONFIRMED; the CYCLE-14 reading is superseded.

### Still runtime-only (R-CAP — capture/debugger-pending, non-blocking)

5/52 literal damage scale + actor-key encoding (R-20, R-44); equip result ≥2 + any C2S-by-result
(R-43, R-45); guild cooldown magnitude + MessageDB label text (R-06 residual); storage Op →
deposit/withdraw/move mapping (R-10 residual). Each carries a breakpoint plan for a live session.

### Firewall

Promotion crossed the dirty→spec boundary via a neutral doc-authoring pass (the spec-author role).
Dirty-room evidence addresses stayed in the gitignored `Docs/RE/_dirty/cycle15/` notes; the committed
specs carry none. Cipher key bytes and S-box tables are characterized by role/length only, never
transcribed. Leak scan over all added spec lines: clean. `names.yaml` and per-file IDB annotations are
deferred to a dedicated ida-toolsmith pass.

---

## 2026-06-29 — Consolidation wave (183 → 164 files)

### Summary

- Starting count: 183 .md files across formats/, scenes/, specs/, structs/, vfs/, and the root.
- Ending count: 164 .md files (183 − 19 definite deletions).
- 18 master files received merged content.
- 19 source files deleted after content was absorbed into their masters and coverage was verified.
- Filenames preserved throughout — no renames.
- 10 of the 19 source files were tainted (French prose, raw addresses, decompiler autonames, or
  _dirty/ references); their merges doubled as firewall remediation passes (scratchpad draft,
  Grep self-scrub, strip-then-fold discipline).

---

### Deletions

19 definite deletions. All content was verified present in the absorbing master before deletion.

| Deleted file | Absorbed into | Reason |
|---|---|---|
| `specs/anticheat_core.md` | `specs/anticheat.md` | Tainted fragment (French prose, raw addresses, _dirty/ links); unique vtable/destructor/monitor-thread/IAT/PEB/exit-code corrections absorbed post-scrub |
| `specs/xtrap_integration.md` | `specs/anticheat.md` | Tainted component fragment of the same anticheat layer; unique XTrap DLL loading sequence, token decryption path, telemetry socket, PE self-check absorbed post-scrub |
| `specs/bignum_flint.md` | `specs/crypto.md` | Tainted substrate fragment (FLINT++ math library); LINT object layout and Montgomery exponentiation are the RSA implementation substrate documented in crypto.md — unique facts absorbed post-scrub |
| `specs/network_protocol.md` | `specs/network_dispatch.md` | Tainted stub (153 lines, 40%); §1 is verbatim duplicate of network_dispatch.md §1; §3 fragments net_contracts.md; zero unique clean content survived scrub — effectively a dead-file removal |
| `specs/sound_system.md` | `specs/sound.md` | Tainted precursor (147 lines, 25%); French, raw addresses, decompiler autonames; unique SoundManager memory field offsets absorbed post-scrub |
| `specs/terrain_system.md` | `specs/terrain-streaming.md` | Tainted precursor (145 lines, 25%); French, raw addresses, decompiler autonames; unique TerrainCell layout details and ground-height sampling specifics absorbed post-scrub |
| `specs/ui_scene_integration.md` | `specs/ui_system.md` | Tainted dirty-room precursor (201 lines, 30%); French, raw addresses, _dirty/ refs; unique 9-state scene lifecycle and state-transition specifics absorbed post-scrub into §0/§3 |
| `specs/ui_asset_loader.md` | `specs/texture_upload.md` | Tainted fragment (148 lines, 30%); despite "ui_" prefix the subject (GTextureManager/GHTex 2D-texture cache) belongs in texture_upload.md — unique field details absorbed post-scrub; NOT routed to ui_system.md |
| `specs/cal3d_runtime.md` | `specs/skinning.md` | Tainted fragment (254 lines, 80%); French, _dirty/ links; Cal3D class catalog (CoreMesh, CoreBone, etc.) and 5-stage pipeline stage-sequence are the substrate of the deform chain skinning.md specifies — unique facts absorbed post-scrub |
| `specs/scene_graph.md` | `structs/scene_graph_nodes.md` (§2 node layout) + `specs/render_pipeline.md` (§3.2 frustum behavior) | Tainted SEVERE (353 lines, 30%); French, raw vtable addresses, _dirty/ links, decompiler autonames; §2 node layout already superseded by scene_graph_nodes.md; §3.2 frustum-traversal behavior absorbed into render_pipeline.md §scene-graph-traversal; no remaining unique content |
| `specs/client_architecture.md` | `specs/client_workflow.md` | Smallest of the master-synthesis trio (568 lines vs 1460); most summary-level; unique singleton lists and module names absorbed into client_workflow.md §architecture-index; client_runtime.md (2,013 lines, deepest subsystem coverage) left untouched |
| `formats/cell_post.md` | `formats/authoring_sidecars.md` | 43-line stub (75%); .ted.post format already fully covered in authoring_sidecars.md §post; no unique content |
| `formats/cell_pre.md` | `formats/authoring_sidecars.md` | 68-line stub (80%); .bud.pre/.sod.pre outline already fully covered in authoring_sidecars.md §pre family; no unique content |
| `scenes/loading.md` | `scenes/load.md` | 297-line fragment (75%); same content as load.md §5A (two immediate-mode textured quads, vertical top-down fill, UV constants, SFX 920100100) |
| `structs/ghtex.md` | `structs/texture_manager.md` | 65% fragment (127 lines); §1.3/§1.4 already corrected in texture_manager.md; surviving unique vtable detail (§1.1/§1.2) absorbed into texture_manager.md §ghtex-vtable |
| `vfs/archive_container.md` | `formats/pak.md` | Identical content (64 lines, no verification banner); 24-byte header/144-byte TOC already in pak.md §1 |
| `vfs/census_and_formats.md` | `specs/vfs_overview.md` | Identical content (87 lines, no verification banner); 49-extension census already in vfs_overview.md §3 |
| `vfs/io_subsystem.md` | `specs/vfs_loader_dispatch.md` | Same coverage (115 lines, no verification banner); CVFSManager lifecycle/mount/DiskFile already in vfs_loader_dispatch.md; any unique offline-patching detail verified and folded before deletion |
| `vfs/linkage_and_usage.md` | `specs/asset_linkages.md` | Same coverage (69 lines, no verification banner); five index manifests already in asset_linkages.md §linkage-layer |

---

### Masters that received content

18 master files were modified to absorb source content or receive in-place prose corrections.

| Master | Note |
|---|---|
| `specs/anticheat.md` | Received XTrap DLL loading sequence as new §XTrap Integration section; received vtable/destructor clarification, monitor-thread algorithm correction, IAT-snapshot mechanism, PEB debugger-presence check, and fatal exit-code table from anticheat_core.md; taint corrections marked static-hypothesis where not re-verified at f61f66a9 |
| `specs/crypto.md` | Received FLINT++ LINT object layout and sliding-window Montgomery exponentiation details as new §RSA Substrate appendix |
| `specs/network_dispatch.md` | Confirmed zero unique surviving content from network_protocol.md after scrub; deletion was effectively a dead-file removal with no prose change to the master |
| `specs/sound.md` | Received unique SoundManager memory field offsets not previously present in §struct |
| `specs/terrain-streaming.md` | Received unique TerrainCell layout details and ground-height sampling specifics; struct-heavy detail also redirected to structs/terrain_cell.md |
| `specs/ui_system.md` | Received unique 9-state scene lifecycle detail and state-transition specifics, folded into §0/§3 |
| `specs/texture_upload.md` | Received GTextureManager/GHTex 2D-texture cache cycle field details; destination confirmed as texture_upload.md, not ui_system.md |
| `specs/skinning.md` | Received Cal3D class catalog (CoreMesh, CoreBone, etc.) and 5-stage pipeline stage-sequence as new §cal3d-class-catalog |
| `structs/scene_graph_nodes.md` | Received residual node-layout field values from scene_graph.md §2 post-scrub (minimal: most content was already superseded by this file) |
| `specs/render_pipeline.md` | Received frustum-traversal behavior facts from scene_graph.md §3.2 post-scrub as new §scene-graph-traversal |
| `specs/client_workflow.md` | Received unique singleton lists and module names from client_architecture.md, folded into §architecture-index |
| `formats/authoring_sidecars.md` | Confirmed §post and §pre families already covered all content of cell_post.md and cell_pre.md; no new prose added; deletions confirmed |
| `scenes/load.md` | Confirmed §5A already covered all content of loading.md (two textured quads, UV constants, SFX 920100100); no new prose added; deletion confirmed |
| `structs/texture_manager.md` | Received unique GHTex vtable detail from ghtex.md §1.1/§1.2 (§1.3/§1.4 had already been corrected in this file) |
| `formats/pak.md` | Confirmed §1 already covered all content of archive_container.md; no new prose added; deletion confirmed |
| `specs/vfs_overview.md` | Confirmed §3 already covered all content of census_and_formats.md; no new prose added; deletion confirmed |
| `specs/vfs_loader_dispatch.md` | Verified io_subsystem.md for unique offline .pre/.post patching pipeline detail; any unique detail folded into §io-backends; deletion confirmed |
| `specs/asset_linkages.md` | Confirmed §linkage-layer already covered all content of linkage_and_usage.md; no new prose added; deletion confirmed |

---

### Cross-reference rewiring

All files that held outbound links to deleted files were updated to point at the absorbing master.
Absorbed-source notes were added to receiving masters for provenance. Files updated:

- `structs/cull_pipeline.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md`
- `structs/gview.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md`
- `structs/render_state.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md`
- `structs/perspective_camera.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md`
- `specs/occlusion_culling.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md` + `specs/render_pipeline.md`
- `specs/transparency_sort.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md`
- `specs/rendering.md` — `specs/scene_graph.md` → `structs/scene_graph_nodes.md` + `specs/render_pipeline.md`
- `scenes/init.md` — `specs/client_architecture.md` → `specs/client_workflow.md`
- `scenes/scene_state_machine.md` — `specs/client_architecture.md` → `specs/client_workflow.md`
- `specs/client_runtime.md` — duplicate-cluster annotation updated (client_architecture.md absorbed)
- `specs/texture_upload.md` — `structs/ghtex.md` → `structs/texture_manager.md`
- `formats/cell_up.md` — `formats/cell_pre.md` → `formats/authoring_sidecars.md`
- `formats/authoring_sidecars.md` — internal cell_pre.md/cell_post.md cross-refs dropped (now internal)
- `specs/sound.md` — precursor note referencing sound_system.md dropped (absorbed)
- `specs/terrain-streaming.md` — precursor note referencing terrain_system.md dropped (absorbed)
- `specs/anticheat.md` — anticheat_core.md and xtrap_integration.md refs dropped (absorbed)
- `specs/crypto.md` — bignum_flint.md ref dropped (absorbed)
- `specs/skinning.md` — cal3d_runtime.md ref dropped (absorbed)
- `structs/scene_graph_nodes.md` — "corrects: specs/scene_graph.md" note updated to "absorbed: specs/scene_graph.md"
- `structs/texture_manager.md` — "corrects/extends: ghtex.md" note updated to "absorbed: structs/ghtex.md"
- `vfs/README.md` — all five vfs/ sub-file links replaced with canonical specs (formats/pak.md, specs/vfs_overview.md, specs/vfs_loader_dispatch.md, specs/asset_linkages.md)

---

### Deferred (not done this wave)

The following items are explicitly out of scope for this consolidation and require a dedicated pass:

- `specs/physics_collision.md` — SEVERE FIREWALL TAINT (French prose, raw addresses, decompiler autonames and calling-convention pseudo-C throughout, no verification banner; 253 lines, 20% usable). Unique quadtree design and geometric intersection algorithm (AABB/sphere/triangle) facts to be absorbed into `formats/sod.md` §runtime-query-algorithm or a new clean spec after full remediation. File retained on disk; content cannot enter any master until remediation passes the firewall self-scrub (see `README.md` firewall doctrine).
- `vfs/vfs_master_manual.md` — §2 carries raw virtual addresses and autonames; §1/§3/§4 are partially superseded but retain unique CVFSManager/DiskFile struct field detail. Full §2 remediation required before any content can be folded into `specs/vfs_overview.md`.
- `specs/gui_framework.md` — optional candidate only; 65% complete, partially overlapping with ui_system.md (C++ layout focus). A content diff against ui_system.md is required before any merge decision; not forced in this wave.

---

### Firewall

All content added to masters in this wave was processed through isolated scratchpad drafts and
Grep self-scrub (forbidden-token pattern per `README.md` firewall doctrine). No
tainted token was carried into a committed file. `journal.md` and `names.yaml` were not modified.
Verification banners on receiving masters were not re-dated to f61f66a9 for facts sourced from
tainted precursors that have not been re-confirmed in the current IDB; those facts carry a
static-hypothesis annotation where applicable.
