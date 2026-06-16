# Format: .psh / .vsh  (Direct3D 9 shader assembly source text)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers / Assets.Mapping. Every offset an engineer cites must reference this file.
>
> **verification:** sample-verified (two-witness) — the plain-ASCII D3D9-assembly-text format, the
> `vs.1.1` / `ps.1.1` version line, the CRLF line endings, the VFS-or-disk load path (assembler flags
> = 0 at every site), the five-shader assemble order, the three render targets, and the `toonramp.bmp`
> stage-1 LUT were all re-confirmed against the live cel/glow initialiser AND a byte-walk of real shader
> files (`dotoonshading.vsh`, `power1dx8.psh`, `toonramp.bmp`). The `c4`–`c10` cel constants (incl. the
> BT.601 `c9`) were NOT re-decoded this pass — they carry forward from the prior bit-pattern confirmation.
> **ida_reverified:** 2026-06-16
> **ida_anchor:** 263bd994
> **evidence:** [static-ida, vfs-sample]
> **conflicts:** NONE — no committed claim is contradicted by the loader or the real sample on this build.
> The three unextracted shader sources (`dotoonshading.psh`, `dotoonshading2.psh`, `finaldx8.psh`) and
> the exact `toonramp.bmp` pixel band layout remain the only open items (see Known Unknowns); the ramp's
> 824-byte file size on this build corroborates the ~256×1×24bpp estimate.
>
> **spec_status:** sample_verified (4 samples cross-confirmed; two-witness re-confirmed build 263bd994)
> **date:** 2026-06-11 (re-verified 2026-06-16)

---

## Identification

- **Extensions:** `.vsh` (vertex shader), `.psh` (pixel shader)
- **Found in:** `.pak` archive; logical path pattern: `shader/*`
- **Magic / signature:** None. There is no binary header or magic bytes. The file begins immediately with the version-declaration text line.
- **Encoding:** 7-bit ASCII throughout. No bytes above `0x7E` observed in any verified sample. No CP949/EUC-KR text is present — shader source is entirely ASCII.
- **Line endings:** Windows CRLF (`0x0D 0x0A`) throughout all verified samples. No lone-LF endings observed.
- **Endianness:** Not applicable (text format).
- **Compression / encryption:** Not observed in verified samples. The game's VFS layer may apply the same decryption pass used for other asset types; this is unconfirmed — see Open Questions.

---

## Format Overview

`.psh` and `.vsh` files are **plain-text Direct3D 9 shader assembly source**. There is no proprietary container, length prefix, binary framing, or compression wrapper around the text. The file is passed verbatim as a byte buffer to the D3D9 runtime assembler at load time.

This format is identical to the standard Direct3D 9 SDK shader assembly text format (documented in the DirectX 9 SDK "Shader Assembly Reference"). The game does not pre-process the text in any way before handing it to the assembler.

---

## File Grammar

```
<file> ::= <version-line> CRLF
           { <statement-line> CRLF }
           [ CRLF ]          ; optional trailing blank line

<version-line>    ::= <shader-type> "." <major> "." <minor>
<shader-type>     ::= "vs"   ; vertex shader (.vsh)
                    | "ps"   ; pixel shader  (.psh)
<major>           ::= ASCII decimal digit(s)
<minor>           ::= ASCII decimal digit(s)

<statement-line>  ::= <blank-line>
                    | <comment-line>
                    | <constant-definition>
                    | <instruction>

<blank-line>      ::= (empty — zero bytes before CRLF)
<comment-line>    ::= ";" <any ASCII text>
<constant-definition> ::= "def" SP <c-register> "," SP <f32> "," SP <f32> "," SP <f32> "," SP <f32>
<instruction>     ::= [ "+" ] <mnemonic> SP <operand-list> [ SP ";" <comment-text> ]
```

The `+` prefix on an instruction denotes a **co-issue (paired) instruction**, valid in ps.1.x. The semicolon introduces an inline comment; everything from `;` to the end of the line is ignored by the assembler.

---

## Version Declaration Line

The first line of every shader file is the version declaration. It is mandatory and has no leading whitespace.

| Field            | .vsh value | .psh value | Verified |
|------------------|-----------|-----------|----------|
| Shader-type token | `vs`      | `ps`      | VERIFIED — all 4 samples |
| First dot separator | `.`    | `.`       | VERIFIED — all 4 samples |
| Major version digit | `1`    | `1`       | VERIFIED — all 4 samples |
| Second dot separator | `.`   | `.`       | VERIFIED — all 4 samples |
| Minor version digit | `1`    | `1`       | VERIFIED — all 4 samples |

All four verified samples declare version `1.1`. Version `1.1` is the highest sub-model in the vs.1.x / ps.1.x family. No other version values have been observed; other values (`1.0`, `1.4`, `2.0`) are legal D3D9 assembly syntax but are not confirmed present in any game shader.

---

## Statement Lines

### Constant definition (`def`)

Defines a float4 constant register with four literal 32-bit float components.

```
def cN, f0, f1, f2, f3
```

- `cN` — constant register index (e.g. `c0`, `c1`).
- `f0`–`f3` — literal floating-point values in standard decimal notation (e.g. `1.0`, `0.5`, `0.0`).

Verified in: `power1dx8.psh`, `power2dx8.psh`, `power4dx8.psh`.

### Instruction line

```
[+] mnemonic  dst [, src0 [, src1 [, src2]]]  [; comment]
```

The optional `+` co-issue prefix is valid in ps.1.x only.

---

## Register Reference

All register names are case-insensitive in D3D9 assembly; the game files use lowercase throughout.

| Prefix | Register kind | Shader stage | Examples observed |
|--------|--------------|--------------|-------------------|
| `v`    | Vertex input | VS only      | `v0` (position), `v1` (normal), `v2` (texcoord) |
| `r`    | Temporary    | VS and PS    | `r0`, `r1`, `r3` |
| `c`    | Constant float4 | VS and PS | `c0`–`c10`; scalar swizzle e.g. `c8.x` |
| `o`    | Output       | VS only      | `oPos` (clip-space position), `oT0`, `oT1` (texcoords), `oD0` (diffuse colour) |
| `t`    | Texture      | PS only      | `t0` |

### Write-mask and swizzle notation

Component selectors observed in samples: `.xyzw`, `.xyz`, `.rgb`, `.a`. These are standard D3D9 assembly notation and are appended directly to a register name with a dot separator.

---

## Instruction Set — Observed Mnemonics

### Vertex shader (vs.1.1)

| Mnemonic | Semantics |
|----------|-----------|
| `m4x4`   | 4×4 matrix multiply: `dst = src0 * src1` (matrix in four consecutive constant registers) |
| `mov`    | Component-wise copy: `dst = src` |
| `dp3`    | 3-component dot product: `dst = dot3(src0, src1)` |
| `max`    | Component-wise maximum: `dst = max(src0, src1)` |
| `mul`    | Component-wise multiply: `dst = src0 * src1` |
| `mad`    | Multiply-add: `dst = src0 * src1 + src2` |

Verified in: `dotoonshading.vsh`.

### Pixel shader (ps.1.1)

| Mnemonic | Semantics |
|----------|-----------|
| `def`    | Define constant register (see above) |
| `tex`    | Sample texture into a texture register: `texN = Sample(samplerN)` |
| `mov`    | Component-wise copy |
| `mul`    | Component-wise multiply: `dst = src0 * src1` |

Verified in: `power1dx8.psh`, `power2dx8.psh`, `power4dx8.psh`.

---

## Known Shader Files

Seven shader filenames are known. Four have been sample-verified; three are inferred from the game's load path (format expected identical).

| Filename | Extension | Shader model | Role | Sample status |
|----------|-----------|-------------|------|---------------|
| `dotoonshading.vsh`  | .vsh | vs.1.1 | Cel-shading vertex shader: world-view-projection transform, two-light Lambert diffuse, emits luminance-based UV on output texcoord 1 for toon LUT lookup | VERIFIED |
| `power1dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample pass 1: base texture sample, 1× multiply | VERIFIED |
| `power2dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample pass 2: squared sample (pass 1 ^ 2) | VERIFIED |
| `power4dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample pass 4: quartic sample (square twice) | VERIFIED |
| `dotoonshading.psh`  | .psh | ps.1.1 | Cel tone pixel shader — normal render state | UNVERIFIED (sample not extracted; load path confirmed) |
| `dotoonshading2.psh` | .psh | ps.1.1 | Cel tone pixel shader — stealth/invisible render state | UNVERIFIED (sample not extracted; load path confirmed) |
| `finaldx8.psh`       | .psh | ps.1.1 | Final composite: saturate(2 × edge × c0 + bloom × c1) | UNVERIFIED (sample not extracted; load path confirmed) |

Whether additional shader files exist beyond these seven is unknown (see Open Questions).

---

## Load Path (neutral summary)

The game uses two code paths to load shader files:

1. **VFS path (primary):** When the virtual filesystem is mounted, the file is opened through the VFS layer, the full byte buffer and its length are obtained, and the raw text buffer is passed to the D3D9 runtime assembler. No game-side preprocessing of the text occurs before assembly.

2. **Disk fallback:** When the VFS is not mounted, the assembler is called directly with a bare relative file path, and the D3D9 runtime reads the file from disk itself.

After assembly, the resulting token buffer is submitted to the D3D9 device to create either a vertex shader or a pixel shader object.

The D3D9 assembler is called with flags value `0` at all observed call sites (no debug flag, no optimization flags).

**Re-confirmed (two-witness, build 263bd994):** the VFS path opens the file, obtains `{buffer, length}`,
and passes them to the runtime "assemble from buffer" call; the disk fallback passes the bare relative
path to the runtime "assemble from file" call. Both call sites pass the assembler flags argument as `0`.
The text buffer is handed over verbatim — no game-side preprocessing precedes assembly.

---

## Re-authoring Guidance for Assets.Mapping / Godot

These files **cannot be used as-is on modern hardware**. D3D9 ps.1.x / vs.1.x assembly requires Direct3D 9 or a compatibility layer and has no equivalent in Vulkan, OpenGL, or Godot's shader pipeline.

The text source files are the complete, human-readable description of the shader logic. Because the assembly is simple and the source is readable, each shader should be re-implemented directly in GLSL 4.5 or Godot Shader Language rather than through any automated translation path.

### Key semantic mappings for re-implementation

**`dotoonshading.vsh` (cel-shading vertex shader):**
- `m4x4 oPos, v0, c0` — multiply the input vertex position by the 4×4 MVP matrix stored in constants `c0`–`c3`; result is clip-space position.
- Two-light Lambert accumulation: for each light, compute `dp3(normal, lightDir)`, clamp to `[0, 1]` with `max`, modulate by light colour, accumulate.
- `dp3 oT1.xyz, r1, c9` — dot the accumulated diffuse colour against a luminance-weight vector (in `c9`) to produce a scalar luminance value; write to the x-component of output texcoord 1. This value indexes the 1D toon LUT texture in the pixel shader.

**`power*.psh` (glow/bloom passes):**
- `power1dx8.psh`: `r0 = tex t0; r0 = r0 * c0` — sample base texture, scale by constant.
- `power2dx8.psh`: square the sample (multiply by itself) to steepen the falloff curve.
- `power4dx8.psh`: square twice (two multiplies) for a quartic falloff, producing a sharper/brighter highlight core.

The Godot re-implementation should produce a `cel_shading.gdshader` (combining the VS and PS logic) and a `bloom_pass.gdshader` (parameterised by the pass index to avoid three near-identical files).

---

## Sample File Metrics (cross-reference only — bytes stay in `_dirty/`)

These sizes are provided for parser sanity-checks and regression tests only. Do not commit the files.

| Filename | File size (bytes) | CRLF-terminated lines |
|----------|------------------|-----------------------|
| `dotoonshading.vsh` | 754 | 24 (including 1 trailing blank) |
| `power1dx8.psh`     | 116 | 7 |
| `power2dx8.psh`     | 170 | 7 |
| `power4dx8.psh`     | 226 | 10 |

---

## Known Unknowns

1. **VFS encryption:** Whether `.psh`/`.vsh` files inside the `.pak` archive are subject to the same encryption or obfuscation pass as other asset types (mesh, texture) is unconfirmed. The load path reads them via the VFS layer, which may transparently decrypt. If shaders are stored raw (unencrypted) inside `.pak`, the parser needs no decryption step; if they are encrypted, the same key/scheme as other assets applies.
2. **Unverified shader files:** `dotoonshading.psh`, `dotoonshading2.psh`, and `finaldx8.psh` were not in the extracted sample set; their per-instruction *content* has not been sample-confirmed. Their format is strongly inferred to be identical based on the shared load path. **Re-confirmed (build 263bd994):** all five shader paths are present in the VFS and `power1dx8.psh` was byte-walked (`ps.1.1\r\n` header, 116 B); the executable carries only the load logic and the file paths — never the shader source or bytecode — so recovering the exact arithmetic of the three unextracted shaders still requires reading those on-disk files.
3. **Shader file completeness:** Only seven shader filenames are known (from the game's internal string table). Whether additional shader files exist for other effects (e.g. character effects, weather, UI) has not been confirmed. The power progression is 1/2/4 — a `power3dx8.psh` is not referenced in known strings.
4. **Other shader model versions:** Only version `1.1` has been observed. Whether any shader files use `vs.1.0`, `ps.1.4`, `vs.2.0`, or any other model is unknown.
5. **D3DX flags:** All observed load sites use flags value `0`. Whether any code path uses `D3DXSHADER_DEBUG` or another flag in a debug build is unknown.

---

## Campaign 5 — Runtime Cel/Glow Shader Set: Assembly, Bind Sites, Toon LUT, and VS Constants

> Added 2026-06-14 from Campaign 5 / Lane 3 (SHADERS) dirty-room static analysis. This section
> records *which* shaders the runtime assembles, *where* they bind, *how* the toon ramp LUT is
> sampled, and *which vertex-shader constants* were recovered as literals. It does NOT contain
> shader source text or bytecode (those are external VFS files — see §C5.2). The render passes that
> consume these shaders are documented in `specs/rendering.md`; this section cross-references that
> spec rather than duplicating the post-chain. `// spec: Docs/RE/specs/rendering.md`

### C5.1 The five runtime-assembled shaders

**Confidence: HIGH** (each path string fans into exactly the two loader functions; handle slots and
bind sites recovered statically).

All five are loaded by the same idiom: a path-string global → the file is opened from the mounted VFS
(or, as a fallback, directly off disk) → the file's text is fed to a Direct3D runtime assemble call
(`D3DXAssembleShader` for the VFS buffer, `D3DXAssembleShaderFromFileA` for the disk fallback) → the
returned token buffer is turned into a device shader object → the handle is stored on the renderer
for later binding. There are exactly **two loader functions**: a *full cel-shading initialiser* (run
at device-creation time, which also creates the post render targets and loads the toon LUT) and a
*partial pixel-shader reload* (run on device reset / hot-reload, which re-assembles only the two cel
pixel shaders without touching the vertex shader or the render targets).

| Shader file | Role | Bound in | Confidence |
|-------------|------|----------|------------|
| `data/shader/dotoonshading.vsh` | Cel vertex shader — world-view-projection transform + two-light Lambert → emits the N·L luminance coordinate on a texcoord | The cel bind site, once per skinned-character draw | HIGH |
| `data/shader/dotoonshading.psh` | Cel tone pixel shader — **normal** render state | The cel bind site (default branch) | HIGH |
| `data/shader/dotoonshading2.psh` | Cel tone pixel shader — **stealth / 은신 variant** | The cel bind site (stealth branch, selected by a boolean argument) | HIGH |
| `data/shader/finaldx8.psh` | Final composite / blur pixel shader for the post chain | The bloom-blur post pass (see `specs/rendering.md` §6) | HIGH |
| `data/shader/power1dx8.psh` | Glow downsample / blur pixel shader for the post chain | The offscreen/scene glow pass (see `specs/rendering.md` §6) | HIGH |

The glow pixel-shader path is **not a fixed string operand**: the renderer constructor pre-fills an
**editable filename slot** with `data/shader/power1dx8.psh`, and the loader reads the path from that
slot. The glow shader is therefore configurable but defaults to `power1dx8.psh`. This is why the
multi-tap `power2`/`power4` chain depth is data-driven and UNVERIFIED — see `specs/rendering.md` §6.4.

### C5.2 The shader source is NOT in the executable

**Confidence: HIGH.**

Because the five shaders are assembled at runtime via the Direct3D runtime assemble calls (which take
**ASCII shader-assembly source text** and assemble it to bytecode at load), the executable contains
only the *file paths* and the *loader logic* — never the shader source or its compiled bytecode. The
actual `.psh` / `.vsh` / `.bmp` bytes are **external VFS asset files** under `data/shader/` and are
**not recoverable from the executable**. Recovering the precise per-instruction shader logic (the
exact ramp lookup, the exact composite arithmetic, any rim math) requires the on-disk files from the
client VFS. The `dotoonshading.psh`, `dotoonshading2.psh`, and `finaldx8.psh` source samples remain
UNVERIFIED for this reason (see Known Unknowns #2).

### C5.3 Toon ramp LUT — `data/shader/toonramp.bmp`

**Confidence: HIGH (role); MEDIUM (exact pixel dimensions).**

The toon ramp is a real loaded texture (`data/shader/toonramp.bmp`), loaded by the full cel
initialiser through the generic VFS-or-disk texture loader and stored in a dedicated renderer slot.
How it is used:

- The cel vertex shader transforms the already-CPU-skinned vertices (the stride-32 XYZ / NORMAL / UV
  layout — see `specs/rendering.md` §5.2) by the world-view-projection matrix and computes the
  per-vertex diffuse term, emitting an **N·L luminance coordinate on a texcoord**.
- The cel bind site binds the ramp as a texture on **texture stage 1**, sets the cel vertex shader,
  then sets either the normal or the stealth cel pixel shader. The pixel shader performs a **1-D
  lookup into the ramp by the interpolated N·L luminance** — a classic 1-D cel-quantisation ramp keyed
  by N·L. The ramp file converts a smooth `0..1` lighting term into a small number of hard tone bands
  (the stepped cel look).
- **The light-step lives in the ramp file, not in code.** There is no numeric "light-step threshold"
  constant to recover — the per-tone quantisation is encoded entirely in `toonramp.bmp`. Re-authoring
  the cel look faithfully therefore requires the on-disk ramp file.
- **Pixel geometry:** a prior lane tagged the ramp as a small 1-D ramp (about 256×1, 24 bpp). The
  build-263bd994 sample is a real BMP (`'BM'` magic) of **824 bytes**, which **corroborates the
  256×1×24bpp estimate via header math**: a 24-bpp BMP with the standard 54-byte header storing
  256×1 pixels is `54 + 256 × 3 = 822` bytes, padded to 824 — an exact match for the prior estimate.
  The dimensions are upgraded from "prior-lane annotation" to **SAMPLE-VERIFIED (size-corroborated;
  ~256×1×24bpp)**; a full pixel-walk of the band layout (how many tone steps, the per-step luminance
  thresholds) is the only remaining detail and is confirmable by reading the on-disk file. The *role*
  (1-D N·L cel ramp on stage 1) remains HIGH confidence.

### C5.4 Recovered cel vertex-shader constants

**Confidence: HIGH** (all float values are literal in code, decoded from their IEEE-754 bit
patterns; none required a debugger). Two sources feed the toon shaders: a block of float defaults
pre-initialised by the renderer constructor, and a per-frame upload of vertex-shader constant
registers `c4`–`c10`. Each register is uploaded as a four-component float vector with a
"set-vertex-shader-constant" device call (count = 4 floats per register), the register index being
the literal first argument to each upload. The values below were **independently re-confirmed** this
pass by decoding the four 32-bit immediate stores that fill each register's source vector back to
their IEEE-754 values — so the table is now bit-pattern-backed, not merely "recovered value".

| Register | Recovered value | Role | Confidence |
|----------|-----------------|------|------------|
| `c4` | runtime-set light direction; **default `[-1, 0, 0, 0]`** | Configurable light direction. The per-frame upload reads three object slots (x, y, z) and forces w = 0 (a direction, not a point). The scene-object constructor pre-fills those slots with `[-1, 0, 0]` (a unit light direction down the −X axis); any later setter can overwrite the live value, but absent an override the default is `[-1, 0, 0, 0]`. | HIGH (mechanism + default recovered); live value runtime-set |
| `c5` | `[0, 0, -1, 0]` | Axis / view-Z vector | HIGH |
| `c6` | `[1, 1, 1, 1]` | White / material-ambient | HIGH |
| `c7` | `[0, 0, 0, 0]` | Zero vector | HIGH |
| `c8` | `[0, 1, 1, 1]` | Mask / partial-white | HIGH |
| **`c9`** | **`[0.299, 0.587, 0.114, 1.0]`** | **Luminance weights — exactly the ITU-R BT.601 luma coefficients (R, G, B) with w = 1.0. Independently re-confirmed: the four float immediates that fill the c9 source vector decode to 0.299 / 0.587 / 0.114 / 1.0 from their bit patterns. This is the signature constant of the cel shader: the dot of the accumulated diffuse colour against `c9` produces the scalar N·L luminance that keys the toon-ramp lookup.** | HIGH (bit-pattern re-confirmed) |
| `c10` | `[1, 1, 1, 1]` | White | HIGH |

**Note on c9 ordering and a sibling constant.** The c9 register receives the BT.601 weights in **RGB
order with w = 1.0** (the four immediate stores above). The image also carries a *separate*,
unrelated luma constant in read-only data storing the same coefficients in **BGR order with w = 0**;
that block is **not** what register c9 receives and must not be confused with it. Only the four
immediate stores feed c9.

**The c4 default — additional detail.** The constructor also pre-fills a *second* light/material
vector (default `[0, 0, -1]`) with an accompanying `[1, 1, 1, 1]` colour. The c4 upload reads only
the **first** slot, so c4's default is `[-1, 0, 0, 0]`; the second vector is not uploaded to the
`c4`–`c10` block and is out of scope for the cel VS constants.

### C5.5 Per-skin pixel-shader constants are a character-brightness colour-modulation system — NOT edge/outline params

**Confidence: HIGH. Verdict: there is NO code-set edge-highlight, outline, or rim threshold/width
constant anywhere in the cel constant block, the cel bind site, or the per-skin pixel-shader
constant table.** This reclassifies the per-skin pixel-shader constants and removes a residual doubt
about a hidden numeric "edge" parameter.

What the per-skin pixel-shader constants actually are: once per skinned-character draw, two
pixel-shader constant registers (registers 0 and 1) are uploaded from a **9-entry table** on the
renderer object — one multiply triple and one add triple — selected by a per-character **state index**
in the range 0..8. A display-config loader fills that table from an external display configuration
file under keys of the form `DISPLAY_CHAR_BRIGHT_{MULTI|ADD}_{R|G|B}_{state}` for the nine states:

| State index | State | Meaning |
|------------:|-------|---------|
| 0 | DEFAULT | normal character render |
| 1 | CHOICE  | character is being selected / targeted |
| 2 | HIT     | hit-flash |
| 3 | ALPHA   | semi-transparent |
| 4 | HIDDEN  | stealth / invisible |
| 5 | POISON  | poisoned tint |
| 6 | TYPE    | type / faction tint |
| 7 | ANGER   | enraged tint |
| 8 | AUTO    | auto-state tint |

These are a **per-state character brightness / colour-tint modulation system** (a per-state RGB
multiply plus a per-state RGB add applied to the character's pixels) — a tone/glow colour control,
**not** an edge, outline, or rim parameter. The in-binary default (when no display config is present)
is a white multiply `[1, 1, 1]` and a zero add for every state. The numeric per-state tweaks live in
the external display configuration file, not in the executable.

**Where the cel "outline" actually comes from.** The cel outline look is **not** produced by any
numeric edge constant in code. It is the bright/edge render-target composite performed in the
post-process chain (the final composite of the bright-extract render target combined with the bloom
render target) together with the stepped tone bands baked into the toon ramp file. There is **no
code-set edge threshold, outline width, or edge-detect constant to recover** — any such math, if it
exists at all, lives only inside the external composite/cel pixel-shader source. The post-chain pass
pipeline and its present-blend are owned by `specs/rendering.md`; this spec does not duplicate them.

### C5.6 Load / bind flow + post-process gating (neutral prose)

1. **Device creation** runs the full cel initialiser. It creates three offscreen render targets at
   backbuffer size (with a 1024×1024 fallback) — a scene/cel RT, a bright/edge RT, and a downscaled
   glow RT — and opens a render-to-surface helper for each (the post chain is documented in
   `specs/rendering.md` §6).
2. It loads the toon ramp LUT (`data/shader/toonramp.bmp`) into its dedicated slot (1-D N·L ramp,
   bound to **texture stage 1**).
3. It builds the inline cel vertex declaration (stream-0 stride 32: XYZ at 0, NORMAL at 12,
   TEXCOORD0 at 24, plus the N·L luminance channel — see `specs/rendering.md` §5.2), then assembles
   the five shaders in order: `dotoonshading.vsh` → `dotoonshading.psh` → `dotoonshading2.psh` →
   `finaldx8.psh` → the editable-slot glow shader (default `power1dx8.psh`). Each is created as a
   device vertex or pixel shader and stored on the renderer; the temporary assembly object is
   released.
4. **Per-frame use:** the cel constants `c4`–`c10` (including the BT.601 luma vector `c9`) are
   uploaded each frame. Once per skinned-character draw, the cel bind site binds the toon ramp to
   **texture stage 1**, sets the cel vertex shader, then sets the **stealth** cel pixel shader
   (`dotoonshading2.psh`) when a per-character stealth flag is set, else the **normal** cel pixel
   shader (`dotoonshading.psh`).
5. **The whole cel path is gated on the post/offscreen enable flag (default OFF).** The skinned-draw
   path tests the renderer's offscreen-render-target / post-process enable flag (pre-set to disabled
   by the scene-object constructor) before taking the cel path. When that flag is off, the skinned
   character is drawn through the **fixed-function fallback** with **no cel bind** — i.e. the toon
   look is **coupled to the bloom/post feature being enabled**, and even skinned actors fall back to
   fixed-function shading when post is off. When the flag is on, both the per-frame cel VS constant
   upload and the per-draw cel bind (ramp + VS + normal/stealth PS) run.
6. **Hot reload:** on a device reset, the partial reload re-assembles only the two cel pixel shaders
   (and clears the stage-1 texture, vertex shader, and pixel shader) without touching the vertex
   shader or the render targets.

### C5.7 Campaign 5 / 5B known unknowns

- **The actual `.psh` / `.vsh` shader-assembly source text** — external VFS files; not in the
  executable. The exact per-instruction shader arithmetic (the N·L accumulation, the 1-D ramp lookup
  math, and any composite/edge math) is recoverable only from the on-disk `data/shader/*.psh|vsh`
  files. The `power1dx8.psh` internal `c0` scale and the `finaldx8.psh` composite arithmetic
  (the `saturate(2·edge·c0 + bloom·c1)` form is a prior-lane summary, not re-derived) live there, not
  in the binary. UNVERIFIED until the on-disk files are read.
- **The numeric light-step threshold** — there is none in code; the quantisation lives in
  `toonramp.bmp` (the number of tone steps and the per-step luminance thresholds are in the ramp
  file's pixels, not in the executable).
- **A distinct rim / outline / edge colour constant** — REFUTED in code (see §C5.5). There is no
  numeric edge threshold, outline width, or edge-detect constant; the outline is the post-chain
  bright/edge render-target composite, not a literal value.
- **Exact `toonramp.bmp` band layout** — the file is 824 bytes on build 263bd994, which corroborates
  the ~256×1×24bpp geometry by header math (§C5.3); the dimensions are now SAMPLE-VERIFIED
  (size-corroborated). What remains open is the **band layout** — the number of tone steps and the
  per-step luminance thresholds baked into the pixels — recoverable only by a full pixel-walk of the
  on-disk file.
- **The runtime light-direction value (`c4`)** — the default initializer `[-1, 0, 0, 0]` is now
  recovered (§C5.4); the **live** value (if overwritten by gameplay/config) needs the running client
  or whatever config feeds the light slot.
- **The shipped per-state character-brightness numbers** — the `DISPLAY_CHAR_BRIGHT_*` 9-state
  multiply/add table (§C5.5) defaults to white-multiply / zero-add in the binary; the actual shipped
  per-state tints live in the external display configuration file, not in the executable.

----------|-----------------|------|------------|
| `c4` | runtime-set light vector | Configurable light direction (from an editable renderer slot; value set at runtime) | HIGH (mechanism); value runtime-set |
| `c5` | `[0, 0, -1, 0]` | Axis / view-Z vector | HIGH |
| `c6` | `[1, 1, 1, 1]` | White / material-ambient | HIGH |
| `c7` | `[0, 0, 0, 0]` | Zero vector | HIGH |
| `c8` | `[0, 1, 1, 1]` | Mask / partial-white | HIGH |
| **`c9`** | **`[0.299, 0.587, 0.114, 1.0]`** | **Luminance weights — exactly the ITU-R BT.601 luma coefficients (R, G, B). This is the signature constant of the cel shader: the dot of the accumulated diffuse colour against `c9` produces the scalar N·L luminance that keys the toon-ramp lookup.** | HIGH |
| `c10` | `[1, 1, 1, 1]` | White | HIGH |

The default colour-modulation triple read by the post composite pass is `[1, 1, 1]` (white init
default); those per-tone post constants are owned by the post chain — see `specs/rendering.md` §6.

### C5.5 Load / bind flow (neutral prose)

1. **Device creation** runs the full cel initialiser. It creates three offscreen render targets at
   backbuffer size (with a 1024×1024 fallback) — a scene/cel RT, a bright/edge RT, and a downscaled
   glow RT — and opens a render-to-surface helper for each (the post chain is documented in
   `specs/rendering.md` §6).
2. It loads the toon ramp LUT (`data/shader/toonramp.bmp`) into its dedicated slot (1-D N·L ramp,
   stage-1 texture).
3. It builds the inline cel vertex declaration (stream-0 stride 32: XYZ at 0, NORMAL at 12,
   TEXCOORD0 at 24, plus the N·L luminance channel — see `specs/rendering.md` §5.2), then assembles
   the five shaders in order: `dotoonshading.vsh` → `dotoonshading.psh` → `dotoonshading2.psh` →
   `finaldx8.psh` → the editable-slot glow shader (default `power1dx8.psh`). Each is created as a
   device vertex or pixel shader and stored on the renderer; the temporary assembly object is
   released.
4. **Per-frame use:** the cel constants `c4`–`c10` (including the BT.601 luma vector `c9`) are uploaded
   each frame and the cel render/texture states are set. Once per skinned-character draw, the cel bind
   site binds the ramp to stage 1, sets the cel vertex shader, and sets the stealth or normal cel
   pixel shader based on a boolean argument. The offscreen/scene and glow-extract/bloom-blur post
   passes use the glow and composite pixel shaders and the three RTs — those passes belong to
   `specs/rendering.md`.
5. **Hot reload:** on a device reset, the partial reload re-assembles only the two cel pixel shaders
   (and clears the stage-1 texture, vertex shader, and pixel shader) without touching the vertex
   shader or the render targets.

### C5.6 Campaign 5 known unknowns

- **The actual `.psh` / `.vsh` shader-assembly source text** — external VFS files; not in the
  executable. The `saturate(2·edge·c0 + bloom·c1)` note for `finaldx8.psh` (above) is a prior-lane
  summary, not re-derived here. UNVERIFIED until the on-disk files are read.
- **The numeric light-step threshold** — there is none in code; the quantisation lives in
  `toonramp.bmp`.
- **A distinct rim / outline colour constant** — not found in the cel path; the outline is the
  bright/edge render target composited in the post chain (see `specs/rendering.md` §6), not a literal
  colour.
- **Exact `toonramp.bmp` pixel dimensions** — prior-lane comment says about 256×1×24 bpp; not
  re-measured (MEDIUM confidence). Confirmable by reading the on-disk file.
- **The runtime light-direction value (`c4`)** — the mechanism (editable slot + accessor) is HIGH
  confidence; the live value would need the running client or whatever config feeds it.

---

## Enumerations / Flags

Not applicable. This format carries no binary enumeration fields. The shader-type token (`vs` / `ps`) in the version line is an ASCII string, not a binary value.

---

## Cross-references

- Related formats: `pak.md` (container that holds these files), `texture.md` (toon LUT texture referenced by `dotoonshading.vsh`)
- Related specs: `specs/rendering.md` (the render pipeline + glow/bloom post passes that bind these shaders), `specs/skinning.md` (the skinned-character vertex buffer the cel shader consumes)
- Glossary: see `Docs/RE/names.yaml`
- Provenance: see `Docs/RE/journal.md`
- Dirty-room samples: `Docs/RE/_dirty/samples/data/shader/` (gitignored, do not commit)
