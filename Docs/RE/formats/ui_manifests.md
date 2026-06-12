# Format: .txt (UI manifest files) — uitex.txt, skillicon.txt, crestlist.txt

> Clean-room spec. Neutral description only — NO sample bytes, NO decompiler pseudo-code.
> Consumed by Assets.Parsers. Every offset an engineer cites must reference this file.
>
> status: PARSER-CONFIRMED for uitex.txt grammar (braced-block tokenizer, UI_TEXTURE/DDS/MSK
>         structure, '#' comments) and skillicon.txt grammar (SKILL block, exact 4-field entry).
>         Content tables for both files additionally SAMPLE-VERIFIED from VFS census.
>         crestlist.txt structure inferred from byte-count and field patterns (PARTIAL).
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

> **Quoting caveat:** one observed entry (`0029`, `data/ui/inactivemember.dds`) has a missing
> closing quote in the source file. A parser must handle a missing closing quote by treating the
> rest of the line (up to the next whitespace or end-of-line) as the path value.

### 1.4 Confirmed ID → path mapping (35 entries in observed version)

| tex_id | VFS path | Notes | Confidence |
|---|---|---|---|
| 0001 | `data/ui/mainwindow.dds` | Main HUD chrome sheet | SAMPLE-VERIFIED |
| 0002 | `data/ui/inventwindow.dds` | Inventory window; also login/select popup chrome | SAMPLE-VERIFIED |
| 0003 | `data/ui/skill_window_1.dds` | Skill window (alternate with mipmaps) | SAMPLE-VERIFIED |
| 0004 | `data/ui/tradekeepwindow.dds` | Trade keep / name-entry chrome | SAMPLE-VERIFIED |
| 0008 | `data/ui/skillwindow.dds` | Main skill window atlas | SAMPLE-VERIFIED |
| 0009 | `data/ui/messagewindow.dds` | Chat / message window | SAMPLE-VERIFIED |
| 0010 | `data/ui/skillpipe.dds` | Skill hotbar (primary) | SAMPLE-VERIFIED |
| 0011 | `data/ui/skillpipe_02.dds` | Skill hotbar (alternate) | SAMPLE-VERIFIED |
| 0013 | `data/ui/direction.dds` | Compass direction indicator | SAMPLE-VERIFIED |
| 0014 | `data/ui/blacksheet.dds` | Black overlay / dimmer | SAMPLE-VERIFIED |
| 0015 | `data/ui/quick.dds` | Quick-slot hotbar | SAMPLE-VERIFIED |
| 0026 | `data/ui/skillicon/stateicon.dds` | Buff / status-effect icons | SAMPLE-VERIFIED |
| 0027 | `data/ui/emoticon.dds` | Chat emoticon sheet | SAMPLE-VERIFIED |
| 0028 | `data/ui/guildicon/guildcresticon1.dds` | Guild crest template sheet 1 | SAMPLE-VERIFIED |
| 0029 | `data/ui/inactivemember.dds` | Inactive member indicator (missing closing quote) | SAMPLE-VERIFIED |
| 0030 | `data/ui/partyleaderflag.dds` | Party leader flag icon | SAMPLE-VERIFIED |
| 0040 | `data/ui/128.bmp` | Shared 128 × 128 BMP resource (24-bit BMP) | SAMPLE-VERIFIED |
| 0041 | `data/ui/map_userpoint.tga` | Player map-dot / waypoint marker | SAMPLE-VERIFIED |
| 0050 | `data/ui/p_green.dds` | Green solid fill patch (4 × 4) | SAMPLE-VERIFIED |
| 0051 | `data/ui/p_red.dds` | Red solid fill patch | SAMPLE-VERIFIED |
| 0052 | `data/ui/p_white.dds` | White solid fill patch | SAMPLE-VERIFIED |
| 0053 | `data/ui/p_blue.dds` | Blue solid fill patch | SAMPLE-VERIFIED |
| 0054 | `data/ui/p_darkblue.tga` | Dark-blue solid fill patch (32-bit TGA) | SAMPLE-VERIFIED |
| 0055 | `data/ui/p_black.tga` | Black solid fill patch | SAMPLE-VERIFIED |
| 0056 | `data/ui/p_yellow.tga` | Yellow solid fill patch | SAMPLE-VERIFIED |
| 0057 | `data/ui/p_orange.tga` | Orange solid fill patch | SAMPLE-VERIFIED |
| 0058 | `data/ui/p_purple.tga` | Purple solid fill patch | SAMPLE-VERIFIED |
| 0069 | `data/ui/yellow.dds` | Yellow gradient / fill (256 × 256) | SAMPLE-VERIFIED |
| 0070 | `data/ui/red.dds` | Red fill | SAMPLE-VERIFIED |
| 0071 | `data/ui/green.dds` | Green fill | SAMPLE-VERIFIED |
| 0072 | `data/ui/blue.dds` | Blue fill | SAMPLE-VERIFIED |
| 0073 | `data/ui/counter.dds` | Counter digit strip (64 × 512) | SAMPLE-VERIFIED |
| 0074 | `data/ui/white.dds` | White fill | SAMPLE-VERIFIED |
| 0075 | `data/ui/target_16x16.dds` | Target indicator — small | SAMPLE-VERIFIED |
| 0076 | `data/ui/target_64x64.dds` | Target indicator — large | SAMPLE-VERIFIED |
| 0077 | `data/ui/countinput.dds` | Item count input dialog | SAMPLE-VERIFIED |
| 0078 | `data/ui/edge.dds` | Edge border texture | SAMPLE-VERIFIED |

> **Coverage note:** `UiTex.txt` accounts for 35–38 of the approximately 130 root-level
> `data/ui/` entries. The remainder are loaded by hard-coded paths in the per-screen build
> routines (login, select, in-game) rather than through this manifest. See `specs/ui_system.md`
> for the per-screen asset manifests.

### 1.5 Path resolution

Paths are VFS-relative strings beginning with `data/`. The engine looks them up via the VFS
binary-search index (`data.inf`). A three-format fallback exists: the path extension in the
manifest is treated as a hint; if the asset is not found under the exact path, the engine may
try alternative extensions (DDS → TGA → BMP) — the exact fallback order is UNVERIFIED.

### 1.6 MSK block (PARSER-CONFIRMED keyword; semantics PROPOSED)

The `MSK` sub-block keyword is confirmed from a string constant adjacent to the `DDS` and
`UI_TEXTURE` string constants in the binary data segment (PARSER-CONFIRMED). The block is
present in the file structure but contains no entries in the analysed version.

`MSK` is interpreted as "mask texture" — a separate greyscale or alpha-channel texture used
to cut out non-rectangular UI elements or to provide per-pixel transparency for UI panels.
This interpretation is consistent with the D3D9 UI pattern of pairing a colour DDS with a
separate mask texture. **This interpretation is PROPOSED and SAMPLE-UNVERIFIED** — the actual
content and extension of MSK-block entries has not been observed from real file data (the block
is empty in the known version). Possible extensions: `.msk`, `.dds` with explicit alpha channel.

A parser must accept and silently skip the MSK block regardless of semantics.

### 1.7 Shared tokenizer engine (PARSER-CONFIRMED)

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

### 2.2 File structure (PARSER-CONFIRMED)

Same braced-block tokenizer with `#` comment convention as `UiTex.txt` (§1.7). The outer
block keyword is `SKILL` (PARSER-CONFIRMED from string constant in the binary).

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
`UiTex.txt` path extraction described in §1.7.

### 2.4 Confirmed entries (22 sheets)

The following icon sheets are confirmed in the VFS census and in the manifest:

| job_id | kind_id | Path | Dimensions | Confidence |
|---|---|---|---|---|
| 1 | 1 | `data/ui/skillicon/musajung.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 1 | 2 | `data/ui/skillicon/musasa.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 1 | 3 | `data/ui/skillicon/musama.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 2 | 1 | `data/ui/skillicon/assasinjung.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 2 | 2 | `data/ui/skillicon/assasinsa.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 2 | 3 | `data/ui/skillicon/assasinma.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 3 | 1 | `data/ui/skillicon/wizardjung.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 3 | 2 | `data/ui/skillicon/wizardsa.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 3 | 3 | `data/ui/skillicon/wizardma.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 4 | 1 | `data/ui/skillicon/monkjung.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 4 | 2 | `data/ui/skillicon/monksa.dds` | 512 × 512 | SAMPLE-VERIFIED |
| 4 | 3 | `data/ui/skillicon/monkma.dds` | 512 × 512 | SAMPLE-VERIFIED |
| (shared) | — | `data/ui/skillicon/stateicon.dds` | 512 × 512 | SAMPLE-VERIFIED (also in UiTex.txt id 0026) |

Additional icon sheets present in the VFS but whose `skillicon.txt` entries were not individually
decoded (present in the manifest; class association is from file naming):

| Path | Dimensions | Probable class | Confidence |
|---|---|---|---|
| `data/ui/skillicon/cmonkicon.dds` | 256 × 256 | Monk class icon | SAMPLE-VERIFIED (file present) |
| `data/ui/skillicon/gungsaicon.dds` | 256 × 256 | Gungsa class icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/pmusaicon.dds` | 256 × 256 | Premium Musa icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/sdocicon.dds` | 256 × 256 | Sdoc class icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/segumicon.dds` | 256 × 256 | Segum class icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/smusaicon.dds` | 256 × 256 | Smusa icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/wizardicon.dds` | 256 × 256 | Wizard class icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/minddashicon.dds` | 256 × 256 | Mind dash icon | SAMPLE-VERIFIED |

> **Within-sheet icon layout:** the `(u, v)` position of a specific skill's icon within its
> 512 × 512 sheet was not recovered. The icon grid stride and slot layout are UNKNOWN. This is
> a separate open task for the assets engineer.

---

## 3. `data/ui/guildicon/crestlist.txt` — guild crest pool registry

### 3.1 Role

Enumerates the set of per-guild player-uploaded crest images available in
`data/ui/guildicon/pool/`. The file is approximately 47,199 bytes and contains roughly 1,350
entries (estimate; actual count varies with CP949 Korean name lengths in the file). It is
consulted when rendering guild crests in the guild window and on the character list.

### 3.2 Inferred file structure

The file is CP949, tab-delimited. Each line corresponds to one entry in
`data/ui/guildicon/pool/`. Based on the byte-count and observed content decoding:

| Field | Type | Notes | Confidence |
|---|---|---|---|
| Filename | CP949 string | The bare filename (matching a `data/ui/guildicon/pool/` entry) | SAMPLE-VERIFIED (pattern; individual rows not decoded) |
| Guild name(s) | CP949 string(s) | Decoded sample shows Korean guild-name text; may be one or more fields | PARTIAL |

> **Format caveat — PARTIAL:** the exact column count, delimiter character, and whether a header
> row is present were not individually verified. The 47,199-byte file size and ~1,350 estimated
> entries imply roughly 35 bytes per line on average; this is consistent with a filename plus one
> Korean guild name per line. A parser should not assume a fixed column count until a full line
> decode is performed.

### 3.3 Pool directory structure

The `data/ui/guildicon/pool/` directory contains **2,235 entries**, each approximately 704 bytes.
These are small per-guild crest images (player-uploaded). Template crest sheets live in the
parent directory as `data/ui/guildicon/guildcresticon1.dds` through `guildcresticon12.dds` and
`guildcresticon25.dds` (13 template sheets, 512 × 512 DDS each).

---

## 4. Cursor file census (`data/cursor/`)

Cursor assets do not have a dedicated manifest text file. The complete set of 12 entries is:

| Path | Format | Dimensions | Notes | Confidence |
|---|---|---|---|---|
| `data/cursor/stand.dds` | DDS DXT1 | 32 × 32 | Default cursor | SAMPLE-VERIFIED |
| `data/cursor/rotate.dds` | DDS DXT1 | 32 × 32 | Camera-rotate cursor | SAMPLE-VERIFIED |
| `data/cursor/repaircursor.dds` | DDS DXT1 | 32 × 32 | Item-repair cursor | SAMPLE-VERIFIED |
| `data/cursor/battle.dds` | DDS DXT1 | 64 × 64 | Attack / battle cursor | SAMPLE-VERIFIED |
| `data/cursor/hand-jap-01.dds` | DDS DXT1 | 64 × 64 | Hand cursor variant 1 | SAMPLE-VERIFIED |
| `data/cursor/hand-jap-02.dds` | DDS DXT1 | 64 × 64 | Hand cursor variant 2 | SAMPLE-VERIFIED |
| `data/cursor/hand-jap-03.dds` | DDS DXT1 | 64 × 64 | Hand cursor variant 3 | SAMPLE-VERIFIED |
| `data/cursor/hand-jap-04.dds` | DDS DXT1 | 64 × 64 | Hand cursor variant 4 | SAMPLE-VERIFIED |
| `data/cursor/curse.txt` | text, CP949 | — | Profanity substitution table (tab-delimited) | SAMPLE-VERIFIED |
| `data/cursor/cursechat.txt` | text, CP949 | — | Chat word-filter substitution table (tab-delimited) | SAMPLE-VERIFIED |
| `data/cursor/game.ver` | binary | — | 28-byte version record (appears twice in VFS — duplicate entry) | SAMPLE-VERIFIED |

Key facts:
- **No `.cur` or `.ani` files exist in the VFS.** Cursors are rendered as textured quads in the
  engine; the WndProc calls `SetCursor(NULL)` to hide the OS cursor, and a software cursor
  drawn over the scene takes over.
- All 8 cursor DDS files are **DXT1** (uncompressed quads); the 32 × 32 variants are
  `32 × 32 × 1 byte DXT1 + 128-byte DDS header = 1,152 bytes`; the 64 × 64 variants are
  `64 × 64 × 1 byte DXT1 + 128-byte header = 4,224 bytes`.
- `game.ver` is a 28-byte binary record. CP949 decoding produces no legible text. Based on the
  file size a plausible layout is 7 × 4-byte fields (little-endian). The exact field
  definitions are UNKNOWN; the client uses the version token for the enter-game request
  (see `specs/login_flow.md` §3.3). The duplicate VFS entry is likely a packaging artifact.

---

## 5. The `do.dds` mislabelled-extension caveat

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

## 6. DDS atlas physical format summary

All large UI sprite sheets in `data/ui/` follow this physical layout:

| Property | Value | Confidence |
|---|---|---|
| DDS magic | `44 44 53 20` ("DDS ") at offset 0 | SAMPLE-VERIFIED |
| DDS header size | 128 bytes | SAMPLE-VERIFIED |
| Pixel format (large sheets) | DXT3 — FourCC `44 58 54 33` ("DXT3") at offset 0x54 | SAMPLE-VERIFIED |
| Pixel format (cursor sheets) | DXT1 — FourCC `44 58 54 31` ("DXT1") at offset 0x54 | SAMPLE-VERIFIED |
| Mip-0 resolution (large atlas) | 1024 × 1024 (common); 512 × 512 (smaller windows) | SAMPLE-VERIFIED |
| Mip count | No mip chain on most large sheets (single mip-0 level) | SAMPLE-VERIFIED |
| Mip count (some windows) | Mip chain present on a subset (e.g. `password.dds`, `countinput.dds`, `cubegamble.dds`, `fame_buff_window.dds`) — indicated by larger file size | SAMPLE-VERIFIED |
| Endianness | Little-endian throughout (standard DDS) | SAMPLE-VERIFIED |

> **Loader FourCC note:** the legacy loader passes `DXT2` internally as the expected pixel format
> code for atlas sheets. DXT2 and DXT3 are both the BC2 block format; they differ only in whether
> the alpha channel is pre-multiplied. The actual pixel format code in every sampled file is DXT3.
> A parser should accept both `DXT2` and `DXT3` FourCCs and treat them identically.

BMP exception: `data/ui/128.bmp` (tex_id 0040) is a 128 × 128, 24-bit Windows BMP. No BMP
files were found outside this single entry in the confirmed `data/ui/` root set.

TGA assets: solid-colour fill patches (the `p_*.tga` group) and some cursor-adjacent assets are
32-bit RGBA TGA. The map waypoint (`map_userpoint.tga`) and character face thumbnails
(`data/ui/face/*.tga`) are also TGA.

---

## 7. Known unknowns

1. **Exact column count and header structure of `crestlist.txt`:** the file was not decoded
   line by line; column layout is inferred from byte statistics and partial content sampling.
2. **Within-sheet skill icon UV layout:** the position of each individual skill's icon within
   its 512 × 512 sheet is not documented here; a separate icon-grid analysis is required.
3. **`MSK` block semantics:** the keyword is PARSER-CONFIRMED; the block is present but empty
   in the analysed version. The "mask texture" interpretation is PROPOSED and SAMPLE-UNVERIFIED.
   The extension of MSK-block entries (`.msk` vs `.dds`) and their exact role in alpha blending
   are unknown.
4. **Path fallback order:** whether the loader tries DDS → TGA → BMP when the manifest-specified
   extension is not found is UNVERIFIED.
5. **`game.ver` field definitions:** the 28-byte binary layout of the version record is not
   resolved beyond byte count and the use of the version token in the login flow.
6. **Item texture manifest:** `data/item/texturelist.txt` is a separate manifest for item icon
   sheets (`data/item/texture/*.dds`). It was identified in the VFS census but not yet decoded.
   Its format is likely the same brace-block convention; record it separately when analysed.
7. **`data/ui/UiTex.txt` IDs above 0078:** whether IDs above 0078 exist in other versions or
   in later VFS updates is UNKNOWN. Only the 35–38 entries in the analysed version are confirmed.
8. **`UiTex.txt` entry sub-field count in `DDS` block:** the integer-before-path structure
   (one integer + one quoted string) is PARSER-CONFIRMED for `skillicon.txt`. For `UiTex.txt`
   the entry-processing routine was located but its internal field loop was not fully
   traced; the `tex_id + path` two-field layout is confirmed by sample observation and is
   consistent with the parser structure, but additional optional fields cannot be ruled out.

---

## 8. Cross-references

- Widget binding and screen usage: `specs/ui_system.md`
- Texture format (DDS/TGA physical layout): `formats/texture.md`
- VFS file lookup: `formats/pak.md`
- Binary configuration tables (`.scr`, `.do`, `.ini`): `formats/config_tables.md`
- Character class IDs and skill IDs: `formats/config_tables.md` §2.6 (users.scr classes),
  §2.8 (skills.scr skill IDs)
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
