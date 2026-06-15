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
