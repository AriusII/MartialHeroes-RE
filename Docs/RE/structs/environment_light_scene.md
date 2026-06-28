---
verification: confirmed (static IDA pass, deep-3d-structs 2026 pass, IDB SHA f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963)
ida_reverified: 2026-06-28
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [environment, lighting]
conflicts: none-open
wave11_deepen: 2026-06-28 — closed: vtable slot 7 role (recompute cull bounding sphere); +6704 gap mapped as sun/key-light block; D3DLIGHT9 full field identity at sub-obj +80..+183 (corrects prior fog-lens reading of +156..+180); keyframe commit order corrected to Ambient/Diffuse/Specular; change-detect cache semantics (fog gate poison pattern, ambient path via global quad); GI/lightmap/probe absence proven statically. Remaining [debugger-confirm]: point-light count (+1404), per-area sun vectors (+6712/+6724). [g_K_ambient CLOSED — see atm_deep_pass]
atm_deep_pass: 2026-06-29 — g_K_ambient CONFIRMED 0.0 statically (baked global, 1 reader, 0 writers; atmosphere deep-cartography pass, f61f66a9); removed from [debugger-confirm]. Deepening pass note: this pass closed the g_K_ambient runtime-value item.
---

# Structs: EnvironmentLightScene — GLight sub-object layout and full hub offset table

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. **All offsets below are byte offsets relative to the start of the object** —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> Full environment pipeline behaviour (fog, ambient, keyframe schedule, D3D render-state) is in
> `specs/environment.md`. Citing engineers: `// spec: Docs/RE/structs/environment_light_scene.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow + operand analysis
> corroborated across multiple call sites; `[static-hypothesis]` = inferred or zero-init only, not
> independently re-isolated this pass; `[open]` = unmapped, requires further work; `[debugger-confirm]`
> = layout fully closed statically, runtime data value requires a live singleton read to settle.

---

## Class identity and RTTI hierarchy

**The hub object's most-derived class is `Diamond::GPositionalLight`.** There is no dedicated
EnvironmentLightScene RTTI type — "EnvironmentLightScene" is the project's working name for this
statically-allocated, extended GPositionalLight singleton. The hub constructor installs the
GPositionalLight vtable at offset +0 and never overwrites it. The ~6846-byte object therefore
presents entirely as a GPositionalLight whose trailing bytes (beyond the 184-byte GPositionalLight
base) are environment keyframe tables appended during construction.

**Recovered class hierarchy** (from RTTI type descriptors at vtable−4):

```
Diamond::GNode
  └─ Diamond::GLight
        ├─ Diamond::GPositionalLight   (hub +0 ambient sub-object; all five point-light slots)
        └─ Diamond::GDirectionalLight  (hub +184 directional sub-object)
```

Both leaf classes expose **10 vtable slots** (indices 0..9). Slots 2–6, 8, and 9 are shared
(inherited GNode / GLight virtuals). **Slot 9 is the scene-graph mark-dirty**: it ORs the per-node
dirty bit at sub-object `+56` and propagates to parent nodes. **Slot 7 is the class-specific
virtual** that differs between GPositionalLight and GDirectionalLight: it recomputes the cull bounding
sphere stored in the GNode sub-fields. GPositionalLight's implementation solves the D3D attenuation
quadratic for the influence radius (using Falloff/Atten0/Atten1/Atten2 from the embedded D3DLIGHT9)
and stores the result at sub-object +36 (radius) with center = light position (sub-object
+40/+44/+48), then clears the dirty bit at +56. GDirectionalLight's implementation writes radius −1.0
(unbounded, since a directional light has infinite extent) and center zero, then clears the dirty bit.

| Slot | Role | GPositionalLight vs GDirectionalLight |
|-----:|------|--------------------------------------|
| 0 | Construction/clone-family virtual | Differs (class-specific) |
| 1 | Debug-describe virtual (prints D3DLIGHT9 field labels and values to debug output) | Differs (class-specific) |
| 2..6, 8 | Shared GNode / GLight virtuals | Identical |
| 7 | **Recompute cull bounding sphere** — positional: radius from attenuation quadratic, center = position; directional: radius −1.0 (unbounded), clears dirty bit at +56 | **Differs** |
| 9 | Scene-graph mark-dirty (ORs sub-object `+56`, propagates to parents) | Identical |

---

## The 184-byte GLight base — internal layout

This 184-byte sub-object shape is used **eight times** in the hub: once at hub `+0` (ambient role,
GPositionalLight), once at hub `+184` (GDirectionalLight), and five times for the point-light slots
at hub `+368 + 184·n` (n = 0..4, GPositionalLight). All offsets below are relative to the
**sub-object base**, not the hub base.

The tail at sub-object +80..+183 is a verbatim **104-byte D3DLIGHT9** struct. This is proven by the
class's slot-1 debug-describe virtual, which emits literal D3DLIGHT9 field labels for every member in
this range ("diffuse:", "specular:", "ambient:", "position:", "direction:", "attenuation (c,l,q):"),
and by the ctor initialization values. The scene-graph draw callback checks the software-enable flag
at sub-object +76 and, if set, passes sub-object +80 directly to `IDirect3DDevice9::SetLight` (D3D9
device vtable slot 51) — no separate marshaling struct is built.

| Offset | Size | Type | Role |
|-------:|-----:|------|------|
| +0 | 4 | ptr | vtable (GPositionalLight or GDirectionalLight, per instance) |
| +4 | 4 | u32 | GNode reference count (incremented on attach to GGroup) |
| +8 | 28 | bytes | GNode base linkage (parent/child/name/callback book-keeping; sub-fields unresolved except those below) |
| +36 | 4 | f32 | Cull bounding-sphere **radius** (−1.0 = unbounded for directional; positive = influence radius for positional, solved by vtable slot 7 from the attenuation quadratic) |
| +40 | 4 | f32 | Cull bounding-sphere **center X** (= D3DLIGHT9.Position X for positional; 0 for directional) |
| +44 | 4 | f32 | Cull bounding-sphere **center Y** |
| +48 | 4 | f32 | Cull bounding-sphere **center Z** |
| +52 | 4 | — | Remainder of GNode base (unresolved) |
| +56 | 1 | u8 | Scene-graph dirty bit (ORed by vtable slot 9 mark-dirty; cleared by vtable slot 7 after sphere recompute; propagated to parents) |
| +57 | 19 | bytes | Remainder of GNode base |
| +76 | 1 | u8 | Software-enable flag (1 = enabled; ctor-init 0 on all hub sub-objects; toggled by the enable/disable helper) |
| +77 | 3 | — | Padding |
| +80 | 4 | u32 | **D3DLIGHT9.Type** — 1 = D3DLIGHT_POINT (GPositionalLight), 3 = D3DLIGHT_DIRECTIONAL; GLight base ctor writes 3, GPositionalLight ctor overwrites with 1 |
| +84 | 16 | vec4 f32 | **D3DLIGHT9.Diffuse** (RGBA; ctor-init 1,1,1,1) |
| +100 | 16 | vec4 f32 | **D3DLIGHT9.Specular** (RGBA; ctor-init 1,1,1,1) |
| +116 | 16 | vec4 f32 | **D3DLIGHT9.Ambient** (RGBA; ctor-init 1,1,1,1) |
| +132 | 12 | vec3 f32 | **D3DLIGHT9.Position** XYZ (ctor-init 0,0,0; also the bounding-sphere center source for vtable slot 7) |
| +144 | 12 | vec3 f32 | **D3DLIGHT9.Direction** XYZ (ctor-init 0,0,0; GDirectionalLight ctor seeds 0,0,1; overwritten each frame by the sun-vector negate-and-normalise path) |
| +156 | 4 | f32 | **D3DLIGHT9.Range** (ctor-init 0; the fog setter writes `s × 3.0` here on the ambient/directional sub-objects; point-light slots use this for effective range) |
| +160 | 4 | f32 | **D3DLIGHT9.Falloff** (range-scale setter writes 1.0; used as the F term in the vtable slot 7 radius quadratic: radius cutoff solved from `l² − 4·q·(c − 1/F)`) |
| +164 | 4 | f32 | **D3DLIGHT9.Attenuation0** (constant; GPositionalLight describe prints "attenuation (c,l,q):" for +164/+168/+172) |
| +168 | 4 | f32 | **D3DLIGHT9.Attenuation1** (linear; fog setter writes `1/s` here on the ambient/directional sub-objects) |
| +172 | 4 | f32 | **D3DLIGHT9.Attenuation2** (quadratic; fog setter writes 0.0 on the LINEAR fog path; used as `q` in the slot 7 radius quadratic) |
| +176 | 4 | f32 | **D3DLIGHT9.Theta** (spotlight inner cone; ctor-init 0; unused in the fixed-function path) |
| +180 | 4 | f32 | **D3DLIGHT9.Phi** (spotlight outer cone; ctor-init 0; unused) |

**D3DLIGHT9 field reuse by the fog and point-light paths:** the same shared range-setter routine is
called for both roles. On the ambient/directional sub-objects it carries fog parameters: Range (+156)
receives `s × 3.0` (far), Attenuation1 (+168) receives `1/s` (near), Attenuation2 (+172) receives
0.0 on the LINEAR fog path. On the five point-light slots the same fields carry the attenuation
quadratic parameters for point-light range. The setter is guarded: it only runs when `0 < s < ~1.02e38`.
After writing the four floats it calls vtable slot 9 (mark-dirty) on the sub-object.

**Per-frame directional publish:** after each directional commit the D3DLIGHT9.Diffuse field value is
also written to a global mirror for other subsystems that consume the sun colour.

---

## Full hub offset table

`this` = the static GPositionalLight singleton (the EnvironmentLightScene hub). All offsets are
hub-relative. Sub-object-relative notes reference the 184-byte GLight base table above.

| Offset | Size | Type | Role |
|-------:|-----:|------|------|
| +0 | 184 | GPositionalLight | **Ambient-role light** — the hub's own GLight base (full 184-byte shape above). Software-enable at hub+76. D3DLIGHT9.Range/Atten fields at hub+156..+172 hold fog far/near/density values. |
| +184 | 184 | GDirectionalLight | **Directional light** sub-object. Software-enable flag at hub+260 (= 184+76). D3DLIGHT9.Direction at sub-object+144 (hub+328). |
| +368 | 5 × 184 = 920 | GPositionalLight[5] | **Five point-light slots** at hub bases +368, +552, +736, +920, +1104. Each slot's enable flag at slot+76; D3DLIGHT9.Range/Atten block at slot+156. |
| +1288 | ~92 | GGroup | Scene-node light group; children = directional + hub-self + five point-light slots. Reference count at hub+1292. |
| +1380 | 5 × 4 = 20 | i32[5] | Active point-light **index array** (one slot per GPositionalLight; value −1 = empty). Ctor-init all −1. |
| +1400 | 4 | ptr | Point-light data **base pointer** → external array of 60-byte point-light records (see §External record below). Set at light-load time; not heap-allocated on the hub. |
| +1404 | 4 | u32 | Point-light **count** — number of 60-byte records at +1400. `[debugger-confirm]` — loader and count field fully mapped; count 0 is fully tolerated (all index slots remain −1); whether any shipped play area carries count > 0 requires a live singleton read. |
| +1408 | 4 | f32 | Point-light **selection radius** — ctor-init 1024.0; overwritten by the `point_light%d.bin` header when that file is present for the current area (dual-purpose: selection radius default, then overwritten at load time). |
| +1412 | 4 | f32 | Cached local-player **X** for proximity re-selection (ctor-init −FLT_MAX). |
| +1416 | 4 | f32 | Cached local-player **Y** (stored; not used in the XZ distance test). |
| +1420 | 4 | f32 | Cached local-player **Z** for proximity re-selection (ctor-init −FLT_MAX). |
| +1424 | 48 × 48 = 2304 | record[48] | **Directional colour keyframe table.** 48 records × 48 bytes. Each record = 3 × vec4 RGBA float. Commit order (corrected): group 1 (record+0) → D3DLIGHT9.Ambient; group 2 (record+16) → D3DLIGHT9.Diffuse; group 3 (record+32) → D3DLIGHT9.Specular. Alpha ignored. Ctor-init white. See §Keyframe element layout. |
| +3728 | 48 | u8[48] | Directional per-keyframe **enable bytes** (0 = apply this keyframe on the directional lerp path). |
| +3776 | 48 × 48 = 2304 | record[48] | **Ambient colour keyframe table.** Identical 48-byte record shape; same commit order (group 1 → Ambient, group 2 → Diffuse, group 3 → Specular). Each RGB channel multiplied by `g_K_ambient` at apply time. `g_K_ambient` **CONFIRMED 0.0** — baked global, single reader, zero writers (atmosphere deep-cartography pass 2026-06-29; see §10.7 and `specs/environment.md §6.2a`). Ambient keyframe table is therefore inert at runtime. Ctor-init white. |
| +6080 | 48 | u8[48] | Ambient per-keyframe **enable bytes**. |
| +6128 | 48 × 4 = 192 | f32[48] | **Fog scalar `s`** keyframe table. Drives D3DLIGHT9.Range = s×3.0 (far), D3DLIGHT9.Atten1 = 1/s (near); LINEAR fog enabled when s > 0. Change-detect gate at +6836. |
| +6320 | 48 × 4 = 192 | f32[48] | **Point-light master intensity** keyframe table. All five slots enabled/disabled together when the lerped value crosses 0.1. Change-detect cache at +6840. |
| +6512 | 48 × 4 = 192 | u8[4][48] | **Ambient-base BGRA byte** keyframe table. Each entry is four bytes (B, G, R, A). Ctor-init (0, 0, 0, 0xFF) per slot. Used in the device-ambient byte-add path (see `specs/environment.md §6.2a`). |
| +6704 | 4 | u32 | **Sun-direction override flag** — 0 = direction is (re)derived from the vec3 at +6712 on each load; nonzero = locked. Default (synth) 0. |
| +6708 | 4 | u32 | **Key-light-position override flag** — paired with the position vec3 at +6724 by editor accessors. Default 0. |
| +6712 | 12 | vec3 f32 | **Sun / world-light direction source** (default −7, 7, 20). At load: negated, normalised, and written to the directional sub-object D3DLIGHT9.Direction (hub+328); also published to global sun-direction variables. `[debugger-confirm]` — per-area bin values require a live singleton read. |
| +6724 | 12 | vec3 f32 | **Key-light position source** (default −7, 7, 20). At load: written to the ambient sub-object D3DLIGHT9.Position (hub+132) and to global key-light position variables. `[debugger-confirm]` — per-area bin values require a live singleton read. |
| +6736 | 3 × 16 = 48 | vec4 f32[3] | **Live directional colour output** (groups 1/2/3 in commit order Ambient/Diffuse/Specular). Ctor-init 1.0. Written by the directional lerp path; read by the directional commit to the D3D light. |
| +6784 | 3 × 16 = 48 | vec4 f32[3] | **Live ambient colour output** (groups 1/2/3, multiplied by `g_K_ambient`). Ctor-init 1.0. Written by the ambient lerp path; read by the ambient commit. |
| +6832 | 3 | u8[3] | Device-ambient **BGR working values** (B at +6832, G at +6833, R at +6834) — freshly interpolated each frame from the +6512 keyframe table. Change-detect comparison is against separate global last-committed bytes (not stored on the hub). |
| +6835 | 1 | u8 | Device-ambient **alpha working value** (ctor-init 0xFF). Part of the same four-byte BGRA block compared against the global last-committed quad each frame. |
| +6836 | 4 | f32 | **Fog-scalar gate** — compared against the current frame's interpolated fog scalar; on mismatch the D3D fog state is re-committed, then this field is set to the poison constant 5.0. Because the sentinel is overwritten with 5.0 (not the actual scalar) after each commit, it forces a re-commit on essentially every subsequent frame whose scalar differs from 5.0. Not a true "last value" change-detect cache. |
| +6840 | 4 | f32 | **Point-light master-intensity cache** — genuine change-detect: commit runs when the interpolated intensity differs from this value OR the dirty flag (+6844) is set; afterwards +6840 = current intensity, +6844 = 0. |
| +6844 | 1 | u8 | Point-light **dirty flag** (1 = force a point re-commit; set after `Light_LoadOrSynthDefault` and after each selection rebuild). |
| +6845 | 1 | u8 | Cached `OPTION_BRIGHT` additive offset (0..255; ctor-init 0; recomputed on options change as `OPTION_BRIGHT / 100 × 255`). |

**Total size ≥ 6846 bytes** (highest addressed field at hub +6845).

**Device-ambient commit path:** each frame the hub BGRA working values (+6832..+6835) are compared
against the global last-committed BGRA byte quad. On mismatch, `IDirect3DDevice9::SetRenderState`
is called with state `D3DRS_AMBIENT = 139` and a packed ARGB value where each RGB channel has the
`OPTION_BRIGHT` offset (+6845) added; the global last-committed bytes are then updated. This path is
entirely separate from the fog-scalar gate at +6836.

---

## Keyframe element layout (anchors +1424 and +3776)

| Position within a 48-byte record | Size | Type | Commit destination |
|----------------------------------:|-----:|------|------|
| record+0 | 16 | vec4 f32 | Committed to **D3DLIGHT9.Ambient** (sub-obj +116); alpha ignored |
| record+16 | 16 | vec4 f32 | Committed to **D3DLIGHT9.Diffuse** (sub-obj +84); alpha ignored |
| record+32 | 16 | vec4 f32 | Committed to **D3DLIGHT9.Specular** (sub-obj +100); alpha ignored |

The in-file commit order is **(Ambient, Diffuse, Specular)** — not the same as the D3DLIGHT9
sub-object memory order (Diffuse, Specular, Ambient at +84/+100/+116). This permutation is proven by
the commit-step destination offsets.

**All three groups are lerped** by `Light_PerFrameApply` on every call. An earlier inline annotation
within the IDB claiming group 3 is not read is stale and incorrect; group 3 is lerped into the live
output slots alongside groups 1 and 2.

The per-frame driver computes `kf = floor(t_ms / 1800)`, `frac = (t_ms mod 1800) / 1800`,
`kf_next = (kf + 1) mod 48` and linearly interpolates all three groups' RGB channels (alpha never
read) between the `kf` and `kf_next` records. Results are written to the live output slots: +6736
for the directional table, +6784 for the ambient table.

Directional colour groups are committed to the D3D light raw. Ambient colour groups are multiplied
by `g_K_ambient` before committing; with `g_K_ambient = 0.0` the ambient keyframe table is inert.
`g_K_ambient` is **CONFIRMED 0.0** — baked global with static initialiser 0.0, exactly one reader
(`Light_PerFrameApply`), and zero writers anywhere in the binary (atmosphere deep-cartography pass
2026-06-29; confirms `specs/environment.md §6.2a`). Live read is no longer needed. The ambient
table produces no contribution to the device in the shipping client.

---

## light%d.bin binary layout (file-to-hub mapping)

The per-area binary file `light%d.bin` (keyed by the current area ID) is loaded as a single
**5312-byte** (0x14C0) block directly into the hub starting at hub offset +1424. The file therefore
covers exactly hub offsets +1424..+6735. The file is composed of eight sections in this order:

| File offset | Byte count | Hub offset | Content |
|------------:|----------:|----------:|---------|
| 0x0000 | 2304 | +1424 | Directional colour keyframe table (48 × 48 B; commit order Ambient/Diffuse/Specular) |
| 0x0900 | 48 | +3728 | Directional per-keyframe enable bytes (0 = apply) |
| 0x0930 | 2304 | +3776 | Ambient colour keyframe table (48 × 48 B; each channel ×`g_K_ambient` at apply) |
| 0x1230 | 48 | +6080 | Ambient per-keyframe enable bytes |
| 0x1260 | 192 | +6128 | Fog scalar `s` keyframe table (48 × f32) |
| 0x1320 | 192 | +6320 | Point master-intensity keyframe table (48 × f32) |
| 0x13E0 | 192 | +6512 | Device-ambient BGRA byte keyframe table (48 × 4 B, order B,G,R,A per entry) |
| 0x14A0 | 32 | +6704 | Sun / key-light block: two u32 flags (+6704/+6708), sun direction vec3 (+6712), key-light position vec3 (+6724) |

If the file is absent, `Light_LoadOrSynthDefault` synthesises defaults: both colour keyframe tables
white (1,1,1,1), both flag words 0, both direction/position vectors (−7, 7, 20).

---

## point_light%d.bin binary header

`point_light%d.bin` (also keyed by the current area ID) begins with a two-word header before the
record array:

| File offset | Size | Type | Role |
|------------:|-----:|------|------|
| +0 | 4 | f32 | **Intensity-scale / selection-radius header** — loaded into hub+1408, overwriting the ctor's 1024.0 default when the file is present |
| +4 | 4 | u32 | **Record count** — loaded into hub+1404 |
| +8 | count × 60 | record[] | Point-light records (60-byte layout in §External record below) |

If the file is absent, hub+1404 remains 0 and all five index slots remain −1. The loader tolerates
absence completely.

---

## External 60-byte point-light record

The base pointer at hub +1400 references an external array of these records. The hub owns only the
pointer and count; the records are loaded by the per-area light loader and pool-owned elsewhere.

| Offset | Size | Type | Role |
|-------:|-----:|------|------|
| +0 | 12 | vec3 f32 | Ambient colour RGB → written to D3DLIGHT9.Ambient (slot sub-obj +116); multiplied by master intensity, alpha forced 1.0 |
| +12 | 12 | vec3 f32 | Diffuse colour RGB → written to D3DLIGHT9.Diffuse (slot sub-obj +84); multiplied by master intensity, alpha forced 1.0 |
| +24 | 12 | vec3 f32 | Specular colour RGB → written to D3DLIGHT9.Specular (slot sub-obj +100); multiplied by master intensity, alpha forced 1.0 |
| +36 | 12 | vec3 f32 | Position XYZ → written to D3DLIGHT9.Position (slot sub-obj +132); X and Z used in the proximity distance test against the local player |
| +48 | 4 | f32 | Range/attenuation scalar `s` → fed to the shared range-setter at slot+156 (D3DLIGHT9.Range = s×3, Atten1 = 1/s) |
| +52 | 4 | u32 | **Skip flag** — nonzero: this record is skipped entirely in the slot-rebuild pass |
| +56 | 4 | u32 | **Weather-flicker-enable flag** — value 1: the weather flicker ramp modulates this light's range each frame |

All colour values are multiplied by the per-frame master intensity (from the +6320 keyframe table,
cached at +6840) before being written to the slot colour fields. Alpha is forced to 1.0 on all three
colour groups.

---

## Point-light selection and per-slot runtime build

**Per-slot runtime field mapping** (written during the D3DLIGHT9 build step for each selected record,
with `n` = slot index 0..4 and slot base = hub +368 + 184·n):

| Slot field (hub-relative) | D3DLIGHT9 field | Source in the 60-byte record |
|---|---|---|
| slot+84..+99 | Diffuse | record+12 × intensity, alpha 1.0 |
| slot+100..+115 | Specular | record+24 × intensity, alpha 1.0 |
| slot+116..+131 | Ambient | record+0 × intensity, alpha 1.0 |
| slot+132..+143 | Position | record+36 |
| slot+156, slot+168 | Range / Atten1 via shared setter | record+48 scalar s → Range = s×3, Atten1 = 1/s |

**Selection** runs lazily, guarded by the cached player position at hub +1412/+1420. When the local
player moves beyond the XZ threshold, the pass scans the external record array, applies a
log-distance form on the XZ plane against the player position, and fills ≤5 index slots in the +1380
array with the nearest records within the selection radius at hub+1408. After each rebuild the dirty
flag (+6844) is set and all selected slots are committed.

**Master-intensity gate:** when the lerped +6320 intensity crosses 0.1, all five GPositionalLight
slots are enabled or disabled together by toggling each slot's software-enable flag at slot+76. The
actual D3D LightEnable call occurs at scene-graph draw time via the GGroup.

**Weather flicker:** for each selected record where the flicker flag (record+56 == 1) is set, a
bouncing ramp from a 20-entry speed table reduces the effective range per frame: the ramp value is
multiplied by 0.3 and subtracted from the record range before feeding the shared range-setter. This
modulates attenuation without altering the stored record.

---

## GI / lightmap / lightprobe — absence confirmed

The complete device-facing lighting commit surface of this subsystem is exactly three fixed-function
D3D9 call types:
- `IDirect3DDevice9::SetLight` (D3D9 device vtable slot 51) — one D3DLIGHT9 per enabled sub-object
  (1 directional sun + up to 5 dynamic point lights), driven by the scene-graph draw callback.
- `IDirect3DDevice9::SetRenderState(D3DRS_AMBIENT = 139)` — a single flat device ambient colour.
- `IDirect3DDevice9::LightEnable` — per-slot software-enable toggled through the GGroup at draw time.

There is **no** lightmap texture stage, **no** spherical-harmonic / irradiance-probe array, **no**
baked GI buffer anywhere on the hub or in the load path. Lighting is pure D3D9 fixed-function vertex
lighting. The keyframe tables animate only the directional + ambient colours and the device ambient by
time-of-day. Absence of GI/lightmap/lightprobe is strongly established by static analysis. (Per-vertex
baked colours or terrain texture blends are separate mechanisms in other subsystems and out of scope
here — they are not a GI/probe system.)

---

## Time source and interpolation cursor

**There is no keyframe cursor or time field on the hub.** The interpolation cursor is entirely
external. `Light_PerFrameApply` is called once per environment tick by `SkySystem_UpdatePerFrame`
with the global time-of-day millisecond value (`g_EnvTime_TODms`). It recomputes `kf`, `frac`, and
`kf_next` on every call from that argument — there is no stored state on the hub for these values.

The hub holds only **change-detect caches** (+6836 fog gate, +6840 point intensity,
+6832–+6835 device-ambient BGRA working values) to skip redundant D3D render-state pushes when
values are unchanged.

**A 1:1 port must drive all keyframe tables from an external day/night clock**, not from any cursor
field on this object.

---

## Object lifetime and singleton pattern

The hub is a **static BSS singleton**: its storage is a fixed-size global buffer, not heap-allocated.
A guarded one-time constructor runs on first access via `EnvironmentLightScene_GetSingleton` and
registers an `atexit` destructor. Subsequent accesses bypass the guard and return the cached pointer
directly.

**Construction order** (`EnvironmentLightScene_ctor`):

1. Construct the `+0` GPositionalLight sub-object (ambient role).
2. Construct the `+184` GDirectionalLight sub-object.
3. Construct the five GPositionalLight point-light slots at +368, +552, +736, +920, +1104 in
   sequence (an eh-vector of five).
4. Construct the GGroup scene node at +1288.
5. Zero the point-light bookkeeping fields; set the selection radius to 1024.0 at hub+1408; seed the
   cached player positions (hub+1412, +1420) to −FLT_MAX.
6. Fill both keyframe colour tables (white 1,1,1,1) and the ambient-base byte table ((0,0,0,0xFF)
   per slot) via the table-init helper.
7. Zero the device-ambient BGR working values (hub+6832..+6834); set the device-ambient alpha
   (hub+6835) to 0xFF; zero the OPTION_BRIGHT offset cache (hub+6845).
8. Attach the directional sub-object, hub-self, and the five point-light slots to the GGroup.
9. Clear every software-enable flag (sub-object+76 = 0) and every active point-light index
   (all five slots in the +1380 array to −1).

---

## Open items / debugger-confirm

| Tag | Item | Static bound |
|-----|------|------|
| `[debugger-confirm]` | **Per-area point-light count (hub+1404)** | Loader, count field, and both tolerances (count 0: all five index slots remain −1) fully mapped. Live singleton read needed to confirm whether any shipped play area carries count > 0. |
| `[CONFIRMED — closed]` | **Runtime value of `g_K_ambient`** | **CONFIRMED 0.0 (atmosphere deep-cartography pass 2026-06-29).** `g_K_ambient` is a baked global with static initialiser 0.0, exactly one reader (`Light_PerFrameApply`), and zero writers anywhere in the binary. A zero-init global with no writer cannot change at runtime through any code path — live confirmation is no longer needed. The per-keyframe ambient table is provably inert in the shipping client. |
| `[debugger-confirm]` | **Per-area sun / key-light vectors (hub+6712, +6724)** | Layout, defaults (−7, 7, 20), flags at +6704/+6708, and all consumers fully mapped. Live read of hub+6704..+6735 on the running singleton needed to confirm per-area bin values. |

All other previously open items are now statically resolved: the 32-byte gap at +6704..+6735 (sun /
key-light block), vtable slot 7 role (cull bounding-sphere recompute), keyframe element commit order
(corrected to Ambient/Diffuse/Specular), all four change-detect cache semantics, the full D3DLIGHT9
identity of sub-object +80..+183, and GI/lightmap/probe absence are closed.

---

## Cross-references

- Full environment pipeline behaviour (fog, ambient, keyframe schedule, OPTION_BRIGHT path,
  D3D render-state tokens): `specs/environment.md`.
- Fog far/near/density path and the `g_K_ambient` ambient gate: `specs/environment.md §6.2a`.
- Point-light runtime selection and flicker ramp: `specs/environment.md §3.4`.
- Sky/day-night time source (`g_EnvTime_TODms`, `SkySystem_UpdatePerFrame`): `specs/environment.md`.
- World and lighting singletons index: `structs/runtime_singletons.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
