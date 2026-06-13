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
>         crestlist.txt structure SAMPLE-VERIFIED for filename pattern and record count;
>         pool blob layout PARTIAL (704-byte exact size; pixel dimensions not byte-confirmed).
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

### 1.4 Confirmed ID → path mapping (35 entries in observed version; SAMPLE-VERIFIED)

> **Coverage note:** this file contains **35 confirmed entries** with IDs spanning the
> non-contiguous range **0001–0078**. The ID space has large gaps (see §1.5 below).
> `UiTex.txt` accounts for 35 of the approximately 140 root-level `data/ui/` entries.
> The remainder are loaded by hard-coded paths in the per-screen build routines.
> See `specs/ui_system.md` for the per-screen asset manifests.

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
| `data/ui/skillicon/cmonkicon.dds` | 262,272 | 512×512 | Common Monk class icon set | SAMPLE-VERIFIED |
| `data/ui/skillicon/gungsaicon.dds` | 262,272 | 512×512 | Gungsa (crossbow) class icons | SAMPLE-VERIFIED |
| `data/ui/skillicon/pmusaicon.dds` | 262,272 | 512×512 | Premium Musa class icons | SAMPLE-VERIFIED |
| `data/ui/skillicon/sdocicon.dds` | 262,272 | 512×512 | Sdoc class icons | SAMPLE-VERIFIED |
| `data/ui/skillicon/segumicon.dds` | 262,272 | 512×512 | Segum class icons | SAMPLE-VERIFIED |
| `data/ui/skillicon/smusaicon.dds` | 262,272 | 512×512 | Smusa (super-warrior) icons | SAMPLE-VERIFIED |
| `data/ui/skillicon/wizardicon.dds` | 262,272 | 512×512 | Wizard class overview icon | SAMPLE-VERIFIED |
| `data/ui/skillicon/minddashicon.dds` | 131,200 | 512×256 | Mind dash (half-size sheet) | SAMPLE-VERIFIED |

**Grand total under data/ui/skillicon/:** 22 entries (12 manifest sheets + 1 stateicon also in
uitex.txt + 9 supplementary = 22 files, consistent with the observed VFS entry count for this
subdirectory).

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

| File | job | kind | classStanceRef | Sheet (from skillicon.txt) |
|---|---|---|---|---|
| `data/script/musajung.do` | 1 (Musa) | 1 (jung) | 1001 | `musajung.dds` |
| `data/script/musasa.do` | 1 (Musa) | 2 (sa) | 1002 | `musasa.dds` |
| `data/script/musama.do` | 1 (Musa) | 3 (ma) | 1003 | `musama.dds` |
| `data/script/assasinjung.do` | 2 (Assassin) | 1 (jung) | TODO | `assasinjung.dds` |
| `data/script/assasinsa.do` | 2 (Assassin) | 2 (sa) | TODO | `assasinsa.dds` |
| `data/script/assasinma.do` | 2 (Assassin) | 3 (ma) | TODO | `assasinma.dds` |
| `data/script/wizardjung.do` | 3 (Wizard) | 1 (jung) | TODO | `wizardjung.dds` |
| `data/script/wizardsa.do` | 3 (Wizard) | 2 (sa) | TODO | `wizardsa.dds` |
| `data/script/wizardma.do` | 3 (Wizard) | 3 (ma) | TODO | `wizardma.dds` |
| `data/script/monkjung.do` | 4 (Monk) | 1 (jung) | TODO | `monkjung.dds` |
| `data/script/monksa.do` | 4 (Monk) | 2 (sa) | TODO | `monksa.dds` |
| `data/script/monkma.do` | 4 (Monk) | 3 (ma) | TODO | `monkma.dds` |

The classStanceRef values for the nine non-Musa files are unconfirmed this pass (§9 item #11c).

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

---

## 3. `data/ui/guildicon/crestlist.txt` — guild crest pool registry

### 3.1 Role

Enumerates the set of per-guild player-uploaded crest images available in
`data/ui/guildicon/pool/`. The file is consulted when rendering guild crests in the guild
window and on the character list.

### 3.2 File structure (SAMPLE-VERIFIED for filename pattern; column layout PARTIAL)

**File size (SAMPLE-VERIFIED):** 47,199 bytes. CP949 encoding, CRLF line endings.
**Estimated entry count:** approximately 1,350 lines (47,199 bytes / ~35 average bytes per line;
exact count varies with CP949 Korean guild name lengths).

The file is plain text with one filename per line. Each line is the bare filename of a pool
crest image stored under `data/ui/guildicon/pool/`. No column headers are present.

**Filename naming convention (SAMPLE-VERIFIED from multiple decoded lines):**

```
{region}_{type}_{guild_id}_{server_id}_{guild_name}.dds
```

| Component | Observed values | Notes | Confidence |
|---|---|---|---|
| `region` | Always `1` in all observed samples | Constant in known data | SAMPLE-VERIFIED |
| `type` | Always `4` in all observed samples | Constant in known data | SAMPLE-VERIFIED |
| `guild_id` | Integer (e.g. 666, 1183, 363, 903) | Unique guild identifier | SAMPLE-VERIFIED |
| `server_id` | `1` or `2` | Server origin of the guild | SAMPLE-VERIFIED |
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
pool files). The precise pixel dimensions for a 704-byte DDS crest are not yet confirmed from
a header read; the most consistent candidate is a 12×12 pixel uncompressed ARGB8888 image
(12 × 12 × 4 bytes per pixel + 128-byte DDS header = 576 + 128 = 704 bytes), but this
dimension is PARTIAL — the DDS header at bytes 0x0C–0x13 (height and width fields) has not
been byte-confirmed from a pool entry.

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
| 1,398,256 | 1024×1024 | DXT3 | full (11 levels) | sum of all mip levels + 128 |
| 1,048,704 | 1024×1024 | DXT3 or DXT5 | none | ceil(1024/4)×ceil(1024/4)×16 + 128 |
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
| 0x54–0x57 | 4 bytes ASCII | FourCC pixel format code | See table below |

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

---

## 5. Window DDS files NOT in uitex.txt (hard-coded paths; SAMPLE-VERIFIED)

These files are loaded directly by VFS path in per-screen build routines and are not registered
in the `UiTex.txt` ID system. An engineer must hard-code their paths, not look them up by ID.

### 5.1 Login and character select screens

| VFS path | Size (bytes) | Dimensions | Format | Role |
|---|---|---|---|---|
| `data/ui/loginwindow.dds` | 1,048,704 | 1024×1024 | DXT5 | Login screen chrome — the only DXT5 in data/ui/ |
| `data/ui/loginwindow_02.dds` | 1,048,704 | 1024×1024 | DXT3 | Login screen variant 2 |
| `data/ui/login_base.dds` | 1,048,704 | 1024×1024 | DXT3 | Login base layer |
| `data/ui/login_new.dds` | 1,048,704 | 1024×1024 | DXT3 | New character creation screen |
| `data/ui/login_slice1.dds` | 1,048,704 | 1024×1024 | DXT3 | Login screen slice 1 |
| `data/ui/password.dds` | 1,398,256 | 1024×1024 | DXT3, full mips | Secondary password dialog |
| `data/ui/server_icon.dds` | 16,512 | 128×128 | DXT3 | Server selection icon |

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
| `data/ui/characwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Character stats window |
| `data/ui/masterwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Master/NPC window |
| `data/ui/statusquestexitwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Status/quest/exit panel |
| `data/ui/shwdnwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Shutdown confirmation |
| `data/ui/moonpa.dds` | 1,048,704 | 1024×1024 | DXT3 | Moon-PA event window |
| `data/ui/myway.dds` | 1,048,704 | 1024×1024 | DXT3 | My-way / personal path window |
| `data/ui/pandemonium.dds` | 1,048,704 | 1024×1024 | DXT3 | Pandemonium event |
| `data/ui/revengesummons.dds` | 1,048,704 | 1024×1024 | DXT3 | Revenge summons window |
| `data/ui/skillprodotherwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Skill production window |
| `data/ui/skilltree.dds` | 1,048,704 | 1024×1024 | DXT3 | Skill tree view |
| `data/ui/tradepartywindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Trade/party window |
| `data/ui/popdepositwindow.dds` | 1,048,704 | 1024×1024 | DXT3 | Deposit/bank pop window |
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
| `data/ui/fame_buff_window.dds` | 2,097,280 | 1024×2048 | DXT3 | Fame buff window (double-height) |
| `data/ui/12pahwangtext.dds` | 1,048,704 | 1024×1024 | DXT3 | 12-pahwang event text |
| `data/ui/blacksheet copy.dds` | 1,048,704 | 1024×1024 | DXT3 | Blacksheet copy variant |

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
| `data/ui/tender_window.dds` | 524,416 | 1024×512 | DXT3 | Tender/auction wide panel |
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
   decoded line-by-line exhaustively.
5. **Pool crest pixel dimensions:** the 704-byte pool entry exact resolution (probable 12×12
   ARGB8888 = 576 + 128 = 704) is unconfirmed without reading the pool DDS header bytes at
   offsets 0x0C–0x13.
6. **Path fallback order:** whether the loader tries DDS → TGA → BMP when the manifest-specified
   extension is not found is UNVERIFIED.
7. **uitex.txt IDs above 0078:** whether IDs above 0078 exist in other client versions is
   unknown. Only the 35 entries in the analysed version are confirmed.
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

    b. **`classStanceRef` enumeration for the nine non-Musa files.** Confirmed values:
       1001 = musajung, 1002 = musasa, 1003 = musama. The corresponding values for Assassin,
       Wizard, and Monk stances (presumably in a 10xx/11xx/12xx range or similar) have not been
       read from those files. A quick read of +0x0C across all 12 `.do` files would produce the
       full ref-to-sheet table. — PLAUSIBLE pattern; UNVERIFIED values.

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

## 11. Cross-references

- Widget binding and screen usage: `specs/ui_system.md`
- Texture format (DDS/TGA physical layout): `formats/texture.md`
- VFS file lookup: `formats/pak.md`
- Binary configuration tables (`.scr`, `.do`, `.ini`): `formats/config_tables.md`
- Per-class stance `.do` skill tables (on-disk source of `iconSrcX`/`iconSrcY`): §2.7 above;
  see also `formats/config_tables.md` for the general `.do` loader pattern. Note: the stride
  for the class-stance skill `.do` files is **116 bytes (0x74)** — this CONTRADICTS the
  `config_tables.md` entry for `monkma.do` and class-stance variants which states 166 bytes
  (0xA6). The 116-byte stride is CODE-CONFIRMED + SAMPLE-VERIFIED here; the `config_tables.md`
  entry should be corrected by the config-table analyst.
- `skillcategory.scr` (17-record × 564-byte category-banner file): see `formats/config_tables.md`
- Character class IDs and skill IDs: `formats/config_tables.md` §2.6 (users.scr classes),
  §2.8 (skills.scr — skill catalog carrying name, cost, cooldown, motion data; does NOT carry
  icon coordinates)
- Character texture lists (same loader family as texturelist.txt): see `formats/config_tables.md`
  for `tex512512list.txt` and its siblings
- Message caption strings: `formats/misc_data.md` §6 (msg.xdb)
- Glossary: `Docs/RE/names.yaml`
- Provenance: `Docs/RE/journal.md`
