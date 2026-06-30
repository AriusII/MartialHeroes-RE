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
> CYCLE 14 re-anchor (f61f66a9): 2 facts re-confirmed SAME (Renderer_InitCelGlowShaders 5-shader assemble order; D3DXAssembleShader caller census), 1 corrected (post-chain enable gate polarity: see §C5.6b — gate condition is option index 12 == 1, not == 0; gate PASSES on f61f66a9; bloom+cel structurally ON; old 263bd994 comparison or debugger confirm required to distinguish spec-misread from build flip).
> CYCLE 15 consumer-confirm (f61f66a9, 2026-06-30): `dotoonshading.psh` confirmed NOT an orphan — it is the 2nd of five runtime-assembled cel/post shaders, the normal-state cel pixel shader; loader and name binding consumer-confirmed (assembled at two sites: full-init loader and device-reset hot-reload; handle at renderer `+0x2B894` released and recreated on device reset; bound via SetPixelShader on the non-stealth cel-bind branch). The only assembled-but-discarded output in the cel set is `dotoonshading.vsh` (§C5.6b-ANOMALY, re-confirmed on f61f66a9). Confidence upgraded to CONSUMER-CONFIRMED. Runtime enable-flag reachability is a separate `[R-CAP]` debugger-confirm (non-blocking — does not affect the statically-confirmed loader/binding facts).
> **ida_reverified:** 2026-06-30 (CYCLE 15, f61f66a9); prior: 2026-06-27 (CYCLE 14, f61f66a9)
> **ida_anchor:** f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> **evidence:** [static-ida, vfs-sample]
> **conflicts:** NONE structural for shader-file content. CYCLE 14 correction: §C5.6b post-chain enable-flag gate polarity corrected (see that section). Wave-11 correction: toon ramp renderer offset corrected from +0x2B9BC to +0x2B9DC (static-confirmed). Prior conflicts note: Three refinements vs the earlier text (not reversals): encoding statement loosened (code tokens ASCII, comments may carry CP949); `power2dx8.psh` / `power4dx8.psh` not statically string-referenced; power ladder extended from 1/2/4 to 1/2/4/8/16/32 (three further taps physically verified on disk). The exact `toonramp.bmp` band layout remains the only open content item (see Known Unknowns).
> **wave-11 deep-dive (f61f66a9, 2026-06-28):** full shader-create API sequence resolved (exact D3DXAssembleShader arguments + PS-create idiom); VS-create ANOMALY discovered — the cel "vertex shader" handle is produced by calling `CreateVertexShader` with a D3DVERTEXELEMENT9 declaration array (not assembled `.vsh` bytecode), and the assembled `dotoonshading.vsh` token stream is released unused; glow/power PS handle (+0x2B6D4) and composite PS handle (+0x2B6D8) added to field map; all three RT texture/surface/RTS triples and glow divisors added; c4 light-direction source field offsets confirmed; composite PS-constant sources (+0x2BB48 / +0x2BB4C) and glow-path slot (+0x2BB54) added; per-pass D3DTSS/D3DSAMP/FVF cascades for bright-copy, glow-blur, composite, and present-blit passes fully decoded (see §C5.6c); §C5.6b gate-polarity open item subsumed by VS-create anomaly (see §C5.6b).
>
> **spec_status:** consumer_confirmed (10 shader files cross-confirmed; all five runtime-assembled shaders read; dotoonshading.psh loader and name binding consumer-confirmed on f61f66a9)
> **date:** 2026-06-11 (re-verified 2026-06-21; CYCLE 11 addition 2026-06-23; re-verified 2026-06-24; CYCLE 14 correction 2026-06-27; Deep-3D 2026 fixed-function pipeline addition 2026-06-28; wave-11 deep-dive 2026-06-28; CYCLE 15 consumer-confirm 2026-06-30)

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
| `dotoonshading.vsh`  | .vsh | vs.1.1 | Cel-shading vertex shader: world-view-projection transform, two-light Lambert diffuse, emits luminance-based UV on output texcoord 1 for toon LUT lookup. **Wave-11 note:** the runtime assembles this file but its bytecode is discarded — `CreateVertexShader` is called with a D3DVERTEXELEMENT9 declaration array instead (see §C5.6b-ANOMALY). Whether this shader ever executes on the live client is `[debugger-confirm]`. | VERIFIED (source content); runtime execution status [debugger-confirm] |
| `power1dx8.psh`      | .psh | ps.1.1 | Glow/bloom downsample pass 1: base texture sample, scale by `c0` | VERIFIED (string-referenced) |
| `dotoonshading.psh`  | .psh | ps.1.1 | Cel tone pixel shader — **normal** render state: `base × toonRamp`, ×2, brightness-modulated by per-state MULTI (`c0`) and ADD (`c1`); alpha passes through the lit value. The 2nd of five runtime-assembled shaders; assembled by both the full-init loader (device creation) and the device-reset cel-PS hot-reload; handle at renderer `+0x2B894` released and recreated on device reset. Bound via SetPixelShader on the non-stealth cel-bind branch. Not an orphan: loader, handle slot, and consumer all consumer-confirmed on f61f66a9. | CONSUMER-CONFIRMED |
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

**Wave-11 static deep-dive — exact API arguments (build f61f66a9):**

Each of the five shaders is assembled with flags = 0:
- **VFS branch:** `D3DXAssembleShader(bufferPtr, length, pDefines=0, pInclude=0, Flags=0, ppShader=&outBuffer, ppErrorMsgs=0)`
- **Disk fallback:** `D3DXAssembleShaderFromFileA(path, pDefines=0, pInclude=0, Flags=0, ppShader=&outBuffer, ppErrorMsgs=0)`

The returned `outBuffer` is an `ID3DXBuffer`. Its vtable: `Release` at slot 2 (+0x08); `GetBufferPointer` at slot 3 (+0x0C).

**Pixel-shader creation idiom (four PS, standard D3D9):** `bytecodePtr = ID3DXBuffer::GetBufferPointer(outBuffer)` → `hr = device->CreatePixelShader(bytecodePtr, &handleField)` (device vtable +0x1A8, slot 106) → `ID3DXBuffer::Release(outBuffer)` → `if (hr < 0) return 0`. This is the correct PS-create idiom.

**Vertex-shader creation — ANOMALY (see §C5.6b-ANOMALY):** the cel "vertex shader" does NOT follow the PS idiom. The assembled `dotoonshading.vsh` token buffer is released unused. Instead, `device->CreateVertexShader` (device vtable +0x16C, slot 91) is called with a hand-built **D3DVERTEXELEMENT9 declaration array** as its first argument. The produced handle is stored at renderer `+0x2B890` and later bound via `SetVertexShader`. A conformant D3D9 runtime rejects a declaration array in place of a shader token stream (which must begin with the version token `0xFFFE0101`) — this is the static mechanism most consistent with `Renderer_InitCelGlowShaders` failing at the first shader-create step.

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
| `data/shader/dotoonshading.psh` | Cel tone pixel shader — **normal** render state. Assembled at two sites: full-init loader (device creation) and cel-PS hot-reload (device reset); handle at renderer `+0x2B894` released and recreated on reset | The cel bind site (non-stealth branch; SetPixelShader) | CONSUMER-CONFIRMED |
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
3. It assembles the five shaders in order: `dotoonshading.vsh` → `dotoonshading.psh` → `dotoonshading2.psh` →
   `finaldx8.psh` → the editable-slot glow shader (default `power1dx8.psh`). The four pixel shaders
   use the standard idiom: assemble → `GetBufferPointer` → `CreatePixelShader` → release buffer.
   The cel vertex shader follows an anomalous path: the assembled `.vsh` buffer is released unused,
   and `CreateVertexShader` is instead called with a hand-built D3DVERTEXELEMENT9 declaration array
   (see §C5.6b-ANOMALY). Each shader handle is stored on the renderer; see the renderer field map above.
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
6. **Hot reload:** on a device reset, the partial reload first clears SetVertexShader(0), SetPixelShader(0), and SetTexture(stage 1, 0), then **releases the existing handles at renderer `+0x2B894` (`dotoonshading.psh`) and `+0x2B898` (`dotoonshading2.psh`)** and re-assembles exactly those two cel pixel shaders. The vertex shader handle, the composite and glow pixel-shader handles, and the three render targets are untouched. The Release-then-recreate of `+0x2B894` on device reset confirms it is a live, owned device object — not an orphaned or dead assembly.

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

**Post-chain enable flag — CYCLE 14 + wave-11 analysis:**

> **CYCLE 14 correction (f61f66a9, 2026-06-27) — prior CYCLE 11 "permanently off" finding REFUTED
> on this build.** The CYCLE 11 analysis (build 263bd994) concluded the enable flag was never set
> because gate condition 2 was read as requiring option index 12 to equal 0. CYCLE 14 static
> re-analysis of build f61f66a9 finds the gate condition is `option index 12 == 1` — and since the
> options loader hardcodes that slot to 1 unconditionally, the gate **passes** on every run.
>
> **Wave-11 deep-dive (2026-06-28) — VS-create ANOMALY subsumes the gate-polarity debate.** The
> CYCLE 14 "gate passes" finding was conditioned on `Renderer_InitCelGlowShaders` returning success
> (gate precondition 1). Wave-11 static analysis finds that this initialiser's first shader-create
> step calls `CreateVertexShader` (device vtable +0x16C) with a D3DVERTEXELEMENT9 declaration array
> as its token-stream argument. A conformant D3D9 runtime rejects a non-shader token stream (which
> lacks the `0xFFFE0101` vs.1.1 version header) with `D3DERR_INVALIDCALL` — so the `if (hr < 0) return 0`
> guard causes `Renderer_InitCelGlowShaders` to fail at this step on every run. Gate precondition 1
> therefore fails before precondition 2 is ever tested, the enable flag stays 0, and the world and
> all characters draw fixed-function. This is the static mechanism most consistent with the corpus
> observation that the engine is primarily fixed-function — and it makes the §C5.6b gate-polarity
> question (was it always == 1 or did the build flip it?) operationally moot: even if the gate
> passes, the initialiser's VS-create step fails first.
>
> **[debugger-confirm] [R-CAP] Tightened:** at the `CreateVertexShader` call inside `Renderer_InitCelGlowShaders`,
> read the return value in EAX (expected < 0). Then read renderer field `+0x2D67C` (expected 0) and
> `+0x2B890` (cel VS handle, expected null/zero). If EAX ≥ 0 and the enable flag becomes non-zero,
> the cel/post path is genuinely live and the D3DVERTEXELEMENT9 anomaly (§C5.6b-ANOMALY) must be
> re-examined. Either way, the live EAX of that `CreateVertexShader` call is the single decisive read.
> Non-blocking: the static loader and name binding of `dotoonshading.psh` are consumer-confirmed
> independently of this runtime read (see §C5.6 step 6 and CYCLE 15 banner note).

The per-frame fork that selects the offscreen RT path over the direct path reads an enable flag on
the scene/post object (constructor default: 0 = off). The flag is stored to 2 at **at least two
distinct call sites** within the device-creation context (see `specs/rendering.md §6.1` CYCLE 14
note), gated by two conditions:

1. The cel/glow shader initialiser (`Renderer_InitCelGlowShaders`) must return success.
2. The toon-shading option flag (a field of the options singleton, option index 12) must equal **1**.

In the options loader the toon-shading option (option index 12) is the **only** option that is
**hardcoded to 1** (the value is written unconditionally in code and is never overwritten by an INI
key — there is no INI read-site for this slot; the value is clamped to 1–2 and always exits as 1).
Because condition 2 requires this field to equal 1, and the field is always 1, **the flag-store
assignment in the device-creation context is reached whenever `Renderer_InitCelGlowShaders` succeeds**.
The enable flag is set to 2, and `Renderer_DrawScene_Fork` takes the offscreen/post path when this
flag is non-zero.

Consequence on build f61f66a9: **the offscreen/bloom/cel path is structurally enabled** whenever the
cel/glow shader initialiser succeeds at startup. The three render targets, all five shader objects,
and the composite/glow machinery are allocated, compiled, and the post flag is set so that
`Renderer_DrawScene_Fork` routes frames through `Renderer_DrawScene_OffscreenRT_0` — the per-frame
cel VS constant upload and the per-draw cel bind (ramp + VS + normal/stealth PS) both run.

> **Debugger-pending confirm (HIGH-STAKES):** a breakpoint at the per-frame flag-read (inside the
> scene-draw fork) should read **2** (CYCLE 14 finding) — not 0. A second breakpoint at the
> device-creation gate should fire and store 2 to the flag. These are the two live confirmation
> points for a `?ext=dbg` session. Also confirm by re-reading the gate comparison in the 263bd994
> IDB to resolve (A) spec-misread vs (B) build-flip.

**Implication for Godot fidelity (pending debugger confirmation):**

- On build f61f66a9 the bloom/glow post-process path appears **enabled** when the shaders load
  successfully — characters draw through the cel shader path and the bloom composite runs.
- Whether this was always the case in the real client (prior spec was a gate-polarity misread) or
  only in this build (a genuine flip) is the open question above; the answer changes the faithful
  default.
- Until the open question is resolved by debugger or 263bd994 IDB comparison: **do not change the
  Godot fidelity default based on this static finding alone**; keep the prior CYCLE 11 default (cel/post
  off) but flag it as pending review.
- The cel/glow shaders are fully specified here (§C5.1–§C5.4, Re-authoring Guidance); the Cel bind
  sub-paths (actor draw routines that test the same flag) also require confirmation that they test `!= 0`
  rather than `== 1` before treating them as fully on. `// spec: Docs/RE/formats/shaders.md §C5.6b`

### C5.6b-ANOMALY Cel vertex-shader create anomaly — D3DVERTEXELEMENT9 declaration array (wave-11)

The call to `CreateVertexShader` (device vtable +0x16C, slot 91) inside `Renderer_InitCelGlowShaders`
is passed a hand-built **D3DVERTEXELEMENT9 array** as its function-token pointer, not the assembled
`dotoonshading.vsh` bytecode. The array is built immediately before the call. The fully decoded
stream-0 declaration (each record is an 8-byte D3DVERTEXELEMENT9: Stream/Offset as WORDs, then
Type/Method/Usage/UsageIndex as BYTEs):

| # | Stream | Offset | Type | Method | Usage | UsageIdx | Meaning |
|--:|:------:|:------:|:----:|:------:|:-----:|:--------:|---------|
| 0 | 0 | 0  | 2 (FLOAT3)   | 0 | 0 (POSITION) | 0 | position at byte 0 |
| 1 | 0 | 12 | 4 (D3DCOLOR) | 0 | 10 (COLOR)   | 0 | diffuse colour at byte 12 |
| 2 | 0 | 16 | 1 (FLOAT2)   | 0 | 5 (TEXCOORD) | 0 | UV at byte 16 |
| 3 | 0 | 24 | 2 (FLOAT3)   | 0 | 0 (POSITION) | 1 | position1 at byte 24 |
| — | 0xFF | 0 | 17 (UNUSED) | 0 | 0 | 0 | D3DDECL_END |

The first dword of this array is `0x00000000` (Stream=0, Offset=0), not the `0xFFFE0101` version
token that must begin any valid vs.1.1 token stream. A conformant D3D9 runtime therefore rejects
this argument with `D3DERR_INVALIDCALL`, making `Renderer_InitCelGlowShaders` fail at the first
shader step and the cel/post enable flag (`+0x2D67C`) stay 0.

The assembled `dotoonshading.vsh` buffer is released immediately after this call — unused. The handle
stored at renderer `+0x2B890` is the (likely null or error-state) output of the `CreateVertexShader`
call, not a real compiled vertex shader. The cel-bind sub-routine binds this same handle via
`SetVertexShader`. `[debugger-confirm]` the live return value of the `CreateVertexShader` call and
the resulting handle value at `+0x2B890` — see §C5.6b.

### C5.6c Per-pass D3DTSS/D3DSAMP/FVF cascades — cel/post chain (wave-11 static deep-dive)

> Exact state tables for the four passes driven by `Renderer_DrawScene_OffscreenRT_0` when the
> cel/post enable flag is non-zero. D3D enum legend: see the "D3D enum decode legend" table.
> FVF 0x102 = XYZ|TEX1 (stride 20); FVF 0x202 = XYZ|TEX2 (stride 28).

#### Bright copy pass (scene TEX0 → bright TEX1)

- Clear(TARGET only, colour 0xFF000000, z=1.0). Z-test on, ZWrite off, lighting off, alpha-blend off.
- Stage 0: COLORARG1=TEXTURE(2), COLOROP=SELECTARG1(2), ALPHAOP=DISABLE(1).
- Stages 1/2: COLOROP=DISABLE(1), ALPHAOP=DISABLE(1).
- Sampler 0: MIN=LINEAR(2), MAG=LINEAR(2), MIP=LINEAR(2).
- FVF 0x102, stride 20; `DrawPrimitiveUP` (device vtable +332/slot 83), primCount 2.
- **No pixel shader** — the bright pass is a plain fixed-function copy of the scene RT. No threshold.

#### Glow blur pass (scene TEX0 → glow TEX2)

- Source stage-0 texture = scene RT (`+0x2B9E0 / TEX0`) — not the bright RT.
- Clear(TARGET, 0xFF000000, z=1.0). Z-test on, ZWrite off, lighting off, alpha-blend off.
- Stage 0: COLORARG1=TEXTURE(2), COLORARG2=TEXTURE(2), COLOROP=SELECTARG1(2),
  ALPHAARG1=TEXTURE(2), ALPHAOP=DISABLE(1).
- Sampler 0: MIN/MAG/MIP=LINEAR(2). FVF 0x102, stride 20.
- Binds glow/power pixel shader (`+0x2B6D4`) before draw; clears it (`SetPixelShader(0)`) after.
- **No PS constant uploaded** — the power shader's scale is a `def`-baked literal inside the `.psh`,
  not a runtime-uploaded constant. (See §C5.7: `power1dx8.psh` body = `tex t0; mul r0, t0, c0` where
  `c0` is the `def` literal.)
- Ortho dims = screenW÷glowDivX × screenH÷glowDivY (the downscale; divisors at `+0x2BA40`/`+0x2BA44`).

#### Composite pass (bright TEX1 + glow TEX2 → scene TEX0)

Renders back into the scene RT surface (`+0x2B9EC`) via its RTS helper. World/View = identity, ortho.
Z-test on, ZWrite off, lighting off, alpha-blend off.

Fixed-function fallback cascade (overridden when the finaldx8 PS is bound):
- Stage 0: COLORARG1=TEXTURE(2), COLORARG2=TEXTURE(2), COLOROP=ADDSIGNED(8), ALPHAOP=DISABLE(1).
- Stage 1: COLORARG1=TEXTURE(2), COLORARG2=CURRENT(1), COLOROP=ADDSMOOTH(11), ALPHAOP=DISABLE(1).
- Stage 2: COLOROP=DISABLE(1), ALPHAOP=DISABLE(1).

Samplers 0 & 1: MIN/MAG/MIP=LINEAR(2). FVF 0x202, stride 28.
- Upload PS `c0` = renderer `+0x2BB48` broadcast ×4 (BASE_BRIGHT); PS `c1` = `+0x2BB4C` broadcast ×4 (GLOW_BRIGHT).
- Bind finaldx8 PS (`+0x2B6D8`).
- Stage-0 texture = bright RT (`+0x2B9E4 / TEX1`); stage-1 texture = glow RT (`+0x2B9E8 / TEX2`).
- `DrawPrimitiveUP`, primCount 2.
- Teardown: reset stage-0 to SELECTARG1, clear stage-0/1 textures, `SetPixelShader(0)`.
- Net arithmetic (PS path, sample-verified from `finaldx8.psh`): `out.rgb = saturate(TEX1 × 2 × c0 + TEX2 × c1)`, `out.a = 1`.

#### Present blit pass (scene TEX0 → backbuffer)

- `BeginScene`; World/View=identity, ortho. Alpha-blend ON, SRCBLEND=ONE(2), DESTBLEND=ZERO(1) — opaque blit.
- Stage 0: COLORARG1=TEXTURE(2), COLORARG2=TEXTURE(2), COLOROP=SELECTARG1(2), ALPHAOP=DISABLE(1).
  Stage 1: COLOROP=DISABLE(1), ALPHAOP=DISABLE(1).
- FVF 0x102, stride 20; stage-0 texture = scene RT (`+0x2B9E0 / TEX0`).
- Overlay callback, optional FPS counter, `EndScene` (device vtable +168/slot 42).

### C5.7 Campaign 5 / 5B known unknowns

- **The actual `.psh` / `.vsh` shader-assembly source text** — external VFS files; not in the
  executable, but now READ. The cel/composite per-instruction arithmetic is **SAMPLE-VERIFIED**:
  `dotoonshading.psh` = `(base × toonRamp) × 2 × c0 + c1` (alpha passthrough); `dotoonshading2.psh`
  = identical with alpha from `c1.w` (stealth fade); `finaldx8.psh` = `saturate(scene×2×c0 + glow×c1)`
  with opaque alpha — see the Re-authoring Guidance. The `finaldx8.psh` arithmetic confirms (and refines
  the naming of) the earlier prior-lane `saturate(2·edge·c0 + bloom·c1)` summary. The cel vertex
  shader's exact N·L accumulation/ramp-lookup math still lives in `dotoonshading.vsh` (already read).
  **Wave-11 clarification:** the `power*dx8.psh` glow shaders take **no runtime PS constant upload** —
  the glow-blur pass does not call `SetPixelShaderConstantF` before drawing. The scale factor (`c0` in
  `power1dx8.psh`: `mul r0, t0, c0`) is a `def`-baked literal inside the `.psh` file, not a runtime
  constant. The exact `def c0` value for each power tap requires reading the on-disk `.psh` (VFS,
  not the executable). The prior re-authoring note "scale by c0" remains accurate but should be read
  as "scale by the `def`-baked `c0`", not a runtime-uploaded one.
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

## Fixed-Function Render-State Pipeline (Deep-3D 2026)

> Added from the Deep-3D 2026 static-analysis pass. This section records the fixed-function
> (D3D9 SetRenderState / SetTextureStageState / SetSamplerState / SetMaterial / SetTransform)
> render-state machine that draws the game world, and deepens §C5 with the precise device-call
> sequence for the programmable cel path. All facts are static-confirmed from decoded D3D
> literal constants unless tagged `[debugger-confirm]` or `[static-open]`.

### Overview — the engine is primarily fixed-function

Programmable `.psh`/`.vsh` shaders (§C5) are used for **one feature only**: the cel/toon character
path, gated by the post/offscreen enable flag at renderer field `+0x2D67C` (dword index `[44687]`).
When that flag is off, even characters draw through a fixed-function multitexture cascade.
Terrain, buildings, sky, water, particles, sword-light, lens-flare, and all visual effects are
**100% fixed-function** in all observed builds. This section resolves the recon's open items on
stage/sampler enum values (+268/+276) and on which draw path fires for each geometry class.

### Per-frame orchestration

`Renderer_DrawScene_Direct` runs the following sequence each frame:

1. Save viewport; set viewport.
2. `GDevice_ClearTargetAndZ` — clear TARGET and ZBUFFER (combined flag = 3), z = 1.0, stencil = 0.
   **No stencil-buffer clear.**
3. `GDevice_BeginScene`.
4. Install the default opaque texture-stage cascade (see §Default cascade below).
5. Invoke registered render-pass callbacks in order:
   - Sky / background pass.
   - World terrain and buildings pass.
   - Culled opaque scene (static meshes + characters) — inherits the default cascade.
   - Opaque-world extras, water, and decals pass.
   - Transparent and particles pass (chained inline here when the post flag is off; driven by
     `Renderer_DrawScene_OffscreenRT_0` when on — see §C5.6b).
   - Overlay / HUD callback, FPS, EndScene, Present.

Passes do **not** save/restore a full device state block. Each pass overwrites its required states
and relies on the subsequent pass to set what it needs.

### Device IDirect3DDevice9 vtable offset map

All render calls go through the device pointer at renderer field `+0x2B738` (dword index `[44494]`).
Offsets decoded from literal call displacements; slot = offset ÷ 4.

| Offset | Slot | D3D9 method |
|---:|---:|---|
| +32  | 8  | GetDisplayMode |
| +68  | 17 | Present |
| +104 | 26 | CreateVertexBuffer |
| +108 | 27 | CreateIndexBuffer |
| +164 | 41 | BeginScene |
| +168 | 42 | EndScene |
| +172 | 43 | Clear |
| +176 | 44 | SetTransform |
| +180 | 45 | GetTransform |
| +188 | 47 | SetViewport |
| +192 | 48 | GetViewport |
| +196 | 49 | SetMaterial |
| +228 | 57 | SetRenderState |
| +260 | 65 | SetTexture |
| +268 | 67 | SetTextureStageState |
| +276 | 69 | SetSamplerState |
| +328 | 82 | DrawIndexedPrimitive |
| +332 | 83 | DrawPrimitiveUP |
| +336 | 84 | DrawIndexedPrimitiveUP |
| +356 | 89 | SetFVF |
| +364 | 91 | CreateVertexShader |
| +368 | 92 | SetVertexShader |
| +376 | 94 | SetVertexShaderConstantF |
| +400 | 100 | SetStreamSource |
| +416 | 104 | SetIndices |
| +424 | 106 | CreatePixelShader |
| +428 | 107 | SetPixelShader |
| +436 | 109 | SetPixelShaderConstantF |

This corroborates the existing recon vtable key (Clear at +172, etc.) and resolves the prior open
items: +268 = SetTextureStageState, +276 = SetSamplerState.

### IDB state-thunk name polarity corrections

Several IDB-assigned canonical names for render-state wrapper functions are **misleading about
enable/disable polarity**. The true operation was decoded from the literal (state, value) bytes in
each function body. Use the "True D3D call" column; disregard the name polarity.

| Canonical name | True D3D call | Note |
|---|---|---|
| `GDevice_SetLightingEnable` | SetRenderState(LIGHTING=137, **0**) | Name implies enable; actually DISABLES |
| `GDevice_SetRenderState137_On` | SetRenderState(LIGHTING=137, **1**) | Enables lighting |
| `GDevice_SetZBufferEnable` | SetRenderState(ZENABLE=7, **0**) | Name implies enable; actually DISABLES |
| `RenderDevice_SetRenderState7_On` | SetRenderState(ZENABLE=7, **1**) | Enables z-test |
| `GDevice_DisableZWrite` | SetRenderState(ZWRITEENABLE=14, **0**) | Disables z-write |
| *(z-write-on thunk — no canonical name assigned)* | SetRenderState(ZWRITEENABLE=14, **1**) | Enables z-write |
| `GDevice_SetAlphaTestEnable` | SetRenderState(ALPHATESTENABLE=15, **0**) | Name implies enable; actually DISABLES |
| `GDevice_EnableAlphaTest` | SetRenderState(ALPHATESTENABLE=15, **1**) | Enables alpha test |
| `GDevice_SetAlphaBlendEnable` | SetRenderState(ALPHABLENDENABLE=27, **1**) | Enables blend |
| `GDevice_DisableAlphaBlend` | SetRenderState(ALPHABLENDENABLE=27, **0**) | Disables blend |
| `GDevice_SetSrcBlend` | SetRenderState(SRCBLEND=19, arg) | Arg-driven |
| `GDevice_SetDestBlend` | SetRenderState(DESTBLEND=20, arg) | Arg-driven |
| `GDevice_SetCullMode` | SetRenderState(CULLMODE=22, arg) | Arg-driven |
| `GDevice_SetFillMode` | SetRenderState(FILLMODE=8, arg) | Arg-driven |
| *(shade-mode thunk — no canonical name assigned)* | SetRenderState(SHADEMODE=9, arg) | Arg-driven |
| `GDevice_EnableFog` | SetRenderState(FOGENABLE=28, 1) | Enables fog |
| `RenderDevice_DisableFog` | SetRenderState(FOGENABLE=28, 0) | Disables fog |
| `GDevice_SetDitherEnable` | SetRenderState(DITHERENABLE=26, **0**) | Name implies enable; actually DISABLES |

**Transform-thunk corrections.** Transform wrapper functions carry incorrect names in the IDB:
- `RenderDevice_GetWorldTransform` actually issues **SetTransform(WORLD = 256, matrix)**.
- A "SetTransform_World0" thunk actually issues **SetTransform(PROJECTION = 3, matrix)**.
- "SetTransform_View" thunks issue **SetTransform(VIEW = 2, matrix)** and set a dirty flag at
  renderer field `+0x2B9A0`.
- The terrain world matrix is the **identity** — terrain vertices are pre-baked in world space;
  no per-frame world-space transform is applied on the GPU.

### D3D enum decode legend

Values in the pass state tables use the following D3D9 constant mappings, decoded from literal
call bytes:

- **D3DTOP (COLOROP/ALPHAOP):** 1=DISABLE 2=SELECTARG1 3=SELECTARG2 4=MODULATE 5=MODULATE2X
  6=MODULATE4X 7=ADD 8=ADDSIGNED 11=ADDSMOOTH.
- **D3DTA (texture argument):** 0=DIFFUSE 1=CURRENT 2=TEXTURE 3=TFACTOR 4=SPECULAR.
- **D3DTSS index:** 1=COLOROP 2=COLORARG1 3=COLORARG2 4=ALPHAOP 5=ALPHAARG1 6=ALPHAARG2
  11=TEXCOORDINDEX 24=TEXTURETRANSFORMFLAGS.
- **D3DSAMP index:** 1=ADDRESSU 2=ADDRESSV 5=MAGFILTER 6=MINFILTER 7=MIPFILTER.
- **D3DTEXF (filter):** 0=NONE 1=POINT 2=LINEAR 3=ANISOTROPIC.
- **D3DTADDRESS (wrap mode):** 1=WRAP 2=MIRROR 3=CLAMP 4=BORDER.
- **D3DBLEND:** 1=ZERO 2=ONE 3=SRCCOLOR 4=INVSRCCOLOR 5=SRCALPHA 6=INVSRCALPHA 7=DESTALPHA
  8=INVDESTALPHA 9=DESTCOLOR 10=INVDESTCOLOR 11=SRCALPHASAT.
- **D3DCULL:** 1=NONE 2=CW 3=CCW. **D3DFILL:** 1=POINT 2=WIREFRAME 3=SOLID. **D3DSHADE:** 1=FLAT 2=GOURAUD 3=PHONG.
- **D3DTS (transform state):** 2=VIEW 3=PROJECTION 16=TEXTURE0 17=TEXTURE1 256=WORLD.
- **D3DTTFF:** 0=DISABLE; COUNT1–COUNT4 = 1–4; PROJECTED = 256; combined 260 = PROJECTED|COUNT4
  (used for the terrain dynamic shadow stage).
- **D3DTSS_TCI mode bits:** 0=PASSTHRU; 0x10000=CAMERASPACENORMAL; 0x20000=CAMERASPACEPOSITION.

### Default opaque texture-stage cascade

Before the culled opaque scene is drawn, `Renderer_DrawScene_Direct` installs this base cascade —
the engine's signature "MODULATE2X" output:

| Stage | COLOROP | COLORARG1 | COLORARG2 | ALPHAOP |
|---:|---|---|---|---|
| 0 | MODULATE2X (5) | TEXTURE (2) | DIFFUSE (0) | DISABLE (1) |
| 1 | DISABLE (1) | — | — | DISABLE (1) |
| 2 | DISABLE (1) | — | — | DISABLE (1) |

Net stage-0 color = `texture.rgb × vertex_diffuse.rgb × 2`; alpha from vertex diffuse.
**Sampler 0:** MINFILTER=ANISOTROPIC (3), MAGFILTER=ANISOTROPIC (3), MIPFILTER=LINEAR (2).
This resolves the prior recon open item on the "+268/+276 stage/sampler block" encoding.

### Per-pass render-state recipes

#### Sky / background pass (`RenderPass_SkyAndBackground`)

Drawn first with no depth buffer, so sky fills the entire framebuffer before world geometry.

- **Render states:** LIGHTING=0, FOGENABLE=0, ZWRITEENABLE=0, ZENABLE=0, ALPHABLENDENABLE=0,
  ALPHATESTENABLE=0, FILLMODE=SOLID (3), CULLMODE=NONE (1), SHADEMODE=GOURAUD (2),
  POINTSPRITEENABLE=0, POINTSCALEENABLE=0.
- **Samplers 0/1/2:** MIN/MAG/MIP=LINEAR (2), ADDRESSU/V=WRAP (1).
- **Sky model meshes:** stage-0 COLORARG1=TEXTURE, COLOROP=SELECTARG1 (2) — unlit, texture-only;
  CULLMODE=CW (2) for the closed sky mesh faces.
- **Stardome:** WORLD set to a sky-locked (camera-following) matrix; ALPHABLENDENABLE=1,
  SRCBLEND=SRCALPHA (5), DESTBLEND=ONE (2) — **additive blending**.
- **Weather and cloud billboards:** SRCBLEND=SRCALPHA (5), DESTBLEND=INVSRCALPHA (6) — alpha blend;
  cloud dome uses the same blend.

#### World terrain and buildings pass (`RenderPass_WorldTerrainAndBuildings`)

- WORLD=identity; default MODULATE2X cascade active; sampler-0 MIN/MAG/MIP=LINEAR (2) — terrain
  uses LINEAR, not anisotropic.
- Per-frame `GDevice_SetMaterial` with the opaque D3DMATERIAL9 record (zero-initialized in static
  data, values set at runtime — `[debugger-confirm]` for exact diffuse/ambient/specular).
- LIGHTING=1, FOGENABLE=1, ZWRITEENABLE=1, ZENABLE=1, ALPHABLENDENABLE=0, ALPHATESTENABLE=0,
  CULLMODE=CW (2).
- **Near buildings:** sampler-0 ADDRESSU/V=WRAP (1).
- **Far buildings:** sampler-0 ADDRESSU/V=MIRROR (2).

**Terrain ground — two-stage multitexture with projected dynamic shadow:**

- SetTexture(stage 1) = the runtime dynamic shadow texture. Stage-1 transform matrix = the dynamic
  shadow projection matrix (saved around the draw; restored afterward).
- Stage 1: TEXCOORDINDEX=CAMERASPACEPOSITION (0x20000); TEXTURETRANSFORMFLAGS=PROJECTED|COUNT4
  (260); COLORARG1=TEXTURE (2), COLORARG2=CURRENT (1), COLOROP=MODULATE (4), ALPHAOP=DISABLE (1).
- Sampler 1: MAGFILTER=NONE (0), MIPFILTER=NONE (0), ADDRESSU/V=CLAMP (3) — shadow does not tile.
- Sampler-0 ADDRESSU/V=MIRROR (2) during the ground draw; restored to WRAP (1) afterward.
- **Net terrain shading:** `(ground_texture × diffuse × 2) × projected_shadow_texture` — the
  stage-0 MODULATE2X base multiplied by the camera-space-position-projected dynamic shadow on stage 1.
- After the draw, stage-1 texture, transform, TEXCOORDINDEX, and TEXTURETRANSFORMFLAGS are cleared;
  sampler 1 reverts to LINEAR.
- Between-layer terrain texture blending (where different ground textures meet) is handled as a
  separate alpha-blended overlay in `RenderPass_OpaqueWorld`, not in this pass.

#### Opaque-world extras, water, and decals pass (`RenderPass_OpaqueWorld`)

- LIGHTING=1; default MODULATE2X cascade; sampler-0 LINEAR + WRAP.
- Per-frame `GDevice_SetMaterial` with the opaque D3DMATERIAL9 record. ZWRITEENABLE=0; ZENABLE=1;
  ALPHATESTENABLE=0; ALPHABLENDENABLE=1; D3DRS_TEXTUREFACTOR (60) = 0x50505050 (ARGB 80,80,80,80).
- **Alpha-blended terrain/building overlay sub-pass** (between-layer texture blend):
  stage-0 ALPHAARG1=TEXTURE, ALPHAOP=MODULATE (4); SRCBLEND=SRCALPHA (5),
  DESTBLEND=INVSRCALPHA (6); draws terrain-layer alpha overlay geometry.
- CULLMODE=NONE (1). Per-frame `GDevice_SetMaterial` with the effects D3DMATERIAL9 record
  (zero-initialized in static data, values set at runtime — `[debugger-confirm]`). A two-stage
  animated/scrolling block: stage-0 COLOROP=MODULATE (4); stage-1 with its own
  TEXTURETRANSFORMFLAGS and COLOROP=MODULATE2X (5), ALPHAOP=ADDSMOOTH (11); draws water, effects,
  and sky-decal geometry.
- **Final extras sub-pass:** ALPHATESTENABLE=1; textured screen quads drawn via the alpha-test path.
  LIGHTING=0, FOGENABLE=0, sampler-0 MIPFILTER=NONE (0); `ActorShadow_DrawBlobQuads` (blob shadow
  billboards).

**Post-path coupling (within this pass):** when the post/offscreen enable flag (`+0x2D67C`, dword
`[44687]`) is **off**, `RenderPass_TransparentAndParticles` is called inline at the end of this
pass; when **on**, `Renderer_DrawScene_OffscreenRT_0` drives the transparent pass separately (§C5.6b).

#### Transparent and particles pass (`RenderPass_TransparentAndParticles`)

- Per-frame `GDevice_SetMaterial` with the opaque D3DMATERIAL9 record. CULLMODE=NONE (1),
  ZWRITEENABLE=0, ZENABLE=1, ALPHATESTENABLE=0, LIGHTING=0, FOGENABLE=0, ALPHABLENDENABLE=1;
  D3DRS_TEXTUREFACTOR (60) = 0xFF505050.
- **First effect batch** (cross-effects and joint effects): SRCBLEND=ONE (2), DESTBLEND=INVSRCCOLOR (4).
- Then SRCBLEND=SRCALPHA (5), DESTBLEND=INVSRCALPHA (6); SetFVF = 0x142 (XYZ|DIFFUSE|TEX1 = 24 B).
- **Sword-light effects:** SRCBLEND=DESTCOLOR (9), DESTBLEND=SRCALPHA (5) — modulate-style glow.
- **Sky / weather particles:** SRCBLEND=SRCALPHA (5), DESTBLEND=INVSRCALPHA (6).
- **Particle effect list draw.**
- **Lens-flare:** SRCBLEND=SRCALPHA (5), DESTBLEND=ONE (2) — **additive**.

**Blend recipe summary by effect class:**

| Effect class | SRCBLEND | DESTBLEND | Mode |
|---|---|---|---|
| Opaque / glass | SRCALPHA (5) | INVSRCALPHA (6) | Standard alpha blend |
| Stardome / lens-flare / particles | SRCALPHA (5) | ONE (2) | Additive |
| First xeffect batch | ONE (2) | INVSRCCOLOR (4) | Inverse-colour additive |
| Sword-light | DESTCOLOR (9) | SRCALPHA (5) | Modulate-style glow |

### Geometry submission and vertex formats (FVF)

Most world geometry is drawn with `RenderDevice_DrawIndexedPrimitiveUP` (user-pointer streams, no
managed vertex buffer) after setting FVF via `RenderDevice_SetFVF`. The programmable character path
uses a managed vertex buffer and index buffer. All primitives are **TRIANGLELIST**,
**INDEX16** (D3DFMT_INDEX16).

| Geometry class | FVF value | Vertex layout | Stride | Draw call |
|---|---|---|---:|---|
| Terrain ground subtile | 0x252 (XYZ\|NORMAL\|DIFFUSE\|TEX2) | pos 12 B + normal 12 B + diffuse 4 B + 2 UV sets 16 B | 44 B | `RenderDevice_DrawIndexedPrimitiveUP` (`TerrainSection_DrawLayer`) |
| Building / mass object | 0x112 (XYZ\|NORMAL\|TEX1) | pos 12 B + normal 12 B + UV 8 B | 32 B | `RenderDevice_DrawIndexedPrimitiveUP` (`BuildingTree_CullAndDraw`) |
| Transparent / particles | 0x142 (XYZ\|DIFFUSE\|TEX1) | pos 12 B + diffuse 4 B + UV 8 B | 24 B | `RenderDevice_DrawIndexedPrimitiveUP` (transparent pass) |
| Character (programmable cel) | vertex declaration (not FVF) | XYZ 12 B + normal 12 B + TEXCOORD0 8 B | 32 B | `RenderDevice_DrawIndexedPrimitive` (`Actor_DrawSkinnedCelWithTint`) |

**Terrain ground subtile geometry (byte-verified):** each subtile is a 5×5 vertex grid — 25 vertices
× 44 bytes = 1 100 bytes total. A shared index template holds 96 unsigned-16 indices forming
32 triangles over the 4×4 quad grid of the 5×5 vertex patch; `GroundBlend_FillCellIndices` adds a
per-subtile base offset to each index. The vertex data is assembled by `GroundBlend_CopyCellVertexBlock`
(the copy length confirms 1 100 bytes). The second UV set (the trailing 16-byte slot in the 44-byte
stride) feeds the stage-1 projected shadow texture coordinate.

**Vertex and index buffer creation:** `CreateVertexBuffer` (vtable +104) and `CreateIndexBuffer`
(vtable +108) wrapper functions each take (length, usage, FVF/format, pool, out-handle) and null
the out-handle on failure. They are called from mesh, particle, and lens-flare initialization paths.

### Fixed-function character path (`Character_DrawSkinnedCelShaded`)

When the post/offscreen enable flag (`+0x2D67C`, dword `[44687]`) is **off** (or the cel initialiser
failed), `Actor_DrawSkinnedCelWithTint` tail-calls `Character_DrawSkinnedCelShaded` — a CPU-skinned,
fixed-function multitexture draw:

1. Build pose (`AnimMixer_BuildPose`, `Actor_EvaluatePoseForRender`); CPU deform and upload
   (`SkinSet_DeformAndUpload`).
2. **Body (lit) pass — texture-stage cascade:**
   - Stage 0: COLORARG1=TEXTURE (2), COLORARG2=DIFFUSE (0), COLOROP=SELECTARG1 (2),
     ALPHAOP=DISABLE (1).
   - Stage 1: COLORARG1=TEXTURE (2), COLOROP=SELECTARG1 (2) **if** a global second-texture-layer
     flag is set (outfit / detail overlay; writer unknown — see §Open questions below), else
     COLOROP=DISABLE (1); ALPHAOP=DISABLE (1).
   - Stage 2: DISABLE / DISABLE.
   - Sampler 0: MIN/MAG/MIP=LINEAR (2), ADDRESSU/V=WRAP (1). Sampler 1: ADDRESSU/V=CLAMP (3).
   - Draw each character sub-mesh via its draw dispatch.
3. **Overlay / blend pass:**
   - ZENABLE=0, LIGHTING=0, FOGENABLE=0, DITHERENABLE=0, ALPHATESTENABLE=0,
     ALPHABLENDENABLE=1, SRCBLEND=SRCALPHA (5), DESTBLEND=INVSRCALPHA (6).
   - Stage 0: COLOROP=MODULATE (4), ALPHAARG1=TEXTURE, ALPHAARG2=DIFFUSE, ALPHAOP=MODULATE (4).
   - Sampler 0: MIN/MAG=POINT (1), MIPFILTER=NONE (0).

### Programmable cel bind — device-call sequence (deepens §C5.6)

`Actor_DrawSkinnedCelWithTint` is the programmable character draw. It runs **only** when renderer
field `+0x2D67C` (dword `[44687]`) is **non-zero**; otherwise it tail-calls
`Character_DrawSkinnedCelShaded`. When the flag is non-zero, the following device calls execute:

1. **MVP upload to VS constants c0–c3.** Read WORLD (D3DTS 256), VIEW (D3DTS 2), and PROJECTION
   (D3DTS 3) via GetTransform (vtable +180); compute MVP = WORLD × VIEW × PROJECTION; transpose via
   D3DXMatrixTranspose; upload to VS register 0, count 4, via SetVertexShaderConstantF (vtable +376).
   The cel VS (`dotoonshading.vsh`) receives the transposed MVP in c0 for its `m4x4 oPos` transform.
2. **State index.** Derive a 0–8 integer from the actor's one-hot state field (maps to the nine
   brightness states of §C5.5).
3. **PS constants.** Upload PS c0 = `(mulR, mulG, mulB, 1.0)` and PS c1 = `(addR, addG, addB, addW)`
   via SetPixelShaderConstantF (vtable +436), count 1 each. Source arrays on the renderer:
   mulR at dword `[44691]`, mulG at `[44700]`, mulB at `[44709]`, addR at `[44718]`,
   addG at `[44727]`, addB at `[44736]`, addW at `[44745]` (nine entries each).
   This provides the concrete memory layout for the per-state tint system described in §C5.5.
4. **Blend states.** FOGENABLE=0; ALPHABLENDENABLE=1; SRCBLEND=SRCALPHA (5),
   DESTBLEND=INVSRCALPHA (6).
5. **Shader bind** (the cel-bind sub-routine, parameterised by a per-actor stealth flag):
   - SetTexture(stage 1) = toon ramp LUT at renderer `+0x2B9DC`.
   - SetVertexShader (vtable +368) = cel VS handle at renderer `+0x2B890`.
   - SetPixelShader (vtable +428) = stealth PS (renderer `+0x2B898`) if stealth flag set, else
     normal PS (renderer `+0x2B894`). Matches the two `.psh` variants of §C5.1.
6. **Draw.** `RenderDevice_SetStreamSource` (stream 0, vertex buffer, stride 32);
   `RenderDevice_SetIndices`; `RenderDevice_DrawIndexedPrimitive` (TRIANGLELIST).
7. **Unbind** (the cel-unbind sub-routine): SetVertexShader(0), SetPixelShader(0),
   SetTexture(stage 1, 0).

**Renderer field map (cel/post-chain) — wave-11 expanded:**

| Field | Renderer offset | Content |
|---|---|---|
| Screen width | `+0x2B6C4` | Backbuffer width (used for RT creation) |
| Screen height | `+0x2B6C8` | Backbuffer height |
| Glow/power PS handle | `+0x2B6D4` | Compiled glow shader (default `power1dx8.psh`); bound in glow-blur pass |
| Composite PS handle | `+0x2B6D8` | Compiled `finaldx8.psh`; bound in composite pass |
| RTS depth-stencil format | `+0x2B724` | D3DFORMAT value used as depth fmt for `D3DXCreateRenderToSurface` |
| Device pointer | `+0x2B738` (dword [44494]) | IDirect3DDevice9* |
| View-dirty flag | `+0x2B9A0` | Set by SetTransform(VIEW) wrapper |
| Default / white texture | `+0x2B9B8` | Fallback white texture |
| Cel VS / decl handle | `+0x2B890` | Handle produced by `CreateVertexShader` called with a D3DVERTEXELEMENT9 array (see §C5.6b-ANOMALY); bound via `SetVertexShader` in the cel-bind sub-routine |
| Cel PS normal handle | `+0x2B894` | Compiled `dotoonshading.psh` device object |
| Cel PS stealth handle | `+0x2B898` | Compiled `dotoonshading2.psh` device object |
| Toon ramp texture | `+0x2B9DC` | `data/shader/toonramp.bmp` device texture (corrected from +0x2B9BC — wave-11 static-confirm) |
| Scene RT texture (TEX0) | `+0x2B9E0` | Scene/cel capture; also composite destination and present source |
| Bright RT texture (TEX1) | `+0x2B9E4` | Plain fixed-function copy of scene (bright-copy pass) |
| Glow RT texture (TEX2) | `+0x2B9E8` | Downscaled glow-blur result |
| Scene RT surface | `+0x2B9EC` | `GetSurfaceLevel(0)` of TEX0 |
| Bright RT surface | `+0x2B9F0` | `GetSurfaceLevel(0)` of TEX1 |
| Glow RT surface | `+0x2B9F4` | `GetSurfaceLevel(0)` of TEX2 |
| Scene RTS helper | `+0x2B9F8` | `ID3DXRenderToSurface` for TEX0 |
| Bright RTS helper | `+0x2B9FC` | `ID3DXRenderToSurface` for TEX1 |
| Glow RTS helper | `+0x2BA00` | `ID3DXRenderToSurface` for TEX2 |
| c4 light dir X | `+0x2BA04` | Live light-direction x → VS `c4.x` (constructor default −1) |
| c4 light dir Y | `+0x2BA08` | → VS `c4.y` (default 0) |
| c4 light dir Z | `+0x2BA0C` | → VS `c4.z` (default 0); `c4.w` is forced 0 at upload |
| Glow divisor X | `+0x2BA40` | screenW ÷ this = glow RT pixel width |
| Glow divisor Y | `+0x2BA44` | screenH ÷ this = glow RT pixel height |
| Composite PS c0 source | `+0x2BB48` | BASE_BRIGHT scalar → finaldx8 PS `c0` broadcast ×4 (≈1.05 from display.lua) |
| Composite PS c1 source | `+0x2BB4C` | GLOW_BRIGHT scalar → finaldx8 PS `c1` broadcast ×4 (≈0.3 from display.lua) |
| Glow shader path slot | `+0x2BB54` | Editable filename string; constructor default `power1dx8.psh` |
| Post / cel enable flag | `+0x2D67C` (dword [44687]) | 0 = FF path; non-zero = cel/post path (§C5.6b) |
| PS tint mulR [0..8] | dword [44691] | Per-state MULTI R, 9 entries |
| PS tint mulG [0..8] | dword [44700] | Per-state MULTI G, 9 entries |
| PS tint mulB [0..8] | dword [44709] | Per-state MULTI B, 9 entries |
| PS tint addR [0..8] | dword [44718] | Per-state ADD R, 9 entries |
| PS tint addG [0..8] | dword [44727] | Per-state ADD G, 9 entries |
| PS tint addB [0..8] | dword [44736] | Per-state ADD B, 9 entries |
| PS tint addW [0..8] | dword [44745] | Per-state ADD W, 9 entries (drives stealth fade via `dotoonshading2.psh` `c1.w`) |

### Render-math details for a faithful port

- **Coordinate handedness.** D3D9 left-handed Y-up. VIEW = inverse-orthonormal of the camera world
  matrix. WORLD, VIEW, and PROJECTION are separate FF transforms (D3DTS 256, 2, 3). Terrain
  WORLD = identity.
- **Default cull = CW.** Opaque world and terrain use D3DCULL_CW (2). Sky, transparent, and the
  opaque-extras water/effects block use CULLMODE=NONE (1). For a CCW-front Godot port with negated
  Z, the effective front face must match this CW convention — verify against the recovered geometry Z
  and mesh X negation documented in `CLAUDE.md`.
- **MODULATE2X is load-bearing.** The ×2 brightness factor lives in the fixed-function texture
  stage, not in any shader. Omitting it renders the world at half brightness.
- **No stencil clear.** Clear passes flags = TARGET|ZBUFFER (3), z = 1.0; stencil is never
  explicitly cleared.
- **No programmable terrain or world shaders.** Only the five hand-written ps.1.1/vs.1.1 assembly
  files (§C5.1) exist, and they are used only on the cel/post character path.

### Open questions (Deep-3D 2026)

1. `[debugger-confirm]` `[R-CAP]` Runtime return of `CreateVertexShader` (device vtable +0x16C) inside
   `Renderer_InitCelGlowShaders` and the resulting value of renderer field `+0x2D67C` (post/cel
   enable flag). Wave-11 static analysis predicts the call returns `D3DERR_INVALIDCALL` (< 0)
   because the argument is a D3DVERTEXELEMENT9 declaration array, not a shader token stream —
   causing the initialiser to return 0, the enable flag to stay 0, and the whole world to draw
   fixed-function. Confirm by reading EAX after the call and reading `+0x2D67C` and `+0x2B890`
   (cel VS/decl handle) live. If EAX ≥ 0 and the flag becomes non-zero, the VS anomaly (§C5.6b-ANOMALY)
   must be re-examined. This is the single decisive read for the cel/post path status. Non-blocking:
   the `dotoonshading.psh` loader and name binding are consumer-confirmed independently (CYCLE 15).
2. `[debugger-confirm]` Live value of VS constant `c4` (light direction): source fields `+0x2BA04`/
   `+0x2BA08`/`+0x2BA0C` (x/y/z). Constructor default is (−1, 0, 0, 0); gameplay or config may
   overwrite the three scalar fields. The upload mechanism and source offsets are now static-confirmed;
   only the live triple is unknown.
3. `[debugger-confirm]` Runtime contents of the two D3DMATERIAL9 records (opaque and effects) used
   by the LIGHTING=1 world passes — diffuse, ambient, and specular channels. Both are
   zero-initialized in static data and written per-frame; read them live after the per-frame setter.
4. `[static-open]` The global second-texture-layer flag in `Character_DrawSkinnedCelShaded` —
   enables the stage-1 outfit/detail overlay for FF characters. Trace the writer to determine what
   activates it (outfit slot change, LOD threshold, or config key).
5. `[static-open]` Exact D3DTTFF count for the terrain shadow stage-1 TEXTURETRANSFORMFLAGS value
   (decoded as PROJECTED|COUNT4 = 260). Confirm the literal immediate byte before relying on the
   projected-coordinate count; PROJECTED|COUNT3 (259) is an alternative if the byte differs.
6. `[static-open]` Whether the culled opaque static-mesh draw (cull-set node draw dispatch)
   inherits the frame-body default cascade or sets its own FVF and material — trace
   `StaticSkin_BuildRenderNode` and the cull-set node draw vtable call.

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
