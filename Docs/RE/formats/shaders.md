# Format: .psh / .vsh  (Direct3D 9 shader assembly source text)

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers / Assets.Mapping. Every offset an engineer cites must reference this file.
>
> **spec_status:** sample_verified (4 samples cross-confirmed against format description)
> **date:** 2026-06-11

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
2. **Unverified shader files:** `dotoonshading.psh`, `dotoonshading2.psh`, and `finaldx8.psh` were not in the extracted sample set. Their format is strongly inferred to be identical based on the shared load path, but has not been sample-confirmed.
3. **Shader file completeness:** Only seven shader filenames are known (from the game's internal string table). Whether additional shader files exist for other effects (e.g. character effects, weather, UI) has not been confirmed. The power progression is 1/2/4 — a `power3dx8.psh` is not referenced in known strings.
4. **Other shader model versions:** Only version `1.1` has been observed. Whether any shader files use `vs.1.0`, `ps.1.4`, `vs.2.0`, or any other model is unknown.
5. **D3DX flags:** All observed load sites use flags value `0`. Whether any code path uses `D3DXSHADER_DEBUG` or another flag in a debug build is unknown.

---

## Enumerations / Flags

Not applicable. This format carries no binary enumeration fields. The shader-type token (`vs` / `ps`) in the version line is an ASCII string, not a binary value.

---

## Cross-references

- Related formats: `pak.md` (container that holds these files), `texture.md` (toon LUT texture referenced by `dotoonshading.vsh`)
- Glossary: see `Docs/RE/names.yaml`
- Provenance: see `Docs/RE/journal.md`
- Dirty-room samples: `Docs/RE/_dirty/samples/data/shader/` (gitignored, do not commit)
