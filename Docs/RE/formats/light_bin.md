# Format: `light%d.bin`  (per-area directional / ambient / star-brightness light table — flat memory image)

```
verification:   parser-verified (static) — flat verbatim read + same-object per-frame consumer + a
                fully-accounted 5312-byte image make every file offset deterministic. SAMPLE-PENDING:
                no on-disk light%d.bin byte sample was inspected; an implementer should confirm a real
                data/sky/dat/light{area}.bin is exactly 5312 bytes (0x14C0).
ida_anchor:     f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
evidence:       [static-ida]
conflicts:      none-open
readiness:      IMPLEMENTATION-READY for the C# rebuild of the day-cycle star-brightness sampler; the
                missing-file synth fallback is a never-fail default and is documented in §4.
```

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code,
> NO binary addresses. Consumed by `Assets.Parsers`. Every offset an engineer cites must reference
> this file.
>
> **Scope.** `light%d.bin` is the per-area light keyframe table. This document specifies its on-disk
> byte layout — in particular the **48-entry star-brightness day curve** that drives the night-sky
> star visibility — and the per-frame sampling math that consumes it. The 48-slot day cycle, the star
> per-vertex tint, and the grayscale `TEXTUREFACTOR` modulation are cross-referenced to
> `Docs/RE/formats/sky.md §B.4 / §C` and `Docs/RE/formats/environment_bins.md §4 / §10`.

## Identification

- **Logical path:** `data/sky/dat/light<area_id>.bin`, where `<area_id>` is the active integer area id
  (plain decimal, not zero-padded). Resolved through the central sky path-template table
  (`formats/sky.md §B.1a`).
- **Found in:** the `.pak` archive / VFS.
- **Endianness:** little-endian throughout.
- **Magic / version:** none — file identity is the path; field meaning comes from the loader.
- **File size:** **5312 bytes (0x14C0)**, fixed.

---

## 1. The whole file is a flat memory image (no per-field parse)

The loader performs a **single verbatim read of the entire 5312-byte file** into one contiguous
manager block; there is **no per-field decode at load time**. The save path (the area editor) writes
the same block back verbatim, confirming a flat dump round-trip. Consequently the file IS the
in-memory light table byte-for-byte, and every field below is addressed by its **file offset** (which
equals the offset within the manager block). The per-frame consumer (§3) is the only code that
interprets individual fields.

## 2. File layout (every byte of the 5312 accounted for)

| File offset | Size | Shape | Field |
|---:|---:|---|---|
| 0x0000 (0) | 2304 | 48 × 48-byte records | directional / diffuse keyframe colours (one vec3 at record `+0x00`, one at `+0x10`; the `+0x20` band is loaded but unconsumed) |
| 0x0900 (2304) | 48 | 48 × u8 | directional keyframe skip / disable flag, one per slot |
| 0x0930 (2352) | 2304 | 48 × 48-byte records | ambient keyframe colours |
| 0x1230 (4656) | 48 | 48 × u8 | ambient keyframe skip / disable flag, one per slot |
| 0x1260 (4704) | 192 | 48 × f32 | per-slot scalar (specular / attenuation magnitude; default 80.0) |
| **0x1320 (4896)** | **192** | **48 × f32** | **`starBrightnessCurve[48]` — the star-brightness day curve (§3)** |
| 0x13E0 (5088) | 192 | 48 × 4-byte | per-slot ambient RGB tint: bytes `+0` / `+1` / `+2` used, `+3` padding |
| 0x14A0 (5280) | 32 | 8 × dword | trailer: two cleared dwords, a light-direction vec3, then a vec3 copy |

Sum = 5312 = 0x14C0. The star-brightness table is **bracketed on both sides** — it starts where the
per-slot scalar table ends (0x1320) and ends where the ambient RGB tint table starts (0x13E0) — so its
offset is pinned independently from above and below.

## 3. `starBrightnessCurve[48]` — the star-brightness day curve (the load-bearing field)

- **File offset 0x1320 (4896), 48 × f32 little-endian, stride 4, spanning bytes [0x1320, 0x13E0).**
- One brightness scalar per **1800-second day slot** (the 48-slot day cycle — `formats/sky.md §C`).

### 3.1 Per-frame sampling

Each frame the per-frame light apply samples this table with the shared 48-slot day decomposition:

```
slot      = floor(time_ms / 1800)                 # 1800 s per slot
nextSlot  = (slot + 1 < 48) ? slot + 1 : 0        # wrap at 48
frac      = (time_ms mod 1800) / 1800             # intra-slot fraction, 0..1
brightness = (starBrightnessCurve[nextSlot] - starBrightnessCurve[slot]) * frac
           + starBrightnessCurve[slot]            # linear lerp between adjacent slots
```

### 3.2 Visibility cutoff (0.1)

- If `brightness >= 0.1` the star dome is drawn; if `brightness < 0.1` it is removed from the draw
  path entirely. Daytime slots (brightness near 0) therefore drop the stars out completely — no dusk
  cross-fade lag.

### 3.3 Grayscale `TEXTUREFACTOR` modulation

- The interpolated brightness is committed and converted to a device value `round(brightness * 255.0)`,
  written into **all three RGB channels equally** (a uniform grayscale) of the star-dome's
  `D3DRS_TEXTUREFACTOR` colour, alpha left opaque. This is the global brightness tier; the per-vertex
  amber star colour is the separate tint tier in `formats/sky.md §B.4` / `environment_bins.md §4`. See
  `formats/sky.md §B.4` for the two-tier brightness model (per-vertex tint × global brightness).

---

## 4. Missing-file fallback — synthesised triangle default

When `light%d.bin` is **absent** for the area, the loader does not fail: a wrapper synthesises 48
keyframes in place and writes the brightness table directly, 48 iterations stride 4:

```
starBrightnessCurve[i] = 1.0 - 0.04 * ( (i > 24) ? (48 - i) : i )
```

- This is a **triangle**: `[0] = 1.0`, decreasing 0.04 per slot to `[24] = 0.04` (the midday dip, below
  the 0.1 cutoff → stars hidden), then rising to `[47] = 0.96`. Night bright → day dim → night bright.
- The same fallback also sets the per-slot scalar table (0x1260) to 80.0, clears the directional and
  ambient skip-flag bytes, and seeds a fallback light-direction vec3 in the trailer.
- This synthesis runs **only** when the file is missing; when the file exists it is read verbatim (§1).
  The static fixed directional-light fallback `(-7, 7, 20)` documented in `environment_bins.md §10.6`
  is part of the same never-fail default path.

---

## 5. Open / pending

- **SAMPLE-PENDING:** no on-disk `light%d.bin` byte sample was inspected; confirm a real
  `data/sky/dat/light{area}.bin` is exactly 5312 bytes and that the 0x1320 band holds plausible
  per-slot brightness scalars on a representative area.
- The `+0x20` band of each directional keyframe record (§2, 0x0000 table) is loaded but not consumed by
  the per-frame path traced here; its role is unresolved.
