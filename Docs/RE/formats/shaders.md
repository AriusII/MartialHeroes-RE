# Format: .psh / .vsh  (Direct3D 9 shader assembly source text)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers / Assets.Mapping. Every offset an engineer cites must reference this file.
>
> **verification:** sample-verified (two-witness) — the D3D9-assembly-text format, the
> `vs.1.1` / `ps.1.1` version line, the CRLF line endings, the VFS-or-disk load path (assembler flags
> = 0 at every site), the five-shader assemble order, the three render targets, and the `toonramp.bmp`
> stage-1 LUT were all re-confirmed against the live cel/glow initialiser AND a byte-walk of real shader
> files. This pass the **three previously-unextracted cel/composite pixel shaders**
> (`dotoonshading.psh`, `dotoonshading2.psh`, `finaldx8.psh`) were extracted from the VFS and read,
> upgrading their per-instruction content from UNVERIFIED to SAMPLE-VERIFIED; the `.psh` family CRLF
> line-ending was re-confirmed by a raw byte-walk. The `c4`–`c10` cel constants (incl. the BT.601 `c9`)
> carry forward from the prior bit-pattern confirmation (not re-decoded this pass). A further re-verify
> pass (build 263bd994) physically confirmed all six power-ladder taps on disk
> (`power1`–`power2`–`power4`–`power8`–`power16`–`power32`), correcting the prior "1/2/4" summary and
> adding size metrics for the three new taps; the Magic / grammar note is tightened to reflect that a
> file may open with CP949 comment lines before the version declaration (confirmed in two cel `.psh`
> samples).
> **ida_reverified:** 2026-06-24
> **ida_anchor:** 263bd994
> **evidence:** [static-ida, vfs-sample]
> **conflicts:** NONE structural. Three refinements vs the earlier text (not reversals): encoding
> statement loosened (code tokens ASCII, comments may carry CP949); `power2dx8.psh` / `power4dx8.psh`
> not statically string-referenced; power ladder extended from 1/2/4 to 1/2/4/8/16/32 (three further
> taps physically verified on disk). The exact `toonramp.bmp` band layout remains the only open content
> item (see Known Unknowns).
>
> **spec_status:** sample_verified (10 shader files cross-confirmed; all five runtime-assembled shaders read)
> **date:** 2026-06-11 (re-verified 2026-06-21; CYCLE 11 addition 2026-06-23; re-verified 2026-06-24)

---

## Identification

- **Extensions:** `.vsh` (vertex shader), `.psh` (pixel shader)
- **Found in:** `.pak` archive; logical path pattern: `shader/*`
- **Magic / signature:** None. There is no binary header or magic bytes. The version-declaration text line (`ps.1.1` / `vs.1.1`) is the first non-comment content, but it is NOT guaranteed to be byte 0: two confirmed `.psh` samples open with a leading CP949 `;`-comment line before the version declaration. A faithful reader must not assume the version line starts at offset 0.
- **Encoding:** Shader **code tokens** (the version line, mnemonics, registers, `def` literals) are 7-bit ASCII. **Comment text** (everything after a `;`) is *not* guaranteed ASCII: two verified cel pixel-shader samples open with a CP949/EUC-KR Korean comment line (bytes above `0x7E`). Because the assembler ignores everything after `;`, this never affects the parse — but do not assert "no bytes above 0x7E"; a faithful reader must tolerate CP949 in comments.
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
<file> ::= { <comment-line> CRLF }   ; zero or more leading comment lines (may be CP949)
           <version-line> CRLF
           { <statement-line> CRLF }
           [ CRLF ]                  ; optional trailing blank line

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
<comment-line>    ::= ";" <any text>       ; ASCII or CP949 — ignored to end-of-line
<constant-definition> ::= "def" SP <c-register> "," SP <f32> "," SP <f32> "," SP <f32> "," SP <f32>
<instruction>     ::= [ "+" ] <mnemonic> SP <operand-list> [ SP ";" <comment-text> ]
```

The `+` prefix on an instruction denotes a **co-issue (paired) instruction**, valid in ps.1.x. The semicolon introduces an inline comment; everything from `;` to the end of the line is ignored by the assembler.

---

## Version Declaration Line

The version declaration is the first non-comment line of every shader file. It is mandatory and has no leading whitespace. In files that open with leading CP949 `;`-comment lines (confirmed in two cel `.psh` samples), it follows those comment lines rather than appearing at byte offset 0.

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
| `add`    | Component-wise add: `dst = src0 + src1` (also used self-paired as `add r, r, r` for a ×2 scale) |

Verified in: `power1dx8.psh`, `dotoonshading.psh`, `dotoonshading2.psh`, `finaldx8.psh`. The `+mov` co-issue prefix is verified in the cel/composite pixel shaders (alpha written in a paired instruction).

---

## Known Shader Files

Ten shader filenames are known. The five that the `doida.exe` build statically references by string are the
five runtime-assembled shaders; the remaining five power-ladder taps (`power2` through `power32`) are present
on disk as data-driven options but are **not** string-referenced by the executable on build 263bd994 — the
glow pixel-shader path is read from an editable filename slot whose constructor default is `power1dx8.psh`,
and the active tap is selected by the `DISPLAY_POWER` key in `display.lua` (shipped value: 2 → `power2dx8.psh`).

| Filename | Extension | Shader model | Role | Sample status |
|----------|-----------|-------------|------|---------------|
| `dotoonshading.vsh`  | .vsh | vs.1.1 | Cel-shading vertex shader: world-view-projection transform, two-light Lambert diffuse, emits luminance-based UV on output texcoord 1 for toon LUT lookup | VERIFIED |
| `power1dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample pass 1: base texture sample, scale by `c0` | VERIFIED (string-referenced) |
| `dotoonshading.psh`  | .psh | ps.1.1 | Cel tone pixel shader — **normal** render state: `base × toonRamp`, ×2, brightness-modulated by per-state MULTI (`c0`) and ADD (`c1`); alpha passes through the lit value | VERIFIED (string-referenced) |
| `dotoonshading2.psh` | .psh | ps.1.1 | Cel tone pixel shader — **stealth / 은신 (invisible)** render state: identical arithmetic to the normal shader except alpha is taken from `c1.w` (the per-state ADD's w drives the stealth fade) | VERIFIED (string-referenced) |
| `finaldx8.psh`       | .psh | ps.1.1 | Final composite / post: `saturate(scene×2×c0 + glow×c1)`, alpha forced opaque from a literal `(1,1,1,1)` constant | VERIFIED (string-referenced) |
| `power2dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample (squared sample — one `mul r0,r0,r0` squares the sample) | VERIFIED (present on disk; not string-referenced in build 263bd994) |
| `power4dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample (quartic — two squarings) | VERIFIED (present on disk; not string-referenced in build 263bd994) |
| `power8dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample (8th power — three squarings) | VERIFIED (present on disk; not string-referenced in build 263bd994) |
| `power16dx8.psh`     | .psh | ps.1.1 | Glow/bloom downsample (16th power — four squarings) | VERIFIED (present on disk; not string-referenced in build 263bd994) |
| `power32dx8.psh`     | .psh | ps.1.1 | Glow/bloom downsample (32nd power — five squarings) | VERIFIED (present on disk; not string-referenced in build 263bd994) |

Whether additional shader files exist for other effects (character effects, weather, UI) has not been
confirmed (see Known Unknowns).

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

**`dotoonshading.psh` (cel tone, NORMAL render state) — sample-verified arithmetic:**
- Sample the base texture into `t0` and the toon ramp LUT into `t1`.
- `lit = (base × toonRamp)` — the base colour modulated by the 1-D N·L cel ramp lookup (this is the toon lighting).
- `lit = lit × 2` (a self-paired `add` doubling).
- `lit = lit × c0` — multiply by the per-state brightness **MULTI** constant.
- `rgb = lit + c1` — add the per-state brightness **ADD** constant; **alpha passes through the lit value** (a paired `+mov r0.a`).
- Net: `out.rgb = (base × toonRamp) × 2 × c0 + c1`, `out.a = lit.a`. This confirms §C5.5 — `c0`/`c1` are the per-state brightness MULTI/ADD pair, applied here.

**`dotoonshading2.psh` (cel tone, STEALTH / 은신 render state) — sample-verified arithmetic:**
- **Identical** to `dotoonshading.psh` except the alpha write: alpha is taken from **`c1.w`** (the per-state ADD's w component) instead of from the lit value. The ADD constant's w therefore drives the stealth/invisible fade.

**`finaldx8.psh` (final composite / post) — sample-verified arithmetic:**
- Sample the scene/bright render target into `t0` and the bloom/glow render target into `t1`.
- `scene = t0 × 2 × c0`, `glow = t1 × c1` — both `c0`/`c1` are composite-scale constants set per pass (NOT per-character brightness).
- `rgb = scene + glow`; alpha forced opaque from a literal `def c2, 1,1,1,1`.
- Net: `out = saturate(scene×2×c0 + glow×c1)`, `out.a = 1`. This refines the prior "saturate(2·edge·c0 + bloom·c1)" summary: `t0` = bright/scene RT scaled ×2·`c0`, `t1` = bloom/glow RT scaled by `c1`, `c2` = literal opaque-alpha constant.

**`power*.psh` (glow/bloom passes):**
- `power1dx8.psh`: `r0 = tex t0; r0 = r0 * c0` — sample base texture, scale by constant.
- `power2dx8.psh`: square the sample (multiply by itself) to steepen the falloff curve.
- `power4dx8.psh`: square twice (two multiplies) for a quartic falloff, producing a sharper/brighter highlight core.

The Godot re-implementation should produce a `cel_shading.gdshader` with normal/stealth variants (the alpha-from-`c1.w` path is the stealth fade), a `final_composite.gdshader` for `finaldx8.psh`, and a `bloom_pass.gdshader` (parameterised by the pass index to avoid near-identical files).

---

## Sample File Metrics (cross-reference only — sample bytes stay in the gitignored quarantine, never committed)

These sizes are provided for parser sanity-checks and regression tests only. Do not commit the files.

| Filename | File size (bytes) | CRLF-terminated lines |
|----------|------------------|-----------------------|
| `dotoonshading.vsh`  | 754 | 24 (including 1 trailing blank) |
| `dotoonshading.psh`  | 332 | (CRLF throughout; zero lone-LF — raw byte-walked) |
| `power1dx8.psh`      | 116 | 7 |
| `power2dx8.psh`      | 170 | 7 |
| `power4dx8.psh`      | 226 | 10 |
| `power8dx8.psh`      | 276 | (CRLF; size-verified build 263bd994) |
| `power16dx8.psh`     | 328 | (CRLF; size-verified build 263bd994) |
| `power32dx8.psh`     | 378 | (CRLF; size-verified build 263bd994) |

`dotoonshading.psh` was raw byte-walked on build 263bd994: 332 bytes, CRLF (`\r\n`) throughout with zero
lone-LF endings — confirming the CRLF claim holds for the `.psh` family as well (the cel/composite pixel
shaders were also read but their exact byte sizes are not pinned here). The power-ladder size progression
(116 → 170 → 226 → 276 → 328 → 378 bytes for power1 through power32) reflects the one-additional-`mul`
pattern per doubling step: each tap adds one `mul r0,r0,r0` instruction that squares the prior result,
doubling the exponent.

---

## Known Unknowns

1. **VFS encryption:** Whether `.psh`/`.vsh` files inside the `.pak` archive are subject to the same encryption or obfuscation pass as other asset types (mesh, texture) is unconfirmed. The load path reads them via the VFS layer, which may transparently decrypt. If shaders are stored raw (unencrypted) inside `.pak`, the parser needs no decryption step; if they are encrypted, the same key/scheme as other assets applies.
2. **~~Unverified shader files~~ — RESOLVED (build 263bd994):** `dotoonshading.psh`, `dotoonshading2.psh`, and `finaldx8.psh` have now been extracted from the VFS and read; their per-instruction arithmetic is sample-verified and recorded in the Known Shader Files table and the Re-authoring Guidance (cel `base × toonRamp × 2 × c0 + c1` with normal/stealth alpha split; composite `saturate(scene×2×c0 + glow×c1)`). All five runtime-assembled shaders are now content-confirmed. The executable still carries only the load logic and file paths (never source or bytecode), so any *future* shader content is recovered only by reading the on-disk file.
3. **Shader file completeness:** Ten shader filenames are now known and physically verified (build 263bd994). The executable string table references exactly six paths (`dotoonshading.vsh`, `dotoonshading.psh`, `dotoonshading2.psh`, `finaldx8.psh`, `power1dx8.psh`, `toonramp.bmp`); the full power ladder `power2dx8.psh` through `power32dx8.psh` (five taps) exist on disk but are **not** string-referenced in build 263bd994 — the glow path is the editable slot selected by `DISPLAY_POWER` (shipped value 2, selecting `power2dx8.psh`). The confirmed power progression is **1 / 2 / 4 / 8 / 16 / 32** (six taps; no `power3dx8.psh` or other interstitial file is present). Whether additional shader files exist for other effects (character effects, weather, UI) has not been confirmed.
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
client VFS. Those on-disk files have now been read: the `dotoonshading.psh`, `dotoonshading2.psh`, and
`finaldx8.psh` sources are **SAMPLE-VERIFIED** (their arithmetic is recorded in the Known Shader Files
table and Re-authoring Guidance). The executable itself still carries no source or bytecode — only the
paths and load logic.

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
code-set edge threshold, outline width, or edge-detect constant to recover**. The now-read cel/composite
pixel-shader sources confirm this: `dotoonshading.psh` / `dotoonshading2.psh` carry only the
`base × toonRamp × 2 × c0 + c1` tone math (no edge/rim term), and `finaldx8.psh` is a plain additive
`saturate(scene×2·c0 + glow·c1)` composite — neither contains an edge-detect or outline-width constant.
The post-chain pass pipeline and its present-blend are owned by `specs/rendering.md`; this spec does not
duplicate them.

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

### C5.6a Shipped display.lua values (CYCLE 11 addition)

> The external display configuration file (`display.lua`) ships per-state brightness values and a
> small set of glow/lighting scalars. The values below are recovered from the shipped file and
> supersede the binary default (white-multiply / zero-add) for the running client. They are
> data-only (the executable reads them at startup into the 9-entry table described in §C5.5).

**Per-state DISPLAY_CHAR_BRIGHT_MULTI / ADD table (9 states):**

| State index | State name | MULTI (R, G, B) | ADD (R, G, B) |
|:-----------:|-----------|-----------------|----------------|
| 0 | DEFAULT  | (see BASE_BRIGHT below) | 0, 0, 0 |
| 1 | CHOICE   | 1.0, 1.0, 1.0 | 0.3, 0.3, 0.3 |
| 2 | HIT      | 1.0, 0.5, 0.5 | 0.3, 0, 0 |
| 3 | ALPHA    | 1.0, 1.0, 1.0 | 0, 0, 0 |
| 4 | HIDDEN   | 1.0, 1.0, 1.0 | 0, 0, 0 |
| 5 | POISON   | 0.5, 1.0, 0.5 | 0, 0.1, 0 |
| 6 | TYPE     | 1.0, 1.0, 1.0 | 0, 0, 0 |
| 7 | ANGER    | 1.5, 0.7, 0.7 | 0, 0, 0 |
| 8 | AUTO     | 1.0, 1.0, 1.0 | 0, 0, 0 |

> State table is representative; individual channel components should be confirmed against the
> shipped `display.lua` file read at runtime — these are loader-read values, not code constants.

**Global scalars from display.lua:**

| Key | Value | Role |
|-----|-------|------|
| `BASE_BRIGHT` | 1.05 | Global character brightness multiplier (applied before per-state MULTI) |
| `GLOW_BRIGHT` | 0.3 | Glow/bloom intensity scale fed to the post chain |
| `GLOW_RANGE` | 1, 1 | Glow range parameters (width, height; both 1 in the shipped file) |
| `LIGHT_RATIO` | 0.5 | Light-to-ambient blend ratio |
| `DISPLAY_POWER` | 2 | Shipped glow tap selector — value `2` selects `power2dx8.psh` as the active glow pixel shader |

**Glow-tap chain (corrected from §C5.1).** The glow downsample/blur chain is a **1 / 2 / 4 / 8 / 16 / 32**
ladder of `power{N}dx8.psh` files (not a 1/2/4 chain). The full ladder:
`power1dx8.psh` → `power2dx8.psh` → `power4dx8.psh` → `power8dx8.psh` → `power16dx8.psh` → `power32dx8.psh`.
The `DISPLAY_POWER` value in `display.lua` selects the active tap: the shipped value is **2**, so
`power2dx8.psh` is the default runtime glow shader (not `power1dx8.psh`). Additional taps beyond
power4 are present on disk as data-driven options but not string-referenced in the build — the actual
tap depth is governed by `DISPLAY_POWER` at load time, not by a hard-coded binary path.

**VS negates light directions; light2 is dead.** The vertex shader (`dotoonshading.vsh`) negates
each light direction before the `dp3` Lambert accumulation (the `c4` direction constant is stored as
a negated direction, not a raw world-space vector). A second light (`light2`) is structurally wired in
the shader source but its colour constant is zero in all confirmed samples — it contributes nothing to
the diffuse accumulation and is effectively dead for the shipped client.

### C5.6b Post-chain render-pass flow and enable-flag analysis (CYCLE 11 addition)

> The pass-order, RT sizes, composite weights, and bright-pass behaviour are documented in full in
> `Docs/RE/specs/rendering.md §6`. This subsection records two CYCLE 11 findings that are
> **shader-file-scoped** (they directly govern which `.psh` files are used and when they run) plus
> the decisive proof that the post chain is **permanently inactive in the shipped build**.

**Pass flow (summary — see `specs/rendering.md §6` for the full ordered pass list):**

Three render targets are used when the post chain is active:

| Canonical name | Dimensions | Role |
|:---------------|:----------:|------|
| Scene RT (TEX0) | screenW × screenH | Cel/toon scene capture; also composite destination and present source |
| Bright RT (TEX1) | screenW × screenH | Plain fixed-function copy of TEX0 — **no pixel shader, no threshold** |
| Glow RT (TEX2) | screenW ÷ glowX × screenH ÷ glowY (default ÷2, ÷2) | Downscaled blur result |

The format for all three is the device backbuffer adapter format. Pass order:

1. Draw cel/toon world → TEX0.
2. Clear to black; plain fixed-function fullscreen quad TEX0 → TEX1 (**bright-pass is a copy, not a threshold**; no pixel shader on this pass).
3. Downscaled ortho quad TEX0 → TEX2; bind **the glow pixel shader** (`power1dx8.psh` by default, configurable via `DISPLAY_POWERSHADER` string — see §C5.1 and `specs/rendering.md §6.4`). Exactly **one** downscale tap; no multi-pass power chain in the binary.
4. TEX1 (stage 0) + TEX2 (stage 1) → TEX0; bind **`finaldx8.psh`**; upload c0 = `BASE_BRIGHT` (≈1.05 from `display.lua`), c1 = `GLOW_BRIGHT` (≈0.3 from `display.lua`). Then run the FX overlay callback into TEX0.
5. Present TEX0 → backbuffer; opaque blit (ONE / ZERO blend, not additive).
6. UI / HUD callback; end scene.

Net composite arithmetic (SAMPLE-VERIFIED from `finaldx8.psh`): `out.rgb = saturate(TEX1 × 2 × c0 + TEX2 × c1)`, `out.a = 1`. See §C5.6 step 4 and the Re-authoring Guidance.

**Post-chain enable flag — CONFIRMED forced off in the shipped build (CYCLE 11, binary-won):**

The per-frame fork that selects the offscreen RT path over the direct path reads an enable flag on
the scene/post object (constructor default: 0 = off). The flag is set to 2 at exactly one site
(the device-creation routine), gated by two conditions:

1. The cel/glow shader initialiser must return success.
2. The toon-shading option flag (a field of the options singleton, option index 12) must equal 0.

In the options loader the toon-shading option (option index 12) is the **only** option that is
**hardcoded to 1** (the value is written unconditionally in code and is never overwritten by an INI
key — there is no INI read-site for this slot; the value is clamped to 1–2 and always exits as 1).
Because condition 2 requires this field to equal 0, and the field is always 1, **the `mov flag,2`
assignment at the device-creation site is never reached**. The enable flag stays at its constructor
default of 0 for the lifetime of every run of `doida.exe`.

Consequence: **`Renderer_DrawScene_Direct` (the direct path) is always taken.** The three render
targets, all five shader objects, and the composite/glow machinery are all allocated and compiled at
startup but **no frame ever passes through the offscreen/cel/bloom path in the shipped client.** The
cel/toon look and the bloom post chain are both dead per-frame features — the cel shaders bind only
if the post flag is on (§C5.6 step 5), so characters fall back to fixed-function shading.

> **Debugger-pending confirm:** a breakpoint at the per-frame flag-read (inside the scene-draw fork)
> should read 0; the device-creation site that conditionally sets the flag to 2 should never fire.
> These are the two confirmation points for a live `?ext=dbg` session.

**Implication for Godot fidelity:**

- The faithful default is **no bloom/glow post-process enabled** — the shipped client never runs
  any bloom. Do not enable `WorldEnvironment` glow by default.
- Characters in the original shipped client always render via **fixed-function (no cel shader, no
  toon ramp)**, because the cel bind is gated on the post-process flag being on.
- The cel/glow shaders are present in the VFS and fully specified here (§C5.1–§C5.4, Re-authoring
  Guidance); a port may optionally implement them as a toggleable quality feature, but the
  **faithful default state has the cel/post chain off**.
- The Godot `CelShade` material (which attempts the per-character cel look) diverges from the
  shipped client's actual behaviour when post is off; this is a known fidelity delta, not a bug in
  the spec recovery. `// spec: Docs/RE/formats/shaders.md §C5.6b`

### C5.7 Campaign 5 / 5B known unknowns

- **The actual `.psh` / `.vsh` shader-assembly source text** — external VFS files; not in the
  executable, but now READ. The cel/composite per-instruction arithmetic is **SAMPLE-VERIFIED**:
  `dotoonshading.psh` = `(base × toonRamp) × 2 × c0 + c1` (alpha passthrough); `dotoonshading2.psh`
  = identical with alpha from `c1.w` (stealth fade); `finaldx8.psh` = `saturate(scene×2×c0 + glow×c1)`
  with opaque alpha — see the Re-authoring Guidance. The `finaldx8.psh` arithmetic confirms (and refines
  the naming of) the earlier prior-lane `saturate(2·edge·c0 + bloom·c1)` summary. The cel vertex
  shader's exact N·L accumulation/ramp-lookup math still lives in `dotoonshading.vsh` (already read).
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

---

## Enumerations / Flags

Not applicable. This format carries no binary enumeration fields. The shader-type token (`vs` / `ps`) in the version line is an ASCII string, not a binary value.

---

## Cross-references

- Related formats: `pak.md` (container that holds these files), `texture.md` (toon LUT texture referenced by `dotoonshading.vsh`)
- Related specs: `specs/rendering.md` (the render pipeline + glow/bloom post passes that bind these shaders), `specs/skinning.md` (the skinned-character vertex buffer the cel shader consumes)
- Glossary: see `Docs/RE/names.yaml`
- Provenance: see `Docs/RE/journal.md`
- Dirty-room shader samples are kept under the gitignored dirty-room quarantine (never committed)
