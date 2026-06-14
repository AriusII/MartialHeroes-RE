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
