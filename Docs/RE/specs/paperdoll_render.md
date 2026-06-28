# Spec: Paperdoll Render — Character Preview in 2D UI Panels

> Clean-room spec. Neutral description only — no decompiler pseudo-code, no binary addresses,
> no decompiler-generated identifiers. Promoted from dirty-room static analysis under EU
> Software Directive 2009/24/EC Art. 6, solely to achieve interoperability. This spec
> documents the **character preview ("paperdoll") render mechanism** used by the inventory/equip
> panel and the character-info panel, and the structurally distinct character-select lineup path
> for contrast. The definitive finding is that **no private render target exists** for any
> character preview in the binary.
>
> The fixed-function cel draw routine consumed here is documented in
> `Docs/RE/specs/character_rendering.md §3.2 / §5.1`. The post-process chain that owns all
> render-to-surface surfaces is documented in `Docs/RE/specs/post_processing.md §2`. The main
> scene draw order is in `Docs/RE/specs/render_pipeline.md §5`.
>
> Every paperdoll constant cited in C# must reference this file:
> `// spec: Docs/RE/specs/paperdoll_render.md`.

> **Verification banner**
> - **verification:** *static-confirmed* for all findings in this document, recovered by
>   exhaustive enumeration of every D3DX render-surface caller, every camera-matrix caller, the
>   viewport wrapper, and the two panel `onDraw` implementations (`ItemPanel__onDraw`,
>   `OtherInfo__onDraw`). Items tagged `[debugger-confirm]` are static hypotheses awaiting a
>   live `?ext=dbg` session before treating them as implementation facts.
> - **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> - **readiness:** IMPLEMENTATION-READY for the definitive-negative finding (no private RTT),
>   the HUD ortho projection parameters, screen-pixel placement formula, per-class X offsets,
>   render-state recipe steps, fixed-function cel draw contract, and the char-select world-space
>   placement offset and scale. The depth-bias runtime value (step 4 of §6), actor field `+100`
>   role (§3.1), and char-select camera setup (§7) are `[debugger-confirm]` — NON-BLOCKING for
>   a first port but required before final fidelity tuning.
> - **evidence:** [static-ida] for all RTT/camera accounting, both panel draw paths, and the
>   char-select world-space placement; `[debugger-confirm]` for depth-bias runtime value, actor
>   `+100` role, and char-select scene camera/view setup.
> - **cross-links:**
>   `Docs/RE/specs/character_rendering.md` (fixed-function UI cel path §3.2/§5.1;
>   in-world programmable per-part path §5.2);
>   `Docs/RE/specs/post_processing.md §2` (the four RTT surfaces accounting for all
>   `D3DXCreateRenderToSurface` callers — shadow + three glow — exhausting the caller list);
>   `Docs/RE/specs/render_pipeline.md §5` (main scene view reused by char-select lineup;
>   vtable key at §11);
>   `Docs/RE/specs/rendering.md` (HUD ortho state context, cel gating §5.1a).

---

## Status

| Item | Confidence |
|---|---|
| No private RTT / no dedicated camera / no sub-rect viewport for any paperdoll | CONFIRMED |
| All `D3DXCreateRenderToSurface` callers accounted for (1 shadow + 3 glow, none preview) | CONFIRMED |
| `D3DXMatrixLookAtLH` and `D3DXMatrixPerspectiveFovLH` — each has exactly one caller (shadow projector) | CONFIRMED |
| `GDevice_SetViewport` — exactly one caller (main scene draw path) | CONFIRMED |
| HUD ortho projection: left 0 / right screenW / bottom screenH / top 0 / near −300 / far +300 | CONFIRMED |
| Identity view and world transforms under ortho | CONFIRMED |
| Screen-pixel placement: panel origin + per-class X offset, +400 Y, Z 200 | CONFIRMED |
| Per-class X offsets {172 / 141 / 144 / 155, default 160} | CONFIRMED |
| Billboard facing: `XEffect_EulerToBillboardDir(π, 0, 0)` | CONFIRMED |
| Draw via `Character_DrawSkinnedCelShaded` (fixed-function UI cel path, not programmable) | CONFIRMED |
| Render-state wrap: cull-CW, Z-enable/Z-write-enable, D3DRS_DEPTHBIAS (195) set and reset | CONFIRMED (static) |
| D3DRS_DEPTHBIAS (195) runtime float bias value from panel field | `[debugger-confirm]` |
| FVF restored to 0x144 after draw | CONFIRMED |
| Per-frame trigger: panel `onDraw` vtable slot 7, only while open and visible | CONFIRMED |
| `ItemPanel__onDraw` per-frame bob / auto-spin idle animation | CONFIRMED |
| Actor spawn sets field `+100 = 30.0f` during preview configuration | CONFIRMED (static) |
| Role of actor `+100 = 30.0f` (scale, setup parameter, or other) | `[debugger-confirm]` |
| Char-select lineup: world-space offset (−1536.5, +0.0, −3538.0) from anchor, scale 80 | CONFIRMED |
| Char-select lineup renders through main perspective scene (no RTT) | CONFIRMED |
| Char-select scene camera / view setup | `[debugger-confirm]` (not traced this pass) |

---

## 1. Overview — Definitive Negative on Private Render Target

**Confidence: CONFIRMED.**

The character preview shown inside the inventory equip panel ("ItemPanel") and the character-info
panel ("OtherInfo") is **not** rendered to an offscreen surface. There is **no private render
target, no dedicated preview camera matrix, and no sub-rect viewport** for any character
paperdoll in the binary. Both panels draw the live 3D character directly into the **main back
buffer**, inside their own 2D `onDraw` callback, using the **HUD orthographic screen-pixel
projection** already active for 2D widget drawing. The character is positioned by screen pixel
coordinates, rendered via the shared fixed-function cel draw routine, and device state is
restored so the panel's remaining 2D widgets draw on top.

A re-implementation that allocates a separate render target for the paperdoll, or that
establishes a dedicated preview camera, diverges from the original. The correct approach is to
draw the skinned character in-place during the panel's 2D draw, under the HUD ortho projection,
with the render-state recipe documented in §6.

A **structurally different** preview path exists for the character-select and character-creation
screens: it places actors in **world space** in front of the world camera and lets the normal
perspective scene draw render them — also with no private render target, reusing the main scene
view. See §7 for details.

In summary, both preview kinds avoid any offscreen target:

| Preview context | Projection | Placement | RTT |
|---|---|---|---|
| Inventory equip / character-info panels | HUD ortho, screen pixels | Panel origin + per-class X offset, +400 Y, Z 200 | None — back buffer |
| Character-select / character-creation lineup | Main scene perspective | World space: anchor + (−1536.5, +0.0, −3538.0), scale 80 | None — back buffer |

---

## 2. Proof: No Dedicated RTT, Camera, or Viewport

**Confidence: CONFIRMED.**

Every D3DX render-surface helper and camera-matrix helper in the binary was enumerated and every
caller was identified; none belongs to any character preview:

| D3DX helper (IAT import) | Total callers | Callers and roles | Preview? |
|---|---|---|---|
| `D3DXCreateRenderToSurface` | 4 | `ShadowManager_Init` × 1 (shadow projector RT); `Renderer_InitCelGlowShaders` × 3 (glow RT0 / RT1 / RT2 — see `post_processing.md §2`) | NO |
| `D3DXMatrixLookAtLH` | 1 | Shadow projector view matrix only | NO |
| `D3DXMatrixPerspectiveFovLH` | 1 | Shadow projector projection matrix only | NO |

There are **exactly 4** `D3DXCreateRenderToSurface` surfaces across the entire client — one
shadow projector RT and three glow render targets, all documented in `post_processing.md §2`.
No fifth surface is allocated for any character preview. `D3DXMatrixLookAtLH` and
`D3DXMatrixPerspectiveFovLH` each have **exactly one** caller (the shadow projector), so the
paperdoll has no dedicated view matrix and no dedicated perspective projection.

`GDevice_SetViewport` — the single viewport-setting wrapper in the binary — has **exactly one**
caller: `Renderer_DrawScene_Direct` (the main scene draw path). No paperdoll panel sets a
sub-rect viewport; all panels share the full back-buffer viewport.

No string literal "paperdoll" or "preview" is present in the binary. The panel functions were
located by their canonical names in the CYCLE-14 annotated IDB.

---

## 3. UI Panel Architecture — Two Host Panels

**Confidence: CONFIRMED.**

Two panels host a character paperdoll using an identical draw mechanism:

| Panel | Draw callback | Vtable slot | Preview-actor field | Per-frame guard |
|---|---|---|---|---|
| Inventory / equip ("ItemPanel") | `ItemPanel__onDraw` | 7 | Panel struct `+2504` (actor pointer) | `if (!buffer \|\| !this+140) return;` |
| Character-info ("OtherInfo") | `OtherInfo__onDraw` | 7 | Panel struct `+608` (actor pointer) | `if (!this+772 \|\| !this+140) return;` |

Both callbacks follow the same pattern:

1. Call `Hud_BeginOrthoRender` (sets the HUD ortho device state — see §4).
2. Draw the panel's 2D widget contents.
3. If the preview actor pointer is non-null, execute the screen-space character draw block (§5–§6).
4. Continue with any remaining 2D widgets.

There is no separate per-frame "render paperdoll" pass. The draw fires **only while the panel is
open and visible**, because the `onDraw` callback is only dispatched for open, visible panels.
Closing the panel stops the preview draw entirely.

### 3.1 Preview-Actor Lifecycle

- **ItemPanel:** on the first `onDraw` call before a preview actor exists, `ItemPanel_BuildPreviewActorSetStates` spawns and configures the actor. Subsequent calls reuse the cached pointer at `+2504`.
- **OtherInfo:** `ActorPreview_CreateAndConfigureRender`, reached via `ActorPreview_SpawnByIdForWindow`, handles spawn and configuration.

Both paths spawn a real actor through `ActorManager_SpawnActorFromDescriptor` into the shared
`ActorVisualGlobal` singleton — the same actor system used by world actors. After spawning, idle
motion is applied via `Actor_ApplyIdleMotion_Direct`, weapon/joint effects are refreshed, and a
fixed-function multitexture/sampler state block is pre-loaded. During spawn, actor field
`+100` is set to `30.0f`; the exact role of this value (scale factor, setup enumeration, or
other) is `[debugger-confirm]`.

---

## 4. HUD Orthographic Projection

**Confidence: CONFIRMED.**

`Hud_BeginOrthoRender` configures the following device state before any panel `onDraw` executes.
This projection is shared by all 2D panel drawing; the paperdoll character is drawn inside it
without any projection switch.

**Projection matrix** — `D3DXMatrixOrthoOffCenterLH` with:

| Parameter | Value | Source |
|---|---|---|
| left | 0.0 | literal |
| right | screen width (float) | global display width |
| bottom | screen height (float) | global display height |
| top | 0.0 | literal |
| zNear | −300.0 | literal |
| zFar | +300.0 | literal |

This is a top-left-origin, screen-pixel orthographic space with a depth range of [−300, +300].
The matrix is applied via `SetTransform(D3DTS_PROJECTION)` (D3DTS_PROJECTION = 3).

**View and world transforms** — both set to identity: `SetTransform(D3DTS_VIEW)` (D3DTS_VIEW = 2)
and `SetTransform(D3DTS_WORLD)` (D3DTS_WORLD = 0).

**Additional render state set by `Hud_BeginOrthoRender`:** Z-buffer enable; cull mode CW (value 2);
lighting enabled; fill mode SOLID (value 3); alpha-blend off.

With this state active, a 3D skinned character placed at screen-pixel coordinates (§5) is
projected orthographically — no perspective foreshortening — and appears at its true pixel size
relative to the panel rectangle.

---

## 5. Screen-Pixel Actor Placement

**Confidence: CONFIRMED.**

Immediately before the cel draw call, each panel writes the actor's world-position fields.
Because the HUD ortho projection maps screen pixels directly to clip space, the actor position
is expressed in screen pixels:

| Component | Actor field | ItemPanel | OtherInfo |
|---|---|---|---|
| X | `+1064` | panel origin X (`this+20`) + per-class X offset (switch table) | panel origin X (`this+20`) + per-class X offset |
| Y | `+1068` | panel origin Y (`this+24`, class-adjusted) + 400.0 | panel origin Y (`this+24`) + 400.0 |
| Z | `+1072` | 200.0 | 200.0 |
| Facing | `+1076` | `XEffect_EulerToBillboardDir(π, 0, 0)` — billboard toward camera | same |

Z = 200.0 places the actor in the forward half of the HUD ortho depth range (near = −300,
far = +300). The +400.0 Y offset places the figure in the lower region of the panel.

### 5.1 Per-Class X Offsets

The X offset is selected by actor class field `+168`:

| Class (field `+168`) | X offset — ItemPanel | X offset — OtherInfo |
|---|---|---|
| default | 157.0 | 160.0 |
| 1 (Musa) | 172.0 | 172.0 |
| 2 (Salsu) | 141.0 | 141.0 |
| 3 (Dosa) | 144.0 | 144.0 |
| 4 (Monk) | 155.0 | 155.0 |

These offsets horizontally center each class's body within the panel; each character proportional
type has a different silhouette width and requires a different anchor.

---

## 6. Render-State and Draw Recipe

**Confidence: CONFIRMED (static). Depth-bias runtime value is `[debugger-confirm]`.**

The following sequence executes inside `ItemPanel__onDraw` / `OtherInfo__onDraw`, after
`Hud_BeginOrthoRender` and the 2D widget draw, only when the preview actor pointer is non-null:

| Step | Operation | D3D9 parameter |
|---|---|---|
| 1 | Set cull mode CW | D3DCULL_CW = 2 |
| 2 | Enable Z write | D3DRS_ZWRITEENABLE (14) = 1 |
| 3 | Enable Z test | D3DRS_ZENABLE (7) = 1 |
| 4 | Set depth bias | D3DRS_DEPTHBIAS (195) = biasValue from panel field (ItemPanel `+2760` / OtherInfo `+864`) `[debugger-confirm: runtime float value]` |
| 5 | Write actor position (§5) and facing (π) | actor fields `+1064` (float3) and `+1076` |
| 6 | `Character_DrawSkinnedCelShaded(actor)` | fixed-function UI cel path — see §6.1 |
| 7 | Reset depth bias to zero | D3DRS_DEPTHBIAS (195) = 0 |
| 8 | Enable Z test; disable Z write | D3DRS_ZENABLE (7) = 1; D3DRS_ZWRITEENABLE (14) = 0 |
| 9 | Restore 2D HUD vertex format | `SetFVF(0x144)` = D3DFVF_XYZRHW \| D3DFVF_DIFFUSE \| D3DFVF_TEX1 |

Step 9 restores the 2D transformed-vertex HUD format so any subsequent panel widgets drawn after
the paperdoll use the correct vertex layout.

### 6.1 Character_DrawSkinnedCelShaded — Fixed-Function UI Cel Path

`Character_DrawSkinnedCelShaded` is the **fixed-function** UI cel draw routine documented in
`character_rendering.md §3.2 / §5.1`. It is **distinct** from the in-world programmable per-part
path (`Actor_DrawSkinnedCelWithTint`, invoked via the actor's data-table vtable, documented in
`character_rendering.md §5.2`). The paperdoll explicitly uses the fixed-function variant; no
programmable vertex or pixel shader is bound during this draw.

The routine performs:

- **Pose build and CPU deform:** calls `AnimMixer_BuildPose`, evaluates the current animation
  pose, then runs CPU linear-blend skinning deform and vertex upload (`SkinSet_DeformAndUpload`).
- **Multitexture stage setup:** stage 0 `COLOROP = MODULATE` with its default arguments; stage 1
  `COLORARG1` is selected by a global display toggle — `D3DTA_TEXTURE` when the toggle is set,
  `D3DTA_CURRENT` when unset; stage 2 disabled.
- **Sampler setup:** sampler 0 MIN / MAG / MIP = LINEAR (2), ADDRESS = WRAP (1); sampler 1
  ADDRESS = WRAP (1).
- **Sub-mesh draw loop:** iterates the actor's sub-meshes, drawing each via the fixed-function
  draw slot of the element's vtable.
- **Blend state:** `D3DBLEND_SRCALPHA` (5) / `D3DBLEND_INVSRCALPHA` (6); alpha-blend, alpha-test,
  Z, lighting, dither, and fog configured per the fixed-function UI cel contract.
- **Restore:** texture stage state restored on exit.

The programmable cel shaders (`dotoonshading.vsh`, `dotoonshading.psh`, `dotoonshading2.psh`,
documented in `post_processing.md §3`) are **not** bound in this path.

### 6.2 ItemPanel Idle Bob and Auto-Spin

`ItemPanel__onDraw` applies a per-frame idle animation to the preview actor between steps 4 and 6:

- **Positional bob:** a ±2.0-unit accumulator (panel field `+2772`, direction flag `+2776`) nudges
  the actor position each frame, clamped at +25.0 / −10.0. This produces a gentle floating motion.
- **Auto-spin:** an optional yaw rotation derived from a small per-millisecond increment is applied
  to the actor facing before the draw, producing a slow rotate of the equipped figure.

`OtherInfo__onDraw` does not apply either animation; its preview actor is stationary.

---

## 7. Character-Select / Character-Creation Lineup (World-Space Path)

**Confidence: CONFIRMED for placement offset and scale; char-select camera setup is `[debugger-confirm]`.**

The character-select and character-creation screens use a structurally different preview
mechanism. Actors are placed in **world space** relative to a camera anchor and rendered through
the **main perspective scene view** — no HUD ortho projection, no offscreen surface.

Key recovered facts:

| Property | Value |
|---|---|
| World-space offset from camera anchor | (−1536.5, +0.0, −3538.0) units (actor field `+1064`) |
| Scale | 80.0 (actor fields `+1160` / `+1164`) |
| Facing | Euler yaw via `XEffect_EulerToBillboardDir`, stored in actor `+1076`, copied to `+1116` |
| Render path | Normal world perspective scene draw (main scene view, `render_pipeline.md §5`) |
| Offscreen target | None |

Multiple lineup actors are spawned via `SelectWindow_SpawnPreviewLineup` with per-class
descriptors including preview-only starter gear. The currently selected actor is rotated each
frame by `SelectWindow_TickSelectedPreviewYaw` (small yaw increment per elapsed time).

The **scene camera** (the view and projection matrices the lineup renders under) was not traced
in this pass and is `[debugger-confirm]`. The camera anchor that the world-space offset above is
relative to is also unresolved. Entry points for a follow-up RE pass:
`SelectWindow_BuildZoomPreviewActor`, `SelectWindow_SpawnPreviewLineup`,
`CharSelect_OpenPreviewWindow`, `SelectSlot_BeginPreviewOrConfirm`.

---

## 8. Debugger-Confirm Items

| # | Item | What to confirm |
|---|---|---|
| 1 | D3DRS_DEPTHBIAS (195) runtime float bias value | While the paperdoll draw step is active, read panel field `ItemPanel +2760` / `OtherInfo +864` and confirm the float value fed to `SetRenderState(195, …)`. |
| 2 | Actor `+100 = 30.0f` role | Trace the spawn-configuration call within `ActorManager_SpawnActorFromDescriptor` that writes `+100 = 30.0f`; determine whether this is a scale factor, a setup-mode enumeration, or a different property. |
| 3 | Char-select scene camera / view setup | Trace `CharSelect_OpenPreviewWindow` or `SelectSlot_BeginPreviewOrConfirm` to recover the view and projection matrices used for the character-select lineup, and identify the world-space anchor that the placement offset (§7) is relative to. |

---

## 9. Open Questions

1. **Global display toggle driving `Character_DrawSkinnedCelShaded` stage-1 `COLORARG1`:** a
   global boolean selects between `D3DTA_TEXTURE` and `D3DTA_CURRENT` for multitexture stage 1
   during the cel draw. What configuration option or runtime condition writes this flag has not
   been determined. An RE pass tracing the flag's write sites would resolve this. The option may
   correspond to a `display.lua` entry or a runtime graphics-quality setting.

2. **Other panels with 3D preview:** only `ItemPanel` and `OtherInfo` were found to call
   `Character_DrawSkinnedCelShaded` from an `onDraw` callback. Whether other panels (for example,
   shop or NPC-dialog panels) host a 3D preview could be confirmed by a broader sweep of all
   vtable-slot-7 `onDraw` implementations in the UI class hierarchy.
