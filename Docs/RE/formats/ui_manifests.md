---
verification: confirmed
ida_reverified: 2026-06-20
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: texture-load flag 0x35540004 semantics (value confirmed, meaning capture/debugger-pending); first-paint font slot index (capture/debugger-pending); .do class-stance stride 116B (0x74) — RESOLVED (CYCLE 1 A3-6): config_tables.md now CONFIRMS 116 too and REFUTES the 166B estimate; char-select corner close-button atlas — RESOLVED (CYCLE 1 A3-7): binds data/ui/tradekeepwindow.dds (1024×1024) at src (941,910,23,23) dst (971,610); blacksheet 512×512 overflow + loginwindow/mainwindow candidates REFUTED — 2026-06-20 CYCLE 7 (IDB SHA 263bd994): added §2.8 — the `.do` stance-manifest SELECTION function and its class-index classStanceRef (Musa=1, Assassin=2, Wizard=3, Monk=4) with the {jung, sa, ma} file triplet per class, and the stanceType 0/1/2 + tier-sign selection rule; this resolves the *selection-by-class* half of §9 item #11b (the on-disk +0x0C discriminator enumeration for non-Musa files stays UNVERIFIED — two distinct quantities both loosely called "classStanceRef") — 2026-06-21 (IDB SHA 263bd994): added §2.9 — `data/script/emoticon.do`, the 40-byte (0x28) chat-emoticon picker sub-family (EOF-driven loader, dual id/index maps, full record layout incl. the +0x04 low-byte pageId padding caveat, the four-widget page builder joining UiTex id 27=emoticon.dds + id 3 chrome, and the +0x0C emoteCode click dispatch); establishes that `.do` is NOT one format but ≥3 distinct fixed-stride tables (40B emoticon / 108B errorinfo-msginfo / 116B stance) disambiguated by filename + loader, never by extension
---

# Format: .txt (UI manifest files) — uitex.txt, skillicon.txt, crestlist.txt, texturelist.txt

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> Promoted from dirty-room analyst notes under EU Software Directive 2009/24/EC Art. 6.
> No decompiler output and no binary virtual addresses appear anywhere in this file.
>
> status: PARSER-CONFIRMED for uitex.txt grammar (braced-block tokenizer, UI_TEXTURE/DDS/MSK
>         structure, '#' comments) and skillicon.txt grammar (SKILL block, exact 4-field entry).
>         Content tables for both files additionally SAMPLE-VERIFIED from VFS census
>         (43,347-entry real VFS archive, project-local clientdata/).
>         crestlist.txt structure SAMPLE-VERIFIED for filename pattern; row count CONFIRMED
>         at 1952 (EOF-driven, CAMPAIGN VFS-MASTERY); pool blob layout PARTIAL (704-byte exact
>         size; pixel dimensions not byte-confirmed). uitex.txt entry count CONFIRMED at 37
>         (EOF-driven, CAMPAIGN VFS-MASTERY).
>         skillicon.txt skill icon cell model CODE-CONFIRMED (§2.6, 2026-06-13);
>         on-disk source of (iconSrcX, iconSrcY) is the per-class stance .do files
>         (data/script/musajung.do and 11 siblings) — CODE-CONFIRMED + SAMPLE-VERIFIED
>         (§2.7, 2026-06-13). skills.scr does NOT carry this pair (SAMPLE-VERIFIED negative).
>         data/item/texturelist.txt grammar CODE-CONFIRMED (§10, 2026-06-13).
>
> These are **text-format manifests** (not binary records). They follow the same
> C-style brace / `#`-comment convention used by other client config files (see
> `formats/config_tables.md` for the `.scr` / `.do` binary siblings). All text is
> CP949 (EUC-KR superset, the legacy Korean code page). Line endings are CRLF.

---

## Identification

- **Extension:** `.txt` (all three manifest files carry this extension)
- **Found in:** inside the VFS archive (`data.inf` + `data/data.vfs`); see `formats/pak.md`
  for VFS lookup. Direct-disk fallback is engine-controlled by a config flag.
- **Character encoding:** CP949 for all string fields (header comments, quoted paths, name
  fields). ASCII subset for numeric IDs and structure keywords.
- **No magic / no version field.** Files are identified by their VFS path.
- **Endianness:** not applicable (text format).
- **Line endings:** CRLF (Windows).

---

## 0. Scope — these are texture-ID REGISTRIES, not window-layout manifests (CONFIRMED)

> **Read this first.** The file title says "UI manifest files," which a reader could mistake
> for "the files that lay out the windows." They are **not** that. Every file documented here is
> a **texture-id → VFS-path registry** (`UiTex.txt`, `skillicon.txt`, item `texturelist.txt`) or a
> **per-guild crest pool list** (`crestlist.txt`). They tell the engine *which DDS file* to load
> for a given integer id. **They never carry an element's on-screen position or its atlas
> source-rect.**
>
> **There is NO on-disk UI layout manifest anywhere in the VFS.** A window's per-element layout
> (each widget's destination position, width/height, and atlas source rectangle) is **code-baked**
> — it is constructed at runtime by that window's `BuildScene` method (the primary vtable slot 14 /
> byte offset +56), invoked once from the engine's scene state machine immediately after the window
> ctor, by calling the shared GU-widget builders with **integer-literal coordinates**. The
> construction model, the builder coordinate contract, and a worked element census are documented
> in **§14** below; the authoritative per-window layout (the literal coordinate tables) lives in
> **`specs/ui_system.md`** (the window-construction authority). [confirmed]
>
> **What is genuinely data-driven for UI rendering** is a narrow set of inputs, summarised so an
> engineer does not go hunting for a layout file that does not exist:
>
> | Data-driven input | What it supplies | Source | This doc |
> |---|---|---|---|
> | Texture-id registries | id → DDS path (atlas *selection* only) | `UiTex.txt`, `skillicon.txt`, item `texturelist.txt` | §1, §2, §10 |
> | Per-skill icon sub-rect | `(iconSrcX, iconSrcY)` 23×23 atlas cell | per-class stance `.do` files | §2.6, §2.7 |
> | Guild crest pool | per-guild uploaded crest blobs | `crestlist.txt` | §3 |
> | Caption text | widget caption strings by numeric id | message DB (`msg.xdb`) | §8, §14.4 |
> | UI scalar tunables | e.g. `NEW_SERVER_INDEX` | `data/script/uiconfig.lua` | §14.5 |
> | UI font slots | 15 code-defined font faces (not a file) | startup code | §12 |
>
> The per-skill `(iconSrcX, iconSrcY)` pair (§2.6/§2.7) is the **one and only genuine data-driven
> source-rect in the entire UI** — the exception that proves the rule that every other element's
> source-rect is a code literal.

---

## 1. `data/ui/UiTex.txt` — primary UI texture registry

### 1.1 Role

Maps 4-digit zero-padded integer **texture IDs** to VFS-relative file paths. The legacy UI
system uses these IDs as handles — a widget's texture is bound by passing a `tex_id` integer
to the window/panel bind call, not by passing a path string. This file is the sole source of
truth for that ID → path mapping.

### 1.2 File structure (PARSER-CONFIRMED)

The file is a **structured config file** using a C-style braced-block tokenizer with `#`
line comments. The file is opened and tokenized by the shared text-stream engine (described
in §1.7 below); tokens are whitespace-delimited; `{` and `}` are plain tokens; lines starting
with `#` are treated as comments and skipped before any token read.

The top-level structure is:

```
# <comment — CP949 header comment>
# UI 관련 텍스쳐 데이타 읽기  (= "UI-related texture data read")

UI_TEXTURE
{
    DDS
    {
        # tex_id path
        <id> "<path>"
        …
    }
    MSK
    {
        # tex_id path
        <empty in observed file>
    }
}
```

- The outer block keyword is `UI_TEXTURE` (PARSER-CONFIRMED from string constant).
- The `DDS` sub-block contains the main texture entries (PARSER-CONFIRMED from string constant).
- The `MSK` sub-block is reserved for mask textures; it is **empty** in the version analysed
  (PARSER-CONFIRMED from string constant; semantics PROPOSED — see §1.6).
- Comments use `#` to end of line. The comment-skip helper is called before every meaningful
  token read (PARSER-CONFIRMED).
- Brace characters `{` and `}` are block delimiters; whitespace is insignificant.
- Both levels of nesting (outer `UI_TEXTURE { }` and inner `DDS { }` / `MSK { }`) use the
  same `{`/`}` detection logic (PARSER-CONFIRMED: same tokenizer for both levels).
- **File size (SAMPLE-VERIFIED):** 1,355 bytes total in the observed VFS copy.

### 1.3 Entry record layout (PARSER-CONFIRMED grammar; SAMPLE-VERIFIED content)

Each entry in the `DDS` block is a single line:

```
<tex_id> "<vfs_path>"
```

| Field | Format | Notes | Confidence |
|---|---|---|---|
| `tex_id` | 4-digit zero-padded decimal integer (e.g. `0001`) | Parsed as a signed integer via the shared integer-parse helper; the handle passed to `GUWindow`/`GUPanel` bind calls | PARSER-CONFIRMED |
| `vfs_path` | Quoted string; VFS-relative, e.g. `"data/ui/mainwindow.dds"` | Extracted by the shared double-quote delimiter helpers; the opening `"` is found first, then the content up to the closing `"` is extracted without including the quotes | PARSER-CONFIRMED |

The integer field is read before the quoted string field on each entry row (PARSER-CONFIRMED:
integer parse helper is called before quote-string extraction in the entry processor).

> **Quoting caveat (SAMPLE-VERIFIED):** entry `0029` (`data/ui/inactivemember.dds`) has a
> **missing closing `"` in the source file**. A parser must handle a missing closing quote
> by treating the rest of the line (up to the next whitespace or end-of-line) as the path
> value. This is not a theoretical edge case — it is confirmed present in the real VFS copy.

### 1.4 Confirmed ID → path mapping (37 entries total — EOF-driven; 35 enumerated below; SAMPLE-VERIFIED)

> **Entry-count correction (CAMPAIGN VFS-MASTERY — CONFIRMED):** the `DDS` block carries
> **37 entries**, not 35. The loader is **EOF-driven** — it consumes `(id, path)` entry rows
> from the `DDS` block until end-of-file; there is **no fixed entry count and no count field**.
> The earlier "35" figure under-listed by 2 because it counted only the 35 rows enumerated in
> the table below. A parser must NOT assume any fixed entry count: read entry rows in a loop
> until the closing brace / end-of-file is reached. The two unlisted rows are not yet
> sample-pinned to specific IDs/paths in this spec; the loop-until-EOF rule covers them. — CONFIRMED.
>
> **Coverage note:** the 37 confirmed entries have IDs spanning the non-contiguous range
> **0001–0078** (the 35 enumerated below plus 2 not individually tabulated here). The ID space
> has large gaps (see §1.5 below). `UiTex.txt` accounts for 37 of the approximately 140
> root-level `data/ui/` entries. The remainder are loaded by hard-coded paths in the per-screen
> build routines. See `specs/ui_system.md` for the per-screen asset manifests.

| tex_id | VFS path | File size (bytes) | Dimensions | DDS format | Notes | Confidence |
|---|---|---|---|---|---|---|
| 0001 | `data/ui/mainwindow.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Main HUD chrome atlas | SAMPLE-VERIFIED |
| 0002 | `data/ui/inventwindow.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Inventory window | SAMPLE-VERIFIED |
| 0003 | `data/ui/skill_window_1.dds` | 1,398,256 | 1024×1024 | DXT3, full mip chain | Skill window (alternate) | SAMPLE-VERIFIED |
| 0004 | `data/ui/tradekeepwindow.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Trade keep / name-entry chrome | SAMPLE-VERIFIED |
| 0008 | `data/ui/skillwindow.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Main skill window atlas | SAMPLE-VERIFIED |
| 0009 | `data/ui/messagewindow.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Chat / message window | SAMPLE-VERIFIED |
| 0010 | `data/ui/skillpipe.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Skill hotbar (primary) | SAMPLE-VERIFIED |
| 0011 | `data/ui/skillpipe_02.dds` | 1,048,704 | 1024×1024 | DXT3, no mips | Skill hotbar (alternate) | SAMPLE-VERIFIED |
| 0013 | `data/ui/direction.dds` | 640 | tiny | unknown | Compass direction indicator | SAMPLE-VERIFIED |
| 0014 | `data/ui/blacksheet.dds` | 349,680 | 512×512 | DXT3, full mip chain | Black overlay / dimmer | SAMPLE-VERIFIED |
| 0015 | `data/ui/quick.dds` | 262,272 | 512×512 | DXT2/3, no mips | Quick-slot hotbar | SAMPLE-VERIFIED |
| 0026 | `data/ui/skillicon/stateicon.dds` | 262,272 | 512×512 | DXT2/3, no mips | Buff / status-effect icons | SAMPLE-VERIFIED |
| 0027 | `data/ui/emoticon.dds` | 65,664 | 256×256 | DXT3, no mips | Chat emoticon sheet | SAMPLE-VERIFIED |
| 0028 | `data/ui/guildicon/guildcresticon1.dds` | 262,272 | 512×512 | DXT2/3, no mips | Guild crest template sheet 1 | SAMPLE-VERIFIED |
| 0029 | `data/ui/inactivemember.dds` | 144 | 1×1 (probable) | unknown | Inactive member indicator — **entry has missing closing `"` in source** | SAMPLE-VERIFIED |
| 0030 | `data/ui/partyleaderflag.dds` | 384 | tiny | DDS | Party leader flag icon | SAMPLE-VERIFIED |
| 0040 | `data/ui/128.bmp` | 49,208 | 128×128 | BMP 24bpp | Shared BMP resource (only BMP in data/ui/) | SAMPLE-VERIFIED |
| 0041 | `data/ui/map_userpoint.tga` | 16,428 | 128×128 | TGA 32bpp | Player map-dot / waypoint marker | SAMPLE-VERIFIED |
| 0050 | `data/ui/p_green.dds` | 136 | 2×2 or 4×4 | DDS | Green solid fill patch | SAMPLE-VERIFIED |
| 0051 | `data/ui/p_red.dds` | 136 | 2×2 or 4×4 | DDS | Red solid fill patch | SAMPLE-VERIFIED |
| 0052 | `data/ui/p_white.dds` | 136 | 2×2 or 4×4 | DDS | White solid fill patch | SAMPLE-VERIFIED |
| 0053 | `data/ui/p_blue.dds` | 136 | 2×2 or 4×4 | DDS | Blue solid fill patch | SAMPLE-VERIFIED |
| 0054 | `data/ui/p_darkblue.tga` | 108 | 1×1 | TGA | Dark-blue solid fill patch | SAMPLE-VERIFIED |
| 0055 | `data/ui/p_black.tga` | 108 | 1×1 | TGA | Black solid fill patch | SAMPLE-VERIFIED |
| 0056 | `data/ui/p_yellow.tga` | 108 | 1×1 | TGA | Yellow solid fill patch | SAMPLE-VERIFIED |
| 0057 | `data/ui/p_orange.tga` | 108 | 1×1 | TGA | Orange solid fill patch | SAMPLE-VERIFIED |
| 0058 | `data/ui/p_purple.tga` | 108 | 1×1 | TGA | Purple solid fill patch | SAMPLE-VERIFIED |
| 0069 | `data/ui/yellow.dds` | 65,664 | 256×256 | DXT3, no mips | Yellow gradient fill | SAMPLE-VERIFIED |
| 0070 | `data/ui/red.dds` | 65,664 | 256×256 | DXT3, no mips | Red fill | SAMPLE-VERIFIED |
| 0071 | `data/ui/green.dds` | 65,664 | 256×256 | DXT3, no mips | Green fill | SAMPLE-VERIFIED |
| 0072 | `data/ui/blue.dds` | 65,664 | 256×256 | DXT3, no mips | Blue fill | SAMPLE-VERIFIED |
| 0073 | `data/ui/counter.dds` | 16,512 | 128×128 | DXT3, no mips | Counter digit strip | SAMPLE-VERIFIED |
| 0074 | `data/ui/white.dds` | 65,664 | 256×256 | DXT3, no mips | White fill | SAMPLE-VERIFIED |
| 0075 | `data/ui/target_16x16.dds` | 384 | 16×16 | DDS | Target indicator — small | SAMPLE-VERIFIED |
| 0076 | `data/ui/target_64x64.dds` | 4,224 | 64×64 | DXT3, no mips | Target indicator — large | SAMPLE-VERIFIED |
| 0077 | `data/ui/countinput.dds` | 1,398,256 | 1024×1024 | DXT3, full mip chain | Item count input dialog | SAMPLE-VERIFIED |
| 0078 | `data/ui/edge.dds` | 65,664 | 256×256 | DXT3, no mips | Edge border texture | SAMPLE-VERIFIED |


### 1.4a UI atlas re-inventory — count + header dims/fourCC re-confirmed (CAMPAIGN 14 — SAMPLE-VERIFIED)

> A fresh black-box re-inventory of the registry against the real VFS (43,347-entry archive,
> project-local `clientdata/`) **re-confirmed** the entry count and the header-decoded dimensions
> of the key atlases. Every one of the **37** `DDS` paths was re-checked for existence in the VFS
> — **all 37 present; 0 `MSK` entries.** The `MSK` block remains empty (see §1.7). — SAMPLE-VERIFIED.

**Entry count re-confirmation.** The registry yields **37** DDS entries, not 35. This matches the
EOF-driven loader (§1.4): there is no count field, so a parser reads rows until end-of-file and
must NOT hard-code any fixed count. — SAMPLE-VERIFIED.

**Key-atlas header dimensions / fourCC (each decoded from that file's own DDS header — SAMPLE-VERIFIED):**

| Atlas | Dimensions | fourCC | Role in the port |
|---|---|---|---|
| `data/ui/mainwindow.dds` | 1024×1024 | DXT3 | Main HUD chrome; also a char-select shared atlas |
| `data/ui/loginwindow.dds` | 1024×1024 | DXT5 | Login chrome; primary char-select / server-list shared atlas |
| `data/ui/inventwindow.dds` | 1024×1024 | DXT3 | Inventory window; shared modal / PIN frame |
| `data/ui/skillpipe.dds` | 1024×1024 | DXT2/3 (BC2) | Skill hotbar |
| `data/ui/skillwindow.dds` | 1024×1024 | DXT2 | Skill window |
| `data/ui/skill_window_1.dds` | 1024×1024 | DXT3 | Skill window (alternate) |
| `data/ui/messagewindow.dds` | 1024×1024 | DXT3 | Chat / message window |
| `data/ui/countinput.dds` | 1024×1024 | DXT5 | Item-count input dialog |
| `data/ui/password.dds` | 1024×1024 | DXT3 | PIN keypad atlas |
| `data/ui/login_slice1.dds` | 1024×1024 | DXT2/3 (BC2) | Login bottom-bar / edit-field frames / buttons |
| `data/ui/loginwindow_02.dds` | 1024×1024 | DXT2/3 (BC2) | Login variant 2 |
| `data/ui/blacksheet.dds` | **512×512** | DXT5 | Dim / black overlay (NOT a 1024² atlas — see §5.1a) |
| `data/ui/skillicon/stateicon.dds` | **512×512** | DXT2 | Buff / status-effect icons (id 0026) |
| `data/ui/carrierpigeonperson.dds` | **512×512** | DXT2/3 (BC2) | Carrier-pigeon personal; a char-select shared atlas |

> **Load-bearing dimension fact.** `blacksheet.dds` and `stateicon.dds` are both **512×512**, not
> 1024×1024. A source-rect whose `srcX + w` or `srcY + h` exceeds 512 on either atlas samples
> outside the texture. This bound is exactly what flags the char-select close-button mis-binding
> (§5.1a). — SAMPLE-VERIFIED.

**DXT2-vs-DXT3 alpha-mode nuance (SAMPLE-VERIFIED).** Several atlases this spec's §1.4 table records
as `DXT3` decode in the re-inventory as `DXT2` (for example `skillwindow` id 0008, `quick` id 0015,
`stateicon` id 0026, `emoticon` id 0027, the colour fills 0069–0072 / 0074, `edge` id 0078). `DXT2`
and `DXT3` are **the same BC2 block format and the same block size** — they differ only in whether
the alpha channel is pre-multiplied (`DXT2` = premultiplied, `DXT3` = straight). This is **not** a
dimension difference and **not** a decode-stride difference; older `fourCC` cells reading `DXT3`
should be understood as "BC2 (DXT2/DXT3)", and a parser must accept either code and decode them
identically (see the §4 census and the loader FourCC-equivalence note). The exact premultiply flag
per atlas is taken from each file's own header. — SAMPLE-VERIFIED.

> **Port note (non-load-bearing).** The legacy-port `Adapters/UiCatalogs.cs` carries a diagnostic
> print that still states the registry "expects 35" entries. The re-confirmed count is **37**; the
> lazy dictionary lookup over the non-contiguous id space is correct, so this is a stale diagnostic
> string only — not a parsing defect. (Recorded for the port lane; not a spec change.)

### 1.5 ID gaps (SAMPLE-VERIFIED)

The following ID ranges are unassigned in the analysed VFS version:

- 0005–0007, 0012, 0016–0025, 0031–0039, 0042–0049, 0059–0068, 0079 and above.

Whether these gaps were used in earlier or later client versions, or are reserved for
expansion, is UNKNOWN. A parser must not assume a contiguous ID space; it must build a
dictionary from the file and perform a direct-key lookup rather than an array-index lookup.

### 1.6 Path resolution

Paths are VFS-relative strings beginning with `data/`. The engine looks them up via the VFS
binary-search index (`data.inf`). A three-format fallback exists: the path extension in the
manifest is treated as a hint; if the asset is not found under the exact path, the engine may
try alternative extensions (DDS → TGA → BMP) — the exact fallback order is UNVERIFIED.

### 1.7 MSK block (PARSER-CONFIRMED keyword; semantics PROPOSED)

The `MSK` sub-block keyword is confirmed from a string constant adjacent to the `DDS` and
`UI_TEXTURE` string constants in the binary data segment (PARSER-CONFIRMED). The block is
present in the file structure but contains no entries in the analysed version (SAMPLE-VERIFIED
— the block is present but empty in the real VFS copy).

`MSK` is interpreted as "mask texture" — a separate greyscale or alpha-channel texture used
to cut out non-rectangular UI elements or to provide per-pixel transparency for UI panels.
This interpretation is consistent with the D3D9 UI pattern of pairing a colour DDS with a
separate mask texture. **This interpretation is PROPOSED and SAMPLE-UNVERIFIED** — the actual
content and extension of MSK-block entries has not been observed from real file data (the block
is empty in the known version). Possible extensions: `.msk`, `.dds` with explicit alpha channel.

A parser must accept and silently skip the MSK block regardless of semantics.

### 1.8 Shared tokenizer engine (PARSER-CONFIRMED)

The `UiTex.txt` parser uses the same text-stream infrastructure shared by `skillicon.txt` and
other client config files. All functions below are PARSER-CONFIRMED from the binary:

| Role | Notes |
|------|-------|
| Open file and create token stream | Called once at the start of the parse |
| Read next whitespace-delimited token | Main iteration primitive |
| Skip tokens starting with `"#"` | Comment-line skip; called before every meaningful token read |
| Parse current token as signed integer | Used for `tex_id` and all integer fields |
| Find next `"` character in stream | First step of quoted-string extraction |
| Extract content between quote delimiters | Extracts path without including the quote characters |
| Close file stream | Called after all entries are processed |

The tokenizer is whitespace-delimited. `{` and `}` are ordinary tokens compared by string
equality. `#` triggers line-comment skipping for the remainder of the line.

---

## 2. `data/ui/skillicon/skillicon.txt` — skill icon sheet registry

### 2.1 Role

Maps `(skill_id, job_id, kind_id)` tuples to the VFS paths of the DDS sprite sheets holding the
corresponding skill icons. The skill window queries this manifest to bind the correct icon sheet
per skill slot.

> **Grammar (CAMPAIGN VFS-MASTERY — CONFIRMED):** each entry inside the `SKILL` block is a
> **4-column record** of the form `SKILL { skill_id job_id kind_id "path" }` — three numeric
> ids (`skill_id`, `job_id`, `kind_id`) followed by one double-quoted VFS path. The full
> column breakdown is in §2.3. — CONFIRMED.

### 2.2 File structure (PARSER-CONFIRMED)

Same braced-block tokenizer with `#` comment convention as `UiTex.txt` (§1.8). The outer
block keyword is `SKILL` (PARSER-CONFIRMED from string constant in the binary).

**File size (SAMPLE-VERIFIED):** 8,404 bytes total in the observed VFS copy.

The CP949 header comment reads: `# 한직업에서 받아오는 총파일은 3개` (= "Total files received
per job is 3"). The column-legend comment reads:
`# job 1-무사 2-자객 3-도사 4-승려` (Warrior, Assassin, Wizard, Monk) and
`# kind 1-정 2-사 3-마교` (Justice, Evil, Demonic).

```
# comment lines start with '#'
SKILL
{
    # id job kind path
    <skill_id> <job_id> <kind_id>  "<vfs_path>"
    …
}
```

- A single outer `SKILL { }` block contains all entries; **no sub-section nesting** (contrast
  with `UiTex.txt` which has nested `DDS` / `MSK` sub-blocks).
- The `#` header comment line inside the block is a column legend; it is a comment row, not
  a data row.
- Fields are whitespace-delimited (space or tab).
- The quoted VFS path follows the three integer fields.

### 2.3 Column definitions (PARSER-CONFIRMED grammar; SAMPLE-VERIFIED content)

Each entry in the `SKILL` block is parsed as **exactly 4 sequential token reads**
(PARSER-CONFIRMED: the entry registration function receives precisely 4 parsed values).

| Column | Parse method | Type | Field | Notes | Confidence |
|---|---|---|---|---|---|
| 1 | integer-parse helper | signed int | `skill_id` | Skill identifier; cross-references `skills.scr` ID at +0 | PARSER-CONFIRMED |
| 2 | integer-parse helper | signed int | `job_id` | Character class ID: 1 = Musa (무사), 2 = Assassin (자객), 3 = Wizard (도사), 4 = Monk (승려) | PARSER-CONFIRMED |
| 3 | integer-parse helper | signed int | `kind_id` | Skill path within class: 1 = jung (정), 2 = sa (사), 3 = ma (마교) | PARSER-CONFIRMED |
| 4 | quote-string helpers | ASCII string | `icon_sheet_path` | Quoted VFS path to the icon DDS sprite sheet. Opening `"` found first; content extracted to closing `"` | PARSER-CONFIRMED |

The column ordering (three integers followed by one quoted string) is PARSER-CONFIRMED from the
call sequence in the entry registration path. No additional fields exist per row.

After parsing, all 4 fields are passed together to the icon registration function, which stores
the entry in the skill icon table keyed by `(skill_id, job_id, kind_id)`.

The quote delimiter is ASCII `"` (decimal 34). Quote extraction works identically to the
`UiTex.txt` path extraction described in §1.8.

### 2.4 Confirmed entries — 12 class-path sheets (SAMPLE-VERIFIED)

All 12 entries covering the complete 4 jobs × 3 alignment paths are confirmed in the real VFS:

| skill_id | job_id | kind_id | VFS path | Dimensions | Confidence |
|---|---|---|---|---|---|
| 1001 | 1 (Musa) | 1 (jung) | `data/ui/skillicon/musajung.dds` | 512×512 | SAMPLE-VERIFIED |
| 1002 | 1 (Musa) | 2 (sa) | `data/ui/skillicon/musasa.dds` | 512×512 | SAMPLE-VERIFIED |
| 1003 | 1 (Musa) | 3 (ma) | `data/ui/skillicon/musama.dds` | 512×512 | SAMPLE-VERIFIED |
| 1004 | 2 (Assassin) | 1 (jung) | `data/ui/skillicon/assasinjung.dds` | 512×512 | SAMPLE-VERIFIED |
| 1005 | 2 (Assassin) | 2 (sa) | `data/ui/skillicon/assasinsa.dds` | 512×512 | SAMPLE-VERIFIED |
| 1006 | 2 (Assassin) | 3 (ma) | `data/ui/skillicon/assasinma.dds` | 512×512 | SAMPLE-VERIFIED |
| 1007 | 3 (Wizard) | 1 (jung) | `data/ui/skillicon/wizardjung.dds` | 512×512 | SAMPLE-VERIFIED |
| 1008 | 3 (Wizard) | 2 (sa) | `data/ui/skillicon/wizardsa.dds` | 512×512 | SAMPLE-VERIFIED |
| 1009 | 3 (Wizard) | 3 (ma) | `data/ui/skillicon/wizardma.dds` | 512×512 | SAMPLE-VERIFIED |
| 1010 | 4 (Monk) | 1 (jung) | `data/ui/skillicon/monkjung.dds` | 512×512 | SAMPLE-VERIFIED |
| 1011 | 4 (Monk) | 2 (sa) | `data/ui/skillicon/monksa.dds` | 512×512 | SAMPLE-VERIFIED |
| 1012 | 4 (Monk) | 3 (ma) | `data/ui/skillicon/monkma.dds` | 512×512 | SAMPLE-VERIFIED |

**Total manifest coverage:** 12 entries for all 4 jobs × 3 alignment paths.

### 2.5 Supplementary icon sheets (not in skillicon.txt manifest; SAMPLE-VERIFIED presence)

Nine additional icon DDS sheets reside in `data/ui/skillicon/` but are not enumerated by
`skillicon.txt`. They are loaded by path in per-screen build routines or bound via `UiTex.txt`.

| VFS path | File size (bytes) | Dimensions | Probable role | Confidence |
|---|---|---|---|---|
| `data/ui/skillicon/stateicon.dds` | 262,272 | 512×512 | Buff/debuff status icons (also uitex.txt id 0026) | SAMPLE-VERIFIED |
| `data/ui/skillicon/cmonkicon.dds` | 262,272 | 256×256 | Common Monk class icon set *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — 256×256×4 + 128 = 262,272; parser must decode as raw ARGB32, not a block-compressed sheet)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/gungsaicon.dds` | 262,272 | 256×256 | Gungsa (crossbow) class icons *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/pmusaicon.dds` | 262,272 | 256×256 | Premium Musa class icons *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/sdocicon.dds` | 262,272 | 256×256 | Sdoc class icons *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/segumicon.dds` | 262,272 | 256×256 | Segum class icons *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/smusaicon.dds` | 262,272 | 256×256 | Smusa (super-warrior) icons *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/wizardicon.dds` | 262,272 | 256×256 | Wizard class overview icon *(corrected 2026-06-13: 256×256 uncompressed ARGB32, not 512×512 DXT — parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |
| `data/ui/skillicon/minddashicon.dds` | 262,272 | 256×256 | Mind dash icon set *(corrected 2026-06-13: 256×256 uncompressed ARGB32 — not 512×256 DXT1; parser must decode as raw ARGB32)* | SAMPLE-VERIFIED |

**Grand total under data/ui/skillicon/:** 22 entries (12 manifest sheets + 1 stateicon also in
uitex.txt + 9 supplementary = 22 files, consistent with the observed VFS entry count for this
subdirectory). One of those 22 is `skillicon.txt` itself (a text manifest, not a DDS), so the
DDS-only count under this subdirectory is 21.

> **Supplementary-sheet format correction (2026-06-13):** the eight supplementary class icon
> sheets above — `cmonkicon.dds`, `gungsaicon.dds`, `pmusaicon.dds`, `sdocicon.dds`,
> `segumicon.dds`, `smusaicon.dds`, `wizardicon.dds`, `minddashicon.dds` — are **256×256
> uncompressed ARGB32**, NOT 512×512 (or 512×256) block-compressed DXT sheets as earlier
> rows implied. Their 262,272-byte size matches 256×256×4 + 128, identical to a 512×512 DXT2
> blob, so size alone is misleading; the DDS pixel-format flags confirm they carry no FourCC
> block-compression marker. A parser MUST decode these eight sheets as raw ARGB32. The primary
> 12 `(job, kind)` manifest sheets and `stateicon.dds` remain 512×512 block-compressed.

> **Within-sheet icon layout:** resolved — see §2.6 below. The icon UV is data-driven per
> skill, not a computed grid stride. Whether the supplementary sheets (stateicon.dds,
> cmonkicon.dds, etc.) share the same 23×23 data-driven cell model is unconfirmed (§9
> open item #13).

### 2.6 Skill icon cell model (CODE-CONFIRMED — 2026-06-13)

**Key finding:** there is no modular grid formula for skill icons. The within-sheet source
rectangle of each skill's icon is **stored explicitly per skill as data** — a pair of signed
16-bit integers `(iconSrcX, iconSrcY)` read at runtime from an in-memory record populated by
the per-class stance `.do` files (§2.7). The icon is blitted as a **fixed 23×23 pixel** square
from the 512×512 `(job, kind)` sheet selected by `skillicon.txt`. Confirmed at three
independent draw sites (skill window grid, hotbar slot branch for kind=5 passive, hotbar slot
branches for standard and special). CODE-CONFIRMED.

**On-disk source correction (2026-06-13):** the `(iconSrcX, iconSrcY)` pair does NOT originate
in `skills.scr`. A full scan of 750 possible aligned u16-pair offsets in the on-disk
`skills.scr` record yielded a negative result for any plausible icon-coordinate pair
(CODE-CONFIRMED negative; SAMPLE-VERIFIED). The actual on-disk source is the per-class stance
`.do` table — see §2.7. The in-memory runtime offsets (+546/+548 for the category-banner path
and +24/+28 for the hotbar path) are populated by the `.do` loader, not by the skills.scr
loader.

#### Source-rect field locations (in-memory record offsets)

| Context | `iconSrcX` record offset | `iconSrcY` record offset | Type | Notes | Confidence |
|---|---|---|---|---|---|
| Hotbar / skill-pipe node (primary per-skill path) | +24 (node-relative) | +28 (node-relative) | i16 | Populated from `.do` record +0x18/+0x1C at load time | CODE-CONFIRMED |
| Category-banner path (secondary / fallback) | +546 (record-relative) | +548 (record-relative) | i16 | `skillcategory.scr` — one banner icon per category (17 total); NOT a per-skill source | CODE-CONFIRMED |

The hotbar/skill-pipe path (+24/+28) is the **primary, player-visible path** for per-skill
icons. The +546/+548 path is the category-banner fallback used by the skill window's category
header; it sources its data from `skillcategory.scr` (17 records × 564 bytes), not from
`skills.scr`. Both are in-memory positions; the on-disk source for the per-skill pair is the
`.do` file described in §2.7.

#### Draw model

| Parameter | Value | Confidence |
|---|---|---|
| Source sheet | 512×512 DDS selected by `skillicon.txt` for the player's `(job_id, kind_id)` | CODE-CONFIRMED |
| Source rect | `(iconSrcX, iconSrcY, 23, 23)` in atlas pixels | CODE-CONFIRMED |
| Destination cell size | 23×23 pixels on screen | CODE-CONFIRMED |
| UV derivation | Data per skill, read from the active `.do` table — not modular arithmetic on skill_id | CODE-CONFIRMED |

**Implication for Assets.Parsers / presentation:** a faithful renderer must load the active
class-stance `.do` file for the player's `(job, kind)` (see §2.7), key the player's skill
instance into the in-memory `.do` table, read `(iconSrcX, iconSrcY)` from the record, and
create an atlas sub-rect of size 23×23 at those coordinates on the sheet returned by
`skillicon.txt`. A modular stride formula produces incorrect icons.

#### Slot chrome — fixed atlas rects on mainwindow.dds (CODE-CONFIRMED)

Every skill slot composites the variable 23×23 icon cell with fixed decorative pieces from
the main HUD atlas (`mainwindow.dds`, uitex.txt id 0001). These pieces are invariant across
all skills:

| Piece | w × h (px) | srcX, srcY on mainwindow.dds | Slot kinds | Confidence |
|---|---|---|---|---|
| Icon backplate (standard) | 58 × 58 | 0, 661 | All standard slots | CODE-CONFIRMED |
| Frame ring A | 29 × 29 | 821, 655 | All standard slots | CODE-CONFIRMED |
| Frame ring B | 29 × 29 | 792, 655 | All standard slots | CODE-CONFIRMED |
| Frame ring C | 29 × 29 | 763, 655 | All standard slots | CODE-CONFIRMED |
| Wide passive backplate (kind = 5) | 146 × 49 | 63, 661 | Passive skills | CODE-CONFIRMED |
| Wide special backplate (kind in {0,6,7,11,18}) | 297 × 50 | 464, 656 | Special skills | CODE-CONFIRMED |
| Cooldown tick sprite | 2 × 10 | 997, 0 | Hotbar slots | CODE-CONFIRMED |
| Cooldown number marker | 10 × 7 | 64, 712 | Hotbar slots | CODE-CONFIRMED |
| Slot key-cap indicator | 7 × 15 | 990, 0 | Hotbar slots | CODE-CONFIRMED |

The skill-level digit field is drawn from a secondary atlas (not mainwindow.dds) at a
per-slot position derived from the skill record; this secondary atlas and its offsets
are not yet mapped.

#### Overlay and state conventions (PROPOSED)

The following overlay conventions were observed in passing during the draw-site trace but
were not exhaustively verified:

- **Disabled / unavailable:** the main icon button component is hidden; a locked frame ring
  is shown instead. Caption and level text colour becomes grey (`0xFF666666`). — PROPOSED.
- **Cooldown:** a sweep-tick sprite (2×10 px, see table above) plus a numeric digit overlay
  are drawn over the icon. The icon itself is not greyscaled. The cooldown fill animation
  style (radial sweep, vertical wipe, or alpha ramp) was not confirmed. — PROPOSED.
- **Hover:** caption text tints to yellow (`0xFFFFFF00`); the icon sprite frame is unchanged
  (all three button-state frames use the same `(iconSrcX, iconSrcY)`, so only caption colour
  changes on hover). — PROPOSED.

### 2.7 Per-class stance `.do` files — the on-disk source of skill icon coordinates (CODE-CONFIRMED + SAMPLE-VERIFIED — 2026-06-13)

**Role:** the 12 per-class stance `.do` files in `data/script/` are the **authoritative
on-disk source** of the per-skill `(iconSrcX, iconSrcY)` pair. Each file covers one
`(job × stance)` path and holds a complete table of skill icon entries for that class-path.
The engine bulk-loader reads these files at startup and inserts each record into two in-memory
maps; the hotbar/skill-window builders then look up the icon coordinates from those maps.

**VFS paths (12 files, SAMPLE-VERIFIED presence):**

> **Column note.** The `classStanceRef` column below is the **`.do` record field at +0x0C** (§2.7
> record layout) — the per-record `(job × stance)` discriminator. It is **distinct** from the
> selection function's **class-index** classStanceRef (1..4) of §2.8 — see the §2.8 "two distinct
> quantities" note. The `selection class index` column is the §2.8 value (CODE-CONFIRMED).

| File | job | kind | selection class index (§2.8) | `.do` +0x0C discriminator | Sheet (from skillicon.txt) |
|---|---|---|:---:|---|---|
| `data/script/musajung.do` | 1 (Musa) | 1 (jung) | 1 | 1001 | `musajung.dds` |
| `data/script/musasa.do` | 1 (Musa) | 2 (sa) | 1 | 1002 | `musasa.dds` |
| `data/script/musama.do` | 1 (Musa) | 3 (ma) | 1 | 1003 | `musama.dds` |
| `data/script/assasinjung.do` | 2 (Assassin) | 1 (jung) | 2 | UNVERIFIED | `assasinjung.dds` |
| `data/script/assasinsa.do` | 2 (Assassin) | 2 (sa) | 2 | UNVERIFIED | `assasinsa.dds` |
| `data/script/assasinma.do` | 2 (Assassin) | 3 (ma) | 2 | UNVERIFIED | `assasinma.dds` |
| `data/script/wizardjung.do` | 3 (Wizard) | 1 (jung) | 3 | UNVERIFIED | `wizardjung.dds` |
| `data/script/wizardsa.do` | 3 (Wizard) | 2 (sa) | 3 | UNVERIFIED | `wizardsa.dds` |
| `data/script/wizardma.do` | 3 (Wizard) | 3 (ma) | 3 | UNVERIFIED | `wizardma.dds` |
| `data/script/monkjung.do` | 4 (Monk) | 1 (jung) | 4 | UNVERIFIED | `monkjung.dds` |
| `data/script/monksa.do` | 4 (Monk) | 2 (sa) | 4 | UNVERIFIED | `monksa.dds` |
| `data/script/monkma.do` | 4 (Monk) | 3 (ma) | 4 | UNVERIFIED | `monkma.dds` |

The **selection class index** (1..4) is CODE-CONFIRMED for all four classes (§2.8). The **+0x0C
discriminator** values for the nine non-Musa files remain unconfirmed this pass (§9 item #11b).

#### Record layout (116 bytes / 0x74) — SAMPLE-VERIFIED

Each `.do` file is a tightly-packed array of fixed 116-byte records with no file header and
no count field. The record count is derived as `file_size / 116`; a non-zero remainder
indicates a truncated trailing record that the loader ignores.

**Verified file sizes:**
- `musajung.do`: 34,916 bytes = exactly 301 records, tail 0 bytes. SAMPLE-VERIFIED.
- `musasa.do`: 34,916 bytes = exactly 301 records. SAMPLE-VERIFIED.
- `musama.do`: 25,792 bytes = 222 full records + 40 trailing bytes (ignored by loader). SAMPLE-VERIFIED.

Endianness: little-endian. All offsets are byte offsets within a single 116-byte record.

| Offset | Size | Type | Field | Notes / observed values | Confidence |
|-------:|-----:|------|-------|-------------------------|------------|
| +0x00 | 4 | u32 | `instanceKey` | Large sequential skill-instance identifier; primary map key. Example: first record of musajung.do = 0x07D07153 (decimal 131101011); increments by a family-specific step per successive record family | CODE-CONFIRMED + SAMPLE-VERIFIED |
| +0x04 | 4 | u32 | `groupSubIndex` | Small integer (0, 1, 2, …); sub-row within a skill family or stance step | SAMPLE-VERIFIED |
| +0x08 | 4 | u32 | `slotIndex` | Sequential slot number 0, 1, 2, …; secondary map key | CODE-CONFIRMED |
| +0x0C | 4 | u32 | `classStanceRef` | (job × stance) discriminator: 1001 = musajung, 1002 = musasa, 1003 = musama. Other 9 files: classStanceRef values unconfirmed (§9 item #11c). Passed to the sheet resolver to select the 512×512 DDS | CODE-CONFIRMED (1001/1002/1003); PLAUSIBLE (other 9) |
| +0x10 | 4 | u32 | `groupId` | Skill-family / page group (observed: 19, 185, …) | SAMPLE-VERIFIED |
| +0x14 | 2 | u16 | (secondary X variant) | Tracks a value related to iconSrcX (observed: 120, 201, 282 …); possibly an unclipped X or a different sprite field | SAMPLE-VERIFIED (value pattern); field name UNKNOWN |
| **+0x18** | **2** | **i16** | **`iconSrcX`** | **Icon atlas left edge, in pixels. Engine reads this field to drive the hotbar/skill-window draw. Observed values: 0, 23, 46, 69, 92, 115, 138, …, 489. Values are authored data, not a formula; some are not multiples of 23.** | **CODE-CONFIRMED + SAMPLE-VERIFIED** |
| **+0x1C** | **2** | **i16** | **`iconSrcY`** | **Icon atlas top edge, in pixels. Engine reads this field to drive the hotbar/skill-window draw. Observed values: 0, 23, 46, 62, 69, 92, 115, 124, …** | **CODE-CONFIRMED + SAMPLE-VERIFIED** |
| +0x20 | 2 | u16 | `secondarySpriteX` | Secondary sprite left (observed: 0, 87, 174, 261 …); possibly a frame/overlay field | SAMPLE-VERIFIED (value pattern); field name UNKNOWN |
| +0x24 | 2 | u16 | `secondarySpriteY` | Secondary sprite top (observed: 23, 36, 85, 98, 147, 160 …); steps with the icon row | SAMPLE-VERIFIED (value pattern); field name UNKNOWN |
| +0x28 | 72 | — | (unmapped) | Remaining 72 bytes to the end of the 116-byte record. May contain motion IDs, SP costs, prerequisite references, level data, and other skill parameters. Not decoded this pass. | UNKNOWN |

#### Load chain

The engine's bulk asset-init loop loads each active class-stance `.do` file through a
dedicated record-loader. The loader opens the file, reads records in 116-byte chunks, and for
each record copies the 116 bytes verbatim into a heap-allocated node, then inserts that node
into two in-memory maps:

- **Map A** — keyed by `instanceKey` (+0x00): used by the hotbar builder and the unified icon
  resolver. This is the primary lookup path for per-skill icon coordinates; the hotbar builder
  iterates this map, filters by stance/sex flag, looks the skill up in the skills catalog, and
  reads `(iconSrcX, iconSrcY)` from the node at the in-memory positions corresponding to
  +0x18/+0x1C on disk (reported as +24/+28 in the runtime draw-site trace).
- **Map B** — keyed by `slotIndex` (+0x08): used for ordered enumeration of slots.

The active `.do` file is selected at character load time by a class-stance dispatcher that
reads the player's character class (1 = Musa, 2 = Assassin, 3 = Wizard, 4 = Monk) and stance
(0 = ma, 1 = sa, 2 = jung) globals. Only one `.do` file is active per session.

#### Worked examples — musajung.do (SAMPLE-VERIFIED)

| Record | `instanceKey` | `slotIndex` | `classStanceRef` | `iconSrcX` | `iconSrcY` | Cell on musajung.dds |
|-------:|--------------|------------|-----------------|-----------|-----------|---------------------|
| 0 | 131101011 | 0 | 1001 | 0 | 0 | col 0, row 0 |
| 1 | 131101021 | 1 | 1001 | 23 | 0 | col 1, row 0 |
| 2 | 131101031 | 2 | 1001 | 46 | 0 | col 2, row 0 |
| 3 | 131101041 | 3 | 1001 | 69 | 0 | col 3, row 0 |
| 4 | 131101051 | 4 | 1001 | 92 | 0 | col 4, row 0 |
| 5 | 131101061 | 5 | 1001 | 115 | 0 | col 5, row 0 |
| 6 | — | 6 | 1001 | 0 | 62 | col 0, next band |

The 23-px step in X is the common case but is stored data, not a computed formula. Coordinate
62 at record 6 (instead of an expected 69 = 3 × 23) confirms that the authored values do not
always follow a uniform stride, and must be read per record.

#### `skillcategory.scr` — secondary / category-banner path (not the per-skill source)

`data/script/skillcategory.scr` holds **17 records × 564 bytes** (total 9,588 bytes). Each
record describes one skill category and carries a single embedded icon entry for the
category-header banner. The banner icon fields sit at record-relative offsets +0x222 (i16
banner `srcX`) and +0x224 (i16 banner `srcY`), which correspond to the absolute record offsets
+546 and +548. This is the source of the "+546/+548" pair observed in the earlier lane
(icon-grids.md). It is the category-banner path — one icon per category (17 total) — and is
the fallback branch of the unified icon resolver. It is **not** the per-skill icon source.

### 2.8 The `.do` stance-manifest selection function — class index + stance type + tier sign (CODE-CONFIRMED — 2026-06-20)

The 12 per-class stance `.do` files of §2.7 are held in a **flat pointer table** of three entries per
class (in `{jung, sa, ma}` order). A dedicated **selection function** picks which one is the active
manifest for the current character, from three runtime inputs:

| Input | Type | Role |
|---|---|---|
| **class index** | small int, **1-based** | Which class: **1 = Musa, 2 = Assassin (Salsu), 3 = Wizard (Dosa), 4 = Monk** |
| **stance type** | `0` / `1` / `2` | Selects within the class's `{jung, sa, ma}` triplet |
| **stance-tier sign flag** | signed | A high/low (≥ 0 vs < 0) flag that toggles `jung↔ma` and `sa↔ma` |

**Selection rule (per class):**

- `stanceType == 0` → **jung**
- `stanceType == 1` → **jung** if tier-sign **≥ 0**, else **ma**
- `stanceType == 2` → **sa** if tier-sign **≥ 0**, else **ma**

#### 2.8.1 `classStanceRef` (the selection class index) → file triplet (CODE-CONFIRMED)

> **Two distinct quantities, both loosely called "classStanceRef" — do not conflate them.** This
> section's `classStanceRef` is the **selection class index** (`1..4`) the selection function switches
> on to choose the file triplet. It is **not** the `.do` **record field at +0x0C** (§2.7), which is a
> per-record `(job × stance)` discriminator observed as `1001 / 1002 / 1003` for the three Musa files.
> The +0x0C values for the nine non-Musa files remain **UNVERIFIED** (§9 item #11b). The table below
> resolves only the **selection-by-class** mapping.

| Class | classStanceRef (class index) | jung `.do` | sa `.do` | ma `.do` |
|---|:---:|---|---|---|
| **Musa** | **1** | `musajung.do` | `musasa.do` | `musama.do` |
| **Assassin** (Salsu) | **2** | `assasinjung.do` | `assasinsa.do` | `assasinma.do` |
| **Wizard** (Dosa) | **3** | `wizardjung.do` | `wizardsa.do` | `wizardma.do` |
| **Monk** | **4** | `monkjung.do` | `monksa.do` | `monkma.do` |

The requested selection-index values are **Assassin = 2, Wizard = 3, Monk = 4** (Musa = 1).
Confidence: **HIGH** — the filename strings and the switch-on-class-index are unambiguous. The
in-source class filename spelling is **"assasin"** (single `s`), which matches the class enum value
**2 = Salsu**. These four class indices are exactly the `job_id` column of `skillicon.txt` (§2.3:
1 = Musa, 2 = Assassin, 3 = Wizard, 4 = Monk), so the selection class index, the `skillicon.txt`
`job_id`, and the character-class globals (§2.7 load chain) are the same 1-based class numbering.

> **Cross-reference.** The selection function consumes the same character class / stance globals the
> §2.7 load chain describes; once it returns the active `.do` file, the icon-coordinate read of
> §2.6/§2.7 proceeds from that file's 116-byte records. This section adds the **selection** step that
> sits upstream of the icon read.

### 2.9 `data/script/emoticon.do` — chat-emoticon picker grid (CODE-CONFIRMED + SAMPLE-VERIFIED — 2026-06-21)

> **The `.do` extension is NOT one format — disambiguate by filename + loader, never by extension.**
> `.do` is a file extension reused by **several unrelated fixed-stride binary record tables** under
> `data/script/`, each with its OWN record size and its OWN loader. They share only the family
> conventions (no header, no magic, no version, no count field; a tightly-packed array of fixed-size
> little-endian records; an EOF-driven loader that copies each raw record verbatim into a heap node
> and inserts it into one or more in-memory ordered maps keyed by a record field). At least three
> distinct strides are confirmed:
>
> | Sub-family | Record stride | Files | Role | This doc |
> |---|---:|---|---|---|
> | Per-class skill stance | 116 B (0x74) | `musajung.do` + 11 siblings | per-skill icon coords | §2.7, §2.8 |
> | Emoticon / chat-token grid | **40 B (0x28)** | `emoticon.do` | emoticon picker grid → `emoticon.dds` sprites + chat code | §2.9 (here) |
> | Message / error label table | 108 B (0x6C) | `errorinfo.do`, `msginfo.do` | panel text-message / label records | §2.9.5 (note only) |
>
> A parser MUST select the stride and field layout from the filename (or the loader that opens it),
> NOT from the `.do` extension. The 116-byte facts of §2.7/§2.8 are unchanged; §2.9 ADDS the 40-byte
> emoticon sibling.

#### 2.9.1 Role and identification

`data/script/emoticon.do` is the data table behind the **chat-emoticon picker panel**. It is a flat
array of fixed **40-byte** records, one per selectable emoticon, that supplies each emoticon button's
on-panel grid position, its glyph and name-strip source rectangles on `emoticon.dds`, its page/tab,
and the chat token emitted when the player clicks it. There is no file header, no magic, no version,
and no count field. All fields are little-endian.

- **Path:** `data/script/emoticon.do`. Loaded once at startup by the boot data-table corpus loader
  (the same boot thread that loads `errorinfo.do`, `msginfo.do`, `textcommand.do`, and the stance
  `.do` family).
- **Record stride:** 40 bytes (0x28).
- **Record count:** `file_size / 40`. There is no count field; the loader is purely EOF-driven, so a
  non-zero size remainder is a truncated trailing record that the loader's failed read drops.
- **Sample:** the observed VFS copy is **840 bytes = exactly 21 records × 40, remainder 0.**

#### 2.9.2 Read algorithm (loader)

The emoticon loader and record constructor follow the shared `.do` family pattern:

1. Open the VFS file by name (`data/script/emoticon.do`).
2. If the handle is valid, loop while the file is not at end-of-entry **and** a read of exactly
   **40 bytes (0x28)** into a stack buffer succeeds:
   - allocate a 40-byte heap node;
   - copy the full 40 raw bytes verbatim into the node (node layout == on-disk layout for all 40 bytes);
   - lazily create two in-memory ordered (red-black-tree) maps on first record, then insert the node
     into both:
     - **Map A** — keyed by the record field at **+0x00** (`id`);
     - **Map B** — keyed by the record field at **+0x08** (`index`).
3. Close and destruct the file handle.

So two indices exist over the same node set: by `id` (+0x00) and by `index` (+0x08). There is **no
count field** — the loop is purely EOF-driven, identical to the stance `.do` family.

#### 2.9.3 On-disk record layout (40 bytes / 0x28, little-endian) — SAMPLE-VERIFIED + CODE-CONFIRMED

Field roles are pinned by the picker-panel page builder (§2.9.4), the click handler (§2.9.5), and the
page-count / reverse-lookup helpers. All offsets are byte offsets within a single 40-byte record.

| Offset | Size | Type | Field | Consumer use | Observed values | Confidence |
|------:|----:|------|-------|--------------|-----------------|------------|
| +0x00 | 4 | u32 | `id` | Map A key | sequential 1..21 | CODE+SAMPLE |
| +0x04 | 1 | u8 | `pageId` (tab) | page/tab filter in the builder and click handler | 0 (recs 0–17), 1 (recs 18–20); valid range 0..2 | CODE+SAMPLE |
| +0x05 | 3 | — | padding | none | authoring-tool struct pad (uninitialized `0xCC` fill) | SAMPLE |
| +0x08 | 4 | u32 | `index` | Map B key; click-match key; passed as the button's child-action id | sequential 0..20 | CODE+SAMPLE |
| +0x0C | 4 | u32 | `emoteCode` | the chat/output token sent to the main window on click; reverse-lookup key | 0 on page-0 recs, then 10/11/12 on the last recs (per-page numbering) | CODE+SAMPLE |
| +0x10 | 4 | i32 | `dstX` | destination X of all four widgets on the panel | 10 (col 0) / 160 (col 1) | CODE+SAMPLE |
| +0x14 | 4 | i32 | `dstY` | destination Y base for the four widgets | 20, 75, 130, 185, 240, 295, 350, 405 (step 55) | CODE+SAMPLE |
| +0x18 | 4 | i32 | `glyphSrcX` | atlas src X of the 23×23 emoticon glyph on `emoticon.dds` | 0 on most early recs; varies later | CODE+SAMPLE |
| +0x1C | 4 | i32 | `glyphSrcY` | atlas src Y of the 23×23 emoticon glyph | 0 early; 23 on rec 20 | CODE+SAMPLE |
| +0x20 | 4 | i32 | `labelSrcX` | atlas src X of the 87×13 name-strip sprite on `emoticon.dds` | alternates 0 / 87 | CODE+SAMPLE |
| +0x24 | 4 | i32 | `labelSrcY` | atlas src Y of the 87×13 name-strip sprite | 46, 46, 59, 59, 72, 72, 85, 85 … (step 13 per pair) | CODE+SAMPLE |

> **`+0x04` width caveat (load-bearing).** Although there are three bytes between `pageId` (+0x04) and
> `index` (+0x08), the loader and every consumer read **only the low byte** at +0x04. Those three pad
> bytes are the authoring tool's uninitialized-fill pattern (`0xCC`), so reading +0x04 as a 4-byte
> integer yields a nonsense value such as `0xCCCCCC00`/`0xCCCCCC01`. A parser MUST read `pageId` as a
> single byte (or mask the i32 with `0xFF`), never as a 4-byte field, and MUST treat +0x05..+0x07 as
> don't-care padding to the +0x08 boundary.

#### 2.9.4 Consumer — the emoticon-picker page builder

The picker panel's per-page build path constructs the grid for a given page index (0..2):

1. Resolve two UI texture handles from the `UiTex.txt` registry (§1):
   - tex id **3** (skill-window chrome atlas) — backplate + frame-ring decoration;
   - tex id **27** = `data/ui/emoticon.dds` (256×256, BC2; §1.4 entry 0027) — emoticon glyph + name-strip
     source. **This is the join to `emoticon.dds`.**
2. Count the records whose `pageId` (+0x04) equals the requested page, and allocate a widget array of
   four widget pointers per matched record.
3. Iterate Map B (the by-`index` map) in order; for each record whose `pageId == page`, read
   `dstX`/`dstY` (+0x10/+0x14), the glyph src (+0x18/+0x1C), the label src (+0x20/+0x24), and `index`
   (+0x08), then build **four** child widgets:

| # | Widget | Texture | Destination (x, y) | Size (px) | Atlas source | Notes |
|---|--------|---------|--------------------|-----------|--------------|-------|
| 1 | Image (backplate) | id 3 chrome | (dstX, dstY) | 146×49 | (63, 661) | fixed chrome rect |
| 2 | 3-state Button (the emoticon) | id 27 `emoticon.dds` | (dstX+23, dstY+11) | **23×23** | **(glyphSrcX, glyphSrcY) = (+0x18, +0x1C)** | all three button states share the same src; the button is added with **child-action id = `index` (+0x08)** |
| 3 | Image (name strip) | id 27 `emoticon.dds` | (dstX+48, dstY+16) | **87×13** | **(labelSrcX, labelSrcY) = (+0x20, +0x24)** | the emoticon's pre-rendered Korean name sprite |
| 4 | Image (frame ring) | id 3 chrome | (dstX+20, dstY+8) | 29×29 | (763, 655) | fixed chrome rect |

Only widget 2 carries a click action (its action id is the record's `index`); widgets 1/3/4 are plain
decoration. This builder is the authority that pins the field roles: the glyph src is (+0x18, +0x1C),
the name-strip src is (+0x20, +0x24), the destination grid is (+0x10, +0x14), `pageId` is the +0x04 low
byte, and the action/`index` is +0x08.

#### 2.9.5 Consumer — the picker click handler, and the other `.do` strides

The picker click handler receives `(page, action)` and:

- guards `page <= 2` (so exactly **three tabs / pages**, ids 0/1/2) and `action < 200`;
- filters Map B for the record whose `pageId == page` AND `index == action`;
- on a match: plays a 2D click sound, reads `emoteCode` (+0x0C), stores it on the panel, and dispatches
  `emoteCode` to the main window — i.e. **+0x0C is the chat/output token emitted when the player clicks
  an emoticon.**

A companion reverse-lookup scans Map A for the record whose `emoteCode` (+0x0C) equals an incoming
value, resolving a received emote code back to its record.

**Other `.do` strides (context, not the emoticon target):** `errorinfo.do` and `msginfo.do` are a
**third** pattern — **108-byte (0x6C) records**, loaded by their own loader on first panel open (the
errorinfo path is guarded by an "already loaded" flag), used as text-message / label record tables for
the error and message panels. They share the family conventions but have a DIFFERENT stride and a
DIFFERENT record constructor; do not conflate them with the 40-byte emoticon table. Their full field
layout is not decoded here.

> **Note on the related text tables.** `data/char/emoticon.txt` and `data/char/sameemoticon.txt` are
> SEPARATE CP949 text tables (likely actor-animation emoticon mappings), not the UI picker grid. They
> are out of scope for this binary `.do` spec.

#### 2.9.6 Linkages (emoticon.do)

| Direction | What | Join key |
|---|---|---|
| emoticon.do → `emoticon.dds` | the 23×23 glyph and 87×13 name-strip sprites are blitted from `data/ui/emoticon.dds` (256×256, BC2) | `UiTex.txt` tex id **27** (§1.4 entry 0027), hard-coded in the page builder |
| emoticon.do → skill-window chrome | backplate / frame-ring decoration | `UiTex.txt` tex id **3** (§1.4 entry 0003), hard-coded in the page builder |
| emoticon.do → chat / main window | clicking an emoticon dispatches `emoteCode` (+0x0C) to the main window | the click handler's main-window dispatch |
| boot → emoticon.do | loaded once at startup by the boot data-table corpus loader | the boot data-table loader thread |
| runtime index | two in-memory ordered maps over the records | Map A by +0x00 `id`; Map B by +0x08 `index` |

#### 2.9.7 Open questions (emoticon.do)

- `emoteCode` (+0x0C) value space: the sample shows 0 and 10/11/12; whether the value is a chat-emoticon
  opcode or an index into `emoticon.txt` is not confirmed (the downstream main-window consumer is not
  traced here).
- The 87×13 name-strip labels are very likely pre-rendered Korean text baked into `emoticon.dds` (they
  are atlas sprites, not text-rendered captions), but this is visually probable, not byte-confirmed.
- Exact tab/page semantics for the three pages (`pageId` 0/1/2): the sample exercises only pages 0 and 1
  (21 records); a fuller `emoticon.do` may populate page 2.
- `errorinfo.do` / `msginfo.do` (108-byte) full field layout is not decoded.

---

## 3. `data/ui/guildicon/crestlist.txt` — guild crest pool registry

### 3.1 Role

Enumerates the set of per-guild player-uploaded crest images available in
`data/ui/guildicon/pool/`. The file is consulted when rendering guild crests in the guild
window and on the character list.

### 3.2 File structure (SAMPLE-VERIFIED for filename pattern; column layout PARTIAL)

**File size (SAMPLE-VERIFIED):** 47,199 bytes. CP949 encoding, CRLF line endings.
**Row count (CAMPAIGN VFS-MASTERY — CONFIRMED):** **1952 rows**, one crest filename per line.
The loader is **EOF-driven** — it reads filename lines until end-of-file; there is **no count
field and no fixed row count**. The earlier "~1,350" estimate (a bytes-÷-average-line-length
guess) was wrong; the real row count is 1952. A parser MUST read every line until EOF and must
NOT assume any fixed row count. — CONFIRMED.

The file is plain text with one filename per line. Each line is the bare filename of a pool
crest image stored under `data/ui/guildicon/pool/`. No column headers are present.

**Filename naming convention (SAMPLE-VERIFIED from multiple decoded lines):**

```
{region}_{type}_{guild_id}_{server_id}_{guild_name}.dds
```

| Component | Observed values | Notes | Confidence |
|---|---|---|---|
| `region` | Always `1` in all observed samples | Constant in known data | SAMPLE-VERIFIED |
| `type` | Crest-type discriminator; small non-negative integer. `4` is dominant, but the full 1952-row read shows the field is **not a constant** — additional type values occur. Treat as a variable integer field, not a literal `4`. | Variable across the full row set (widened CAMPAIGN VFS-MASTERY) | CONFIRMED-variable |
| `guild_id` | Integer (e.g. 666, 1183, 363, 903) | Unique guild identifier | SAMPLE-VERIFIED |
| `server_id` | Small positive integer; `1` and `2` are common but the field is **not limited to `{1,2}`** across the full 1952-row read — treat as a variable server-origin integer. | Variable across the full row set (widened CAMPAIGN VFS-MASTERY) | CONFIRMED-variable |
| `guild_name` | CP949 Korean guild name string | The remainder of the filename before `.dds` | SAMPLE-VERIFIED |

Example decoded lines:
```
1_4_666_2_만복주머니.dds
1_4_1183_2_백월.dds
1_4_363_2_옴마니반메훔.dds
1_4_903_2_달기가문.dds
```

> **Format caveat — PARTIAL:** the exact column count, delimiter character (the current
> evidence shows lines as single space-free filenames with no tab/separator other than
> underscores embedded in the name), and the full line-by-line exhaustive decode were not
> individually verified. A parser should treat each line as a single CP949 filename string
> with no guaranteed fixed column count, and extract the `guild_id` and `server_id` by
> splitting on `_` delimiters.

### 3.3 Pool directory structure (SAMPLE-VERIFIED)

| Path | Entry count | Notes |
|---|---|---|
| `data/ui/guildicon/pool/` | ~2,235 entries | Per-guild player-uploaded crest DDS blobs |
| `data/ui/guildicon/` (root) | 14 entries | 13 template crest sheets + `crestlist.txt` |

**Pool blob size:** every pool entry is exactly **704 bytes** (SAMPLE-VERIFIED across multiple
pool files), with one documented exception (`guildbasic.dds`, below). The precise pixel
dimensions for a 704-byte DDS crest are not yet confirmed from a header read; the most consistent
candidate is a 12×12 pixel uncompressed ARGB8888 image (12 × 12 × 4 bytes per pixel + 128-byte
DDS header = 576 + 128 = 704 bytes), but this dimension is PARTIAL — the DDS header at bytes
0x0C–0x13 (height and width fields) has not been byte-confirmed from a pool entry.

**Non-standard pool entry — `guildbasic.dds` (added 2026-06-13; SAMPLE-VERIFIED):** the pool
directory also holds `data/ui/guildicon/pool/guildbasic.dds`, which does **not** follow the
704-byte user-crest convention. It is **72×48 pixels, DXT2, 3,584 bytes**. It is not a
user-uploaded crest (wrong size and a name rather than the `{region}_{type}_{guild_id}_…`
pattern of §3.2). It appears to be a built-in blank/default crest template stored alongside the
user crests (PLAUSIBLE role). A parser scanning the pool by fixed 704-byte stride must treat this
single file as an exception rather than assume a uniform blob size for the directory.

**Template crest sheets (SAMPLE-VERIFIED):** the 13 template sheets are
`data/ui/guildicon/guildcresticon1.dds` through `guildcresticon12.dds` and
`guildcresticon25.dds`. Each is 512×512 DDS (file size: 262,272 bytes, DXT2/3).

---

## 4. DDS size/dimension reference table (SAMPLE-VERIFIED)

All formulas verified against real file bytes from the VFS. An engineer loading any DDS file
from `data/ui/` can use this table to verify the expected file size given dimensions and format.

| File size (bytes) | Dimensions | Block format | Mip chain | Byte formula |
|---|---|---|---|---|
| 2,097,280 | 1024×2048 | DXT3 | none | ceil(1024/4)×ceil(2048/4)×16 + 128 |
| 2,097,280 | 1024×512 | Uncompressed ARGB32 | none | 1024×512×4 + 128 *(added 2026-06-13: same byte count as 1024×2048 DXT3 — ambiguous, see size-ambiguity note below)* |
| 1,398,256 | 1024×1024 | DXT3 | full (11 levels) | sum of all mip levels + 128 |
| 1,048,704 | 1024×1024 | DXT3 or DXT5 | none | ceil(1024/4)×ceil(1024/4)×16 + 128 |
| 1,048,704 | 512×512 | Uncompressed ARGB32 | none | 512×512×4 + 128 *(added 2026-06-13: same byte count as 1024×1024 DXT3 — ambiguous, see size-ambiguity note below)* |
| 786,560 | 1024×768 | DXT3 | none | ceil(1024/4)×ceil(768/4)×16 + 128 |
| 699,216 | 1024×512 | DXT3 | full chain | sum of all mip levels + 128 |
| 524,416 | 1024×512 | DXT3 | none | ceil(1024/4)×ceil(512/4)×16 + 128 |
| 349,680 | 512×512 | DXT3 | full chain | sum of all mip levels + 128 |
| 262,272 | 512×512 | DXT2 or DXT3 | none | ceil(512/4)×ceil(512/4)×16 + 128 |
| 131,200 | 512×256 | DXT1 | none | ceil(512/4)×ceil(256/4)×8 + 128 |
| 91,332 | 151×151 | Uncompressed ARGB8888 | none | 151×151×4 + 128 (non-power-of-2) |
| 65,664 | 256×256 | DXT3 | none | ceil(256/4)×ceil(256/4)×16 + 128 |
| 32,896 | 256×256 | DXT1 | none | ceil(256/4)×ceil(256/4)×8 + 128 |
| 16,512 | 128×128 | DXT3 | none | ceil(128/4)×ceil(128/4)×16 + 128 |
| 8,320 | 128×128 | DXT1 | none | ceil(128/4)×ceil(128/4)×8 + 128 |
| 4,224 | 64×64 | DXT3 | none | ceil(64/4)×ceil(64/4)×16 + 128 |

**DDS header field locations (SAMPLE-VERIFIED):**

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0x00–0x03 | 4 bytes | Magic | `44 44 53 20` = "DDS " |
| 0x0C–0x0F | 4 bytes u32LE | Height in pixels | |
| 0x10–0x13 | 4 bytes u32LE | Width in pixels | |
| 0x50–0x53 | 4 bytes u32LE | Pixel-format flags | `0x04` = block-compressed (FourCC present, e.g. DXTn); `0x41` = uncompressed ARGB (DDPF_ALPHAPIXELS \| DDPF_RGB, 32 bpp, no FourCC). The discriminator between same-byte-count DXT and ARGB32 files — see size-ambiguity note below *(added 2026-06-13)* |
| 0x54–0x57 | 4 bytes ASCII | FourCC pixel format code | See table below. Only meaningful when the pixel-format flags at 0x50 indicate block compression (`0x04`); zero/ignored when 0x50 = `0x41` (uncompressed) |

**FourCC observed values (SAMPLE-VERIFIED):**

| FourCC bytes | String | Format | Notes |
|---|---|---|---|
| `44 58 54 32` | `DXT2` | BC2 (premultiplied alpha) | **Dominant FourCC across data/ui/** (SAMPLE-VERIFIED: full-census of 2,509 DDS files shows DXT2 is the most frequent value; see census note below) |
| `44 58 54 33` | `DXT3` | BC2 (non-premultiplied alpha) | Present in window sheets and fill patches; treat identically to DXT2 |
| `44 58 54 35` | `DXT5` | BC3 (interpolated alpha) | `loginwindow.dds` only — the sole DXT5 in data/ui/ (SAMPLE-VERIFIED) |
| `44 58 54 31` | `DXT1` | BC1 | Cursor DDS files; `gage.dds` |
| n/a | none | Uncompressed ARGB8888 | `weap_made2_xx.dds` — no block-compression FourCC; raw RGBA32 |

> **Census facts (SAMPLE-VERIFIED — vfsls scan-ui census 2026-06-12):** the full `data/ui/`
> tree contains **2,509 DDS files** (the remaining entries in the 2,588-entry tree are TGA,
> BMP, and text manifest files, plus 1 file carrying a `.dds` extension that is actually a
> TGA — see §7). FourCC values observed across the 2,509 DDS files: DXT1, DXT2, DXT3, DXT5,
> and uncompressed (no FourCC). **DXT2 is the dominant FourCC.** Of the 2,509 DDS files,
> **2,283 are non-power-of-2 textures** (the guildicon/pool crest blobs account for the
> large majority of these).
>
> **Loader FourCC equivalence note:** the legacy loader passes `DXT2` internally as the
> expected pixel format code for atlas sheets. DXT2 and DXT3 are both the BC2 block format;
> they differ only in whether the alpha channel is pre-multiplied. A parser must accept both
> `DXT2` and `DXT3` FourCCs and treat them identically for decoding purposes.
>
> **loginwindow.dds is the sole DXT5 file** in the entire `data/ui/` tree (SAMPLE-VERIFIED).

### 4.1 Size-ambiguity note — file size alone does NOT determine format (SAMPLE-VERIFIED — 2026-06-13)

Several window sheets were previously mis-recorded because a file's byte count is not unique to
one (dimensions, format) pair. Two collisions matter for `data/ui/`:

| File size (bytes) | Candidate A | Candidate B | How to disambiguate |
|---|---|---|---|
| 1,048,704 | 1024×1024 DXT3, no mips | 512×512 uncompressed ARGB32 | Read pixel-format flags at header offset 0x50: `0x04` = DXT3 (1024×1024); `0x41` = ARGB32 (512×512) |
| 2,097,280 | 1024×2048 DXT3, no mips | 1024×512 uncompressed ARGB32 | Read pixel-format flags at header offset 0x50: `0x04` = DXT3 (1024×2048); `0x41` = ARGB32 (1024×512) |

A parser MUST read the DDS pixel-format flags at offset 0x50 (and, when present, the FourCC at
0x54) before deciding dimensions or decoder. It must NOT infer format from file size. The set of
known 512×512 uncompressed ARGB32 windows that collide with 1024×1024 DXT3 at 1,048,704 bytes is:
`characwindow.dds`, `masterwindow.dds`, `statusquestexitwindow.dds`, `shwdnwindow.dds`,
`popdepositwindow.dds`, `skillprodotherwindow.dds`, `tradepartywindow.dds`, `login_base.dds`,
and `blacksheet copy.dds`. The known 1024×512 ARGB32 window colliding at 2,097,280 bytes is
`fame_buff_window.dds`. (All SAMPLE-VERIFIED from header reads — see §5.4 and the window-art
census.)

---

## 5. Window DDS files NOT in uitex.txt (hard-coded paths; SAMPLE-VERIFIED)

These files are loaded directly by VFS path in per-screen build routines and are not registered
in the `UiTex.txt` ID system. An engineer must hard-code their paths, not look them up by ID.

### 5.1 Login and character select screens

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/loginwindow.dds` | 1,048,704 | 1024×1024 | DXT5 | Login screen chrome — the only DXT5 in data/ui/ |
| `data/ui/loginwindow_02.dds` | 1,048,704 | 1024×1024 | DXT3 | Login screen variant 2 |
| `data/ui/login_base.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Login base layer *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/login_new.dds` | 1,048,704 | 1024×1024 | DXT3 | New character creation screen |
| `data/ui/login_slice1.dds` | 1,048,704 | 1024×1024 | DXT3 | Login screen slice 1 |
| `data/ui/password.dds` | 1,398,256 | 1024×1024 | DXT3, full mips | Secondary password dialog |
| `data/ui/server_icon.dds` | 16,512 | 128×128 | DXT3 | Server selection icon |


### 5.1a Character-select / server-list draw from SHARED atlases — there is NO dedicated select atlas (SAMPLE-VERIFIED)

A black-box VFS probe (CAMPAIGN 14) tested every plausible dedicated char-select atlas name —
`select.dds`, `charselect.dds`, `characterselect.dds`, `charselectwindow.dds`, `charwindow.dds`,
`create.dds`, `charcreate.dds` — and a substring scan for `select` / `charselect` / `lobby` across
all `data/ui/*.dds`. **All absent.** There is **no dedicated character-select or server-list atlas
in the VFS.** — SAMPLE-VERIFIED.

The character-select and server-list front-end scenes are therefore **code-baked from shared
atlases** already documented above — drawing their chrome with literal source rects (see §14 for the
`BuildScene` construction model) from this set:

| Shared atlas | Dimensions | Role on char-select / server-list |
|---|---|---|
| `data/ui/loginwindow.dds` | 1024×1024 (DXT5) | Primary chrome: tabs, stat grid, Create/Delete/Enter strip, server plates |
| `data/ui/inventwindow.dds` | 1024×1024 (DXT3) | Shared confirm-popup / modal frame |
| `data/ui/mainwindow.dds` | 1024×1024 (DXT3) | Shared HUD-family glyphs |
| `data/ui/blacksheet.dds` | 512×512 (DXT5) | Dim / black overlay |
| `data/ui/carrierpigeonperson.dds` | 512×512 (DXT2/3) | Auxiliary shared glyphs |

> **Binding rule for the port.** A faithful renderer must bind char-select / server-list elements to
> these **shared** atlases. **Any port code that binds a `select.dds` (or similar dedicated
> char-select atlas) would be wrong** — no such file exists in the VFS. — SAMPLE-VERIFIED.

### 5.1b RESOLVED — char-select corner close button binds `tradekeepwindow.dds` (CYCLE 1 A3-7; was a mis-binding to `blacksheet.dds`)

> **RESOLVED — CYCLE 1 A3-7 (2026-06-19), static-ida + sample-verified.** The earlier FLAGGED
> MIS-BINDING (port bound the close button to `data/ui/blacksheet.dds`, which overflows; correct
> 1024-square atlas IDA-pending) is now settled from the char-select window construct witness.
> **The button binds `data/ui/tradekeepwindow.dds` (uitex.txt id `0004`, 1024×1024 DXT3, §1.4).**

The char-select (state-4) window construct builds the corner close button as a **3-state button**
(normal / hover / pressed). The texture handle handed to that button is the one loaded from
**`data/ui/tradekeepwindow.dds`**; that handle is consumed unchanged by the button build (no
intervening reassignment between the load and the build), so the binding is unambiguous.

**Source rect — read directly from the construct (CONFIRMED):**

| Field | Value | Note |
|---|---:|---|
| srcX  | 941 | computed at build time as `910 + 0x1F` (so a literal "941" never appears in the image — see below) |
| srcY  | 910 | |
| w     | 23  | |
| h     | 23  | |
| dstX  | 971 | on-screen destination X |
| dstY  | 610 | on-screen destination Y |
| color | 0   | |

All **three** button states (normal / hover / pressed) sample the **same** atlas origin `(941, 910)`
on the **same** texture handle — the glyph does not move between states; only the interaction flags
change.

> **Why a literal "941" search returns nothing.** The `srcX = 941` value is a **computed** quantity
> (`910 + 0x1F`), not a stored literal, which is why a whole-binary search for the literal `941`
> finds no hit while `910` is present. The port's rect was therefore **correct** — record this so the
> rect `(941, 910, 23, 23)` is not re-doubted on the basis of a missing literal. Only the *atlas* was
> wrong.

**Refuted candidates.** The previously proposed candidates are all **REFUTED**:
- `data/ui/blacksheet.dds` (512×512) — the original mis-binding; the rect overflows it
  (`941 + 23 = 964 > 512`, `910 + 23 = 933 > 512`), so the overflow diagnosis below was correct in
  spirit, but the fix is a different file.
- `data/ui/loginwindow.dds` and `data/ui/mainwindow.dds` (the two 1024² guesses) — neither is the
  bound atlas. The witnessed atlas is a **third** file, `tradekeepwindow.dds`.

**Bounds check on the bound atlas (`tradekeepwindow.dds`, 1024×1024) — fits with margin:**
- `srcX + w = 941 + 23 = 964 ≤ 1024` → OK.
- `srcY + h = 910 + 23 = 933 ≤ 1024` → OK.

> **Port fix (implied).** Re-bind the char-select corner close button to
> **`data/ui/tradekeepwindow.dds`** (1024×1024), keep the source rect **`(941, 910, 23, 23)`** and the
> destination **`(971, 610)`**, with all three states sharing origin `(941, 910)`. This was the
> **only** char-select source-rect overflow found in the CAMPAIGN 14 bounds sweep; all other
> char-select / login / PIN rects fit their bound atlas (the tightest fit is the create-form class
> strip at `srcY = 1005`, which sits at the 1024 bottom edge for a height up to 19px — worth a runtime
> height check but in-bounds).

> **Confidence: PARSER-CONFIRMED / HIGH.** Single decisive construct site; the texture-handle
> dataflow from the `tradekeepwindow.dds` load to the close-button texture argument is direct and
> uninterrupted; the rect is read straight from the construct; the atlas dimension (1024×1024) is
> cross-referenced from the sample-verified UiTex registry (§1.4). Static-only caveat: the on-screen
> pixel result was not visually verified — that is presentation, not the binding.

### 5.2 Loading screens

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/loading.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading screen base |
| `data/ui/loading01.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 1 |
| `data/ui/loading02.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 2 |
| `data/ui/loading03.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 3 |
| `data/ui/loading04.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 4 |
| `data/ui/loading05.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 5 |
| `data/ui/loading06.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 6 |
| `data/ui/loading07.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 7 |
| `data/ui/loading08.dds` | 1,048,704 | 1024×1024 | DXT3 | Loading background 8 |
| `data/ui/loadingbar.dds` | 65,664 | 256×256 | DXT3 | Loading progress bar |

### 5.3 Opening / cinematic screens

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/openning_001.dds` | 786,560 | 1024×768 | DXT3 | Opening cutscene frame 1 |
| `data/ui/openning_002.dds` | 786,560 | 1024×768 | DXT3 | Opening cutscene frame 2 |
| `data/ui/openning_003.dds` | 786,560 | 1024×768 | DXT3 | Opening cutscene frame 3 |
| `data/ui/openning_004.dds` | 786,560 | 1024×768 | DXT3 | Opening cutscene frame 4 |
| `data/ui/openning_scenario.dds` | 2,097,280 | 1024×2048 | DXT3 | Opening scenario full sheet (non-square) |

> **Non-standard dimensions:** the opening frames are **1024×768** (non-square) and the
> scenario sheet is **1024×2048** (double-height). Both are confirmed DXT3 and fit the
> size formula in §4.

### 5.4 In-game HUD windows (hard-coded)

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/mainwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Main HUD chrome (also uitex 0001) |
| `data/ui/characwindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Character stats window *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/masterwindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Master/NPC window *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/statusquestexitwindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Status/quest/exit panel *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/shwdnwindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Shutdown confirmation *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/moonpa.dds` | 1,048,704 | 1024×1024 | DXT3 | Moon-PA event window |
| `data/ui/myway.dds` | 1,048,704 | 1024×1024 | DXT3 | My-way / personal path window |
| `data/ui/pandemonium.dds` | 1,048,704 | 1024×1024 | DXT3 | Pandemonium event |
| `data/ui/revengesummons.dds` | 1,048,704 | 1024×1024 | DXT3 | Revenge summons window |
| `data/ui/skillprodotherwindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Skill production window *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/skilltree.dds` | 1,048,704 | 1024×1024 | DXT3 | Skill tree view |
| `data/ui/tradepartywindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Trade/party window *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/popdepositwindow.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Deposit/bank pop window *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/help.dds` | 1,048,704 | 1024×1024 | DXT3 | Help overlay |
| `data/ui/itemshop.dds` | 1,048,704 | 1024×1024 | DXT3 | Item shop |
| `data/ui/itemshoppop.dds` | 1,048,704 | 1024×1024 | DXT3 | Item shop popup 1 |
| `data/ui/itemshoppopup.dds` | 1,048,704 | 1024×1024 | DXT3 | Item shop popup 2 |
| `data/ui/letter.dds` | 1,398,256 | 1024×1024 | DXT3, full mips | Letter/mail window |
| `data/ui/product.dds` | 1,398,256 | 1024×1024 | DXT3, full mips | Crafting/production window |
| `data/ui/cubegamble.dds` | 1,398,256 | 1024×1024 | DXT3, full mips | Cube gamble window |
| `data/ui/cubegamble_help.dds` | 1,048,704 | 1024×1024 | DXT3 | Cube gamble help 1 |
| `data/ui/cubegamble_help2.dds` | 1,048,704 | 1024×1024 | DXT3 | Cube gamble help 2 |
| `data/ui/delivery.dds` | 1,048,704 | 1024×1024 | DXT3 | Delivery quest window |
| `data/ui/chunrihojung.dds` | 1,048,704 | 1024×1024 | DXT3 | Chunri-hojung window |
| `data/ui/guildwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Guild window |
| `data/ui/guildnewwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Guild new window |
| `data/ui/ui_help1024.dds` | 1,048,704 | 1024×1024 | DXT3 | UI help full (1024 mode) |
| `data/ui/ui_help800.dds` | 1,048,704 | 1024×1024 | DXT3 | UI help full (800 mode) |
| `data/ui/fame_buff_window.dds` | 2,097,280 | 1024×512 | Uncompressed ARGB32 | Fame buff window *(corrected 2026-06-13: real format is 1024×512 landscape uncompressed ARGB32, not 1024×2048 DXT3 double-height; same byte count — disambiguate via header pixel-format flags, §4)* |
| `data/ui/12pahwangtext.dds` | 1,048,704 | 1024×1024 | DXT3 | 12-pahwang event text |
| `data/ui/blacksheet copy.dds` | 1,048,704 | 512×512 | Uncompressed ARGB32 | Blacksheet copy variant *(corrected 2026-06-13: real format is 512×512 uncompressed ARGB32, not 1024×1024 DXT3; same byte count — disambiguate via header pixel-format flags, §4)* |

### 5.5 512×512 secondary windows

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/buywindow.dds` | 262,272 | 512×512 | DXT2 | Buy dialog |
| `data/ui/carrierpigeonall.dds` | 262,272 | 512×512 | DXT2/3 | Carrier pigeon all-msgs |
| `data/ui/carrierpigeonperson.dds` | 262,272 | 512×512 | DXT2/3 | Carrier pigeon personal |
| `data/ui/chukji.dds` | 262,272 | 512×512 | DXT2/3 | Chukji (local map) |
| `data/ui/combo.dds` | 262,272 | 512×512 | DXT2/3 | Combo indicator |
| `data/ui/confessionnpc.dds` | 262,272 | 512×512 | DXT2/3 | Confession NPC dialog |
| `data/ui/cubegamble_ani.dds` | 262,272 | 512×512 | DXT2/3 | Cube gamble animation |
| `data/ui/guildcap.dds` | 262,272 | 512×512 | DXT2/3 | Guild cap emblem |
| `data/ui/guildcreate.dds` | 262,272 | 512×512 | DXT2/3 | Guild creation window |
| `data/ui/guildmemberposition.dds` | 262,272 | 512×512 | DXT2/3 | Guild member position |
| `data/ui/guildnpc.dds` | 262,272 | 512×512 | DXT2/3 | Guild NPC dialog |
| `data/ui/guildreask.dds` | 262,272 | 512×512 | DXT2/3 | Guild re-ask dialog |
| `data/ui/hyub_heng_popup.dds` | 262,272 | 512×512 | DXT2/3 | Hyub-heng (cooperation) popup |
| `data/ui/itemrepair.dds` | 262,272 | 512×512 | DXT2/3 | Item repair window |
| `data/ui/mediate.dds` | 349,680 | 512×512 | DXT3, full mips | Mediate window |
| `data/ui/o_kwang.dds` | 262,272 | 512×512 | DXT2/3 | O-kwang window |
| `data/ui/relation.dds` | 262,272 | 512×512 | DXT2/3 | Social relations window |
| `data/ui/slotboard.dds` | 262,272 | 512×512 | DXT2/3 | Slot board |
| `data/ui/stalllist.dds` | 349,680 | 512×512 | DXT3, full mips | Stall listing |
| `data/ui/famedonate.dds` | 349,680 | 512×512 | DXT3, full mips | Fame donation |
| `data/ui/itemupgrade.dds` | 349,680 | 512×512 | DXT3, full mips | Item upgrade |
| `data/ui/battleboard.dds` | 262,272 | 512×512 | DXT2/3 | Battle scoreboard |
| `data/ui/broodwarallystate.dds` | 262,272 | 512×512 | DXT2/3 | Brood War ally state |
| `data/ui/broodwarlist.dds` | 262,272 | 512×512 | DXT2/3 | Brood War list |

### 5.6 Special-size windows

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/broodwarmap.dds` | 1,048,704 | 1024×1024 | DXT3 | Brood War large map |
| `data/ui/npc_helper.dds` | 524,416 | 1024×512 | DXT3 | NPC helper wide panel |
| `data/ui/tender_window.dds` | 524,416 | 512×1024 | DXT2 | Tender/auction panel *(corrected 2026-06-13: real dimensions are 512 wide × 1024 tall portrait, format DXT2 — width and height were transposed in the prior 1024×512 DXT3 entry)* |
| `data/ui/warstoneinfo.dds` | 699,216 | 1024×512 | DXT3, full mips | War stone info |
| `data/ui/pahwangtext.dds` | 262,272 | 512×512 | DXT2/3 | Pa-hwang event text |

### 5.7 Solid-colour fill and misc small

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/gage.dds` | 8,320 | 128×128 | DXT1 | Gauge bar fill |
| `data/ui/five.dds` | 16,512 | 128×128 | DXT3 | Five-star rating icon |
| `data/ui/friendmove.dds` | 65,664 | 256×256 | DXT3 | Friend move indicator |
| `data/ui/musasword.dds` | 65,664 | 256×256 | DXT3 | Musa sword icon |
| `data/ui/publicpeace.dds` | 65,664 | 256×256 | DXT3 | Public peace indicator |
| `data/ui/autopenalty.dds` | 5,616 | ~60×60 est. | DXT3 | Auto-penalty icon (non-power-of-2; exact dims unconfirmed) |
| `data/ui/no_pk_penalty.dds` | 2,176 | ~32×32 est. | DDS | No-PK penalty icon |
| `data/ui/guildcrestback.dds` | 16,512 | 128×128 | DXT3 | Guild crest backdrop |

### 5.8 Upgrade and product animation frames

| Path pattern | Count | Size each (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|---|
| `data/ui/upgrade/weap_made01..28.dds` | 28 | 16,512 | 128×128 | DXT3 | Weapon upgrade step frames |
| `data/ui/product/weap_made2_01..28.dds` | 28 | 91,332 | 151×151 | ARGB8888 uncompressed | Weapon crafting step frames |

> **Non-power-of-2 note:** `weap_made2_xx.dds` files are 151×151 pixels, stored as 32-bit
> uncompressed RGBA (4 bytes per pixel). Size formula confirmed: 151 × 151 × 4 + 128 = 91,332.
> No block-compression FourCC is present in these files.

### 5.9 Map, dice, face, and mode assets

| Asset group | Count | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/map/map1.dds` | 1 | 512×512 | DXT2/3 | World minimap |
| `data/ui/dice/jusa_001..021.dds` | 21 | 128×128 | DXT3 | Dice roll animation frames |
| `data/ui/diceresult/jusai_win_001..022.dds` | 22 | 128×128 | DXT3 | Dice result animation frames |
| `data/ui/face/1_1..4_3.tga` | 12 | 128×128 | TGA 32bpp | Character face thumbnails (4 classes × 3 face variants) |
| `data/ui/face/anger/anger_01..05.dds` | 5 | 1024×1024 | DXT3 | Anger face expression sheets |
| `data/ui/face/anger/ani_000..015.dds` | 16 | 1024×920 | DXT3 | Anger animation frames (non-square) |
| `data/ui/face/fire/face_fire_01..18.tga` | 18 | 128×128 | TGA | Fire face animation |
| `data/ui/mode/attackmode-01..18.tga` | 18 | ~256×128 est. | TGA | Attack mode animation |
| `data/ui/mode/peacemode-01..18.tga` | 18 | ~256×128 est. | TGA | Peace mode animation |

> **Anger animation frame dimensions confirmed:** each `ani_000..015.dds` file is
> **1024×920** pixels, DXT3, file size 942,208 bytes (SAMPLE-VERIFIED:
> ceil(1024/4) × ceil(920/4) × 16 + 128 = 256 × 230 × 16 + 128 = 942,208). The
> 5 `anger_0x.dds` base sheets at 1,048,704 bytes are the full 1024×1024 sheets.

### 5.10 Newly enumerated root windows (added 2026-06-13; SAMPLE-VERIFIED)

The following root-level `data/ui/` DDS files were absent from the prior census and are recorded
here. With these two entries, the full root-level `data/ui/` DDS census stands at **119 entries**.

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/carrierpigeon.dds` | 65,664 | 256×256 | DXT2 | Carrier-pigeon channel icon — a small icon distinct from the 512×512 `carrierpigeonall.dds` / `carrierpigeonperson.dds` window chrome in §5.5 (PLAUSIBLE: chat-window channel button for carrier-pigeon mail) |
| `data/ui/productnpc.dds` | 65,664 | 256×256 | DXT2 | Production/crafting NPC icon (PLAUSIBLE: portrait or icon for the crafting NPC dialog) |

> Both are 256×256 DXT2; size 65,664 = ceil(256/4)×ceil(256/4)×16 + 128. Their attribution to a
> specific widget is PLAUSIBLE (inferred from filename and dimensions); no draw-site trace has
> confirmed the binding call.

---

## 6. data/ui/ directory overview (SAMPLE-VERIFIED)

Total files under `data/ui/`: **2,588 entries** across 13 subdirectories.

| Subdirectory | Entry count | Notes |
|---|---|---|
| `data/ui/` (root) | ~140 | Primary window sheets, fill patches, misc |
| `data/ui/dice/` | 21 | `jusa_001.dds` .. `jusa_021.dds` |
| `data/ui/diceresult/` | 22 | `jusai_win_001.dds` .. `jusai_win_022.dds` |
| `data/ui/face/` | ~50 | Face thumbnails (.tga); `anger/` and `fire/` subdirs |
| `data/ui/face/anger/` | 21 | 5 base expression sheets + 16 animated frames |
| `data/ui/face/fire/` | 18 | `face_fire_01..18.tga` |
| `data/ui/guildicon/` | 14 | 13 template crest sheets + `crestlist.txt` |
| `data/ui/guildicon/pool/` | ~2,235 | Per-guild 704-byte user crest DDS blobs |
| `data/ui/map/` | 1 | `map1.dds` (512×512 minimap) |
| `data/ui/mode/` | 36 | `attackmode-01..18.tga` + `peacemode-01..18.tga` |
| `data/ui/product/` | 28 | `weap_made2_01..28.dds` (weapon crafting step icons) |
| `data/ui/skillicon/` | 22 | Per-class skill icon sheets + `skillicon.txt` |
| `data/ui/upgrade/` | 28 | `weap_made01..28.dds` (weapon upgrade step icons) |

---

## 7. The `do.dds` mislabelled-extension caveat

The file `data/ui/do.dds` has a `.dds` extension but its header bytes match the **TGA format**
(image type byte = 2 at offset 2, characteristic of an uncompressed true-colour TGA) rather than
the DDS magic `44 44 53 20` ("DDS "). This file should be treated as a 32-bit TGA image, not a
DDS container.

A parser for `data/ui/` assets should probe the first four bytes of each file before dispatching
to a format-specific loader, rather than trusting the file extension. The FourCC `44 44 53 20`
confirms DDS; a DDS-absent header on a `.dds`-named file should fall back to TGA probing (check
the TGA image-type byte at offset 2; values 1, 2, 3, 9, 10, 11 are valid TGA image types).

This caveat applies to all `data/ui/` assets loaded by the parsers, not only `do.dds`.

---

## 8. Widget-to-texture-to-caption binding reference

### 8.1 Core window binding chain

| Widget | uitex_id | VFS path | Dimensions | Caption source |
|---|---|---|---|---|
| Main HUD chrome | 0001 | `data/ui/mainwindow.dds` | 1024×1024 | No captions — chrome only |
| Inventory | 0002 | `data/ui/inventwindow.dds` | 1024×1024 | msg.xdb (edge region) |
| Skill window | 0008 | `data/ui/skillwindow.dds` | 1024×1024 | Skill name from skills.scr |
| Skill hotbar | 0010, 0011 | `data/ui/skillpipe*.dds` | 1024×1024 | No text captions |
| Chat window | 0009 | `data/ui/messagewindow.dds` | 1024×1024 | msg.xdb 101–107 (button labels) |
| Quick-slot bar | 0015 | `data/ui/quick.dds` | 512×512 | No text |
| Trade keep | 0004 | `data/ui/tradekeepwindow.dds` | 1024×1024 | msg.xdb 401+ (server list) |

### 8.2 Skill icon lookup chain (CODE-CONFIRMED + SAMPLE-VERIFIED — 2026-06-13)

```
player (job, kind)
  -> load data/script/<job><kind>.do (the class-stance skill table, §2.7)
  -> read all 116-byte records into in-memory map keyed by instanceKey
  -> skill instance lookup by instanceKey
     -> read (iconSrcX, iconSrcY) i16 pair at record +0x18 / +0x1C
        (runtime map positions: hotbar reads these as node +24 / +28)
  -> skillicon.txt registry -> (job_id, kind_id) -> 512x512 DDS sheet path
  -> load the DDS sheet
  -> blit fixed 23x23 px cell at (iconSrcX, iconSrcY) on the sheet
  -> composite with fixed chrome pieces from mainwindow.dds (see §2.6)
```

The `(iconSrcX, iconSrcY)` values originate in the per-class stance `.do` files under
`data/script/` (§2.7), NOT in `skills.scr`. The `skills.scr` file is the skill name/cost/
cooldown catalog; it carries no icon coordinates. Recovering the full per-skill coordinate
table requires reading the active `.do` file for the player's `(job, kind)`.

---

## 9. Known unknowns

1. **Per-skill `(iconSrcX, iconSrcY)` literal data — CLOSED (mechanism and on-disk source);
   PENDING (full table extraction). Resolved 2026-06-13:** the UV is confirmed data-driven —
   a signed i16 pair stored per skill in the per-class stance `.do` file at record offsets
   +0x18 (iconSrcX) and +0x1C (iconSrcY) (§2.7). The runtime draw reads these via in-memory
   map positions +24/+28 (hotbar path). The draw cell is a fixed 23×23 px square on the
   512×512 `(job, kind)` sheet. CODE-CONFIRMED + SAMPLE-VERIFIED. The actual
   `(iconSrcX, iconSrcY)` literal values for every skill_id can be extracted by reading the
   relevant `.do` file; see §9 item #11 for the remaining open questions on that file.
2. **`MSK` block semantics:** the keyword is PARSER-CONFIRMED; the block is present but empty
   in the analysed version (SAMPLE-VERIFIED). The "mask texture" interpretation is PROPOSED
   and SAMPLE-UNVERIFIED. The extension of MSK-block entries and their exact role in alpha
   blending are unknown.
3. **uitex.txt ID gaps:** IDs 0005–0007, 0012, 0016–0025, 0031–0039, 0042–0049, 0059–0068,
   and 0079+ are unused in this version. Whether they existed in earlier VFS versions or are
   reserved for expansion is unknown.
4. **crestlist.txt exact column layout:** each line is treated as a single filename. Whether
   tab-separated guild-name columns appear after the filename is unconfirmed; the file was not
   decoded line-by-line exhaustively. (Row count is now CONFIRMED at 1952, EOF-driven — §3.2;
   the within-line component value ranges for `type`/`server_id` are widened to variable.)
5. **Pool crest pixel dimensions:** the 704-byte pool entry exact resolution (probable 12×12
   ARGB8888 = 576 + 128 = 704) is unconfirmed without reading the pool DDS header bytes at
   offsets 0x0C–0x13.
6. **Path fallback order:** whether the loader tries DDS → TGA → BMP when the manifest-specified
   extension is not found is UNVERIFIED.
7. **uitex.txt IDs above 0078:** whether IDs above 0078 exist in other client versions is
   unknown. The analysed version has **37 EOF-driven entries** (§1.4, CONFIRMED); 35 of them
   are individually enumerated in §1.4, the remaining 2 are covered by the loop-until-EOF rule
   but are not yet sample-pinned to specific IDs/paths.
8. **Item texture manifest format — CLOSED 2026-06-13.** `data/item/texturelist.txt` is a
   flat newline-delimited filename list (not a braced block); the leading decimal digits of
   each filename are the tex_id; path resolves to `data/item/texture/<filename>`; one DDS
   file per icon, whole-texture blit. CODE-CONFIRMED. See §10 for the full grammar.
9. **direction.dds at 640 bytes:** 640 − 128 = 512 data bytes; no standard power-of-2
   DDS layout accounts for this cleanly. The format (possible 1D palette strip, non-standard
   layout, or proprietary small resource) is unconfirmed.
10. **mode animation TGA exact dimensions:** `attackmode-xx.tga` and `peacemode-xx.tga`
    exact pixel dimensions are estimated (~256×128) but the TGA header was not byte-confirmed.

**Open items related to the per-class stance `.do` format (added/revised 2026-06-13):**

11. **`.do` record fields +0x28..+0x73 — unmapped (72 bytes).** The record layout is
    confirmed for the key icon fields (+0x00–+0x24), but the remaining 72 bytes are not yet
    decoded. These likely contain motion IDs, SP costs, prerequisite references, level data,
    and other per-skill parameters. A column-by-column census of `musajung.do` cross-referenced
    against the `skills.scr` catalog would close this. — UNKNOWN.

    Related open questions that need their own passes:

    a. **`instanceKey` (+0x00) join semantics with `skills.scr` `skill_id`.** The `.do`
       `instanceKey` is a 9-digit integer (e.g. 131101011) and appears to encode class, page,
       step, and variant in a structured way (pattern 13-1-101-01-1 is suggestive). Whether
       `instanceKey` equals `skill_id` in `skills.scr`, or is a packed composite key that must
       be decoded before joining, is not confirmed. — PLAUSIBLE (structured key); UNVERIFIED.

    b. **`.do` record-field (+0x0C) discriminator enumeration for the nine non-Musa files.**
       Confirmed values: 1001 = musajung, 1002 = musasa, 1003 = musama. The corresponding +0x0C
       record values for Assassin, Wizard, and Monk stances (presumably in a 10xx/11xx/12xx range
       or similar) have not been read from those files. A quick read of +0x0C across all 12 `.do`
       files would produce the full ref-to-sheet table. — PLAUSIBLE pattern; UNVERIFIED values.
       **(PARTIALLY RESOLVED — CYCLE 7):** the *selection-by-class* half is now closed — the
       selection function's **class-index** classStanceRef is Musa = 1, Assassin = 2, Wizard = 3,
       Monk = 4, with the `{jung, sa, ma}` file triplet per class (§2.8). What remains UNVERIFIED is
       only the **on-disk +0x0C record discriminator** values for the nine non-Musa files — a
       different quantity from the selection class index (see the §2.8 "two distinct quantities" note).

    c. **`musama.do` 40-byte trailing fragment.** `musama.do` is 25,792 bytes = 222 full
       116-byte records + 40 trailing bytes. The loader ignores the incomplete tail. Whether the
       40 bytes are a truncated 223rd record, padding, or an artifact of the file's creation is
       unknown. Cosmetic for parsing purposes. — UNKNOWN.

12. **Item icon native pixel size and inventory cell layout unpinned.** Item DDS files are
    registered with width and height set to zero, so the loader uses DDS-native dimensions.
    The typical pixel size for item icons (32×32? 64×64?) and the inventory window cell grid
    geometry (slot count, cell spacing, icon-to-slot scaling) have not been determined. The
    inventory window chrome builder was not analysed in this pass.
13. **Supplementary skillicon sheets — cell-size and UV source unconfirmed.** Whether
    `stateicon.dds`, `cmonkicon.dds`, and the other non-manifest sheets under
    `data/ui/skillicon/` share the same 23×23 data-driven cell model as the primary `(job,
    kind)` sheets, or use a different grid stride or fixed constants, has not been traced.
    The strongest current lead for `stateicon.dds` (buff/status icons) is a separate
    12-byte-per-record loader for buff icon positions (a "stateicon path" loader called later
    in the engine's bulk asset-init sequence), which likely carries `(buffId, srcX, srcY)` or
    `(buffId, col, row)` per entry. This loader and its draw site need their own trace to
    confirm whether `stateicon.dds` uses stored UV pairs or a fixed grid. — PLAUSIBLE lead;
    UNVERIFIED.

---

## 10. `data/item/texturelist.txt` — item icon texture manifest (CODE-CONFIRMED — 2026-06-13)

### 10.1 Role

Maps numeric texture IDs to the DDS files that provide 2D item icons. This manifest is the
sole source for resolving an item's 2D icon texture from its `tex_id` field. It is loaded by
the same generic line-per-file loader used by the character texture list files
(`data/char/tex512512list.txt`, etc.), but it is not a braced block.

### 10.2 File structure (CODE-CONFIRMED)

The file contains **no block keyword, no braces, and no column headers**. It is a plain
newline-delimited list of filenames, one filename per line:

```
<filename1>
<filename2>
…
```

No `#` comment convention was observed in the parse path. A line that begins with a
non-digit character would produce a tex_id of 0 and register a junk entry; the file
likely has no such lines in practice.

**Approximate file size:** approximately 20,034 bytes, consistent with approximately
1,000+ filename entries at an average of 18–20 bytes per line.

### 10.3 Per-line parsing rules (CODE-CONFIRMED)

| Step | Operation | Detail |
|---|---|---|
| 1 | Read one line (up to 256 bytes) | Shared line-read helper |
| 2 | Locate the last `'.'` in the line | Extension delimiter |
| 3 | Split into `name` (before `.`) and `ext` (`.` plus remainder) | Base name contains the numeric prefix and any trailing label text |
| 4 | Parse leading decimal digits of `name` as integer | `tex_id = atol(name)` — the numeric prefix IS the texture ID |
| 5 | Construct full path | `data/item/texture/` + `name` + `ext` |
| 6 | Register in the item texture registry | Keyed by `tex_id`; width and height passed as 0 (loader reads native DDS dimensions) |

**Example:** a line `1234someicon.dds` yields `tex_id = 1234`, path
`data/item/texture/1234someicon.dds`.

The tex_id key space is non-contiguous. A parser must build a dictionary from the file
and perform a direct-key lookup; array indexing by tex_id is not valid.

### 10.4 Loader behaviour (CODE-CONFIRMED)

- Width and height are passed as **0**, so the DDS native dimensions are used. This
  differs from the character texture lists, which explicitly force specific dimensions.
- The registration call creates one texture entry per line and sets the default source
  rectangle to the **entire texture** (width/height sentinels of -1, -1 in the
  source-rect setter mean full-texture blit).
- The registry is shared across the item icon resolution path and the 3D equip preview
  path (see §10.6).

### 10.5 Item icon draw model — whole-texture blit (CODE-CONFIRMED)

Each item definition record carries a single integer `tex_id` that indexes into this
registry. The 2D inventory icon is drawn as a **full-texture quad**: the entire DDS is
blitted to the inventory cell rectangle. There is no per-item atlas sub-rect.

| Aspect | Value | Confidence |
|---|---|---|
| Source file | `data/item/texture/<filename-with-matching-prefix>` | CODE-CONFIRMED |
| Source rect | Entire texture (no sub-rect, no atlas math) | CODE-CONFIRMED |
| Atlas sharing | None — one DDS file per item icon | CODE-CONFIRMED |
| Destination rect | Inventory cell rectangle (cell geometry not yet pinned — §9 item #12) | CODE-CONFIRMED (mechanism); UNKNOWN (cell geometry) |

This is fundamentally different from the skill-icon model (§2.6), where multiple skill
icons share a single 512×512 sheet and each picks a 23×23 sub-rect by stored coordinates.

### 10.6 3D equip preview — same tex_id, separate code path

The same `tex_id` from an item record is also used to bind a texture to a 3D model for the
rotating equip-preview render. The texture is resolved via the same registry lookup, but is
consumed as a model skin rather than drawn as a flat quad. An engineer implementing the 2D
inventory icon does not need to handle this path; it is noted here only to avoid confusion
when the same tex_id appears in both 2D and 3D contexts.

---

## 12. UI font slot table (15 fixed slots) — CONFIRMED

**CAMPAIGN VFS-MASTERY-B — CONFIRMED (V1 loader: init loops over 15 slots, WinMain populates
exactly 15). NOT file-observable (the table is built in code; there is no font `.txt` in the VFS),
so this is a code-defined fixed table, not a parsed file format — documented here so the front-end
text renderer reproduces it exactly.**

The client builds a fixed array of **15 font slots** at startup. Each slot is a small record
describing one font face the UI can draw with; a widget selects a slot by index. There is no
on-disk font manifest — the slots are populated from constants in the startup path and the per-slot
font object is created with a HANGEUL (CP949) character set so Korean glyphs render.

### 12.1 Per-slot record (logical fields)

Each of the 15 slots carries the following fields (order as built in code; sizes are the logical
field widths a re-implementation needs, not an on-disk stride — this table is never serialised):

| Field | Type | Role | Confidence |
|---|---|---|---|
| `face_name` | string pointer | Font face name passed to the font-creation call (e.g. the front-end default is the Korean `DotumChe` family) | CONFIRMED |
| `size` | int | Nominal point/pixel size requested when the font object is created | CONFIRMED |
| `char_width` | int | Advance width per character used by the UI text-layout math (see §12.3) | CONFIRMED |
| `row_height` | int | Line height used to advance to the next text row | CONFIRMED |
| `weight` | int | Font weight passed to the font-creation call (0 = normal/default) | CONFIRMED |

The font object itself is created with the **HANGEUL character set** so the CP949 game text draws
correctly. A re-implementation should request a Korean-capable font with the same family/size and
fall back gracefully if the exact face is unavailable.

### 12.2 Slot 0 — the front-end default (CONFIRMED)

Slot **0** is the default font used by the front-end (login / server-list / character-select):

| Field | Value | Confidence |
|---|---|---|
| `face_name` | `DotumChe` (Korean fixed-pitch family) | CONFIRMED |
| `size` | 12 | CONFIRMED |
| `char_width` | 6 | CONFIRMED |
| `row_height` | (per-slot; row advance for slot 0) | CONFIRMED (field present) |
| `weight` | 0 (normal) | CONFIRMED |

The remaining 14 slots (1..14) are distinct face/size/weight combinations used by other UI surfaces;
their individual face/size values are part of the recovered table but are not enumerated row-by-row
here beyond the slot-0 default and the per-field shape above. The **count of exactly 15 slots** is
the load-bearing fact for an engineer (allocate 15, index by slot).

### 12.3 Text layout math (CONFIRMED)

The UI text layout uses the slot's own metrics, not per-glyph measurement:

- **Horizontal extent** of a single-line string = `char_width * strlen` (the slot's `char_width`
  times the string length), i.e. a fixed advance per character.
- **Vertical advance** between lines = the slot's `row_height` per line.

Because the advance is a fixed `char_width` (not per-glyph kerning), a faithful re-implementation
should lay out text monospaced at the slot's `char_width`/`row_height` rather than relying on the
host font's natural metrics, or front-end text alignment will drift from the original.

> **DBG-pending:** which slot index paints the very first login frame (the first-paint default) is
> not settled from static analysis; confirm against the live client under the maintainer's debugger.

---

## 13. Manifest loader vs texture loader — `Icon_LoadFileVFSorDisk` loads TEXT, not pixels (CORRECTION)

**CAMPAIGN VFS-MASTERY-B — CONFIRMED correction (supersedes the campaign-8 baseline framing).**

The loader named `Icon_LoadFileVFSorDisk` is **NOT a texture/pixel loader**. It byte-slurps a
**text manifest file** (e.g. a `uitex` / `skillicon`-family registry) into memory and feeds the
manifest **text tokenizer** described in this document; it never decodes image pixels. The earlier
baseline that listed it among the texture loaders was wrong.

- **What it loads:** the raw bytes of a *text* manifest (the braced-block `UiTex.txt` /
  `skillicon.txt` grammars of §1 and §2), which it then hands to the text parser.
- **What it does NOT do:** it does not call the in-memory texture-creation path and does not produce
  a decoded image. Pixel decoding for the DDS files those manifests *reference* happens later, via
  the single unified texture loader documented in `formats/texture.md`.
- **Implication for `Assets.Parsers`:** keep the manifest-text path (this document) and the
  texture-pixel path (`formats/texture.md`) as two distinct loaders. Do not treat
  `Icon_LoadFileVFSorDisk` as a decoder of DDS/PNG/TGA/BMP — it only delivers manifest *text*.

This is a names/framing correction only; it does not change the manifest grammar tabled above.

---

## 14. The code-baked element-construction model (CONFIRMED — 2026-06-16)

> **The other half of the picture.** §1–§13 above cover the *registries* (id → texture path) and
> the *content tables* an engineer loads from disk. This section documents how a window's per-element
> **layout** is actually produced — and the load-bearing fact is that it is **not loaded from any
> file at all**. It is constructed in code. This section is the bridge from "which texture" (the
> registries) to "where on screen / which sub-rect" (the code), with `specs/ui_system.md` holding the
> authoritative per-window literal tables.

### 14.1 Window construction model — `BuildScene` (CONFIRMED)

Each front-end and HUD window is a Diamond UI window object. Its layout is built by a single
virtual method — **`BuildScene`, the primary vtable slot 14 (byte offset +56)** — invoked **once**
from the engine scene state machine immediately after the window's constructor runs. (The state
machine is the 8-case game loop: 0 = cold bootstrap, 1 = LOGIN, 2 = load/opening-skip gate,
3 = OPENING cinematic, 4 = CHARACTER-SELECT, 5 = in-game, 6 = quit, 7 = error; the LoginWindow is
built in the LOGIN case, the SelectWindow in the CHARACTER-SELECT case. See `specs/game_loop.md`.)

The constructor itself builds **no widgets** — it only zero-inits fields, registers the window's
command name, installs its vtables (primary at object +0x00, a secondary interface vtable), and
seeds an initial sub-state field. All widget construction happens in `BuildScene`. [confirmed]

`BuildScene` is one long straight-line routine that:

1. Optionally loads a single scalar tunable from `data/script/uiconfig.lua` (see §14.5) — the only
   data-file input to layout, and it is a scalar **index**, not coordinates.
2. Computes the window origin from screen metrics — e.g. the LoginWindow centres a 1024×768 canvas:
   `originX = screenW/2 − 512`, `originY = screenH/2 − 384`. [confirmed]
3. **Eagerly preloads the window's texture atlases** into the window's own embedded texture list
   (a `GUTextureList` sub-object at a fixed object offset), each via a single texture-load call
   that returns a per-window texture handle (§14.6). HUD windows may resolve an atlas through the
   `UiTex.txt` id; front-end windows hardcode the paths (§14.5).
4. **Constructs every child widget** — allocates the widget object, then makes one GU-builder call
   carrying **literal `dstX, dstY, w, h, srcX, srcY`** values, then parents the widget onto its
   container with the add-child / add-child-with-action helper.
5. Installs a render callback and performs an initial relayout/show.

There is **no per-element file read** in this routine other than the one `uiconfig.lua` scalar and
the hardcoded atlas paths. No element table, no per-element manifest, no source-rect file exists on
disk. [confirmed]

> **General-pattern scope note (capture/debugger-pending for non-Login windows):** the shared GU
> builders and the `BuildScene` slot-14 mechanism are confirmed identical across windows; the full
> element census below is enumerated for the LoginWindow. The claim that *every* window's literals
> follow this exact pattern is confirmed for the builder mechanism and confirmed per-window only as
> each window's `BuildScene` is enumerated (the CharSelect/Main/Loading windows are enumerated in
> `specs/ui_system.md`). The mechanism itself is [confirmed].

### 14.2 GU-builder coordinate contract (CONFIRMED)

Every widget ctor chains a shared base image-component builder first, then appends its own state.
The canonical builder signature is:

```
Build*(self, texHandle, dstX, dstY, w, h, srcX, srcY, color)
```

- **Destination rect** = `(dstX, dstY, w, h)` in the window-local canvas (the 1024×768 front-end
  space, offset by the computed window origin).
- **Atlas source rect** = `(srcX, srcY, w, h)` on the named atlas — **the same `w, h` as the
  destination**. UI blits are strictly **1:1; there is no scaling**. The builder derives the source
  *right* edge as `srcX + w` and the source *bottom* edge as `srcY + h`.
- `color` is a packed tint; `−1` means untinted (draw the atlas pixels as-is).

[confirmed]

The base builder writes the supplied arguments into the GUComponent fields described in §14.3.

### 14.3 GUComponent geometry fields (CONFIRMED — corrects an earlier transposition)

The GU widget base object lays its geometry out as follows. **Width and position were transposed in
an earlier draft; the corrected layout is:**

| Offset | Field | Notes |
|---|---|---|
| +0x0C | packed RGB tint | low 24 bits = RGB; forced-alpha byte at +0x0F |
| +0x14 | local x | builder `dstX` source before world-compute |
| +0x18 | local y | builder `dstY` source before world-compute |
| +0x1C | **WIDTH** | destination/source width `w` |
| +0x20 | **HEIGHT** | destination/source height `h` |
| +0x24 | posX | computed destination left |
| +0x28 | posY | computed destination top |
| +0x2C | computed world x | resolved against parent |
| +0x30 | computed world y | resolved against parent |
| +0x34 | (width span / src-W seed) | seeded from `w` |
| +0x44..+0x83 | 64-byte D3D transform matrix | per-widget transform |
| +0x84 | parent pointer | owning container |

There is **no sized constructor** — only a default zero-init ctor. Geometry is applied afterwards
through the setters/builders, never via constructor arguments. [confirmed]

### 14.4 Multi-state widgets bake every state as an atlas origin (CONFIRMED)

Widgets with visual states store **each state as its own `(srcX, srcY)` literal on a single atlas**.
There is no separate per-state texture and no runtime tinting to fake states — each state is a
distinct sub-rect on the same sheet, baked as code literals:

- **3-state button** — three `(srcX, srcY)` origins: **normal / hover / pressed**, all the same
  `w, h`. A post-construction `SetFrameOrigins(self, nX,nY, hX,hY, pX,pY)` helper can override the
  three origins after the ctor (used where a strip wants per-instance frames). [confirmed]
- **Checkbox** — two `(srcX, srcY)` origins: **off / on**. [confirmed]
- **Label** — no texture (texture handle 0); its caption is set separately from the message DB by
  numeric id (see §14.4 caption note below and §8). [confirmed]
- **Textbox** — base geometry plus an IME-mode field and a max-length field, both set by literal
  immediately after construction. [confirmed]
- **Panel** — base geometry plus an opaque/clip flag. [confirmed]

> **Caption binding.** Label/text captions are bound **at build time** by numeric id from the
> message DB (`msg.xdb`). The LoginWindow row captions use ids 4001..4028 (see §14.7 / §8). This is
> the only "text" a window pulls from disk during build; it is not layout data.

> **Relationship to §2.6.** §2.6's chrome table (icon backplate, frame rings, cooldown sprites) is
> the *skill-window-specific* application of this general model — those are fixed `(srcX, srcY)`
> literals on `mainwindow.dds`. §14.4 states the general button/checkbox frame model that §2.6 is a
> specific instance of.

### 14.5 Front-end windows preload atlases by HARDCODED PATH (bypassing UiTex.txt) (CONFIRMED)

The LoginWindow does **not** resolve its atlases through the `UiTex.txt` id registry. Its
`BuildScene` preloads **exactly four atlases by hardcoded VFS path** into the window's embedded
texture list, in this order, and the builders thereafter reference the returned per-window handles
directly:

| Order | Hardcoded path | Census alias |
|---|---|---|
| 1 | `data/ui/login_slice1.dds` | A1 |
| 2 | `data/ui/loginwindow.dds` | LW |
| 3 | `data/ui/InventWindow.dds` | IW |
| 4 | `data/ui/loginwindow_02.dds` | L2 |

So for the front-end windows even the *texture binding* is code-baked, not registry-driven. §5 lists
several of these files as "hard-coded path" windows; §14.5 states the *mechanism*: preload into the
window's embedded texture list, then build with the returned handles. [confirmed]

**`data/script/uiconfig.lua` — a data-driven UI input.** The LoginWindow `BuildScene` loads this Lua
config and reads a scalar integer key `NEW_SERVER_INDEX`. It is **not** a layout manifest (it carries
no element coordinates) — it is a small scalar-tunables file. It belongs in the cross-reference list
as a genuine data-driven UI input. [confirmed]

### 14.6 Per-window texture-list ownership (CONFIRMED)

Each window embeds its own `GUTextureList` sub-object at a fixed object offset (the LoginWindow's is
at object +0x220). Every atlas is added with one texture-load call,
`Texture_LoadFromVfsOrDisk(textureList, path, flag, -1)`, which appends the atlas and returns a
per-window handle that the element builders reference. The flag argument is the constant
`0x35540004` (decimal 894720068), passed identically for every atlas in the front-end build path.

> **`0x35540004` semantics — capture/debugger-pending.** The flag *value* is confirmed (it is the
> immediate operand of every front-end texture-load call); its *meaning* (a format/usage flag) is a
> static hypothesis not yet confirmed against the running loader. [value confirmed; meaning
> capture/debugger-pending]

### 14.7 Worked element census — LoginWindow (CONFIRMED)

All coordinates below are **source-code integer literals** read from `BuildScene`. Destination =
`(dstX, dstY, w, h)` in the window-local 1024×768 space; Source = `(srcX, srcY)` (the width/height
of the source rect equal the destination `w, h` — see §14.2). The `tex` column names the atlas (see
§14.5 aliases: A1 = login_slice1, LW = loginwindow, IW = InventWindow, L2 = loginwindow_02). The
`action` column is the command id bound at parent time and routed to the LoginWindow event handler.
The window builds **73 widgets** in total (the construction counter runs 0..73). [confirmed]

> **Captioned-row identity — RECONCILED:** rows 1–5 and the 22-label loop are the **server-list /
> channel selection container** (the listbox panel, its scroll-up/down/thumb, its banner, and its
> 22 channel-row captions), **not** an EULA/terms panel. The message-db ids **4001..4022 are the
> server-list / channel ROW CAPTIONS** parented to that container. There is **no EULA/terms panel**
> in the LoginWindow construct (an earlier "EULA" reading and any "built-but-hidden EULA" inference
> are superseded by this element-by-element construct walk). [confirmed]

| # | widget | tex | dst (x, y, w, h) | src (x, y) | action | role |
|---|---|---|---|---|---|---|
| 0 | Image | LW | 0, 110, 1024, 490 | 0, 0 | — | full login backdrop (initially hidden) |
| 1 | Panel | LW | 270, 85, 483, 490 | 0, 490 | — | server-list / channel container |
| 2 | Button | LW | 467, 86, 13, 10 | 483, 490 | 106 | channel list scroll-up |
| 3 | Button | LW | 467, 455, 13, 10 | 505, 490 | 107 | channel list scroll-down |
| 4 | Button | LW | 469, 98, 9, 9 | 496, 490 | 108 | channel list scroll thumb |
| 5 | Image | LW | 207, 44, 70, 17 | 70, 980 | — | server-list / channel banner |
| 6..27 | Label ×22 | (text) | x = 50, y = 100 step +18, 383×50 | — | — | channel row captions; loop while y < 496; captions = msg ids 4001..4022 |
| 30 | Panel | A1 | 0, 0, 1024, 398 | 0, 0 | — | top backdrop band (shown) |
| 31 | Panel | LW | 270, 85, 483, 490 | 0, 490 | — | server-select panel |
| 32 | Image | LW | 207, 44, 70, 17 | 0, 980 | — | server-select banner |
| 33..37 (loop ×2) | Label/Image/Btn3/Label/Label | L2/A1 | per-iter (x = 30 step +233; src x = 448 step +124) | varies | 400+i | server "plate" rows (loop count 2) |
| 38 | Image | LW | 0, 0, 60, 39 | 500, 786 | — | server-status glyph (hidden) |
| 39 | Image | LW | 0, 0, 60, 39 | 500, 786 | — | server-status glyph |
| 40 | Image | LW | 0, 0, 60, 39 | 500, 786 | — | server-status glyph |
| 41 | Image | L2 | 0, (dynamic), 46, 168 | 700, 18 | — | dynamic-Y selection highlight |
| 42 (loop) | Button3State ×N | LW | x = 13 step +47, y = 66, 47×18 | normal(596,985) / hover(643,985) | 115+i | server name-strips; loop while x < 483 |
| — | (frame-origin override) | — | — | 690,985 / 737,985 | — | `SetFrameOrigins` on strip[0] |
| — | (frame-origin override) | — | — | 784,985 / 831,985 | — | `SetFrameOrigins` on strip[1] |
| 43 | Button3State | A1 | 456, -3, 111, 38 | normal(792,398) / hover(602,416) | 105 | quit/help strip |
| 44 | Image | A1 | 407, -3, 210, 70 | 743, 398 | — | title art |
| 45 | Panel | IW | 342, 289, 340, 190 | 318, 647 | — | message/confirm dialog A |
| 46 | Label | (text) | 10, 100, 330, 20 | — | — | dialog A body; msg 4023; centre-aligned |
| 48 | Button3State | IW | 120, 136, 113, 40 | normal(302,900) / hover(415,900) | 113 | dialog A confirm |
| 49 | Panel | IW | 342, 289, 340, 190 | 318, 647 | — | message/confirm dialog B |
| 50 | Label | (text) | 10, 100, 330, 20 | — | — | dialog B body; msg 4024 |
| 52 | Button3State | IW | 120, 136, 113, 40 | normal(302,860) / hover(415,860) | 114 | dialog B confirm |
| 53 | Panel | A1 | 0, 326·screenH/768, 1024, 442 | 0, 582 | — | bottom login-form band |
| 54 | Button3State | A1 | 456, 166, 112, 39 | normal(154,398) / hover(378,398) | 102 | "server list" button |
| 55 | Image | A1 | 265, 0, 494, 113 | 0, 469 | — | login form chrome |
| 56 | Panel | (none) | 0, 0, 1024, 100 | — | — | login-fields sub-panel (hidden) |
| 57 | Image | A1 | 340, 30, 38, 13 | 0, 398 | — | "ID" label glyph |
| 58 | Image | A1 | 507, 30, 49, 13 | 38, 398 | — | "password" label glyph |
| 59 | Image | A1 | 619, 86, 67, 13 | 87, 398 | — | "save id" label glyph |
| 60 | Textbox | A1 | 390, 32, 102, 13 | 615, 404 | 109 | account field; IME = 16, maxlen = 6 |
| 61 | Textbox | A1 | 568, 32, 102, 13 | 615, 404 | 110 | password field; IME = 12, maxlen = 129 |
| 62 | CheckBox | A1 | 694, 86, 13, 13 | off(717,398) / on(730,398) | 104 | save-ID checkbox |
| 65 | Button3State | A1 | 456, 64, 112, 39 | normal(266,398) / hover(490,398) | 103 | OK / login button |
| 66 | (SecondPassword / PIN keypad) | — | 347, 173, 329, 422 | 0, 0 | — | PIN modal; keypad built by its own sub-builder |
| 67 | Panel | A1 | 356, 531, 313, 132 | 0, 0 | — | bottom button-bar panel |
| 68 | Image | A1 | 67, 48, 178, 13 | 0, 437 | — | bar label glyph |
| 69 | Image | A1 | 0, 100, 313, 32 | 289, 437 | — | bar chrome |
| 70 | Button3State | LW | 40, 82, 110, 38 | normal(520,492) / hover(635,492) | 111 | bar button 1 |
| 71 | Button3State | LW | 164, 82, 110, 38 | normal(750,492) / hover(865,492) | 112 | bar button 2 |
| 72 | ExitPanel | IW | 342, 289, 340, 190 | 318, 647 | — | exit-confirm panel (own sub-builder) |
| 73 | ErrorPanel | IW | 342, 289, 340, 190 | 318, 647 | — | error panel (own BuildScene slot +56) |

**Action-id → command** is also code-baked (the third argument of the add-child-with-action helper):
102 = server list, 103 = login, 104 = save-id, 105 = quit/help, 106/107/108 = channel-list scroll,
109 = account field, 110 = password field, 111/112 = bar buttons, 113/114 = dialog confirms,
115+i = server name-strips, 400+i = server plates. These route through the LoginWindow event handler.
[confirmed]

**Sub-panels recurse into their own code-baked builders** with the same literal pattern: the PIN
second-password keypad has its own keypad sub-builder; the exit-confirm panel and the error panel
each build their own children (the error panel via its own slot-14 `BuildScene`). [confirmed]

### 14.8 Parenting helper is add-child, not "register into a window manager" (CONFIRMED — framing correction)

The helper that attaches each built widget to its container is the GU panel **add-child** /
**add-child-with-action** call — it parents the child widget under its panel and, in the
with-action form, binds the routed command id. It is **not** a "register this window into the window
manager" call. The window *manager* is the application MainMaster (the MainWindow) and its
~223-slot service-slot table (service slots reached from object +0x238); a separate per-scene helper
PUSHes scene objects onto a `std::list` teardown/**dispose** list used to tear the scene down — that
dispose-list push is **not** manager attachment either. Keep the three distinct: (a) parent a widget
onto its panel (this §14.8 helper), (b) push a scene object onto the dispose list, (c) the MainMaster
service-slot table. [confirmed]

### 14.9 UI event-record types (CONFIRMED)

For completeness of the construction/dispatch model an engineer reproduces: a UI event record's
type byte at record offset +0 takes these values, and the wheel delta is at record +4.

| Type byte | Event | Source |
|---|---|---|
| 1 | key-down | DirectInput8 keyboard thread |
| 2 | key-up | DirectInput8 keyboard thread |
| 3 | mouse-move | WndProc |
| 4 | button-press | WndProc |
| 5 | button-release | WndProc |
| 6 | CLICK (synthesised) | synthesised only when a release lands on the **same** widget that was pressed — the click-vs-drag discriminator |
| 7 | double-click | WndProc |
| 8 | wheel | WndProc; delta at record +4 |

UI dispatch is **topmost-child-first, first-consumer-wins**, and runs **before** the 3D world view
gets the event. [confirmed]

---

## 11. Cross-references

- **Window-layout AUTHORITY (the code-baked per-element coordinate tables):** `specs/ui_system.md`.
  The registries documented here select *which* atlas; `ui_system.md` holds the authoritative
  per-window element layout (positions, source-rects) baked in each window's `BuildScene` (vtable
  slot 14 / +56). See §0 (scope) and §14 (construction model) above.
- Scene state machine that invokes each window's `BuildScene`: `specs/game_loop.md`
- UI scalar tunables (`NEW_SERVER_INDEX`, etc.): `data/script/uiconfig.lua` — a data-driven UI
  input (scalar config, **not** a layout manifest); read in the front-end `BuildScene` (§14.5)
- Widget binding and screen usage: `specs/ui_system.md`
- Texture format (DDS/TGA physical layout): `formats/texture.md`
- VFS file lookup: `formats/pak.md`
- Binary configuration tables (`.scr`, `.do`, `.ini`): `formats/config_tables.md`
- Per-class stance `.do` skill tables (on-disk source of `iconSrcX`/`iconSrcY`): §2.7 above;
  see also `formats/config_tables.md` for the general `.do` loader pattern. Note: the stride
  for the class-stance skill `.do` files is **116 bytes (0x74)** — this now AGREES with
  `formats/config_tables.md` (§2.2 / §2.16 / §3.5), which also CONFIRMS 116 bytes (0x74) and
  REFUTES the earlier 166-byte (0xA6) estimate (166 divides none of the 12 files). The 116-byte
  stride is CODE-CONFIRMED + SAMPLE-VERIFIED across both specs (CYCLE 1 A3-6, resolved).
- `skillcategory.scr` (17-record × 564-byte category-banner file): see `formats/config_tables.md`
- Character class IDs and skill IDs: `formats/config_tables.md` §2.6 (users.scr classes),
  §2.8 (skills.scr — skill catalog carrying name, cost, cooldown, motion data; does NOT carry
  icon coordinates)
- Character texture lists (same loader family as texturelist.txt): see `formats/config_tables.md`
  for `tex512512list.txt` and its siblings
- Message caption strings: `formats/misc_data.md` §6 (msg.xdb)
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`

> **Provenance — CAMPAIGN VFS-MASTERY (two-witness: loader + black-box):** uitex.txt entry
> count corrected to **37 (EOF-driven, no fixed count)**; crestlist.txt row count corrected to
> **1952 (EOF-driven)** with `type`/`server_id` value sets widened to variable; skillicon.txt
> grammar confirmed as the 4-column record `SKILL { skill_id job_id kind_id "path" }`. Promoted
> as neutral prose; no addresses or decompiler output crossed the firewall.

> **Provenance — CAMPAIGN VFS-MASTERY-B (two-witness reconcile):** added §12 the 15-slot UI font
> table (per-slot face/size/char_width/row_height/weight; slot 0 = DotumChe 12, char_width 6,
> weight 0, HANGEUL charset; layout math = char_width*strlen wide / row_height per line) and §13
> the `Icon_LoadFileVFSorDisk` correction (loads manifest TEXT, not pixels). Reconfirmed the
> EOF-driven (no count field) uitex/crestlist loaders and the 4-token skillicon entry. Promoted as
> neutral prose; no addresses, no decompiler output, and no sample bytes crossed the firewall.

> **Provenance — CAMPAIGN 10 Block B (static-only, anchor 263bd994, 2026-06-16):** added §0
> (scope: these are texture-id registries, not layout manifests) and §14 (the code-baked
> element-construction model — `BuildScene` vtable slot 14 / +56; the GU-builder coordinate
> contract `Build*(tex, dstX, dstY, w, h, srcX, srcY, color)` with a 1:1 src/dst rect; multi-state
> widgets bake every state as an atlas origin; the corrected GUComponent geometry layout
> +0x1C=width/+0x20=height/+0x24=posX/+0x28=posY; front-end hardcoded-atlas preload bypassing
> UiTex.txt; `uiconfig.lua` `NEW_SERVER_INDEX`; the LoginWindow 73-widget element census;
> add-child vs window-manager/dispose-list framing; UI event-record type bytes). **RECONCILED:
> the LoginWindow has NO EULA/terms panel — the captioned container is the server-list/channel
> selection box and msg ids 4001..4022 are its channel row captions** (an earlier EULA reading is
> superseded by the construct walk). Per-element layout authority cross-linked to
> `specs/ui_system.md`. Residuals flagged in the banner: `0x35540004` flag meaning, first-paint
> font slot, and the §11 `.do` 116B-vs-166B config-table stride. Promoted as neutral prose; no
> addresses, no decompiler output, and no sample bytes crossed the firewall.


> **Provenance — CAMPAIGN 14 ★ HUD/UI lane (black-box VFS re-inventory, SAMPLE-VERIFIED):** added
> §1.4a (UI atlas re-inventory: registry **re-confirmed at 37 DDS / 0 MSK**, all 37 paths present in
> the 43,347-entry VFS; key-atlas header dims/fourCC including **blacksheet.dds = 512×512** and
> **stateicon.dds = 512×512**; the **DXT2-vs-DXT3 = premultiplied-vs-straight-alpha, same BC2 block**
> nuance; the stale port "expects 35" diagnostic noted as non-load-bearing) and §5.1a/§5.1b
> (char-select & server-list draw from **shared** atlases — **no dedicated `select.dds` exists**; and
> the **flagged mis-binding**: the corner close button declared `(941,910,23,23)` on the 512×512
> `blacksheet.dds` overflows the atlas — `964 > 512`, `933 > 512` — so the correct atlas is a 1024²
> sheet, **IDA-pending** the char-select construct witness). Promoted as neutral prose; no addresses,
> no decompiler output, and no sample bytes crossed the firewall.
