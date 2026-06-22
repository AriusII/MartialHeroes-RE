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

---

## 2026-06-22 — Login + CharSelect from-scratch rebuild (RE spec corrections + C# 02/04 + Godot 05)

- binary: doida.exe @ 263bd994
- tool: IDA Pro 9.3 via MCP (mcp__ida__*) — static analysis only; no debugger session this entry
- analyzed: LoginScene widget builder (msg.xdb caption loop, notice-panel structure); PIN keypad
  control-button element walker (Reset/OK/Cancel tag roles, source rects); UI render-state bucket
  entry path (front-end overlay vs in-game HUD blend modes); char-select preview body resolver
  (AnimCatalog body-key formula, per-class InternalClass → IdB → body key64); SpawnDescriptor
  `SkinClassId` vs `model_class_id` disambiguation at the skeleton/idle lookup sites
- specs produced/updated:
  - `formats/msg_xdb.md` — CORRECTED: IDs 4001–4022 are the static notice/agreement text column
    labels, not EULA / Terms-of-Service body text; no scroll/accept gate exists anywhere in the login
    scene builder. Prior "EULA overlay" reading refuted CODE-CONFIRMED.
  - `specs/client_workflow.md` — cross-reference updated to agree with the msg_xdb correction; EULA
    references removed.
  - `specs/frontend_layout_tables.md §3` — PIN keypad control-button rects and tag roles confirmed
    CODE-CONFIRMED: Reset = tag 11, OK = tag 12, Cancel = tag 13; source rect bands on `password.dds`
    recorded (panel-relative coordinates carried).
  - `specs/rendering.md §4.1/§4.2` — UI blend-state reconciliation, binary-won: front-end overlay
    bucket uses a global additive ONE/ONE blend (not per-quad opt-in); in-game HUD bucket leaves
    alpha-blend disabled at bucket-enter with per-quad opt-in. Two rows now explicitly split.
  - `specs/frontend_scenes.md §3.7.5` — per-class starter-body table rewritten from the
    binary-confirmed resolver math: body selected via AnimCatalog lookup keyed by
    `(slot=3, IdB)` where `IdB = 5*(InternalClass + 4*variant) - 24`; starter variants
    `{1, 2, 1, 1}` for classes `{1, 2, 3, 4}`; resulting IdB values `{1, 26, 11, 16}`
    CODE-CONFIRMED. Concrete `g{skinId}.skn` per class remains SAMPLE-UNVERIFIED. Prior VFS-only
    observation (wrong class-tag mapping, wrong .skn path structure) refuted.
- notes: This entry covers RE-side spec corrections that unblocked the C# 02/04 and Godot 05
  from-scratch rebuild of the Login and CharSelect scenes. The msg.xdb mislabel (EULA) was a
  long-standing doc error: the binary's login scene builder constructs no terms/agreement panel and
  gates on no accept action — the 4001–4022 caption band is a purely decorative stacked notice
  column. The UI blend-state reconciliation resolved a conflict between the in-game HUD (per-quad
  opt-in) and the front-end overlay (global ONE/ONE additive) that had been conflated in the prior
  rendering.md row. The starter-body table rewrite fixed a VFS-observation-driven error: the binary
  resolver ignores the part-id mantissa and keys exclusively on `(slot=3, IdB)` via the AnimCatalog,
  not on a direct VFS class-tag scan. The slot-2 skeleton/idle bug (Dosa avatar falling back to
  static bind pose) was a port-side misuse of the appearance key as a `.bnd` filename number —
  corrected in `SlotAppearanceResolver` to use `SkinClassId = InternalClass ∈ {1,2,3,4}` (the
  proven path; `g1..g4.bnd` are the only skeletons that exist). All findings crossed the firewall as
  neutral prose; no addresses or pseudo-C in any committed file.
- build result: `dotnet build MartialHeroes.slnx` 0 errors / 0 warnings; Godot headless exit 0;
  loginPass + charSelectPass verified live (windowed). Remaining residuals are data gaps (equip .skn
  + effect.cache absent from local VFS) and the §3.3.7 multi-surface deform overlay, both recorded in
  ROADMAP CYCLE 10.

## CYCLE 11 — « CharSelect → World — Evidence-Driven Reforge » (static IDA, 263bd994) — 2026-06-22

- date: 2026-06-22
- analyst: AriusII (Tier-1 main session orchestrating re-orchestrator / csharp-port-orchestrator / godot-orchestrator)
- binary: `doida.exe` @ IDB `263bd994` (SHA `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`)
- tool: IDA Pro 9.3 via MCP — **STATIC ONLY** (no `?ext=dbg`, no captures; user decision). NO tests this cycle.
- scope: evidence-driven reforge of the two largest scenes — CharSelect (Block A) then World/InGame (Block B) —
  in the 5-stage rhythm DeepAnalyse → Counter-DeepAnalyse → C# rebuild 00–04 → VERIFY → Godot 05 rewire.
  Existing code treated as candidates only; unjustified code deleted. Branch `cycle9-charselect-world` off `master`.

- **Scene-state numbering settled in the binary (headline).** The app-entry `while(1) switch(game-state value)`
  pre-writes the NEXT value on case ENTRY. **CharSelect = switch case-index 4 → writes value 5**; **World/InGame =
  switch case-index 5 → writes value 4** (the no-network default = return to CharSelect); the leave-world/logout
  path overrides value-4 with the quit-prep value (client EXITS, no auto-return). Corrects the older loose
  "state 5 = in-game" doc and the misattribution of the value-4 write to teardown. Now unambiguous in
  `world_systems.md §13.1` (+ a cross-ref caveat in `frontend_scenes.md`). The C# SceneStateMachine encodes both.

- Block A — CharSelect specs promoted/updated (all G4-stamped, banner-pinned 263bd994):
  - `packets/cmsg_char_create.yaml` — full 52-byte 1/6 create payload byte-pinned LE (name[18]@0, Face u8@0x12,
    AppearanceA u16@0x14, AppearanceB u16@0x16, ClassInternalId u8@0x18, Reserved1A[2]@0x1A, Stat0..4 u32@0x1C..0x2C,
    PointsRemaining u32@0x30); Σ=52.
  - `specs/character_creation.md` — class remap {UI 0→internal 4, 1→1, 2→3, 3→2}; point-buy = 5-point budget, all
    five stats floor 10, clamp [10,15], invariant Σstats+points=55; the "fifth-stat-floor" reading REFUTED (the 5
    is the budget); npc.scr class-description binding (record +20/+84/+148); per-class create BGM 910062000/3/4/5.
  - `specs/rendering.md` — preview camera (FOV 50, near 5, far 15000), KF0→KF1 2.0 s dolly (KF0 yaw +2.4°/pitch −6.0°,
    KF1 yaw ~π/4/pitch −2.67°, dt=elapsed×0.0005 clamp 1), standard char material (SRC_ALPHA/INV_SRC_ALPHA), ambient
    effect id 380003000 at (508.483, 69.887, −9758.569). Filter/cull = runtime residual.
  - `specs/environment.md` — preview fog resolved OFF (overturns the §6.4a "do not assert" caveat: the build helper
    calls the fog-blend setter with literal 0.0; the char-select tick never re-pushes fog). Binary wins over spec.
  - `structs/spawn_descriptor.md` — 96-byte info-block static reach (7 create-path dword writes + constant-7 marker,
    rest runtime-filled); facing flag at +0x1548 (refutes the +0x148C side-claim).
  - `specs/frontend_scenes.md` — action dispatch table, delete-via-1/7-flag, the enter-world ladder (slot-pick →
    1/7 select → 3/14 bridge confirm → enter-ready gate → 1/9 enter → loop-exit; local player spawns on 4/1, not 3/14).
  - C# (02–04) real fixes found while rebuilding: NpcScrParser off-by-4 (paragraph offsets 0x050/0x090→0x054/0x094,
    width 60→64 — was reading corrupt class-description lines 2/3); HP corrected to i64 qword @descriptor+0x3C (the
    +0x40 "CurrentMp" was a misread; removed across 6 callers); point-buy validator + class remap made spec-exact.
  - Godot (05): KF1 yaw was authored in degrees not radians (→ Mathf.Pi/4); preview actor Y 69.89→0.0 (the ~70 was
    an invented platform-top offset; 70 is the camera look-at pivot + ambient-effect Y, not actor placement);
    create-preview scale ratio (81/70) was inverted; SlotAppearanceResolver slot-2 Dosa (InternalClass 3 → g3.bnd
    + col16 idle) confirmed correct.

- Block B — World/InGame specs promoted/updated (16, all G4-stamped, banner-pinned 263bd994): world_systems.md,
  frontend_scenes.md, rendering.md, environment.md, formats/terrain_layers.md, formats/sky.md, ui_hud_layout.md,
  equipment_visuals.md, chat.md, effects.md, effect-scheduling.md, structs/spawn_descriptor.md,
  packets/5-3_char_spawn.yaml, packets/5-1_actor_spawn_extended.yaml, packets/3-1_character_list.yaml,
  packets/5-7_chat_broadcast.yaml. Key facts:
  - Scene build = 17-step case-5 sequence; per-frame loop = 4 phases (input pump → per-render-view device step+present
    → round-robin scheduler → frame-rate limiter); 5 view-platforms; GScene root + terrain-manager singleton + **5
    layer nodes** (msg ids 2006/2004/2005/2148/2148 — 2148 reused; corrects an earlier "4 layer nodes"). Streaming:
    camera frustum copied into terrain each frame; stream radius clamped to 1000 at the far plane. Keepalive ENABLE
    on entry; DISABLE on logout (C2S 2/112, 1-byte body).
  - Spawn wire (Σ-balanced): 5/3 = 908 B (8-byte prefix + 880 descriptor + 20 trailer); 5/1 = 912 B (12-byte prefix
    incl. title-state/title-slot/relation-flag + descriptor + trailer; player-branch 64-bit HP); 3/1 = 981 B/slot
    (880 descriptor + opaque 96-byte stats + 1 flag + 4 flags-word, 5-bit mask).
  - Chat 5/7 body = [u32 len][len CP949 bytes] (client NUL-appends; the YAML old size−8−36 over-counted by 4);
    sender name in the 36-byte header; channel byte selects routing + an ARGB colour ladder — **code 7 = pink
    0xFFFF797C** (NOT red; the dirty "code 7 = red" mis-read was reverted — red is codes 16/17 0xFFFF4040), **code 10
    = yellow 0xFFFFFF00** (49079 notice; not code 8).
  - Lighting/sky: brightness = pixel-shader composite constants (defaults 1.0), **NOT a gamma ramp** (the 0.5 was an
    FP-stack artifact); DISPLAY_LIGHT_RATIO is parsed-but-never-read (DEAD); 9-state character tint applied at the
    cel draw via a 9-entry table. Sun/moon orbit = closed-form trigonometry (cos/sin), **NOT a log curve and NOT a
    stored keyframe track**; moon is a flat circle (no Z), only the sun carries a Z term; fog LINEAR, range=s×3,
    near=1/s, enabled when s>0. (Corrects sky.md §D.2 "natural-log curve"/"millisecond"/moon-vertical-arc.)
  - Render state: particle/UI vertex stride 24; in-world UI via the D3DX sprite helper; in-game HUD bucket leaves
    alpha-blend OFF at bucket-enter, each panel opts in per-quad. Terrain fx1–7 = 7 channels (fx3/fx5 water), indexed
    meshes with on-disk UVs, 16×16 cull, 1-based texture remap. Actor visual: skin-level threshold 1000 (gates
    equip-overlay catalog + full vs reduced slot binding), non-weapon GID scale 10000, categoryBase[47], dual-wield
    node flags 2/1. Effects: the 10001 scheduled-effect drain is a TWO-PASS full-tree sweep (fire ALL due events, no
    early stop — resolves the open spec question); scene-transition reset = a timed-event-queue flush (not a full
    effect reset); bone-source→name is data-driven (no compiled table).
  - C# (02–04): new spec-faithful modules WorldSceneContract, TimedEventQueue (two-pass drain), EquipOverlayResolver;
    deleted SkyBoxParser (.box absent from VFS, spec says no loader), a stale chat-body heuristic, a vestigial
    terrain field. Closeout wired two dangling connections: 5/53 vitals → IHudEventHub.PublishVitals (local player),
    and the local player six visible-gear EquipGids → LocalPlayerSpawnedEvent → in-world equip overlay (GID→part
    bridge single-sourced with the char-select SlotAppearanceResolver path; weapon slot-14 hand-bone-id 0 deferred).
  - Godot (05): sun/moon trig billboards on SkyDomeNode; HUD geometry corrected (skill-hotbar container 349,13,7,504
    inner-x 982 two 9-slot loops, target plate 226×54 top-flush centred, caption font slot 4); chat colour authority
    moved to the core (deleted a duplicate hardcoded-white publish path).

- conflicts arbitrated against the binary (binary wins, all journaled in the dirty ARBITRATION records): facing flag
  +0x148C→+0x1548; rig node offset +6204→+6196 (world-mgr node = +6204); camera +544=yaw/+520=pitch; preview fog OFF
  (overturned the spec caveat); chat code-7 pink (dirty mis-read reverted, the committed chat.md was already right);
  5/7 body framing (explicit u32 len prefix); layer-node count 4→5; sky log→trig + moon-no-Z; DISPLAY_LIGHT_RATIO
  dead; composite-brightness defaults 1.0. A NEW conflict downgraded rendering.md §4.2 front-end blend (the CYCLE-10
  "one/one additive binary-won" claim) to DEBUGGER-PENDING — the front-end leaf quads route through the sprite
  helper Begin(alpha-blend) which may override the global one/one per draw; the in-game HUD per-quad opt-in is
  unaffected and stays CONFIRMED. (Registered as RD-render-blend; load-bearing but presentation-only, non-blocking.)

- IDB legibility annotations applied (idempotent, neutral, idb_save OK): CharSelect — SelectWindow_ApplyClassSelection
  (+ a renamed singleton local); World — MainWindow_SceneInit, MainWindow_SceneTeardown, MainWindow_PerFrameUiHook,
  Terrain_WireCameraFrustum, Terrain_SetStreamRadius, EffectSchedule_DrainDueEvents, EffectManager_FlushTimedEventQueue,
  Actor_DrawSkinnedCelWithTint, ActorVisualGlobal_Init (+ globals ActorVisualGlobal[3]=skin-level-threshold(1000),
  [2]=non-weapon-GID-scale(10000), categoryBase[47]).

- doc reconciliations (prose only; C# already followed the authoritative reading): formats/terrain_layers.md fx7
  group-header width 48→52 B (reconciled §1.4a/§1.10/§1.12 to the §1.13 census); packets/5-3_char_spawn.yaml
  trailer offset comment 0x37c→0x378 (correct Pack=1 sum 8+880=888).

- deferred (STATIC-ONLY → owed to a future ?ext=dbg/capture pass): 11 RD-* residuals registered in ROADMAP CYCLE 11
  (RD-render-blend, RD-hand-bone, RD-chat-u32, RD-create, RD-action [1/7 delete-context flag — delete path is a
  guarded no-op until pinned], RD-spawn-values, RD-effect-pools, RD-light-ratio, RD-xeffname, RD-mat, RD-vitals-hp
  [5/53 HP exposed as u32 while descriptor HP is i64 — low-impact, values fit 32-bit]). None block structural fidelity.

- build result: `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = 0 errors / 0 warnings (39 core
  projects + Tools + layer-05); check_dag.py OK (downward-only acyclic); Godot headless --quit-after 150 exit 0,
  clean to Login; code-reviewer PASS (0 blockers, zero-alloc world packet path confirmed); render-reviewer structural
  PASS for both scenes; clean-room-check PASS (no decompiler artifacts; firewall held). FINAL GATE A and FINAL GATE B
  both GREEN. **Live runtime-pixel fidelity (terrain streaming, spawned/equipped actors, live HUD/chat) is the
  maintainer visual oracle on the Auth replica — not claimed here.** No commit made (awaiting user request).

## CYCLE 12 — PHASE 2 « select→enter ladder root-cause » (static IDA, 263bd994) — 2026-06-22

- Mandate: statically determine WHY a char-enter at char-select drew a genuine server 3/100 result
  code 23 reject. Phase 0 had proved the wire LAYOUT byte-correct (1/7, 1/9) and the 3/100 Result
  decode correct, so the reject is provoked by a VALUE or ORDERING, not a layout defect. IDA STATIC
  ONLY; no debugger, no captures.

- Recovery (dirty, gitignored): three READONLY analyst lanes, reconciled:
  - the 1/9 enter-request FIELD VALUE SOURCES;
  - the select→enter EMISSION ORDERING + the 1/7 mode-byte → UI-action table;
  - the char-select inbound flow CONFIRM/DELTA (3/1, 3/4, 3/7, 3/14, 1/6, drain mechanism, descriptor).

- Binary-won findings promoted (rewrite-not-copy; firewall held; no addresses/pseudo-C crossed):
  1. ENTER IS A SERVER ROUND-TRIP (load-bearing correction). The normal play flow is
     1/7 (mode 1, select-and-play / play-confirm) → 3/14 SmsgCharSpawnResponse (positive flag) →
     1/9 CmsgEnterGameRequest emitted FROM the 3/14 handler (server-triggered) → 3/5 SmsgEnterGameAck.
     A client firing 1/9 unilaterally off the Enter button (no mode-1 1/7, no positive-flag 3/14 wait)
     sends 1/9 out of sequence — the leading static hypothesis for the genuine code-23 reject.
  2. 1/7 MODE TABLE corrected: mode 1 = select-and-play (the play-confirm that drives the ladder);
     mode 0 = slot-lock / pre-play (also stamps the chosen name into the HUD). The earlier
     "mode 0 = select, mode 1 = delete" reading is corrected; the DELETE-confirm mode byte is NOT
     statically provable and is capture/debugger-pending.
  3. 1/9 SessionToken (33 B @ +0x01) corrected: it is the lowercase-hex MD5 digest of the client's
     OWN executable file (argv[0] path through an MD5-of-file hasher; 32 hex + NUL) — a build-integrity
     self-checksum, NOT a launcher/login session token. Always present in a normal launch. The exact
     digest bytes are runtime (capture-pending); the source + format are static-HIGH. Supersedes the
     prior "launcher command-line token" reading.
  4. 1/9 other fields confirmed: SlotIndex @0x00; 2-byte zero Pad @0x22; VersionToken @0x24 =
     10*(game.ver field 5)+9. No 1/9 field echoes any prior inbound packet (the C# echoing nothing is
     correct).
  5. Char-select flow CONFIRMED: 3/1 SmsgCharacterList (5-slot roster cache; 880-byte spawn descriptor
     + 96-byte stats + flag + timing; class/variant/level offsets match); 3/4 SmsgSceneEntityUpdate
     in-place refill; 3/7 SmsgCharManageResult (8-byte UI result, no engine state, subtype 2 =
     delete-confirmed, clears the select-window latch); 3/14 = the enter-trigger; 1/6 create (52-byte
     body, CP949 name @0). Inbound char-select packets are dispatched SYNCHRONOUSLY in wire-arrival
     order via the shared NetHandler singleton (no deferred drain/replay queue). Terminology DELTA:
     there is no separate "96-byte descriptor" at select — appearance fields come from the 880-byte
     spawn descriptor; the 96-byte block is the stats block.

- Committed specs corrected + banners re-pinned 263bd994 / 2026-06-22:
  specs/frontend_scenes.md (§7 enter-world handoff rewritten to the server round-trip ladder; §8
  send-map mode bytes corrected; banner), packets/cmsg_char_enter.yaml (SessionToken self-checksum +
  ENTER SEQUENCE ladder), packets/cmsg_char_select.yaml (mode roles per UI action), packets/
  3-14_char_spawn_response.yaml (role corrected to the enter-ladder TRIGGER), packets/
  3-100_char_action_result.yaml (enter-ordering hypothesis for the code-23 reject).

- Implied C# fix (route-ready; NOT applied here): the SelectScene Enter button currently calls the
  enter use-case which sends ONLY 1/9 (mode-byte select sends 0, SessionToken zero-filled). To match
  the original: (a) send 1/7 mode=1 on play-confirm; (b) gate 1/9 on the positive-flag 3/14 and emit
  it from the 3/14 handler; (c) fill the 33-byte token with a valid-shaped 32-hex string + NUL rather
  than 33 zeros. Routed to csharp-port-orchestrator (02/04) + godot-orchestrator (05).

- Capture-pending residuals (static boundary stated, NOT guessed into specs): the exact MD5-digest
  bytes; the concrete VersionToken integer; the delete-confirm 1/7 mode byte (0 vs 1); whether the
  server strictly requires the mode-0→mode-1 two-step or only the mode-1 play-confirm; and the exact
  server rule that maps to result code 23. All owed to a future ?ext=dbg/capture pass.

- Firewall: clean-room held — promotions are neutral rewrites; no Hex-Rays artifact, pseudo-C, or
  address crossed into a committed spec. Dirty sources left intact under Docs/RE/_dirty/protocol/.


---

## CYCLE 12 — PHASE 3 « in-game WIRE + STRUCT deepening » (static IDA, 263bd994) — 2026-06-22

- Mandate: statically deepen the in-game packet + struct corpus the live world consumes once the
  player enters — the 4/1 world-entry snapshot internals, the 4/4 area-snapshot actor record, and the
  position/combat sync packets — so spawned actors, the hotbar, and movement render/track correctly.
  IDA STATIC ONLY on doida.exe, IDB SHA 263bd994; no debugger, no captures. DeepAnalyse → Counter-check
  → promote → STAMP.

- Recovery (dirty, gitignored): six READONLY analyst lanes (massively parallel), each independently
  counter-checked by a fresh analyst; reconciled, binary wins:
  - 4/1 WorldEntryTableA / TableB internal strides + roles (the two stale-slot sweep consumers);
  - 4/1 HotbarSlots stride + per-slot layout (the verbatim hotbar-copy consumer);
  - 4/4 area-snapshot 892-byte actor record (prefix + descriptor + trailer + composite key);
  - 5/10 SmsgCharDeath body + deathCause switch map;
  - 5/3 / 4/13 / 5/13 / 2/13 re-confirm sweep;
  - 4/4 tag-4 ground-item LIVE/orphaned verdict + the dangling cross-ref fix.

- Binary-won findings (control-flow-confirmed + counter-confirmed on 263bd994):
  1. 4/1 table A = a flat 16-byte-record roster table (cap 193, 120 swept): each record = an actor id
     + a keep-guard (eviction gate; doubles as the displayed member number) + an aux value. Role =
     the membership/roster panel slot source.
  2. 4/1 table B = a HETEROGENEOUS 4044-byte block: 240 × 16-byte scene tracked-entity / actor-slot
     records (same shape + evict predicate as table A) + a 20-byte gap + a 21 × 8-byte category-entry
     tail list (category code + value) + a 16-byte world-target selection record (3840+20+168+16=4044).
     Role = the scene's tracked-entity/actor-slot table (party & spawn-group queries read it).
     Shared evict predicate: clear a slot only when id != 0 AND the actor resolves AND its stale flag
     is set AND keep-guard == 0.
  3. 4/1 HotbarSlots = 240 × 8-byte slots: entry key (0 = empty) + count; NO inline type byte —
     skill-vs-item resolved by a skill-catalogue lookup (catalogue category 5 = skill).
  4. 4/4 area-actor record = exactly 892 bytes = 8-byte prefix (actor id u32 @+0, kind byte @+4
     [value 5 = visual-only refresh], relation/visual byte @+5, 2 pad) + 880-byte SpawnDescriptor @+8
     + a 4-byte trailer @+0x378 (visual byte, pad, combat-timer flag, pad). NO sort dword in the
     prefix — the actor SORT is carried out-of-band by the leading TAG byte (tag 1 ⇒ player); the
     composite key is (actor id from +0, sort from the tag byte). The 4/4 record is the shortest
     spawn carrier; its name update arrives via the separate 36-byte tag-6 record.
  5. 5/10 SmsgCharDeath = a 20-byte body: victim sort/id @+0/+4, deathCause i32 @+8, killer sort/id
     @+0xC/+0x10. deathCause switch {0 normal, 1 PK-A, 2 PK-B, 3 special/no-modal}; the common arm
     clears the locked battle target, zeroes combat resources, plays the death motion (action-state +
     alive flag + timestamp), and clears the buff-slot array. PK effects anchor on the VICTIM pair,
     not the killer (counter-refined from the Stage-1 reading).
  6. 4/4 tag-4 ground item = LIVE (handler reads the 24-byte record; layout confirmed unchanged). The
     dangling cross-ref is closed: the parent 4/4 framing spec now exists and opcodes.md 0x40004
     points at it.

- CONFLICTS resolved (binary wins):
  - 4/13 SmsgLocalPlayerStateSync: the sync-mode byte is at wire offset 33 (0x21), NOT 32 — wire
    bytes 24..32 are an unconsumed reserved gap. The committed yaml was corrected.
  - Stage-1 "table B 12-byte selection record" → counter corrected to 16 bytes (the 4044 arithmetic
    only closes with a 16-byte selection record).
  - 5/10 PK effect anchor → victim pair (not killerId).
  - 2/13 move-mode wire value space = the SINGLETON {3} (the mode byte is a hardcoded literal in a
    shared sender, not a parameter); no second statically-decidable arm.

- Committed specs created / corrected / re-pinned 263bd994 / 2026-06-22:
  - CREATED packets/5-10_combat_death.yaml (5/10 SmsgCharDeath body + deathCause switch map).
  - CREATED packets/4-4_area_entity_snapshot.yaml (the parent 4/4 framing: 17-byte header + tag loop;
    closes the dangling cross-ref).
  - CORRECTED packets/4-13_local_player_state_sync.yaml (Mode byte → offset 33; banner).
  - EXTENDED packets/4-1_game_state_tick.yaml (interior record strides for tables A/B + hotbar).
  - structs/actor.md (new "4/4 area-entity-snapshot actor record" section + status-header row).
  - specs/world_entry.md (new §2.3a interior tables + hotbar init; §2.4 4/4 framing note; banner).
  - specs/world_systems.md (new §13.3 the 4/1 world-entry seed tables).
  - Banners re-pinned on packets/5-3_char_spawn.yaml, packets/5-13_actor_movement_update.yaml,
    packets/2-13_move_request.yaml (re-confirmed unchanged; 2/13 move-mode singleton note added).
  - opcodes.md: 0x40004 and 0x5000a spec-path columns now point at the new specs.

- Capture-pending residuals (static boundary stated, NOT guessed into specs): all 5/10 death-penalty
  magnitudes (xp/durability/drop) and its effect/message-id + level-threshold value meanings; the
  4/1 category-code and hotbar non-skill-family semantics; the 4/4 kind/relation/trailer byte VALUE
  meanings; whether 4/13 Mode / 5/10 deathCause is a full dword vs a byte+pad on the wire. All owed
  to a future ?ext=dbg / capture pass — none block the in-game world-handler layout work.

- G4 readiness: every touched/created in-game wire+struct spec is STAMPED implementation-ready for
  the C# world-handler fixes (control-flow-confirmed against 263bd994); the residuals above are
  NON-blocking runtime confirmations. GATE: GREEN.

- Proposed names.yaml glossary additions (orchestrator to apply via ida-toolsmith, NOT applied here):
  structs PartyMemberSlot/RosterSlot, SceneEntitySlot, TargetCategoryEntry, HotbarSlot,
  AreaActorRecord; fields actorId/keepGuard/auxValue, EntryKey/Count, victimSort/victimId/deathCause/
  killerSort/killerId; enum DeathCause{Normal,PkTypeA,PkTypeB,SpecialNoModal}; globals
  g_PartyMemberSlots/g_RosterSlots, g_SceneEntitySlots, g_HotbarSlots.

- Firewall: clean-room held — every promotion is a neutral rewrite; no Hex-Rays artifact, pseudo-C,
  mangled name, or binary address crossed into a committed spec. Dirty sources (Stage 1 + Stage 2
  counter) left intact under Docs/RE/_dirty/world/ and Docs/RE/_dirty/world/counter/.

---

## CYCLE 12 — Block A « CharSelect deep+counter recon » (static IDA, 263bd994) — 2026-06-22

- date: 2026-06-22
- analyst: AriusII (Tier-1 main session orchestrating re-orchestrator / csharp-port-orchestrator / godot-orchestrator)
- binary: doida.exe @ 263bd994 (SHA `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*) — STATIC ONLY (no `?ext=dbg`, no captures; user decision). NO tests this cycle.
- analyzed: SelectWindow build path (virtual slot 14, 127-widget census); keyframed free-look 3D preview camera;
  3/1 880-byte roster descriptor; create flow and its 1/6 send path; SceneStateMachine select-mode dispatch table
  (handlers.md §23.1 full set vs the stale §7.5.2 subset); C2S opcode builder family (1/0, 1/6, 1/7, 1/9, 1/13,
  1/14) confirmed via twin forward+backward static reads; SelectWindow command-dispatcher action-id model (full
  roster 10–73 incl. spinner ids 25–34, create 35/36, yaw 66–73); spinner display fields as 1/6 create-blob wire
  fields; 3/7 SmsgCharManageResult sub=2 delete+cooldown semantics; charselect.md §4.3/§4.4/§6.1/§6.2/§7.4/§7.5
  spec corrections; scene_state_machine.md dual Select→InGame bridge + post-1/9-send globals copy.

- binary-won findings (control-flow-confirmed via twin forward+backward read + reconcile):
  1. C2S opcode map definitively settled: create=1/6 (52B), select-slot=1/7 (2B [slot, mode]; mode 0 = slot-lock,
     mode 1 = select-and-play), enter-world=1/9 (40B; SessionToken = client-executable MD5 33B @+0x01;
     VersionToken = 10×game.ver[5]+9), rename=1/13 (18B), logout=1/0 (0B). Earlier hypotheses enter=1/8 and
     create=1/9 are REFUTED. All C# [PacketOpcode] bindings were already correct.
  2. SelectWindow action-id roster recovered: spinners 25–34 (10 ids, non-sequential +/− pairing; spinner display
     fields ARE the 1/6 create-blob wire fields); create-confirm 35 (sends 1/6); create-cancel 36 (inferred,
     C12-D2); face +/− 21/22; class pick 10=Monk(4)/11=Musa(1)/12=Dosa(3)/13=Salsu(2); select-slot confirm/cancel
     54/55; rename 59/60; conditional play overlay 61; delete/move-out 62/63 (1/14, gated); plain panel-close 64;
     actor-yaw buttons 66–69; actor-yaw drag 70/71; camera boom-zoom drag 72/73.
  3. 3/100 select-mode inert band {212–219, 228–231} in SceneStateMachine was routing to the fatal Error(7/8)
     arm. Per handlers.md §23.1 this band is inert no-op in select-mode; corrected to a no-op. The in-world 3/100
     branch was rekeyed on local-player presence per §23.1. The fatal code-23 exit is fixed; the residual latent
     crash (inert-band mis-routing) is also fixed.
  4. Enter ladder confirmed: 1/7 mode-1 → 3/14 SmsgCharSpawnResponse (positive flag) → 1/9 from the 3/14 handler
     → 4/1 sole spawn. Opcode 1/14 = 1-byte [slot] behind the confirm modal; move-vs-delete label CONTESTED
     (send-site debug strings say "move/relocate"; 3/7 sub=2 performs deletion+cooldown). Label deferred (C12-D1).

- specs produced/updated (all banner-pinned to 263bd994 / CYCLE 12 / 2026-06-22):
  - `Docs/RE/scenes/charselect.md` — §7.5 3/5↔3/7 swap removed + fictitious IDB-mislabel note deleted;
    §6.1 keyframed camera + ambient XEffect 380003000 pos; §6.2 scales 70 lineup / 81 create with
    3.0 = idle playback-rate; §7.4 enter ordering; §4.3/§4.4 full action-id roster incl. 35/36 create,
    25–34 spinners, 66–73 yaw/zoom.
  - `Docs/RE/specs/character_creation.md` — implicit ceiling-15 via 5-point budget; @BLANK@-name vs
    880-byte record separated; both create latches.
  - `Docs/RE/scenes/scene_state_machine.md` — re-stamped CYCLE 12; dual Select→InGame bridge;
    post-1/9-send globals copy.
  - `Docs/RE/opcodes.md` — 1/14 contested-label note added.
  - `Docs/RE/packets/SmsgCharManageResult` citation repointed to `3-7_char_manage_result.yaml`.
  - `Docs/RE/packets/SmsgCharSpawnResult` citation repointed to `3-14_char_spawn_response.yaml`.

- notes: This entry covers the Block A CharSelect deep+counter recon pass. The primary outcome is the
  definitive C2S opcode map (1/6/7/9/13/14), settling long-standing static hypotheses. The full action-id
  roster (25–34 spinners, 35/36 create, 66–73 yaw/zoom) was recovered and the Godot action model rebuilt
  to it — a create-unreachable regression (action 35/36 swallowed by the spinner range) was found and fixed.
  The 3/100 inert-band fix in SceneStateMachine closed a latent crash that existed alongside the original
  fatal code-23 bug fixed in Phase 0. The spinner display fields being the 1/6 wire fields is a key
  structural finding: the create modal is not a separate encoding; the widget values ARE the wire values.
  All findings crossed the firewall as neutral prose; no addresses or pseudo-C in any committed file.
  Dirty sources under Docs/RE/_dirty/cycle12/charselect/ (gitignored).

- deferred (static boundary stated, capture/debugger-pending; registered in ROADMAP CYCLE 12 deferred register):
  C12-D1 (1/14 move-vs-delete label), C12-D2 (action id-36 create-cancel inference), C12-D3 (id-4/id-61 button
  binding), C12-D4 (66–69 vs 70–73 live widget binding), C12-D5 (Stat0..4 named-stat mapping), C12-D6
  (SessionToken digest bytes), C12-D7 (1/7 mode server interpretation), C12-D8 (3/7 ready_time epoch),
  C12-D9 (3/100 full code enum), C12-D10 (3/14 flag/pos value semantics), C12-D11 (visual fidelity of
  rebuilt create flow).

- build result: `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = 0 errors / 0 warnings
  (40 projects); check_dag.py OK (39 core, downward-only acyclic); Godot headless boot exit 0 clean through
  login → server-select → game-connection → char-select; clean-room firewall audit CLEAN. BLOCK A GATE GREEN.

---

## CYCLE 12 — Block B « World/InGame deep+counter recon + layer-05 correctness » (static IDA, 263bd994) — 2026-06-22

- date: 2026-06-22
- analyst: AriusII (Tier-1 main session orchestrating re-orchestrator / csharp-port-orchestrator / godot-orchestrator)
- binary: doida.exe @ 263bd994 (SHA `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*) — STATIC ONLY (no `?ext=dbg`, no captures; user decision). NO tests.
- analyzed: world loop exception handling path (OnUnhandled / Handle overloads / _Process-drain try/catch);
  terrain height sampling (triangle selection + plane interpolation vs bilinear); pixel-shader brightness
  constants (BASE_BRIGHT_MULTI c0, GLOW_BRIGHT_MULTI c1; DISPLAY_LIGHT_RATIO read-site sweep);
  sky orbit math (cos/sin thunk bodies; sun Z term; moon flat-circle shape); camera near/far + height
  sampler; SelectWindow enter-committed latch (scene member vs NetClient); enter-world descriptor/stats/level
  copy sequence; spawn-opcode census (908 / 912 confirmed; 981 = roster slot byte-size, not a spawn opcode);
  weapon bone-slots 902..905; ingame.md HUD cartography scope; HudTargetFrame chrome rect + label slots;
  Key.B vs Key.I disambiguation; camera eye-offset legacy model; EffectRenderer dead array path;
  GameLoop.EventDrain delegate caching.

- binary-won findings (control-flow-confirmed on 263bd994, counter-confirmed by fresh analyst pass):
  1. NO P0 live-crash exists in the in-world loop. The world loop is fail-soft by design: all unhandled
     paths route through OnUnhandled + Handle overloads; _Process and drain paths carry try/catch guards.
     GUARDRAIL: the _unhandled.Record diagnostic must be preserved; a fatal 3/100 classifier must NOT be
     added to the in-world path.
  2. Opcode 981 is the byte-count of a 3/1 SmsgCharacterList slot record (roster data, not a spawn).
     The only spawn opcodes are 908 (5/3 SmsgCharSpawn) and 912 (5/1 SmsgActorSpawnExtended).
  3. Ground height = per-triangle plane interpolation, NOT 4-corner bilinear averaging. The height-map
     supplies vertex Z values; the sampler identifies the correct triangle within the cell and evaluates
     the plane equation for the query point. Corrects a prior spec reading.
  4. DISPLAY_LIGHT_RATIO: CONFIRMED DEAD on the world-geometry path (no live read site reaches it).
     BASE_BRIGHT_MULTI and GLOW_BRIGHT_MULTI are pixel-shader constants c0/c1, not CPU-side scalars
     (environment.md §9.2/§9.4 corrected).
  5. Sky helpers are cos/sin thunks (closed-form orbit). The prior "natural-log curve" and
     "millisecond-resolution keyframe track" readings are refuted. Moon = flat circle (no Z term);
     only the sun carries a Z component in its orbit math. Corrects sky.md §D.2.
  6. Camera near/far CONFIRMED; terrain-height sampler pinned to camera_movement.md §A.7.1/§B.6.
  7. The enter-committed latch is a SelectWindow scene member (NOT a NetClient keepalive-suppress).
     Entering copies descriptor + stats + level into live-player globals; 4/1 reuses the descriptor
     as the spawn seed; 3/5 is the ack, NOT the spawn trigger. Corrects scene_state_machine.md §3.1/§3.2.
  8. Weapon bone-slots 902..905 confirmed as the weapon-attachment range. Which-hand assignment within
     that range is runtime-selected (debugger-pending; registered as C12-D16).
  9. ingame.md rescoped as HUD cartography. Entries for opcodes 981/2/12 removed (not world-build
     opcodes). The fabricated §5.5b anchor in the spec was flagged as non-existent.

- specs produced/updated (all banner-pinned to 263bd994 / CYCLE 12 / 2026-06-22):
  - `specs/terrain-streaming.md §6.5` — ground height corrected to per-triangle plane interpolation.
  - `formats/terrain.md` (new §5.4a) — per-triangle height-sampling algorithm recorded.
  - `formats/sod.md` — cross-reference to per-triangle height updated.
  - `specs/environment.md §9.2 / §9.4` — DISPLAY_LIGHT_RATIO CONFIRMED DEAD; BASE/GLOW_BRIGHT_MULTI
    recorded as pixel-shader constants c0/c1.
  - `specs/sky.md §D.2` — sky orbit corrected to closed-form cos/sin; moon flat-circle (no Z);
    natural-log and keyframe-track readings refuted.
  - `specs/camera_movement.md §A.7.1 / §B.6` — near/far confirmed; terrain-height sampler pinned.
  - `scenes/scene_state_machine.md §3.1 / §3.2` — enter-committed latch corrected to SelectWindow
    scene member; enter copy sequence + spawn-trigger (4/1 not 3/5) corrected.
  - `specs/effects.md §A.16.2` — weapon bone-slots 902..905 confirmed; which-hand deferred.
  - `scenes/ingame.md` — rescoped to HUD cartography; 981/2/12 removed; §5.5b flagged non-existent.
  - Dirty sources: `Docs/RE/_dirty/cycle12/world/` (gitignored).

- firewall integrity fix: 52 fabricated `§5.5b` citations were identified in `HudTargetFrame.cs`,
  `HudMaster.cs`, and `HudMaster.Builder.cs` and stripped. Re-pointed to real committed anchors:
  `ingame.md §5 / §5.4 / §5.5a` and `ui_hud_layout.md §5.5a`. Zero §5.5b citations remain in
  any committed file.

- layer-05 fixes applied (Godot layer 05, all spec-cited):
  - TerrainNode.cs: ground-height sampler → per-triangle plane interpolation (terrain.md §5.4a).
  - HudPlayerStatusPanel.cs (NEW, slot 15): player-vitals panel built (was previously ABSENT);
    displays HP / MP / stamina / condition / portrait / level; drains from the vitals hub.
  - HudTargetFrame.cs: chrome rect corrected to 226×54; PercentLabel populated from HpRatio;
    label font slots corrected (ui_hud_layout.md §5.5a).
  - HudMaster.cs / HudMaster.Builder.cs: Key.B no longer toggles the skill window (ingame.md §13.1).
  - HUD command bar: CP949 literals replaced with msg.xdb IDs.
  - CameraController.cs: F1/F2/F3 view-mode hotkeys (Third/First/Static); Gamble/Event deferred;
    eye-offset switched to the legacy rotated model (camera_movement.md §A.5).
  - EffectRenderer.cs: vestigial gpuParticles array + placeholder-fallback removed.
  - GameLoop.EventDrain.cs: OnTimedEvent delegate cached.
  - World.tscn env + PlayerController.cs deletion: confirmed already done by prior campaign (no-op).

- deferred (static boundary stated, NOT guessed into specs; registered in ROADMAP CYCLE 12 deferred
  register as C12-D12 through C12-D18):
  WaterRenderer keep-vs-remove (non-original; visual-oracle pending); RealWorldRenderer dual-path
  merge (deferred refactor); ~20 secondary HUD windows without hub channels; camera Gamble/Event modes;
  weapon which-hand bone (C12-D16); 5/53 VitalB/VitalC + level boundary, chat 5/7 endianness/ARGB
  band, 3/100 in-world code value map, 3/5-vs-4/1 arrival order (all capture/?ext=dbg-pending; C12-D17);
  visual fidelity of terrain height, vitals panel, camera framing — spec-faithful but not
  screenshot-verifiable this cycle (C12-D18).

- build result: `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = 0 errors / 0 warnings
  (40 projects); check_dag.py OK (39 core, downward-only acyclic); Godot headless boot World.tscn exit 0
  clean (VFS 43,347 / catalogues 90,937 items / 2,000 skills / 3,997 mobs; no SCRIPT ERROR / ERROR /
  Parse Error / Failed to load); clean-room firewall CLEAN (zero decompiler artifacts, zero raw addresses,
  zero §5.5b residue). CYCLE 12 BLOCK B GATE GREEN. CYCLE 12 CLOSED.

---

## CYCLE 12 — Block C « static-only deferred sweep » (static IDA, 263bd994) — 2026-06-22

- date: 2026-06-22
- analyst: AriusII (Tier-1 main session; docs-engineer consolidation pass)
- binary: doida.exe @ 263bd994 (SHA `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*) — STATIC ONLY (no `?ext=dbg`, no captures; user decision).
- analyzed: 1/6 CmsgCreateCharacter stat-field layout and data-flow (all five stat wire fields, their
  action-id spinner pairings, and the PointsRemaining derivation); RealWorldRenderer dual build-path
  runtime-flag mechanism; secondary HUD window inbound-feed audit (all ~20 windows checked for
  recovered-but-disconnected channels).

- binary-won findings (static IDA, 263bd994):
  1. **1/6 stat fields are `u32`, not single bytes.** The committed `CmsgCreateCharacter` struct and
     `cmsg_char_create.yaml` already record Stat0–Stat4 as `u32` at offsets `0x1C / 0x20 / 0x24 /
     0x28 / 0x2C` (4 bytes each); PointsRemaining is `u32 @0x30`. An earlier implementation assumed
     byte-wide stat fields; that assumption is corrected by the static recovery. Total payload = 52 bytes
     (layout confirmed from the 263bd994 IDB across multiple read sites).
  2. **Non-sequential spinner action-id mapping** confirmed from the Block A action-id roster:
     Stat0 ↔ ids 25/30 (+/−); Stat1 ↔ 26/31; Stat2 ↔ 27/32; Stat3 ↔ 29/34; Stat4 ↔ 28/33.
     The ids are non-sequential: the +/− pairs for Stat3 and Stat4 swap the tens digit (29/34 vs 28/33).
  3. **No msg.xdb stat-name table exists** (negative finding). The five stat-row labels are `.gui`
     widget-name driven at the layout-resource level; there is no opcode-level or `msg.xdb`-id-level
     mapping from an integer index to a named stat (STR/CON/…). The search for such a table is closed.
  4. **RealWorldRenderer build-path split is intentional, not dead code.** The `_composeRender` runtime
     flag (env `MH_COMPOSE_RENDER` / `client_dir.cfg` key `compose_render`, default OFF) gates two
     distinct paths in one partial class. The legacy path (`BuildLegacyAreaContent`) is the shipped
     canonical; the composer path is dormant pending three preconditions (per-cell `.map` cache, ring-wide
     building-texture coverage, windowed FX-slot validation). Neither branch is deletable now. Three
     `TODO(C12-D13)` breadcrumbs document the preconditions at the fork sites.
  5. **Secondary HUD windows: no wiring is possible without RE.** Full audit of all ~20 secondary windows
     found that every one carries a `TODO(capture)` or `TODO(world-campaign)` on its inbound feed. There
     is no recovered-but-disconnected layer-04 channel for any of them. `HudPartyWindow` (via
     `RosterSnapshotEvent`) and `HudSkillHotbar` (via `HotbarInitializedEvent`) are already wired and
     correct. All others need their feed opcode and handler recovered before wiring can proceed.

- specs produced/updated:
  - `Docs/RE/packets/cmsg_char_create.yaml` — no structural change required (layout already correct);
    the stat-field `u32` types and the PointsRemaining derivation are already recorded. This entry
    confirms the implementation is now consistent with the committed spec.
  - Dirty source for the stat data-flow finding: `Docs/RE/_dirty/cycle12/charselect/create_payload_layout.md`
    (gitignored).
  - No other committed spec was changed this block (audit findings; no new binary facts to promote).

- notes: This was a consolidation / actionable-deferred sweep, not a new RE pass. The primary outcome
  is the point-buy data-flow wiring (Outcome 1): the signal, use-case, and 1/6 packet now carry the
  real player-allocated stat values instead of a fixed seed. The build-path finding (Outcome 2) confirms
  that no code should be deleted from `RealWorldRenderer` at this stage; the deferred register entry
  C12-D13 is unchanged in scope. The HUD audit (Outcome 3) is a clean negative: no hidden wiring
  opportunities exist in the current corpus; secondary HUD windows are a future-campaign RE task
  (registered as C12-D19). All three outcomes closed GREEN; no spec values were invented or changed
  without binary backing.

- deferred (registered in ROADMAP CYCLE 12 deferred register):
  C12-D13 (composer path merge — three preconditions unmet; gated on windowed visual oracle);
  C12-D19 (secondary HUD window feed RE — ~20 windows, future world-campaign).

- build result: `dotnet build MartialHeroes.slnx --no-incremental` (nuked bin/obj) = 0 errors / 0 warnings
  (40 projects); check_dag.py OK (39 core, downward-only acyclic); Godot headless clean; clean-room
  firewall CLEAN. CYCLE 12 BLOCK C GATE GREEN. CYCLE 12 FULLY CLOSED.
