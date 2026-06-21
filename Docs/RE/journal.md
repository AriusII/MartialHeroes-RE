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

---

## CYCLE — Front-End Fidelity 1:1, PHASE 1 (promotion-centric) — 2026-06-21

**Scope:** Docs/RE committed specs only (the porting/C# lane is separate and was NOT touched). Static IDA
only (no debugger). IDB SHA 263bd994 (doida.exe), preflight green. Phase 0.B had already
done the deep static recovery; this phase promoted the spec-delta and confirmed two binary facts. All
findings crossed the firewall as rewritten neutral prose — no addresses, no Hex-Rays artifacts in any
committed file (re-scanned clean). Dirty dossiers (gitignored) remain intact as provenance.

**STREAM 1 — spec-delta promotions (binary wins where it refines/contradicts):**
1. `specs/frontend_layout_tables.md §4` — REFINED the server-list per-render shuffle: the visible plate
   order is STABLE (page i shows raw records [2i]/[2i+1] in order); the Fisher-Yates permutation hits a
   PARALLEL server-id vector whose only effect is the `Lastserver` registry value. The old "on-screen rows
   shuffled / row≠record" note dropped. RESOLVED the standing "ServerId-vs-ServerId-1 off-by-one": it is TWO
   ARRAYS (raw record = connected id; shuffled vector = Lastserver id), not an off-by-one. CORRECTED the
   default-highlight compare key to NEW_SERVER_INDEX (not Lastserver). Banner residual list trimmed. Confirmed
   (unchanged): plate/pager/status/highlight rects, load thresholds 1200/800/500 + colours, msg maps
   (4029+StatusCode; 6001..6005), click actions 400/401, commit guard status==0 && load<2400.
2. `structs/gucomponent.md` — ADDED +0x89 `hover_edge` as a DISTINCT enter/leave edge latch (separate from
   steady hovered +0x88); slot-5 vtable row updated. Full offset table + 13-slot vtable confirmed.
3. `structs/guwindow.md` — REFINED the MI naming: the +0xBC base is the abstract `Diamond::EventHandler`
   realised by a concrete embedded `CmdHandler` subobject; secondary (MI base) vtable CORRECTED to **2 slots**
   (was "3"); derived-window overrides marked UNVERIFIED; +0xBC/+0xE8(GView)/+0x220 confirmed; the spurious
   third RTTI COL noted as a dynamic-cast artifact (not a real base).
4. `specs/ui_system.md §2` — CORRECTED the divergent vtable mapping (was a flat "16 slots", slot 2/3 generic
   accessor, slot 4 "rect setter", slot-15 SetShown alias) to the actual LAYERED GUComponent **13-slot**
   vtable (2 setPosition, 3 getPosition, 4 hitTest-vector, 5 hitTest, 7 draw, 10 getHitActionId, 11
   onMouseEnter, 12 onMouseLeave) + GUPanel slot 13, GUWindow slot 14; made gucomponent.md §6 authoritative.
   §4 now cross-references the new dispatch spec.
5. `specs/frontend_scenes.md` — char-select camera boom-Z clamp set to **26.0** everywhere (CONFLICT C3
   RESOLVED; the "22" ambiguity removed across §3.5.x).
6. `specs/skinning.md` — recorded the binary RE-confirmation that **DEBT#1 is CLOSED** end-to-end, with the
   char-select PREVIEW path as an independent second witness (id_b-verbatim skeleton key + col16 idle); the
   "mesh explodes / static-upright" note marked OBSOLETE (a port-side bug class, not a recovery gap).
   skn.md/animation.md/bindlist.md confirmed by the dossier with no contradiction (no edits needed).
7. `specs/ui_event_dispatch.md` (NEW) — promoted the GU hit-test / hover / press / dispatch STATE MACHINE
   (the load-bearing 1:1 input behaviour): press-inside-then-release-inside-same-widget => synthetic CLICK;
   ONE process-global click-capture; container walks children in REVERSE for pointer/click (topmost-painted
   wins), move (type 3) broadcasts hover to all; action_id (+0x10) -> panel active_child (+0xB4) -> window
   switch; ESC=close, login also reads Tab/Enter. Event-type catalogue (1..8) included.

**STREAM 2 — static-IDA confirmations + promotions:**
8. NETCODE socket setup -> `specs/net_contracts.md §1.1`: re-confirmed the client sets **NO TCP_NODELAY** on
   the game path (Nagle stays ON) — the only setsockopt anywhere is SO_RCVBUF on the game socket. CONFIRMS the
   live replica's `Socket.NoDelay=false`. Added blocking-mode detail: game socket non-blocking only during
   connect (FIONBIO+select ~2s), reverted to blocking; overlapped WSARecv steady-state; lobby socket plain
   blocking, no options.
9. GAP-4 (login ID textbox max-length) -> RESOLVED to **16** in both `specs/frontend_scenes.md §1.3` and
   `specs/frontend_layout_tables.md §2.6/§2.7`. The "6" (frontend_scenes) was the textbox CHARSET-FILTER mask
   misread as a length; the "20/19" (frontend_layout_tables) was the downstream TAB hand-off BUFFER, not the
   input cap. Per-keystroke input cap = 16 (ID) / 12 (password), enforced at textbox construction + the
   WM_CHAR/paste handlers. Contradiction removed.

**Provenance:** dirty sources under `Docs/RE/_dirty/{functions/server_list, functions/char_select,
structs/gu_framework, formats/skinning_verdict, functions/netcode_socket}/` (gitignored). Files touched:
frontend_layout_tables.md, frontend_scenes.md, net_contracts.md, skinning.md, ui_system.md, gucomponent.md,
guwindow.md, + NEW ui_event_dispatch.md. No `names.yaml` change required this cycle (no new canonical names).

---

## Phase 2b — protocol-semantics reconciliation (3 netcode divergences, static IDA)
**Date:** 2026-06-21  **Build/anchor:** doida.exe IDB SHA 263bd994  **Evidence:** [static-ida] (READONLY; no debugger)

The Phase 2 netcode audit surfaced three spec-vs-spec / spec-vs-code divergences. Each was reconciled against
the binary (static IDA only). The binary wins; the committed specs were corrected to agree.

1. **1/7 CmsgSelectCharacterSlot — mode semantics.** 1/7 (2-byte `[u8 slot][u8 mode]`) is a character-**SELECT**
   commit, NOT a delete carrier. One send-builder, two call sites in the select-window command handler: the
   "play / select this slot" confirm writes mode = 1 (select-and-play); the slot-lock / pre-play confirm writes
   mode = 0 (slot-lock). The earlier "mode = 1 = delete request, delete multiplexed onto 1/7" reading is
   **REFUTED** — there is **no major-1 char-delete opcode** on this build (major-1 C2S builder family = 1/0, 1/2,
   1/6, 1/7, 1/9, 1/13, 1/14); character removal is surfaced only via the inbound major-3 result ladder
   (3/7 SmsgCharManageResult subtype 2). Static-HIGH on builder/sites/literals; the runtime meaning of mode 1 vs
   0 stays capture-pending. Corrected: `net_contracts.md §2.2` (1/7 row + the 3/4-vs-3/7 CONFLICT retired) and
   `login_flow.md §3.6` (CYCLE 6b quoted block). `opcodes.md` (1/7) + `cmsg_char_select.yaml` were already correct.

2. **3/23 SmsgCharStatusBytesByName — size & role.** 3/23 reads exactly 28 bytes and is a BY-NAME status/level
   patch (keys off an in-body 17-byte CP949 name, string-matched across the up-to-5-slot roster, then writes a
   status byte + a level byte), NOT a 12-byte create-result. **No 12-byte create-result opcode exists** anywhere
   in the major-3 table. Character-create (1/6) is acked by 3/7 SmsgCharManageResult (8B, clears the awaiting-reply
   latch) PLUS a refreshed char list (3/1 SmsgCharacterList roster rebuild; 3/4 SmsgSceneEntityUpdate scene
   refresh). `net_contracts.md §2.2` (1/6 row) + `login_flow.md §5.4` + `opcodes.md` (3/23) were already correct;
   no spec edit needed. Flagged a layer-02 codegen follow-up (retire SmsgCharCreateResult).

3. **Lobby connect — inet_addr, NO DNS.** CONFIRMED: the lobby (port-10000 server-list) connect path resolves
   its host via **inet_addr** on a dotted-quad string (ip.txt -> list.dat CIPList -> hardcoded 211.196.150.4
   fallback) — no gethostbyname, no DNS, getaddrinfo absent (inet_ntoa only for diagnostic peer-IP display). The
   GAME-server path uses gethostbyname (DNS). Affirmed with a dated re-confirmation note in `login_flow.md §2.0`.
   Flagged a layer-02 follow-up (LobbyClient.ConnectBlocking currently uses Dns.GetHostAddresses).

**Provenance:** dirty sources under `Docs/RE/_dirty/{protocol/1-7_mode_semantics_recon, protocol/3-23_size_role_recon,
functions/lobby_connect_resolution_recon}.md` (gitignored). Committed files touched: `specs/net_contracts.md`,
`specs/login_flow.md`. No `names.yaml` change required (3/23 = SmsgCharStatusBytesByName already canonical; the
mode-enum candidates Mode.SelectAndPlay=1 / Mode.SlotLock=0 stay deferred until capture). Firewall: no addresses
or pseudo-C in any committed file; raw findings stayed in `_dirty/`.


---

## Asset-Fidelity Campaign (Phases 1+2) — UI 2D / Character / World 3D re-verification (static IDA)
**Date:** 2026-06-21  **Build/anchor:** doida.exe IDB SHA 263bd994  **Evidence:** [static-ida] (READONLY; no debugger — `dbg_start` never called)

Re-verified the documented OPENs across three asset domains and promoted the reconciled dirty findings into the
committed specs (re-pinning each touched banner to 263bd994 / 2026-06-21). Already-confirmed corpus (skinning/LBS
math, .skn/.bnd/.mot layouts, terrain texturing chain, GUComponent/GUWindow layouts + the 178 MainWindow slots)
was NOT re-derived.

**UI 2D**
1. **UI bucket render-state** — CORRECTED/REFINED: the per-bucket matrix's single "UI / HUD" row split into
   (a) in-game HUD panels (2D-ortho enter — alpha-blend disabled at bucket-enter, per-quad opt-in; depth test
   off; depth write off; cull CW; fill solid; ortho proj; no alpha-ref) and (b) front-end overlay (alpha ON,
   additive ONE/ONE; clears fog/dither/alpha-test; stage-0 select-arg1/diffuse). The 18-slot render-state cache
   mechanism re-confirmed (one cache slot per state type; blend setter forwards the bare integer; Z-write
   imperative). Present row = opaque copy (unchanged). Two DBG-PENDING: the per-quad translucent blend pair and
   the effective first-draw depth-write. Corrected `specs/rendering.md §4.1/§4.2` + Status.
2. **Font / glyph** — CAPTURE/DBG-PENDING cleared, settled statically: 15 font slots via the D3DX font API,
   common params (char-set 129 HANGUL, mip-levels 1, italic off, default precision/quality/pitch), faces
   DotumChe/Dotum/BatangChe; monospace per-slot layout (advance = slot char-width), NO kerning table, the only OS
   text measurement is the IME composition underline; password mask = fixed 6 px/char. CORRECTED the prior
   "every front-end label = slot 0" to "slot 0 is the unset default" (some controls call the slot setter).
   Corrected `specs/ui_system.md §0/§6`.
3. **Tooltip / auto-hide timer** — settled the GUComponent +0x95..+0xA0 block: +0x95 = auto-hide enable (opt-in,
   gates arm + tick); +0x98 = arm-START timestamp (CORRECTED from "expiry"); +0x9C = timeout (default 3000 ms,
   per-instance override e.g. 6000 ms / config*1000); +0xA0 = on-timeout callback (fires first, then the
   component hides, then disarms); +0x94/+0x96/+0x97 = padding (no flag block). Corrected `specs/ui_system.md`
   GUComponent timer rows + a mechanism note.

**Character**
4. **Equipment / item attachment** — load-bearing OPEN settled: the WEAPON is a rigid single-bone attach chosen
   by a numeric bone-id on the attach-host node (NO bone-name string exists in the binary; 88-byte bone stride;
   the concrete hand bone-id is the one DBG-PENDING value, default 0 statically). NON-weapon parts (head/face/body
   slots {2,3,4,6,11}) are skinned-deform under the shared skeleton root (draw list, deformed each frame) — NOT
   bone-attached, and there is NO head/face socket (the head is built like a body part). `Visual+100` = a single
   SCALAR scale (not a matrix). slot->mesh + dual-hand (off-hand flag 1 / main flag 2) unchanged. Corrected
   `specs/equipment_visuals.md §4/§5` + Status.
5. **Idle motion** — CONFIRMED-UNCHANGED: idle = actormotion column 16 (record field +0x44 = motion_ids_a[1]);
   column 15 / field +0x40 statically dead (zero readers). No spec change (carried).

**World 3D**
6. **GPU particle emitter (resource_id >= 10000)** — the 52-byte sub-record ROLES resolved from DBG-pending to
   CODE-CONFIRMED. Structural CORRECTION: a sub-record is a PER-PARTICLE spawn+Euler-integration descriptor, NOT
   a keyframe; num_frames = particle count (not a timeline); the live ring/loop count is num_frames, not the disk
   max_particles. Field roles: life_bonus/lifetime/spawn_delay/size_init (u16), RGBA8 colour, position xyz +
   size_rate (f32), four SIGNED i16 colour rates, velocity xyz + velocity_damp (f32); alpha scaled by a global
   brightness option (0.05 + 0.95*bright/100). Fixed ~67 ms sim step; camera-facing billboard, -0.5 half-extent,
   per-emitter alpha-vs-additive blend. Corrected `formats/effects.md §E.2.1/§E.2.2 (+ new §E.2.4)` and added
   `specs/effects.md §11.3`. One DBG-PENDING (does disk max_particles bound VB capacity when != num_frames).
7. **Glow .psh conflict (C5)** — RESOLVED (binary-won) in favour of `rendering.md §6.4`: the binary's shader set
   is exactly five files (cel VS, two cel PS, finaldx8 composite, power1dx8) — NO power2/power4 literal and NO
   power-N filename-construction format string. The display.lua DISPLAY_POWERSHADER key is a FILENAME STRING
   copied verbatim into the editable glow-shader slot (default power1dx8.psh); the stock client binds power1dx8
   for glow and finaldx8 for the composite; VFS-first by name. The "DISPLAY_POWER=2 -> power2dx8.psh" reading is
   REFUTED. Whether a VFS-supplied power2dx8.psh exists is now DATA-PENDING (not IDA-pending). Corrected
   `specs/rendering.md §6.3/§6.4/§6.5/§6.6` + banner C5 + Status.
8. **.fx1-.fx7 terrain overlay internals** — re-walked the seven on-disk file decoders: universal group-array
   model (u32 group_count + per-group {header, vc x VF, ic x u16}); per-channel header width / vc-ic offsets /
   vertex stride tabulated (fx1=VF_36/20B, fx2=VF_44/20B, fx3=VF_36/44B, fx4=VF_44/48B, fx5=VF_36/48B,
   fx6=VF_32/36B, fx7=VF_32/48B). CORRECTED: group header +0x00 = 1-based texture_index into the per-channel
   register (was mis-read as a "constant 15/0x0F/5" header word — REFUTED); fx4 is the universal model, NOT a
   distinct "flat tile array"; fx3 carries a signed elevation/extent dword before vc. Corrected
   `formats/terrain_layers.md §1.1a (+ new §1.4a/§1.4b)` + banner. DBG-PENDING: index topology + exact UV
   encoding + the unread inter-header dwords.
9. **Water + skybox absences** — CONFIRMED-UNCHANGED: no water renderer / no water asset-loader (OPTION_WATER is
   a stored quality-toggle; map_option 0x00/0x04 are dungeon flag + sight-clamp, not water); .box skybox absent
   (the .box by-name open path is wired but gated by the map_option SKYBOX flag, reset to 0 before each area
   load; the sun/moon orbiting billboards are a separate system). Re-pinned `specs/environment.md §4` +
   `formats/sky.md Section A`.
10. **Ambient brightness (DEBT#3)** — CONFIRMED-UNCHANGED: ambient floor = floor((OPTION_BRIGHT/100)*255) over
    the (0,0,0) base, default 100 / clamp [1,100]; K_ambient = 0.0 with zero writers (per-keyframe ambient term
    inert); device-ambient render-state token 139; quality-mode sky LIGHT-RATIO {mode1 0.25 / mode2 0.7 / else
    2.0}. Re-pinned `specs/environment.md`.

**Provenance:** dirty sources under `Docs/RE/_dirty/assetfidelity/{ui,character,world}/*.md` (gitignored).
Committed files touched: `specs/ui_system.md`, `specs/rendering.md`, `specs/equipment_visuals.md`,
`formats/effects.md`, `specs/effects.md`, `formats/terrain_layers.md`, `specs/environment.md`, `formats/sky.md`.
No `names.yaml` change applied this campaign (proposed canonical names left in the dirty notes for a later
`ida-toolsmith` annotation pass). Firewall: zero addresses / pseudo-C / Hex-Rays autonames in any committed file
(self-scrubbed); all raw findings stayed in `_dirty/`. Recovery and promotion ran as separate sub-waves.

---

## CYCLE 8 — GIGA STATIC-IDA "TRUTH PASS" (Window/scene FSM · GUI framework · HUD · asset/VFS assembly · netcode) (2026-06-21)

**Ground truth:** `doida.exe` IDB, SHA-256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`
(MD5 `a1437026…`); imagebase 0x400000; 25,792 funcs. **Mode:** STATIC ONLY (no debugger, no captures —
maintainer chose static-only this cycle). **Scope:** `Docs/RE/**` only (no C#/Godot touched; that is Phase 2).
**Method:** preflight → 5 parallel READONLY recovery lanes (1A scene FSM · 1B GUI struct + 1B GUI behaviour ·
1C asset/VFS assembly · 1D netcode) → reconcile → promotion sub-wave (separate). Apparatus: Tier-1 →
`re-orchestrator` (Tier-2) → re-*-analyst (Tier-3). Dirty staging: `_dirty/{scenes,gui_struct,gui_behavior,
asset_assembly,net}_cycle8/` (gitignored). **This was a CONFIRM + DEEPEN + CLOSE pass over an already-mature
corpus, not a from-scratch reverse.**

### Phase 1 (recovery) — every load-bearing claim re-confirmed against the binary; ZERO structural conflicts
- **1A scene/Window FSM:** the 8-case dispatch (bounds `state<=7`, value 8 = terminal exit sub-state, 3-int
  record `[state,sub,reason]`), every per-state Window construction + ctor name + object size + pre-arm (incl.
  the `5→4` world-exit pre-arm so logout returns to Select not Login), the `[OPENNING]/SKIP` gate (skip→4 else 3),
  the keepalive enable on world-enter, and Error reading the reason field + closing the net client — all CODE-CONFIRMED.
- **1B GUI struct:** the layered vtable chain (GUComponent 13 → GUPanel 14 → GUWindow 15 primary + 2-slot secondary)
  and the headline sizes (leaf 0xF0, LoginWindow 0x558, MainWindow 0x5B8) re-confirmed; the full **leaf-widget
  offset family** deepened (GUButton 3-state src-rects; GUCheckBox checked +0xFC; GULabel caption/aux + font +0xE4;
  GUTextbox mode-style +0xA4 bit 0x80 = password / maxLength +0xD0 / font +0xDC; GUList selected-index +0xB8;
  GUScroll up/down/thumb 24-byte sub-blocks; GUScrollEx derives GUPanel; GUCanvas3D 0xB0 drag-only).
- **1B GUI behaviour:** the event-dispatch FSM (8 types, reverse-child topmost-first, single process-global
  click-capture, synthetic CLICK, action-id→active-child→window switch, ESC=27/Tab=9/Enter=10), the single
  shared `ID3DXSprite` render path (one `D3DXCreateSprite` call site; ARGB tint; forced-alpha +0x0F; auto-hide
  timer block), the 15-slot HANGUL (charset 129) font table, and the 178-slot HUD roster (base +0x238; slots 35
  MopGagePanel / 52 PetPanel / 110 Gamble / 135 UpgradeProcessPanel / 178 MainHandler lazy-filled) — all CODE-CONFIRMED.
- **1C asset/VFS assembly:** the VFS mount (data.inf `FILE_FLAG_RANDOM_ACCESS`, header `entry_count @+0x0C`,
  magic NOT validated / no FILETIME, 144-byte/entry TOC, retained data.vfs handle, **raw `ReadFile` — no codec**,
  not memory-mapped), the find-and-read chokepoint (lowercased path → binary-searched TOC), the ~57-step fixed
  boot-loader order, and the consolidated mount→boot-corpus→six-subsystem-chain assembly graph — all CODE-CONFIRMED;
  47-format index audited complete (no orphan format, no missing loader).
- **1D netcode:** the 8-byte frame header (`u32 size incl header` LE / `u16 major` / `u16 minor`, no bswap; inbound
  reads a u16 length, send writes u32), the asymmetric transform (C2S timestamp→keyless byte cipher→LZ4; S2C
  LZ4-decompress-only into the fixed 11680-byte buffer; the byte cipher has exactly ONE xref = the outbound send
  gate; size==8 bypass), the 3-phase handshake (0/0→1/4, 1/6→3/1, 1/9→3/5), keepalive 2/10000=20s, move 2/13 (16B),
  buff 5/31 (56B 30-slot), combat 2/52 server-authoritative, the major-4/major-5 154-slot tables (98 Response / 65
  Push installed), and SO_RCVBUF-only / Nagle ON — all CODE-CONFIRMED.

### Phase 2 (promotion) — firewall crossing (rewrite-not-copy; one writer per file; banners re-pinned 263bd994/CYCLE 8)
**Five static GAPs CLOSED (binary-won, all upgrades from debugger-pending → CODE-CONFIRMED):**
1. **Error sub-state 1-vs-3** (`scenes/scene_state_machine.md §9`, `scenes/login.md`): NOT an Init(0) failure —
   it is in the **Login(1)** case: main-window-creation failure → Error sub 1; graphics-device init failure →
   Error sub 3. Re-attributed and promoted to CODE-CONFIRMED.
2. **GUWindow secondary 2-slot per-derived override** (`structs/guwindow.md §3.2`): all five derived windows
   override BOTH secondary slots — slot 0 = base/dtor entry, slot 1 = the window's command/event sink (action-id
   routing). The prior `[UNVERIFIED]` is closed.
3. **GUCanvas3D render-target wiring** (`specs/ui_system.md §1.5a / open-item 9`): the canvas carries **no**
   render-target/viewport field — its draw is an empty stub and it holds only a drag-orbit delta; the live preview
   renders via the owner window's embedded GView (window +0xE8). The "untraced" conflict is closed.
4. **+0x8D semantic** (`structs/gucomponent.md §8 / status / conflicts`): resolved to `remove_mark` — the deferred
   child-removal flag consumed by GUPanel slot 13 (==1 ⇒ remove, cleared on survivors); `setVisible`'s co-write is
   an incidental mirror of +0x8C that the panel-build path zeroes. The "enable/clip vs pending-removal" ambiguity
   resolves to pending-removal; CODE-CONFIRMED.
5. **World-entry state-2 replay vs cached short-circuit** (`specs/resource_pipeline.md §2.6 / §8 item 5`): REPLAY —
   the full ~57-loader corpus re-runs on world entry (no cache gate; idempotent rebuilders); `msg.xdb` is the only
   state-1-only non-reloaded table. Only the second pass's wall-clock timing remains runtime-only.

**Refinements promoted:** per-leaf-class font-slot offsets (Button +0xE8 / Label +0xE4 / Textbox +0xDC — not a
universal base field) added to `structs/gucomponent.md` + `specs/ui_system.md`; `GUShortLabel` recorded ABSENT as a
distinct RTTI class (a GULabel/GULabels variant).

**Phase 2 (C# alignment) action items surfaced (NOT applied this cycle — Docs/RE-only):** (a) re-affirm idle motion =
`actormotion` **col16** (the owed C# + CLAUDE.md re-flip); (b) **retire `SmsgCharCreateResult`** — no 12-byte
create-result opcode exists; 1/6 create is acked by `3/7` + `3/1` + `3/4` (re-confirmed); (c) **lobby/server-list
connect uses `inet_addr` dotted-quad** (port 10000, no DNS) while the game socket uses `gethostbyname` (re-confirmed);
(d) slot-35 placement rect VALUE stays deferred (D6).

**Deferred register (D1–D13) re-affirmed unchanged** (do NOT chase statically): server-authored damage/crit/XP/HP +
the opaque VALUE tails of 4/48·4/56·4/71, stat-grid f32→stat mapping, effect particle VALUE roles, PIN keypad seed,
slot-35 rect, button caption font-slot byte, `[OPENNING]/SKIP` literal INI filename, fx UV encoding, UI blend pair,
weapon hand bone-id, dormant terrain-worker spawn, 1/7 mode meaning; plus the concrete RSA `n`/`e` and the L1/L2 split
(read live off the 0/0 wire).

**Committed files touched:** `scenes/scene_state_machine.md`, `structs/gucomponent.md`, `structs/guwindow.md`,
`specs/ui_system.md`, `specs/ui_event_dispatch.md`, `specs/ui_hud_layout.md`, `specs/resource_pipeline.md`,
`specs/vfs_overview.md`, `specs/asset_linkages.md`, `specs/assembly_graph.md`, `specs/net_contracts.md`,
`specs/network_dispatch.md`, `specs/login_flow.md`, `specs/crypto.md`, `specs/handlers.md`, plus `names.yaml`
(CYCLE 8 re-verification note + rename-candidate flags; NO renames applied this READONLY pass).
**Firewall:** zero addresses / pseudo-C / Hex-Rays autonames in any committed file (self-scrubbed); all raw findings
(incl. anchor addresses) stayed in `_dirty/*_cycle8/`. Recovery and promotion ran as separate sub-waves. No IDB
mutation this cycle (a future `ida-toolsmith` annotation pass owns the rename/comment/type apply).


---

## CYCLE 8 — Phase 2.1: tiny packet-spec reconciliation (3/23 name offset · 3/6 body size) — 2026-06-21

**Ground truth:** `doida.exe` IDB SHA-256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`;
imagebase 0x400000. **Mode:** STATIC ONLY (no debugger, no captures). **Scope:** Docs/RE-only (no C#/Godot touched).
**Apparatus:** Tier-1 → `re-orchestrator` (Tier-2) → `re-protocol-analyst` (recovery) + orchestrator-applied promotion.
Dirty staging: `_dirty/protocol/cycle8_3-23_3-6_reconcile.md` (gitignored).

Two packet-spec consistency conflicts surfaced during the C# alignment were settled against the binary (the binary wins):

1. **3/23 `SmsgCharStatusBytesByName` — name-key body offset.** The matched CP949 character-name key is read at
   **body offset 0x08** (post-8-byte-header payload cursor), NOT 0x00 and NOT 0x02 (both prior readings refuted).
   The leading 8-byte block @0x00 is copied but unused by the slot matcher/writer; status byte @0x19 (+25), level
   byte @0x1A (+26), pad @0x1B; total body = 28 bytes (unchanged). Roster scratch: ~5 slots, slot stride 880 bytes,
   slot name field at slot+0x3C. The packet YAML `fields:` block was already correct (CharacterName @0x08); the stale
   `@ 0x02` line in its VERIFICATION header was corrected, and `login_flow.md §5.4`'s table (which placed the name at
   0x00) was corrected to the 0x08 layout. CONTROL-FLOW CONFIRMED; field VALUE semantics capture-UNVERIFIED.

2. **3/6 `SmsgRenameCharResult` — true body size.** The handler reads a single fixed **12-byte** block (NOT 19); the
   earlier "19-byte / embedded up-to-18-byte CP949 name @+1" reading is refuted — the success path performs NO name
   string-copy. Layout (body-relative): Result u8 @0 (0=fail/1=success), ErrorCode u8 @1 (failure-only, 0xC8..0xD4 →
   UI message buckets), pad @0x02 (2B), then two IEEE-float placement values @0x04 and @0x08 forwarded to the
   char-select slot-record writer (the YAML's prior `SlotIndex u32` / `Unk u32` are corrected to two `f32` placement
   values). `opcodes.md` already carried 3/6 = 12 and 3/23 = 28B/name-keyed; both rows were refined with the now-specced
   layouts. CONTROL-FLOW CONFIRMED; field VALUE semantics capture-UNVERIFIED.

**Committed files touched:** `packets/3-23_char_select_status_update.yaml`, `packets/3-6_rename_char_result.yaml`,
`specs/login_flow.md` (§5.4, §5.7), `opcodes.md` (header changelog + the 3/6 and 3/23 catalog rows). Banners re-pinned
to 263bd994 / CYCLE 8 / 2026-06-21. **`names.yaml`:** no canonical names changed (both opcode names unchanged) — not edited.
**Firewall:** zero addresses / pseudo-C / Hex-Rays autonames in any committed file (self-scrubbed); anchor addresses
stayed in `_dirty/`. Recovery and promotion ran as separate sub-waves.

**C# follow-up surfaced (NOT applied — Docs/RE-only):** layer-02 `SmsgCharStatusBytesByName` (3/23) struct already
matches the 28-byte / name@0x08 `fields:` layout — confirmed correct. Layer-02 `SmsgRenameCharResult` (3/6) struct is
12 bytes — size correct; if it declares the two trailing dwords as integer SlotIndex/Unk, the network-engineer should
retype them to two `float` placement values to match the binary (offsets 0x04/0x08), no size change.

---

## CYCLE 9 - Phase 1: static-IDA front-end confirm (Opening -> Login -> PIN -> Server list -> Channel -> Load -> CharSelect) - 2026-06-21

**Ground truth:** `doida.exe` IDB SHA-256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`;
imagebase 0x400000. **Mode:** STATIC ONLY (no debugger, no captures). **Scope:** Docs/RE-only (no C#/Godot touched).
**Apparatus:** RE-orchestrator (dirty->spec firewall owner) -> 2x `re-protocol-analyst` + 3x `re-function-analyst` (READONLY
recovery, massively parallel) -> orchestrator-applied promotion (single controlled crossing).
Dirty staging (gitignored): `_dirty/protocol/cycle9_serverrecord_signedness.md`, `_dirty/protocol/cycle9_channel_endpoint.md`,
`_dirty/recon/cycle9_lobby_hostresolve.md`, `_dirty/functions/cycle9_login_ladder_spotcheck.md`,
`_dirty/functions/cycle9_loading_charselect_spotcheck.md`.

This was a CONFIRM + sharpen pass over the already-mature front-end corpus, not a re-derivation. The overwhelming result is
CONFIRMATION; one binary-won CONFLICT was corrected.

**Binary-won CONFLICT (corrected - the binary wins):**

1. **Server-list 8-byte record field SIGNEDNESS.** All four fields are **signed `i16`**, not mixed sign. The client reads
   each field with a sign-extending load; the decisive numeric ordering branch - the plate-pick commit gate `load < 2400`
   (0x960) - is a **signed** strict-less-than, and the painter's 1200/800/500 load-colour thresholds are signed branches.
   `status_code (+2)` and the `+6` flag are loaded signed but tested only by equality / small-enum / `!= 0`, so sign is
   behaviorally inert there; the `status==3` HH:MM minute use is a signed load + signed division by 10. The `server_id`
   **1..40** range check is emitted as an unsigned-style range idiom (`(unsigned)(server_id - 1) > 39`) - the ordinary
   compiler form for a bounded range test - which an earlier reading mistook for the field being `u16`. **`login_flow.md`
   2.1 (record table), 6 (catalog row), and 7 (constants) previously listed `server_id (+0)` as `u16`; corrected to
   `i16`.** This brings `login_flow.md` into agreement with `packets/lobby.yaml` Record Shape A and
   `frontend_layout_tables.md 4.1`, which already record all four fields as `i16`. The wrapper **record_count** (wrapper +4,
   the reused game-frame "major" slot) is read with a zero-extending load but gated by a signed `> 0` test, sign-extended for
   the `8*count` allocation, and stored in a **signed** 32-bit slot that takes a `-1` sentinel on connect-failure. CONTROL-FLOW
   CONFIRMED; on-wire byte VALUES capture-UNVERIFIED.

**Sharpened (static-CONFIRMED this pass, previously carrying NEEDS-CAPTURE caveats):**

2. **Channel-endpoint parse shape (`lobby.yaml` Record Shape B / `login_flow.md 2.2`).** The host/port split is on a
   **SINGLE SPACE (0x20)**, port tail parsed by `atol` (NOT colon, NOT NUL); the **30 (0x1E)** byte field is a fixed **COPY CAP**
   over a zero-filled destination with **no `len>=30` guard** (a shorter payload, e.g. 23 bytes, is tolerated as a
   NUL-terminated C string - requiring `>=30` is a defect); the reply is a **SINGLE endpoint** (one fixed 30-byte token, no
   count field, no loop) - decisively single by direct contrast with the sibling server-roster thread, which DOES read a count
   and loops an 8-byte-stride array. The prior "single-vs-array / delimiter" NEEDS-CAPTURE caveats were downgraded to
   static-CONFIRMED; `capture_verified` stays false (no `.pcapng`; only the host token FORM - DNS name vs dotted quad - and the
   literal byte values remain capture-pending).

**Re-confirmed unchanged (no churn - the specs were already correct):**

- **Lobby host resolution** (`login_flow.md 2.0/3.0`, `lobby.yaml`): 3-tier `ip.txt` (<=19 chars) -> `list.dat` CIPList
  (length invariant `768*count+4`, name match key at record +0, host at +256, selector = registry `servername`) -> fallback
  `211.196.150.4` (referenced from **exactly one** function); lobby = `inet_addr` (no DNS; `getaddrinfo`/`gethostbyaddr` not
  even imported), game = `gethostbyname`; base port 10000, channel port `10000 + server_id` (`htons`). `inet_ntoa` is
  diagnostic-only.
- **Login ladder + PIN + curtain + game.ver** (`frontend_layout_tables.md 2/3`, `login_flow.md 1/4`): 29->31 PIN raise
  (ID >= 4 else msg 4025; PW != 0 else msg 4026); 32->33 fetch; 37->38 commit guard `status==0 && load<2400` -> persist
  `Lastserver` -> channel fetch; 40 hand-off TAB string `account \t password \t PIN \t "host port"` + login sub-opcode `0x2B`
  + 30 s connect timeout. PIN keypad `srand(time())` (whole-second CRT wall-clock - explicitly NOT GetTickCount/timeGetTime/
  QPC/GetSystemTimeAsFileTime) ascending uniform shuffle of digits 0..9, one digit per cell, 4-digit cap, `*` mask, re-scramble
  on open/Reset/OK/Cancel. Curtain offset +5/tick, top Y = -offset, bottom Y = offset+326, stop at offset>222, intro SFX
  861010105 (category 2). game.ver field-index-5 u32 equality gate (mismatch -> msg 2204 + quit; runs only when VFS mounted;
  gated by the OK/Login action 103).
- **Loading + char-select** (`frontend_layout_tables.md 5`, `login_flow.md 3.2`): Loading = two ortho-projected textured
  quads (immediate-mode, not a widget tree), `rand()%3` background `{loading.dds|loading06.dds|loading08.dds}`, looped 2D BGM
  cue 920100100 (category 0), loading-active flag cleared by the background corpus loader after a 500 ms grace (the per-frame
  ~100 ms loop is the "replay"), Opening-skip gate `GetPrivateProfileInt("OPENNING","SKIP",0, option.ini)`. Char-select forced
  by inbound `3/1` (zeroes the five-slot scratch, populates per the slot mask, unconditionally forces GameState -> char-select)
  with a **hard 5-slot loop** (`for i = 4..0`).

**Committed files touched:** `specs/login_flow.md` (2.1 record table + new signedness note, 6 catalog row, 7 constants,
verification banner), `packets/lobby.yaml` (Record Shape B parse-shape note, verification banner). Banners re-pinned to
263bd994 / CYCLE 9 Phase 1 / 2026-06-21.

**`names.yaml`:** no canonical name changed - not edited. **PROPOSED name (flagged, NOT applied):** the 30-byte
channel-endpoint token field (login-object) -> suggested `channelEndpointToken[30]`; a future `ida-toolsmith` annotation pass
owns any IDB rename + `names.yaml` sync.

**Deferred GAPs (runtime-only / non-code-literal - NOT chased, already reflected in `login_flow.md 9`):**
the `3/4` in-place refill handler (`form_byte0 == 1`) interior (the `3/1` forced path is fully confirmed); the char-select
map000 14:30 time-of-day freeze (script/config-driven - the literal `"14:30"` has no code xref); the server-list fetch port
10000 lives in the worker-proc (base port 10000 itself is confirmed elsewhere); all on-wire byte VALUES + the full
`status_code` enum + the `open_time` packing remain capture-pending (`capture_verified: false`).

**Firewall:** zero addresses / pseudo-C / Hex-Rays autonames in any committed file (self-scrubbed); all anchor addresses stayed
in `_dirty/`. Recovery (5 READONLY analysts) and promotion (orchestrator rewrite) ran as separate sub-waves.

**C# Phase-2 action items surfaced (NOT applied - Docs/RE-only):**
(1) the layer-02 server-record wire struct + the layer-04 DTO must both type all four fields as **signed 16-bit** (`short`),
removing the `short`/`ushort` mix - `ServerId`, `StatusCode`, `Load`, `OpenTime` are all `short`; selectable iff
`StatusCode == 0 && Load < 2400` (signed compare); the record_count slot is a signed int holding `-1` on fetch-failure.
(2) the channel-endpoint parser must copy up-to-30 bytes into a zero-filled `char[30]`, treat it as a NUL-terminated ASCII
string (stop at first NUL), and split on a **single space** (host = before, port = `atol`/decimal of the tail) - requiring
`>=30`, or splitting on `:`/NUL, is a bug; treat the reply as a single endpoint (do not loop for a trailing array).

## CYCLE 9 - Phase 3.1: tiny RE - settle the 3/1 SmsgCharacterList class/variant descriptor offset (static IDA) - 2026-06-21

**Trigger.** The live char-select wire decode read `internal_class == 0` at SpawnDescriptor +0x34 for
every real roster slot (chars of class {1,2,3,4}), so the model formula `5*(class + 4*variant) - 24`
yielded a bad key and every preview actor was skipped (empty platform). The 3/1 record was banner-flagged
`capture_verified: false`, so +0x34 was under suspicion.

**Recovery (1 READONLY analyst, static-only, IDB SHA 263bd994).** The binary genuinely reads the
model-class `class` argument from descriptor **+0x34** (u16, sign-extended) and the `variant` argument
from descriptor **+0x2C** (u8, zero-extended). Confirmed at **four independent read sites**: the single
shared spawn factory; the char-select preview-lineup spawn; the zoomed single-preview build; and the
character-creation synthesizers (which `switch` on the {1,2,3,4} class value they write into +0x34). The
in-world spawn path (5/3 CharSpawn, 5/1 ActorSpawnExtended, area snapshot, respawn, game-state-tick)
feeds the **same** two offsets to the **same** factory - no per-caller remapping. So the spec offsets
were already CORRECT; this pass raises them to STATIC-CONFIRMED HIGH.

**The +0x34==0 puzzle - resolved as a port-side bug, not a spec error.** The `level` u16 at +0x3A in the
SAME descriptor copy decodes the real character levels (L1/L3/L12), proving the descriptor block is
intact and correctly based. A correct +0x3A next to a zero +0x34 in the same 0x370 block means the
port's decoder read +0x34 from a **misaligned descriptor base**, not that the field moved. Static settles
WHICH offset the client reads (conclusively +0x34/+0x2C); the live wire VALUE byte-proof stays
debugger-pending.

**Reconciliation of the near-by fields (was CAMPAIGN-16 open).** Four distinct roles near +0x2C..+0x36
are now disambiguated, not conflated: variant/occupied byte (+0x2C, u8), preview occupied gate (+0x2E,
u16), model class input (+0x34, u16), PC-spawn name-assign gate (+0x36, u16).

**Committed files touched:** `packets/3-1_character_list.yaml` (banner + descriptor field notes raised
to STATIC-CONFIRMED HIGH + alignment-bug port note), `structs/spawn_descriptor.md` (banner + the +0x2C /
+0x34 rows + the CAMPAIGN-16 +0x2E/+0x36 reconciliation), `specs/frontend_scenes.md` (3.2 preview model
inputs confirmation + port action item). Banners re-pinned to 263bd994 / CYCLE 9 Phase 3.1 / 2026-06-21.
`capture_verified: false` kept (offset is static-confirmed; live VALUE byte-proof debugger-pending).

**`names.yaml`:** no canonical name changed - not edited.

**C# action item surfaced (NOT applied - Docs/RE-only):** the Godot/C# SpawnDescriptor decoder for the
3/1 per-slot record must base each per-slot descriptor at the FIRST byte of that slot's 880-byte (0x370)
descriptor block, then read `internal_class` as a u16 at descriptor +0x34 and `appearance_variant` as a
u8 at descriptor +0x2C, so `class in {1,2,3,4}` and `ModelClassId = 5*(class + 4*variant) - 24` resolves.
The current decoder's class==0 read is a descriptor-base alignment bug (the +0x3A level field already
decodes correctly), not a wrong offset - do NOT relocate the field.

**Firewall:** zero addresses / pseudo-C / Hex-Rays autonames in any committed file (self-scrubbed); all
anchor addresses stayed in `_dirty/protocol/3-1_class_variant_offset.md`. Recovery (1 READONLY analyst)
and promotion (orchestrator rewrite via re-promote) ran as separate sub-waves.
