# RE Provenance Journal

Append-only log of reverse-engineering sessions. Each entry documents *what* was analyzed and
*which neutral specs* resulted — the audit trail backing the EU Art. 6 "analysis performed solely
to achieve interoperability" claim. **Never paste decompiler output here.**

Entry format (append newest at the bottom; the `re-session-log` skill automates this):

```
## YYYY-MM-DD — <analyst>
- binary: Main.exe @ <sha256 prefix>
- tool: IDA Pro 9.3 via MCP (mcp__ida__*)
- analyzed: <functions / opcodes / structs touched, by canonical name>
- specs produced/updated: <committed paths under Docs/RE/>
- notes: <plain-language summary, no pseudo-code>
```

---

<!-- entries below -->

## 2026-06-11 — protocol-spec-author
- binary: Main.exe (doida.exe build referenced in dirty notes)
- tool: none (firewall bridge — no IDA; rewrote neutral analyst notes only)
- analyzed (by canonical name): WireCipher, WireCompression, SecureHandshake, ConfigCryptoApi
- specs produced/updated:
  - Docs/RE/specs/crypto.md (new)
  - Docs/RE/names.yaml (crypto: block populated; opcode handshake note)
- notes: Promoted the dirty-room cryptography findings into one clean spec. Documented that the
  8-byte frame header is always plaintext and that payload transforms live at the message tier,
  not the socket/framing layer; header-only packets (length 8) bypass all transforms. Described the
  outbound pipeline as plaintext payload -> keyless/stateless byte cipher (3 rounds, each a forward
  then backward sweep with per-sweep feedback accumulator, position mixing, direction-specific
  rotations and two whitening constants) -> LZ4 compression. Stated explicitly the cipher takes no
  seed/key and that the major:minor opcode is not a cipher seed. Flagged prominently the #1 open
  question: the client receive path only LZ4-decompresses and applies no inverse cipher, implying
  server->client payloads may be compressed-only; marked capture_verified: false pending a live
  capture check. Documented the separate opcode 0/0 bignum public-key session handshake (server
  modulus + exponent + token; client modular-exp reply as Auth 1/4 with light per-dword XOR
  whitening) and that it does not key the wire cipher. Scoped out the ADVAPI32 CryptoAPI cluster as
  anti-cheat + signed-config loader. Listed the numeric constants still to recover (round count,
  per-sweep rotation amounts, whitening bytes, position-counter derivation, LZ4 variant, handshake
  XOR key and field layout) under a Recovery TODO; no values invented. No code or addresses copied.

## 2026-06-11 - protocol-spec-author
- binary: Main.exe (legacy client; analysis pinned to the dirty-room note set)
- tool: none (clean-room promotion only; no IDA, no decompiler access)
- analyzed: promoted dirty-room protocol findings into committed neutral specs. Frame header
  (8-byte, LE: size u16, major u16, minor u16, payload from +8); the (major:minor) pair is the
  opcode; no sequence number, no checksum, no socket-layer cipher; some frames LZ4-compressed and
  expanded before dispatch. Catalogued 182 opcodes across families 0=KeyExchange, 1=ServerCommand,
  2=GameAction (C2S), 3=CharacterMgmt, 4=Response, 5=Push. Applied the 4/13 naming correction
  (Response LocalPlayerStateSync; ignored the stale 5-100 suffix). Promoted the Actor +
  SpawnDescriptor entity layout (positions are float, not fixed-point; Y often 0).
- specs produced/updated: Docs/RE/opcodes.md (182-row catalog + frame-header spec);
  Docs/RE/packets/0-0_key_exchange.yaml, 2-52_use_skill.yaml, 3-1_character_list.yaml,
  3-5_enter_game_response.yaml, 5-0_char_despawn.yaml, 5-3_char_spawn.yaml, 5-7_chat_broadcast.yaml,
  5-13_actor_movement_update.yaml, 5-52_actor_skill_action.yaml; Docs/RE/structs/actor.md;
  Docs/RE/names.yaml (opcodes block, 182 entries).
- notes: All field layouts are static inferences - NO live capture was available, so every packet
  spec is tagged status: hypothesis / capture_verified: false and lists its open unknowns. Catalog
  passes the opcode-catalog validator (0 warnings); fixed-size specs (5/0=12, 3/5=44, 5/13=40,
  5/3=908) verified to sum to their declared size via the packet-codegen generator. The 2/52
  C2S spec is explicitly incomplete (send-site not analyzed) and must not be implemented as-is.

## 2026-06-11 — dirty-room RE sessions (re-static / re-protocol / re-crypto / re-asset-format / re-struct-cartographer)
- binary: doida.exe @ 63fcaf8e (x86 32-bit, imagebase 0x400000)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*), read-only (no IDB modification)
- analyzed (by canonical subsystem): overlapped Winsock receive loop and the (major:minor) message-tier
  dispatch architecture; the message-tier wire cipher + LZ4 stage and the bignum session handshake;
  the memory-mapped VFS archive (data.inf index + data/data.vfs blob) and the .xobj/.skn/.bnd geometry
  + texture load paths; the Actor/entity object and embedded SpawnDescriptor layout.
- specs produced/updated: none directly — all raw findings were written to the gitignored
  Docs/RE/_dirty/ quarantine, then rewritten into clean specs by the spec-author entries here.
- notes: Foundation interoperability analysis of the legacy client. No pseudo-code recorded anywhere;
  address-tagged raw findings live ONLY under _dirty/ and are never committed. The re_provenance_logger
  hook captured per-call digests under .claude/hooks/state/.

## 2026-06-11 — asset-spec-author
- binary: doida.exe @ 63fcaf8e
- tool: none (firewall bridge — no IDA; rewrote neutral analyst notes only)
- analyzed (by canonical name): VfsHeader, VfsEntry (data.inf index + data/data.vfs blob); .xobj / .skn /
  .bnd geometry; texture container.
- specs produced/updated:
  - Docs/RE/formats/pak.md (new) — 24-byte index header (entry_count @ +12 CONFIRMED), 144-byte TOC
    record (name[100], dataOffset i64 @ +104, dataSize i64 @ +112), binary-search lookup; no compression
    or encryption on the read path.
  - Docs/RE/formats/mesh.md (new) — .xobj (ASCII static), .skn (binary skinned: 36B faces, 24B verts
    normal-before-position, 12B weights), .bnd (72B bones).
  - Docs/RE/formats/texture.md (new) — no proprietary format; raw bytes handed to D3DX; inbound is not
    JPEG (ijl is export-only); DDS/TGA likely, sample-unverified.
- notes: All field layouts derived from the parser routines only; no archive/asset sample was available,
  so the five unconsumed index dwords and the trailing TOC padding are flagged UNVERIFIED. Promotion of
  the dirty-room asset-format notes; no decompiler output or addresses crossed the firewall.

## 2026-06-11 — protocol-spec-author
- binary: doida.exe @ 63fcaf8e (analysis pinned to the dirty-room note set; no IDA this session)
- tool: none (firewall bridge — no IDA; rewrote a neutral analyst note only)
- analyzed (by canonical name): WireCipher, WireCompression, SecureHandshake
- specs produced/updated: Docs/RE/specs/crypto.md
- notes: Promoted the now-recovered wire-cipher numeric constants from the neutral dirty-room note
  into the clean crypto spec, replacing the former Recovery TODO placeholders with a pinned
  constants table and weaving the values into the algorithm so Network.Crypto can implement a
  round-tripping cipher. Pinned: round count R=3; forward sweep rotate-left 3, add the
  remaining-length counter, XOR the feedback accumulator, then rotate-right 1 with one's-complement
  plus 0x48 (equivalently 71 minus that rotated value); backward sweep rotate-left 4, add the
  counter, XOR feedback, then XOR 0x13 and rotate-right 3. Called out as load-bearing that the
  position counter is a remaining-length countdown initialized to the payload length and decremented
  per byte (8-bit), NOT the forward index, and that the feedback accumulator is a one-byte value
  reset to zero at each sweep start. Gave the algebraic inverse for decrypt. Pinned LZ4 as stock
  raw-block (no frame header/magic/checksum), acceleration 1, inbound max decompressed size 11680
  with length carried by the 8-byte header. Pinned the 1/4 handshake reply whitening: XOR key 0x29,
  selector 0x40, complement test (selector & key & 0x1F)==1 evaluating false so the key is used
  as-is; whitened span is the whole dword-aligned payload — corrected the earlier note that 0x40
  was a length (it is the selector). Pinned the handshake field layout: 0/0 server->client is a
  54-byte key blob plus two 4-byte scalars (62 bytes), blob is two 2-byte headers then [u32 len]
  [digits] twice (modulus then exponent), little-endian lengths, constraint L1+L2=42; reply uses
  PKCS#1 v1.5 block-type-2 padding with padded block = modulus_bytes-1 and body [u32 len][digits].
  Kept flagged as unresolved/capture-dependent: the exact L1/L2 split (server wire data), the bit
  meaning of the two 2-byte per-bignum headers, and whether an inbound decrypt exists (structurally
  absent, capture-unverified). Spec stays capture_verified: false. No pseudo-code or addresses copied.

## 2026-06-11 — dirty-room RE expansion wave (re-protocol / re-struct-cartographer / re-asset-format / re-crypto)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP, read-only (no IDB modification)
- analyzed (by canonical subsystem): the 105 outbound C2S build-sites (Net_SendPacket) — MoveRequest 2/13,
  UseSkill 2/52 send-site, EnterGameRequest 1/9, Auth/Login 1/4 & 1/6, chat 2/7 & 2/83 & 3/21 — plus
  expanded S2C layouts (5/53 vitals, 5/1 spawn-extended, 5/32 level-up, 4/29 stat-update, 3/1 full slot
  record); the max-HP/MP vitals formula and per-stat composition; the full 880-byte SpawnDescriptor,
  the item and skill structs; the asset UNVERIFIED fields (data.inf header dwords, LenStr width,
  .bnd bone record, texture container); and the session handshake reply construction.
- specs produced/updated (promoted by the spec-author entries that follow, all neutral, capture_verified: false):
  Docs/RE/opcodes.md (189 rows; C2S opcodes added), Docs/RE/packets/*.yaml (10 new/expanded),
  Docs/RE/structs/{stats,spawn_descriptor,item,skill}.md, Docs/RE/specs/crypto.md (§6 handshake reply),
  Docs/RE/formats/{pak,mesh,texture}.md (mesh corrections: LenStr u32, .bnd 36-byte record), names.yaml (C2S opcodes).
- notes: All findings written to the gitignored Docs/RE/_dirty/ quarantine first, then rewritten into the
  clean specs. No pseudo-code or addresses crossed the firewall. Two corrections to already-written
  Assets.Parsers code surfaced (LenStr 4-byte prefix; .bnd 36-byte on-disk record). Handshake reply build,
  cipher constants, and the stat formula are statically pinned; concrete server values (RSA n/e, L1/L2 split,
  level/server stat bases) and field semantics remain capture/catalog-dependent.

## 2026-06-11 — client-mechanics RE wave (terrain / animation / config-catalog / game-loop / input-ui / lua)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP, read-only (no IDB modification)
- analyzed (by canonical subsystem): the terrain streaming manager and its .map/.ted/.mud/.sod/.lst cell
  files; the skeletal-animation .mot clip format and the layered animation mixer; the client-side .scr
  catalogue tables (exp / userlevel / userpoint / users / items / skills / mobs) plus .ini config and the
  per-map sound tables (.wlk/.run/.bgm/.bge/.eff) under the VFS; the Win32 message-pump game loop, its
  subscriber-interval tick scheduler and timeGetTime clock; the WndProc input dispatch and UI→world
  responsibility chain with its widget tree; and the embedded Lua 5.1.2 scripting subsystem.
- specs produced/updated (all neutral, sample_verified: false unless noted):
  - Docs/RE/formats/terrain.md (new) — cell nomenclature + .map text descriptor + .ted 5-block blob
    (65x65 f32 heightmap, normals, lookup, direction map, RGBA diffuse; 46987B) + .sod collision +
    streaming policy (1024x1024 cells, origin bias 10000, quality rings 5x5/3x3, background FIFO).
  - Docs/RE/formats/animation.md (new) — .mot binary clip (header {id_a,id_b,LenStr name,frame_count},
    tracks of 28-byte keyframes = f32[3] translation + f32[4] quaternion XYZW, 10 fps fixed, Lin/SLERP),
    bone_id linkage to .bnd self_id, normalized weighted-average mixer (action/cycle lists).
  - Docs/RE/formats/config_tables.md (new) — WAVE-7 BLOCKER RESOLVED: stat curves and catalogues are
    CLIENT-SIDE in VFS data/script/*.scr (exp 20B, userlevel 60B, userpoint 32B, users 496B block,
    items 548B+N*8, skills 1504B+N*8, mobs 488B); no compression; field internals beyond confirmed
    offsets UNVERIFIED. Plus .ini ([DO_OPTION]).
  - Docs/RE/formats/sound_tables.md (new) — five per-map extensions sharing one 256x48B layout; .xeff
    (magic "XEFF") flagged as the separate visual-effects format.
  - Docs/RE/specs/game_loop.md (new) — message-pump→render→tick loop, subscriber-interval scheduler
    (interval_ms/last_tick_ms threshold, no accumulator), timeGetTime ms clock with optional time-scale;
    documents the intentional .NET divergence to a fixed PeriodicTimer tick with Godot interpolating.
  - Docs/RE/specs/input_ui.md (new) — WndProc dispatch (IME-first, key filters, mouse capture), 20-byte
    normalized mouse event ring-buffer, UI→world responsibility chain, widget-tree offset table, 5 view modes.
  - Docs/RE/specs/lua_scripting.md (new) — Lua 5.1.2 (banner-confirmed) + lua_tinker binding, minimal native
    surface (cpp_load global + stdlib), .lua = config/localization/UI layout loaded plaintext from data/script/;
    "ANIC" demystified as standard "PANIC" misread; interpreter-vs-direct-parse tradeoff left for checkpoint.
  - Docs/RE/names.yaml (client-mechanics block: file extensions, Lua version, loop/input concepts).
- notes: All findings written to the gitignored Docs/RE/_dirty/ quarantine first (terrain.raw.md,
  animation.raw.md, config_tables.raw.md, recon/game_loop.raw.md, structs/input_ui.raw.md,
  recon/lua_scripting.raw.md), then rewritten into the clean specs. No pseudo-code or addresses crossed the
  firewall. Key outcome: the wave-7 "stat curves are server-side, not extractable" gap is overturned — the
  curves live in client .scr files and become recoverable once a VFS sample is provided. The Lua VM version
  is confirmed (banner); all asset field internals and .lua roles stay sample-dependent. Journal authored
  centrally by the orchestrator (spec-authors were barred from journal.md/names.yaml to avoid the
  parallel-write clobber observed in an earlier wave).

## 2026-06-11 — real-asset RE wave (26 analysts / 12 spec-authors, sample-verified against the real VFS)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP, read-only — used sparingly to disambiguate;
  PRIMARY evidence this wave was real sample bytes (the user supplied the real client at D:\MartialHeroesClient).
- inputs: the real archive (data.inf 6,241,992 B index + data/data.vfs 3.8 GB) was parsed by our own
  MappedVfsArchive — 43,347 entries, 100.0% coverage (declared == vfs within 24 B), 0 out-of-bounds, 0 sort
  violations, high-dword always 0 → pak.md byte-confirmed. 146 representative samples extracted (gitignored
  Docs/RE/_dirty/samples/) for hexdump analysis; full TOC manifest under _dirty/assets/ (never committed).
- analyzed (by canonical format): every asset extension across the 49 present. Validated existing specs against
  real bytes (terrain .ted/.map/.mud/.sod, animation .mot, mesh .skn/.bnd/.xobj, catalogues .scr, textures
  .dds/.tga, sound tables) and DISCOVERED previously-unspecified formats (.bud cell geometry, .xeff visual
  effects, .fx1-7/.up/.exd/.pre/.post/.bin terrain sidecars, .psh/.vsh shaders, .arr NPC spawns, items.csv,
  .do/.xdb/.mi/.tol misc data).
- specs produced/updated (all promoted neutral; sample_verified flipped to true where confirmed against bytes):
  - formats/terrain.md (UPDATED) — .ted 5-block fully confirmed (no header; heights = direct world Y; normals
    signed-byte/127; 16x16 texture-index + direction grids; diffuse x0.5 runtime); .mud CRACKED (64x64 grid of
    8-byte per-tile ambient-sound records); .sod variable-length records; DATAFILE->.bud linkage fixed.
  - formats/terrain_scene.md (NEW) — .bud: u32 objectCount, per object {u8 type, u32 texId, u32 vertexCount,
    32-byte vertices (XYZ f32 + 5 unknown f32), u32 indexCount, u16 indices}.
  - formats/terrain_layers.md (NEW) — .fx1-7 (per-index vertex strides 32/36/44 B), .up/.exd (u32 count +
    40-byte triangle records), .sod.pre/.ted.post precomputed sidecars, light*.bin.
  - formats/animation.md, mesh.md, config_tables.md, texture.md, sound_tables.md (UPDATED — real-byte verified;
    config_tables now carries the real .scr column semantics + the items.csv ~100-column schema; texture adds
    .bmp/.png as standard directly-usable containers).
  - formats/effects.md (NEW, .xeff/.eff), shaders.md (NEW, D3D9 .psh/.vsh), npc_spawns.md (NEW, .arr),
    misc_data.md (NEW, .xdb/.mi/.tol/.ion/.sc).
  - formats/pak.md (UPDATED by orchestrator) — promoted to CONFIRMED with the 43,347-entry/100%-coverage proof.
  - names.yaml (orchestrator) — asset_formats + constants blocks for the new extensions.
- notes: All raw findings went to gitignored Docs/RE/_dirty/formats/*.raw.md first, then were rewritten into the
  clean specs by no-IDA spec-authors. Orchestrator firewall pass removed leaked Hex-Rays locals (v3/v5/v6 ->
  field_NN) and analyst function-symbol names from the clean specs; final scan is clean (no addresses, no
  sub_/loc_/dword_, no pseudo-code, no standalone "IDA" editorializing). All game text is CP949/EUC-KR. The
  wave-7/8 "stat curves not extractable" gap is now fully resolved: the curves are client-side .scr + items.csv
  and their real column layouts are documented. Journal + names.yaml authored centrally by the orchestrator.

## 2026-06-12 — gameplay-logic RE wave (10 analysts / 8 spec-authors)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP, read-only — IDA was the primary evidence here
  (engine/gameplay logic, no asset samples).
- analyzed (by canonical subsystem): combat damage/stat model; the stat-aggregation & equipment pipeline; the
  skill cast/effect system; inventory/equip/trade/shop/enchant; the camera and its five view modes; client
  movement & terrain collision; the still-undocumented S2C/C2S opcode handlers; the login/character-select/
  enter-game flow; chat & social (party/guild/friend); and quest/NPC interaction.
- specs produced (all NEW, neutral, promoted by no-IDA spec-authors):
  - specs/combat.md — client is server-authoritative for HP deltas; it computes a full derived combat-stat
    model locally. Pinned formula shapes + coefficients (e.g. attack_base = (STR*2.5 + DEX*2.0 + AGI*2.3 +
    CON*1.0 + INT*1.0) * 0.2; a parallel secondary base), the PvE/PvP rate split, the weapon-proficiency hit
    penalty tiers, the buff/equip/set contribution model, and stat-id enumeration.
  - specs/skills.md — cast pipeline (target/range/cost/cooldown), effect dispatch, buff/debuff model.
  - specs/inventory_trade.md — inventory grid, equip rules, trade state machine, shop, enchant (+N).
  - specs/camera_movement.md — five view modes; camera clamps; click-to-move; collision against .sod / .ted.
  - specs/handlers.md — behavior catalogue for opcode handlers not yet covered by opcodes.md/packets.
  - specs/login_flow.md — login -> char-select -> enter-game; boundary with the lobby/online server processes.
  - specs/social.md — chat channels, whisper, party, guild, friend/block; membership state.
  - specs/quests.md — quest accept/progress/complete, NPC dialog, Lua/npc.arr linkage, rewards.
- notes: All raw findings (which DO contain addresses and decompiler locals) stayed in the gitignored
  Docs/RE/_dirty/recon/*.raw.md quarantine. The clean specs were rewritten with neutral formula/behavior prose;
  orchestrator firewall scan confirmed ZERO leaked addresses / sub_/loc_/dword_ / handler-symbol names in the
  committed specs (the only `0x..` tokens are packet field offsets and the 0xFFFFFFFF sentinel — format facts).
  Combat coefficients are bit-exact from the binary but final damage combination is server-authoritative and
  cannot be confirmed from the client alone (flagged in combat.md). Journal + names authored centrally.

## 2026-06-12 — RE deepening wave (22 analysts / 12 spec-authors): unverified-field elimination + runtime subsystems
- binary: doida.exe @ 63fcaf8e, IDA Pro 9.3 via MCP (read-only) + real sample bytes (Docs/RE/_dirty/samples/).
- analyzed (by canonical target): .bud vertex format + objects; .xeff element/emitter/keyframe fields + effects
  runtime; terrain internals (.mud 8-byte tile record, .sod SolidRecord/CollisionQuad, .ted five blocks); .mot
  runtime (AnimationMixer two-list blend, sync clock, actormotion.txt 33-col); .scr record-body columns + the
  ~140-col items.csv; full Actor / item-instance / skill / inventory / NPC structs; the complete opcode dispatch
  sweep + tightened packet layouts; the login/lobby/world-entry protocol; and the client runtime subsystems
  (sound, UI widget tree + Lua binding, Diamond render pipeline + shaders, camera constants, movement/collision,
  quests).
- specs updated: formats/terrain.md (.mud/.sod/.ted internals confirmed), terrain_scene.md (.bud vertex = pos +
  unit normal + tiled UV, CCW, tex_id 1-based), effects.md (.xeff keyframe = index+velocity Vec3+size Vec3+quat;
  emitter types 0/1/2; alpha inversion), animation.md (mixer runtime), config_tables.md (resolved .scr column
  offsets + items.csv columns), specs/handlers.md (opcode-sweep completion + tighter layouts), specs/login_flow.md
  (full server-list/channel/char-select/enter-game), structs/{actor,item,skill}.md (full field tables).
- specs created: specs/client_runtime.md (sound / UI / render pipeline / camera / movement / quests), structs/npc.md
  (NPC/mob struct + npc.arr spawn record + mobs.scr linkage).
- notes: Raw findings (with addresses + decompiler locals) stayed in gitignored _dirty/. Orchestrator firewall
  scan of the committed specs is clean: no addresses, no sub_/loc_/dword_ identifiers, no pseudo-code — the only
  0x.. tokens are data constants (class flags 0x000N0000, sentinels 0x7FFFFFFF, the XEFF magic 0x46464558, the
  1 MiB audio ring 0x100000) and the "sub_command_id"/"sub_level_byte" semantic field names (word prefix, not a
  decompiler symbol). Several dirty claims were DOWNGRADED on promotion where samples contradicted them (e.g.
  skill +1072 is not a reliable constant). Still-open items are flagged per spec (mostly fields with no observed
  non-zero sample). Journal + names authored centrally by the orchestrator.

## 2026-06-12 — render-and-UI campaign, evidence wave (7 analysts / 5 spec-authors)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP (read-only) + real VFS sample observation.
  The user-supplied VFS now lives PROJECT-LOCAL at 05.Presentation/MartialHeroes.Client.Godot/clientdata/
  (data.inf + data/data.vfs, byte-verified copy, triple-gitignored) — analysts read it through the
  Assets.Vfs harness from there.
- analyzed (by canonical subsystem): the complete Skinning/animation runtime math (bind-pose accumulation,
  load-time inverse-bind bake, LBS deform, .mot sampling/composition, quaternion+handedness conventions);
  a full-corpus animation census (3,891 .mot / 349 .bnd / 2,786 .skn — the prior "all stubs" reading was
  sampling bias; canonical multi-bone test specimens identified; an 11-file BANI .mot variant discovered);
  the UiSystem widget toolkit + hardcoded login/char-select layouts + per-screen asset manifests + the
  data/ui census (uitex.txt / skillicon.txt / crestlist.txt manifests); the per-area Environment .bin
  family (map_option/fog/light/material/stardome/clouddome/cloud_cycle) incl. water enable+Y placement and
  the 48-keyframe day/night cycle; bgtexture.lst true record layout; .bud vertex completion (normal+UV,
  no lightmaps); and CameraMovement/SceneLifecycle numeric parameters + the 9-state engine machine.
- specs produced (promoted by no-IDA spec-authors; disjoint files):
  - specs/skinning.md (NEW) — implementable skinning+animation pipeline incl. Godot import guidance and
    canonical test trios. specs/ui_system.md (NEW) — widget model, screen layout tables, fonts, scene
    machine, reconstruction guidance. specs/environment.md (NEW) — per-area env assembly, day/night
    sampling, water placement rule (renderer flagged unrecovered). formats/environment_bins.md (NEW),
    formats/ui_manifests.md (NEW).
  - formats/animation.md, mesh.md (census + BANI variant + bone-ID addressing), texture.md (bgtexture.lst
    48-byte records — supersedes the 76B claim), terrain_scene.md (.bud bytes 12–31 resolved; light*.bin
    are NOT lightmaps), specs/camera_movement.md + client_runtime.md (CODE-CONFIRMED camera parameters,
    fixed-radius orbit model, event-camera correction, 9-state lifecycle) — all UPDATED.
  - names.yaml (orchestrator) — Skinning/UiSystem/Environment/SceneLifecycle mechanics entries, corrected
    ".bin" extension entry, new asset constants (BANI magic, bgtexture record size, .bud FVF, sky keyframe
    timing, UI canvas, camera projection).
- notes: All raw findings (with addresses) stayed in gitignored Docs/RE/_dirty/ (anim/, formats/, recon/,
  queries/, tools/). Orchestrator firewall scan of all 11 touched committed specs: CLEAN (no addresses, no
  sub_/loc_/dword_, no pseudo-code). Known conflicts recorded rather than reconciled: bgtexture.lst 76B->48B
  correction is explicit in texture.md; the animation.md "no magic" claim corrected to standard-vs-BANI
  duality. A follow-up dirty-room pass (msg.xdb format, water renderer, BANI loader branch, UI-manifest
  parsers) is in flight; its promotions will be journaled separately. Journal + names authored centrally
  by the orchestrator.

## 2026-06-12 — render-and-UI campaign, follow-up pass (1 analyst / 1 spec-author)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP (read-only) + project-local VFS samples.
- analyzed: the msg.xdb UI message catalogue loader (flat 516-byte records: u32 id + u8[512] CP949;
  count = filesize/516; id-keyed ordered-map lookup); a definitive water-renderer hunt (NEGATIVE —
  the original client renders no water plane; water_y feeds only spawn/terrain-init; OPTION_WATER has
  no runtime reader); the BANI .mot variant header (confirmed on samples; the standard loader performs
  no magic sniffing, so the 11 BANI files are non-loadable dead data); and the uitex.txt /
  skillicon.txt manifest parsers (braced-block grammar PARSER-CONFIRMED).
- specs updated: formats/misc_data.md (NEW msg.xdb section), specs/environment.md +
  formats/environment_bins.md (water renderer RESOLVED-NEGATIVE + reimplementation freedom note),
  formats/animation.md (BANI header SAMPLE-VERIFIED + loader-rejection conclusion),
  formats/ui_manifests.md (grammar upgraded to PARSER-CONFIRMED; MSK still PROPOSED),
  specs/ui_system.md (msg.xdb open item resolved by pointer).
- notes: raw findings stayed in gitignored _dirty/formats/. Orchestrator firewall scan found ONE leaked
  decompiler identifier in ui_manifests.md (a sub_ token in an open-questions line) — rewritten
  neutrally by the orchestrator; rescan CLEAN. All six touched specs otherwise clean (no addresses,
  no pseudo-code). Journal + names authored centrally by the orchestrator.

## 2026-06-12/13 — Cycle 2: client-workflow comprehension campaign, evidence wave (19 lanes + 1 follow-up sweep)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 via MCP (read-only, IDAPython sweeps) +
  project-local VFS (clientdata/, 43,347 entries) read via Assets.Vfs harnesses + vfsls.
- analyzed (14 IDA lanes, sub-waves of 3): boot/init timeline (game.lua bootstrap, DoOption.ini 30-key
  map, window/D3D9 init, VFS mount, singleton tiers); main frame loop (3-step anatomy, uncapped
  IMMEDIATE present, winmm clock, day/night tick in render pass); exhaustive 9-state scene machine
  (full state×trigger transition table); login scene (action-id dispatch, validation, msg ids,
  sub-states 2..41); server-select (lobby protocol port 10000, 8-byte records, list.dat layout,
  channel endpoint); char-select (5 sends incl. unresolved 1/6 login-vs-create collision, 880B
  descriptors, 3D preview stage); world scene (17-step entry, dual 4/1+4/3 materialize, 6-callback
  render order); effects runtime (XEffect family, pools, manifests, trigger ids, tick math); GU*
  toolkit internals (sprite path, state frames, IME, fonts, z-order); sound (DirectSound + static
  Vorbis, volume curve, ambient driver, footstep source = actor-visual fields); network lifecycle
  (3 threads, bus dispatch, keepalive 2/10000, persistent game socket); resource pipeline (no file
  cache, boot bulk loader, 3×3 sync ring + streamer); module cartography (25,973 fns classified,
  engine "Diamond", MSVC2005/Lua 5.1.2/LZ4/XTrap); 19 runtime singletons. Follow-up sweep:
  pixel-exact widget atlas rects (login 21 sites ~100%, char-select 77 sites ~100%, 117 HUD builders;
  multi-state button ctors yield 3 distinct frames; login form on login_slice1.dds; char-select
  action ids Create=4/Delete=5/Enter=6).
- analyzed (5 VFS lanes, sample-only): .xeff/.fx*/particle censuses (xeff layout byte-verified);
  audio census (2,107 .ogg, 2d/3d split); full UI asset census (uitex/skillicon/crestlist/msg.xdb
  2,644 records); serverlist/config hunt (clean negative — list is network-fetched; do.ini encrypted);
  63-area per-file inventory (~2,505 cells, gap analysis).
- notes: all raw findings (addresses included) stayed in gitignored Docs/RE/_dirty/workflow/ + _dirty/queries/.

## 2026-06-12/13 — Cycle 2: promotion wave (10 satellite authors + 4 recovery + master synthesis)
- specs NEW: specs/client_workflow.md (MASTER end-to-end workflow: flow diagram, 4 scene chapters,
  9 module chapters, interconnection matrix, engine identity, open-questions register);
  specs/effects.md; specs/sound.md; specs/frontend_scenes.md; specs/resource_pipeline.md;
  formats/area_inventory.md; structs/runtime_singletons.md.
- specs UPDATED: opcodes.md (+Appendix A lobby protocol; 193 rows validator-clean) + 8 new
  packets/*.yaml (1-6_login_or_create collision doc, 1-7 select, 1-13 rename, 1-14 delete,
  2-10000 keepalive, 3-4 char_manage_result, 4-1 game_state_tick, lobby); specs/client_runtime.md
  (§7 state machine, §8 frame loop, §9 world scene); specs/ui_system.md (toolkit internals, 3-frame
  button correction, actionId +0x10 correction, login_slice1.dds atlas correction, corrected font
  Height/Width table, full login/char-select widget tables, HUD uitex-binding contract, sub-state
  29/31 corrections); specs/frontend_scenes.md (action-id corrections); formats/effects.md (xeff
  census + layout); formats/sound_tables.md (per-extension 2d/3d split); formats/ui_manifests.md
  (DXT2 dominant fourCC census + full DDS reference tables); formats/misc_data.md (msg.xdb
  SAMPLE-VERIFIED: 2,644 records, 0xEE fill, id-range groups).
- notes: three first-pass authors died on API socket errors and were re-run in a recovery wave; a
  stray helper script left in specs/ was removed by the orchestrator. Orchestrator firewall scan of
  all 22 touched committed files: CLEAN (5 regex hits = false positives: the sub_effect_count field
  name and client_workflow.md's own disclaimer line). Known cross-spec conflicts recorded, not
  silently resolved: 1/6 opcode collision (login vs create — needs capture), fx2 header field[3]
  15-vs-50, fame_buff_window.dds 1024x512-vs-1024x2048, 4/1-vs-4/3 ordering. Journal + names
  authored centrally by the orchestrator. Tooling: vfsls gained 8 census subcommands (smoke-tested).

## 2026-06-13 — Cycle 2: icon-chain recovery + promotion (2 IDA lanes / 1 VFS lane / 2 spec passes)
- binary: doida.exe @ 63fcaf8e via IDA MCP (read-only) + project-local VFS harness observation.
- analyzed: the skill/item icon rendering chain end-to-end. Skill icons: NO modular grid — each
  skill stores a 16-bit (iconSrcX, iconSrcY) pair, blitted as a fixed 23×23 cell from the 512×512
  (job,kind) sheet selected by skillicon.txt; confirmed at three draw sites. The ON-DISK source is
  the 12 per-class stance .do files (116-byte records, icon pair at +0x18/+0x1C) — proven by a
  field-write trace AND a sample harness (musajung.do = 34,916 B = exactly 301×116; a full
  750-offset u16-pair scan of skills.scr was NEGATIVE; the +546/+548 "static record" path is
  skillcategory.scr category banners, a secondary path). Item icons: texturelist.txt is a flat
  newline list (leading numeric prefix = tex_id; 1,335 entries, all present in the VFS); one whole
  DDS per icon, no atlas sub-rects.
- specs updated: formats/ui_manifests.md (§2.6 corrected source + §2.7 NEW .do record layout +
  §8.2 load chain rewritten + §9 items #1/#8 closed, #11 rewritten, #12/#13 added + §10 NEW
  texturelist.txt grammar + §11 cross-refs); formats/config_tables.md (stance .do stride corrected
  166→116, SAMPLE-VERIFIED 12/12 by the orchestrator from file-size arithmetic).
- notes: raw findings in gitignored _dirty/workflow/ (icon-grids, skill-icon-data,
  icon-source-trace). The first promotion pass predated the source trace and briefly attributed
  the pair to skills.scr; corrected the same day by a follow-up pass — both passes journaled here.
  Firewall scans CLEAN. Journal + names authored centrally by the orchestrator.

## 2026-06-13 — Cycle 2: W3/W5 engineering record (not RE — provenance pointer only)
- The UI/GUI engineering wave (widgets kit, UiCatalogs, login/char-select/HUD fidelity, audio,
  SoundTableParser) implements ONLY committed clean specs (ui_system.md §8, frontend_scenes.md,
  sound_tables.md, ui_manifests.md, misc_data.md §6, names.yaml constants); no engineer read
  _dirty/ or IDA. Review wave + fix wave closed all confirmed findings; build 0/0, 1,066 tests.

## 2026-06-13 — Cycle 2: pre-commit gate catches (orchestrator fixes)
- The clean-room-auditor + preservation-archivist pre-commit pass caught TWO leaks in the staged
  set, both fixed by the orchestrator before commit: (1) a header comment in the committed
  skillcat-scan harness (.claude/skills/vfs-inspect/scripts/skillcat-scan/Program.cs) carried
  four raw decompiler autonames pasted from a dirty note — rewritten neutrally with a spec
  citation (ui_manifests.md §2.7); (2) structs/runtime_singletons.md quoted a verbatim mangled
  MSVC RTTI symbol as engine-brand evidence — neutralized to a prose description (class
  CVFSManager in a Diamond C++ namespace). Re-scan after both fixes: CLEAN. The two RE-probe
  scripts under vfs-inspect (skillcat-scan, skill-icon-scan) received spec-citation headers as
  recommended. Documented per the audit-trail discipline; both gate agents' findings preserved
  in their reports.

## 2026-06-13 — Cycle 3 W1: World-Scene gameplay-systems dirty-room research (20 lanes)
- scope: a 20-lane GIGA research wave over the WORLD scene — combat, chat, NPC interaction/shops,
  quests, party/trade, minimap/world-map, in-game windows, buff/state icons, .do records,
  equipment visuals, skill->effect cast chain, floating text/target frame, do.ini "crypto",
  drop/pickup, exp/level-up (15 IDA lanes in 5 sub-waves of 3 against the single doida.exe IDB) +
  5 harness-only VFS lanes (do-census, minimap-assets, window-art-census, quest-dialog-data,
  fx-asset-links). All 20 delivered high-confidence.
- output: raw notes in gitignored `_dirty/world/*.md` ONLY (tainted; never shipped). No IDA address,
  autoname, or pseudo-C left the dirty room.
- headline facts (full detail in the promoted specs below): basic melee IS C2S 2/52 with skill-slot
  0xFF (no separate attack opcode); ALL everyday chat is C2S 2/7 with first payload byte = channel
  code; storage 2/142 / shop 2/115 / sell 2/20 / repair 2/113 / interact 2/19 (no teleport opcode);
  30-slot buff bar driven by S2C 4/102 with icon positions in data/script/buff_icon_position.xdb
  (12B records); .do stance record is 116B with icon srcX/srcY at +0x18/+0x1C read as u32 (corrects
  an earlier i16 read; resolves the 72-vs-76 tail discrepancy = 76B overlay-sprite block);
  DoOption.ini + .do/.scr data are PLAINTEXT (the only obfuscation is FILE_ATTRIBUTE_HIDDEN — the
  prior "do.ini ships encrypted" open item is RESOLVED=plaintext); FX field[3] is VARIABLE per group
  (the earlier "constant 15" committed claim was WRONG); NO baked minimap tiles exist in the VFS.

## 2026-06-13 — Cycle 3 W2: promotion of the 20 world lanes into committed clean specs (21 authors + master)
- crossing: 21 spec-author agents (each owning ONE committed file — zero write contention) REWROTE
  the `_dirty/world/` notes into neutral committed specs; a 22nd agent synthesised the master
  `specs/world_systems.md` reading ONLY the freshly-cleaned specs (clean by construction).
- written/updated: specs/combat.md (UPD), specs/chat.md (NEW), specs/social.md (UPD), specs/
  inventory_trade.md (UPD), specs/npc_interaction.md (NEW), specs/quests.md (UPD), specs/minimap.md
  (NEW), specs/progression.md (NEW), specs/equipment_visuals.md (NEW), specs/ui_system.md (UPD),
  specs/effects.md (UPD), specs/world_systems.md (NEW master); formats/effects.md (UPD), formats/
  config_tables.md (UPD), formats/misc_data.md (UPD), formats/ui_manifests.md (UPD); opcodes.md (UPD)
  + ~35 new packets/*.yaml (npc/trade/party/guild/quest/progression/buff/drop families) and the
  2-7 relabel (CmsgWhisper -> CmsgChat).
- key corrections promoted: combat 2/52-0xFF melee RESOLVED (was UNVERIFIED #7); chat reclassified
  (2/82/83/84/3/21 are friend-note/announce/relation, NOT say-chat); trade 2/23-25 fully decoded
  (2/25 is the confirm manifest, not a flat 2-byte packet); 4/23 is a phase machine on byte +10;
  social 5/21 affected-member is a party-slot index (not actor id); ui_system StatusPanel/SkillPanel
  mislabels fixed; ui_manifests 22 DDS dimension/format corrections (many "1024^2 DXT3" are really
  512^2 ARGB32); .do icon offsets u32; FX field[3] variable.
- ORCHESTRATOR firewall scan over all committed specs/formats/packets/opcodes (excluding _dirty/):
  CLEAN. Zero autonames (sub_/loc_/dword_/byte_), zero image-range VAs in the new files (only hit was
  the documented imagebase 0x400000 in journal/names/audits, and sample DATA values like 0x46464558
  / 0x7FFFFFFF / 0x64000007 which are interoperability facts, not addresses).
- ORCHESTRATOR consistency fixes (post-promotion): the opcodes-agent had added the 2/14/2/15/2/28/2/29
  C2S rows but omitted 16 sibling C2S rows whose packet YAMLs existed — added them in sorted position
  (2/19,2/20,2/23,2/24,2/25,2/30,2/35,2/36,2/37,2/100,2/110,2/113,2/115,2/142,2/143,2/151-153).
  names.yaml merged centrally: 2/7 renamed CmsgChat, 22 new world C2S opcodes, 6 new gameplay/format
  subsystem entries (WorldSystems/Chat/NpcInteraction/Minimap/Progression/EquipmentVisuals/BuffState),
  16 new asset/runtime constants (buff_icon_position 12B, mapsetting 84B, regiontable 32B, quests.scr
  3720B, npc.scr 404B, minimap dot scale, 4/102 476B, coin/fallback-model ids, melee slot 0xFF).
- NOTE on S2C: most world S2C opcodes (4/14, 4/15, 4/23, 4/36, 4/102, 5/9/11/14/15/21/32/52/53/67/68/73)
  were ALREADY in the catalog from the Cycle-2 dispatch-table sweep; the canonical names were kept and
  the new packet YAMLs link to them. The Cycle-3 net-new contribution is the C2S send-site layer.
- harness/tooling (Phase C3-T, ran parallel): vfsls gained dump-do / scan-minimap / scan-quest (now 12
  subcommands); firewall-clean (orchestrator pre-audit PASS). Journal + names authored centrally.

## 2026-06-13 — Cycle 4 W1: live-debugger login capture + PIN spec promotion (FIRST dynamic-analysis session)
- binary: doida.exe @ 63fcaf8e (x86 32-bit), IDA Pro 9.3 DEBUGGER via MCP (mcp__ida__dbg_*). The
  maintainer armed the local Windows debugger in the IDA GUI (F9 + trust-dialog accept) and launched the
  live client; the orchestrator PILOTED the already-running session (breakpoints, register/memory reads —
  the debugger reads even through PAGE_NOACCESS). `dbg_start` is unusable via MCP (cannot dismiss the
  modal trust dialog, and a session is already active). Game servers are dead, so live driving covers
  build-time (pre-send) packet assembly and VFS-read paths only.
- analyzed (by canonical name, DYNAMIC): drove the live client through account + password + second-password.
  AuthSession_BuildLoginPacket43 (the login-blob builder) and its caller NetClient_RebuildSecureContext
  (= the login_flow.md §4.1 secure-context builder); PacketBuf_Write{U8,LenPrefixedBytes} + the PACKETBUF
  object; Net_EncryptOutboundPacket / Cipher_XorRolEncrypt (framing rule re-confirmed against ground truth —
  8-byte header plaintext, payload XOR/ROL-encrypted; cipher already recovered & tested, 15/15); DName::isPin.
- specs produced/updated:
  - Docs/RE/specs/login_flow.md (UPD) — identified the previously-unnamed "optional auxiliary string" of the
    1/6 login blob as the SECOND-PASSWORD / PIN; added the runtime-confirmed capacities (account < 20,
    password < 17 staged in an exactly-17-byte zero-padded RSA-plaintext buffer, PIN < 5), the u32-LE
    NUL-inclusive length prefix, the second-password step in the §1 flow, and a packet-framing §4.4 citing
    crypto.md. The 1/6 login-vs-create collision is left OPEN (the live read reached login only).
  - Docs/RE/specs/frontend_scenes.md (UPD) — added the §1.4a second-password / PIN modal (shown after the
    primary login submit; ≤4-digit PIN → the optional login-blob field; data/ui/password.dds asset).
- notes: runtime evidence (with addresses + live bytes) stayed in the gitignored
  Docs/RE/_dirty/workflow/login-packet.dyn.md; the in-game credentials were NEVER transcribed anywhere. The
  promotion was done by a no-IDA protocol-spec-author who grep-verified both touched specs CLEAN (no
  addresses, no autonames, no pseudo-code, no credentials — only within-packet offsets/sizes and length-
  example constants); a full clean-room-audit gate will run before any commit. Strategic finding (not a
  spec): the workflow specs are already comprehensive, so Cycle 4 pivots to front-end implementation + VFS
  tooling. Tooling (parallel): vfsls gained decode/extract/convert/hexdump/coverage (auto-detect registry,
  28 formats; build 0/0); a pre-existing DDS dwFourCC off-by-4 in Assets.Mapping/PngConverter was found and
  is being fixed. Journal authored centrally by the orchestrator.

## 2026-06-13 — Campaign 2: IDB comprehension and annotation run (5 clusters, WRITE to IDB)
- binary: doida.exe @ 63fcaf8e81a61097c68d22ae82514dded54e59c41c480850a568a0f0d79eb9df (x86 32-bit)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*), static analysis + IDB annotation (WRITE — names and
  comments set in the IDB; no execution, no debugger)
- analyzed (by canonical cluster):
  - network-dispatch: NetHandler_DispatchGamePacket (central opcode router), NetClient recv engine
    and thread workers, NetPacketDeque queue internals, SkillActionList/Ring container for 5/52
    records, Actor_FindByPresenceAndTag, GameState_GetSingleton, Scene_QuitDispatcher
  - crypto-session: Cipher_XorRolEncrypt (XOR/ROL payload cipher), LZ4_compress_default /
    LZ4_decompress_safe, Bignum_ModExp / Rsa_PadAndModExp / Secure_BuildSecureAuthReply
    (0/0 bignum handshake), Secure_EncryptCredentialReply, PacketBuf_WhitenPayloadDwords
  - vfs-assetio: VfsOpenRouter_ThreeWay, DiskFile_ReadBytes_Impl, GHTex_GetBoundOrLoad /
    GHTex_LoadFromDisk (named-texture cache), BulkAssetLoader_Thread, Terrain_StreamWorkerThread,
    VFS_OpenArchive / VFS_FindEntry / VFS_ReadEntryData
  - scene-machine: WinMain (program entry + master scene state machine), Engine_MainLoop /
    Engine_FrameStep, Diamond_LoginWindow_BuildScene, Diamond_SelectWindow_BuildScene,
    Diamond_MainHandler_BuildGameWorld, Diamond_LoginSecondPassword_BuildKeypad,
    MessageDB_GetString, GameState_Init, DiamondEventScheduler_Subscribe /
    DiamondEventScheduler_TickAll, Scene_LogoutFromInGame / Scene_QuitFromInGame
  - effects-render: AnimMixer_BuildPose, Skin_DeformLBS, BindPose_ParseBoneRecord,
    Pose_WorldWalk, XEffect_tickAndDispatch, EffectSystem_TickAllPerFrame,
    EFF_LoadParticleEmitter, Renderer_DrawScene / Renderer_DrawScene_OffscreenRT,
    Diamond_GCullPipeline_dispatchAndDraw, ShadowManager_StampGroundCells,
    CoreXEffect_LazyParseXeff, GParticleBuffer_fillVertices
- counts: 361 functions renamed + 19 globals renamed + 380 neutral comments set in the IDB;
  live sub_ census moved 21,278 -> 21,076 (202 autonames resorbed into named entries); 0 failures
- committed artifacts produced:
  - Docs/RE/names.yaml — functions: and globals: maps first-populated (380 entries total);
    all keys are hex addresses as strings; all values are neutral canonical role names with
    cluster tags; no pseudo-code, no decompiler locals, no raw autonames
  - Docs/PLAN-CAMPAGNE2.md — campaign method document (5-cluster comprehension plan,
    annotation protocol, firewall rules)
  - Docs/ROADMAP-CAMPAGNE2.md — run record (per-cluster counts, session log, open items)
  - .claude/agents/re-comprehension-orchestrator (new agent)
  - .claude/agents/re-annotation-orchestrator (new agent)
  - .claude/agents/re-ida-annotator (new agent)
  - .claude/skills/ida-annotate-batch (new skill)
- notes: The campaign purpose was comprehension and IDB legibility — making the IDB self-describing
  so future RE waves can navigate by role name rather than autoname. All annotations are neutral
  functional descriptions; no pseudo-code, decompiler syntax, or raw addresses appear in any
  committed file. Spec refinements identified during comprehension (client_workflow.md §4.4
  disconnect routing, formats/effects.md §E.2 particleEmitter.eff variable-length record,
  crypto.md §6.5 placeholder-seed note) were noted but NOT applied to committed specs — they are
  queued as a future spec-author task and must be journaled separately when promoted. Several
  OQ-EFX-* touchpoints were incidentally resolved during the effects-render cluster walk. The
  dirty room received no new tainted material this session (annotations write only to the IDB,
  not to _dirty/). Journal authored by the preservation-archivist.

## 2026-06-13/14 — CAMPAIGN 3: doida.exe Workflow / UI-UX / VFS — deep reverse → clean specs → client

- scope: continuation of the doida.exe reverse, end-to-end. Six comprehension clusters recovered
  (READONLY, static IDA on the Campaign-2-annotated IDB; one targeted `define_func` over the in-game
  HUD-build routine, no other IDB writes), promoted to committed clean specs, then wired into the C#
  core + Godot client. One live-debugger confirmation pass on the login path. Apparatus: PLAN.md +
  ROADMAP.md (this campaign's method + run record, which replaced the prior ROADMAP/​*-CAMPAGNE2 docs).
- comprehension (Docs/RE/_dirty/campaign3/, gitignored — never committed):
  - B1 workflow-spine: the login credential is payload sub-opcode 0x2B carried on the secure 1/4 frame
    (the earlier "1/6 collision" was a false premise — 1/6 is char-create only); the CharacterMgmt
    request family (1/0,1/6,1/7,1/9,1/13,1/14); the boot→login→server-list→PIN→char-select→enter scene
    sub-state flow; the anti-keylogger PIN keypad.
  - B2 ui-window-manager/HUD: the main window IS the window manager (flat service-slot table); the
    GUComponent/GUPanel/GUWindow field model; the in-game HUD layout — first 5 panels, then the FULL
    sweep of the defined HUD-build routine (152 placement sites, 4 anchor conventions); the char-select
    6-keyframe preview camera.
  - B3 vfs-assetio: the 144-byte TOC + RAW/uncompressed storage verdict + three-way open-mode dispatch;
    the actormotion 136-byte record (col3–14 resolved); the sky/fog/cloud/star formats + day-cycle.
  - B4 lua-config: one statically-linked Lua 5.1.2 VM (lone cpp_load binding); the vfsmode/launcher/
    debugmode integer boot flags; LOAD-BEARING — the Lua text tables decode as UTF-8, not CP949.
  - B5 sound + combat-timers: OGG play-by-kind dispatch; the one-now-ms-per-frame tick spine fanned to
    four linear active-list managers (not a priority queue); the death FSM.
  - B6 terrain-stream: LOAD-BEARING — streaming is synchronous per-frame ring-shift; the async worker/
    FIFO is dormant compiled-in scaffolding.
- live-debugger confirmation (login path, against the running client; values session-only, never
  recorded): the 0x2B plaintext field layout byte-exact; the fixed 17-byte zero-padded RSA password M;
  the __thiscall builder signature; the account\tpassword\tPIN\thost:port login-string contract; the
  secure 1/4 header set before encrypt; the RSA ciphertext framing [u32 LE len][big-endian digits].
- committed clean specs produced/refined (neutral prose; no pseudo-code, no addresses; firewall
  grep PASS across specs/​formats/​structs/​packets):
  - opcodes.md (1/6 resolution; CharacterMgmt family; 0/0 key-exchange); packets/login.yaml +
    cmsg_char_create/select/enter/rename/move.yaml + cmsg_logout.yaml; specs/login.md (new);
    crypto.md + login_flow.md (refined).
  - structs/gucomponent.md + structs/guwindow.md (new); specs/ui_system.md §1.6–1.8 (refined);
    specs/ui_hud_layout.md (new — §3 the 5 core panels, §5 the full 152-site HUD inventory);
    frontend_scenes.md §3.5 (preview camera).
  - formats/pak.md (RAW verdict), formats/actormotion.md (new), formats/sky.md (new),
    specs/environment.md (refined).
  - specs/lua-config.md (new), specs/sound.md §15, specs/effect-scheduling.md (new),
    specs/terrain-streaming.md (new), structs/terrain-manager.md (new).
- engineering (re-implemented fresh from the clean specs; full `dotnet build` 0/0, full `dotnet test`
  1296 green across 10 suites after each wave): Network.Protocol char-mgmt request structs + opcode
  routing; Network.Crypto login-credential build (17-byte M, 0x2B pre-image, full secure 1/4 payload +
  whitening); Assets.Parsers sky.box parser + actormotion typed catalogue; Client.Infrastructure
  LuaConfig reader (UTF-8); Godot client UI — inventory (W=732, populated), buff bar, the 5 HUD panels
  at recovered coords, the right-edge HP/MP gauge, bottom action bar, top status bar, a reusable
  CenteredModal base, the char-select 6-keyframe preview camera, login screen (CP949).
- pending (next): Phase D IDB annotation of the new clusters (apply the proposed names/comments) and
  the follow-on names.yaml sync — the per-cluster names.proposed.yaml manifests are staged in _dirty/
  but NOT yet applied to the IDB nor pulled into names.yaml; the layer-04 login-driver migration to the
  new credential API; the deferred Phase-Dbg live confirmations (preview camera, char-create record,
  scheduler now-ms). Journal authored by the Top Orchestrator (main session).

## 2026-06-14 — CAMPAIGN 4: Front-End Fidelity (Login · PIN · Server-List · Char-Select) + VFS comprehension

- scope: refocus onto the front-end being 1:1 with the official client (World scene FROZEN). Recovered
  the exact composition of the four first scenes from `doida.exe` (READONLY static, no IDB writes) + the
  real VFS (harness observation, no IDA), promoted to committed specs, rebuilt the Godot front-end
  faithfully, and deepened the VFS-subsystem understanding. Apparatus: `Docs/PLAN.md` + `Docs/ROADMAP.md`
  (the "CAMPAIGN 4" section).
- recovery (dirty, `Docs/RE/_dirty/campaign4/`, gitignored): the front-end = the `LoginWindow` state
  machine (login+PIN+server-list, states 1–41) + the `SelectWindow` (char-select); the widget-factory
  convention `(texId,X,Y,W,H,srcUV…)` cracked → every widget's screen-rect + atlas-source-UV rect dumped;
  the atlas DDS inventory (loginwindow / loginwindow_02 / password / characwindow / openning_scenario /
  server_icon, cursor stand); the PIN keypad time-seeded Fisher-Yates scramble; the SFX architecture (the
  button base plays nothing — the owning window does; BGM 920100200, UI click 861010101); the "red
  ribbon" = the `OpeningWindow` pre-login intro crawl (vertical scroll + slideshow), not a login effect;
  the front-end captions pulled from `msg.xdb`. VFS-subsystem deep dive: the mount/open/read pipeline
  (hardcoded paths, `vfsmode` packed/loose toggle), the loader-dispatch VERDICT (no magic-sniffing —
  always by call-site/extension; third-party codecs recognise formats), the GHTex named-texture cache
  (the only real asset cache, name-keyed, explicit eviction), the cell→texture / skin→bind/motion linkage
  chains, the sequential bulk-loader; plus a full VFS structure map (43,347 entries, 49 extensions, the
  manifest-linkage tables, 8 asset-resolution chains, the un-specced extensions `.mud/.pre/.post/.tol`).
- committed clean specs (neutral; no addresses, no literal Korean — captions resolve from the VFS at
  runtime; firewall grep PASS): `specs/frontend_scenes.md §11` (the pixel-exact rebuild contract for all
  four scenes), new `formats/msg_xdb.md` (516-byte caption records) + `specs/intro_sequence.md` (the
  OpeningWindow intro), refined `specs/sound.md §15` + `formats/effects.md §A.15` (front-end audio/VFX).
- engineering (re-implemented fresh from the specs; full build 0/0, full test suite green): the `.xeff`
  parser fixed (header 8→32 bytes — `char_select`/`zone_sel` now parse; +28 tests); `MsgXdbCatalog`
  (CP949 caption lookup); the Godot front-end REBUILT faithfully — LoginScreen (12 atlas layers from
  §11.2), PinModal (the Fisher-Yates keypad §11.3), ServerSelectScreen (§11.4), CharacterSelect (§11.5),
  the `OpeningWindow` intro, `FrontEndAudio` (BGM/SFX/cursor), `FrontEndEffectPlayer` (the front-end VFX);
  the layer-04 login flow migrated to build the real secure 0x2B credential (`CredentialPlaintext` +
  `LoginCredentialReply.Build` via `LoginCredentialStore` / `LoginHandshakeDriver`).
- CONFLICTS for arbitration: `bgtexture` — the binary loads a BINARY `bgtexture.lst` (u32 count + 48-byte
  records) while CLAUDE.md/text mirror reference `bgtexture.txt`; resolve before promoting a terrain spec.
- pending: promote the VFS deep-dive (`_dirty/campaign4/vfs/`) into `formats/pak.md` refinements +
  `specs/asset_pipeline.md`; the front-end render-fidelity review vs the official screenshots; the un-
  specced `.scr`/`.xdb` bulk tables; the Phase-D IDB annotation + `names.yaml` sync (still owed from
  CAMPAIGN 3 + 4). No commit yet (maintainer: continue-then-commit-later). Journal authored by the Top
  Orchestrator (main session).

## 2026-06-14 — CAMPAIGN 4 (continued): VFS-pipeline promotion + binary-format specs + front-end fidelity pass

- VFS deep-dive promoted: refined `formats/pak.md` (the `vfsmode` packed/loose toggle, the 3-branch
  DiskFile read, no-decompress); created `specs/asset_pipeline.md` (loader-dispatch = call-site/extension
  with no magic-byte sniffing; the GHTex named-texture cache; the linkage chains A–H; the sequential
  bulk-loader) and `specs/vfs_overview.md` (directory tree + 49-extension census + manifest-linkage table).
- New binary-format specs: `formats/bgtexture_lst.md` (the BINARY terrain-texture index — `u32` count +
  48-byte records = kind byte + `char[47]` relpath; the `.txt` is an authoring mirror; supersedes the
  earlier inferred 76-byte estimate in `terrain.md`); `formats/xdb_tables.md` (the five small flat `.xdb`
  tables: actor_size / buff_icon_position / effectscale / vehicle / creature_item); `config_tables.md`
  refinements (`mapsetting.scr` 84B × 52; `skills.scr` = 1504 + N×8 trailing, NOT a flat array).
- Front-end: a render-fidelity review (PARTIAL — boot + VFS assets OK; gaps = missing 종료 button,
  exposed debug pose buttons, generic server rows, missing PIN warning + connecting dialog), then a Godot
  fidelity-fix pass applied them (atlas server rows from `loginwindow_02.dds`, the centered connecting
  dialog, the PIN warning line, the 종료 button, debug poses hidden, the Enter button art). Atlas
  source-UV sub-rects recovered (READONLY) for the PIN dragon-frame (`318,647,340×190` of
  `InventWindow.dds`, NinePatch-stretched) + the server-row parchment plates (`loginwindow_02.dds`) +
  the verdict that the login 종료 is button #63 of `login_slice1.dds` (no dedicated bottom-bar sprite) —
  staged in `_dirty/campaign4/frontend/atlas-subrects.md` for a `frontend_scenes.md §11` refinement.
- Build 0/0. Remaining: apply the PIN dragon-frame sub-rect, the format-table loaders (bgtexture.lst /
  .xdb / mapsetting), the char-preview skinning debt, the IDB annotation + `names.yaml` sync. Journal
  authored by the Top Orchestrator (main session).

## 2026-06-14 — CAMPAIGN 4: Char-Select is a 3D scene (map000) + the skinning debt FIXED

- RECOVERY (READONLY static IDA + VFS harness, `_dirty/campaign4/charselect3d/`): the Character-Select
  is NOT a 2D screen — it is a **3D GScene built on `map000`**. `Map_SetActiveArea(area=0,…)` → "000" →
  map000; the earlier "area 52200" in `frontend_scenes.md §3.5.1` was a MISREAD — **52200 = 14:30
  time-of-day, 48 = weather sub-index**. Recovered: the backdrop (the single map000 cell
  `d000x10000z9990` + its 11 textures), the camera (live keyframe = index 1, eye ≈ (512,87,−9652),
  look-at the orbit point, ~2 s ease), the environment (real area-0 world env frozen at 14:30; area-015
  sky `.bin` files; ~5 positional lights; ambient `380003001.xeff` + `zone_sel_u.xeff`), the
  character-preview placement (a row along world +X at Y=0, spacing 36, pure-yaw facing 0=front/π=back,
  pose = the in-world pipeline with an idle vs select-turn clip swap, selection = 3D AABB hit-test), and
  the preview-character assets (4 starter classes at IdA=1 share `g1.bnd` 84 bones + idle
  `g111100010.mot`; meshes g202/203/209/206 110001.skn).
- PROMOTION: `specs/frontend_scenes.md` corrected (§3.5.1) + extended (§3.3 placement, §3.5.2/.4 camera,
  §3.6 environment, §3.7 the 3D composition + preview assets). Firewall PASS (no addresses, no Korean).
- ENGINEERING — **the long-standing skinning debt (D1, exploded character mesh) is FIXED**: the root
  cause was the `.mot` animated-rotation composition mode in `SkinningMath.ComputeAnimatedWorld` — it
  REPLACED the bone's bind-local rotation with the sampled keyframe rotation, but `specs/skinning.md`
  §6.5/§6.6 say the keyframe is a right-multiply DELTA (`bindLocal ⊗ animLocal`). Fixed (the vertex
  normal-first/position-second order was already correct). Now the mesh is intact through the idle and
  frame-0 is pixel-identical to the bind pose. Godot build 0/0. Remaining: the faithful Godot 3D
  char-select rebuild (load the map000 backdrop + the 3D actor row + the camera/environment/VFX + the 2D
  overlay) from the promoted spec; a front-3/4 preview framing; the format-table loaders; the IDB
  annotation + `names.yaml` sync. Journal authored by the Top Orchestrator (main session).

## 2026-06-14 — CAMPAIGN 4: front-end deep comprehension (Login + Char-Scene) + faithful fixes

- RECOVERY (READONLY static IDA on `doida.exe` 0x400000 / sha `63fcaf8e…`, ≤3 readers per sub-wave, no
  debugger, no IDB writes; `_dirty/campaign4/{login,charselect3d,cs-flows,vfs}/`):
  - **Char-scene composition TRUTH:** the select/create scene IS map000 (area **0**, CODE-CONFIRMED —
    every sky/env loader builds its name from raw area 0) — the single cell `d000x10000z9990.bud`
    (17 objects) + its `.fx3/.fx5` water + exactly **ONE** code-spawned ambient effect **380003000** at
    (508.48, 69.89, −9758.57). There is NO placement manifest for area 0 (`data/effect/map000.txt`
    absent; the `data/sky/map/map%d.txt` table is dead). The "cavern" = the cell geometry + lighting +
    water + that one effect, NOT a different cell. SUPERSEDES the earlier "area 015/52200" inference.
  - **Caption** `character count : N` = MessageDB id **2209** (SUPERSEDES 48001/2206); N = the
    BillingState char-count field (also decremented by the delete-response).
  - **Double-music root cause:** char-select BGM = cue **920100200**, started unconditionally by the
    select-window ctor with NO stop-before-play guard, teardown does no sound teardown, the scene is
    re-enterable → the cue re-issues on the single BGM slot → overlap.
  - **Skeleton resolution (resolves the g6/g11 gap):** the binary has NO `g%d.bnd` printf. `g1..g4.bnd`
    are pre-loaded by name from `bindlist.txt`; the skeleton is SELECTED via the AnimCatalog visual map
    keyed by `IdB = 5·(class+4·variant)−24 ∈ {1,11,16,26}`. classGroup 6/11 is only an outfit tag →
    g6/g11 never needed. CORRECTS the CLAUDE.md `g{IdB}.bnd` rule.
  - **PIN modal show-trigger (RESOLVED static):** shown UNCONDITIONALLY after login-OK (login tick
    substates 29→31→32 SetVisible); `DName::isPin` is DEAD (zero xrefs); the PIN rides as the **3rd
    tab-delimited field** of the state-40 credential blob fed to the secure-context rebuild (no standalone
    PIN opcode). Login states 29/31/32 are PIN-show/poll, NOT EULA (corrects the prior tick labelling).
  - **Camera (RESOLVED):** ONE fixed camera (live keyframe 1) frames all 5 slots; slot select/hover does
    NOT re-aim or zoom (only highlight+anim+labels); the camera `event` is a mouse-wheel dolly only; the
    create-mode +56.5u is an ACTOR offset, not a camera move. Framing law
    `eye = orbitPoint + Rotate(quat, boom)`.
  - **Login state machine 1..41**, credential capture, server/channel fetch (blocking worker threads,
    LZ4), intro = SFX 861010105 + a curtain/letterbox widget animation (no login BGM), transition effect
    10001 @ 30000 ms — recovered.
- PROMOTION (REWRITE, firewall PASS — no addresses/pseudo-C/Korean): `specs/frontend_scenes.md`
  (§1.5 login flow, §3.5/§3.6/§3.8 char-scene truth + BGM/caption/camera, §4 create sub-form geometry +
  preview + class permutation `{0,1,2,3}→{4,1,3,2}`, §11 atlas formats), `opcodes.md` +
  `packets/cmsg_char_{create,enter,rename,select}.yaml` (1/6 create 52B body, 1/9 enter version-token,
  1/13 rename, 1/7 = dual manage/delete, 1/14 = slot-move), `formats/effects.md` (§A.2/§A.4/§A.15 the
  block[0]-has-no-prefix correction). `names.yaml`: 0x10007 → `CmsgManageCharacter`, 0x1000e →
  `CmsgMoveCharacterSlot`.
- ENGINEERING (build 0/0, 1300+ tests green): **XeffParser fixed** — block[0] carries no entry-count
  prefix (count comes from header `first_entry_count @ 0x1C`); blocks 1..N-1 have a 24-byte prefix; the
  front-end `char_select-u.xeff` (68 sub) + `zone_sel_u.xeff` (11) now parse (Parsers 437 tests incl. 5
  new). **Godot char-select:** the colored-cube bug was `SkinnedCharacterNode.Setup` forcing a RED debug
  material and ignoring the resolved albedo (+ a `_meshInstance.Mesh` pointing at an empty mesh causing
  per-frame `p_surface` errors) → fixed, the 4 starters are textured; the stray blue/red "flying pixels"
  were the xeff-parse-fail fallback emitters → removed; the double-music `_shot.gd` autoload artefact →
  removed; characters were out of frame (placed at Y=0 under the platform) → placed at the platform
  surface Y≈70 with the camera reframed (full-body, lower-centre, per the official screenshot).
- Remaining: wire the now-parsing front-end effects (brazier/portal) into the Godot scene; Login/PIN/
  ServerList Godot fidelity rebuild from the confirmed atlas/flow; the server/channel reply record layout
  + enter-world handshake; Phase-D IDB annotation. Journal authored by the Top Orchestrator (main session).

## 2026-06-14 — CAMPAIGN 4: login→world bridge RE + Login/Create Godot fidelity

- RECOVERY (READONLY static IDA on `doida.exe` + VFS harness; `_dirty/campaign4/{login,vfs}/`):
  - **Server-list reply:** 8-byte frame wrapper (`+0 u32 size`=8+payload, `+4 u16`=entry COUNT, `+6 u16`
    unused); payload is **LZ4-compressed and NOT encrypted**. Each server entry = **8 bytes** =
    `{+0 i16 id/select-key (also the ==100 available gate), +2 i16 status/kind, +4 i16 population,
    +6 i16 flag}`. status/population → caption message-ids (headers 4029–4032; population 6001–6003 by
    thresholds 1200/800/500 or discrete 4/3/2; status==3 → 6004/6005; OOR → 5901). The channel/endpoint
    reply copies the first **30 bytes** of the decompressed payload as a fixed `char[30]` endpoint; connect
    target = port **10000 + channelOffset** (literal host:port format = needs-capture).
  - **Enter-world handshake:** char-select Enter → **C2S 1/9** (40B = slot 1B + 33B launcher session
    token + 4B version dword `10×game.ver-field5 + 9`) → **3/5 SmsgEnterGameAck** (44B account/billing
    confirm; sets scene→loading; NOT spawn data) → **4/1 SmsgGameStateTick** (world spawn + self-snapshot
    CARRIER; FROZEN world scene, not reversed). **Loading screen** = its own LoadHandler scene (random
    `data/ui/loading{,06,08}.dds`, SFX 920100100, progress = VFS asset PRELOAD, NOT a net wait).
    **GameState scene model** = 1 login / 2 loading / 3 opening / 4 char-select / 5 in-world / 6,8 quit /
    7 error — SUPERSEDES the earlier "GameState=7 at login submit" (7 = error/abort). needs-debugger:
    the 3/5-vs-4/1 arrival ORDER, the 1/9 version-dword offset.
  - **VFS facts:** `data/char/bind/bindlist.txt` = a one-column explicit list of 349 `.bnd` names (gaps →
    confirms NO computed `g{N}.bnd` rule; the client reads the explicit list). `data/cursor/game.ver` =
    28B = 7×u32 LE; field5 @0x14 (=2114) → enter token 21149. NO server-config file in the VFS (auth
    host:port is compiled-in / out-of-VFS). Atlas: `loginwindow_02.dds` is **DXT2** (premultiplied — Godot
    import flag), others 1024² DXT5/DXT3, `characwindow.dds` 512² RAW BGRA8. Audio: cue → `data/sound/2d/
    {cue}.ogg` (direct, no lookup) — 861010105/861010101/920100200/920100100/910062000..910065000.
- PROMOTION (REWRITE, firewall PASS): `opcodes.md` (1/9, 3/5, 4/1 rows + server-entry appendix) +
  `packets/cmsg_char_enter.yaml` + `packets/3-5_enter_game_response.yaml` + `packets/lobby.yaml`
  (server-list framing) + `specs/login.md §5` (fetch + handshake + GameState model); `formats/bindlist.md`
  (new) + `formats/actormotion.md` xref + `formats/config_tables.md §7` (game.ver) + `specs/sound.md
  §15.6/§15.7` (front-end audio) + `frontend_scenes.md §11.1a` (atlas DDS formats). names.yaml already
  carried 0x10009/0x30005/0x40001 (consistent — no change).
- ENGINEERING (Godot build 0/0): **Login/PIN/ServerList fidelity** — corrected the flow order to
  Login-validate → PIN → ServerList → CharSelect; PIN modal now the `InventWindow.dds` NinePatch dragon
  frame (318,647,340×190); ServerList = `loginwindow_02.dds` parchment plates; recessed ID/PW textboxes.
  **Character-CREATION sub-form** built (`CharCreatePreview3D` + integrated into `CharacterSelectScreen`):
  class list (left), centered enlarged preview (+56.5u, scale 75, turntable), stat/name/OK-Cancel panels
  (right), UI→internal class map {0→4,1→1,2→3,3→2}, name validation. Residual DEBT: the create-preview
  actor shows a non-upright (≈90° lying) pose — a skinning stand-up-basis bug on the preview path, to fix.
- Remaining: fix the create-preview pose; wire the now-parsing brazier/portal `.xeff` into the cavern;
  implement the `bindlist.txt`/`game.ver` parsers + the server-list/enter-world network structs; the
  3/5-vs-4/1 live-debugger order check; Phase-D IDB annotation. Journal authored by the Top Orchestrator.

## 2026-06-14 — CAMPAIGN 4: opening-intro + login-form RE; parsers/structs implemented; create-pose fixed

- RECOVERY (READONLY static IDA on `doida.exe`; `_dirty/campaign4/login/`):
  - **OpeningWindow intro:** a STANDALONE `COpeningWindow` scene at engine-state **3 (Opening)**, BEFORE the
    login form (state 4) — torn down before the LoginWindow ctor (NOT a login phase). A fade machine of
    **4 phases × 17,500 ms = 70.0 s** (`openning_001..004.dds`, alpha 0→250) + a parallel scroll crawl of
    `openning_scenario.dds` at 30 u/s to bound ~1843 (~61 s). One looped 2D cue **910061000** (doubles as
    BGM; distinct from login SFX 861010105). Transition = auto-after-dwell OR skip-on-input
    (Enter/ESC/Space/skip-button) which persists `[OPENNING] SKIP=1` to the INI (returning players bypass).
    Crawl text is baked into the art (no message table).
  - **Login-form widgets (CODE-CONFIRMED ~18-widget table):** atlases `loginwindow.dds` (edit frames) +
    `login_slice1.dds` (buttons/captions). ID edit src 390,32,102,13 @615,404 action 109 max 16; PW edit
    src 568,32,102,13 @615,404 action 110 max 12; login-OK button src 456,64,112,39 action 103; notice src
    456,166,112,39 action 102; save-ID checkbox action 104; server up/down/confirm 106/107/108. **PW
    masking = one ASCII `*` per char (NOT a round dot).** The quit/종료 tab strip is register-staged
    (PLAUSIBLE); the prior "widget index 170/171" is SUPERSEDED (global slots, not the field handles →
    needs-debugger).
- PROMOTION (REWRITE, firewall PASS): `frontend_scenes.md` §1.0 (opening intro = state 3→4) + §11.2e/§11.6
  (login widget table + the two distinct intros: standalone opening vs login-window curtain).
- ENGINEERING (full solution build 0/0, ~1409 tests green):
  - **Parsers (layer 03):** `BindlistParser` (skeleton registry — ordered list + O(1) `IsRegistered`) +
    `GameVerParser` (7×u32, `EnterGameVersionToken = 10×field5+9`); +23 tests (460 total).
  - **Network.Protocol (layer 02):** `CmsgEnterGameRequest` (1/9, 40B), `SmsgEnterGameAck` (3/5, 44B),
    and the lobby server-list structs (`LobbyFrameWrapper` 8B, `LobbyServerEntry` 8B,
    **`LobbyChannelEndpointToken`** 30B — renamed from `LobbyChannelEndpoint` to avoid colliding with the
    established `Network.Abstractions.Lobby.LobbyChannelEndpoint` record) + a zero-alloc `ref struct`
    reader; +10 tests (102 total). UNVERIFIED 1/9 intra-buffer offsets left as `// TODO needs-capture`.
  - **Godot (layer 05):** the create-preview "lying 90°" defect FIXED — root cause was the create-preview
    CAMERA aiming at the recentre OFFSET (not the mesh AABB), pushing the upright actor out of frustum;
    `CharCreatePreview3D.FrameCameraOnActor` now frames the actor's real world-AABB. Slot-row confirmed
    upright. **Build-break fixes:** the duplicate `LobbyChannelEndpoint` (rename above) + `XeffSubEffect.SubId`
    made non-`required` (it broke `Assets.Mapping.Tests`; SubId defaults 0 for block[0]).
- Residual DEBT: the create form defaults to UI class 0 → internal class 4 (`g202140001`), whose ANIMATED
  idle shatters (separate per-mesh skinning-convention debt; its rest pose is clean and class 1 animates
  fine) — needs the unrecovered skinning convention. Plus: wire the brazier/portal `.xeff`; the
  3/5-vs-4/1 live order; Phase-D IDB annotation. Journal authored by the Top Orchestrator (main session).

---

## 2026-06-14 — CAMPAIGN 4 (cont.): Login display-list 1:1 + char-create rig fix + workflow conflict reconciliation

Top-Orchestrator session (main loop). Many parallel waves; firewall held (dirty → `_dirty/`, REWRITE-only
promotion, no pseudo-C in committed files), build 0/0, ~1409 tests green throughout.

- **RECOVERY (dirty, READONLY IDA ≤3 readers/wave, no `dbg_start`):**
  - **Login render fidelity:** full LoginWindow DISPLAY LIST (#0–#73 + cursor) — canvas 1024×768 top-left
    anchored/centered; the carved-iron bezel + hanging rings + red badge/flag + URL are **NOT widgets** but
    **baked art** in two `login_slice1.dds` backdrops: upper src(0,0,1024,398), lower src(0,582,1024,442).
    Draw/z-order + conditional default-focus (`(null)` saved-id sentinel → ID else PW) + caret (1 Hz blink,
    insertion bar, PW masked `*`) + generic ~4-frame show/hide fade. VFS cross-check confirmed the atlas
    regions visually.
  - **Char-select camera:** there is **NO traveling/dolly/focus-on-selected** — fixed keyframe-1 frames the
    whole row; only interactive motion = mouse-wheel dolly (±4, boom-Z clamp [0,22]). Angle multipliers
    0..5=PITCH / 6..11=YAW; base pitch −30°; field +0x114 = zoom (not pitch); no keyframe auto-advance.
  - **Char-create shatter root cause:** a `.skn` is authored against ONE skeleton named by its own `id_b`
    (class 4 = Monk g4/89 bones; class 1 = g1/84 bones). The preview hard-coded a single shared g1 rig+idle
    for all classes → wrong-rig clip shatters off-bind. Fix = resolve `g{id_b}.bnd` + idle per class.
  - **Full front-end workflow:** Loading = engine-state 2, **VFS-preload gate (not network)**, progress bar,
    SKIP-driven out-edge. SelectWindow ops confirmed (1/6 create 52B, 1/7 manage `{slot,mode}` 2B, 1/9 enter
    40B, 1/13 rename 18B, 1/14 slot-move 1B); 1/9 offsets statically pinned; **UI→internal class map
    {0→4,1→1,2→3,3→2}**; **delete = 1/7 {slot,1}** (mode byte literal 1; `1/14` = slot-move). Char-count =
    BillingState **+0x80** + MessageDB 2209, 4 writers incl. the 3/5 ack overwrite; slots = **bit-position**
    (mask bit k → slot k). The **981-byte** per-character `3/1` list record fully cartographed (name@+0x00,
    variant@+0x2C, internal_class@+0x34, equip/visible-gear table@+0x58 slots {3,4,6,2,11,14}); appearance is
    **descriptor-driven** (`model_class_id = 5·(internal_class + 4·variant) − 24` → IdB → catalog skeleton).
- **PROMOTION (REWRITE, firewall PASS):** `frontend_scenes.md` §3.1/§3.5.3-5/§3.8.2/§5/§6/§8/§10/§11.2e/g/h;
  `skinning.md` §8(e) rig/clip identity; `opcodes.md` 0x10006/0x10007/0x10009 corrected; `cmsg_char_*.yaml`
  (create class-map, select delete mode=1, enter offsets pinned); `3-1_character_list.yaml` (96B StatBlock +
  appearance driver); `structs/actor.md` (SpawnDescriptor sharpened).
- **ENGINEERING (build 0/0, tests green):**
  - **Godot login (layer 05):** rebuilt 1:1 from the display-list — removed the wrong `loginwindow.dds`
    "TopChrome" hack; bottom-layer `login_slice1.dds` backdrops (frame/rings/flag/URL baked) + ink-wash panel
    on top; DXT2 premultiplied-alpha decode+unpremultiply in `RealClientAssets`; channel-blocks/listbox hidden
    at boot; faceplate dst/src transposition fixed. (Residual: painting-vs-frame proportional calibration.)
  - **Godot char-create (layer 05):** per-class `g{id_b}.bnd`+idle resolution + defensive skip-out-of-range-
    track guard — all 4 classes render intact and animate (the class-4 shatter is gone).
- **RESIDUAL:** login painting/frame proportional polish; wire brazier/portal `.xeff` into the cavern; the
  3/5-vs-4/1 + `3/4`-vs-`3/7` delete-carrier live-debugger confirms; Phase-D IDB annotation (function-name
  proposals staged under `_dirty/**/names.proposed.yaml` — NOT opcode glossary, deferred to ida-naming-sync).

---

## 2026-06-14 — CAMPAIGN 4 (cont.): char-select red-screen fixed + scene/PIN/server-list RE

Top-Orchestrator session, continued. Firewall held; build 0/0; ~1520 tests green (3/1 reader +9).

- **RECOVERY (READONLY IDA):**
  - **Char-select scene assembly:** 5 actors placed by a separate post-build step from a 5-row code-immediate
    table; platform Y = hard 0.0; ΔX negative offsets from base X 2048 (+12 step); ΔZ shallow arc; ×3.0.
    Single composite brazier effect `char_select-u.xeff` (internal id **380003000** — prior hex→dec
    misconversion corrected) at world ≈ (508.5,69.9,−9758.6); waterfall = terrain cell water layer (not an
    effect); `zone_sel*` are World-only portals. Selection feedback = idle→select clip swap (distinct second
    `.mot`), no glow.
  - **PIN modal display-list:** keypad window (347,173,329,422); 100 scrambled digit tiles where TAG = true
    digit (positions re-rolled on open/Reset); OK tag 12 / Cancel tag 13 / Reset tag 11; dragon frame
    `InventWindow.dds`(318,647,340,190) NinePatch; masked echo GULabel (81,138,150,22). **Correction:** digit
    glyph src is (d*52, 560, 52, 52) — U=digit, V=state. A separate `AutoCheckPanel` anti-bot keypad exists.
  - **Server-list display-list:** single LoginWindow builder + render-time NEW_SERVER branch; exactly 10
    server rows (actions 115..124); 8-byte record {id u16, status i16, load i16, open_time i16}; row click
    sets selected server, persists Lastserver, fetches channel endpoint at 10000+id; two channel parchment
    plates (actions 400/401); scroll-arrow src corrected to loginwindow.dds (483/505/496,490); names 5001..5040.
- **PROMOTION (REWRITE, firewall PASS):** `frontend_scenes.md` §3.3.1/§3.3.4/§3.3.5/§3.6.5 (char-select scene
  + 380003000 resolved) and §11.3 (PIN corrected + masked echo + AutoCheckPanel). Server-list deltas staged
  in `_dirty/structs/serverlist-displaylist.md` for a later §11.4 promotion.
- **ENGINEERING (build 0/0):**
  - **Network.Protocol (layer 02):** `3/1` character-list reader — 981-byte per-slot record, zero-alloc
    `ref struct`, bit-position slot placement, appearance helper `5*(class+4*variant)−24`; +9 tests.
  - **Godot (layer 05) — char-select RED SCREEN FIXED:** root cause was `FrontEndEffectPlayer` 2D particles
    double-scaled (SizeX ×24 then ×20 → ~77,000 px) covering the viewport. Removed the 2D overlay (braziers
    are 3D per §3.6.5); wired `GPUParticles3D` braziers at the world anchor in the cavern; the 3D scene
    (cavern + characters) now renders. **Login margins FIXED:** `ScreenHost` letterbox → non-uniform fill
    (no gray bands); roster seeded to 3 to match "캐릭터 개수 : 3".
- **RESIDUAL:** brazier emitters should sit on the two side pillars (currently centred — needs the xeff
  68-sub-effect layout); char-select environment should be a darker enclosed cavern (currently shows map000
  green-grass surround); build the PIN + server-list Godot screens from the now-recovered display-lists;
  promote server-list §11.4; the live-debugger render-path confirms.

---

## 2026-06-14 — CAMPAIGN 4 (cont.): char-select cavern + server-list/loading RE

- **PROMOTION:** `frontend_scenes.md §11.4` (server-list: 10 rows actions 115..124, 8-byte record, channel
  plates 400/401, scroll-arrow src corrected to loginwindow.dds 483/505/496,490, single builder + NEW_SERVER
  render-branch) + §1.5 callout.
- **RECOVERY (READONLY IDA):** Loading-screen composition — canvas 1024×768 center-origin; background = one of
  `loading.dds`/`loading06.dds`/`loading08.dds` (rand%3); progress bar lower-left rect, fill 223×pct/100 px
  sampled from a baked strip of the SAME DDS; NO caption/spinner/percent text (any wording baked); looping cue
  920100100 (abort 861010106). Staged in `_dirty/campaign5/loading-screen-composition.md`.
- **ENGINEERING (Godot layer 05, build 0/0):** char-select cavern — two brazier `GPUParticles3D` now sit on
  the two side pillars (±X from the §3.6.5 anchor) with matching torch OmniLights; environment reworked to a
  dark enclosed cavern (near-black BG, exponential fog absorbing the map000 grass surround, warm torch
  lighting, glow; sun DirectionalLight removed). Red-screen era fully over; cavern reads like the official.
- **RESIDUAL:** char-select SLOT actors render T-posed (idle `.mot` not applied to the slot row — same
  per-id_b idle resolution as the create fix is needed); waterfall render; build the PIN/server-list/loading
  Godot screens from the recovered display-lists; promote loading §9.

---

## 2026-06-14 — CAMPAIGN 4 (cont.): char-select slots animate + appearance pipeline RE + loading §9.1

- **ENGINEERING (Godot layer 05, build 0/0):** char-select SLOT actors no longer T-pose — each slot resolves
  its OWN `g{id_b}.bnd` + per-id_b idle `.mot` (same §8(e) fix as the create preview); slots 0/2/3 animate
  (track==bone, INV1<2e-6), slot 1 (id_b=2, no idle row in this VFS) fail-safes to a clean rest pose;
  character key-light raised so figures read lit, not silhouettes.
- **PROMOTION:** `frontend_scenes.md §9.1` loading-screen visual composition (rand%3 background, 223×pct/100
  bar from a baked DDS strip, no caption/spinner, looping cue 920100100).
- **RECOVERY (READONLY IDA):** full character APPEARANCE ASSEMBLY — a character = one shared skeleton + up to
  6 overlay `.skn` parts (body = overlay slot 3, NOT a separate base mesh); `model_class_id = 5·(class+4·
  variant)−24 ∈ {1,11,16,26}`; overlay slots {3,4,6,2,11,14} (14=weapon, local-player only); textures +
  motions are REGISTRY-keyed by numeric id from list files (`tex{W}{H}list.txt`, `motlist.txt`) — not
  `%d.png`/`g%d.mot` formatting; idle via actormotion(id_b). Resolves `preview-character.md §8` "no IDA
  cross-check". Staged in `_dirty/campaign5/character-appearance-assembly.md` (+ deltas for texture.md/
  animation.md/skinning.md/frontend_scenes §3).
- **RESIDUAL:** waterfall render; build PIN/server-list/loading Godot screens from the recovered display-lists;
  promote the appearance-pipeline deltas; live-debugger value-edge confirms (catalog categoryBase[], bind-pose).

---

## 2026-06-14 — CAMPAIGN 4 (cont.): PIN screen 1:1 + appearance-pipeline promotion + enter-world sequence RE

- **ENGINEERING (Godot layer 05, build 0/0):** PIN modal built 1:1 from §11.3 — dragon/parchment frame
  (`InventWindow.dds` 318,647,340,190 NinePatch), 2×5 scrambled digit grid (`password.dds`, digit src
  CORRECTED to (d*52,560,52,52); positions Fisher-Yates re-rolled on open/Reset, button carries true digit),
  masked `*` echo (81,138,150,22), OK tag12 / Cancel tag13 / Reset tag11.
- **PROMOTION (REWRITE, firewall PASS):** appearance pipeline → `formats/texture.md` (list-file numeric-id
  registry, not %d.png), `formats/animation.md` (motlist.txt registry + 0x88 actormotion record),
  `skinning.md §3.5` (shared skeleton + 6 overlays, body=slot 3, model_class_id formula, slot→family),
  `frontend_scenes.md §3.3.6` (list-slot vs create-preview share the factory).
- **RECOVERY (READONLY IDA):** enter-world sequence — `1/9` (40B) confirmed off the Enter helper (slot@0,
  33B session token@+1 = launcher token NOT typed account, version@+0x24); `3/5 EnterGameAck` sets
  GameState→2 (LOADING, gate = VFS preload not net); `4/1` builds LocalPlayer at (X,0,Z) → GameState→5.
  `1/9` is the ONLY C2S the Enter action emits. **The live 3/5-vs-4/1 arrival ORDER remains the one
  genuinely debugger-pending fact** (login.md §5.3 marker stands). Staged `_dirty/protocol/enter-game-sequence.md`.
  Note: no protocol `.tsv` capture is present in the tree — the 3/5 "capture x2" provenance can't be
  re-verified now (flagged for reconciliation).
- **Also committed (prior-wave bonus):** `Docs/RE/specs/rendering.md` — D3D9 per-frame draw loop / draw order
  / render-state cache / glow-bloom post chain (clean, firewall-verified).
- **RESIDUAL:** build the ServerList + Loading Godot screens from the recovered display-lists; login bezel
  polish; waterfall; the 3/5-vs-4/1 live-debugger confirm (needs maintainer F9-launch).

---

## 2026-06-14 — CAMPAIGN VFS-DEEP: undocumented VFS file-format gaps closed (hybrid harness + READONLY IDA)

Binary: `doida.exe` sha256 `63fcaf8e…`. Analyst: clean-room fleet (re-cleanroom-orchestrator → vfs-data-analyst ×8 harness + re-asset-format-analyst ×8 IDA, sub-waves of ≤3 on the single IDB; promotion via asset-spec-author ×14). The VFS *container* (`pak.md`) and ~21 prior formats were untouched — this wave targeted only the 13 undocumented extensions + 4 known parser debts surfaced by the 43,347-entry census. Firewall: all raw findings quarantined under `_dirty/campaign-vfs-deep/` (gitignored); every promotion was a REWRITE (self-scrubbed, zero Hex-Rays artifacts/addresses/payload bytes).

- **RECOVERY (harness observation of maintainer's own VFS + READONLY IDA confirm):**
  - `.scr` family — all 44 `data/script/*.scr` are BINARY fixed/variable-stride struct tables (never line-delimited): `citems.scr` 1052B×512 (cash items, NX price @+0x38, CONFIRMED); `items.scr` ~544–556B variable records (CP949 name/desc CONFIRMED, stats block UNVERIFIED); `events.scr` 520B×1848; `autoquestion_cl.scr` 92B×1300 client-side captcha (answer is server-side).
  - `.mud` — per-cell **ambient-sound zone grid** (NOT terrain/water): 32768B = 64×64 tiles ×8B (bgm/bge/eff indices); world→tile = 16 units, `col+(row<<6)`, ×8. Both sources agree.
  - `.tol` / **`region%s.bin`** — map-wide region-id byte grid (256 units/cell). `.tol` = authoring sidecar (origins in front header); the RUNTIME reads `region.bin` (origins trailing) — newly-discovered runtime format.
  - `.pre` / `.post` — both proved **authoring sidecars the shipped runtime NEVER opens** (`.pre` = full standalone base-format file; `.ted.post` = full drop-in `.ted`). Engineering takeaway: no runtime parser needed.
  - small `.xdb` — headerless fixed-stride arrays (actor_size 12×15, buff_icon_position 12×134, creature_item 48×921, effectscale 8×2, vehicle 52×58).
  - `game.ver` 7×u32/28B (matches existing GameVerParser); `mobinfo.mi` 4B count + 28B×21 widget records (fields UNVERIFIED); `.lua` config keys (config/display/uiconfig).
  - debts resolved: `.mot` LenStr CONFIRMED 4B u32 LE (no terminator); `.xeff` header is 8B and the old "type_flag@+0x08" is element-0 `emitter_type` (1=mesh,2=billboard) — NOT a tagged union; `.sod` quad trailing f32 @+32..+47 are a DEAD edge-line cache, not a plane equation; environment fog/colours packed as D3DCOLOR bytes, LINEAR fog range=s·3.0, too-dark = missing OPTION_BRIGHT ambient floor / K_ambient gate.
- **CORRECTIONS to prior knowledge:** `.lua` files are CP949, NOT UTF-8 (refutes the earlier B4 note — `specs/lua-config.md §0`); `terrain.md §11` sod plane-equation reading retired; `effects.md` xeff header size 32→8.
- **PROMOTION (committed specs):** NEW — `formats/scr.md`, `formats/items_scr.md`, `formats/events_scr.md`, `formats/text_tables.md`, `formats/mud.md`, `formats/region_grid.md`, `formats/mi.md`, `formats/game_ver.md`. EXTEND/FIX — `formats/xdb_tables.md`, `formats/terrain.md`, `formats/terrain_layers.md`, `formats/effects.md`, `formats/animation.md`, `formats/environment_bins.md`, `specs/lua-config.md`, `specs/environment.md`.
- **RESIDUAL / debugger-pending:** `mobinfo.mi` 7-field semantics (UI-panel loader not statically located → live-debugger follow-up); `items.scr` stats block cross-family verify; environment `K_ambient` / `OPTION_BRIGHT` numeric defaults (debugger read). Format-concept glossary names (MudTile, MiWidgetRecord, region-grid, …) are address-less and were NOT added to `names.yaml` — they belong to a later IDB-annotation phase if pursued.

## 2026-06-14 — CAMPAIGN 5: Map-Rendering & VFX fidelity (RE re-confirm + spec fills + C# wiring)

Binary: `doida.exe` sha256 `63fcaf8e…`. Fleet: re-cleanroom-orchestrator (4 READONLY IDA lanes ≤3 concurrent: effects-runtime / render-pipeline / shaders / regions) + vfs-data-analyst (4 non-IDA fills) → asset-spec-author promotion; then clean-room engineers for the C# wiring. Firewall held: raw findings under `_dirty/campaign5/` (gitignored), zero Hex-Rays/addresses/payload bytes in committed files; every offset cites its spec.

- **RE re-confirmed (already documented; no net spec change):** render pipeline draw-order (opaque→alpha-test→transparent→FX→post→UI, no back-to-front Z sort), cel/`dotoonshading` is the SKINNED-CHARACTER path (stride-32, TC1 = N·L luminance, ramp `data/shader/toonramp.bmp`, BT.601 luma c9 = [0.299,0.587,0.114]), 6-pass glow/bloom, regions = `region<area>.bin` byte grid (256u) → `regiontable` zone-type enum @+40 (1=PvP, 2=closed, 0=safe provisional). These were already in `specs/rendering.md`, `formats/shaders.md`, `specs/world_systems.md` Ch.16 from prior work.
- **Spec FILLS this wave (committed):** `formats/effects.md` §E `particleEmitter.eff` (16B header magic 0x2711 + 2,243×52B records, stride anchor u16 @+0x10); `formats/terrain_layers.md` FX7 (VF_32) / FX4 (VF_44, single-sample UNVERIFIED); `formats/sky.md` `.box` → CONFIRMED-ABSENT (no `.box` in the 43,347-entry VFS; synthetic dome is the correct path); `formats/environment_bins.md` §1 `map_option` reconciled to 10×u32 (0x00 `MOVE_DUNGEON`, 0x04 `SIGHT_FIX`, … 0x20 `MAPHIDE`) — the old `water_enable`/`water_y` labels were a misread; **no water field exists in map_option**.
- **C# WIRING (clean-room, build 0/0 + ~1484 tests green + headless RC=0):** corrected `XeffParser` 32→8-byte header to the spec (the VFS-DEEP spec fix had left the parser + `XeffJsonConverter` + `FrontEndEffectPlayer` + Mapping test fixtures broken — all reconciled); real `.xeff`-driven `EffectRenderer` (ArrayMesh billboard/mesh emitters + keyframe curves, placeholder fallback) replacing the orange-sphere stub; faithful cel-shading `CelShade.gdshader` + glow/bloom on `WorldEnvironment` (skinned characters only, per spec); `MapOptionBinParser` + consumers (`EnvironmentNode`, `WaterRenderer` placement, vfs-inspect decoder) corrected to the 10×u32 layout with sun/moon dome gating; region runtime (`VfsRegionSource` → `RegionService` → `ZoneChangedEvent` → HUD zone indicator), `ZoneType` in Shared.Kernel + `RegionCatalog` in Domain.
- **RESIDUAL:** `EffectRenderer` carries a local 8-byte `XeffMiniParser` (functional) pending a switch to the shared `XeffParser`; full skill→effect resolution via `xeffect.lst`/`totalmugong.txt` left as a documented hook; `particleEmitter.eff` per-record field semantics + FX4 second-section boundary remain UNVERIFIED (single sample).

## 2026-06-14 — CAMPAIGN VFS-DEEP-II lane I1: mobinfo.mi field-level enrichment (READONLY IDA + harness re-parse)

Binary: `doida.exe` sha256 `63fcaf8e…`. Analyst: re-asset-format-analyst (READONLY static pass, no IDB writes, no debugger) + harness re-parse of the maintainer's single VFS sample (`data/ui/mobinfo.mi`, 592B); promotion via asset-spec-author. Firewall held: raw finding quarantined at `_dirty/campaign-vfs-deep-ii/ida/mi_loader.raw.md` (gitignored, addresses confined to its DEBUGGER PROBE block and NOT carried across); the committed promotion is a self-scrubbed REWRITE — zero Hex-Rays artifacts, zero addresses, zero payload bytes.

- **PROMOTION (EXTEND):** `formats/mi.md` — re-stated with three independent confidence levels. Container = SAMPLE-VERIFIED (4B `recordCount` + 21×28B records, 7×u32 LE, exact 592B factorization). Per-field meanings upgraded from opaque to a PLAUSIBLE hypothesis table from a full 21-record re-parse: field0 = sequential ordinal; (field1,field2) = ±1 caption/text-id couple; field3/field6 = a small co-varying kind/link couple; (field4,field5) = decimal-packed icon/sprite ids (CONFIRMED NOT pointers; sibling delta +1 or +3 → packed (base,range), not adjacent atlas cells); `0xFFFFFFFF` = none-sentinel (fields 1/2/5/6, never field0). All field meanings remain parser-UNVERIFIED.
- **LOADER: UNRESOLVED (static) → LIVE-DEBUGGER-PENDING.** Re-confirmed statically unlocatable: no `.mi`/`mobinfo` path literal (only an unrelated MSVC CRT section-name false positive); opened via the generic by-name VFS reader with a non-literal path; the 28-byte stride sweep is swamped by ~28-byte MSVC `std::string` arrays; the located mob-info panel *renderer* uses hard-coded caption ids + screen coords and is NOT the `.mi` consumer (the file's data values never appear as code immediates → read at runtime). Added a neutral-prose LIVE-DEBUGGER PROBE PLAN (no addresses): breakpoint the by-name VFS open router + load-whole-file helper, filter for a path ending `mobinfo.mi` (trigger by targeting a monster), capture the return frame as the real loader, read the 592B buffer, single-step the consume loop to bind each of the 7 u32 fields — expected to upgrade fields 0–6 PLAUSIBLE→CONFIRMED.
- **RESIDUAL / debugger-pending:** `mobinfo.mi` 7-field semantics + loader identity (the live-debugger pass above is the required next step); single-sample, so stride/header invariants are uncross-checkable. Provisional glossary names (`MiPanelDescriptor`, `MiWidgetRecord`, `EntryId`/`CaptionId`/`KindOrLink`/`IconId`/`LinkOrNext`, …) are address-less and NOT added to `names.yaml` — they belong to a later IDB-annotation phase if pursued.

## 2026-06-14 — asset-spec-author (campaign-vfs-deep-ii residual promotion)
- binary: none (firewall bridge — no IDA; promoted a black-box harness note, no decompiler input)
- tool: VFS harness full-record scans (no decompiler); neutral rewrite only
- analyzed: bgtexture.lst `kind` byte (full scan of both shipped instances, 2,330 records); the
  five small `.xdb` tables (actor_size, buff_icon_position, effectscale, vehicle, creature_item)
  by full-record per-column statistics.
- specs produced/updated:
  - Docs/RE/formats/bgtexture_lst.md (CORRECTED — `kind` is NOT constant 0x01: promoted to a
    material render-mode tag with a value→mode table 0x01 static / 0x02 scroll / 0x0A grass /
    0x0B plant / 0x0C tree-bark / 0x14 foliage, HIGH; mirrored the correction onto bgtexture.txt
    col1; updated banner + Known unknowns)
  - Docs/RE/formats/xdb_tables.md (LANDED — actor_size scale_a/scale_b axis inference;
    buff_icon_position buff_id non-contiguous RESOLVED + sprite_y confirmed pixel-Y;
    effectscale key hi16 type-tag / lo16 index; vehicle tag_b constant table-stamp +
    tag_a 3-family discriminator + param_1 always-zero + params 0/2 rider X/Z offset;
    creature_item attach_probability_f32 + four independent u8 flags + probability const 100 +
    scale_or_radius two-level collision radius)
- notes: One residual-promotion wave correcting and characterizing previously UNVERIFIED columns.
  Firewall held — neutral prose and tables only, no addresses, no pseudo-code, no sample bytes.
  Concept names flagged for names.yaml (KIND_* render modes, VehicleXdb.tableStamp,
  CreatureItemXdb.collisionRadius / attachProbability) but NOT written there (orchestrator-owned).

## 2026-06-14 — asset-spec-author (promotion: per-zone sound index tables)

- source (dirty, gitignored): `Docs/RE/_dirty/campaign-vfs-deep-ii/harness/mud_sound_tables.raw.md` (black-box VFS-census harness, no decompiler; IDA cross-check NOT yet staged)
- method: rewrite-not-copy promotion of harness findings into neutral committed specs
- specs updated:
  - `Docs/RE/formats/sound_tables.md` — EXTENDED with the harness-measured on-disk layout: 5 table types (`.bgm`/`.bge`/`.eff`/`.wlk`/`.run`) × ~60 areas (~300 files) under `data/mapNNN/soundtableNNN.*`; **on-disk record stride CORRECTED 48 → 52** (256 records × 52 = 13312, exact division — SAMPLE-VERIFIED across ~300 tables); record now resolved as `sound_entry_id` u32 @+0x00, 24-byte mask @+0x04 (semantics UNVERIFIED), `weight` f32 @+0x1C, EFF-only 3D position `pos_x/pos_y/pos_z` @+0x20/+0x24/+0x28 and `radius` @+0x2C, `tail_unknown` 4 bytes @+0x30 (UNRESOLVED); the prior 48+1024 "editor-metadata" split preserved as a provenance/conflict note; full `.mud`-byte → table-row → `data/sound/{2d|3d}/{sound_id}.ogg` resolution chain wired (0-based direct index).
  - `Docs/RE/formats/mud.md` — replaced the "BGM/BGE/EFF table sources not traced" known-unknown with the confirmed resolution chain to `sound_tables.md`; recorded the **bytes-0/1 walk/run hypothesis** (byte 0 → `.wlk`, byte 1 → `.run`; previously "reserved") as **PLAUSIBLE**, with a one-line harness/debugger re-verify request (breakpoint the footstep trigger, confirm it indexes `.wlk`/`.run` by mud byte 0/1).
- confidence: stride=52 / 256 records / sound_id @+0x00 / 0-based direct index / leaf-dir-by-extension = SAMPLE-VERIFIED; 24-byte mask semantics, EFF tail bytes, and the mud bytes-0/1 walk/run source = UNVERIFIED/PLAUSIBLE (flagged for IDA cross-check).
- firewall: committed files scrubbed — zero addresses / Hex-Rays tokens / autonames / payload bytes. Dirty source left intact.
- names flagged for names.yaml (orchestrator-owned, NOT edited here): `soundtable_bgm/bge/eff/wlk/run`, `SoundTableRecord` (+ field set), `SOUNDTABLE_FILE_SIZE/RECORD_COUNT/RECORD_STRIDE`; `MudTile` byte-0/1 renamed `wlkZoneId?`/`runZoneId?` (PLAUSIBLE).

## 2026-06-14 — CAMPAIGN VFS-DEEP-II lane I4: terrain height axis + texture idx-1 resolved

- **Spec authoring** (dirty -> clean promotion via re-promote firewall; source note left intact under
  `_dirty/campaign-vfs-deep-ii/ida/` as gitignored provenance). Three committed specs updated:
  `Docs/RE/formats/terrain.md`, `Docs/RE/specs/asset_pipeline.md`, `Docs/RE/formats/terrain_layers.md`.
- **AXIS (terrain.md §5.2) — UNVERIFIED -> PARSER-VERIFIED (CONFIRMED), no residual.** The `.ted`
  height grid is row-major with **X = column** (inner/fast, stride 1) and **Z = row** (outer/slow,
  stride 65): `heights[row * 65 + col]`. Proven from the loader's mesh-build nested-loop index
  arithmetic (`row * 65 + col`) correlated with per-vertex world-X/Z coordinate stamping against the
  cell-origin bases; corroborated by the independent seam-continuity sample test. Converter (Assets.
  Mapping terrain -> glTF) may drop the axis caveat.
- **TEXTURE INDEX (asset_pipeline.md §B; terrain.md §5.6) — raw-vs-`idx-1` CONFLICT resolved to
  `idx-1`, HIGH.** Per-cell `.ted` texture byte is 1-based; texture resolves as
  `per_cell_texture_list[byte - 1]`, byte 0 = no-texture sentinel. On-disk block-3 bytes are 1-based
  (observed 1..11, never 0); the per-cell list is built 0-based by `TEXTURES{}` registration order,
  forcing the -1 — mirroring the already-CONFIRMED BUILDING `tex_id - 1` path. terrain_layers.md
  known-unknown #12 aligned to the same resolution.
- **RESIDUAL (honest, thin):** the literal `- 1` instruction was not pinned to a single site because
  the draw path resolves patches to texture-node pointers at cell-attach (not re-subscripted per
  frame); the mapping is structurally certain (HIGH) but the instruction-exact decrement site is the
  one residual (debugger-pending). The axis finding has **no** residual.
- **Firewall:** self-scrub PASS — zero decompiler autonames, pseudo-types, mangled symbols, or
  binary addresses in the committed prose; working symbols and addresses stayed in `_dirty/`.

## 2026-06-14 — asset-spec-author
- binary: doida.exe (Main.exe) — promotion from neutral `_dirty/` analyst notes only
- tool: none (firewall bridge — no IDA; rewrote neutral analyst notes by hand)
- analyzed (by canonical name): MotionClip `track_descriptor`; authoring sidecars `pre` /
  `post` families (`sod_pre_polygon_list`, `fxN_pre_record_extra`)
- specs produced/updated:
  - `Docs/RE/formats/animation.md` — promoted `track_descriptor` upper-3-byte finding to
    CONFIRMED (loader-direct + sample): low byte = `bone_id`, bits 8–31 reserved/unused padding.
    Added a byte-decomposition subsection that positively REFUTES the three candidate
    interpretations (key/keyframe count → driven by separate `key_count`; channel/component mask →
    fixed unconditional 7-float keyframe; interpolation flag → chosen at runtime sampler). Updated
    status-summary and resolved-unknowns rows.
  - `Docs/RE/formats/authoring_sidecars.md` — NEW consolidated index of the content-pipeline
    sidecar families the shipped runtime never opens. KEY RULE: no `.map` `DATAFILE` ever names a
    `.pre`/`.post` (incl. the single `.fx7.pre` cell), so `Assets.Parsers` needs no runtime parser.
    Lands the deep-pass `.sod.pre` lean multi-polygon layout (VERIFIED across 848 files:
    `u32 polyCount`, per-poly `u32 vertexCount (3..7)` + `vertexCount × (f32 X, f32 Z)`, no Y) and
    the `.fx<N>.pre` extended 24-byte record header (6 floats) + wider vertex stride (44 vs 36,
    SAMPLE-UNVERIFIED). Cross-references `terrain.md` §5.10/§8/§10/§11/§16 and
    `terrain_layers.md` §4/§5 rather than duplicating block tables; reconciles the
    `terrain_layers.md` §4 single-polygon table as the `polyCount == 1` slice.
  - `Docs/RE/formats/terrain.md` — added a §16 forward cross-reference to the new sidecar index.
- notes: self-scrub PASS — zero decompiler autonames, pseudo-types, mangled symbols, or binary
  addresses in committed prose; `.sod.pre` size formula `4 + Σ(4 + vertexCount×8)` reconciles the
  observed file sizes (e.g. 40 = 4 + (4 + 4×8)). Names flagged for `names.yaml` (orchestrator-owned):
  `pre`, `post`, `sod_pre_polygon_list`, `fxN_pre_record_extra`. `_dirty/` sources left intact.


---

## 2026-06-14 — config_tables.md: full-record value-distribution promotion (constants -> variables + meanings)

- **Dirty source (gitignored, intact):** `Docs/RE/_dirty/campaign-vfs-deep-ii/harness/config_constants.raw.md`
  (black-box full-record harness pass over real VFS bytes; no IDA).
- **Committed spec updated:** `Docs/RE/formats/config_tables.md` (rewritten, never copied; neutral prose + tables).
- **5 constant->variable corrections** (prior "constant" claims were small-sample artifacts; corrected to
  variable with the full-record range/distribution, each marked CONFLICT-PENDING pending an IDA loader witness):
  - skills.scr +1072 (was const 0x00003000 -> interior of a CP949 string field, 54 distinct values)
  - skills.scr +1176 (was const f32 1.0 -> 8 float values 0.4-2.0; PROPOSED per-tier rate multiplier)
  - skills.scr +1306 (was const u16 7 -> 10 values, modal 5; PROPOSED max-tier / combo-depth enum)
  - skills.scr +1328 (was const 0x00010000 -> 8 values (1<<16)..(8<<16); PROPOSED stance/school bitmask)
  - mobs.scr +60 (was const f32 3.0 -> 31 values 0.5-400; PROPOSED aggro radius / move-speed base),
    mobs.scr +188 (was const f32 1.0 -> 41 values; PROPOSED HP/combat multiplier),
    mobs.scr +272 (was "6x1.0" -> 0/3997 records all-1.0; variable region). [counts as the 3 mobs corrections]
- **Proposed meanings recorded at honest confidence (no overclaim):** exp.scr +2=64 (EXP-rate divisor /
  sub-system cap, LOW); userpoint.scr +2=25 (character-creation stat-point budget, MEDIUM); exp.scr +12 / +16
  (secondary/tertiary advancement-track EXP thresholds, MEDIUM); skills.scr +260 (type/version sentinel, MEDIUM);
  mobs.scr +19 (spawn-zone label, HIGH); mobs.scr +296..+308 (two symmetric spawn variance pairs 0.95/1.05 +
  1.0 sentinel, uniform across all 3,997 records, HIGH); products.scr body fully structured into zones A-G new
  Section 2.18 (ingredients / quantities / outputs / costs / prerequisites / type; HIGH structure / MEDIUM names).
- **Names flagged for names.yaml (orchestrator to apply):** exp_rate_divisor_or_cap, secondary/tertiary_
  advancement_exp_threshold, creation_stat_budget, spawn_zone_label, spawn_stat_variance_{center,low,high,low_2},
  recipe_output_quantity, recipe_crafting_fee, recipe_type_flag.
- **Firewall:** self-scrub PASS — zero decompiler autonames, pseudo-types, mangled symbols, addresses, or pasted
  payload hex in the committed prose. Dirty note left intact as provenance.

## 2026-06-14 — VFS-DEEP-II residual text-surface promotion (asset-spec-author)

Promoted from gitignored dirty note `_dirty/campaign-vfs-deep-ii/harness/text_residual.raw.md`
(black-box harness observation of the maintainer's legally-owned VFS; no IDA used). Rewrite, not
copy; firewall self-scrub PASS (zero autonames / pseudo-types / addresses in the committed prose).

- **NEW: `Docs/RE/formats/items_csv.md`** — `data/script/items.csv`, the only `.csv` in the VFS:
  comma-delimited, **LF-only**, CP949, no header, ~33 MB. Documents the column layout (name, id,
  description, archetype/type ids, wide numeric stat tail) and PROMINENTLY flags the two CONFIRMED
  parser hazards: (A) embedded commas inside the unquoted CP949 name (col 0) and description (col 2)
  — a naive `Split(',')` corrupts alignment, so the spec gives a numeric-anchor field-splitting rule;
  (B) at least one numeric column is a float with a period decimal (e.g. `0.26`) — an integer-only
  parse inserts a phantom column. Includes a hazard-safe parsing recipe and the items.scr relationship
  (probable authoring/text-parallel export vs binary runtime db; cross-key on item_id, UNVERIFIED).
- **EXTEND: `Docs/RE/formats/text_tables.md`** — `emoticon.txt` resolved to **12-column CONFIRMED**
  (count-prefix; emote_id, emote_name(CP949), enter_state, next_state, 8× anim_id per class group;
  state-machine cols 2/3 HIGH, exact transition semantics IDA-pending). `userjoint.txt` resolved to
  **5-column CONFIRMED** (count-prefix 40; bone-index-vs-offset of cols 1–4 stays UNVERIFIED, IDA
  needed). `weather{N}_rain.txt` promoted §4.5 from UNVERIFIED to **CONFIRMED** (identical schema to
  base `weather{N}.txt`; differs only in cell values). `bmplist.txt` §3.6 alternating-line model
  CONFIRMED (even line = sequential 0-based ordinal) with the new `.txt`(runtime count) vs `.lst`
  (binary count, 30-byte stride) 8-record discrepancy documented (counts kept in dirty note, not
  transcribed). `sameemoticon.txt` promoted to CONFIRMED (§5.3, 2-col, no header/no count prefix).
- **RE-CONFIRM: `Docs/RE/formats/scr.md`** — all 44 `.scr` plus the one `.sc` sibling are BINARY
  fixed-stride tables; there is NO line-delimited/text-mode `.scr`. Re-verified this wave by head-byte
  hexdump of a random sample (non-printable binary at offset 0 on every file). Status CONFIRMED.
- **Names flagged for `names.yaml` (orchestrator-owned):** `items_csv_table`, plus already-proposed
  `emoticon_table`, `user_joint_table`, `same_emoticon_alias_table`, `bmp_texture_manifest`,
  `sky_weather_grid`.

## 2026-06-14 — asset-spec-author
- binary: doida.exe (campaign VFS-DEEP-II lane I2 — environment lighting apply-path)
- tool: none (firewall bridge — no IDA; rewrote one neutral analyst note, addresses quarantined in
  the dirty note's DEBUGGER PROBE block and NOT carried across)
- analyzed (by canonical name): environment lighting apply-path — ambient gate `K_ambient`, ambient
  base colour, `OPTION_BRIGHT` brightness slider, device ambient render-state path
- specs produced/updated:
  - Docs/RE/formats/environment_bins.md (§10.4 / §10.5 / §10.7 + Status block, Overview, family-level
    known-unknowns #11, cross-refs)
  - Docs/RE/specs/environment.md (§6.2a / §6.2b / §6.4 + §1.1 step 4, §3.2 step 5, §5.2, §7 fallback,
    §8 known-unknowns #11, Status block, cross-refs)
- notes: Promoted the lighting apply-path recovery that resolves the long-standing UNVERIFIED ambient
  numeric defaults behind the "EnvironmentNode too dark" debt. Three promotions, statically proven:
  (1) the ambient gate `K_ambient` is a static 0.0 float with exactly one reader and zero writers ⇒
  the per-keyframe §B ambient term is always ×0 (inert); the prior "sky-detail option writes it"
  hypothesis is DENIED. (2) the ambient base colour is static (0,0,0), then driven by a per-keyframe
  byte colour table. (3) `OPTION_BRIGHT` default is 100, not the previously assumed ~50 (INI default
  arg 100, clamp [1,100]→100); device additive offset = floor(bright/100×255), so at default the
  device ambient is full white (255,255,255). Spec consequence stated for the Godot layer: the modern
  ambient floor should DEFAULT to 1.0 (full), not 0.5 — the concrete root-cause fix for the too-dark
  scene. One thin residual kept UNVERIFIED: whether a user's on-disk DoOption.ini overrides the 100
  default at runtime (described as a neutral one-time runtime read; no addresses in the spec). Firewall
  self-scrub PASS — zero autonames / pseudo-types / addresses in either committed file.
- Names flagged for `names.yaml` (orchestrator-owned): `K_ambient` (ambient gate multiplier),
  `OPTION_BRIGHT` (brightness slider) — both already listed for canonicalisation in earlier entries.

## 2026-06-14 — CAMPAIGN 5 (Map Rendering & VFX Fidelity — deepening) — Tier-1 orchestrator
- binary: doida.exe @ 63fcaf8e
- tool: IDA Pro via MCP (static READONLY for recovery; serialized WRITE for the IDB-annotation phase);
  Godot 4.6.3-mono headless verify; dotnet build/test. Addresses quarantined in `_dirty/campaign5b/`,
  never carried into any committed file.
- scope: eliminate residual doubt on the map subsystems (effects/VFX, cel-shading/render pipeline,
  regions/triggers, skybox, music, water) — RE re-confirm + corrections, clean-spec upgrades, C# wiring,
  and IDB legibility.

### Deep RE closure — per-target verdicts (neutral prose; recovered facts only)
- Effect resolution: CONFIRMED the runtime resolves a descriptor through a boot-populated registry keyed
  by the RAW effect_id (the .xeff header's first u32). REFUTED the `{id}.xeff` filename-sprintf / numeric
  fallback model, and REFUTED the "resource_id − 10000" particle-index guess (resource_id is the verbatim
  particleEmitter key).
- particleEmitter.eff: REFUTED the flat 16B+52B×N table. CORRECTED to variable-length entries (28-byte
  header {entry_id, num_frames, sprite_size_x/y, max_particles} + num_frames×52B sub-record + 64B texture
  name; loop until num_frames==0). The apparent "magic 0x2711" was entry_id=10001. The 52B sub-record's
  fields past the +0x08 colour quad remain UNRESOLVED.
- FX4 terrain layer: RESOLVED (u32 tileCount; per tile a fixed 48-byte header with vertexCount@+0x28,
  indexCount@+0x2C, VF_44 vertices, u16 indices) — the earlier "second-section boundary" was moot.
- Cel/toon: CONFIRMED the luma projection c9 = BT.601 [0.299,0.587,0.114,1.0]; recovered the c4..c10 toon
  constant block (default toon light direction [-1,0,0], distinct from the scene directional light).
  REFUTED any code-set edge/outline constant — the "outline" look is produced by the post bright/glow RT,
  not the cel shader. The cel path is bound to SKINNED actors only.
- Bloom/post: CONFIRMED single-tap (only the power1 pass runs; power2/power4 are absent from the binary),
  NO bright-pass threshold, composite ≈ base×0.5 + glow×0.5, opaque present (ONE/ZERO). 4 render targets
  total (1 shadow + 3 glow/cel); NO water-reflection RT.
- Regions: zone-type enum CONFIRMED-COMPLETE {0 safe, 1 open-PvP, 2 closed}. The regiontable record is
  {zoneName[40]; zoneType@+40; _tail@+44} (48B × 32). Quest/event triggers are server-authoritative, NOT
  encoded in client region data.
- Water: CONFIRMED-NEGATIVE — the original has no water plane, no reflection, no refraction; "water" is
  generic transparent fx1..fx7 terrain overlay layers, and OPTION_WATER is a dead slider. The Godot water
  plane is therefore a documented PORT CHOICE, not a 1:1 feature.
- Sky: sun/moon are billboards orbiting angle=(tod/86400)×360° (sun X=sin×−3200, moon X=sin×+3200, moon
  phase floor((day mod 30)/2)→moon0..14.dds); the directional light itself stays static. The sky `.box`
  is CONFIRMED-ABSENT (the synthetic dome is correct).
- Music: per-map soundtable<id>.{wlk,run,bgm,bge,eff} (256×52); driven per-frame by the `.mud` cell byte
  at +2 (music-zone id) → `.bgm` cross-fade, with an indoor override; regions do NOT drive music.
- specs upgraded: Docs/RE/formats/{effects.md §E/§C.2/§F, terrain_layers.md §1.11, shaders.md §C5.4-7,
  sky.md §D, environment_bins.md §1.4, region_grid.md}; Docs/RE/specs/{effects.md, rendering.md §4-8,
  world_systems.md Ch.16-17}.

### C# wiring (clean-room engineers, layer ledgers; every offset cites // spec:)
- Layer 03 (Assets.Parsers): NEW ParticleEmitterParser (corrected variable-length layout; unresolved
  sub-record bytes preserved verbatim), TerrainLayerParsers.ParseFx4 (48B header), XobjParser
  .ParseAsMeshParticle (24-byte stride) — +37 xUnit tests.
- Layer 05 (Client.Godot): EffectRenderer unified onto the shared XeffParser (private mini-parser
  deleted; alpha-inversion / total-time / UV-scroll derivation preserved) and given the effect_id
  registry (xeffect.lst → header-keyed map, numeric fallback documented); CelShade.gdshader /
  CelShadeMaterialFactory set to the recovered toon constants (no fabricated edge term); EnvironmentNode
  glow corrected to single-tap / no-threshold / 0.5-mix; RealWorldRenderer wires the previously-DEAD
  per-cell water detection (CellHasWater) so water actually renders. No `using Godot;` below layer 05.

### IDB annotation (serialized single-writer)
- Applied 22 sub_XXXX→canonical renames + 4 overwrite-refinements + 53 neutral comments + 2 types
  (struct RegionRecord, enum RegionZoneType) across the effects/render/regions/sky/audio clusters; the
  headline correction re-labels the render-state setter previously called "fill-mode" as a Z-write-DISABLE
  setter (byte-proven). A cluster worker confabulation was caught by the orchestrator's post-apply
  read-back gate and corrected from the gate-passed manifest. IDB saved (never committed). `names.yaml`
  synced in this entry's companion edit (Campaign-5B block + the two overwrites).

### Verification (gate)
- Build: 0 errors from a FROM-SCRATCH clean rebuild. NOTE: this environment's incremental build proved
  unreliable — it masked and surfaced errors inconsistently, so authoritative verdicts required deleting
  bin/obj. En route, 5 PRE-EXISTING latent build debts (uncommitted, hidden by incremental caching) were
  fixed: two stale test files reconciled to corrected models (SoundTable 52B stride / ItemsScr Model-A),
  an ItemsScrParser iterator/Span issue (CS4007), and an ItemsCsvParser nullable-reference misuse (CS1061).
- Tests: 1593 xUnit pass / 0 fail across 10 suites (Parsers 617). Two area-2 BGM smoke tests surfaced a
  genuine `.bgm`-vs-`.eff` field-layout conflict (an id field reads 0x3F800000 = the IEEE-754 bit pattern
  for 1.0f) — deferred + documented in-test and routed to a spec-author; not a campaign regression.
- Headless: Godot 4.6.3 loads and runs 200 frames with zero SCRIPT ERROR / exception.
- Firewall: PASS (clean-room-firewall-check exit 0 tracked+staged; leak_scan 0 HIGH; no `_dirty/` tracked;
  no `.cs` cites `_dirty/`; no originals staged).
- Architecture: the downward-only DAG and engine-free core hold (engine-free parsers, global::Godot.*
  qualified, .Pipelines naming, Assets.Mapping bridge — all PASS). The dependency checker additionally
  flags 5 PRE-EXISTING reference-table edges (Application→Protocol/Crypto, Diagnostics→Kernel,
  Infrastructure→Parsers/Vfs) — all downward and acyclic, NONE introduced by this campaign.
- Residual doubt (documented, not closed): particleEmitter 52B sub-record inner fields; external
  `.psh`/`.vsh` arithmetic + toonramp.bmp quantisation bands + shipped glow config (these live in VFS
  files, not the binary — recover via asset-chain-trace, not IDA); sun/moon vertical-arc math; lens-flare
  anchor projection; the `.bgm` soundtable field-layout conflict above.

## 2026-06-14 — CAMPAIGN 6 (IDA-only total IDB legibility): W1-W3 + library-ID + struct types (WRITE to IDB)
- binary: doida.exe @ 63fcaf8e (sha256 == names.yaml pin)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*), heavy IDAPython batch (profile / RTTI harvest / call-graph / cluster / annotate / type-apply / pull)
- scope: industrial IDB legibility. Live census re-pinned the denominator at ~21,075 anonymous sub_ (the prior "4,897 named" ROADMAP figure was stale — prior-campaign IDB writes largely absent from the current i64). Profiled all 22,273 unnamed functions, harvested 431 MSVC RTTI classes (real demangled C++ names) + a 62,410-edge call-graph, partitioned into 16 subsystem clusters by anchor/RTTI/import/string seeds + call-graph propagation.
- IDB writes (every wave: dry-run -> apply, idempotent, additive, single serialized writer; re-census TRIPWIRE = 0 library functions renamed across the whole campaign; ~2,097 functions confirmed named):
  - W1 (RTTI class layer): 1,537 functions — vtable-slot functions + 615 ctors across 368 classes; the Diamond:: GU UI hierarchy's 14 vtable slots given behavioural names (setVisible/hitTest/onEvent/onDraw/onUpdate/computeTransform/...) inherited by ~400 derived widget classes.
  - W2 (crypto / vfs / parsers): 277 — incl. the in-binary FLINT bignum library (the RSA modular-exp substrate) identified to its operators, the DiskFile text-stream family, and asset-parser entry points.
  - Library-ID pass: 132 functions tagged <Lib>__ (CxImage / STL / Boost / Lua / libjpeg / XTrap); 572 total third-party functions identified and excluded from the game-code clusters. Finding: the big clusters (ui-hud, residual) are GAME-code mis-clustering by call-graph propagation, not third-party libs.
  - W3 (anim / render / scene): 158 — PoseNode/skinning + the AnimCatalog appearance-id resolver, render-pass callbacks + particle/FX runtime, the scene state-machine + the GameState/EngineView/TickScheduler objects + three C2S transition opcodes.
  - Struct-type pass: 12 structs declared in the local TIL and applied — 110 functions this-typed + 4 globals typed (g_GameState / g_EngineView / g_VfsTocBase / g_VfsTocCount); Hex-Rays field-name propagation verified.
- specs produced/updated: Docs/RE/names.yaml — 2,110 names synced from the annotated IDB (cumulative ~2,513). No other committed spec changed this campaign (W1-W3 = IDB legibility only; struct layouts are staged in _dirty/campaign6/comprehension/*/types.proposed.md for later promotion to Docs/RE/structs/). Working artifacts gitignored under _dirty/campaign6/.
- notes: neutral-prose firewall held (zero pseudo-C / autoname / raw-address tokens in any committed file; a comment-sanitiser neutralised one leaked dword_ reference). Cartography is best-effort — propagation over-attributes the large clusters, handled by per-lane analyst flagging rather than perfect clustering. Source of truth = doida.exe; the annotated IDB is the deliverable and is never committed.

## 2026-06-14 — CAMPAIGN VFS-DEEP-II (doubt-reduction & hardening) — Tier-1 consolidation

Binary: doida.exe @ 63fcaf8e. Phased fleet: 1 re-cleanroom-orchestrator (6 READ-ONLY IDA-static lanes, ≤3 concurrent on the single IDB) ∥ 8 vfs-data-analyst harness lanes (research) → 11 asset-spec-author promotions (one-writer-per-file) → assets-parser / assets-mapping / godot-shader engineering (serialized on the build DAG) → csharp/perf/architecture/clean-room review + fix wave. Goal: crush the residual UNVERIFIED markers left by CAMPAIGN VFS-DEEP and reviewer-grade the C#. Firewall held: all raw findings under `_dirty/campaign-vfs-deep-ii/` (gitignored; debugger-probe addresses confined there); every promotion a REWRITE. The per-lane / per-author sub-entries above (lanes I1/I2/I4, sound-tables, config_tables, text-surface, environment, …) carry the detailed provenance — this is the rollup.

- **MAJOR CORRECTIONS (prior knowledge refuted, empirically/loader-confirmed):**
  - `items.scr` is a FIXED 548-byte (0x224) record + `8*effect_count` tail (count u8 @0x220), **90,937 records, EOF-clean** — REFUTES both the old §1.4 "floating stats block at 0x38+desc_width" and the harness "3-sub-record [A,B,C]" model (both were CP949 high-byte segmentation artifacts; whole-file walk confirms 548+8N).
  - `citems.scr`: `item_name` @0x04 (48B); the documented `item_ref` u32 @0x04 DOES NOT EXIST; description = SIX fixed 81-byte paragraphs @0x0E4/0x135/0x186/0x1D7/0x228/0x279 (not a single buffer near 0xDC).
  - environment "too dark" ROOT CAUSE: `K_ambient` is a STATIC 0.0 (one reader, zero writers) → keyframe ambient is inert; `OPTION_BRIGHT` INI default = 100 (not ~50) → legacy device ambient = full white. Modern ambient floor must DEFAULT to 1.0.
  - `bgtexture.lst` `kind` u8 is a material RENDER-MODE tag (0x01 static / 0x02 scroll-UV / 0x0A grass / 0x0B plant / 0x0C bark / 0x14 foliage), not a static/animated flag (227/2330 records ≠ 0x01).
  - `.mud` sound source = `soundtableNNN.{bgm,bge,eff,wlk,run}`, 256×52B (NOT 48), `sound_id` u32@0x00 + `weight` f32@0x1C → `data/sound/{2d,3d}/{id}.ogg`; mud bytes 0/1 PLAUSIBLY wlk/run footstep indices.
  - terrain height grid row-major **X=column** (resolves terrain.md §5.1); texture index = **idx-1** (resolves asset_pipeline.md §B conflict). Both already correct in code; only the docs hedged.
  - `events.scr` client loader reads only event_id@0x00 + mode_flag u16@0x64 + rate_array@0x68 (/1e6) + actor_array@0x130; the flag/reserved/trailer fields are present-but-unread; `autoquestion_cl.scr` has NO client loader (captcha graded server-side).
- **PROMOTION (committed specs):** items_scr.md, events_scr.md, mud.md, sound_tables.md, environment_bins.md, mi.md, terrain.md, terrain_layers.md, config_tables.md, text_tables.md, scr.md, bgtexture_lst.md, xdb_tables.md, animation.md, asset_pipeline.md, environment.md; NEW: items_csv.md, authoring_sidecars.md.
- **C# (build slnx 0 warnings / 0 errors; 1605 tests green, 10 projects):** ItemsScrParser (full Model-A walk), CitemsParser (corrected), SoundTableParser+SoundTable (new), MudSoundGridParser (wlk/run + resolver), ItemsCsvParser (embedded-comma + float hazards), BgtextureLstParser (BgTextureKind enum), AnimationParser (track upper-bytes CONFIRMED reserved), EventsScrParser (loader contract), ConfigTableParser (constant→variable), MobInfoPanelParser (enriched), TerrainGltfConverter (axis docs), EnvironmentNode (ambient floor 1.0 — too-dark FIXED, headless RC=0 + screenshot). Reviewer-grade hardening: self-contained bounds guard, signed-numeric guard, per-record stackalloc, encoding hoist, closure→static local fns; pre-existing EffectRenderer CS8601 also cleared.
- **names.yaml:** +9 LOCATED loader/lighting functions (items.scr `ItemsScr_LoadRecord`/`ItemsScrRecord_Ctor`; terrain `Ted_LoadCellTerrainBlob`/`TileTerrain_SetTextureId`; `Map_ParseDescriptor`; events.scr `EventsScr_LookupById`/`EventsScr_ConsumeRecord`; lighting `Lighting_ApplyBrightnessAmbient`/`Renderer_SetDeviceAmbient`) staged for a later annotation phase (addresses kept in names.yaml, the whitelisted glossary).
- **GATES:** clean-room-auditor PASS (firewall held; 18 specs journaled; `_dirty/` untracked) — audit: `Docs/RE/audits/audit-2026-06-14-campaign-vfs-deep-ii.md`; architecture-guardian PASS (zero new edges, zero csproj change, zero engine leak; the 5 DAG findings are pre-existing drift, unchanged from HEAD).
- **RESIDUAL / debugger-pending** (probe plan: `Docs/RE/debugger_probe_plan.md`): `mobinfo.mi` 7-field semantics (loader opened via by-name VFS reader, not statically locatable); items.scr stat-field roles across families; runtime DoOption.ini override of OPTION_BRIGHT/K_ambient; mud bytes 0/1 wlk/run re-verify. CAPTURE-pending items (combat/chat on-wire) stay out of scope (no .pcapng in the tree).

## 2026-06-14 — CAMPAIGN 6 (cont.): W4 UI + W5 world/lua/sound + libVorbis + names.yaml re-sync
- binary: doida.exe @ 63fcaf8e ; tool: IDA Pro 9.3 via MCP, IDAPython batch ; all writes dry-run -> apply, additive, single-writer ; re-census TRIPWIRE = 0 library-renamed throughout.
- W4 (ui-toolkit / ui-hud / actor-combat): 100 names (GUScroll/GUScrollEx state machine + GUTextureList/GUCmdHandler structs; chat slash-command interpreter + 1000x36B chat ring; 5x8 trade grid; ActorBuffArray 30x12 + SkillActionRecord 1468B; closed actor.md level@+0xBA). C11 confirmed ~185 genuine of 5,941 (rest = STL/thunks/propagation noise).
- W5 (world / lua / sound): 522 — 80 HUD window-manager + NPC/quest (world, with the panel-index->subsystem map + ItemSlotRt/QuestTemplateRt structs); 31 lua_tinker binding glue + 391 stock Lua 5.1.2 VM tagged LIB-Lua; 20 sound/input glue (GSoundThread queue, 3D audio, registry/INI settings persistence, CP949 pair test).
- libVorbis 1.3.2 OGG decoder: 273 functions band-tagged libVorbis__ in 0x6dd000-0x6f3000 (range independently identified by two W4/W5 lanes via the "Xiph.Org libVorbis 1.3.2" string).
- specs produced/updated: Docs/RE/names.yaml re-synced from the IDB — cumulative ~3,400 entries (campaign ~2,992 named/tagged + prior 403). No other committed spec changed; struct layouts staged in _dirty/campaign6/comprehension/*/types.proposed.md for later promotion to Docs/RE/structs/.
- CAMPAIGN 6 TOTAL: ~2,992 functions named/tagged across 5 waves + library-ID + struct types (12 structs declared, 110 this-typed, 4 globals). High-value naming complete: all 15 game-code clusters + the statically-linked third-party libraries (FLINT/CxImage/Lua/libVorbis/zlib/libjpeg/STL/Boost/XTrap/BugTrap) identified. Remaining surface = the C16 residual ~8,900 low-value leaf/thunk/STL tail (optional bulk auto-name). Source of truth = doida.exe; the annotated IDB is the deliverable and is never committed.

## 2026-06-15 — CAMPAIGN 7: Re-anchor the corpus onto a NEW doida.exe build (IDA-only) — Tier-1

- binary: NEW build `doida.exe` @ sha256 `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee` (≠ the prior pin `63fcaf8e…9eb9df`). The prior IDB crashed unrecoverably; a fresh IDB was rebuilt (auto-analysis + decompile_all). Verified live: all 14 sampled prior-anchor addresses miss (not function starts) → **ADDRESSES DO NOT TRANSFER between builds**. RTTI (406 classes — same as prior) + signature strings + imports DO transfer → re-anchor by **CONTENT**, never by address. tool: IDA Pro 9.3 via MCP, heavy IDAPython batch; all writes dry-run→apply, additive, single serialized writer; re-census **TRIPWIRE = 0** throughout.
- METHOD (one new apparatus): a content re-anchor matcher (`_dirty/campaign7/tools/content_reanchor.py`) + a BinDiff-style call-graph propagation matcher (`cg_propagate.py`) seeded by the RTTI anchors. Both emit build-2 glossaries the existing `/ida-annotate-batch` consumes unchanged (only the SHA pin differs). Every batch: dry-run → independent adversarial audit (default-refute) → apply → read-back → re-census.
- RE-ANCHORED & APPLIED (cumulative ≈2,176; audited 0 structural FP on the applied bands; TRIPWIRE 0):
  - Phase A′/D0: **721** via RTTI deterministic slot/ctor join (406/406 classes matched by demangled name + base_chain; 0% FP / 56 audited).
  - Phase B1: **1,122** via call-graph propagation (margin ≥ 0.16; the margin==0.15 floor band was demoted after an independent adversarial audit found its FP concentrated there — 22% floor vs 0% strong band).
  - Phase B MED-verify: **322** of 504 ground-truth-verified candidates promoted (PASS+REVISE). KEY FINDING: the call-graph propagator **systematically mislabels RTTI constructors** → all ctors verified/corrected against vtable-write → COL → TypeDescriptor.
  - ctor-QA on the already-applied set: **11** ctors found wrong-class (5% of applied ctors) and corrected (incl. a 4-cycle CameraManipulator swap).
  - Phase B2 (expanded propagation, ~2,343 seeds, cap 60): **SATURATED** — 1 new confirmed HIGH; the propagator cannot distinguish the GU Panel ctors (identical factory `sub_53EB62` shape, margin=0.25 = FP signature). Automated re-anchoring is exhausted at ≈64% of the prior corpus.
- DURABILITY: **`Docs/RE/names.build2.yaml`** — NEW committed file, re-pinned `263bd994…` (prior_sha256 recorded), 2,591 functions + 19 globals, neutrality PASS — crash-proofs the re-anchored names WITHOUT overwriting the curated old-build `names.yaml`. The old 3,417-entry `names.yaml` is preserved (commit 8918ece) for later re-anchor of the residual. NOTE: a handful of build2 notes still cite stale old-build string VAs (`@0x…`) — cosmetic, flagged for cleanup; globals/data-globals not yet re-anchored.
- RESIDUAL (Phase-B comprehension worklist, ~1,057 prior names unplaced — automation can't reach them; they sit in regions without anchored neighbours): effects-render 161, rtti-class-core 272, network 121, scene 77, vfs-assetio 77, anim-skinning 75, crypto-session 48, render-pipeline 43, misc. The interop spine (ItemsScr/Ted/Sky/Sound/network/crypto loaders) is enumerated in `_dirty/campaign7/anchor/fresh_comprehend.md`; the committed specs already carry that behaviour, so the 1:1 port is not blocked.
- FIREWALL: held — all working artifacts under `_dirty/campaign7/` (gitignored); every applied name/comment neutral-prose (no pseudo-C / autoname / new-address tokens); the IDB never commits. Reusable apparatus staged in `_dirty/campaign7/tools/`. Source of truth = the new `doida.exe`; the annotated IDB is the deliverable and is never committed.
- PHASE-B RESIDUAL WAVES (à-fond, 2026-06-15) — processed ALL remaining unplaced prior-name clusters: **A1** rtti-residual deterministic vtable/ctor placement **+153** (0% FP / 30 audited; resolves the GU Panel-ctor blind spot via vtable-write→COL); **B** net/crypto spec-guided **+57** — DISCOVERED the prior `net-dispatch` names were STL-mislabeled, and LOCATED the real S2C handler family (dispatcher `0x5f6a02`, installers `0x5f6383` Response / `0x5f6777` Push ~67 handlers, NetClient ctor `0x6191df`); **C** asset/render spec-guided **+168**; **D** scene/untagged **+7** (incl. globals `g_GameState`/`g_EngineRunFlag`/`g_FrameTickScheduler`). Across B/C/D **≈509 prior names REFUTED** as library-masquerade (STL/Boost/Vorbis/CxImage/FLINT/Lua mislabeled in the old corpus) or stale-inlined — refuting them is CORRECT and improves corpus quality. Each wave: spec-oracle + decompile-confirm + ≤3 readers, applied via a single serialized writer; TRIPWIRE 0 throughout.
- FINALIZE (Phase E, per maintainer "merge") — `Docs/RE/names.yaml` REPLACED with the re-pinned build-2 corpus: sha256 `263bd994…` (prior_sha256 recorded), **2,970 functions (2,132 game + 838 library-tagged) + 19 globals**, **2,414/3,398** prior names placed, neutrality PASS (firewall guard on write). The old 3,398-entry build-1 names.yaml is preserved in git history (commit 8918ece). `names.build2.yaml` removed (redundant). Cumulative content-re-anchored ≈**2,561 functions** on the new build (721 RTTI + 1,122 CG + 322 MED + 11 ctor-fix + 385 residual waves − overlaps).
- FOLLOW-UP at first close (since ADVANCED by Waves E/F below): the real S2C handler family + ~70 scene functions were the documented signature-recovery worklist; rtti-MED ambiguous ctor variants (63) remain. Worklist: `_dirty/campaign7/anchor/{fresh_comprehend.md, waveB_report.md, waveC_report.md, waveD_report.md, unplaced.json}`.

## 2026-06-15 — CAMPAIGN 7 (cont.): Wave E (S2C+scene) + Wave F (high-value unnamed) — heavy fan-out

- Two ~20-agent **Workflow** runs (deterministic dump-once → wide NON-IDA fan-out → adversarial IDA verify; live-IDA stayed **≤3 readers** throughout; a single serialized writer applied results via SHA-guarded py_eval). 40 agent-deployments total.
- **Wave E (S2C handler family + scene):** dumped 250 bundles → 237 named → **207 applied** (0 FP / 51 audited; TRIPWIRE 0). Recovered the full S2C dispatch family (`SmsgEnterGameAck`/`SmsgStatUpdate`/`SmsgUserTradeSlotUpdate`/`SmsgNpcBuyOrAcquireAck`/… mapped via opcodes.md + packets) + scene singletons — the prior corpus had these mislabeled or absent.
- **Wave F (highest-value still-unnamed game functions):** triaged the most-referenced `complex`/`dispatcher` `sub_` (excluding lib/thunk/already-named) → 160 bundles → 138 named → **138 applied** (0 FP / 46 audited; TRIPWIRE 0). e.g. `ActorVisual_ApplyWalkRunMotion`, `GSound_SetVolumeFromAmplitude`, `DiskFile_CloseAndReset`, `Hud_BeginOrthoRender`, `MainWindow_GetSingleton`, `Vec3_Lerp`/`Matrix4_InvertOrthonormal`.
- **+345 functions** beyond the re-anchor phase. IDB now **≈4,685 user/FLIRT-named of 25,791** (≈2,467 canonical game + 848 library-tagged).
- **names.yaml re-synced (final):** re-pinned `263bd994…`, **3,315 functions + 19 globals** (2,467 game + 848 lib), neutrality PASS. Firewall held (the 40 fan-out agents wrote only `_dirty/`; the IDB never commits; applies are neutral-prose + value-sanitised). Reusable fan-out apparatus: the two Wave E/F workflow scripts + the universal py_eval applier. STILL uncommitted on branch campaign3.

## 2026-06-15 — CAMPAIGN 7 Phase S: promote recovered interop knowledge to clean specs (firewall crossing)

- 5-lane parallel spec-author promotion (one writer per file; spec-authors have NO IDA → no IDB contention), dirty→REWRITE→clean, build-pinned `263bd994`. Independent firewall gate PASS: zero Hex-Rays identifiers / zero IDA-address tokens across the 5 files (the only long-hex is the legit `0x29` XOR whitening key).
- **opcodes.md** — ~120 S2C rows (major 1/3/4/5) enriched with per-handler ROLE from Wave E; **2 NEW** opcodes (4/143 SmsgTrackedItemPanelToggle, 4/144 SmsgTrackedItemRecordFold, status `observed`). Catalog validator clean (218 rows, 0 dup, 0 address tokens).
- **specs/network_dispatch.md (NEW)** — S2C receive-dispatch architecture: master dispatcher (major@+4/minor@+6, decompress-then-route, 154-slot tables), Response/Push installers (~98/~65 slots), NetHandler object, NetClient lifecycle (construct/StartNetworkEngine/worker/recv-loop/keepalive/Disconnect), connection-state machine (codes 201/202/203/232 + timed event 10001).
- **specs/crypto.md** — Wave B re-confirmed the cipher/LZ4/RSA/whitening/page-guard stack on the new build (§9.1); secure-context page pinned 0x2E20 (11808B, distinct from the 0x2DA0 inbound LZ4 cap); login form = TAB-delimited key string (account & password ≥2 chars). The §6b debugger-verified facts kept precedence.
- **packets/5-53_actor_vitals_and_pair_state.yaml** — 32-byte payload (HP/MP/Stamina/VitalC + level/state + couple/pair sub-mode; (sort@0,id@4) actor-key); size==sum(widths) validated.
- **specs/resource_pipeline.md** — §1.5 VFS runtime: mount (data.inf 24B header → entry_count → 144B-stride TOC → data.vfs handle), CS-locked entry read, find-and-read chokepoint, 3-way open router, 64-bit seek. Byte layouts deferred to formats/pak.md (cited, not duplicated).
- VERSION DELTAS flagged for capture/debugger arbitration (NOT silently overwritten): **5/28 SmsgRespawnAtPoint** carries position IN-BODY on build-263bd994 (old spec inferred id-only/cached); **4/143+4/144** = one physical handler branching on the minor word (observed). Secure-context page size newly pinned.
- specs touched (for commit/auditor): opcodes.md, specs/network_dispatch.md (new), specs/crypto.md, packets/5-53_actor_vitals_and_pair_state.yaml, specs/resource_pipeline.md. (specs/frontend_scenes.md shows modified but is PRE-SESSION state, untouched by Campaign 7.)

## 2026-06-15 — CAMPAIGN VFS-MASTERY (VFS-DEEP-III): exhaustive re-derivation of every data.vfs format from IDA + black-box cross-instance validation + C# hardening

- **Method = two independent witnesses per format, then a HARD GATE.** Every format's on-disk layout was re-derived two ways and reconciled: (A) an IDA **loader-witness** (read the actual loader routine on the new `doida.exe` build 263bd994, READ-ONLY) and (B) a **black-box witness** (open ALL real instances in the 43,347-entry VFS via a throwaway harness driving the production parsers; stats over the whole population, never one sample). A field reached CONFIRMED only when both agreed; disagreements stayed UNVERIFIED; runtime-only fields are honestly marked DBG-pending (no debugger this cycle, by choice). Dirty work under `_dirty/campaign8/` (gitignored); reconciliation in `_dirty/campaign8/reconcile/{W1-synthesis,W3-promotion-map}.md`.
- **Phase A cartography (3 IDA readers):** mapped the complete read path (mount data.inf 24B header → 144B-stride TOC → binary-search by name → CS-locked seek+read from data.vfs → 3-way open router) and a caller→format census = **8 loader families** (terrain/character/items/sky-env/audio/effects/ui-icons/bulk-loader). C05 "vfs-assetio" cluster denoised (104 fns → 53 genuine-VFS / 40 GHTex-cache / 11 noise). The C7 "undefined gap 0x608C70–0x608E97 = DiskFile I/O" hypothesis was **REFUTED** (it is a graphics render-submission routine) and 5 DiskFile primitives were found MISLABELED-PRIOR and corrected.
- **Phase V black-box (16 lanes) + Phase B comprehension (12 IDA lanes, sub-waves ≤3):** cross-instance validation of every family over the full VFS, then independent loader re-derivation + adjudication of the V conflicts.
- **Methodology win (recorded):** the gate caught issues a single witness would have gotten wrong in BOTH directions — black-box correctly found **sound-table stride = 48, not 52** (loader does `add 0x30`×256 + a 1024B unread trailer; the spec's 52 was a 13312/52=256 coincidence → C# `SoundTableParser` was wrong, now fixed); AND the loader-witness **vindicated regiontable stride = 48** against a black-box "32" false-correction (the 32 was a conflation with the 28B npc.arr record the same loader reads) — single-witness promotion would have shipped that regression.
- **Other CONFIRMED corrections promoted (Phase P, 21 committed `formats/*.md`, one author per file, firewall PASS):** `.ted` TextureIndexGrid value 0 → **clamp-to-1** (not a no-texture sentinel) and idx−1 confirmed; `.bud` vertex cap = **warn-and-continue full count** (legacy never throws → fixes a real layer-03 parser bug on 4 oversized cells); `.fx` `type_tag` = a **group COUNT** (refutes both the spec's "constant=1" and a black-box "sub-format selector"); `.xeff` track header = **9 bytes in both paths** and the `unknown_constant` field **does not exist** (the live "67" is the low byte of `anim_stride`); `users.scr` = a **single 496B blob** addressed by a grid formula (not 4×124); `items.scr` discriminator is **+0xBA != 14** (not +0xB8); `actor_size.xdb` and `weather_rain.bin` are **DEAD in this build** (no loader → a faithful port must not load them); `mob.arr`/`mobinfo.mi` have **no client loader** (tool/editor formats); `chatfilter` confirmed **absent**; plus `.mud` effId2-consumed/walk-run-refuted, `.sod` +36 = padding, env fog flag=1 = literal black, stardome per-star tint, region origins i32-signed, ui manifest counts EOF-driven (uitex 37 / crestlist 1952), discript.sc = UI context-menu labels.
- **Phase D (1 serialized IDB writer):** 172 renames + 240 neutral comments + 1 define_func applied to the live IDB (idempotent; TRIPWIRE/NOISE-as-VFS = 0; IDB saved, never committed) — incl. the 5 corrected DiskFile primitives, `Render_SubmitDrawBatch` (the ex-gap), `CoreMot_LoadHeader/LoadFullData`, `SoundTable_LoadFiveTables`, `NpcArr_FindRecordById`, and 12 families' name/comment manifests. Full applied list: `_dirty/campaign8/applied/phase-d.md`.
- **Phase E (C# hardening, disjoint-file lanes):** corrected the parsers per the two-witness verdicts (SoundTableParser stride 48 + 1024B trailer; TerrainScene/.bud warn-continue; Ted clamp-to-1; TerrainLayer group-array model; XeffParser 9B header / removed UnknownConstant; ConfigTable users.scr 496B; ItemsScr +0xBA; EnvironmentBin default-tolerance + dead-table guard; Xdb effectscale/creature_item; Region origins i32). Models updated in lockstep; DBG-pending fields kept as opaque slices (never fabricated). **+~220 new xUnit test facts** for previously-untested parsers. Downstream consumers migrated to the corrected models: `Assets.Mapping/XeffJsonConverter`, `Client.Domain/Simulation/RegionCatalog` (origins → int), and Godot adapters `ZoneCatalog` (zone-name resolution now via the recovered grid→region-id→ZoneName chain) + `VfsRegionSource`.
- **Gates:** full-solution **nuked** build **0/0**; `dotnet test MartialHeroes.slnx` = **1848 passed / 0 failed**; clean-room audit **PASS** (0 HIGH leakage, no `_dirty/` tracked, no `using Godot;` below layer 05, magic constants cited). The dirty→clean firewall held (EU Art. 6).
- **Honest residual register (DBG-pending — documented, never guessed):** mobinfo.mi/mob.arr field semantics (no client loader, likely permanent); `.xeff` sub-record float fields + emitter_type=20 render meaning; sound `hour_schedule` mask gating consumer; `mapsetting +0x44/+0x48/+0x4C`; `userpoint`/`items.scr`/`config_tables` deep semantic fields; one indoor-area SkySystem_Init bypass.
- **FOLLOW-UP:** (1) `names.yaml` sync of the campaign8 VFS function names — the names are durable in the annotated IDB + staged in `_dirty/campaign8`; regenerate via `ida-naming-sync` (incl. the `.skn id_b` vs `skin.txt col2` "IdB" disambiguation and bindlist=349). (2) Godot presentation TODOs deferred to avoid destabilizing the concurrent campaign3 workstream: class→skin_class formal table, audio per-bus option store, EnvironmentNode lighting-from-env-bin, inventory-title msg.xdb id (unrecovered → defer), water unwired-by-choice memo.
- FIREWALL: held throughout. All recovery under `_dirty/campaign8/` (gitignored); committed specs carry neutral prose only; the IDB is the annotation deliverable and never commits. STILL uncommitted on branch campaign3 (targeted-paths commit on maintainer request — the tree is entangled with prior campaign3 work).

## 2026-06-16 — CAMPAIGN 10 (Total Client Comprehension & Doc Re-Verification)

- binary: `doida.exe` @ `263bd994…` — **static IDA + VFS observation only** (no debugger / no packet capture this campaign, by the maintainer's choice).
- tool: IDA Pro 9.3 via MCP (`mcp__ida__*`, read-only) + the `vfs-inspect` harness over the maintainer's own `data.inf`/`data.vfs`.
- method: instantiated `Docs/CAMPAIGN_TEMPLATE.md` as **7 domain blocks** — A boot/runtime construction, B scene/window state-machine + Diamond UI framework, C VFS/asset-IO + resource pipeline, D the asset-format corpus (two-witness), E network/protocol/crypto, F gameplay systems, G rendering/effects/terrain/skinning/environment/sound. Each block: a massively-parallel dirty-room read wave re-confronting **every committed spec's claim/offset/constant to the live IDB** (and, for formats, to a real VFS sample), then one-author-per-file clean-room promotion with a machine-checkable `verification:` banner; a **Tier-1 firewall scan per block (all PASS)**.
- analyzed (by canonical name/subsystem): the `WinMain` scene state machine (`GameState 0..7`), the engine run-loop + QPC frame-limiter + device-step/Present + device-lost recovery, the Diamond `GU*` widget framework (`GUComponent`/`GUPanel`/`GUWindow` two-vtable MI) + `MainMaster` window-manager service-slot table + every front-end window `construct()` element-by-element, the VFS path (`Vfs_Mount`/`FindEntry`/`ReadEntryData`) + the `data.inf`/`data.vfs` container, the asset/resource pipeline + boot data-table corpus + the `TerrainLoader`/`TerrainManager` streamer, the inbound dispatcher (`Net_DispatchInboundByMajorMinor` + the two 154-slot Response/Push tables) + the outbound keyless cipher + the `0/0→1/4` FLINT/RSA handshake + keepalive, the gameplay systems (battle controller, skills/hotbar, inventory/trade/equip-visual, progression, quests, chat/social, npc-interaction, minimap, camera/movement, Lua), and the render/effects/skinning/environment/sound runtime.
- specs produced/updated: **NEW `specs/client_architecture.md`** (the master top-level synthesis); **re-verified + corrected the ENTIRE committed `Docs/RE` base** — 37 `specs/`, 32 `formats/`, 10 `structs/`, ~80 `packets/`, plus `opcodes.md` — each stamped with a `verification` banner (`ida_reverified: 2026-06-16`, `ida_anchor: 263bd994`). **NEW `Docs/PLAN.md` + `Docs/ROADMAP.md`** (campaign charter + dated run record).
- key corrections (the docs were NOT 100% — these were caught & fixed): scene machine is **0..7** (8 = an exit sub-state); the flow is **login(1)→load(2)→opening(3, post-login)→char-select(4)→world(5)**; per-frame loop is **fixed ~60 FPS** (rate field `engine+0x30` = `scene+48`); **crypto OQ#1 RESOLVED** — inbound is **LZ4-decompress only, NO inverse cipher** (single-caller proof); frame header = **`[u32 size][u16 major][u16 minor]`**; the **major-3 opcode ladder de-swapped** (3/4 SceneEntityUpdate / 3/7 CharManageResult / 3/14 CharSpawnResponse); **GUComponent geometry was transposed** (width/height/posX/posY corrected); UI layout is **code-baked**, **no EULA panel** (msg 4001-4022 = server-list captions); VFS header = **`VFS001`** + 144-stride TOC + 3 FILETIME, storage **RAW (ReadFile, not mmap)**; terrain = **TWO singletons** (34-slot pool / 25-slot ring); sound stride **48**; `.do` stride **116**; `mobinfo.mi` **present** in the VFS; `items.csv` **authoring-only**; the effects **Z-negation is port-side only** (the original binary applies none).
- IDB annotation (Phase D): applied the reconciled glossary — **201 addresses** (113 renamed sub_→canonical + 82 already-canonical), **201 neutral repeatable comments**; `sub_` count **19,090 → 19,020**. IDB **never committed**. 6 ctor name-collisions (the campaign re-identified the real ctor addresses) flagged for the `names.yaml` sync adjudication.
- firewall: **HELD.** All dirty work under `_dirty/campaign10/` (gitignored); committed specs carry neutral prose only; Tier-1 firewall scan PASS per block (the one real leak — a Hex-Rays `_QWORD` type name in `structs/npc.md` — was fixed); no pseudo-code, no code addresses, no capture/payload bytes. EU Art. 6 preserved.
- residual (honestly flagged, never guessed): packet field VALUE semantics, server-authored magnitudes (damage/cooldown/XP/HP scale), and matrix-major/up-axis are **capture/debugger-pending** (no debugger or wire capture this campaign). FOLLOW-UP: `ida-naming-sync` to pull the ~195 live IDB names → `names.yaml` (+ adjudicate the 6 ctor collisions); **Phase E** (align the C#/.NET core + Godot client to the corrected specs — load-bearing: skinning math, UI geometry, the opcode ladder + frame-header, the VFS header). STILL uncommitted on branch campaign3 (targeted-paths commit on maintainer request).

## 2026-06-15 — CAMPAIGN 9: LoginWindow + CharactersWindow total comprehension → 1:1 Godot port
- binary: doida.exe @ 263bd994 (x86 32-bit, imagebase 0x400000), IDA Pro 9.3 via MCP. Phases A/B/D used the IDB (READONLY comprehension, then 1 serialized WRITE annotator); the VFS track used black-box harness observation over the real 43,347-entry VFS (no IDA). Mandate: EXHAUSTIVE re-walk of both front-end scene classes + skinning, full IDB annotation, and a Godot engineering wave — autonomous.
- analyzed (by canonical name/subsystem):
  - **`Diamond::LoginWindow`** (entry `Diamond_LoginWindow_BuildScene`): atlas-loader lifecycle (`Texture_LoadFromVfsOrDisk` — NO cache / NO ref-count, eager preload of 4 atlases, `GUTextureList` handle list, release on End); per-widget font = **slot 0 (DotumChe) universally**; draw = child-vector insertion order, fade ±64/tick, forced-alpha (+0x0F) present-but-never-armed; the intro "curtain" is a **two-panel vertical slide (+5 px/tick)**, not an alpha ramp; the substate field is a **{1–6, 29–41} = 19-state** machine ("41" = max value, 7–28 gap); real input router `LoginWindow_OnEvent`; server-select plates 400/401 + pagers 115–124 + load thresholds 1200/800/500; PIN modal = Fisher-Yates `srand(_time64)` scramble-on-show, ≤4 digits → 3rd TAB token of the 1/4 second-password blob; 73 widget ctor sites; LoginWindow struct (dual-vptr, GUTextureList +0x220, substate +0x238, child-vector +0xA4).
  - **`Diamond::CharactersWindow` / `SelectWindow`**: **the "6-keyframe camera orbit" is REFUTED** — no keyframe array / no sin·cos / no lerp·slerp·ease exists; the scene uses a **single STATIC perspective camera** (anchor +2048,0,−6144; FOV 50 / near 5 / far 15000) plus two manual hold-to-move inputs (camera boom-zoom ±10 u/s no clamp; preview-ACTOR yaw ±2 rad/s on the selected actor). Slot selection = **3D world-space ray-pick** (unproject → 5 per-slot AABBs, ±6 X/Z, Y 70..92), NOT 2D rects. Env: keyframe-29 PINNED, the per-keyframe ambient table is INERT (×K_ambient=0), real fill = a **white device ambient floor** (OPTION_BRIGHT=100), directional ≈0.047 grey, **fog OFF** behind the row; area-0 tables are static + achromatic. Single ambient XEffect 380003000 (`char_select-u.xeff`, 68 sub-effects) at the row pivot. BGM 920100200 on one category-0 voice (double-voice defect REFUTED); login is BGM-absent. Create-form class map {0→4,1→1,2→3,3→2}; starter gear SERVER-assigned; create=1/6 (52B), slot-select=1/7 (2B); face +/- mutates a 2D appearance index only (no 3D rebuild). CharactersWindow struct (previewActors[5] +6220, activePreviewActor +6240, yaw accumulator +6264, vtable 15 slots).
  - **Skinning deform math** (`.skn`/`.bnd`/`.mot`): LBS with QUATERNIONS (no 4×4 matrices) `v'=Σ wᵢ·(boneWorldQuatᵢ ⊗ (localPosᵢ·scale)+boneWorldTransᵢ)`, parent-on-left product; inverse-bind COMPUTED-not-stored; `.mot` 10fps keyframe 28B lerp-translation + shortest-arc-slerp; no axis flip inside the math (single uniform handedness conversion required); meshScale is real (skin +128). The committed `skinning.md` was RATIFIED correct.
  - VFS black-box (own samples): area-0 light0/fog0/material0 keyframe-29 value table; cell `d000x10000z9990` components (.sod = 4 collision quads, `.up` ABSENT, 11 backdrop textures resolve, + a sidecar `data/effect/map/<cell>.bmp`); `char_select-u.xeff` carries id 380003000; bgm 920100200.ogg present.
- specs produced/updated (Phase C, neutral, firewall PASS, one author per file): `specs/frontend_scenes.md` (§1 login asset/font/z-order/curtain; §3.5 STATIC-CAMERA REWRITE; §3.3.3 ray-pick; §3.6 env truth; §3.7 cell; §3.8 BGM; §4 create), `specs/ui_system.md` (§6 font slot 0, §7 z-order/fade, §8.1=73 widgets, §8.2, §9.0 atlas-loader lifecycle), `specs/sound.md` (login BGM-absent), `formats/environment_bins.md` (area-0 keyframe-29 value table + static/achromatic note), `specs/camera_movement.md` (§A.5.2 stale char-select orbit → supersede-redirect), `specs/skinning.md` (ratified + meshScale + deform math), `specs/login_flow.md` (PIN scramble + create/slot-select cross-refs).
- IDB annotation (Phase D, 1 serialized writer): **104 renames (98 fn + 6 global) + 147 neutral comments + 4 struct types** (`SknWeightRecordDisk`, `SknVertexDisk`, `MotKeyframe`, `BndBoneMem`); sub_ 19185→19090 (−95); conflicts arbitrated (0x5fa382 comment-only; PIN keypad named; the refuted +6032 actor-array dropped); 0 library/CRT symbols renamed; IDB saved, never committed. Pull-backs under `_dirty/campaign9/applied/`.
- engineering (Phase E, layer-05 Godot, disjoint-file lanes — NOT RE, provenance pointer): char-select environment rewired to the real area-0 truth (white ambient floor / 0.047 directional / fog-off / achromatic — fixes the "too dark" debt); row-Y driven from the terrain ground sampler; the static camera + the 2 manual inputs + the `TryHitTestSlot` 3D ray-pick implemented (and the dead 6-pose-button orbit scaffolding REMOVED); login curtain switched to the vertical-slide model + version-gate + eager atlas preload; BGM moved off login onto char-select; skinning files spec-aligned (the mesh-explosion debt was already retired in a prior pass — verified coherent + animating). 1848 xUnit tests unaffected (layer 05 is uncovered).
- gates: full-solution build **0/0** (incremental + `--no-incremental` concordant); `dotnet test MartialHeroes.slnx` = **1848 passed / 0 failed**; clean-room audit **PASS** (0 CRITICAL leakage, no new `_dirty/` citations, the camera-orbit refutation internally consistent, magic constants cited). Headless boot clean.
- notes: All dirty recovery under `_dirty/campaign9/{login,chars,skinning,applied}/` (gitignored); committed specs carry neutral prose only; the IDB annotation never commits (EU Art. 6 firewall held). The load-bearing correction is the **camera-orbit refutation** (the Godot client was about to implement a non-existent 6-keyframe orbit; it now matches the real static camera). FOLLOW-UP: (1) `names.yaml` sync of the 104 campaign9 IDB names (durable in the IDB + staged in `_dirty/campaign9/applied/`; regenerate via `ida-naming-sync`); (2) Phase Dbg live-debugger confirmations (camera resting eye/target exact numbers, a SKIP-keypress curtain branch, the fog-OFF suppression site) are documented as debugger-pending, never guessed; (3) wire the new `CreateCharacterRequested` Godot signal to the Application create use-case. STILL uncommitted on branch campaign3 (targeted-paths commit on maintainer request).

## 2026-06-15 — CAMPAIGN 9 WAVE 2: screenshot-grounded re-investigation (camera CORRECTION) + front-end fidelity
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY) + black-box VFS harness. Driven by the maintainer's own official-client screenshots (login/PIN/server-list/char-select/4×creation) as the visual oracle. Two orchestrators (IDA-RE + VFS) + a supplementary IDA+VFS lane, sub-waves ≤3.
- analyzed / CORRECTED (IDA is the source of truth; the maintainer's observations were right on the two big ones):
  - **CAMERA — the Wave-1 "single static camera (orbit REFUTED)" reading was ITSELF WRONG.** The char-select camera is an **ENTRY DOLLY KF0→KF1**: a camera-PATH rig (object at SelectWindow+6204, updated every frame via vtable slot +64 — the prior pass analysed only the bare projection camera +6200 and missed the rig) holds 6 position keyframes + 12 PI-scaled yaw/pitch channels. On scene ENTER the index is set 0 (ctor) → 1 (entry ResetScene, ~tick frame 5); the camera blends KF0→KF1 over ~2.0 s (dt = elapsed×0.0005), position-Lerp + orientation-Slerp. **KF1 = world (512,87,−9652)** — the value Wave-1 mislabeled "the static camera" was keyframe 1 of a path all along. Keyframes 2–5 exist but are NEVER armed (no orbit, no auto-advance); there is NO select-focus camera move (the "focus" is the preview ACTOR's yaw). The "no sin/cos" claim that anchored the static-camera reading was an artefact (the quaternion half-angle builder uses routines IDA mislabels as logf). Net: orbit (campaign-4) → static (Wave-1, WRONG) → **entry dolly KF0→KF1 (correct)**.
  - **Flying blue/red pixels** = the ambient XEffect 380003000 emitters are D3D9 POINT-SPRITES (fixed-function expansion Godot/Vulkan lacks); red=brazier fire (`fire_4-*`), blue=waterfall (`waterfall-pie-*`) + the cell `.fx3/.fx5` water plane. There are NO scene point-lights — the warm brazier glow is the ADDITIVE FIRE TEXTURE, not lamps. Faithful port = camera-facing alpha-blended billboards (not bare points, not opaque quads).
  - **Double music is REAL** (Wave-1 "double-voice REFUTED" was wrong): the Loading screen plays **920100100 as a category-0 LOOP** (the earlier "cat-2 SFX" label was wrong), never explicitly stopped, contending with char-select's 920100200 across the loading→select handoff (+ a detached loading-audio worker). Fix = stop-previous-track at each scene boundary.
  - **No-char branch** = a zero-character account shows the normal char-select with **5 BLANK slots** (NOT auto-creation); creation opens per-slot on confirming an `@BLANK@` slot. **Creation uses the SAME cell** d000x10000z9990 (carved stone-relief wall `suksang01..04` + portal baked into the `.bud`; select→create differs by camera + a single actor ~56 units nearer, not a different backdrop).
  - **Create-form class DESCRIPTIONS** = `data/script/npc.scr` (404-byte keyed records, keys 1-4, fields +0x14/+0x54/+0x94 = 3 CP949 lines), class NAME from `msg.xdb` 14003..14007 (two-witness CONFIRMED: IDA layout + real-VFS byte dump). **Loading screen** (`Diamond_LoadingWindow`, state 2) fully recovered: bg rand()%3 over loading.dds/loading06/loading08, progress bar fill = 223·pct/100, BGM 920100100, advance on worker-done + 500 ms grace (not bar==100%).
  - VFS: per-screen atlas inventory (login_slice1/loginwindow/loginwindow_02/inventwindow; PIN glyphs = `password.dds`; server-list parchment = login_slice1 + plates on loginwindow); the brazier/waterfall TGA texture set; the create backdrop is the SAME cell (not a separate stage).
- specs corrected/added (firewall PASS): `frontend_scenes.md` §3.5 (camera = ENTRY DOLLY, un-refuting the dolly + fixing the Wave-1 static-camera over-correction), §3.6.1/§3.6.6 (effect billboard render model; no point-lights), §3.7.6 (creation = same cell), §3.8/§3.8.1 (no-char blank slots; double-music REAL), §4.1.1 (create description = npc.scr + msg.xdb name), new §2L (loading screen); `sound.md` (920100100 = cat-0 loading-BGM loop, not SFX; stop-previous contract); `camera_movement.md` §A.5.2 (→ entry dolly); `config_tables.md` §2.17.3 (npc.scr 404-byte keyed table).
- engineering (layer-05 Godot, disjoint-file lanes, NOT RE — provenance pointer): login z-order vs screenshot; PIN keypad 2×5 + server-list parchment vs screenshots; char-select 2D chrome + creation 3-column form; **char-select camera entry-dolly KF0→KF1**; **flying-pixels fixed** (XeffSceneEffect → alpha-blended billboards, untextured emitters dropped); **lighting corrected** (removed the wrongly-added warm omnis — warmth = fire texture; kept achromatic white ambient floor + faint directional); creation 3D backdrop (same cell carved wall); actor placement on the platform (Y≈70, not the .ted rock at ~26); loading screen; real npc.scr CP949 class descriptions. Build 0/0; the double-music + no-char branch were already correctly handled in the port (idempotent PlayBgm; `@BLANK@`-confirm→create).
- notes: All dirty recovery under `_dirty/campaign9/wave2/` (gitignored); committed specs neutral prose only; IDB never mutated this wave (annotation deferred). The load-bearing lesson: the Wave-1 promotion OVER-corrected (static camera, double-voice refuted) by analysing the wrong object / looking only inside one window — Wave 2, grounded in the maintainer's screenshots + a wider IDA walk, restored the truth (entry dolly; real double-music). STILL uncommitted on campaign3.

## 2026-06-15 — CAMPAIGN 9 WAVE 3: IDA-exact construction blueprints → FROM-SCRATCH Godot reconstruction of the front-end Diamond windows
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY) + black-box VFS harness. Maintainer mandate: "pull EVERYTHING from IDA; we already have all the assets; stop adding hand-made/invented elements; rewrite the Godot scenes from ZERO so every element is either a real VFS asset or an IDA construction."
- analyzed (exhaustive IDA-EXACT construction blueprints, READONLY; all dirty under `_dirty/campaign9/wave3/{login,chars,vfs}/`):
  - **LoginWindow blueprint:** the 73-widget master manifest (every widget's exact rect / atlas / src-rect / action / font / z, each flagged LITERAL vs COMPUTED), the action→handler table (16 ids), the tick/animation laws (curtain +5 px/tick; ±64 fade), the 8-asset list. Corrections to prior specs: the substate is TWO fields (workflow @+0x17C + tick @+0x238); the **EULA panel EXISTS** in the IDB build (substate 6, msg 4001–4022) but the maintainer's client does not show it; PIN = conditional child panel; version = a binary 7×u32 `game.ver` GATE; server name-strips = 10; UI server record = 8 bytes; 3-state button src = NORMAL/PRESSED/HOVER.
  - **CharactersWindow blueprint:** select + create are ONE object (`Diamond::SelectWindow`); the §8.2 src-rect GAPS filled from immediates (class-button src-Y **1005**; Create/Delete/Enter src V=1004; "413/531" = stepper HOVER src-X, not action ids); the **camera is a free-look keyframed ENTRY DOLLY** (KF0 = world (515.549,137.266,−9397.710) exact, KF1 (512,87,−9652), 2.0 s lerp+slerp); ambient FX 380003000 @ (508.483,69.887,−9758.569); class arg→key non-identity {0→4,1→1,2→3,3→2}; stat-grid binds `2·disc+{110..141}` (the `disc+{210..240}` family REFUTED = equipment ids); face ± does not rebuild the 3D actor.
  - **VFS asset manifest:** the complete front-end asset table — sounds (intro 910061000, per-class preview 910062000-910065000, loading 920100100, char-select 920100200, click 861010101, login stinger 861010105), cursors, the 8 loading DDS, `game.ver` = binary 7×u32, the carved-wall cell textures (suksang01-04/walll04), the xeff TGA set.
- specs corrected/added (firewall PASS): `frontend_scenes.md` §1.4/§1.4b/§1.4c (game.ver 7×u32 gate; EULA exists), §1.5 (two-field substate), §3.3/§3.5 (free-look entry dolly, KF0 exact), §3.6.1/§3.6.6 (no point-lights), §4.1 (non-identity class map); `ui_system.md` §8.1/§8.2 (full widget manifests + filled src-rects), §9 (asset+sound inventory), §10 (msg.xdb 200–212 corrected); `sound.md` (per-class BGM crossover); `config_tables.md` §2.17.3 (stat-grid key family).
- engineering — FROM-SCRATCH RECONSTRUCTION (layer-05 Godot, 5 disjoint-file lanes, NOT RE — provenance pointer): every front-end Diamond window REWRITTEN from zero so each element is a real VFS asset sliced at its exact src-rect OR an IDA construction; ALL hand-made noise REMOVED — fallback panels/colours/English text, the demo roster (무사영웅/격사전설/TaoMaster), the fake servers (Jade Dragon/Iron Phoenix), procedural sky, the hand-tuned warm-omni rig, the invented EULA panel + cancel/quit/toast widgets. `LoginScreen` (exact atlas slices + 7×u32 version gate); `OpeningWindow`+`LoadingScreen` (real openning_*/loading* DDS, no gold fallback bar); `PinModal`+`ServerSelectScreen` (real password.dds keypad + plates/pagers; server list EMPTY offline); `CharacterSelectScreen`+`CharacterSelectLayout` (exact §8.2 widgets, real npc.scr descriptions, BLANK slots offline); `CharSelectScene3D`+`CharSelectCameraRig`+`CharCreatePreview3D` (real cell + entry-dolly KF0 exact + area-0 env from data, no procedural sky/omnis + real xeff billboards). `BootFlow` cleaned: the synthetic roster + fake-server injection removed (flow driven by real events; offline = faithfully empty). Orphaned `EulaPanel.cs` deleted. Maintainer decision: strict — no synthetic characters offline (the row appears only with a real server's 3/1 list).
- gates: nuked full-solution build **0/0**, `dotnet test` **1848/0**, clean-room firewall PASS, headless walk confirms every window builds from `vfs=real-atlas` with no SCRIPT ERROR (blank slots, no procedural sky, dolly KF0 exact, 68 xeff billboards with 0 fallback).
- notes: dirty under `_dirty/campaign9/wave3/` (gitignored); committed specs neutral prose only; IDB READONLY (annotation deferred). The lesson: the maintainer was right — patching a noisy implementation with IDA values still leaves the noise; the fix was a true from-scratch reconstruction where the scene IS the IDA construction over the real assets, with everything else deleted. Committed targeted-paths on maintainer request.

## 2026-06-15 — CAMPAIGN 9b: IDA re-verification (5 manifests) → noise sweep + load-bearing corrections + dev-account hardcode
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY) + black-box VFS harness. Maintainer mandate: "pull EVERYTHING from IDA (static), rewrite the C# AND especially the Godot correctly, remove the residual noise — we already have all the assets — and hardcode (dev/test) the account xwdvg26 / crfgb727* / PIN 1472; deploy as many parallel agents as possible." Apparatus: 5 parallel READONLY IDA analysts (Login / Chars-2D / Chars-3D / Aux-windows / Skinning) + 3 read-only Godot noise-audit agents + 5 disjoint-file Godot engineer lanes, all fanned out concurrently.
- re-verified against ground truth (manifests under `_dirty/campaign9b/{login,chars2d,chars3d,aux,skinning}.md`): the prior CAMPAIGN-9 specs are HIGHLY ACCURATE — Chars-3D (camera dolly KF0..5, env area-0/14:30/no-lights/fog-off, single FX 380003000 @ (508.483,69.887,−9758.569), cell d000x10000z9990, slots X{488,500,512,524,536}) is EXACT to the decimal, zero corrections; Skinning math (quaternion LBS, computed-not-stored inverse-bind, .mot 10fps lerp+slerp raw-seconds alpha, meshScale +128, idle by id_b) CONFIRMED byte-exact; Chars-2D literals CONFIRMED (Create/Delete/Enter actions 4/5/6 src-V 1004; stat-grid 2·disc+{110..141}; class map {0→4,1→1,2→3,3→2}; name=msg.xdb 14003..14007; desc=npc.scr keys 1..4 +0x14/+0x54/+0x94).
- LOAD-BEARING CORRECTIONS (escalated to specs + C#): (1) **game.ver login gate is single-field equality on list index 5** (byte 0x14), file = ≥7 u32-LE (count<7 rejected, >7 tolerated) — NOT a 7×u32 field-by-field compare; mismatch → msg 2204 → quit. (2) **PIN keypad tag-roles RESOLVED: 11=Reset, 12=OK, 13=Cancel** (the §11.3d table was right; the wave-3 "11=OK/12=Clear" reading was WRONG); on-Reset re-roll now CODE-CONFIRMED. (3) **Server status ARGB exact**: load >1200 red 0xFFFF0000 / >800 orange 0xFFED6806 / >500 yellow 0xFFFFFF00 / ≤500 green 0xFFB5FF7A; captions 6001-6003; readout "%4d / %4d"; id-range guard 5901; headers 4029-4032. (4) **Create-form refinements**: class-select strip src-Y 1005 (idle {590,635,680,725} ↔ sel {770,815,860,905}); create chrome atlas = InventWindow.dds (a third atlas); face actions 21/22 (2D-only); class-pick actions 10/11/12/13. (5) **Opening**: skip-button mainwindow.dds src (761,165)/(634,165) + persist [OPENNING] SKIP=1 + scrub ids 1004/1005.
- noise / dead-code removed (3-audit driven, layer-05): **deleted** `CharPreview3D.cs` (dead, superseded — inverted axis-split vs §3.5.3, invented warm dir-light + blue omni fill rig, ColorRect/English placeholders) + `ConnectingDialog.cs` (built on a guessed caption id — no IDA basis → flow now server-select→loading direct); **removed** BootFlow dead `BuildServerList`/`SeedSyntheticCharacterList` (fake Jade Dragon/Iron Phoenix + 무사영웅/격사전설/TaoMaster), StateButton fallback ColorRect, FrontEndEffectPlayer dead `BuildFallback` + ~80% aesthetic-guess Xeff emitter (now a cited stub), XeffSceneEffect MinQuadSize floor, CameraRig invented Q/E actor-yaw, CharSelectScene3D/CharCreatePreview3D uncited BG 0.04 → real sky_haze 0.004303 + silent-Musa fallback → log+skip + magic 420×600 viewport → derived; **fixed** FrontEndAudio intro BGM 910061000 looping:false→true, UiAssetLoader 6× phantom §1.10.1 citations → ui_system §9.0/§11.1, WidgetFactory/ScreenHost mis-cited Godot mitigations relabeled. **CharacterSelectScreen REWRITTEN** from real art: invented StyleBoxFlat/ColorRect panels + "Lv"/"Cl"/"Name:"/"OK"/"Cancel" English + hardcoded "캐릭터 개수" Korean + 5-button 2D slot strip + point-buy stat grid → real loginwindow.dds chrome plates (§11.5a/d), class names via msg.xdb, descriptions via npc.scr, pure-display stat grid, slot select via tabs + 3D ray-pick; NpcScrDescriptions + CharacterSelectLayout English fallbacks → empty offline.
- dev/test hardcode (LoginScreen + PinModal + BootFlow): account **xwdvg26 / crfgb727\* / PIN 1472** pre-filled in dev-offline mode (overridable via client_dir.cfg dev_account_id/pw/pin), guarded by IsDevOfflineMode(), NEVER ships.
- specs promoted (firewall PASS): `formats/game_ver.md` (login gate single-field index-5 + count≥7); `frontend_scenes.md` §2.3 (exact ARGB), §11.3c (on-Reset re-roll CODE-CONFIRMED), §11.3d (tag-role DISCREPANCY RESOLVED), new §11.5e (create-form construction refinements); `intro_sequence.md` §2.2 (scrub 1004/1005 + skip-button src + SKIP persist).
- gates: NUKE bin/obj → full-solution build **0/0**, `dotnet test --no-build` **1848/0**, headless walk (charselect + login) CLEAN — vfs=real-atlas, BLANK slots offline, npc.scr 4/4 CP949, env from data (white ambient floor / fog off / no procedural sky / no point-lights), entry-dolly KF0=(515.549,137.266,9397.71)→KF1 exact then hold, 68 xeff billboards authored-size (0 fallback), BGM 920100200 via dedup guard, 0 SCRIPT ERROR.
- notes: dirty under `_dirty/campaign9b/` (gitignored); committed specs neutral prose only; IDB READONLY (annotation deferred). FOLLOW-UPS: (1) `names.yaml` sync of ~20 aux/login/chars fn names proposed in the manifests (via `ida-naming-sync`); (2) Phase-Dbg live confirmations (KF2-5 never-armed proof, per-emitter fire/water labels, fog-off site) documented debugger-pending; (3) §11.2e Kind-column cap wording (ID cap 6 / PW cap 129 — §1.3 already correct). Uncommitted on campaign3 (targeted-paths commit on maintainer request).

## 2026-06-15 — CAMPAIGN 9c: SCREENSHOT-DRIVEN 1:1 reconstruction (the maintainer's 8 official captures as the visual oracle)
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY) + black-box VFS harness. Maintainer supplied 8 official-client screenshots (login/PIN/serverlist/char-select/4×creation) — the visual oracle missing from every prior cycle. Diagnosis (3 Explore agents): the Godot code faithfully followed the SPECS but the SPECS diverged from the SCREENSHOTS on 4 points → the persistent "things are missing". Apparatus: 2 Tier-2 `re-cleanroom-orchestrator`s each fanning out their own Tier-3 analysts (effects/format/portal + camera/lighting/serverlist/create; ~10 analysts total), then 5 disjoint-file Godot lanes, then a Tier-1 SCREENSHOT-vs-CAPTURE verification loop (the step that was missing).
- IDA findings (two-witness, manifests in `_dirty/campaign9c/`): (1) **EFFECT** — the 68 sub-effects are NOT 68 uniform billboards at one anchor: the first track triplet is each sub-effect's STATIC LOCAL OFFSET (position, not velocity), placed at `anchor + offset` (identity orientation); the 3 emitter types render distinctly (0 billboard, 1 = oriented `.xobj` mesh = the 28-tile waterfall CURTAIN, 2 = directional +90°Y corona); brazier offsets RIGHT (+28.6,+36.8) / LEFT (−23.9,+36.8) (~52.5u apart), waterfall a ~265u sheet at z≈−105; blend per element +0x30 flag (additive for fire/water). (2) **BLUE PORTAL** = the cell's terrain WATER LAYER (`.fx3/.fx5` → `_water_new01/03/04.dds`), not the xeff. (3) **CAMERA** — create close-up = SAME scene (hold KF1) + camera-local BOOM dolly (boom.Y−1/Z+15) + actor at scale 81/70 nearer (Z−9682), NOT a separate camera (≈×5.7 closer). (4) **LIGHTING** — zero light objects; near-WHITE device ambient (OPTION_BRIGHT/100→white) on neutral-white geometry (no baked vertex colour) + additive sprites; the `.bmp` is the MINIMAP not a lightmap; the waterfall's BLUE is per-particle BGRA diffuse (textures are white). (5) **SERVER** — 1 server = 1 centred parchment plate (= the screenshot's vertical scroll); name = Korean font-slot text. (6) **CREATE-FORM** — left class column (screenshots beat a static right-strip formula), right npc.scr description, class name in the name modal.
- specs promoted (firewall PASS): `frontend_scenes.md` §3.6.7 (effect build recipe — per-emitter-type placement, brazier ±X, waterfall sheet, additive blend), §3.5.6 (create boom-dolly + scale 81), §3.6.1 (the white-ambient warm recipe + "too dark" cause + waterfall-blue-from-diffuse); `formats/game_ver.md` already (9b).
- Godot reconstruction (layer 05, 5 lanes + Tier-1 screenshot iterations — NOT RE, provenance pointer): **EFFECTS** (`XeffSceneEffect` rewrite: 3 emitter types + `Position=(Vx,Vy,−Vz)` Z-negate of the offset [the load-bearing fix] + double-sided + per-frame animation → the "flying blue/red pixels" became 2 COHERENT FIRE BRAZIERS, SCREENSHOT-VERIFIED scattered→braziers). **LIGHTING** (ambient energy parity ×3 + sky-contribution 0 → the dark cave became a lit scene, SCREENSHOT-VERIFIED). **WATER** (cell water layer wired as a blue `_water_new01` PlaneMesh with scrolling UV — open render-debt #4). **CREATE close-up** (boom-dolly + actor scale 81 + viewport enlarged + camera re-aimed at the actor centre so the figure fills/centres the frame). **SKINNING blob FIXED** (root cause: the stand-up pivot was derived from the REST AABB; the g4 Monk lies along X at rest but its idle frame-0 stands on Y → a double 90° tipped it into a blob; fix = derive pivot+recentre from the DISPLAYED animated frame-0 AABB; World g1/g2048 byte-identical → non-regression verified by deform simulation). **SERVERLIST** (1 server → 1 centred parchment scroll). **CREATE-FORM 2D** (left class column + 3-line npc.scr description from mainwindow.dds + class name in the name modal + large centred preview).
- VERIFICATION (the missing discipline, now in place): a Tier-1 windowed-screenshot loop captured char-select + login at each step and compared them DIRECTLY to the official captures — proving scattered-pixels→2 braziers, dark→bright, and login = the stone bezel + warrior painting + form bar matching `…015528`. Screenshots under `%TEMP%\mh-*.png`.
- gates: NUKE bin/obj → full-solution build **0/0**, `dotnet test --no-build` **1848/0**, clean-room firewall PASS (the only `sub_` token is the legit `sub_effect_count` format field).
- notes: dirty under `_dirty/campaign9c/` (gitignored); committed specs neutral prose only; IDB READONLY. RESIDUALS (static-first; live debugger only if blocking, per maintainer): the EXACT create-framing Euler + character-clarity at full resolution, water prominence/colour, and the waterfall's per-particle BGRA diffuse (needs a parser field) are best judged on the maintainer's full-res windowed screen or a live F9 — flagged debugger-pending, never guessed. THE LESSON: without the official screenshots as oracle, "spec-faithful" code still diverged from the real client; the screenshot-vs-capture loop is what finally grounded the fidelity. Uncommitted on campaign3.
- 9c FOLLOW-UP (same session, screenshot-verified): (a) the waterfall-blue was CORRECTED — a `re-asset-format-analyst` proved the `waterfall-pie` diffuse is WHITE in the file (the spray IS white; the visible blue is the SEPARATE cell water plane), and that the `.xeff` curve passes 2/3/4 are the per-keyframe **DIFFUSE R/G/B** multiplier (the spec's "scale X/Y/Z" was a MISLABEL — the real size is the keyframe size floats). `formats/effects.md §A.4.2` corrected; the renderer now tints each sub-effect by its REAL recovered diffuse (waterfall white, lens-flare warm-yellow, smoke black) and dropped an interim blue-tint hack. (b) The create-preview "brown blob" leaking into the OFFLINE select view was `CharCreatePreview3D`'s SubViewport sharing the main `World3D` (Godot SubViewports do NOT isolate by default) → fixed with `OwnWorld3D = true`; offline select now renders the clean temple (braziers + water + stone) with NO leaked character, per §3.8. Both confirmed by windowed screenshot; build 0/0, 1848 tests. New manifest `_dirty/campaign9c/xeff_diffuse.md`. DEFERRED naming follow-up: rename the parser model `ScaleX/Y/Z` → `DiffuseR/G/B`.

## 2026-06-15 — CAMPAIGN 9d: front-end from the TRUE entry point (WinMain) + ALL LoginScene windows + the scenes the maintainer called ugly
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY) + black-box VFS harness + windowed-screenshot verification. Maintainer (frustrated that 9c was char-select-centric) demanded: reverse the WHOLE front-end from the `WinMain` entry point — startup, the scene lifecycle, the UI/UX framework, asset loading — and make EVERY scene render for real (not just chars). Apparatus: 2 Tier-2 `re-cleanroom-orchestrator`s each fanning out their own analysts (startup/framework/asset-pipeline + per-window constructions) + Godot fix lanes + a Tier-1 screenshot loop.
- LOAD-BEARING discovery: **`WinMain` IS the front-end scene state machine** — it mounts the VFS once, then runs `while(1) switch(GameState)` over engine states 0..8; each case constructs that state's window object and re-enters the shared per-frame engine loop to tick it until the state value changes (a scene tick or a net message rewrites the state to redirect the outer switch). First window = LoginWindow; there is NO separate scene-manager class. This is the foundation never written down before. Manifests `_dirty/campaign9d/{startup_flow,scene_state_machine,ui_framework,asset_pipeline}.md` + the 5 per-window `*_construct.md` (opening/login/pin/serverlist/loading) — each exhaustive (every widget atlas/src-rect/dst/z/3-state/action/visibility, font slot + msg.xdb id, per-tick animation laws, the window's sub-state machine, audio cues). Per-window corrections: opening skip-button UPPER-right + dead auto-transition (only SKIP exits) + fade starts saturated; login NO EULA (re-confirmed) + version=gate + 10 pagers; PIN commit=button id 12 (wide), id 11 re-scrambles (prior art swapped them); serverlist 1 server→1 central plate, name=Korean-font msg-DB (msg 5000+id), load colours byte-exact; loading exits on preload+500ms. CONFLICT (login: one substate field or two — provisional ONE, matches committed login.md) is debugger-pending, never merged silently.
- specs promoted (firewall PASS, neutral rewrite): `client_runtime.md §7.9` (the 0..8 scene state machine + state×event table + state writers), `game_loop.md §0/§7` (CRT→WinMain bootstrap, D3D9 present-params IMMEDIATE/no-vsync, init order, 15-slot font table, the authoritative 4-phase loop — supersedes the old 3-step order), `ui_system.md §15` (the Diamond UI framework: GUComponent 16-slot vtable, AddChild/AddChildWithAction attach primitives [the 0x5fe063 "widget-attach" guess CORRECTED to a disposable-tracking push], lifecycle, ID3DXSprite insertion-order z-model, click→hit-test→action-id→OnEvent dispatch, D3DXFont/MessageDB text), `resource_pipeline.md §3A` (VFS-or-disk file-open chokepoint, UiTex.txt + bmplist.lst manifests, DDS/TGA upload, per-scene window-owned texture lists released on teardown, boot font load).
- Godot fixes (layer 05, screenshot-verified — the scenes the maintainer called "dégueulasse"): **PIN modal** (was broken/cramped — root cause: added under a `CanvasLayer` [a Node, not a Control] so `SetAnchorsAndOffsetsPreset(FullRect)` gave it Size(0,0) → collapsed; fix = explicit `ApplyViewportSize()` + a scaled inner 1024×768 `_canvas` like ScreenHost + a dim ColorRect backdrop → now a centred stone-framed modal with an aligned 2×5 keypad over the dimmed login). **Char-select 3D row** (the 3 characters didn't render — the row built DEFERRED before the 3/1 list arrived, then `ApplyCharacterList` re-set `SlotDescriptors` without rebuilding; fix = a `RefreshSlotActors()` that frees + rebuilds the actor row, called from `ApplyCharacterList` → the 3 dev-seeded characters now stand on the platform, skinning PASS / non-exploded). **Dev seeds** (BootFlow, guarded by IsDevOfflineMode, NEVER shipped): a 2-server list + a 5-slot character row so the otherwise-empty offline ServerList renders its calligraphy parchment scrolls and char-select shows a populated row — so the maintainer can finally SEE the real rendering (offline-empty made every populated scene unjudgeable).
- VERIFIED rendering (windowed screenshots `%TEMP%\mh-*.png`): Login = stone bezel + warrior painting + form bar (≈015528); PIN = centred framed modal + aligned 2×5 keypad + dimmed login (≈015544); ServerList = 2 calligraphy parchment scrolls (≈015642); Char-select = bright temple + 2 braziers + blue water + 3 standing characters (≈015759).
- gates: full-solution build **0/0**, `dotnet test` **1848/0**, clean-room firewall PASS.
- notes: dirty under `_dirty/campaign9d/` (gitignored); the 4 framework/pipeline manifests promoted to specs; the 5 per-window `*_construct.md` NOT yet promoted (next sub-wave) — their corrections are relayed but should land in `frontend_scenes.md`/`intro_sequence.md`. DEBUGGER-PENDING (maintainer F9): the login single-substate confirmation + ~10 other items in the dossier §4. THE LESSON: the maintainer was right — fidelity work must START from the true entry point (`WinMain`) and cover EVERY scene with a real on-screen render, not just the one being iterated. Uncommitted on campaign3.

## 2026-06-16 — CAMPAIGN 14 Phase-W/P: ground-truth research + promotion (3 residuals + UI/HUD)
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY static) + black-box VFS harness (Assets.Vfs). NOTE: the IDA MCP server CRASHED mid-pass (a `server_warmup{build_caches:true}` call + several heavy parallel decompiles of large functions stalled then killed the plugin) — several lanes returned PARTIAL with honest IDA-pending gaps; nothing past the drop was fabricated. Lesson recorded: never `server_warmup{build_caches}` in one shot on this DB; decompile large functions singly, not 8 in parallel.
- analyzed (by canonical name / behaviour, confirmed before the drop): Display_LoadDisplayLua (the `data/script/display.lua` config loader) + Diamond_LuaConfig_GetFloat/GetString — the world display-config layer (DISPLAY_BASE_BRIGHT_MULTI/GLOW_BRIGHT_MULTI/LIGHT_RATIO, POWER→powerNdx8.psh, GLOW_RANGE, the 9-state DISPLAY_CHAR_BRIGHT tint table); the actor anim sampler/clip-advance (floor(t*10) @10fps, 28-byte keyframe stride, slerp, cycle vs sync layer advance) and the col15 idle-selection chain; the WinMain scene-state machine refined (case 5 = in-game world; GPerspectiveCamera off-centre RH, 60° FOV) and the create/zoom-preview camera family (SelectWindow_BuildZoomPreviewActor, anchor (2048,0,−6144), actor (511.5,0,−9682) scale 81) — camera follow math + boom-rig EYE formula NOT recovered (IDA-pending after the crash).
- VFS sample-verified (no IDA): display.lua scalar VALUES (BASE 1.05 ≈ neutral, GLOW 0.3, LIGHT_RATIO 0.5, POWER 2→power2dx8.psh, GLOW_RANGE 1×1, full CHAR_BRIGHT 9-state table); the col15 human idle `g101100001.mot` is genuinely STATIC (3f/84 tracks, 0/84 animate) while mob clips + other human slots animate; uitex registry = 37 DDS (not 35), key atlas dims (blacksheet/stateicon 512²), NO dedicated char-select atlas, and an arithmetic-certain close-button overflow (src (941,910,23,23) on the 512² blacksheet.dds).
- specs produced/updated (firewall PASS, neutral prose, no addresses):
  - Docs/RE/specs/environment.md (new §9 display.lua world-brightness layer; apply-path IDA-pending; records BASE=1.05 is NOT the near-black cause)
  - Docs/RE/specs/rendering.md (§6.3–6.7 glow config 0.3/range 1×1; the power1-vs-power2 conflict FLAGGED IDA-pending; DISPLAY_CHAR_BRIGHT 9-state tint table)
  - Docs/RE/formats/animation.md (per-frame dt advance CONFIRMED; static-idle clip section)
  - Docs/RE/specs/skinning.md (§10 — col15 idle is static DATA = faithful; mesh-explosion debt RETIRED; runtime standing-idle slot debugger-pending)
  - Docs/RE/formats/ui_manifests.md (§1.4a 37-atlas re-inventory + dims; §5.1a shared-atlas char-select; §5.1b close-button overflow mis-binding, correct atlas IDA-pending)
- notes: dirty under `_dirty/campaign14/` (gitignored). KEY zero-trust outcome: the assumed "world-black = a missing brightness scalar" was REFUTED by ground truth — DISPLAY_BASE_BRIGHT_MULTI is a neutral +5%; the near-black world is camera/state (the port never drives GameState→world case 5 + spawns no local actor) and/or material ambient reception, recovery IDA-pending. The "flat idle" was settled as faithful static col15 data, not a port bug. IDA-PENDING (needs MCP restart + live debugger, maintainer F9): display.lua render-stage reads, world camera follow math, boom-rig eye formula, HUD pop-up/overlay construct() walks, runtime idle-slot selection. names.yaml candidates flagged (Display_LoadDisplayLua, Diamond_LuaConfig_Get*, the DISPLAY_* keys, uitex_registry) — staged for maintainer hand-merge, not auto-merged. Uncommitted on campaign12.

## 2026-06-17 — CAMPAIGN 16 Phase A: zero-trust re-confrontation of the scene state machine (spine)
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY static). Goal: scene-by-scene clean rebuild of the front-end (C# core + Godot); Phase A re-derives the 8-state spine from ground truth before any code is trusted. Dirty note `_dirty/scene16/spine.md`.
- analyzed (by canonical name / behaviour): WinMain_SceneStateMachine (the `while(1) switch(GameState){0..7}` entry loop), GameState_ctor (3-int record + debug byte), Engine_RunSceneLoop / Engine_FrameRateLimiter / FrameTickScheduler_TickAll (the 4-phase per-frame loop), and the inbound scene-edge handlers SmsgEnterGameAck (3/5), SmsgCharacterList (3/1), SmsgGameStateTick (4/1), SmsgCharManageResult (3/7), SmsgCharActionResult (3/100), Scene_LeaveWorldToLogout.
- CONFIRMED as-spec: the 8 cases 0..7 (no case 8; 8 = shared-exit sentinel); operator-new byte sizes LoginWindow 0x558=1368, SelectWindow 0x1888=6280, MainHandler 0xC8=200; the 3-int state record (field0 state / field1 sub default 8 / field2 detail) + a +0x0C debug byte; the 4-phase loop (pump → device step+present → amortised tick → frame throttle) with a HARDCODED 60.0f cap (engine ctor +0x30; DISPLAY_FRAMERATE feeds only a display-config object the limiter never reads).
- DRIFT corrected (binary wins): **D1 (HIGH)** the table-driven GameState transition handler is **3/100 `SmsgCharActionResult`** (codes 0→Quit, 1..4/7→Error/5, 202/203/232→Load, out-of-range→Error/8), NOT 3/7 — the real **3/7 `SmsgCharManageResult`** is a Character-Select delete/rename/select UI result that writes NO scene state. **D2 (MED)** in-world logout drives 5→6 (Quit) via Scene_LeaveWorldToLogout, distinct from the case-5 default re-entry edge 5→4. **D3 (LOW)** EnterGameAck (3/5) forces state 2 unconditionally (state-agnostic). **D4 (LOW)** the 4/1→Select edge is specifically the local-player-absent + descriptor-spawn-failed fallback.
- specs produced/updated (firewall PASS, neutral prose, no addresses): `Docs/RE/opcodes.md` (rows 3/7 re-scoped, 3/100 promoted to the engine-state transitioner); `Docs/RE/packets/3-100_char_action_result.yaml` (new; routing + GameState targets confirmed, byte VALUE semantics capture-pending); `Docs/RE/specs/client_runtime.md §7.5.2` (CharActionResult-3/100-not-3/7 banner + D3/D4 nuances).
- C# (clean room, measured against the corrected spec — provenance pointer, not RE): rewrote Kernel `EngineSceneState`/`GameState` (build 0/0); re-derived + corrected `SceneStateMachine` (`OnCharManagementResult`→`OnCharActionResult` pinned to 3/100; `OnEnterGameAck` made state-agnostic; 4/1 doc clarified) and REMOVED the latent D1 bug in `GamePacketHandler.HandleCharManageResult` (3/7 was wrongly driving the spine) — 3/100 `HandleCharActionResult` is the sole scene driver. Tests updated; Application.Tests 38/38 green.
- confidence: routing + control flow + struct sizes CODE-CONFIRMED; wire byte VALUE semantics capture/debugger-pending (no live capture this cycle). names.yaml already carries SmsgCharManageResult (3/7) + SmsgCharActionResult (3/100) — no rename; direction/role rows reconciled. Uncommitted on campaign15.

## 2026-06-17 — CAMPAIGN 16 Phase B: State-1 LOGIN re-confrontation + verify-and-fix
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY static). Two parallel analysts: window-construction lane + login-network lane; one reconciliation follow-up. Dirty notes `_dirty/scene16/login_construct.md` (+ a RECONCILE section) and `_dirty/scene16/login_net.md`.
- analyzed (by canonical name / behaviour): the LoginWindow construction + its tick/drive sub-state field (+0x238), the click/action router (LoginWindow_OnEvent on the +0xBC CommonLoginWindow subobject), the 0/0→1/4 session handshake (54-byte key blob + XOR 0x29 whitening; does not key the wire cipher), the 1/4 credential blob (sub-opcode 0x2B, length-prefixed account + optional PIN, password staged as RSA M), the server-list / channel-endpoint blocking-worker fetches (lobby port 10000(+offset)), and the 3/5 EnterGameAck.
- CONFIRMED as-spec: no EULA (the modal is the PIN/second-password keypad); PIN raise=31 / poll=32 / server-list interactive pick=37; sub-state range 1..41; font slot 0 DotumChe 12pt + msg.xdb captions + 1024×768 hardcoded-pixel Diamond layout; 4-token TAB join key (account/password/PIN/host port); submit pre-arms GameState 7 + 30000ms timeout, 3/1 overwrites to 4 on success; quit → 6/2 + SFX 861010106; field caps account<20 / password<17 / PIN<5.
- DRIFT corrected (binary wins): **the login window has ONE sub-state field at +0x238, not two** — the `+0x17C`/`+0x238` pair is the same cell (`0xBC+0x17C=0x238` via the CommonLoginWindow subobject), and the "MAIN written-only-by-router / TICK written-only-by-tick" partition is false (one field, three writer classes: tick 1..41, action router {29,34,38}+computed, two lobby workers {35,39}). Also: server-list sub-state is 37 (older "32=server-list" was the PIN-poll mis-attributed); login_flow.md §1 TAB string was a vague 3-slot shape → corrected to the confirmed 4 tokens; game.ver gate is server-enforced (no client compare-quit branch on the login path — field-5 only derives the outbound 1/9 token).
- specs updated (firewall PASS, neutral prose, no addresses): `client_runtime.md §7.6` (one +0x238 field; sub-state map; supersedes the +0x17C/+0x238 split), `frontend_scenes.md §1.5` (ONE-field headline correction + the RESOLVED-31/32 blockquote made consistent; value semantics kept), `login_flow.md §1` (4-token TAB string). opcodes.md + login/enter packet YAMLs needed NO change (match the binary).
- C# (verify-and-fix, measured against the corrected specs): Godot login UI (LoginScene/LoginScreen/PinModal/ServerSelectScreen) verified FAITHFUL on all 7 load-bearing facts — ZERO behavioural fixes needed (PIN 2×5 scrambled keypad tags 11/12/13 + window actions 111/112; server plates 400/401 + pagers 115..124 + refresh 105; no EULA; no login BGM; quit→RequestQuit). Application login orchestration verified faithful — only comment/doc drift fixed (3/5 state-agnostic comment; CP949-not-UTF-8 chat docstrings). LoginCredentialStore 3-input staging + PIN cap<5 correct; the 4-token join is assembled at the join handoff, not in the credential store (faithful separation).
- gates: clean full-solution build **0/0**; Application.Tests 192/192; headless boot CLEAN (Login scene builds, no script errors). FLAGGED follow-ups (not blockers): quit SFX 861010106 fires with no fade-delay before GetTree().Quit() (timed-quit debt); spec conflict frontend_scenes.md §2.3 (status==3 = HH:MM open-clock) vs lobby.yaml (latency digit-split) — server-record display, reconcile later. Uncommitted on campaign15.

## 2026-06-17 — CAMPAIGN 16 Phases C+D: State-2 LOAD + State-3 OPENING re-confrontation + verify-and-fix
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY static). Two parallel analysts (Load + Opening). Dirty notes `_dirty/scene16/load.md`, `_dirty/scene16/opening.md`.
- analyzed (by canonical name / behaviour): the LoadHandler + Boot_LoadDataTableCorpus worker (ABOVE_NORMAL, ~50 loads), the OPENNING/SKIP gate (GetPrivateProfileIntA over the DoOption settings singleton's path), the progress meter (denom 9,395,240, 500ms trailing Sleep), the LoadingScreen (rand()%3 bg, looping cue 920100100); the OpeningWindow slideshow FSM (4 panels, 17500ms dwell, alpha seeded 250 / first phase fade-out), the skip dispatch (Enter/ESC/Space + click action 100), BGM 910061000.
- CONFIRMED as-spec: Load boot-worker + ABOVE_NORMAL; progress denom 9,395,240 (decorative — completion gated on worker done-flag + 500ms grace, not the bar); LoadingScreen bg rand()%3 (loading.dds/06/08) + cue 920100100 loop; Opening BGM 910061000, skip button top-right, action 100, SKIP persisted.
- DRIFT corrected (binary wins): **(Load) the OPENNING/SKIP INI file is `<exe-dir>\option.ini` via the DoOption settings singleton** (NOT the per-account/net-config singleton — resolves the long-open INI-path item); **there is NO "LoadIsReload forces Select / reload skips the INI read" rule** — a char-management reload (3/100 codes 202/203/232 → state 2) re-enters case 2 and re-reads OPENNING/SKIP unconditionally; the ONLY reload difference is msg.xdb (case-1-only, not re-loaded); boot corpus is ~50 not ~10. **(Opening) the slideshow does NOT auto-finish** — it loops panel 4 indefinitely; the finish flag is written only by the skip handler (two sites); the SOLE exit is an explicit skip (settles the old movie-complete-vs-loading-done-vs-timer question = none of those).
- specs updated (firewall PASS, neutral prose): `resource_pipeline.md §2.5` (option.ini + reload re-reads SKIP + msg.xdb-only difference), `client_runtime.md §7.10 item 2` (RESOLVED = option.ini), `intro_sequence.md §3.1` (no auto-finish; skip is the sole exit).
- C# (verify-and-fix): **Load** — `SceneStateMachine.AdvanceLoadScene` reload short-circuit REMOVED (reload re-reads SkipOpening); `LoadOrchestrator` drops the reload-forces-Select + INI-read-skip (re-reads OPENNING/SKIP every state-2 entry; `LoadIsReload` now only skips the msg.xdb reload). **Opening** — `OpeningScene`/`OpeningWindow` auto-finish REMOVED (panel 4 loops; skip is the sole exit; SceneHost auto-walk drives headless). Load Godot scene verified faithful (no change). Dead code removed (`_openingDecisionApplied`, `_sequenceDone`, headless self-advance timer).
- gates: clean full-solution build **0/0**, `dotnet test --no-build` **2049 green** (Application.Tests 192→194: +2 faithful reload tests, −1 drift test, net new reload coverage), headless boot CLEAN (spine walks Init→Login→Load→Opening→Select, no script errors, the old "IntroFinished → advance" log gone). Uncommitted on campaign15.

## 2026-06-17 — CAMPAIGN 16 Phase E: State-4 CHARACTER-SELECT re-confrontation + verify-and-fix
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY static). Two parallel analysts (construct/camera/preview + SelectWindow/SpawnDescriptor struct). Dirty notes `_dirty/scene16/select_construct.md`, `_dirty/scene16/select_struct.md`.
- analyzed (by canonical name / behaviour): SelectWindow build (operator new 0x1888=6280B) + the camera path rig (KF apply has exactly 2 callers: rig ctor arms KF0, ResetScene arms KF1; KF2-5 dead), the 3D preview actor row (5 slots, absolute X={488,500,512,524,536}, scale 70, idle×3, facing byte), the 3D ray-pick (unproject→AABB ±6 X/Z, Y band [70,92]), the area-0 environment, the create-form trigger (@BLANK@ slot→create modal), and the enter-world ACK path; the embedded 5×880 SpawnDescriptor array (NetHandler roster +0x3C master / SelectWindow copy +0x238) + the 880-byte descriptor layout.
- CONFIRMED as-spec: entry dolly KF0→KF1 then hold (not static, not orbit; KF1=(512,87,−9652)); **FOV literally 50.0°/aspect, near 5, far 15000 (NOT 60/65 — the port's FOV 50 is CORRECT)**; preview row + scale 70 + ray-pick; area-0 frozen 14:30, fog OFF, zero point-lights, ambient xeff 380003000 @ (508.483,69.887,−9758.569); SelectWindow 6280B + 5×880 descriptor (stride 0x370); 3/1 slot record 880+96+1+4=981; descriptor name char[17]@0 CP949 / internal_class@0x34 / variant@0x2C / level@0x3A / world_x,z@0x4C,0x50 / equip@0x58; IdB = 5·(class+4·variant)−24 ∈ {1,11,16,26}.
- DRIFT corrected (binary wins): **the live enter-world path is Select(4) → Load(2) → … → InGame(5), NOT a direct 4→5.** Confirming an OCCUPIED slot sends the EnterGame request (1/9); the server's 3/5 EnterGameAck → state 2 (Load) drives the transition; the case-4 entry pre-writes 5 only as the no-network DEFAULT. The C# `SelectCharacterAsync` was driving a direct `OnSelectConfirmCharacter` 4→5 jump (bypassing Load) — removed. Also flagged: a char-select PREVIEW occupied gate read at descriptor +0x2E (vs the spec's +0x36 gate) — debugger-pending discrepancy, C# unaffected.
- specs updated (firewall PASS): `client_runtime.md §7.5.3` (enter-world = 4→2→…→5 via 3/5; +a @BLANK@ create-modal row), `structs/spawn_descriptor.md` (+0x2E discrepancy flag on the +0x36 gate row). frontend_scenes.md §3 camera/preview/env confirmed accurate (no change).
- C# (verify-and-fix): `ApplicationUseCases.SelectCharacterAsync` no longer side-effects a direct 4→5 (the 3/5 EnterGameAck drives 4→2); `SceneStateMachine.OnSelectConfirmCharacter` kept as the documented no-network default (case-4 pre-write); dev-offline path preserved (synthetic seed / guarded SceneHost.Advance = the faithful no-network 4→5 default). SpawnDescriptorReader verified matching the struct (stride 880, all offsets). Godot select scene (camera/preview/env/create-form + enter-world via SelectCharacterAsync) VERIFIED FAITHFUL — no Godot changes needed (the dev fallback uses the faithful no-network 4→5). Legacy BootFlow/ScreenHost double-subscription noted as dead-in-active-flow cruft (Phase G delete).
- gates: clean full-solution build 0/0; Application.Tests 194 green (+1 enter-world-via-Load test, −1 drift test); headless select-scene boot CLEAN (roster + 3D preview at X={488,500,512} + camera dolly FOV 50 + white-ambient/fog-off/no-point-lights + backdrop + water; no script errors). Uncommitted on campaign15.

## 2026-06-17 — CAMPAIGN 16 Phase F: State-5 IN-GAME WORLD structure re-confrontation + verify (first faithful pass)
- binary: doida.exe @ 263bd994, IDA Pro 9.3 via MCP (READONLY static). One structure-scoped analyst (NOT the 16 gameplay subsystems — a later campaign). Dirty note `_dirty/scene16/ingame.md`. SCOPE = scene-graph structure + entry + per-frame loop + exit edges only.
- analyzed (by canonical name / behaviour): WinMain case 5 → MainHandler (operator new 0xC8=200B) + BuildGameWorld → the scene-graph builder (1 GPerspectiveCamera + exactly 5 GViewPlatform + GScene root "charater scene" + GSwitch + terrain-manager singleton + 4 layer nodes {2004,2005,2006,2148}); g_LocalPlayer singleton (seeded by 4/1, X@+0x2374/Z@+0x2378, Y forced 0); the MainWindow HUD master singleton (handler @+0x500, one notice sink); the per-frame world-update callback (installed @ MainHandler+444); the two exit edges (case-5 pre-write 5→4 vs explicit logout Scene_LeaveWorldToLogout 5→6).
- CONFIRMED as-spec: 5 view-platforms (no sixth), 4 layer nodes, world camera FOV **65**/near 5/far 15000 (distinct from select's 50, same shared ctor), cell 1024 / 3×3 ring (+5×5 cold-start), the 4-phase loop @ 60 FPS, the two exit edges. The CAMPAIGN-10 world specs are essentially correct.
- DRIFT corrected (minor, binary wins): `client_runtime.md §9.5 item 7` "reserved sixth view-platform" → **RESOLVED: there is no sixth** (exactly 5 confirmed); §9.1 step-2 + the §7.5.1 transition-table row updated (5 platforms, 4 layer nodes; the "2148/2148" is real id reuse across the 5 manipulators, not a typo). World FOV is 65 (specs already correct).
- C# (verify-and-fix): the InGame scene-machine edges (5→4 default via AdvanceScene / OnGameStateTickNoLocalPlayer spawn-failure; 5→6 logout via RequestQuit) were already CONFIRMED correct in Phase A — no change. Godot world structure VERIFIED FAITHFUL by read (subagent path was API-overloaded; verified via direct read): `InGameScene.BuildGameWorld` = "charater scene" root + 5 GViewPlatform slots {Third,First,Static,Gamble,Event} + GameHud shell + TerrainNode + camera FOV 65/near 5/far 15000; **exit edges correct** — `OnWorldExitRequested(logout)` → logout ⇒ SceneMachine.RequestQuit() (5→6), else ⇒ SceneHost.Advance() (5→4). No structural drift; no code change needed.
- FLAGGED for a follow-on WORLD campaign (out of first-pass scope): the 5 camera VIEW MODES — only Third/First/Static (+dev FreeFly) implemented; Gamble/Event not built; the 16 gameplay subsystems (combat/quest/inventory/trade/…); the character skinning debt; the NPC fallback-Y-before-terrain race.
- gates: build 0/0 (unchanged C#); headless world-entry boot CLEAN — RealWorldRenderer enters area 2, resolves cell (10009,10006), 5×5 terrain ring (25 sectors), bgtexture pool 1222 slots, BUD scene 244 objects via ArrayMesh (no GltfDocument), npc.scr 4/4 CP949, env wired; NO script errors. Uncommitted on campaign15.

## 2026-06-17 — CAMPAIGN 16 Phase G: States 6/7/8 (Quit/Error) + consolidation
- No new RE (states 6/7/8 were CODE-CONFIRMED in Phase A.1: case 6 = engine shutdown → writes field-0 8; case 7 = build error string from sub/detail + error.log + MessageBoxA → writes 8; 8 = shared exit-tail, no case). The SceneStateMachine already models these (RequestQuit→6, ToError→7, ExitTail→8) — confirmed, no change.
- C# (verify-and-fix): filled the two stub Godot controllers faithfully — `QuitScene` (state 6): graceful engine-shutdown → deferred `SceneTree.Quit()` (the exit-tail return); `ErrorScene` (state 7): reads the scene-machine error detail (sub = reason, detail = result code), writes the error to the engine log (the error.log analogue, §7.7), shows a centred modal, then exits after a readable beat (the modal→exit of §7.5.1). Both cite client_runtime.md §7.3/§7.5.1/§7.7.
- cleanup: identified that `ScreenHost` is ACTIVE infrastructure (every CAMPAIGN-15 scene controller does `new ScreenHost` as its 2D canvas host — NOT cruft, KEEP). The genuine legacy/superseded cruft is `Screens/BootFlow.cs` + `Scenes/Boot.tscn` (the pre-CAMPAIGN-15 ScreenHost-driven entry; the active main scene is `SceneHostBoot.tscn`; BootFlow is never `new`-ed by code) — FLAGGED for removal (not deleted this session; needs a maintainer confirm + careful ref check, since deletion is destructive and verify-and-fix did not quarantine).
- firewall: committed C# (Scene + Controllers) carries NO `_dirty/` cites and NO decompiler artifacts (sub_/loc_/_DWORD/__thiscall/raw addresses); spec `<!-- source: _dirty/… -->` provenance comments are the pre-existing allowed convention; every changed spec is journalled in this campaign's entries (A/B/C+D/E/F/G). PASS.
- FINAL GATE: clean full-solution `dotnet build --no-incremental` **0/0**; `dotnet test --no-build` **2049 green** (12 suites); headless full-spine walk Init→Login→Load→Opening→Select→InGame CLEAN.

## CAMPAIGN 16 — summary (scene-by-scene clean rebuild, C# core + Godot, zero-trust vs IDA)
Re-derived the 8-state scene layer from doida.exe scene-by-scene in engine order (0→7), verify-and-fix (keep what is faithful, correct only real drift), NO screenshot oracle — IDA + specs the sole truth. Real drift caught + fixed: the 3/7-vs-**3/100** table-driven transition mislabel (+ a latent bug feeding the spine from 3/7); the LoginWindow **one +0x238** field (not two) + server-list sub-state **37**; the OPENNING/SKIP file = **option.ini** + reload **re-reads** SKIP (no reload-forces-Select); the Opening has **no auto-finish** (skip-only exit); the enter-world path is **4→Load(2)→…→5** (not a direct 4→5); the world has exactly **5** view-platforms (no sixth). Select camera **FOV 50** and world camera **FOV 65** both CONFIRMED correct. Specs corrected + journalled across client_runtime/frontend_scenes/login_flow/resource_pipeline/intro_sequence/opcodes + structs/spawn_descriptor + a new packets/3-100 yaml. C# fixes in Kernel (EngineSceneState/GameState), Application (SceneStateMachine/GamePacketHandler/LoadOrchestrator/ApplicationUseCases), Godot (Opening/Quit/Error controllers). build 0/0, 2049 tests, headless clean, firewall PASS. Minted agents: application-engineer, godot-presentation-engineer, godot-input-engineer. Deferred to a follow-on WORLD campaign: the 16 gameplay subsystems, the 5 camera view modes (Gamble/Event), skinning, NPC fallback-Y race, BootFlow/Boot.tscn legacy removal, the quit-SFX fade-delay, the §2.3-vs-lobby.yaml server-status reconcile, the +0x2E-vs-+0x36 preview-gate discrepancy. Uncommitted on campaign15 (commit on maintainer request).

## CAMPAIGN 17 — HUD/UI total rebuild (scene by scene, from scratch, IDA-faithful) (launched 2026-06-17)
Mandate: rebuild the ENTIRE HUD/UI layer from scratch, zero-trust, scene by scene in engine order (Foundation→Login→Load→Opening→Select→In-game core→Quit/Error), each element re-derived from doida.exe (IDA) + Docs/RE HUD specs — NO screenshot oracle this cycle (IDA + specs the sole truth). In-game scope = core HUD + main windows (the ~100-panel long tail = a named HUD-II follow-on).
- 2026-06-17  specs/ui_system.md — Phase A HUD re-confront: corrected the GU primary vtable layout
  (§2, §1, §15) from a phantom flat shared 16-slot table to the binary-true LAYERED shape that grows
  by inheritance — GUComponent 13 slots (0..12), GUPanel 14 (adds slot 13 = marked-removed-children
  sweep), GUWindow 15 (adds slot 14 = auxiliary-view (GView) init helper, per structs/guwindow.md
  §3.1). Deleted the non-existent slot 15 "SetShown alias" and the Campaign-9D 16-slot assertion;
  relabelled "BuildScene" as each window's per-class build routine (the hardcoded constructor-call
  path), not a universal vtable slot. Closed the open item on the in-game GUButton caption font-slot:
  pinned at +0xE8 (i32, ctor zero-init -> default slot 0; §6.3, §12), removed from the header
  conflicts line. doida.exe 263bd994 wins; gucomponent.md / guwindow.md were already correct and were
  the reconciliation reference.
- 2026-06-17  Phase A also VINDICATED (no change): structs/gucomponent.md (all offsets),
  structs/guwindow.md, formats/ui_manifests.md (uitex ids, skillicon 12 entries, .do icon record
  116-byte stride + iconSrcX@+0x18/iconSrcY@+0x1C, texturelist), formats/msg_xdb.md (516-byte
  records). buff_icon_position.xdb = 12-byte stride {u32 id, i32 srcX, i32 srcY} (the "12+8=20"
  premise REFUTED; spec already correct, HIGH confidence). Flagged for later lanes (not Phase A's
  five specs): .do stride 116 vs config_tables.md's 166 (config-tables re-confront should resolve —
  Phase A witnesses + VFS sample both say 116); buff draw-cell footprint 23×23 vs 25×25 (live-draw,
  MEDIUM); GUList reverse hit-test + IME per-keystroke composition (runtime/debugger-only).
- 2026-06-17  Phase B Login(1) re-confront — specs ~90% PASS, 7 surgical drifts corrected (binary wins):
  ui_system.md §11.3 = login sub-state is ONE +0x238 field (re-affirms CAMPAIGN 16; +0x17C is the same
  cell via the +0xBC sub-object base; +0x554 is an unrelated page counter); §8.1 action ids — server
  PLATES 400/401 commit a selection, pagers 115..124 select nothing, 101=quit; §7.6 lower-panel atlas =
  loginwindow_02; §10/§11.3 caption ids. structs/guwindow.md §2.1 (+0x554 = page counter, not a 2nd
  sub-state), §6 (CommonLoginWindow size pinned EXACT 0x558 = 1368 B). frontend_scenes.md §1.4a (PIN
  keypad confirm/cancel = keypad tags 12/13, reset 11, NOT window 111/112), §1.4c (msg.xdb 4001-4022 = a
  static stacked notice column, NOT server-list captions; 4025-4028 softened — cached-notice sourced,
  only 2204 confirmed inline). Curtain CONFIRMED (+5/tick, accumulator thresholds 200/222, SFX 861010105
  on sub-state 1→2). Also reworded a pre-existing firewall leak in frontend_scenes.md §1.5 (a disassembly
  fragment → neutral prose). Capture/debugger-pending (flagged, not guessed): PIN keypad scramble
  seed+permutation; substate-31 account/save gating; account-vs-password TAB token order; server-record
  +6 open_time packing; curtain ms-per-tick.
- 2026-06-17  Phase C Load(2) re-confront — ALL PASS, no drift. LoadingWindow: one rand()%3 picks
  loading.dds/loading06.dds/loading08.dds; progress bar = 223*progress/100 px, a horizontal U sub-rect of
  the SAME loading DDS (U clamp 0.21777 = 223/1024), left→right, NO text/percent (caption baked into art);
  progress = boot-thread VFS cumulative-bytes getter (not a timer; barely advances — a faithful property);
  loading cue 920100100 (cat-0 music, loop 1 — the double-music handoff with char-select BGM 920100200);
  exit on worker completion (run-flag byte cleared) → WinMain reads [OPENNING]SKIP → state 3 Opening or
  state 4 Select. No spec change (frontend_scenes §2L / resource_pipeline §2 already faithful). Asset-side
  pending (not a spec error): exact bar pixel rect depends on loading*.dds dims (UV constants 0.75/0.21777
  confirmed).
- 2026-06-17  Phase D Opening(3) re-confront — ALL PASS, no drift. OpeningWindow: 4 panels openning_001..004
  dwell 17500 ms each, alpha cross-fade ±1/frame [0,250], panel 4 LOOPS (no auto-finish — CAMPAIGN-16 holds);
  scenario crawl = 1024×2048 quad centred X, base Y=field−200, 1000 ms start gate, pos += dt×30 u/s, clamp
  1843, no wrap (positional translate; "upward" = port read of +Y increment), manual nudge actions
  1004/1005; skip = keyboard 10/27/32 (Enter/ESC/Space) or click action 100 → WritePrivateProfileString
  [OPENNING]SKIP=1 → WinMain case-3 pre-set GameState=4 Select (NO timed/movie/loading auto-advance);
  intro BGM 910061000 loop (distinct from login 861010105). No spec change (intro_sequence + frontend_scenes
  §1.0 already faithful). Residual capture-only: realized fade wall-clock; .ogg form of 910061000.
- 2026-06-17  Phase E Select(4) 2D-chrome re-confront — 11 PASS, 4 DRIFT corrected in ui_system.md
  §8.2/§8.4 (binary wins): (1) Create/Delete/Enter (act 4/5/6) at dst-Y 112; the former y=325 widgets are
  create Confirm/Cancel (act 35/36), 413/531 = their HOVER src-X (NOT action ids); (2) NO "5 roster 2D
  plates" — one shared left info panel + 3D ray-pick, per-slot occupancy from the record class/state word
  at +0x66 (0=empty), count caption msg 2209; (3) stat-icon grid = 10-cell (2×5) with build-time-literal
  column origins (col-1 500,770 / col-2 524,770; HOVER 548/572) doubling as the point-buy ± cells (NOT
  18-cell/runtime/debugger-pending — that earlier claim was a spec error, now fixed); (4) corner-close
  re-flagged base GUWindow chrome / debugger-pending (close action type 13 / id 10001 → quit confirmed).
  Confirmed unchanged: tabs Server/Channel/Back act 1/2/3; class buttons 10-13 → Monk4/Musa1/Dosa3/Salsu2;
  point-buy ± 25-34 (class floor 10); face ± 21/22 (1-7); name textbox (60,80,274,18) min-2 / a-z·0-9·CP949
  pairs / 16 payload bytes, err msg 2190/2075/12012; captions modal-title 14003-14007 + npc.scr keys 1-4 +
  count 2209; 8 atlases; 3D preview REUSED unchanged. Debugger-pending: corner-close geometry/src origin.
- 2026-06-17  Phase F In-game(5) HUD core re-confront — 9/10 core panels byte-confirmed (binary wins).
  Open items resolved: skill-bar per-slot registry (base X +0x10, Y +0x14 biased −92, slot +0x04, action
  +0x08, icon texset +0x0C, overlay flags +0x28..+0x2A, overlay rects +0x2C..+0x70; layout variant by the
  looked-up entity KIND word: 5→146×49, {0,6,7,11,18}→297×50, else→58×58; overlay-rect VALUES data-driven
  = debugger-pending); stat sibling A/B/C = ActorStatePassivePanel / ActorStateCashPanel / ActorStateSkillPanel
  (TEXT read-outs, NOT gauges — HP/MP is the separate §5.6 right-edge composite); inventory item-grid =
  8×5=40-cell bag (38×38 cells, +38px flush pitch, actions 0..39) + 20-cell equip sub-grid (actions 50..69)
  + hand-placed paperdoll (closes campaign-12 "PLAUSIBLE grid" + open-item 16). Identity drifts: the §5.5
  "target/close-up frame" slot 135 is UpgradeProcessPanel (weapon-enhance: progress-% + 3D item preview),
  NOT the target plate — the real selected-target frame (OtherInfo/MopGagePanel/GagePanel) was NOT recovered
  → HUD-II follow-up; in-game bag = ItemPanel (not GatherSlotPanel = gather/craft progress); minimap =
  MapPanel (slot 0x284) vs TotalMapPanel (slot 0x288); MainWindow +0x500 = a ~0xC8-byte state-5 command
  handler (not the 16-byte hub). Buff payload = +4 i32 atlas_x / +8 i32 atlas_y into stateicon.dds (matches
  misc_data.md §1.3; ui_hud_layout §2 was stale). Chat geometry: chat.md's 448×324 output + 330×20 input is
  truth (ui_hud_layout §1.2's 268×462/290×18 were mis-reads); chat.md §6.2 field order transposed (+0x1C
  channel / +0x20 colour). mainwindow.dds is a FRONT-END atlas, not in-game status chrome. Specs corrected:
  ui_hud_layout.md (9), chat.md (2), ui_system.md (§8.6.1/§8.7/§8.8/§8.10), structs/runtime_singletons.md
  (§3.10/§6/§9); formats/misc_data.md unchanged. names.yaml candidates (Phase-G sync): MapPanel, TotalMapPanel,
  ItemPanel, GatherSlotPanel, UpgradeProcessPanel, ActorState{Passive,Cash,Skill}Panel, StatusPanel, the
  state-5 cmd handler @MainWindow+0x500, g_StatNameMsgIdTable (60001-60022). Debugger-pending: skill-hotbar
  overlay-rect values; the real target plate (HUD-II); absolute pixels of screen-relative panels.

## CAMPAIGN 17 — summary (HUD/UI total rebuild, scene by scene, from scratch, IDA-faithful)
Rebuilt the ENTIRE HUD/UI layer from scratch on a fresh `Ui/` substrate (layer 05), scene by scene in engine
order, each re-derived zero-trust from doida.exe 263bd994 + Docs/RE specs — NO screenshot oracle this cycle.
**Phase A (foundation):** fresh Ui/Widgets (HudWidget/button/label/panel/list/textbox/scrollbar/checkbox +
AlphaFade ±64/tick + factory) + Ui/Assets (atlas/icon/text libraries) + HudFont; corrected ui_system.md §2
vtable to the LAYERED 13/14/15 shape (was a phantom flat-16) + pinned GUButton font-slot +0xE8. **Phases B–E
(front-end, from scratch, controllers re-pointed):** Login (LoginWindow + PinSubView + ServerSelectSubView;
7 drifts — one +0x238 sub-state, plates 400/401 vs pagers 115-124, PIN tags 12/13/11, 1368 B, curtain +5/tick
200/222, notice column 4001-4022), Load (LoadingWindow; fill = U[0,223/1024] sub-rect of the same DDS — fixed
an old speculative UV), Opening (OpeningWindow; 4-panel loop + crawl +30u/s/1843 + skip→SKIP=1; no drift),
Select (CharSelectWindow + CharSelectEventDrainer over the REUSED 3D preview; 4 drifts — Create/Delete/Enter
dst-Y 112, no "5 plates", stat grid 10-cell, corner-close base-chrome). **Phase F (in-game core):** fresh
Ui/Hud (HudMaster + right-edge gauges + chat + minimap + buff + hotbar + inventory[I] + skill[K] + char-stats);
9/10 core panels byte-confirmed (skill-bar registry, stat siblings A/B/C = text, inventory 8×5+20-cell, buff
+4/+8, chat 448×324+330×20); the "target frame" (slot 135 = UpgradeProcessPanel) proved mis-identified — the
real target plate not recovered → HUD-II. IHudEventHub verified-faithful (NOT rewritten — engine-free tested
plumbing). Specs corrected across ui_system.md / structs/guwindow.md / frontend_scenes.md / ui_hud_layout.md /
chat.md / structs/runtime_singletons.md; firewall PASS. Each scene gated (build 0/0 · headless clean · firewall
· diff-oracle vs the in-place old code). **FINAL GATE:** clean slnx build 0/0 · 2049 tests green (12 suites) ·
Godot build 0/0 · headless full-spine 0→5 with HUD + world clean · DAG clean. **Deferred:** physical removal
of the now-dead legacy front-end (BootFlow + old Screens/HUD; entangled via ServerEntry / OpeningWindow.SkipCfgPath,
needs maintainer confirm); names.yaml hand-merge; the HUD-II long tail (real target plate + ~100 secondary
panels); HP/MP Vitals hub channel + hotbar overlay values + minimap per-area DDS naming (world-campaign /
debugger). Uncommitted on campaign15 (commit on maintainer request).

## CAMPAIGN 17 → HUD-II + legacy removal (2026-06-17 continuation, post-commit)
After the CAMPAIGN-17 core rebuild was committed, removed the now-dead legacy and pushed the in-game HUD to functional.
- LEGACY REMOVAL: deleted the dead front-end (BootFlow.cs + Boot.tscn + Screens/{LoginScreen,PinModal,
  ServerSelectScreen,ServerListDrainer,OpeningWindow,LoadingScreen,CharacterSelectScreen,CharListEventDrainer})
  after extracting ServerEntry → Screens/ServerEntry.cs and re-pointing ClientContext's SkipCfgPath to the new
  Ui Opening window; deleted the old in-game HUD (16 HUD/* files = GameHud + panels) once consolidated; deleted
  the old toolkit (Screens/Widgets/{StateButton,WidgetFactory,AlphaFadeDriver}, Screens/UiAssetLoader,
  Screens/Layout/CharacterSelectLayout). Screens/ now holds only active files (3D preview, ScreenHost,
  FrontEndAudio, NpcScrDescriptions, ServerEntry, LoginLayout); HUD/ folder empty.
- IN-GAME HUD CONSOLIDATION (the crux): Phase F had added Ui/Hud/HudMaster ALONGSIDE the old GameHud (GameLoop
  still drove GameHud, BYPASSING IHudEventHub → the new HUD drained empty channels). Fixed: GameLoop now PUBLISHES
  its world events into IHudEventHub (instead of GameHud.OnXxx), HudMaster is the SOLE HUD (old GameHud removed
  from InGameScene/World.tscn), HudRightEdgeGauge drains the new Vitals channel, InputRouter's HUD-gate →
  HudMaster.HitTest, AudioService's UI-click SFX → the new HudButton (meta is_hud_button) not StateButton.
- LAYER-04: added a VitalsChanged channel + HudVitalsEvent (HP/MP/stamina current+max, clamped ratios) to
  IHudEventHub (the gauges' missing feed). slnx 0/0, 2068 tests (+19).
- HUD-II W (IDA recovery, doida.exe 263bd994): the REAL selected-target plate = MopGagePanel (HUD slot 177),
  full geometry (screen-centred W=226; HP bar (35,5) 172×6 src(40,517) uitex 1; 3D portrait (13,55) 200×200;
  close/nav actions 3/1/2; msg 16001; client-side target-driven). Vitals = current/max poll model (5/52 delta,
  5/32·4/13·4/1 absolute; max computed; no 4/2 push). Minimap corner radar = RUNTIME BMP-tile mosaic
  data/effect/map/d<area3>x<X>z<Z>.bmp (3×3 128px ring) — NOT map%d.dds (the dead per-area-DDS panel = the
  campaign-17 map2.dds-bug root cause). Hotbar overlay = 3 hardcoded 29×29 frames (src-X 763/792/821, src-Y 655,
  uitex 3); per-skill values from .do. Specs promoted: ui_hud_layout §5.4a/§5.5b/§5.6/§5.10a, ui_system §1.7.1,
  combat §13. names.yaml candidates: MopGagePanel(177)/GagePanel(157)/OtherInfo/MapPanel/TotalMapPanel/map-marker
  keys 52,29 + hotbar builder roles. Firewall PASS.
- HUD-II E: built HudTargetFrame (MopGagePanel, drains hub.TargetChanges — HudMaster now 10 panels), rewrote
  HudMinimapPanel to the BMP-tile mosaic (map%d.dds path retired), added HudSkillHotbar cooldown/charge/ready
  overlay. Build 0/0, headless clean (10 panels). TODO(world-campaign): live player-tile feed for the BMP mosaic;
  TargetChangedEvent.Level field; msg 10037/10038 relation tag; MopGagePanel dstY (debugger). Uncommitted.
- 2026-06-17  HUD-II Wave 1 W+P — recovered 6 secondary in-game windows (doida.exe 263bd994), all build-ready.
  OptionPanel (4 tabs + Character/Sound/Graphic/Other; centered 215×204; checkboxes act 2-13 msg 8009-8039
  non-monotonic; CLIENT-LOCAL, persists option.ini CHAR_* + DoOption.ini OPTION_*; uiconfig.lua NOT used in-game).
  PartyPanel (screenW+318×318 right-dock — RESOLVES ui_hud_layout §3.3; 8 members 54px stride, 124×5 HP/MP/EXP
  bars; invite 2/35 / accept 2/36 / leave-kick 2/37; populate 5/21+5/38). KeepPanel/TradeKeepWindow (318×732;
  single 60-cell 10×6 grid TOGGLED my/their side, NOT two grids; uitex 4; cells 200-259, commit 260, toggle
  261/262, money 263/264; populate 4/24 44B their-side + 4/25 full-refresh, opened 4/23 phase=3). FriendPanel
  (TWO-TAB add/cut NOT add/search; add act 2 "friend %s %s" / cut act 3 "cut %s"; 30 slots/tab; uitex 9; outbound
  2/49+2/54; inbound 5/26 candidate UNVERIFIED). GuildAPanel (50-member, paged 4600/4601; action map 4501-4623;
  populate 4/65 1812B = ~60B header + 17B name×50 + parallel arrays, NOT 50×36). QuestPanel (3 tabs+detail;
  uitex 8; accept 85 / proceed 86 / giveup 91 / track 94; populate 5/68 452B + 5/73 344B = opaque blobs,
  capture-pending). Specs: ui_system §8.6.1/§8.9.1/§8.12-8.16, ui_hud_layout §3.3/§5.3, opcodes.md
  (+2/49,2/54,4/24,4/25,4/65,2/152), new packets 2-49/2-54/4-24/4-25/4-65/2-152. Firewall PASS (221 opcode rows,
  0 warn). **ARBITRATION (Tier-1): opcode 2/152 = ONE generic two-u32 C2S sender with MULTIPLE consumers**
  (QuestPanel row-request + ProductPanel paging) — modeled as one generic opcode, both alias names kept
  (CmsgQuestRowRequest / CmsgProductPage). Residuals (stub w/ documented fallback, NO fake data): OptionPanel
  Sound/Graphic/Other widget tables (sweep-pending; 31 OPTION_* keys known so audio wires); FriendPanel inbound
  feed (5/26 candidate); Quest/Trade/Guild record-value bodies (opaque → pcap-pending). names.yaml candidates:
  OptionPanel(+4 sub), PartyPanel/MiniParty/PartyReqPanel, KeepPanel(C# TradeKeepWindow), FriendPanel, GuildAPanel,
  QuestPanel/QuestInfoListPanel, CmsgFriendAddRemove(2/49), CmsgFriendListRefresh(2/54).
- 2026-06-17  HUD-II Wave 1 E — built the 6 secondary windows on the Ui/Hud substrate as TOGGLE windows
  (HudOptionsWindow / HudPartyWindow / HudTradeWindow / HudFriendWindow / HudGuildWindow / HudQuestWindow);
  HudMaster 10→16 panels (Build + Toggle*/ShowTrade + HitTest auto-covers all 16 children). Each fully built
  (chrome/tabs/lists/actions/captions per ui_system §8.9.1/§8.12-8.16) with DOCUMENTED stubs (NO fake data) for
  the inbound populate feeds (world-campaign: party 5/21+5/38; capture-pending: friend 5/26 candidate, guild 4/65
  value fields, quest 5/68+5/73 opaque, trade 4/24+4/25) and the unrecovered toggle hotkeys (spec-pending;
  GuildAPanel CONFIRMED no-hotkey = context-action open). Build 0/0, headless 16-panel clean. Uncommitted.
- 2026-06-17  HUD-II Wave 2 W+P — recovered 5 more windows + the window-open mechanism (doida.exe 263bd994).
  **TOOLBAR: there is NO dedicated toolbar class** — window-open is a GLOBAL action-id dispatcher on the main HUD
  window where action ids ARE ASCII keycodes (button == hotkey by construction), remappable via per-account INI
  [<account>_KEYSET] — this RESOLVES the Wave-1 toggle-hotkey residuals. Button→slot→key: inventory ItemPanel 158
  (i/b), skill SkillPanel 159 (s), status StatusPanel 146, quest QuestPanel 206 (q), party PartyPanel 220 (k),
  DefaultMenu radial 191 (g), help HelpPanel 322 (h/Space), guild-war 224 (j), stall 228 (l), broodwar 235 (u);
  close-all p, esc-collapse Esc. The DefaultMenu radial has a SEPARATE id space 4000-4024 → SAME MainWindow slots
  (wire open-buttons to the SLOT, not one id space). ProductPanel (slot 230) = NPC PRODUCTION/CRAFTING (NOT a
  vendor — that's the KeepNpcPanel family); 4×2 recipe grid; sends C2S 2/151 (1-byte selector). EmoticonPanel
  (right-dock rail, MainWindow +0x370): page0 text-macros send via chat 2/7, page1 graphical balloons send NO
  packet (client-local overhead balloon slot 327 + sfx 862030103). MessagePanel (slot 190) = screen-centered
  dual-mode modal (mode0 OK / mode1 OK+Yes/No; uitex 2+8, NOT messagewindow.dds; peer of Confirm/BigAlarm/Announce/
  ErrorPanel). Tender/mail family: TenderInfoPanel(118)→2/118 (header-only); CarrierPigeon mailbox(96/98)→send 2/70
  (132B) + letter-req 2/60 (8B); DeliveryPanel(40, 5×8)→claim 2/71 (4B); arrival on existing 1/20. **2/152 CONFLICT
  RE-RESOLVED BY THE BINARY (SUPERSEDES the Wave-1 Tier-1 arbitration): 2/152 = QuestPanel-ONLY (CmsgQuestRowRequest);
  ProductPanel never emits it; 3/8 = a 4-byte money-refresh repaint.** Specs: ui_system §8.17-8.21, ui_hud_layout
  §5.13, opcodes.md (2/152 QuestPanel-only, 2/151 1-byte, 3/8 refined, +4 new C2S 2/60·2/70·2/71·2/118), new packets
  2-60/2-70/2-71/2-118/3-8 + rewritten 2-151/2-152. names.yaml candidates: ProductPanel/EmoticonPanel/MessagePanel/
  TenderInfoPanel/CarrierPigeonPanal[sic]/DeliveryPanel + the HUD-slot→window/key map; CmsgLetterRequest(2/60)/
  CmsgCarrierPigeonSend(2/70)/CmsgDeliveryClaim(2/71)/CmsgTenderConfirm(2/118)/CmsgQuestRowRequest(2/152). Residuals:
  Pet-window slot (NOT 230); MessagePanel S2C notice opcode; mailbox/delivery/tender POPULATE opcodes + record
  layouts (capture-pending); new format specs warranted msginfo.do(128B)/emoticon.do(40B). Firewall PASS.
  **INCIDENT (no permanent loss):** a correction worker ran `git checkout --` and reverted the uncommitted
  §8.17-8.21 spec content; orchestrator detected (empty git diff) + re-promoted from the dirty notes. LESSON:
  spec/code edits are Edit-only — sub-agents must NEVER run git checkout/restore/reset (working-tree verified intact:
  61 deletions + 24 mods + new files all present, build 0/0).
- 2026-06-17  HUD-II Wave 2 E — built 6 windows + wired the ASCII-key window-open dispatch. HudMessagePanel
  (slot 190 dual-mode modal, ShowNotice/ShowConfirm — the faithful replacement for the deleted ConfirmDialog/
  CenteredModal), HudProductWindow (crafting 230, 4×2 recipe, C2S 2/151), HudEmoticonWindow (rail +0x370; page0
  macros → chat 2/7, page1 balloons client-local), HudTenderWindow (118, 2/118), HudMailWindow (CarrierPigeon
  96/98, 2/70+2/60), HudDeliveryWindow (40, 5×8, 2/71). HudMaster 16→21 panels. Key dispatch (§8.17) wired in
  _Input: i/b→inventory, s→skill, k→party, q→quest, c→stats, g→DefaultMenu(TODO), h/Space→Help(TODO), Esc→close-top
  — BACKFILLS the Wave-1 toggle hotkeys. Build 0/0, headless 21-panel clean. Stubs (NO fake data): all outbound
  use-cases (no IApplicationUseCases method yet → world-campaign), all inbound populate (capture-pending), emoticon
  graphical grid (emoticon.do 40B format-pending), INI [<account>_KEYSET] remap (defaults hardcoded). Uncommitted.
- 2026-06-17  HUD-II Wave 3 W+P — recovered 5 more windows; zero-trust corrected 4 mis-IDs (binary won).
  VENDOR ≠ KeepNpcPanel: the buy/sell shop is the IDB-mislabeled SubscriptionPanel @ slot 259 (itemshop.dds),
  opened by NPC-interaction KIND 32 (no hotkey); C2S 2/19 buy(12B)/2/20 sell(12B)/2/115 shop-buy(8B), S2C acks
  4/19/4/20/4/21/4/113/4/114 + 4/115 (vendor money refresh — NOT 3/8). KeepNpcPanel = the NPC dialog menu (no
  wire). DefaultMenu ≠ radial/≠191: slot 148, a horizontal BOTTOM command strip (1024×45, expandable, action
  4000); full entry→action→slot map 4000-4024 resolved (opens Inv 158/Skill 159/Quest 206/Party 220/Help 322/
  Product 230/stance 146/relation 193/…); slot 191 = KeepPanel (unrecovered, future lane). HelpPanel (322) ≠
  panel: a full-screen data/ui/help.dds overlay (member of MainWindow), key h only (Space refuted). AnnouncePanel
  = slot 221 (scrolling banner), ErrorPanel = slot 168 (timed floating notice, msg.xdb-101, auto-dismiss); S2C
  routing RESOLVED (Wave-2 residual): server notice/error → global sink → ErrorPanel+AnnouncePanel+chat;
  SmsgShowPopupByCode 4/500 (4B, codes 1-7) + result-with-message 4/132/138/140/146; MessagePanel(190) is NOT a
  wire destination (client-side level-milestone only). Pet window RESOLVED: slot 194 PetPanel = the COUPLE/pair-
  relation window (no tamed-pet feature in this build; zero pet assets), fed by SmsgActorPairRelation 5/53 (32B)
  + 5/42/5/64, auto-show on pair push (not a hotkey). Specs: ui_system §8.22-8.26, ui_hud_layout §5.13 corrected
  (148=DefaultMenu + 191=KeepPanel; 322 overlay; +168/221/194/259), opcodes.md (225 rows, 0 warn; 2/19 re-attributed
  CmsgNpcBuyOrAcquire, 2/115 8B, 4/500 4B), 8 new + 2 rewritten packets. names.yaml candidates: SubscriptionPanel
  (vendor 259, IDB-mislabeled), PetPanel(194), AnnouncePanel(221)/ErrorPanel(168), KeepPanel(191), rename
  2/19→CmsgNpcBuyOrAcquire. Orphan packets/2-19_npc_interact.yaml left intact (Tier-1 tidy). Residuals: opcode
  field VALUE semantics (12B vendor bodies, 4/500 popup strings) capture-pending. Firewall PASS. Edit-only (no git).
- 2026-06-17  HUD-II Wave 3 E — built 6 windows + the bottom command strip. HudCommandBar (DefaultMenu slot 148,
  bottom strip, 11 entry buttons wired entry→HudMaster.Toggle* per §8.23: Inv 4001/Skill 4003/Quest 4004/Status
  4005/Help 4011/Party 4012/Product 4013 — makes the HUD MOUSE-navigable), HudVendorWindow (SubscriptionPanel 259,
  buy/sell, C2S 2/19/2/20/2/115), HudHelpOverlay (help.dds full-screen overlay, key h — backfills the Wave-2 Help
  TODO), HudAnnouncePanel (221 banner, ShowAnnounce), HudErrorPanel (168 timed notice, ShowError + auto-dismiss),
  HudPetPanel (194 couple/pair window, ShowPartner/ClearPartner). HudMaster 21→~27 panels. Build 0/0, headless
  ~27-panel clean. Stubs (NO fake data): vendor stock + all outbound use-cases (world-campaign); the 4/500 popup-
  by-code S2C sink → Error/Announce (world-campaign); pet 5/53 feed (world-campaign); command-bar slot 161/164/185/
  240 entries (TODO). Uncommitted.
- 2026-06-17  HUD-II Wave 4 W+P — recovered the last 6 toolbar/NPC windows + resolved identities. KeepNpcPanel
  (slot 152) = the NPC keep/storage DIALOG MENU (option list; emits nothing on wire); full 35-case KIND→target-window
  router (KIND 9→storage KeepPanel 191, 0x20→vendor 259, 0xE→repair 150, 0xF→guild 153, default→quest-giver 287;
  descriptor KIND@+0x22). KeepPanel (191) RESOLVED = the player STORAGE/WAREHOUSE — a 60-cell 10×6 item grid (actions
  200-259, money 263/264); opened ONLY via KIND-9 NPC → KeepNpcPanel (not a hotkey); item move C2S 2/46 / quick-move
  2/44, storage open/money 2/142. RelationPanel = slot 193; BuddyRelation = slot 185 (social window opened by
  DefaultMenu 4002, emits chat-command friend/cut — likely the SAME social surface as the Wave-1 FriendPanel; reconcile,
  do not duplicate). StallListPanel (228, key l) = player market list; populate S2C 4/74; emits 2/74/2/56.
  BroodWarListPanel (235, key u) SCOPE-CORRECTED = the guild-DIPLOMACY relations list (declare-war/ally), NOT events;
  emits 2/81, result 4/81. GuildWarInfoPanel (224, key j) = read-only war info; populated by S2C 5/73 (344B).
  **CONFLICTS resolved (binary wins): 5/73 = SmsgGuildWarInfoUpdate, NOT quest-complete (corrected opcodes.md, fixed
  the false 2/28+2/152 cross-refs, 5-73_quest_complete.yaml → deprecation redirect; quest results = 5/68 ONLY); slot
  185 BuddyRelation ≠ 193 RelationPanel; storage 2/142 vs item-move 2/46/2/44.** Specs: ui_system §8.27-8.32,
  ui_hud_layout §5.13 (+152/185/193/191/224/228/235), npc_interaction §7, opcodes.md (+5 C2S 2/44·2/46·2/56·2/74·2/81;
  renames 4/74→SmsgStallListRefill, 4/81→SmsgGuildDiplomacyResult, 5/73→SmsgGuildWarInfoUpdate), new packets + 5-73
  redirect. validator 230 rows 0 warnings. Firewall PASS. Edit-only (no git).
- 2026-06-17  HUD-II Wave 4 E — built the final 6 windows. HudKeepNpcDialog (152, NPC menu — routes sel→storage/
  vendor), HudStorageWindow (KeepPanel 191, 60-cell storage grid, 2/46/2/44/2/142), HudStallListWindow (228, key l,
  2/74/2/56), HudGuildDiplomacyWindow (235, key u, 2/81), HudGuildWarInfoWindow (224, key j, 5/73 stub), HudRelationPanel
  (193). HudMaster ~27→~33 panels. KeepNpcDialog wired to the existing Vendor/Storage; l/u/j keys + DefaultMenu 4002→
  Relation wired. BuddyRelation (185) NOT built (layout unrecovered — separate lane; 4002 interim → Relation/Friend).
  FriendPanel(§8.14)/RelationPanel(193)/BuddyRelation(185) = 3 distinct social classes (no duplicate built). Build 0/0,
  headless ~33-panel clean. Stubs (NO fake data): all inbound populate + outbound use-cases (world-campaign), atlases
  via literal paths (uitex-pending). **HUD-II window set ≈ COMPLETE (~33 panels: 10 core + 23 windows across 4 waves
  + command bar + key/mouse navigation). Residual = world-campaign live data-wiring + a few unrecovered/niche panels
  (BuddyRelation 185, the deep ctor-tail).** Uncommitted.

## 2026-06-18 — re-static-analyst + protocol-spec-author (front-end scene rebuild, campaign15)
- binary: doida.exe @ 263bd994
- tool: IDA Pro 9.3 via MCP (mcp__ida__*, static only)
- analyzed (by canonical name): WinMain scene state machine (states 0..3 spine), Diamond GUComponent/
  GUPanel/GUButton/GULabel/GUTextbox/GUCheckBox framework + AddChildWithAction dispatch, LuaConfig
  (uiconfig.lua scalar loader), LoginWindow build/OnEvent/per-frame sub-state driver (flowSubState),
  LoginSecondPassword keypad (PIN), GameVer index-5 gate, LoadingScreen render tick + VFS progress +
  boot worker, COpeningWindow slideshow/crawl/skip-gate, lobby host resolution (ip.txt/list.dat/
  servername registry), server-list + channel-endpoint queries, secure-context key-string builder +
  login packet 0x2B, 0/0 key-exchange parse.
- specs produced/updated:
  - Docs/RE/specs/frontend_layout_tables.md (NEW — the build oracle: full login widget table, sub-state
    machine + per-sub-state visibility, curtain, gates, Save-ID, credential hand-off; PIN keypad;
    server-list display model; loading immediate-mode quads; opening cinematic; audio cues)
  - Docs/RE/specs/login_flow.md (server-address resolution chain, 62-byte 0/0, dynamic game endpoint)
  - Docs/RE/packets/login.yaml (1/4 credential reply, M pad parameter-driven), Docs/RE/packets/lobby.yaml
    (8-byte wrapper + LZ4, 8-byte records, channel-endpoint 30-byte "host port")
  - Docs/RE/opcodes.md (0/0, 1/4 re-confirmed; frame header two u16 words)
- notes: PIVOTAL — login/select/opening UI geometry is HARD-CODED (uiconfig.lua read for a single int
  NEW_SERVER_INDEX only); retired the "uiconfig.lua ~340 widgets" claim. Corrections: single login
  sub-state field; no ±64 alpha fade (curtain is +5/tick Y-offset only); game.ver = single u32 index-5
  equality; SKIP gate read in load case 2 (not init); loading screen = immediate-mode 2-quad renderer
  (random loading.dds/loading06.dds/loading08.dds); opening alpha ceiling 250, crawl +Y must invert for
  Y-up; PIN keypad = 100 stacked buttons (10/cell), time-seeded Fisher–Yates scramble (seed DEBUGGER-
  PENDING). capture_verified: false (no .pcapng present). names.yaml sync owed (orchestrator/maintainer).

## 2026-06-18 — re-static-analyst ×5 + Tier-1 (front-end scene re-confirmation + C#/Godot fix, campaign15)
- binary: doida.exe @ 263bd994
- tool: IDA Pro 9.3 via MCP (mcp__ida__*, static only — no debugger per maintainer)
- re-confirmed (by canonical name): WinMain scene spine (states 0..7 + 8 terminal sentinel; Init
  auto-advance; OPENNING/SKIP gate non-zero→Select / zero→Opening; reload 202/203/232; GameState
  {state, subState=8, errorDetail, debugMode}); LoginWindow_OnEvent action map; LoginSecondPassword
  keypad tags + scramble; lobby server-list 8-byte record + commit guard; LoadingScreen + COpeningWindow
  render constants.
- KEY CORRECTIONS to frontend_layout_tables.md (binary overrides a prior CODE-CONFIRMED reading):
  - §2.2 action map: **102 = open quit-confirm ExitPanel (a distinct child object), NOT "show server-list"**;
    **105 = throttled restart server-list fetch (→34, ~10s)**; 101 = app quit (Engine run-flag clear);
    112 = same as 102; 113/114 = hide re-fetch popup + restart fetch (→34). The server-list is reached
    ONLY via the sub-state machine after PIN (33→…→37) — no form action opens it. Idle = state 6;
    real hand-off = state 40 (41 = post-hand-off idle). Verified by reading LoginWindow_OnEvent.
  - §3 PIN keypad tags: **Reset=11 (ScrambleKeypad: wipe+re-roll), OK=12 (SubmitOk), Cancel=13** — no
    separate clear/backspace verb.
  - §4 server-list colors (ARGB): red 0xFFFF0000 / orange 0xFFED6806 / yellow 0xFFFFFF00 /
    green 0xFFB5FF7A (≤500: numeric text, no msg id); status 3 → 6004 only when load(+4)==24 else 6005
    from +4/+6. Record fields +0 id / +2 status / +4 load / +6 open_time (no swap).
  - §5 progress-bar depth literal = 0.5859375f (not 0.108; irrelevant to the 2D port).
- C#/Godot fixes (layer 05) measured against the corrected oracle: PinSubView tag swap fixed +
  dragon-frame chrome (InventWindow 318,647,340×190) + transparent modal capture (removed invented
  0.6 black dim); LoadingWindow TextureRect ExpandMode.IgnoreSize (bg 1024×768, fill 223×223 — was
  ballooning to 1024×1024); ServerSelectSubView exact colors + dead BuildBackControl/BackRequested
  removed; LoginWindow action handler re-aligned (102/112→ExitConfirm, 105→restart-fetch, 101→quit).
  Application layer (SceneStateMachine / LoadOrchestrator / LobbyServerRecord) re-verified faithful — no
  change. Verified via headless LayoutDump (numeric oracle, no screenshots) before/after.
- notes: dirty findings in Docs/RE/_dirty/campaign-frontend/. capture_verified: false. names.yaml sync
  owed (orchestrator/maintainer): LoginWindow_OnEvent, GameVer_CompareIndex5, Lobby_FetchServerList,
  Diamond_LoginSecondPassword_* (builder / OnEvent / ScrambleKeypad / SubmitOk),
  Diamond_OpeningWindow_BannerSlideshowFSM/ScenarioCrawlUpdate, Boot_LoadDataTableCorpus.

## 2026-06-18 — CYCLE 18 Phase A+B: re-cleanroom-orchestrator (6 static-IDA lanes) + spec-authors (front-end scenes, campaign15)
- binary: doida.exe @ 263bd994
- tool: IDA Pro 9.3 via MCP (mcp__ida__*, **static only — no debugger** per maintainer)
- scope: pre-character-select scenes (Init/Login/Load/Opening) + login sub-flows (PIN, server-list). Char-select and world out of scope.
- dirty findings (gitignored): Docs/RE/_dirty/frontend18/{pin-scramble, serverlist-record, saveid-ini, load-bar-rect, login-substates, opening-fade}.md
- promoted to clean specs:
  - frontend_layout_tables.md (author: asset-spec-author): §3 PIN scramble — seed = whole-second time()
    (CRT srand; NOT GetTickCount/ms), ASCENDING uniform shuffle (j=rand()%i, i=2..10; MSVC random_shuffle
    shape), reproduce-by-mechanism; §2.5/§7.10 INI split — Save-ID = DoOption.ini [DO_OPTION] OPTION_ID,
    [OPENNING] SKIP = option.ini (five distinct EXE-relative inis); §5 load-bar — rect X −499/−170,
    Y −363/−140 (329×223) + fill clamp(223·pct/100,0,223)/1024 CONFIRMED, depth → Z=1.0 (ortho near/far
    0/1; irrelevant to the 2D port); §2.1/§2.2 visibility — set IMPERATIVELY on edges (no declarative
    table); CORRECTED bands: Background from state 2 (was ≥3); curtains = always-present Y-animated host
    panels; interactive credential group ≈ 5..33 (was 3..32, shown on 5→6 edge); server-list CONTENT
    panel 35..37 (was 33..37; 33 only starts the worker); PIN keypad 31/32 CONFIRMED; §6 Opening — WinMain
    state-3 pre-sets next=4, fade only governs when the blocking loop returns; residual = armed-flag site.
  - lobby.yaml (author: protocol-spec-author): Record Shape A static-CONFIRMED — 8B LE
    {+0 server_id i16 (1..40), +2 status_code, +4 load, +6 open_time}; commit/selectability gate
    status_code==0 && load<2400 (0x960). DENIED the ServerId==100 gate (==100 is a display-only
    NEW_SERVER_INDEX label reposition, commits nothing); +6 = open_time (status==3 → HH:MM from +4/+6),
    NOT a Flag. On-wire byte VALUES remain capture-pending.
- C#/Godot fixes (Phase C, layers 05/04/02) measured against the corrected oracle: ClientContext wires
  LoginHandshakeDriver (was null → 0/0 never answered with 1/4); LoginWindow single-source visibility
  (structural _pinKeypadRoot mirroring _serverListRoot, 8 direct .Visible writes removed) + bands
  Background≥2 / server-list 35..37; PinSubView scramble seed→whole-second time() + ascending shuffle;
  LobbyServerRecord/LobbyServerEntry/ServerListEntryView field rename {Status,Population,Flag}→
  {StatusCode,Load,OpenTime} + gate fix. Verified via headless LayoutDump (numeric oracle, no screenshots).
- notes: capture_verified: false. names.yaml sync owed (orchestrator/maintainer): the PIN-scramble /
  server-list-parse / Save-ID-ini / load-bar-draw / login-substate routines by canonical name.

## 2026-06-18 — Scene re-confirmation campaign Phase 1 (read-only re-confront) + Phase 2 (spec promotion) — re-orchestrator + spec-authors
- binary: doida.exe @ 263bd994 (SHA confirmed against names.yaml; IDB module = doida.exe, hexrays ready)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*, ?ext=dbg endpoint UP) — Phase 1 STATIC read-only; Phase 2 firewall bridge (no IDA, dirty→clean rewrite)
- scope: first four scenes — Init, Login, Load, Opening + the scene state machine — re-confronted against the live IDB; corrections promoted into committed specs. Char-select and world out of scope.
- dirty findings (gitignored, Phase 1): Docs/RE/_dirty/scenes/{scene_state_machine, init, login, load, opening}.md
- promoted to clean specs (Phase 2, one author per file):
  - specs/client_runtime.md (Init §0/§7): de-swapped resolution setters (WIDTH→+44465 cap 1920, HEIGHT→+44466 cap 1200; the full-desktop-only global is a width value, NOT a height-setter width cap); re-described the init "logger/profiler" as a third-party CRASH-REPORTING SDK (unhandled-exception filter + crash log + symbol-index load) — NEUTRAL PROSE ONLY, no application-identity string and no crash-submission endpoint committed (non-distribution); corrected active color depth = forced 16-bit (32-bit is only the settings ctor default). Added: SetThreadExecutionState(DISPLAY_REQUIRED|CONTINUOUS) step 1, C++ terminate-handler step 2, display-mode 1→2 promotion, bring-up order (scheduler→OS window "diamond engine application"→D3D9 device→15 font slots→effect reset), DoOption map (index[30]@+120 = display mode; option.ini path buffer @+1165), the symindex_dx9-style DirectX symbol-index file.
  - specs/intro_sequence.md (Opening): scenario crawl = a SINGLE pre-rendered DDS sprite data/ui/openning_scenario.dds (1024×2048) translated in Y — NO font slot, NO typeset text, NO string-table source (corrected the typeset framing); teardown helper is NOT a destructor — it dispatches a NAMED engine command on the driver, then dispose-list push + slot-0 scalar-deleting destructor. Kept the 4-frame slideshow / crawl clamp / mouse-wheel scrub / alpha crossfade.
  - specs/resource_pipeline.md (§4 Load): transcribed the FULL ORDERED 48-file boot data-table corpus as the authoritative registration order; preserved filename quirks (OPENNING double-N, discript.sc not .scr, Tutor.scr capital T, musajung.do, items_extra.do); worker thread raised to ABOVE_NORMAL; progress denominator = build-time literal 9,395,240 bytes (re-confirmed/kept). Kept the [OPENNING]/SKIP gate.
  - specs/login_flow.md + specs/frontend_scenes.md (§1): re-confirmation lane — all login opcodes/sizes/routing (0/0, 1/4, server-list port 10000, channel-endpoint port 10000+server_id, 3/1 Login→Select GameState=4, 1/9+3/5 in Select) re-CONFIRMED, no change. Candidate facts (login sub-state field @+0x238 init=1 + transition table; lobby length-prefix u32-LE NUL-inclusive; PIN keypad 2×5 / 100 buttons / tags 11=reset,12=OK,13=cancel) verified ALREADY PRESENT — no duplication added. EULA check: no mislabel — frontend_scenes already carries the corrected reading (4001–4022 = a static caption column, explicitly NOT an EULA).
  - specs/client_architecture.md + specs/client_workflow.md: U0 fully confirmed — verification banners re-dated only, no content change.
- banners: all six touched scene specs + the two U0 master specs re-dated ida_reverified 2026-06-18 (note "scene re-confirmation campaign (build 263bd994)").
- firewall: clean-room + non-distribution audit PASS over all touched specs. The BugTrap crash-reporter application-identity string, version, support email, and crash-submission IP:port were NOT committed (verified absent across the whole specs tree). Remaining scanner hits are documented false positives: a Win32 WS_EX_TOPMOST constant, u32 wire length values, the firewall banners' own forbidden-identifier list, and the pre-existing engine project slug do_korea_service_dx9 (engine-identity interop fact, distinct from the crash-reporter identity).
- notes: capture_verified: false. names.yaml NOT touched (deferred to Phase 7). IDB annotations / dirty-only fixes (EULA→caption comment; the two stale IDB labels — dispose-list-push mislabeled as an Opening-window ctor, and the slot-record vector-insert with a stale "WinMain" label) deferred to Phase 7. GAP list carried to Phase 5 — DEBUGGER: which Init failure → error state 7 (codes 1 vs 3); the live display-mode value + live resolution; the PIN per-show scramble seed + PIN-token slot width. CAPTURE: server-list record on-wire packing; channel-endpoint tail DNS-vs-dotted-quad; RSA blob contents; whether the second load pass (in-world reload via 3/100) replays the full 48-file corpus or short-circuits cached tables. STATIC follow-up: the literal on-disk INI filename behind [OPENNING] (+1165 path buffer); the D3D blend-factor enum mapping; the realized Opening crawl rate + fade duration.

## 2026-06-18/19 — Scene re-confirmation campaign Phases 3–7 (C# corpus + strict-1:1 reconstruction + verification + deliverables) — Tier-1 + port-orchestrator
- binary: doida.exe @ 263bd994 (unchanged). Scope: first four scenes (engine states 0-3: Init/Login/Load/Opening) + the scene state machine. Char-select (4) and world (5) explicitly DEFERRED to a follow-on.
- Phase 3 (C#, layer 04): restored the state-2 boot corpus (LoadResourcePlan.BootWorkerPaths) from a curated ~9 paths to the full 48-file ordered corpus of resource_pipeline.md §2.1a, existence-aware (absent VFS entries warn-and-continue, 0 bytes; the 9,395,240 progress denominator + [OPENNING]/SKIP gate untouched; no catalogue parse added — raw-byte progress only). nuked build 0/0.
- Phase 4 (Godot verify): ran the corpus against the real 43,347-entry VFS — 51/52 paths load, 1 (effect.cache) warn-and-continue, 0 crash; forced [OPENNING]/SKIP=0 to render Opening (state 3) for the first time; LayoutDump Login/Load/Opening = MATCH vs frontend_layout_tables.md; visual oracle (Login + Opening screenshots) reviewed = faithful.
- maintainer review: rejected the verify-and-fix posture as having VALIDATED polluted/non-faithful code; directed a strict-1:1 reconstruction of the scene C# with ALL non-original noise removed (dev/offline/verification scaffolding deleted entirely; the client may REQUIRE the real VFS). Two adversarial noise audits (layer 04 + 05) catalogued the pollution.
- Phase 3R (strict-1:1 reconstruction, port-orchestrator; states 0-3 only):
  - FSM unification: DELETED the redundant second state machine (ClientStateMachine + ClientState enum + ClientStateChangedEvent) — the original has ONE WinMain switch; SceneStateMachine is now the SOLE faithful FSM. Removed 4 unreachable SceneStateMachine methods; migrated all consumers (ApplicationUseCases, GamePacketHandler, ClientContext, AudioService, InputRouter, GameLoop, LoginHandshakeDriver) to EngineSceneState/SceneStateChangedEvent.
  - 3 fidelity FIXES: (a) removed the invented Opening "phase-4 auto-exit" (it carried a fabricated spec citation; per intro_sequence.md panel 4 holds and loops, skip is the sole exit); (b) per-scene audio cues — Login = curtain stinger 861010105 only (Login has no BGM), Opening = 910061000 only, lobby BGM 920100200 + click 861010101 kept at the Select call-site (not bled into states 1/3); (c) IMPLEMENTED the real msg.xdb 5001..5040 server-name lookup (replacing fabricated "Server N" English placeholders).
  - noise purge: deleted all dev/offline/verification scaffolding (DevAccount creds, DevPrefill, IsDevOfflineMode, AutoSubmitPin, SceneHost auto-walk, RunLayoutDump + the LayoutDump oracle class, EnsureMinimalFallbackState + the per-catalogue offline fallbacks → ClientContext now hard-fails without the VFS); dropped defensive catch-to-Faulted (LoadOrchestrator) + frame-swallow (InboundFrameDispatcher); trimmed over-commenting to one-line // spec: cites; removed dead/stale code (dead _clickPlayer, tombstones, stale tag docs).
- gate (Tier-1 INDEPENDENT verification): nuked --no-incremental build = 0/0 (the LSP mid-edit diagnostics were stale, per the known build/LSP-staleness caveat); grep = ZERO hits for every removed scaffolding token; headless boot clean (Init 0→1, LoginWindow holds at substate 4, FrontEndAudio 861010105 only, no crash); code-reviewer PASS (0 blocker).
- Phase 6 deliverables: authored Docs/RE/scenes/{scene_state_machine,init,login,load,opening}.md — per-scene reconstruction dossiers (ownership/state/sequence Mermaid diagrams + asset manifest + validation checklist + fidelity summary), firewall-clean, plan-reviewer PASS; §7 fidelity summaries updated post-reconstruction.
- decision: campaign CLOSED at states 0-3. DEFERRED to a follow-on: states 4 (Select dev-offline + synthetic roster) and 5 (World DEV_OFFLINE_FLOW + debug baseline + SyntheticWorldFeeder) noise purge; the Phase-5 live-debugger/capture GAPs (carried from the Phase-1+2 entry above); the IDB legibility close (EULA→caption comment + the two stale labels) + names.yaml sync. The packets/3-4_char_manage_result.yaml legacy filename is KEPT deliberately (documented link-stability; the content correctly describes 3/7).
- notes: capture_verified: false. Uncommitted on branch major-campaign.

## 2026-06-19 — Front-end element-level construction recovery (LoginWindow state 1 + OpeningWindow state 3) — re-orchestrator (RE) + spec-author bridge
- binary: doida.exe @ 263bd994 (SHA confirmed against names.yaml: 263bd994c927c20a...; IDB module = doida.exe, 25792 funcs, hexrays + ?ext=dbg endpoint UP)
- tool: IDA Pro 9.3 via MCP (mcp__ida__*, ?ext=dbg) — READONLY static recovery (4 parallel analyst lanes); promotion = firewall bridge (re-promote, no IDA, dirty->clean rewrite)
- scope: VISUAL CONSTRUCTION of the LoginWindow (engine state 1) and OpeningWindow (engine state 3) recovered from the binary at the element / asset / source-rect / draw-order level (NOT from screenshots, NOT from the existing port). Char-select/world out of scope.
- analyzed (by canonical name): Diamond::LoginWindow::BuildScene + InitFields + OnEvent + TickSubStateMachine + Draw; the GU widget builders (BuildImageComponent / BuildButton3State / GULabel / Panel) rect contract; Diamond::LoginSecondPassword BuildKeypad + ScrambleKeypad + SubmitOk + AppendDigit (the PIN keypad); Diamond::CommonLoginWindow::PaintServerList + the server-record decode + MessageDB server-name table + Registry WriteLastServer (the server-list sub-view); COpeningWindow BuildScene + SlideshowFSM + ScenarioCrawlUpdate + OnEvent/SkipGate + PersistSkipFlag (the Opening slideshow + crawl + skip).
- dirty findings (gitignored): Docs/RE/_dirty/scenes/{login_construction, login_pin_construction, login_serverlist_construction, opening_construction}.md
- promoted to clean specs (rewrite-not-copy):
  - specs/frontend_layout_tables.md — added the credential-textbox mask mechanism (mask = length/flags high bit + literal "*" 6px/char, font slot 0; NOT IME-driven) as new s2.7; added s0.10 (every front-end widget is a 1:1 atlas blit), s0.11 (mask is a field-flag), s0.12 (front-end 3-state arg order = NORMAL,PRESSED,HOVER); CORRECTED the PIN digit-face state bands to Normal=560 / Pressed=664 / Hover=612 (was Pressed=612 / Hover=664 — resolved via the documented ui_system s1.5 builder order); confirmed curtain extent (top 0->-222, bottom 326->548, +5/tick, 222px); deepened the server-list element detail (outer panel 270,85 483x490; plate face 100x372 / select 202x372 on loginwindow_02; status quads 500,786; selection strip 700,18; shuffled-per-repaint row order; status==100 = display-only + non-selectable); ADDED the Opening single-texture alpha-over-black crossfade model + concurrent crawl/slideshow + manual scrub actions 1004/1005.
  - scenes/login.md — refreshed verification banner (2026-06-19, build 263bd994); aligned s5.1 (PW mask = field-flag), s5.2 (PIN digit-face srcX=d*52, idle band 560), s5.3 (server-list shuffled order + status==100 non-selectable + 2D hit not ray-pick), and the validation checklist.
  - scenes/opening.md — refreshed verification banner (2026-06-19, build 263bd994); confirmed all numerics (4 panels, ~17500ms dwell, alpha 250 / +1 per tick, crawl 1000ms gate + 30 u/s + 1843 clamp, skip src N/H 761,165 / P 634,165 110x32 action 100, BGM 910061000, OPENNING/SKIP=1 to option.ini); noted the single-texture alpha-over-black crossfade + concurrent layers + manual scrub 1004/1005.
- DIFFs the binary revealed vs the prior committed specs: (1) PIN digit-face Pressed/Hover bands were inverted in frontend_layout_tables s3 (now corrected to Pressed=664/Hover=612); (2) the PW mask mechanism (field-flag high bit + "*"/6px, font slot 0) was not previously spelled out (the spec only said "masked"); (3) the credential textboxes are built in the main construct routine, not the secondary-init nav anchor. All other element/asset/src-rect values RE-CONFIRMED matching the CYCLE-18 spec with no change.
- firewall: clean-room audit PASS over the three touched committed specs — no addresses, no sub_/loc_/off_/__thiscall/_DWORD/pseudo-C/mangled names. Remaining scanner hits are documented false positives: the four UI status ARGB color constants (0xFFFF0000 / 0xFFED6806 / 0xFFFFFF00 / 0xFFB5FF7A — interoperability color facts, pre-existing house style) and the Win32 REG_DWORD registry-type name.
- notes: capture_verified: false. names.yaml NOT touched (deferred — sync owed for the BuildScene/InitFields/BuildKeypad/ScrambleKeypad/PaintServerList routines + the GU builder arg names by canonical name). One residual UNVERIFIED carried: the Opening final-fade armed-flag producer site (consumer side fully confirmed; non-blocking). Uncommitted on branch major-campaign.

## 2026-06-19 — Front-end Login/Opening 1:1 Godot reconstruction from the IDA manifest + 2 gaps closed — Tier-1 + port-orchestrator (port)
- Built the Godot Login (state 1) + Opening (state 3) to the committed IDA construction manifest (`frontend_layout_tables.md` §0.10/§0.12/§2.1/§2.3/§2.7/§3/§4/§6). NO screenshots — fidelity = code-vs-manifest conformance, verified element-by-element by Tier-1.
- Every front-end widget is now a 1:1 atlas blit from the real VFS at the binary's src-rects (no Godot StyleBox/ColorRect/Theme chrome). New `Ui/Widgets/MaskedTextField.cs` replaces the Godot `LineEdit` credential boxes: field-background blit + caret; the PW field draws the literal `*` glyph at 6 px/char in font slot 0 (the mask is a field flag, not the IME mode — §0.11/§2.7). PIN digit-face state bands corrected to Normal=560 / Pressed=664 / Hover=612 (§3). Server-list status-color indicator quads ×3 added.
- 2 gaps recovered from IDA (static) → promoted to spec → wired: (a) §4 — the status==100 special-row quad anchoring: quad 0 at `(anchorX−30, anchorY−13)`, quads 1&2 at `(anchorX+139, anchorY+13)` (overlapping duplicate), gated by a one-byte show flag; (b) §6 — the Opening crawl manual scrub is KEYBOARD-bound: Page Up (DIK_PRIOR) = action 1004 (rewind, floor 0), Page Down (DIK_NEXT) = action 1005 (forward, ceil 1843), fixed bindings via the DIK→app-code table. Dirty source: `_dirty/scenes/frontend_gaps_followup.md`.
- Cleanup: removed the dead `TextboxRenderH` (LineEdit-era leftover) + the write-only `PinSubView.HostInReferenceSpace` (and its 2 call sites) + stale comments.
- gate (Tier-1 independent): nuked `--no-incremental` build 0/0 (the LSP mid-edit cascades were stale every time); headless boot clean (Init 0→1, LoginWindow holds at substate 4, audio 861010105 only). No xUnit suite on this branch.
- COMMITTED to `major-campaign` (targeted paths: 01/04/05 + Docs/RE). DEFERRED: states 4/5 (Select/World) dev/offline noise; Phase-5 live-debugger/capture GAPs + live login; names.yaml sync + IDB legibility annotations; minor advisory (`ConfirmButton`/`QuitButton` var-names stale vs actions 102/105, data correct).

## 2026-06-19 — Login sub-window recovery: validation-error countdown modal + PIN chrome + server-list (3 lanes) — re-orchestrator (RE) + orchestrator-driven promotion
- binary: doida.exe @ 263bd994 (SHA confirmed against names.yaml: 263bd994c927c20a...; IDB module = doida.exe, 25792 funcs, hexrays + ?ext=dbg endpoint UP, ground-truth gate GREEN).
- tool: IDA Pro 9.3 via MCP (mcp__ida__*, ?ext=dbg) — READONLY static recovery, 3 parallel analyst lanes; promotion = firewall bridge (re-promote discipline, dirty->clean rewrite-not-copy; spec-author subagent absent from registry, so the dirty->clean crossing was authored in the orchestrator role with full self-scrub).
- scope: the THREE Login sub-windows the Godot port renders wrong/missing — (1) the validation-error message box with the live "확인 - N" countdown OK button; (2) the PIN second-password modal chrome; (3) the server-select view. Recovered from the binary at the element / asset / src-rect / draw-order + behavior level (not from screenshots).
- analyzed (by canonical name): Diamond::LoginWindow::BuildScene + TickSubStateMachine + OnEvent (error-msgbox raise + countdown tick); the dedicated ErrorPanel message-box object + its countdown OK button; MessageDB string fetch for the per-failure msg ids; Diamond::LoginSecondPassword keypad constructor + SetVisible + SubmitOk (PIN chrome); Diamond::CommonLoginWindow::PaintServerList + the server-name MessageDB table + the server-record decode + the connecting-popup raise.
- dirty findings (gitignored): Docs/RE/_dirty/scenes/{login_error_modal, login_pin_complete, login_serverlist_complete}.md (extend the prior login_*_construction.md + frontend_gaps_followup.md).
- promoted to clean specs (rewrite-not-copy):
  - specs/frontend_layout_tables.md — NEW §2.1a (the validation-error message box: dedicated ErrorPanel, atlas A3 InventWindow.dds, dst 342,289 340x190 src 318,647; centered message label action 670; OK/countdown button local 125,151 90x25 src N417,943/H507,943 action 671; per-failure msg map 4025 ID empty-or-<4 / 4026 PW empty / 4027 no servers / 4028 fetch-1; THE COUNTDOWN: start N=3 from a 3000ms budget, decrement <=1/1000ms off a ms wall-clock delta, caption "<확인 msg101> - <N>", auto-close at 0 returning to idle substate 6, early dismiss via OK 671); §2.1 note clarifying Confirm-A IS the "서버에 접속중입니다" connecting popup (msg 4023, raised substate 40); §3 PIN corrected (container is textureless tex=0; the ornate frame + title "2차 비밀번호 입력" + red warning + "번호입력" caption are BAKED password.dds art, NOT widgets/msg.xdb — zero MessageDB calls, no red ARGB constant; the InventWindow.dds 340x190 panel is a reused HIDDEN quit-confirm ExitPanel msg 2007, not the visible PIN frame); §4 server-list extended (DEFINITIVE server name = msg.xdb TEXT id 5000+server_id fallback 5901 on a FIXED A4 scroll-plate, NOT a per-server calligraphy image; two-layer parchment backdrop full-screen A2 0,110 1024x490 src 0,0 + the list-box scroll; title "서버선택" baked A2 207,44 70x17 src 0,980; EVENT badge baked A1 407,-3 210x70 src 743,398; 새로고침 refresh = action 105 A1 456,-3 111x38 N792,398/H602,416 10s-debounced; the 115+i "tabs" are a hidden page-jump strip, the visible 하왕관 rows are the 2 server plates; connecting popup = Confirm-A A3 msg 4023 substate 40).
  - scenes/login.md — §5.2 (PIN chrome = baked password.dds art; container textureless; digit-face bands Normal 560 / Pressed 664 / Hover 612 confirmed; the hidden reused ExitPanel clarified); §5.3 (server name = msg.xdb text on a fixed plate; parchment two-layer / title / EVENT / refresh / connecting-popup facts); §10 (the validation-error countdown modal moved from open-item to RESOLVED static, with the geometry/actions/msg-map/countdown).
- DIFFs the binary revealed vs the prior committed specs: (1) the validation error uses its OWN ErrorPanel with a live 3->0 countdown OK button + auto-close — NEW, not previously documented; (2) the PIN visible frame/title/warning/번호입력 are baked password.dds pixels, NOT widgets or msg.xdb text (corrects the §3 "InventWindow.dds backdrop frame" reading — that panel is a hidden reused ExitPanel); (3) the server-list name is msg.xdb TEXT on a fixed scroll-plate, definitively NOT a per-server calligraphy image (settles the open question; confirms the existing 5000+id/5901 model); (4) the title/EVENT badge are baked atlas images and the backdrop is two layers (added detail).
- firewall: clean-room audit PASS over both touched committed specs — the newly authored §2.1a + §4 blocks scanned CLEAN (no addresses, no sub_/loc_/off_/__thiscall/_DWORD/pseudo-C/mangled names). Remaining scanner hits are documented pre-existing false positives untouched by this campaign: the four UI status ARGB color constants (interoperability color facts) + Win32 REG_DWORD name in frontend_layout_tables.md, and the pre-existing struct-field offset notes (+0x238/+0xBC/+0x554) in scenes/login.md (house-style field references, not navigation addresses; not introduced here).
- notes: capture_verified: false. names.yaml NOT touched (sync owed by canonical name for the ErrorPanel message-box + its countdown OK button + the connecting-popup field; the analysts also flagged proposed names InputCtx_IsActionDown/ActionBitset_Test/_Set + g_DikToAppCodeTable from the prior gaps pass). UNVERIFIED carried: the literal CP949 strings of msg ids 101/4025/4026/4027/4028 (runtime msg.xdb, debugger-confirmable). Uncommitted on branch major-campaign.

## 2026-06-19 — Login sub-windows: PIN-chrome contradiction resolved + Godot fixes (error modal, PIN, server-list) — Tier-1 + re-function-analyst + port-orchestrator
- **PIN-chrome contradiction RESOLVED against the binary + the maintainer oracle.** Two prior passes traced the bare keypad (`Diamond::LoginSecondPassword`) + the textureless container and concluded "the login PIN draws no chrome" — which CONTRADICTED the maintainer's capture (a full ornate frame + title "2차 비밀번호 입력" + red warning + "번호입력" + input field). A 3rd targeted pass (the login PIN-open path, sub-state 29→31→32) found what they missed: the keypad-build routine writes the `password.dds` handle into the PANEL's own backdrop-texture field, and the GUPanel draw path blits that backdrop (source (0,0), full panel size) BEFORE the children. So the chrome = `password.dds` source (0,0,329,422) → dst (347,173) 329×422 — the frame + carved top + title + red warning + 번호입력 caption + input-field box are all baked into that texture region (which is exactly why the keypad routine makes zero MessageDB calls + sets no color). `frontend_layout_tables.md` §3 corrected from "no chrome / InventWindow.dds frame" to the real password.dds backdrop-blit. (Doctrine note: the visual oracle correctly overrode two mis-scoped IDA reads; the binary, read at the RIGHT scope (the PIN-open path, not the embedded keypad), confirms it.)
- **Godot fixes (layer 05; built from the promoted specs; no screenshots; 1:1 atlas blits):**
  - W1 ErrorPanel (`Ui/Scenes/Login/LoginWindow.cs`): built the dedicated validation-error message box (A3 dst 342,289 340×190 src 318,647) with the live "확인 - N" countdown OK button (N=3, 1 Hz, auto-close at 0, early dismiss action 671), wired into `RunValidation` (msg 4025/4026) + the server-list fetch result (4027/4028); + the "connecting" popup (Confirm-A, msg 4023) at sub-state 40.
  - W2 PIN (`Ui/Scenes/Login/PinSubView.cs`): replaced the wrong `InventWindow.dds` 340×190 blit with the `password.dds` (0,0,329,422) backdrop as the first child behind the keypad; the keypad / masked-echo / Reset / OK / Cancel were already correct (§3).
  - W3 server-list (`Ui/Scenes/Login/ServerSelectSubView.cs`): added the missing full-screen A2 backdrop layer; fixed the EVENT badge to A1 src(743,398) dst(407,−3) 210×70; hid the 10-button page-jump pager (blank UV); kept the baked title plate + the 5000+id msg-text server names.
- gate (Tier-1 independent): nuked `--no-incremental` build 0/0; headless boot clean, login spine 1→2→3→4→5→6 (the prior-turn curtain-settle + credential-form rest-visibility fix holds); no SCRIPT ERROR. (LSP "Godot/Control not found" cascades after every bin/obj nuke are stale — ignore; the nuked build is the authority.)
- COMMITTED to `major-campaign`. names.yaml sync still owed (ErrorPanel + countdown OK button + connecting-popup field + the PIN-window backdrop path + the prior gaps-pass names). UNVERIFIED: the CP949 strings of msg 101/4025/4026/4027/4028 (runtime msg.xdb).

## 2026-06-19 — Login/Opening deepest-fidelity polish (ID/PW size + exhaustive widget cross-check) — Tier-1 + port-orchestrator
- ID/PW credential text enlarged per the maintainer (visual oracle): `Ui/Widgets/MaskedTextField.cs` drawn font 12→15 px, the PW `*` mask advance 6→7.5 px (proportional), baseline centered in the 13-px field, `ClipContents` off so the larger glyph is not clipped. (Spec slot 0 = DotumChe 12; this is a deliberate readability oracle-override, NOT a spec change.)
- Login widget exhaustive cross-check vs §2.1/§2.3/§4 → 4 fixes: (1) the curtain submit-plate snap to (494,469) at offset>200 was MISSING — added (TickCurtain + SnapCurtainOpen, reset at state 1); (2) server-list strip/deco visibility band 35..37 → ≥35 ("state 35 onward", §2.2); (3) Confirm-A/B body-label position corrected to spec (10,100,330,20); (4) LoginLayout gained spec-cited ConfirmLabel constants.
- Opening cross-check vs §6 → 3 fixes: (1) slideshow alpha init = 250 / dir −1 (fade-out first; was 0/+1); (2) full-screen opaque black ColorRect added below the slideshow (the "alpha-over-black-cleared-back-buffer" crossfade model); (3) skip (Enter/ESC/Space) + Page Up/Down scrub matched by Keycode/PhysicalKeycode (DIK-anchored, §6), not the layout label.
- Preserved W1/W2/W3 + curtain rest-visibility (no regression). Gate (Tier-1 independent): nuked `--no-incremental` build 0/0; headless login spine 1→6 + the curtain submit-plate snap fired; code-reviewer + render-reviewer PASS (render-reviewer's one note = the intended bigger-glyph oracle override). Invisible residual: the always-hidden notice-panel title plate has a local-vs-absolute coord mismatch (never renders; left as-is). COMMITTED to `major-campaign`.

## 2026-06-19 — Server-list fidelity: connecting modal + plate labels + calligraphy z-order — Tier-1 + re-function-analyst ×2 + re-asset-format-analyst + godot-ui-engineer
- binary: doida.exe @ 263bd994 (live IDA MCP, static); maintainer visual oracle = official server-list capture (the "서버에 접속중입니다…" connecting modal + green "사용가능" + on-scroll calligraphy).
- analyzed (by canonical name / role): LoginWindow OnEvent + tick sub-state machine (server-join hand-off 37→41), the connecting popup (Confirm-A, msg 4023), the inbound dispatcher (0/0→1/4→3/1 success; 4/500 server popup), the countdown message-box helper (msg 101 "%s - %d"); the server-list painter + its per-plate components (name/face/button/status/count) and font slots; the per-plate widget insertion/paint order. Dirty notes: `_dirty/scenes/serverlist_connecting.md`, `_dirty/scenes/serverlist_painter.md`, `_dirty/scenes/serverlist_plate_zorder.md`.
- specs produced/updated: `Docs/RE/specs/frontend_layout_tables.md` §4 (server-list display model) + §2.2 (sub-state ladder 39/41).
- CORRECTIONS confirmed against the binary (the prior spec was wrong):
  - **Connecting popup ≠ credential feedback.** Confirm-A (msg 4023, atlas A3, dst 342,289 340×190, src 318,647) is raised at **sub-state 39** (not 40); its single 3-state button (caption baked in the .dds) is **action 113 = Cancel → abort to sub-state 34** (re-fetch the list). SUCCESS = inbound **3/1** char-list tears the scene down → char-select (state 4); the popup is never explicitly closed. FAILURE feedback = the SEPARATE §2.1a auto-counting-down box: channel-fetch result 0 → msg **4027**, result −1 → msg **4028** (→ back to list); stale list → msg **4025/4026** (→ form); game.ver mismatch → native MessageBox msg **2204**; post-handshake rejection over the wire (code-15 disconnect / 30 s timeout / server popup opcode **4/500**).
  - **Plate label contents were mis-mapped.** Name = font **slot 0** (DotumChe 12 px), **center-aligned**, horizontal+ellipsis @ (…,390,174×21) — NOT large/vertical, NOT left-aligned. Status caption = font **slot 4** @ (…,410,174×20), colored: status==0+load-valid → >1200 msg 6001 red / >800 6002 orange / >500 6003 yellow / **≤500 → status caption msg(4029+status_code) GREEN 0xFFB5FF7A = the "사용가능" available case**; status==3 → 6004 (load==24) / 6005 HH:MM; else caption msg(4029+code). The **+430 count label is set EMPTY**; the "%4d / %4d" population string is a **dead-debug stub** (never drawn) — this is why the port showed a tiny name and no status. msg 4029-4032 are STATUS captions (not column headers). Name source = resolver over msg banks **5301-5440** (fallback 5901), NOT "5000+server_id" (exact bank math = GAP).
  - **Calligraphy z-order.** The big on-scroll brush calligraphy is NOT engine name text (name is small slot-0) — it is a **baked per-column face quad** (atlas A4 `loginwindow_02.dds`, src(448+124·i,6) 100×372, i=0/1, identical per server). Per-plate insertion = paint order = button → name → **face** → status → count, so the face draws **ON TOP** of the parchment (all widgets full-alpha, no blend). The port drew the face BEHIND the opaque parchment → hidden → "empty scroll" bug.
- Godot port (layer 05, clean-room from the promoted spec): `Ui/Scenes/Login/LoginWindow.cs` (connecting popup now shows Confirm-A=msg4023 `_quitModal` at state 39, hand-off 38→39 no longer skips to 41, Cancel 113 → ConnectCancelled + RestartServerFetch → 34, new `NotifyConnectSuccess/Failed`), `Ui/Scenes/Login/ServerSelectSubView.cs` (status caption slot-4 green-available @410, count empty @430, name center @390; calligraphy face drawn ON TOP of the parchment), `Scene/Controllers/LoginScene.cs` (connect result drives success→advance / failure→countdown box, offline-safe, CancellationToken).
- Gate (Tier-1 independent): nuked bin/obj → `dotnet build MartialHeroes.slnx` **0/0** (Godot csproj included); headless login spine **1→6** clean (no script error/exception). Firewall: all addresses confined to `_dirty/`; no pseudo-C in committed files.
- GAPs (open, debugger/oracle-pending, NON-blocking): CP949 text behind msg 4023/4029-4032/101 (bp MessageDB_GetString); the .dds pixel content of the face crop src(448,6)/(572,6) (does it hold the brush calligraphy — visual oracle); the name-resolver exact id→bank math; the 4025/4026 validation-vs-server-list msg attribution. names.yaml sync owed. UNCOMMITTED (awaiting maintainer visual confirmation — the maintainer is the pixel oracle).

## 2026-06-19 — assembly-graph-cycle (CYCLE 1, Tier-1 promotion close)
- binary: doida.exe @ 263bd994
- tool: IDA Pro 9.3 via MCP (static only; no debugger) — recovery by 3 axis re-orchestrator waves; promotion by per-file authors; IDB legibility by ida-toolsmith
- analyzed (by canonical role): per-cell terrain loader + 9-slot sub-manager + 34-pool/25-ring streaming; two-phase area bootstrap + .map DATAFILE fan-out; bgtexture kind-byte dispatch; server-snapshot actor spawn path + 880B descriptor; fx1-7 cell-slot attach + .ted idx-1 finalize site; inverse-bind quaternion bake + base-relative bone-ID weight indexing; eager .bnd pose-pool preload + verbatim id_b key; mob to appearance to anim-catalogue to skeleton chain; actormotion action-clip vs SFX routing; equipment GID-digit composition + weapon-glow tier toggler; events.scr lookup-by-id; items.scr +0x80/+0x84 asset keys; citems 10-paragraph block; vehicle/creature_item.xdb runtime linkage; trade/exchange-busy BGM override; char-select close-button atlas.
- specs produced/updated:
  - Docs/RE/specs/assembly_graph.md (NEW — master cross-format wiring synthesis: World-boot chain + Actor-bake chain + format-to-format edge table + OPEN-RISK ledger + port-side notes)
  - Docs/RE/structs/terrain-manager.md (9 cell slots named; 34-pool owns / 25-ring borrowed view; live centre = ring slot 12)
  - Docs/RE/formats/area_inventory.md (added area-to-cell fan-out linkage vocabulary the census lacked)
  - Docs/RE/specs/terrain-streaming.md (two-phase bootstrap, §7)
  - Docs/RE/formats/bgtexture_lst.md (kind-byte dispatch; scrubbed 4 pre-existing leaked addresses)
  - Docs/RE/formats/npc_spawns.md (.arr is NOT the live-actor source — server packet 4/4 is)
  - Docs/RE/formats/terrain.md (fx1-7 slot attach during .map parse; .ted idx-1 decrement RESOLVED)
  - Docs/RE/specs/skinning.md (inverse-bind bake PINNED; base-relative bone-ID weight indexing = explosion fix)
  - Docs/RE/formats/bindlist.md (eager boot preload; verbatim id_b pool key)
  - Docs/RE/formats/actormotion.md + Docs/RE/formats/animation.md (motion_ids_b = SFX/FX, NOT secondary motion; rate_x/y = move speed; float_h/i = dust FX)
  - Docs/RE/specs/equipment_visuals.md (GID-digit to column CODE-CONFIRMED; weapon-glow tier toggler 1..9)
  - Docs/RE/formats/config_tables.md (mobs.scr +52 appearance / +104 weapon flag)
  - Docs/RE/formats/events_scr.md (lookup-by-id; item/shop/exchange UI only)
  - Docs/RE/formats/items_scr.md (+0x80 to g%d.skn, +0x84 to bind pool; citems 6 to 10)
  - Docs/RE/formats/scr.md (citems 6-vs-10 conflict RESOLVED to 10)
  - Docs/RE/formats/xdb_tables.md (creature_item.xdb = held-item VISUAL, not loot; vehicle.xdb by vehicle_id)
  - Docs/RE/specs/sound.md + Docs/RE/specs/world_systems.md (the "indoor BGM" override is a trade/exchange-busy flag, not a map/region attribute)
  - Docs/RE/formats/ui_manifests.md (.do stride reconciled to 116; char-select close button = tradekeepwindow.dds 941,910,23,23)
- notes: CYCLE 1 "Runtime Inter-Format Assembly Graph". Recovered the runtime inter-format assembly graph (World + Actor + other-format links), static-only, across 3 parallel axis waves; promoted to the committed specs above (one author per file) and synthesised the master assembly_graph.md. SIX deliberate binary-won spec reversals: (1) live actors are server-driven (packet 4/4 entity snapshot to 880B descriptor to spawn routine), NOT spawned from on-disk npc.arr (the .arr supplies position/facing/static metadata only); (2) motion_ids_b routes SFX/FX events, NOT secondary motion (every runtime consumer feeds sound/effect routers; the old 74.5% sample stat was coincidental id-namespace overlap); (3) creature_item.xdb is a creature held-item VISUAL attachment keyed by creature_key, NOT a loot/drop table; (4) the sound "indoor BGM override" is a per-local-player trade/exchange-busy flag forcing BGM 863500002, NOT a map/region "indoor" attribute; (5) citems.scr carries 10 description paragraphs (capacity; sentinel-terminated; accessor bounds index<10; 10x81=810 fits the 1052 record — the prior "6, overflow" was a hex slip, +0xE4+810 = 0x40E < 0x41C); (6) the inverse-bind bake is statically PINNED (quaternion conjugate, subtract-then-rotate, parent-on-left, rest-pose world) with the skin-weight explosion root-caused to base-relative bone-ID indexing (bone_array[id - base_id]) — retires the skinning-explosion render debt; the avatar can be animated. A1-8 (.ted idx-1 decrement) statically RESOLVED to a real finalize site (-1 on the .ted byte only). Firewall gate PASS (Tier-1 scan: no addresses/autonames/pseudo-C in any committed spec). Static IDA only; no debugger. names.yaml canonical-name delta staged to _dirty/ for maintainer hand-merge; IDB legibility annotation applied by ida-toolsmith. OPEN RISKs (debugger-pending, none blocking the port): spawn-vs-cell timing (Godot fallback-Y), inverse-bind handedness label, unused motion slots, per-tier glow set, item-side event_id/bnd join columns, trade-mode track label.

## 2026-06-20 — CYCLE 3: Netcode total cartography, contracts & IDB legibility — Tier-1 + re-orchestrator waves + ida-toolsmith
- binary: doida.exe @ 263bd994 (SHA confirmed full sha256 263bd994c927c20a...; IDB module doida.exe, 25792 funcs, MCP ?ext=dbg UP). **STATIC ONLY — no debugger.**
- tool: IDA Pro 9.3 via MCP (mcp__ida__*) READONLY recovery across ~27 parallel lanes (re-orchestrator Tier-2 fan-out: re-protocol/function/struct/crypto analysts); promotion = firewall bridge (re-promote, rewrite-not-copy; general-purpose workers as spec-author absent from registry); IDB legibility by ida-toolsmith.
- scope: the ENTIRE networking subsystem — the inbound dispatcher (5 majors 1-5 + the major-0 handshake), the two 154-slot install tables, every S2C handler, every C2S builder, the net object model, and the Client↔Server contracts.
- analyzed (by canonical role): Net_SendPacket send-convergence (105 call sites / 104 builders); the major-1/2/3 C2S builder family; the major-4 Response + major-5 Push install tables (response base +0x1378 / push base +0x15E0); the inline major-1/3 handlers; the connection-state machine (codes 201/202/203/232, timed tag 10001) + network-event entry (type-15 / sub-code 100 vs 102); the receive worker + the connection I/O thread (3rd thread — resolves the "second worker" open item); the keepalive/ack family (2/10000, 2/112, 5/146→2/146); NetHandler/NetClient/SecureContext layouts; the big S2C payload interiors (4/1, 4/4, 4/65, 4/100, 4/56, 4/71, 4/102, 5/52, 5/68, 5/73, 5/77, PvP cluster); result/code maps; CP949 text-field inventory; actor-key dword order; request↔response correlation.
- dirty findings (gitignored): Docs/RE/_dirty/netcode/*.md (40 files — census, install_tables_full, dispatch_reconfirm, conn_state_machine, send_framing_keepalive, the c2s_* domain files, c2s_unknown_sweep + band1..5, the s2c_* interior files, net_objects_layout, crypto_recheck, result_code_maps, cp949_text_fields, actorkey_conflicts, req_resp_pairs, names_staged_wave1..3) + applied/cycle2_phase_T.md.
- promoted to clean specs (rewrite-not-copy):
  - Docs/RE/specs/net_contracts.md (NEW — master Req↔Resp by domain; 15 domains; 58 pairs; result-code + CP949 appendices)
  - Docs/RE/opcodes.md (C2S rows 59→105: +46 newly-catalogued senders; 4/56/4/71 reclassified out of thin-slot; corrections — 2/10000 4-byte body, 2/23·2/100·2/143 fixed-not-var, 2/44 byte-shape, 2/145 count-prefixed, 2/146 reactive ack)
  - Docs/RE/packets/*.yaml (57 created + 11 extended; total 146→205)
  - Docs/RE/specs/handlers.md (Part III §19-§24: decomposed interiors + the 5/52 verdict + 4/56/4/71 reclassify + 3/100 code clusters + actor-key idiom + phantom-refute)
  - Docs/RE/specs/network_dispatch.md (§8 second-worker RESOLVED = 3-thread model; 2/10000 4-byte body; 5/146→2/146 link-health ack; exhaustive install-slot maps; {202/203/232}→GameState cross-link)
  - Docs/RE/structs/net_handler.md, net_client.md, secure_context.md, net_packet_bodies.md (NEW — object layouts + DTO offset tables)
- DIFFs the binary revealed / corrections vs prior specs: (1) 5/52 target record = ONE 64-bit damage sum @rec+0x14/+0x18, the record count is the header byte @payload+0x14, and there is NO ActionCode @rec+0x10 (Reading 2 REFUTED); (2) 4/56 (1552B) + 4/71 (1092B) are STRUCTURED panel snapshots, NOT thin-slots; (3) the "5000/10000/10001 select-screen string-id class" is a PHANTOM — 5000 = display-duration ms, 10000 = keepalive minor + 10s timer, 10001 = timed-event tag — refuted, NOT promoted; (4) 5/73 body proves SmsgQuestComplete (the committed SmsgGuildWarInfoUpdate label is wrong — names lane); (5) the "second network worker" = a 3rd socket-I/O thread (WSARecv/WSASend); (6) 2/146 is the reactive ack to inbound 5/146; (7) the install-table validator refuted several naive reply mirrors (no 4/44/4/46 item-move ack, no 4/30 guild-op ack, no 4/143 quest-keep ack).
- IDB legibility (ida-toolsmith, applied + saved): 92 renames (machinery + Smsg*/Cmsg* handlers + builders) + 6 struct types + 7 signatures + 20 neutral comments; SHA-pinned 263bd994; idempotent. 4 name conflicts surfaced for names.yaml (NetClient_PostNetworkEvent; SmsgStallListRefill @ Response slot 74; SmsgGuildDiplomacyResult; a minor _Push suffix).
- firewall: clean-room audit PASS — leak scan 23→0 across the cycle's spec surface; 20 packet YAMLs scrubbed of PRE-EXISTING prior-campaign leaks (sub_/dword_/byte_/raw addresses) with zero factual change; final sweep over packets/, opcodes.md, handlers.md, network_dispatch.md, structs/*.md returns zero matches.
- completeness: ZERO GAP — all 99 installed Response slots + 65 Push slots + 104 C2S builders catalogued or explicitly documented (no-op/discard/reactive/timer-built).
- notes: capture_verified: false (static-only; all wire VALUE semantics capture/debugger-pending — never fabricated). names.yaml sync OWED (92 names + 4 conflicts staged in _dirty/netcode/applied/cycle2_phase_T.md). UNVERIFIED carried: 1/7 delete-mode meaning; 2/151 reply on 1/20 (atypical major-1 answering major-2); 4/48 8×28B+24 overruns the 236B read by 12B; the 2/153 product_confirm-vs-pet_summon opcode-id COLLISION (Tier-1 arbitration). Journalled as CYCLE 3 (the working label was "CYCLE 2", but that ROADMAP slot is the visual-world cycle). Uncommitted on branch major-campaign.
