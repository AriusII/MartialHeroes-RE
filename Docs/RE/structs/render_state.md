---
verification: confirmed (initial, IDB SHA f61f66a9, deep-3d-wave4 (2026-06-28)); deepened (deep-3d-cartography, static-only, 2026-06-29 — slot 7 = GTexture not reserved; full 18-class apply enumeration with D3D9 RS keys resolved; z_func sentinel open item closed; struct-size validation added)
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence: [static-ida]
sample_verified: false
subsystems: [render_pipeline, scene_graph]
cross_refs: [structs/cull_pipeline.md, structs/renderer_device.md, structs/gview.md, specs/rendering.md, specs/render_pipeline.md, specs/scene_graph.md]
---

# Structs: GRenderState family — render-state-set objects (18-slot OSG-style StateAttribute)

> **Clean-room spec. Neutral description only — NO decompiler pseudo-code, NO binary addresses,
> NO decompiler-generated identifiers.** Promoted from dirty-room static comprehension under EU
> Software Directive 2009/24/EC Art. 6, solely to document object layout for clean-room
> reimplementation. All offsets are byte offsets relative to the start of the named struct —
> they are struct/layout offsets, NOT memory addresses, and must never be treated as such.
> Citing engineers: `// spec: Docs/RE/structs/render_state.md`.
>
> **Confidence vocabulary:** `[confirmed]` = recovered from static control-flow, RTTI, and
> operand analysis corroborated across constructors, destructors, and the apply chain;
> `[static-hypothesis]` = inferred or seen only in zero-init paths; `[debugger-pending]` =
> flagged for live `?ext=dbg` session confirmation (never `dbg_start`).

---

## 1. Object identity

The Diamond engine models each Direct3D render-state group as a polymorphic `GRenderState` object —
the OpenSceneGraph `StateAttribute` analogue. Every render-state concern (alpha test, blending,
cull mode, depth test, material, fog, etc.) is a subclass that carries the raw D3D values for one
group and overrides an apply/bind virtual (vtable slot 2) to push those values to the device.

There are **18 cache slots**, indexed 0–17 by the object's `state_type_id` field. All 18 map to
concrete classes — **slot 7 is `GTexture`**, a direct `GRenderState` subclass (own vtable,
60-byte allocation) that binds a texture to stage 0. The force-commit dispatcher calls apply on
all 18 slots unconditionally with no null-check; every slot is populated at runtime.

All concrete applies route to the single global `GDevice` device wrapper (see
`structs/renderer_device.md`). The 18-slot cache lives on the per-view cull machinery (`GCull`),
not on the renderer-device object — confirmed and cross-referenced by `structs/renderer_device.md`
§7/§9 and detailed in `structs/cull_pipeline.md` §5.

RTTI type descriptors are intact for the full family (`Diamond::GRS*` MSVC vftable symbols and
per-class constructors are symbolized in the IDB), so the recovery is authoritative.

---

## 2. Class hierarchy

```
GObject  (ref-counted base)
  └─ GNode  (scene node)
       └─ Diamond::GRenderState    (abstract base; compare_key at +0x24, state_type_id at +0x28)
            ├─ GRSAlphaTest          id  0
            ├─ GRSBlending           id  1
            ├─ GRSCullMode           id  2
            ├─ GRSDepthTest          id  3
            ├─ GRSTransformBase      id  4   (abstract intermediate)
            │    └─ GRSTransform             (concrete; instances are pool-allocated)
            ├─ GRSShadeModel         id  6
            ├─ GTexture              id  7   (direct GRenderState subclass; texture-stage-0 bind)
            ├─ GRSFillMode           id  8
            ├─ GRSMaterial           id 11
            ├─ GRSColorMask          id 13
            ├─ GRSLinePattern        id 14
            ├─ GRSFog                id 16
            ├─ GRSTFactor            id 17
            └─ GRSBoolean            (abstract boolean base; `on` bool at +0x2C)
                 ├─ GRSDepthMask      id  5
                 ├─ GRSLighting       id  9
                 ├─ GRSHighlight      id 10
                 ├─ GRSDithering      id 12
                 └─ GRSTransparency   id 15
```

`GRSBoolean` is the intermediate base for the five boolean-toggle states. Its vtable slot 2
(apply) is abstract; every Boolean subclass provides a concrete override. The same holds for
`GRenderState` itself and `GRSTransformBase`.

---

## 3. VTable slot-role map

The vtable is **5 slots wide** for all family members; `GRSTransform` extends to 6 by adding slot 5.
Slots 0–4 are shared across the entire family with the same ordinal role.

| Slot | Offset | Role |
|-----:|:------:|------|
| 0 | +0x00 | **Scalar-deleting destructor.** Per-class; restores vtable, calls `GNode`/`GObject` destructor chain, frees the allocation when the free-flag is set. |
| 1 | +0x04 | **Print debug info.** Verbose dump of every field to a debug output stream. The bodies for this slot are the largest virtual bodies in the family — they are debug printers, not the apply. |
| 2 | +0x08 | **Apply / bind-to-device.** The polymorphic commit that pushes this state's values to the Direct3D device via the global `GDevice` wrapper. **This is the slot `GCull_ApplyRenderStateSet` calls.** Abstract (`__purecall`) in `GRenderState` and `GRSBoolean`; every concrete subclass overrides it. |
| 3 | +0x0C | **Minor per-class predicate.** Base implementation returns 0. A few subclasses (Blending, AlphaTest, TFactor) supply small variants that return small constants. Not load-bearing for state semantics. |
| 4 | +0x10 | **Equals / compare.** Shared implementation across all classes: returns true iff `state_type_id` (+0x28) and `compare_key` (+0x24) both match; a `compare_key` of −1 forces re-apply ("always differ"). Drives the lazy compare-and-apply path of the dispatcher. |
| 5 | +0x14 | **GRSTransform-only extended virtual.** Absent on all other classes in the family; present only in the `GRSTransform` vtable. |

---

## 4. Base class offset tables

### `Diamond::GRenderState` base fields

| Offset | Size | Type | Field | Role | Confidence |
|-------:|-----:|------|-------|------|------------|
| +0x00 | 4 | vtable ptr | vtable | Class vtable pointer. | confirmed |
| +0x04 | 32 | bytes | *(GNode/GObject base)* | Ref-count and scene-node base fields inherited from `GNode` / `GObject`; not enumerated here (see `specs/scene_graph.md`). | confirmed |
| +0x24 | 4 | int32 | `compare_key` | The value the `equals` virtual (slot 4) diffs on. Simple toggle/mode states store their mode value here; complex states (Material, Fog) use −1 = "always apply". | confirmed |
| +0x28 | 4 | int32 | `state_type_id` | 0–17; selects the cache slot. Set by the base constructor argument and never modified after construction. | confirmed |

### `Diamond::GRSBoolean` additional fields (base of DepthMask / Lighting / Highlight / Dithering / Transparency)

`GRSBoolean` inherits the full `GRenderState` base layout and adds one field:

| Offset | Size | Type | Field | Role | Confidence |
|-------:|-----:|------|-------|------|------------|
| +0x24 | 4 | int32 | `compare_key` | Set to `(on != 0)` — the boolean value doubles as the compare key. | confirmed |
| +0x2C | 1 | bool | `on` | The single boolean toggle this state carries. Applied by the per-class slot-2 override. | confirmed |

---

## 5. Concrete subclass offset tables

All objects share the base layout (+0x00..+0x28) described in §4. Fields below begin at +0x2C
(the first byte after `state_type_id`) unless noted.

### Slot 0 — GRSAlphaTest

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 4 | int32 | `alpha_func` | `D3DCMPFUNC` for `D3DRS_ALPHAFUNC`. Value 8 (D3DCMP_ALWAYS) triggers the disable path instead of setting the func. |
| +0x30 | 4 | int32 | `alpha_ref` | Reference value for `D3DRS_ALPHAREF`. |

Apply: enables `D3DRS_ALPHATESTENABLE`, then sets `D3DRS_ALPHAFUNC` (+0x2C) and `D3DRS_ALPHAREF`
(+0x30). When `alpha_func` is 8 (ALWAYS), the apply disables the alpha-test instead.

### Slot 1 — GRSBlending

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 4 | int32 | `src_blend` | `D3DBLEND` value for `D3DRS_SRCBLEND`; passed through to the device. |
| +0x30 | 4 | int32 | `dest_blend` | `D3DBLEND` value for `D3DRS_DESTBLEND`. |
| +0x34 | 1 | bool | `blend_enable` | Feeds the alpha-blend-enable decision; combined with the Transparency override (slot 15) by `ApplyAlphaBlend`. |

Apply: writes `src_blend` and `dest_blend` to the device, then calls `ApplyAlphaBlend` which
enables alpha-blend when `blend_enable` is true **or** when `GRSTransparency` (slot 15) is active
(see §7).

### Slot 2 — GRSCullMode

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x24 | 4 | int32 | `compare_key` | Mirrors `mode`; used by the equals virtual (slot 4). |
| +0x2C | 4 | int32 | `mode` | Engine cull mode → `D3DRS_CULLMODE`: 0 → CW (D3DCULL_CW, 2); 1 → CCW (D3DCULL_CCW, 3); 2 → None (D3DCULL_NONE, 1). |

### Slot 3 — GRSDepthTest

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 4 | int32 | `z_func` | `D3DCMPFUNC` for `D3DRS_ZFUNC`. Value 20 → disable Z entirely (`D3DRS_ZENABLE = FALSE`). Value 8 (D3DCMP_ALWAYS) → enable Z without writing a func. All other values → enable Z and write the func. |

The `z_func` field stores a `D3DCMPFUNC` value verbatim; the default is 4 (`D3DCMP_LESSEQUAL`).
Value 8 (`D3DCMP_ALWAYS`) is special-cased by the apply to enable Z without writing the func.
Value 20 is an engine sentinel outside the `D3DCMPFUNC` range that disables Z entirely. A
construction-time helper remaps the `z_func` value to the `compare_key` (+0x24) via a fixed
9-entry table (D3DCMPFUNC 1–8 → normalized key; sentinel 20 → its own key); this is not
load-bearing for device output.

### Slot 4 — GRSTransformBase / GRSTransform

`GRSTransformBase` (id 4) is the abstract intermediate. `GRSTransform` is the concrete subclass
with its own extended vtable (adds slot 5). Instances are **pool-allocated**.

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 64 | float[16] | `matrix` | 4×4 world-transform matrix; pushed via `SetTransform` with `D3DTS_WORLD` (token 256). |
| +0x6C | 4 | int32 / bool | `normalize_normals` | Non-zero → enable `D3DRS_NORMALIZENORMALS` (143); zero → disable. |

Apply: calls `SetTransform` with `D3DTS_WORLD` (256) and a pointer to `matrix`, then writes
`D3DRS_NORMALIZENORMALS` (143) from `normalize_normals`. Total allocation: 112 bytes (0x70).

### Slot 5 — GRSDepthMask (GRSBoolean)

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 1 | bool | `z_write_on` | Desired `D3DRS_ZWRITEENABLE` value. |

Apply delegates to the shared `ApplyZWrite` helper. `ApplyZWrite` enables z-write only when
`GRSDepthMask.on` (+0x2C) is set **and** `GRSTransparency` (slot 15) is not active. See §7.

### Slot 6 — GRSShadeModel

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 4 | int32 | `shade_mode` | `D3DRS_SHADEMODE` value; passed through to the device shade-mode setter. |

### Slot 7 — GTexture

`GTexture` is a direct `GRenderState` subclass (own vtable, 60-byte allocation) with
`state_type_id` = 7. It is populated in the default render-state set alongside the other 17
GRS-family objects. The full `GTexture` field layout belongs in a dedicated struct doc; only the
field relevant to slot-dispatch is listed here.

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x34 | 4 | ptr | `texture_handle` | Handle to the bound texture. When non-null, apply calls the stage-0 texture setter on the device; when null, it calls the default clear setter. |

Fields +0x2C..+0x33 and +0x38..+0x3B belong to `GTexture`'s own refcount-wrapper layout and
are outside `GRenderState` family scope. The compare key (+0x24) reflects the handle identity.

### Slot 8 — GRSFillMode

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x24 | 4 | int32 | `compare_key` | Mirrors `mode`; used by the equals virtual. |
| +0x2C | 4 | int32 | `mode` | Engine fill mode → `D3DRS_FILLMODE`: 0 → SOLID (D3DFILL_SOLID, 3); 1 → WIREFRAME (D3DFILL_WIREFRAME, 2); 2 → POINT (D3DFILL_POINT, 1). |

### Slot 9 — GRSLighting (GRSBoolean)

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 1 | bool | `on` | True → `D3DRS_LIGHTING` = TRUE; false → disable lighting. |

### Slot 10 — GRSHighlight (GRSBoolean)

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 1 | bool | `on` | Carried in the object, but apply is a **no-op** in this build — no device write is performed. The slot is inert at device level. |

### Slot 11 — GRSMaterial

Holds a complete `D3DMATERIAL9` starting at +0x2C; apply passes `&this[0x2C]` directly to the
device `SetMaterial` call. The compare key is forced to −1 (always apply).

| Offset | Size | Type | Field | Role / constructor default |
|-------:|-----:|------|-------|---------------------------|
| +0x24 | 4 | int32 | `compare_key` | Always −1 (force re-apply every time). |
| +0x2C | 16 | float[4] | `diffuse` (RGBA) | D3DMATERIAL9.Diffuse; default (0, 0, 0, 1). The alpha component (diffuse.w at +0x38) doubles as the material alpha reported by the debug print. |
| +0x3C | 16 | float[4] | `ambient` (RGBA) | D3DMATERIAL9.Ambient; default (1, 1, 1, 1). |
| +0x4C | 16 | float[4] | `specular` (RGBA) | D3DMATERIAL9.Specular; default (0, 0, 0, 1). |
| +0x5C | 16 | float[4] | `emissive` (RGBA) | D3DMATERIAL9.Emissive; default (0.2, 0.2, 0.2, 1). |
| +0x6C | 4 | float | `power` | D3DMATERIAL9.Power; default 10.0. |

Total `D3DMATERIAL9` block occupies +0x2C through +0x6F (64 bytes + 4 bytes power = 68 bytes).

### Slot 12 — GRSDithering (GRSBoolean)

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 1 | bool | `on` | → `D3DRS_DITHERENABLE` boolean toggle. |

### Slot 13 — GRSColorMask

Four per-channel enable bytes; apply assembles a 4-bit write-enable mask
(R = bit 0 = 1, G = bit 1 = 2, B = bit 2 = 4, A = bit 3 = 8) and writes it to
`D3DRS_COLORWRITEENABLE` (168).

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x24 | 4 | int32 | `compare_key` / `disabled_mask` | Bitfield of disabled channels; used as the compare key. |
| +0x2C | 1 | bool | `write_r` | Red channel write enable. |
| +0x2D | 1 | bool | `write_g` | Green channel write enable. |
| +0x2E | 1 | bool | `write_b` | Blue channel write enable. |
| +0x2F | 1 | bool | `write_a` | Alpha channel write enable. |

### Slot 14 — GRSLinePattern

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 2 | uint16 | `repeat_factor` | Line-repeat factor (default 1; accepted range 2–0xFE). |
| +0x2E | 2 | uint16 | `pattern` | 16-bit line-stipple pattern. |

Apply is a **no-op** in this build — no device write is performed. The slot is effectively inert
(corresponds to the legacy `D3DRS_LINEPATTERN` concept, which is not meaningful in D3D9).

### Slot 15 — GRSTransparency (GRSBoolean)

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x2C | 1 | bool | `on` | Master transparency override; when true it forces alpha-blend on AND z-write off, overriding the settings in GRSBlending (slot 1) and GRSDepthMask (slot 5). |

Apply calls **both** `ApplyAlphaBlend` and `ApplyZWrite`. This is the cross-slot coupling
mechanism; see §7 for the full interaction matrix.

### Slot 16 — GRSFog

The compare key is forced to −1 (always apply). Mode 4 is the "unset/default" sentinel that
results in a no-op apply (neither enables nor disables fog).

| Offset | Size | Type | Field | Role / constructor default |
|-------:|-----:|------|-------|---------------------------|
| +0x24 | 4 | int32 | `compare_key` | Always −1 (force re-apply). |
| +0x2C | 4 | int32 | `mode` | Fog mode: 0 → disable fog; 1 → EXP (use `density`); 2 → EXP2 (use `density`); 3 → LINEAR (use `start`, `end`, `range_fog_enable`); 4 → no-op (default unset state). |
| +0x30 | 4 | D3DCOLOR | `color` | Fog colour in BGRA byte order: byte +0x30 = B, +0x31 = G, +0x32 = R, +0x33 = A. Default white (0xFF, 0xFF, 0xFF, 0xFF). |
| +0x34 | 4 | float | `start` | LINEAR fog start distance; default 0. |
| +0x38 | 4 | float | `end` | LINEAR fog end distance; default 8000.0. |
| +0x3C | 4 | float | `density` | EXP/EXP2 fog density; default 1.0. |
| +0x40 | 1 | bool | `range_fog_enable` | Range-fog flag for the LINEAR path (`D3DRS_RANGEFOGENABLE`). |

Apply dispatches by `mode`. The engine uses **vertex fog** (`D3DRS_FOGVERTEXMODE` = 140)
throughout; table fog is not used. Exact emit sequence by mode:

- Mode 0: `D3DRS_FOGENABLE` (28) = 0 (disable).
- Mode 1 (EXP): `D3DRS_FOGENABLE` (28) = 1; `D3DRS_FOGVERTEXMODE` (140) = 1; `D3DRS_FOGCOLOR` (34) = `color`; `D3DRS_FOGDENSITY` (38) = `density`.
- Mode 2 (EXP2): same as EXP with `D3DRS_FOGVERTEXMODE` (140) = 2.
- Mode 3 (LINEAR): `D3DRS_FOGENABLE` (28) = 1; `D3DRS_FOGVERTEXMODE` (140) = 3; `D3DRS_FOGCOLOR` (34) = `color`; `D3DRS_FOGSTART` (36) = `start`; `D3DRS_FOGEND` (37) = `end`; `D3DRS_RANGEFOGENABLE` (48) = `range_fog_enable`.
- Mode 4: no-op (neither enables nor disables fog; default unset sentinel).

### Slot 17 — GRSTFactor

| Offset | Size | Type | Field | Role |
|-------:|-----:|------|-------|------|
| +0x24 | 4 | int32 | `compare_key` / `active_mask` | −(`enable` != 0): 0 when disabled, −1 when enabled. Used as compare key. |
| +0x2C | 1 | uint8 | `factor_b` | Texture-factor blue byte (D3DCOLOR component). |
| +0x2D | 1 | uint8 | `factor_g` | Texture-factor green byte. |
| +0x2E | 1 | uint8 | `factor_r` | Texture-factor red byte. |
| +0x2F | 1 | uint8 | `factor_a` | Texture-factor alpha byte; constructor default 0xFF. |
| +0x30 | 1 | bool | `enable` | Enable flag; feeds `compare_key` (+0x24) only as `−(enable != 0)`. Does **not** gate the apply — all four device writes always execute. |

Apply always emits four device calls: `D3DRS_TEXTUREFACTOR` (60) = packed D3DCOLOR from
+0x2C; `SetTextureStageState`(stage 0, `D3DTSS_ALPHAARG1` = 5, `D3DTA_TEXTURE` = 2);
`SetTextureStageState`(stage 0, `D3DTSS_ALPHAARG2` = 6, `D3DTA_TFACTOR` = 3);
`SetTextureStageState`(stage 0, `D3DTSS_ALPHAOP` = 4, `D3DTOP_MODULATE` = 4). This wires
the stage-0 alpha pipeline to modulate texture alpha by the factor alpha on every frame.

---

## 6. 18-slot dispatcher and cache mechanics

The dispatcher is `GCull_ApplyRenderStateSet` — a force-commit routine invoked from the draw-
traverse stage `GCull_DrawAndFlush`. Its operation, from the `GRS*` object's perspective:

1. All 18 candidate-state pointers are snapshot-copied from the per-view cull accumulator (the
   "default render-state-set heads" table on `GCull`, documented at `structs/cull_pipeline.md`
   Table A offsets +396 and +468) into a flat working cache of 18 pointer slots.
2. For each of the 18 slots, the pointed-to `GRenderState` object's **apply virtual (vtable slot
   2, +0x08)** is called — unconditionally pushing every state to the device without a lazy
   compare. **The dispatcher performs no null-check before dereferencing**; all 18 slots must be
   populated (`GTexture` at id 7 ensures slot 7 is never null).

The lazy compare path (used outside the force-commit) calls the **equals virtual (slot 4)** for
each slot; it skips the apply when `state_type_id` and `compare_key` both match the cached values
(unless `compare_key` is −1, which always forces apply).

Two API functions populate the candidate table on the `GCull` accumulator (see
`structs/cull_pipeline.md` §5 for the full dispatch narrative):

- `GCull_SetGlobalRenderState` — stores the state pointer at its type-id slot and sets the
  "default-applied" bit. Used for scene-wide default states.
- `GCull_LinkRenderStateOverride` — stores the state pointer without setting the default bit.
  Used for per-drawable override states that the drawable contributes on top of the defaults.

---

## 7. Cross-slot couplings

Two shared helper functions arbitrate between state slots to produce the final device state. These
helpers are called from the apply virtuals of Blending (slot 1), DepthMask (slot 5), and
Transparency (slot 15):

**`ApplyAlphaBlend`** — enables `D3DRS_ALPHABLENDENABLE` when
`GRSBlending.blend_enable` (slot 1, +0x34) is true **or** `GRSTransparency.on` (slot 15, +0x2C)
is true. Both conditions independently trigger alpha-blend.

**`ApplyZWrite`** — enables `D3DRS_ZWRITEENABLE` only when `GRSDepthMask.on` (slot 5, +0x2C)
is true **and** `GRSTransparency.on` (slot 15, +0x2C) is false. A transparent draw item
therefore forces z-write off regardless of the DepthMask setting.

Summary matrix:

| Transparency (slot 15) | DepthMask (slot 5) | Blending (slot 1) | Z-write enabled | Alpha-blend enabled |
|:---:|:---:|:---:|:---:|:---:|
| false | true | any | yes | `blend_enable` |
| false | false | any | no | `blend_enable` |
| true | any | any | **no** | **yes** |

Both helper functions recompute the coupled device state from all three participating cache slots
on every call. The final device state therefore reflects `(blend_enable OR transparency.on)` and
`(depthmask.on AND NOT transparency.on)` regardless of which slot executed or in what order.
`GRSTransparency` acts as a true override without needing to execute after the other two;
the coupling is apply-order-independent.

This is the mechanism behind the per-bucket blend/z-write matrix described in
`specs/rendering.md` §4.2. Slots 10 (Highlight) and 14 (LinePattern) have no-op applies in this
build and do not interact with the coupling logic.

---

## 8. Open items

- **Slot 3 per-class variants** (Blending, AlphaTest, TFactor return small constants from vtable
  slot 3): the exact semantic of these predicate variants (a default-state predicate?) was not
  fully resolved and is not load-bearing for state semantics. Base returns 0.
- **GRSHighlight (slot 10) and GRSLinePattern (slot 14) field reads outside apply**:
  statically confirmed that both applies are true no-ops (no device write). No static reader of
  their fields outside the apply virtual was located. `[debugger-confirm]` — low value; live watch
  on both objects' field offsets to prove no runtime reader exists.
- **GRSFog runtime mode**: statically proven that ctor default is mode 4 (no-op) and all four
  active modes (0–3) are fully mapped with exact D3D9 keys (§5 Slot 16). `[debugger-confirm]` —
  the concrete mode value written into the live Fog object at +0x2C by the environment
  load path needs live session confirmation. Cross-check `specs/environment.md`.
- ~~**DepthTest `z_func` special values 20 and 8**~~ — **CLOSED** (deep-3d-cartography): `z_func`
  is a `D3DCMPFUNC` verbatim; 8 = `D3DCMP_ALWAYS` (enable Z, skip func write); 20 = engine
  sentinel outside D3DCMPFUNC range (disable Z). See §5 Slot 3.
- ~~**GRSTransform exact allocation sizes**~~ — **CLOSED** (deep-3d-cartography): all 17
  GRS-family sizes validated against default-factory allocations (see §9). Transform = 112 bytes
  (0x70); Material = 112 bytes (0x70); Fog = 68 bytes (0x44); AlphaTest / TFactor = 52 bytes
  (0x34); Blending = 56 bytes (0x38); all remaining = 48 bytes (0x30).
- **GRSTransform pool count/owner**: allocator stride confirmed static; the pool base, owning
  object, and live `count` value require a debugger session.
  `[debugger-confirm]` — cross-ref `structs/cull_pipeline.md` Table A offset +700.

---

## 9. Struct-size validation

Sizes validated against each class's default-factory allocation. Base is 0x2C (44 bytes).
Layout uses natural 4-byte alignment (not Pack=1); trailing padding after a 1/2-byte last field
is MSVC alignment padding, not a declared field. All 17 GRS-family sizes account exactly for
base + fields + alignment; no residual gap.

| id | Class | Allocation (bytes) | Last field (offset, size) | Verdict |
|---:|-------|-------------------:|--------------------------|---------|
|  0 | GRSAlphaTest    | 52 (0x34) | +0x30 `alpha_ref` (4)          | exact |
|  1 | GRSBlending     | 56 (0x38) | +0x34 `blend_enable` (1)       | +3 align pad |
|  2 | GRSCullMode     | 48 (0x30) | +0x2C `mode` (4)               | exact |
|  3 | GRSDepthTest    | 48 (0x30) | +0x2C `z_func` (4)             | exact |
|  4 | GRSTransform    | 112 (0x70) | +0x6C `normalize_normals` (4) | exact (pool allocator) |
|  5 | GRSDepthMask    | 48 (0x30) | +0x2C `on` (1)                 | +3 align pad |
|  6 | GRSShadeModel   | 48 (0x30) | +0x2C `shade_mode` (4)         | exact |
|  7 | GTexture        | 60 (0x3C) | +0x34 `texture_handle` (4)     | see §5 Slot 7 note |
|  8 | GRSFillMode     | 48 (0x30) | +0x2C `mode` (4)               | exact |
|  9 | GRSLighting     | 48 (0x30) | +0x2C `on` (1)                 | +3 align pad |
| 10 | GRSHighlight    | 48 (0x30) | +0x2C `on` (1)                 | +3 align pad |
| 11 | GRSMaterial     | 112 (0x70) | +0x6C `power` (4)             | exact |
| 12 | GRSDithering    | 48 (0x30) | +0x2C `on` (1)                 | +3 align pad |
| 13 | GRSColorMask    | 48 (0x30) | +0x2F `write_a` (1)            | exact |
| 14 | GRSLinePattern  | 48 (0x30) | +0x2E `pattern` (2)            | exact |
| 15 | GRSTransparency | 48 (0x30) | +0x2C `on` (1)                 | +3 align pad |
| 16 | GRSFog          | 68 (0x44) | +0x40 `range_fog_enable` (1)   | +3 align pad |
| 17 | GRSTFactor      | 52 (0x34) | +0x30 `enable` (1)             | +3 align pad |

`GTexture` (id 7) allocation is 60 bytes (0x3C); fields +0x2C..+0x33 and +0x38..+0x3B are
`GTexture`-internal layout outside GRS-family scope. See §5 Slot 7.

---

## 10. Cross-references

- Per-view cull object owning the 18-slot cache and dispatching apply: `structs/cull_pipeline.md`.
- Renderer-device object and global `GDevice` wrapper: `structs/renderer_device.md`.
- Per-view render parameters: `structs/gview.md`.
- Render-pass frame sequence, per-bucket z-write/blend matrix (§4.2), force-commit call site
  (§2.2, §4.1): `specs/rendering.md`.
- Render pipeline overview: `specs/render_pipeline.md`.
- Scene-graph class hierarchy (`GNode`, `GObject`, `GGeode`, `GTraverser`): `specs/scene_graph.md`.
- Glossary: `Docs/RE/names.yaml`.
- Provenance: `Docs/RE/journal.md`.
