# Structs: ParticleEffect / GParticleBuffer / GParticle_State / GParticle_Render (GPU particle emitter runtime object graph)

> Clean-room struct layout specification. Derived-truth from static analysis of the client binary.
>
> **Verification:** fully laid out (static-confirmed, CYCLE 14 deep-struct pass). All four heap objects in
> the GPU particle emitter graph have every field mapped and role confirmed. FVF/stride constants
> CODE-CONFIRMED from constructor literals; dual-path flag source (`sprite_size_x > D3DCAPS9.MaxPointSize`)
> CODE-CONFIRMED; additive-flag source (`.eff` entry `blend_additive_flag` at entry+0x10 →
> `ParticleEffect+0x30`) CODE-CONFIRMED. Two field corrections applied to `Docs/RE/formats/effects.md` §E
> (see §8).
> ida_reverified: 2026-06-28 · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> subsystems: [particle_effects, d3d9_rendering, effects_runtime]
> C# implementation: `MartialHeroes.Client.Application` (effect tick and draw); `MartialHeroes.Assets.Parsers` (`.eff` entry loading)
> Cross-refs: `Docs/RE/formats/effects.md` §E (on-disk `particleEmitter.eff`) · `Docs/RE/specs/effects.md` (runtime lifecycle)
> **Deepening pass (2026-06-29):** deep-3D cartography static pass (IDB anchor f61f66a9) confirmed all four struct sizes exact (ParticleEffect 60 B, GParticleBuffer 248 B, GParticle_State 32 B, GParticle_Render 20 B — every byte accounted); extended §3 with the full two-variant FVF/stride/usage/draw-call table (§3.2) and per-vertex quad layouts (§3.3); closed §9 open item 3 (graphics-quality alpha-fade lower bound = 0.05f, formula and special case confirmed statically); tightened §9 items 1 and 2 with static evidence bounds. No values changed from the prior CYCLE 14 deep-struct pass — only new detail added.

---

## 1. Object Graph Overview

Four heap-allocated objects form the runtime emitter graph, created per-emitter whenever the effect system activates a `particleEmitter.eff` entry (either from a `.xeff` element with `resource_id ≥ 10000` or by direct name lookup):

| Object | Size | Polymorphic? | Created by |
|---|---|---|---|
| `ParticleEffect` | 60 bytes (0x3C) | No — no vtable; plain struct | The `ParticleEffect` constructor, called by `ParticleEffectManager_CreateEffect_atPos` / `_v0` / `_byName` |
| `GParticleBuffer` | 248 bytes (0xF8) | Yes — one virtual slot (destructor) | Allocated and constructed inside the `ParticleEffect` constructor |
| `GParticle_State` | 32 bytes (0x20) | No | Array built by the state-record array element constructor |
| `GParticle_Render` | 20 bytes (0x14) | No | Array built by the render-record array element constructor |

**Ownership tree (one graph per active emitter):**

```
ParticleEffectManager (singleton)
 └─ emitter-file map (keyed by entry_id)          ← built by EFF_LoadParticleEmitter
      each entry: 28-byte in-memory descriptor (§6)
 └─ active ParticleEffect list (intrusive; walked by ParticleEffectList_DrawAll)

ParticleEffect (60 B)                              ← the live emitter
 ├─ +0x04 → GParticle_Render array  (20 B × capacity)
 ├─ +0x0C → GParticleBuffer         (248 B)
 │            ├─ +0x18 → shared GPU vertex-buffer wrapper (process-global; grown to max capacity)
 │            └─ shared GPU index-buffer wrapper (process-global; quad path only)
 ├─ +0x10 → GParticle_State array   (32 B × capacity)
 ├─ +0x18 → spawn sub-record array  (52 B × capacity; from the .eff entry)
 └─ +0x20 → origin pointer → +0x24 world Vec3 (self-owned by default; may point to external Vec3)
```

**Construction.** `ParticleEffectManager_CreateEffect_atPos` (and the `_v0` and `_byName` variants) look up the emitter-file entry, assert that `texture`, `particle_count`, and `particle_info` are non-null, allocate and construct a `ParticleEffect`, link it into the manager's active list, and stamp the world origin. The `ParticleEffect` constructor allocates both per-particle arrays (count = `capacity` = `entry.particle_count`) and the `GParticleBuffer`, then wires all field pointers.

**Bridge from `.xeff`.** `XEffect_FirstTickInit_BuildParticles` is the bridge from a `.xeff` element with `resource_id ≥ 10000`: it calls `ParticleEffectManager_CreateEffect_atPos` and stores the resulting `ParticleEffect` pointer on the `UserXEffect` element node.

**Per-frame cycle.** `GParticle_Integrate` runs per-particle simulation each tick (position, size, and colour-rate integration; lifetime and spawn-delay countdown). `ParticleEffectList_DrawAll` walks the active list, sets the per-effect blend state from `ParticleEffect+0x30`, then calls `ParticleEffect_drawAlive` — which locks the shared vertex buffer, fills it with live particle data, unlocks, and issues the draw call.

**Shared vertex and index buffers.** The vertex and index buffers are process-global (one per render path per buffer variant), lazily created and grown when an emitter's capacity exceeds the current global size. `GParticleBuffer+0x18` points to the global vertex-buffer wrapper slot, not a private allocation. The index buffer (quad path only) is likewise global.

---

## 2. `ParticleEffect` — 60 bytes (0x3C)

No vtable. Plain struct; all methods invoked through direct calls (`ParticleEffect_drawAlive`, `ParticleEffect_setOrigin`).

| Offset | Size (bytes) | Type | Role |
|:---:|:---:|:---|:---|
| `+0x00` | 4 | `ptr` | **texture handle** — resolved D3D texture for this emitter (from `.eff` entry `texture` at entry+0x14). Forwarded to `GParticleBuffer+0x14`; bound at draw via `SetTexture`. |
| `+0x04` | 4 | `ptr` | **render-particle array base** (`GParticle_Render` records, count = capacity). Doubles as the point-sprite vertex source (see §5). |
| `+0x08` | 4 | — | Never written by the constructor. No reader found in any traced consumer path (`ParticleEffect_drawAlive`, `GParticle_Integrate`, `ParticleEffect_setOrigin`, `ParticleEffectList_DrawAll`, `CreateEffect_atPos`/`_v0`/`_byName`). Strong static evidence this field is dead/vestigial. **[debugger-confirm: no other subsystem reads this offset at runtime]** |
| `+0x0C` | 4 | `ptr` | **`GParticleBuffer`** — the GPU submit object (§3). |
| `+0x10` | 4 | `ptr` | **particle-state array base** (`GParticle_State` records, count = capacity). |
| `+0x14` | 4 | `u32` | Set to 0 by the constructor; no subsequent writer or reader found in any traced path. Strong static evidence this field is dead/vestigial. **[debugger-confirm: no other subsystem reads this offset at runtime]** |
| `+0x18` | 4 | `ptr` | **spawn sub-record array** — the 52-byte per-particle sub-records from the `.eff` entry. One sub-record per particle index; each `GParticle_State+0x00` points into this array. |
| `+0x1C` | 4 | `u32` | **capacity** — particle count (= `.eff` entry `particle_count`). Drives both per-particle array sizes and the draw-loop bound in `ParticleEffect_drawAlive`. |
| `+0x20` | 4 | `ptr` | **origin pointer** → the emitter world Vec3. Defaults to `&self+0x24` (self-owned); may be an external Vec3 supplied at create-time. The respawn path dereferences this and adds it to each spawned particle's base position. |
| `+0x24` | 12 | `f32[3]` | **origin Vec3** (x, y, z) — the emitter world position. Written by `ParticleEffect_setOrigin` on each placement. Live only when the origin pointer is self-owned. |
| `+0x30` | 4 | `u32` | **additive blend flag** — 0 = alpha blend (SRC_ALPHA / INV_SRC_ALPHA); non-zero = additive blend (SRC_ALPHA / ONE). Read by `ParticleEffectList_DrawAll`. Source: `.eff` entry `blend_additive_flag` at entry+0x10 (see §8, R1). |
| `+0x34` | 1 | `u8` | **active gate** — `ParticleEffect_drawAlive` no-ops unless set. Cleared by the constructor; set when the emitter goes live. |
| `+0x35` | 1 | `u8` | **stop / dead flag** — when set, the simulation stops recycling particles (an expired particle is killed rather than respawned). Read by `GParticle_Integrate` via the particle's emitter back-pointer (`GParticle_State+0x1C`). Cleared by the constructor. |
| `+0x36` | 2 | — | Padding (alignment to next dword). |
| `+0x38` | 4 | `ptr` | **active-list node handle** — the intrusive list link written by the manager on insertion. Set to 0 by the constructor, then updated. |

**Constructor argument order (for C# porting):** texture handle, `sprite_size_x` (f32), `sprite_size_y` (f32), `blend_additive_flag` (u32), capacity (u32), spawn sub-record array ptr, unused arg, external-origin ptr (or 0). `sprite_size_x`, `sprite_size_y`, and the unused arg are forwarded into the `GParticleBuffer` constructor and are not stored directly on `ParticleEffect`.

---

## 3. `GParticleBuffer` — 248 bytes (0xF8)

Minimally polymorphic: one virtual slot (destructor). Class name confirmed as `GParticleBuffer` via RTTI and the class-name strings embedded in the buffer method error messages.

A second constructor variant handles the weather/sky render path with different FVF and stride constants (FVF `0x102` / stride 20 for CPU-quad, FVF `0x002` / stride 12 for point-sprite) and different process-global vertex-buffer slots. That variant is NOT used by the particle-emitter path. All offsets below describe the emitter-path constructor.

| Offset | Size (bytes) | Type | Role |
|:---:|:---:|:---|:---|
| `+0x00` | 4 | `ptr` | **vtable pointer** for `GParticleBuffer` (one real slot — destructor; see §7). |
| `+0x04` | 4 | `u32` | **FVF** — `0x142` (XYZ \| DIFFUSE \| TEX1) in CPU-quad mode; `0x062` (XYZ \| DIFFUSE \| PSIZE) in point-sprite mode. Set by the constructor per the +0x0C flag. Applied via `SetFVF` at draw. |
| `+0x08` | 4 | `u32` | **vertex stride** — 24 bytes in quad mode; 20 bytes in point-sprite mode. Used as the `SetStreamSource` stride argument. |
| `+0x0C` | 1 | `u8` | **dual-path flag** — 0 = hardware point-sprite path; non-zero = CPU billboard-quad path. Decided in `setSpriteSize` by the test `sprite_size_x > D3DCAPS9.MaxPointSize`. Point-sprite if the size fits the hardware cap; CPU quad if oversize. Per-emitter test, not a global flag. |
| `+0x0D` | 3 | — | Padding. |
| `+0x10` | 4 | `u32` | **capacity** — particle count (from constructor argument). Used to size the shared VB and IB. |
| `+0x14` | 4 | `ptr` | **texture handle** — bound at draw. Receives the texture handle forwarded from `ParticleEffect+0x00`. |
| `+0x18` | 4 | `ptr` | **shared vertex-buffer wrapper pointer** — address of the process-global vertex-buffer wrapper slot. The draw-lock path dereferences this twice to reach the D3D vertex buffer; Lock writes the mapped range into `+0xF4`. |
| `+0x1C` | 4 | `ptr` | **CPU-quad scratch array** — `4 × capacity` quad-vertex slots (24 bytes each) staged on the CPU before upload. Null in point-sprite mode. Freed by the destructor. |
| `+0x20` | 48 | `f32[3]×4` | **local quad corner basis** — four model-space corner offsets (±half-extent, z = 0), baked from the point size in `setBillboardSize` (quad mode only). Corner element i is at `+0x20 + 12·i` (i = 0…3, spanning +0x20…+0x4F). |
| `+0x50` | 48 | `f32[3]×4` | **camera-facing corner basis** — the local basis rotated by the view matrix each frame in the draw-lock path; consumed by the vertex-fill routine to expand each particle into a camera-facing quad. Corner element i at `+0x50 + 12·i` (spanning +0x50…+0x7F). |
| `+0x80` | 32 | `f32[2]×4` | **quad corner UVs** — four (u, v) pairs (8 bytes each) pre-baked into the scratch quad-vertex layout at vertex+0x10. Pair i at `+0x80 + 8·i`. |
| `+0xA0` | 64 | `f32[16]` | **view matrix copy** — the camera/device view matrix (rotation part) captured each frame in the draw-lock path; used to orient the camera-facing corner basis. |
| `+0xE0` | 4 | `f32` | **POINTSIZE** — D3DRS_POINTSIZE (RS 154). Derived in the constructor from the incoming sprite size via a fast-reciprocal table; clamped to ≤ POINTSIZE_MAX. |
| `+0xE4` | 4 | `f32` | **POINTSIZE_MIN** — D3DRS_POINTSIZE_MIN (RS 155). Set by `setSpriteSize` to `sprite_size_y`, clamped to [1.0, POINTSIZE_MAX]. Also the lower clamp for per-particle size on the CPU-quad path. |
| `+0xE8` | 4 | `f32` | **POINTSIZE_MAX** — D3DRS_POINTSIZE_MAX (RS 166). Set by `setSpriteSize` to `sprite_size_x`. Also the upper clamp for per-particle quad size and the half-extent source for the local corner basis (`setBillboardSize`). |
| `+0xEC` | 4 | `f32` | **POINTSCALE_C** — D3DRS_POINTSCALE_C (RS 160; distance-attenuation C coefficient). Set to the constructor's unused-argument value (100.0). |
| `+0xF0` | 4 | `u32` | **fill cursor** — vertex count (point path) or particle count (quad path) filled so far; advanced by the run-length on each fill call; used as the draw-count source; reset to 0 at end of draw. |
| `+0xF4` | 4 | `ptr` | **locked-VB write pointer** — receives the mapped VB memory from the Lock call; the vertex-fill routine writes particle data here; cleared after unlock. |

The D3D render states at `+0xE0..+0xE8` are applied only on the point-sprite path. On the CPU-quad path, `+0xE4` and `+0xE8` additionally clamp per-particle quad size; the `+0xEC` POINTSCALE_C state is set but only meaningful for the hardware point-sprite path.

**`setSpriteSize`** sets `+0xE4`, `+0xE8`, and `+0x0C` (dual-path flag) from `sprite_size_x/y` and the hardware cap test. **`setBillboardSize`** sets `+0xE0` and, in quad mode, the `+0x20` local corner basis. **`setPointScaleA`** sets `+0xEC`.

### 3.1 Quad index buffer (shared global; `GParticleBuffer_FillStaticIndices`)

Quad mode uses a shared, process-global 16-bit index buffer sized to `12 × capacity` bytes = 6 indices per particle. For particle i with base vertex `4·i`, the six indices are `{4i, 4i+1, 4i+3, 4i, 4i+3, 4i+2}`, forming two triangles (0,1,3) and (0,3,2). Draw call: `DrawIndexedPrimitive(TRIANGLELIST, NumVertices = 4·count, PrimitiveCount = 2·count)`. Format `D3DFMT_INDEX16` (101); usage `0x208` (WRITEONLY | DYNAMIC). The index buffer is one process-global shared by both quad variants (emitter and sky/weather).

### 3.2 Two GParticleBuffer construction variants — FVF, stride, VB usage, draw call

Two constructors produce `GParticleBuffer` objects with different vertex formats. The emitter path (`Diamond` constructor) carries a `D3DCOLOR` diffuse in each vertex; the sky/weather path (full constructor) omits diffuse.

| Variant / path | Mode | FVF (`+0x04`) | Stride (`+0x08`) | VB usage | Draw call |
|---|---|---|---|---|---|
| Emitter | CPU-quad | `0x142` (XYZ \| DIFFUSE \| TEX1) | 24 | `0x208` (WRITEONLY \| DYNAMIC) | `DrawIndexedPrimitive` TRIANGLELIST, shared 16-bit IB |
| Emitter | point-sprite | `0x062` (XYZ \| DIFFUSE \| PSIZE) | 20 | `0x208` | `DrawPrimitive` POINTLIST |
| Sky/weather | CPU-quad | `0x102` (XYZ \| TEX1, no diffuse) | 20 | `0x208` | `DrawIndexedPrimitive` TRIANGLELIST, shared 16-bit IB |
| Sky/weather | point-sprite | `0x002` (XYZ only) | 12 | `0x248` (WRITEONLY \| DYNAMIC \| POINTS) | `DrawPrimitive` POINTLIST |

Notes:
- The sky/weather point-sprite VB carries the `D3DUSAGE_POINTS` bit (0x40) in its usage flags (total `0x248`); the emitter point-sprite VB uses `0x208` (the PSIZE component is carried in FVF `0x062` instead).
- The `pointscale` argument to the constructor sets `+0xEC` (POINTSCALE_C / D3DRS_POINTSCALE_C): **100.0** for the emitter path, **1.0** for the sky/weather path.
- The draw call mode (point vs quad) is decided per-emitter at runtime by `setSpriteSize`: quad when `sprite_size_x > D3DCAPS9.MaxPointSize`, point otherwise.
- **Separate `.xeff` mesh draw path (NOT a GParticleBuffer variant):** `WeatherParticle_DrawIndexedMesh` (the `.xeff` mesh-element draw) uses FVF `0x112` (XYZ | NORMAL | TEX1), stride 32 (12+12+8), and `DrawIndexedPrimitiveUP` — a completely separate path that shares neither the `GParticleBuffer` framework nor the shared index buffer.

### 3.3 Per-vertex quad layouts (CPU-billboard path)

The four corner vertices written per particle by the CPU fill routine:

**Emitter quad (FVF `0x142`, 24 bytes per vertex):**

| Sub-offset | Size | Type | Field |
|---:|---:|---|---|
| +0 | 12 | `f32[3]` | World position (camera-facing corner = local basis × half-extent + particle pos) |
| +12 | 4 | `D3DCOLOR` | Diffuse colour (B, G, R, A in-memory order; copied from `GParticle_Render+0x10`) |
| +16 | 8 | `f32[2]` | UV (u, v) — pre-baked into the scratch array by the constructor; corners (0,0)(1,0)(0,1)(1,1) |

**Sky/weather quad (FVF `0x102`, 20 bytes per vertex):**

| Sub-offset | Size | Type | Field |
|---:|---:|---|---|
| +0 | 12 | `f32[3]` | World position (camera-facing corner = local basis + particle pos) |
| +12 | 8 | `f32[2]` | UV (u, v) — pre-baked; same (0,0)(1,0)(0,1)(1,1) layout |

The emitter point-sprite vertex is the `GParticle_Render` record verbatim (§5; FVF `0x062`). The sky/weather point-sprite vertex is a raw `f32[3]` world position (12 bytes; FVF `0x002`).

---

## 4. `GParticle_State` — 32 bytes (0x20)

One per particle. Array base at `ParticleEffect+0x10`. Simulation object ticked by `GParticle_Integrate`. `ParticleEffect_drawAlive` scans the `+0x08` alive flag across this array to find contiguous live runs.

| Offset | Size (bytes) | Type | Role |
|:---:|:---:|:---|:---|
| `+0x00` | 4 | `ptr` | **spawn sub-record pointer** — points at this particle's 52-byte sub-record in the `ParticleEffect+0x18` array. Source of velocity, size, colour rates, and initial spawn values (§6.1). |
| `+0x04` | 4 | `ptr` | **render record pointer** — points at this particle's `GParticle_Render` record in `ParticleEffect+0x04`. `GParticle_Integrate` writes position, size, and colour here each tick. |
| `+0x08` | 1 | `u8` | **alive flag** — 1 while integrating; 0 when expired or not yet spawned. Key field for `ParticleEffect_drawAlive`'s contiguous-run detector. |
| `+0x09` | 1 | — | Padding. |
| `+0x0A` | 2 | `i16` | **spawn-delay countdown** — decremented by dt each tick; particle integration is suppressed while above 0. Seeded from spawn sub-record `+0x02` (`spawn_delay`) at respawn (see §8, R2). |
| `+0x0C` | 2 | `i16` | **life countdown** — decremented by dt each tick; when below 1, triggers the respawn path (or kills the particle if the emitter stop flag is set). Seeded from spawn sub-record `+0x04` (`lifetime`) at respawn (see §8, R2). |
| `+0x0E` | 2 | — | Padding. |
| `+0x10` | 12 | `f32[3]` | **velocity** (x, y, z) — added to the render position each tick (scaled by dt); optionally damped by the per-particle velocity-damp value (spawn sub-record `+0x30`) when non-zero. Seeded from spawn sub-record `+0x24` at respawn. |
| `+0x1C` | 4 | `ptr` | **emitter back-pointer** — points at the owning `ParticleEffect`. Read for the stop flag (`ParticleEffect+0x35`) and the world origin (`ParticleEffect+0x20` deref) during the respawn path. |

---

## 5. `GParticle_Render` — 20 bytes (0x14)

One per particle. Array base at `ParticleEffect+0x04`.

**This record is also the point-sprite vertex** (D3D9 FVF `0x062`: XYZ + DIFFUSE + PSIZE). On the point-sprite path the array is copied verbatim into the locked vertex buffer. On the CPU-quad path this record is the per-particle source for the four expanded camera-facing corner vertices (FVF `0x142`).

| Offset | Size (bytes) | Type | Role |
|:---:|:---:|:---|:---|
| `+0x00` | 12 | `f32[3]` | **world position** (x, y, z). At respawn = spawn sub-record position (`+0x0C`) + emitter origin; each tick `GParticle_Integrate` adds velocity × dt. |
| `+0x0C` | 4 | `f32` | **size** (point size / PSIZE). At respawn = `(float)` spawn sub-record `size_init` (`+0x06`); each tick += `size_rate` × dt (from spawn sub-record `+0x18`). Clamped to [POINTSIZE_MIN, POINTSIZE_MAX] by `GParticleBuffer` at draw. |
| `+0x10` | 4 | `D3DCOLOR` | **diffuse colour** — in-memory byte order B, G, R, A (ARGB dword). At respawn = spawn sub-record initial colour (`+0x08`); each tick the four signed per-channel rate values (`spawn sub-record +0x1C`) are added and the alpha channel is scaled by the graphics-quality fade factor. |

---

## 6. In-memory `.eff` emitter-file entry — 28-byte header

Loaded by `EFF_LoadParticleEmitter` from `data/effect/particle/particleEmitter.eff`. See `Docs/RE/formats/effects.md` §E for the full on-disk format and byte-walk. The table below documents the **runtime field roles** as consumed by the `ParticleEffect` constructor.

| Offset | Size (bytes) | Type | Runtime role | Feeds |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `u32` | `entry_id` — map lookup key | (key) |
| `+0x04` | 4 | `u32` | `particle_count` — sub-record count; also the live particle capacity | `ParticleEffect+0x1C` (capacity) |
| `+0x08` | 4 | `f32` | `sprite_size_x` | `GParticleBuffer` POINTSIZE_MAX / quad half-extent |
| `+0x0C` | 4 | `f32` | `sprite_size_y` | `GParticleBuffer` POINTSIZE_MIN |
| `+0x10` | 4 | `u32` | **`blend_additive_flag`** (see §8, R1) | `ParticleEffect+0x30` (additive blend gate) |
| `+0x14` | 4 | `ptr` | `texture` — resolved D3D texture handle (overwrites the on-disk head dword after the texture name is resolved) | `ParticleEffect+0x00` (texture handle) |
| `+0x18` | 4 | `ptr` | `particle_info` — spawn sub-record array pointer (overwrites the on-disk head dword after the sub-record block is read) | `ParticleEffect+0x18` (spawn sub-record array) |

The on-disk head occupies 28 bytes; the loader then reads `particle_count × 52` sub-record bytes followed by a 64-byte trailing texture name that is resolved to a handle and stored at entry+0x14. Fields at +0x14 and +0x18 are reused as runtime pointers after loading and no longer hold the original on-disk bytes. The entry-read loop terminates when an entry header's `particle_count` is 0 or fewer than 28 bytes remain.

### 6.1 52-byte spawn sub-record — runtime field map

One sub-record per particle; array at `ParticleEffect+0x18`. Per-particle spawn template and Euler-integration descriptor. Corrections R2 apply to the +0x02/+0x04 timer order (see §8).

| Offset | Size (bytes) | Type | Role |
|:---:|:---:|:---|:---|
| `+0x00` | 2 | `u16` | `life_bonus` — carried value; not consumed by the integrate or respawn paths examined. |
| `+0x02` | 2 | `u16` | **`spawn_delay`** — seeds `GParticle_State+0x0A` (spawn-delay countdown) at respawn. (R2: `effects.md` §E previously labelled this column `lifetime`.) |
| `+0x04` | 2 | `u16` | **`lifetime`** — seeds `GParticle_State+0x0C` (life countdown) at respawn. (R2: §E previously labelled this column `spawn_delay`.) |
| `+0x06` | 2 | `i16` | `size_init` — initial particle size; cast to float, seeds `GParticle_Render+0x0C` at respawn. |
| `+0x08` | 4 | `RGBA8` | Initial colour — seeds `GParticle_Render+0x10` (diffuse) at respawn. |
| `+0x0C` | 12 | `f32[3]` | Spawn position (x, y, z) — added to emitter origin to set `GParticle_Render+0x00` at respawn. |
| `+0x18` | 4 | `f32` | `size_rate` — per-tick size delta; `GParticle_Render+0x0C += size_rate × dt` each frame. |
| `+0x1C` | 8 | `i16[4]` | Per-channel colour-rate (B, G, R, A signed deltas) — added to each `GParticle_Render+0x10` channel component each tick. |
| `+0x24` | 12 | `f32[3]` | Initial velocity (x, y, z) — seeds `GParticle_State+0x10` at respawn. |
| `+0x30` | 4 | `f32` | `velocity_damp` — when non-zero, scales `GParticle_State+0x10` (velocity) each tick. |

Total: 52 bytes (0x34). This layout matches `effects.md` §E except for the +0x02/+0x04 label swap documented in R2.

---

## 7. Polymorphism and RTTI

**`GParticleBuffer`** — minimally polymorphic. One virtual slot:

| Slot | Offset | Role |
|:---:|:---:|:---|
| 0 | `+0x00` | Destructor (scalar / vector-deleting). Body restores the vtable pointer, frees the CPU-quad scratch array at `+0x1C`, and runs element destructors over the local-basis, camera-basis, and UV arrays at `+0x20`, `+0x50`, and `+0x80`. |

RTTI is present. Class name confirmed as `GParticleBuffer` by the RTTI CompleteObjectLocator and by the class-name string in the error messages emitted by the buffer methods. No base-class virtuals beyond the destructor.

**`ParticleEffect`** — no vtable. Offset `+0x00` holds the texture handle, not a vptr. All methods invoked through direct calls; not polymorphic.

**`GParticle_State` and `GParticle_Render`** — plain POD records. No vtable.

---

## 8. Reconciliation with `Docs/RE/formats/effects.md` §E

Two field definitions in the committed `effects.md` §E are corrected by the runtime analysis in this pass. Both corrections are `[static-confirmed]`.

### R1 — `entry+0x10` is the blend/additive flag, not `max_particles`

Both `ParticleEffectManager_CreateEffect_atPos` and the `_byName` variant pass the dword at entry+0x10 as the `ParticleEffect` constructor's additive-flag argument, which is stored at `ParticleEffect+0x30`. That field is read by `ParticleEffectList_DrawAll` to select SRCALPHA/ONE (additive) versus SRCALPHA/INVSRCALPHA (alpha-blend). The per-emitter live-particle capacity is entry+0x04 (`particle_count`), not entry+0x10.

**Required correction to `effects.md` §E:** rename the entry+0x10 field from `max_particles` to `blend_additive_flag` (or `blend_mode`).

### R2 — sub-record timer order at +0x02 / +0x04 is reversed versus §E

The respawn path seeds the particle's spawn-delay countdown (`GParticle_State+0x0A`) from sub-record **+0x02** and the life countdown (`GParticle_State+0x0C`) from sub-record **+0x04**. The current `effects.md` §E states the reverse: +0x02 = `lifetime`, +0x04 = `spawn_delay`.

**Required correction to `effects.md` §E:** swap the labels so that +0x02 = `spawn_delay` and +0x04 = `lifetime`. Fields at +0x00 (`life_bonus`) and +0x06 (`size_init`) are unchanged.

---

## 9. Open questions

**CLOSED (static, 2026-06-29) — Graphics-quality alpha-fade factor.** `GParticle_Integrate` scales each particle's alpha byte (`GParticle_Render+0x13`) every tick by a quality-dependent fade factor. Resolved byte-exact:
- Factor formula: `fade = 0.05 + 0.95 × qfield / 100.0`, where `qfield` is the graphics-quality singleton at dword index 28 (`+0x70`).
- Special case: when `qfield == 1`, `fade = 0.0` (particles rendered fully transparent).
- The 0.05f floor constant is fixed and confirmed. See also `Docs/RE/formats/effects.md §E.2.4` (same constant confirmed for the `.xeff` emitter path).
- Only the alpha byte (`+0x13`) is affected; RGB channels (`+0x10`, `+0x11`, `+0x12`) are untouched by the fade.

**[debugger-confirm] `ParticleEffect+0x08` and `+0x14`** — the constructor never writes a meaningful value to `+0x08`, and writes 0 to `+0x14` only at construction. No reader of either offset appears in any traced consumer (`ParticleEffect_drawAlive`, `GParticle_Integrate`, `ParticleEffect_setOrigin`, `ParticleEffectList_DrawAll`, all three `CreateEffect` variants). Static evidence is strong that both fields are dead/vestigial. Confirm via the live debugger that no out-of-lane subsystem (network handlers, UI, actor-attach) reads either offset at runtime; if confirmed, downgrade both to "unused".

**[debugger-confirm] CPU-quad multi-batch fill-cursor correctness.** In QUAD mode the memcpy destination advances by `perVertexStride × cursor` while each particle occupies 4 vertices (`4 × stride` bytes). This is correct only when the cursor starts at 0 (single batch) or in POINT mode. Rain and snow append **5 batches per lock**; emitter `drawAlive` appends one `fillVertices` per contiguous alive-run (potentially multiple). Correctness therefore hinges on the hardware falling into POINT mode (`D3DCAPS9.MaxPointSize ≥ sprite_size_x`). Read `MaxPointSize` (device caps at dword index 44) and the dual-path flag (`GParticleBuffer+0x0C`) live for rain (sprite 64.0) and snow (sprite 8.0) to confirm which branch is taken; if quad mode is active for a multi-batch draw, the 2nd+ batch destination under-advances by ×4 (visible overlap artefact).
