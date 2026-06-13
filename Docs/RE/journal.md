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
