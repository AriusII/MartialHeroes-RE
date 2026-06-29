# Spec: cloud texture selection + cross-fade (the `CloudDome` per-frame animation)

```
verification:   parser-verified (static) — the two `.bin` path templates, the 70-byte 10×7 cloud-cycle
                table, the column→layer/slot mapping, the row = date%10 rule, the 86400-second day
                bound, the per-layer frame counters (÷43200 / ÷21600), and the cross-fade scroll
                mechanism are all control-flow confirmed; the 7-column layout independently matches the
                on-disk `.txt` authoring mirror (CYCLE 13, `formats/sky.md §B.3`). SAMPLE-PENDING: the
                concrete cloud-id byte values in any real cloud_cycle{area}.bin are data, not in the
                binary; the exact atlas sub-rect geometry of the scroll is described at the mechanism
                level only.
ida_anchor:     f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence:       [static-ida, vfs-sample]
conflicts:      none-open
readiness:      IMPLEMENTATION-READY for a faithful two-layer scrolling-crossfade cloud reproduction.
```

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. This document describes the **render-time selection and animation** of the two
> cloud layers; the byte layouts it consumes are owned elsewhere and cross-referenced.
>
> **Scope split — read this first.**
> - The **`cloud_cycle%d.bin` byte table** (70 bytes = 10 rows × 7 columns) is authoritative in
>   `Docs/RE/formats/environment_bins.md §6` and enumerated in `Docs/RE/formats/sky.md §B.3`.
> - The **`clouddome%d.bin` per-vertex day-tint colour grid** (23040 bytes, two layers) is in
>   `environment_bins.md §5` and `sky.md §B.3`.
> - **This document adds** the per-frame rule that turns the cloud-cycle table + the in-game date and
>   time-of-day into the two displayed `cloud%d.dds` textures, plus the continuous cross-fade scroll
>   between consecutive cloud frames.

## 1. Files consumed

| Purpose | Path | `%d` is | Size / notes |
|---|---|---|---|
| Cloud texture | `data/sky/texture/cloud%d.dds` | the 1-byte cloud-id from the cloud-cycle row | colorkey `0xFF000000` (black → transparent); loaded via VFS |
| Cloud-cycle id table | `data/sky/dat/cloud_cycle%d.bin` | the active area / map id (plain `%d`, NOT 3-digit) | 70 bytes = 10 rows × 7 bytes — `environment_bins.md §6` |
| Cloud-dome colour grid | `data/sky/dat/clouddome%d.bin` | the active area / map id | 23040 bytes (two layers) — `environment_bins.md §5` |

- The texture path is exactly `cloud%d.dds` (NOT `clouds%d.dds`); the `%d` is the raw unsigned cloud-id
  byte read from the cloud-cycle table (e.g. id 7 → `data/sky/texture/cloud7.dds`).
- A missing `cloud{id}.dds` makes the surface load fail and aborts that cloud update; there is no
  graceful "no-cloud" sentinel in code. Whether a particular id maps to a deliberately-absent texture
  (clear sky) is a per-area **data** convention, not enforced by the binary (SAMPLE-PENDING).

## 2. Cloud-cycle row/column meaning (consumed here)

70 bytes = 10 records × 7 unsigned-8-bit columns. The active record is selected by the in-game date
(see §3); the 7 columns drive the two layers:

| Col | Field | Role | Layer |
|---:|---|---|---|
| 0 | `Speed` | per-row animation-rate multiplier `S` | both |
| 1 | `Cloud1[0–12h]` | cloud-id, layer-1 first half-day slot | layer 1 |
| 2 | `Cloud1[12–24h]` | cloud-id, layer-1 second half-day slot | layer 1 |
| 3 | `Cloud2[0–6h]` | cloud-id, layer-2 quarter slot 0 | layer 2 |
| 4 | `Cloud2[6–12h]` | cloud-id, layer-2 quarter slot 1 | layer 2 |
| 5 | `Cloud2[12–18h]` | cloud-id, layer-2 quarter slot 2 | layer 2 |
| 6 | `Cloud2[18–24h]` | cloud-id, layer-2 quarter slot 3 | layer 2 |

Layer 1 reads columns {1, 2}; layer 2 reads columns {3, 4, 5, 6}; column 0 is the shared rate
multiplier. This matches the `sky.md §B.3` / `environment_bins.md §6` column layout (HIGH).

## 3. Time inputs

Two env-time globals are set when an area loads:

- **`date_block`** — an integer in-game date / day counter, range `[0, 360000)` (wraps by subtracting
  360000). Selects the cloud-cycle **row**: `row = date_block % 10` — effectively a 10-pattern rotation
  keyed on the in-game date (the 10 rows index "0 Day" .. "9 Day").
- **`time_of_day` (TOD)** — bounded to `<= 86400`. A full game day is **86400 units = seconds-of-day**;
  one unit is one game-second of a 24-hour day. Therefore **43200 = 12:00 (midday)** and
  **21600 = 06:00**. Hour from TOD: `hour = TOD / 3600` (0..24).

> The 86400 bound proves TOD is a 0..86400 seconds-of-day counter, NOT milliseconds (an internal
> millisecond-suffixed name in the binary was misleading). This is the same 86400-second day length as
> the 48-slot sky-colour cycle (`sky.md §C`) and the sun/moon orbit (`sky.md §D.1`).

## 4. Per-frame texture selection

Let `S = cloud_cycle[row][0]` (the Speed byte) and `row = date_block % 10`.

### 4.1 Layer 1 (slower, inner; 2 columns / 12-hour slots)

- Frame counter `f1 = floor( TOD / 43200 * S )` (integer).
- Re-selects only when `f1` changes (crosses an integer boundary). On change:
  - if `2*S > f1 + 1`: column `c1 = (f1 + 1) mod 2`, row unchanged.
  - else: column `c1 = 0`, row advances to `(row + 1) mod 10`.
  - displayed id = `cloud_cycle[row][1 + c1]` (one of columns 1, 2).
- With `S = 1` the day sequence is: `Cloud1[0–12h]` for the first half-day, then `Cloud1[12–24h]` for
  the second, carrying into the next row's `Cloud1[0–12h]` at the day boundary.

### 4.2 Layer 2 (faster, outer; 4 columns / 6-hour slots)

- Frame counter `f2 = floor( TOD / 21600 * S )` (integer).
- Re-selects only when `f2` changes. On change:
  - if `4*S > f2 + 1`: column `c2 = (f2 + 1) mod 4`, row unchanged.
  - else: column `c2 = 0`, row advances to `(row + 1) mod 10`.
  - displayed id = `cloud_cycle[row][3 + c2]` (one of columns 3, 4, 5, 6).
- With `S = 1` the day cycles through the four 6-hour `Cloud2` columns, carrying into the next row at
  the day boundary.

The area-load init seeds BOTH the current and the next frame for each layer using the same formulas
with the TOD at load time.

## 5. Cross-fade between consecutive frames (continuous scroll, not an alpha dissolve)

There is a genuine per-frame cross-fade between each layer's CURRENT cloud frame and its NEXT cloud
frame, implemented as a continuous **texture-coordinate scroll**, not a discrete alpha dissolve:

- Each layer keeps a small composite / atlas surface holding the CURRENT frame in one sub-rect and the
  (already pre-loaded) NEXT frame in another. When the frame counter ticks, the previous "next" becomes
  "current" and a fresh "next" `cloud{id}.dds` is loaded — an A/B ping-pong.
- A scroll / blend factor advances continuously and is added to a stored base U coordinate of every
  cloud vertex each frame (60 vertices updated per frame), sweeping the visible clouds from the current
  frame toward the next:
  - Layer-1 blend = `(TOD * S mod 43200) * 0.5 / 43200` → ranges 0 → 0.5 across each `12h / S` interval.
  - Layer-2 blend = `2 ×` the layer-1 blend, wrapped (subtract 0.5 when `>= 0.5`) → 0 → 0.5 across each
    `6h / S` interval. Layer 2 scrolls at twice layer 1's rate.
- Net effect: two superimposed cloud layers, each smoothly scrolling and morphing from one cloud-cycle
  id to the next over its slot, layer 2 moving twice as fast as layer 1.

The per-vertex day-tint colour applied to these scrolling vertices is the separate 48-slot
`clouddome%d.bin` grid (`sky.md §B.3` / `environment_bins.md §5`); the `WRAP` sampler addressing the
sky pass sets (`sky.md §E.2`) tiles the scroll seamlessly.

## 6. Open / pending

- **SAMPLE-PENDING:** the concrete cloud-id byte values in any real `cloud_cycle{area}.bin` are data,
  not in the binary; whether any id deliberately maps to an absent texture (clear sky) is a per-area
  data convention.
- The exact composite-atlas sub-rect geometry (how the 0..0.5 U scroll maps current → next within the
  atlas) is described at the mechanism level; the precise atlas rectangles were not byte-dumped (not
  needed for a faithful scroll-crossfade reproduction). Debugger-confirmable against a live area if a
  pixel-exact atlas layout is later required.
