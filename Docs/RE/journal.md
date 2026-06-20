# Docs/RE — Provenance Journal

Append-only audit trail for the clean-room RE pipeline (dirty → spec → IDB). Orchestrator-owned.
Each entry records what was confirmed in the binary, what crossed the firewall into committed
specs, what was annotated into the IDB, and what remains owed — so every committed fact is traceable
to `doida.exe` without exposing dirty-room artifacts. (Prior entries were removed in a 2026-06-20
"documentation cleanup"; the journal is restored here at CYCLE 7. Earlier provenance lives in git
history + the per-spec `verification:` banners + `names.yaml`.)

---

## CYCLE 7 — GIGA DEEP STATIC-IDA CARTOGRAPHY & DOCS REFINEMENT (2026-06-20)

**Ground truth:** `doida.exe` IDB, SHA-256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`
(MD5 `a1437026…`); imagebase 0x400000; 25,792 funcs (5,056 named / 1,901 lib / 18,835 `sub_` at start).
**Mode:** STATIC ONLY (no debugger, no captures). **Scope:** Docs/RE-only (no C#/Godot touched).
**Method:** W (7 parallel research blocks) → P (7 promotion lanes) → L (IDB legibility) → R (review gate) → C (this).
Apparatus: Tier-1 → `re-orchestrator` (Tier-2, one per block/lane) → re-*-analyst / spec-author / ida-toolsmith (Tier-3).
Dirty staging: `_dirty/cycle7/{A..G,L_applied,C_names_sync}` (gitignored).

### Phase 1 (W) — static research, by block (each traced to IDA in `_dirty/cycle7/<block>/`)
- **A spine/runtime:** scene FSM is 8 cases 0..7, **value 8 = loop-exit terminal shutdown sentinel (NOT a login sub-state — supersedes "0..8")**; exactly 5 view platforms (apparent 6th = GScene root); **FPS cap = hardcoded 60.0f, `DISPLAY_FRAMERATE` config is DEAD** (2 writers/0 readers); allocator = plain CRT (no game pool); "Diamond" engine RTTI lifecycle (+0 vtable/+4 ref_count/+8 name); boot = ~57 fixed-order loaders (StatCurves_LoadAll #10); 3×3 terrain ring is **main-thread-blocking** (worker apparatus present but dormant).
- **B network values:** 3/100 code set {0,1-5,7,10,11,16,22,23,200-211,220-227,232} (**code 9 refuted**); 3/6+3/13 error bucket **duplicated** not relocated; structured panels fixed 4/48=236B, 4/56=1552B, 4/71=1092B; 2/142 storage `op = widget-action-id − 7`; 4/15 outcome {100/101/else}; LE wire, size u32 inclusive of 8B header, no bswap; keepalive 2/10000=20s exact, 2/112=event toggle, 1/2=idle; **periodic move emitter = 2/13 (not 2/112)**; endpoints port 10000 / XTrap 211.115.86.66:2424 / fallback 211.196.150.4 static; 1/4 credential layout (account+0x2A8 / password+0x2AC RSA / PIN singleton).
- **C gameplay (16 subsystems):** combat melee = **C2S 2/52** server-authoritative (no client damage math), cadence gate `(t−tlast)<100ms×skill[+1332]`; **4/99+4/100 = the Cube-Gamble minigame, NOT combat** (cubegamble.dds + daily bet limit + 2/141 submit); skills.scr columns mapped (AoE = circular sector); 0-HP/MP absent-by-design ((10/A)×B 45-float grid; level cap data-driven 300; no client XP→level formula); EquipTable 20×16 / BagTable 240-cap / upgrade cap 28 result codes; quest record **4960B (3720 refuted)**, **5/73 = SmsgQuestComplete**; 14-type NPC KIND→panel table (KIND@npc+0x22); **30-slot buff table @ actor+520, tick 4000ms, opcode 5/31 (supersedes 4/102)**; channel enum {0,1,2,3,4,6,7,9,13,15}; **party id-array @ local+204**; ZoneType @ region+0x28 {SAFE/PVP/CLOSED}, region grid 32×48B, cell stride 256u; death actor+1424/+1420, 5/10; respawn 2/3→4/28+5/28; ground items = actors (pickup 2/15→4/15).
- **D dark/missing:** crafting PRESENT (products.scr 212B; 2/151→2/153→4/79); mail = delivery-inbox 2/71+4/70 + carrier-pigeon 2/70 (**2/60 = couple/marriage, NOT mail**); **no pet/summon subsystem** (PetPanel slot 52 = couple window; creature_item.xdb = cosmetic prop); char-creation 1/6 52B + evolution 5/32 @ lvl 12/24 (**3/23 = SmsgCharStatusBytesByName 28B, not a 12B create-result**); skill-trees PRESENT (learn 2/145, **respec ABSENT**); FATE/fame/public-peace recovered; **mounts / auction / instanced-dungeons / housing / arena-ladder ABSENT** (positive evidence).
- **E asset formats:** **E6 settled the skinning math** (RH Hamilton, parent-left premul, no axis flip/remap, scale 1.0 → unblocked Block F); citems = **10 paragraphs** (the "6" was a UI loop); AnimCatalog = std::map (stride 136) not array; effect_id collision = **first-wins**; 52B particle subrecord typed (RGBA8 @+0x08); items.scr discriminator **+0xBA / flags +0xCD..+0xD0** (the +0x18-shift/+0xD2 reading **refuted**); helps.scr 1696B (helps_1.scr DEAD); VFS **"VFS001" NOT validated + NO FILETIME in the 24B index header** (entry_count @+0x0C); **mobinfo.mi confirmed DEAD**.
- **F skinning (DEBT #1 — CLOSED):** full LBS deform chain pinned (.skn 12B influences → drop <0.01 + renormalize Σ=1; inverse-bind quat+trans; parent-left RH XYZW; scale 1.0; base-relative bone resolve; port keeps top-4); engine is **Y-up, correct import = identity** ("avatar lies on X" = a port-side spurious −90° to remove); **verified reversal: idle motion = actormotion COLUMN 16, not 15** (col15/+0x40 is statically dead); no `g{id}.mot` sprintf (clips via motlist.txt, lookup by appearance key = .mot header id_b).
- **G render/UI/audio:** camera = C++ polymorphism (Gamble/Event modes + constants recovered); effects attach to **actor+0x240** (XEffect RTTI family); **"ambient ×3" REFUTED** (real = OPTION_BRIGHT/100·255; the ×3 is port-side); **cel outline REFUTED** (toon ramp real, no outline); GUComponent vtable 13 slots, +0x8D bool / +0xB8 int char-width, MainWindow 1464B; **exhaustive 178-slot UI roster** — **pet-window = slot 52 (110 refuted), target-frame = slot 35 MopGagePanel (177 refuted), slot 135 = UpgradeProcessPanel**; SOUND_KIND 0-11, trade-busy = 5/106 + cue 863500002; sound stride 48 (0x3000 = file size); minimap scale 0.125 + origin 66.5px.

### Phase 2 (P) — promotion (firewall crossing; one writer per file; all banners bumped to CYCLE 7 / 263bd994)
~50 committed specs corrected/deepened + **9 NEW**: `specs/buffs.md`, `crafting.md`, `mail.md`, `pets.md`,
`character_creation.md`, `skill_trees.md`, `pvp.md`; `structs/quest_record.md`; `formats/skn.md`.
opcodes.md reconciled (6 relabels incl. 2/60 couple, 4/99-4/100 Cube-Gamble, 4/70 delivery, 4/126 faction-side, 4/102 buff-wording).
**Cross-block conflicts arbitrated in the binary:** CONFLICT-D-1 = two distinct fields (actor+0x60 relation-type key / +0xAC pair-state); locked-target lives on the battle-controller singleton (not an actor field); 4/99-4/100 = Cube-Gamble (de-labelled off combat).

### Phase 3 (L) — IDB legibility (ida-toolsmith, unbridled)
89 renames + 31 neutral comments + 3 declared types (ActorBuffSlotRecord 12B, DiamondObjectHeader, SOUND_KIND enum);
`sub_` 18,835 → 18,791. Corrected the wrong idle-column comment (→ col16), the misnamed move-heartbeat builder,
the DISPLAY_FRAMERATE dead field, the dormant terrain worker, PetPanel=couple, 4/99-4/100=Cube-Gamble.

### Phase 4 (R) — review gate
Independent clean-room firewall audit + cross-spec consistency. Caught and FIXED: 3 raw addresses + decompiler
locals in `frontend_layout_tables.md`; 2 packet `spec:` fields dangling into `_dirty/` (+6 more affirmative
`_dirty/` prose citations); 81 `<!-- source: _dirty/ -->` HTML provenance comments scrubbed; Hex-Rays arg-names
made descriptive; and 3 consistency stale survivors (world_systems 4/99-4/100→Cube-Gamble; frontend_scenes
col15→col16 inversion; login_flow 3/23 12B→28B by-name). The `4-100` packet YAML reframed to Cube-Gamble
(layout preserved). Re-grep: zero raw addresses / dangling `_dirty/` / SmsgCombat-on-4·99·100 / col15-idle / 12B-3/23.

### Phase 5 (C) — provenance
This journal restored. `names.yaml` synced (3,352 → 3,393; +41 / 8 updated; SHA 263bd994), incl. the Cube-Gamble
handler corrections + move-emitter clarification + PetPanel note; 3 mid-function addresses deferred (non-blocking).

### Follow-ups owed (out of this Docs/RE-only cycle's scope)
1. **CLAUDE.md + C# code-alignment + IDB:** the **col15 → col16** idle-motion reversal contradicts CLAUDE.md's
   skinning chain and the campaign12 memory ("idle col15") — the prior "was col16" fix was BACKWARDS. The C#
   `actormotion` idle-column reads (~4 sites per campaign12 memory) must be re-flipped to col16, and CLAUDE.md
   corrected, when the port catches up (binary wins).
2. **HUD-II:** the campaign17 "target frame slot 177 not recovered" note is superseded — the real target plate is
   **MopGagePanel at slot 35** (placement rect remains a HUD-II follow-up). Pet window = slot 52 (not 110/230).
3. **Cosmetic:** the `packets/4-100_combat_attack_update.yaml` filename retains the legacy "combat" autoname
   (body corrected to Cube-Gamble) — optional `git mv` to `4-100_cube_gamble_reel_update.yaml`.
4. **names.yaml:** 3 mid-function addresses (0x41b59e/0x41b5e8/0x49c979) documented but not IDB-nameable without
   Make-Code/Make-Function; a few low-confidence proposed names deferred.
5. **Capture/debugger register (~25 genuinely runtime-only residuals):** server-authored magnitudes (damage/crit/
   XP/HP base), on-wire VALUE semantics inside the 4/48·4/56·4/71 opaque tails, the stat-grid float→named-stat
   mapping, effect per-field particle roles, the dormant terrain-worker spawn question — each re-flagged with a
   reason in the affected specs; require a future `?ext=dbg` / capture pass.
