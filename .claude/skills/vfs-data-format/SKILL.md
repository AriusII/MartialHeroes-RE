---
name: vfs-data-format
description: Use to recover a CP949 text/tab DATA-FILE format (skin.txt, actormotion.txt, bgtexture.txt, *.csv, text *.scr) by observing real bytes via the vfs-inspect harness — NOT by decompiling. Writes neutral column/delimiter findings to Docs/RE/_dirty/formats/<name>.raw.md for later promotion. The sanctioned non-IDA reverse-engineering path for the client's plain-text tables.
allowed-tools: Read Write Bash(dotnet *)
model: opus
---

# vfs-data-format — recover a plain-text data-file format by harness observation

Many of this client's data tables are **plain CP949 text** — tab- or comma-separated rows under
`data/char/` (`skin.txt`, `actormotion.txt`), `data/map000/` (`bgtexture.txt`), and item CSVs.
Their structure can be recovered **without IDA at all**, simply by reading the real bytes from the
VFS and observing the columns. This *data-file observation* is an explicitly sanctioned reverse-
engineering path: there is no decompiler in the loop, so there is no Hex-Rays taint to firewall —
but the findings still land in `_dirty/` and are promoted to a committed spec by `re-promote`, so
the audit trail and the "rewrite, never copy" discipline stay intact end to end.

```
real client text bytes (D:/MartialHeroesClient VFS)
   ──► [vfs-inspect harness reads & previews]  ──► observe columns/delimiter/encoding
   ──► Docs/RE/_dirty/formats/<name>.raw.md     ──► [re-promote rewrites]  ──► Docs/RE/formats/<name>.md
        (dirty findings, gitignored)                                            (committed neutral spec)
```

## When to use this vs. asset-format-doc

- **This skill** — for **text tables** (`.txt`, `.csv`, text-mode `.scr`): delimiter-separated rows,
  CP949 strings, column meanings inferred by reading values across many rows.
- `asset-format-doc` — for **binary** assets (mesh/terrain/anim/texture): byte-offset headers,
  annotated hexdumps.

## Preconditions

1. The bring-your-own client is mounted (`D:/MartialHeroesClient/data.inf` + `data/data.vfs`), and
   `vfs-inspect` runs (it is the read harness this skill drives — never re-implement file reads).
2. You know (or can find via `vfs-inspect`) the entry's virtual path. If unsure, list candidates:
   `… --project <vfsls> -- <stem> .txt` or `--ext .csv`.

## Steps

1. **Confirm the entry exists and size it.** Use `vfs-inspect`:

   ```powershell
   dotnet run -c Release --project ".claude/skills/vfs-inspect/scripts/vfsls" -- --contains data/char/skin.txt
   dotnet run -c Release --project ".claude/skills/vfs-inspect/scripts/vfsls" -- data/char/skin.txt
   ```

2. **Read the head as CP949.** The harness decodes via code page 949 so Korean columns are legible:

   ```powershell
   dotnet run -c Release --project ".claude/skills/vfs-inspect/scripts/vfsls" -- --head data/char/skin.txt --head-bytes 1024
   ```

   Note: ALL game text is CP949 (EUC-KR), never UTF-8. Treat the bytes accordingly.

3. **Characterise the table by observation** (this is the recovery work):
   - **Delimiter** — tab vs comma vs whitespace; is the first row a header or already data?
   - **Comment / blank-line conventions** — lines starting with `//`, `#`, `;`, or a sentinel.
   - **Column count and per-column type** — read several rows and infer each column: integer id,
     float, CP949 label, a relative path (e.g. `tex_id` → `data/char/tex512512/{id}.png`), a flag.
   - **Key column(s)** — which column is the lookup id other tables join on (e.g. `skin IdA` →
     `skin.txt` col 4; `actormotion.txt` col 1 = mob_id). Capture the cross-file relationships,
     since these tables form a join graph (skin ↔ actormotion ↔ bnd ↔ mot, etc.).
   - **Encoding/terminator** — line endings (`\r\n` vs `\n`), trailing delimiter, quoting in CSV.

4. **Write neutral findings to the dirty quarantine.** Create/append
   `Docs/RE/_dirty/formats/<name>.raw.md` (matching the existing `*.raw.md` convention in that dir).
   Lead with a banner:

   ```
   > DIRTY — recovered by harness observation of real client text; never commit; rewrite to promote.
   ```

   Then record: the virtual path, byte size, delimiter, header presence, a **column table**
   (`col# | inferred meaning | type | example value | confidence`), the comment/blank conventions,
   the cross-file join keys, and CP949 confirmation. Describe columns in neutral prose — you may
   quote a *few* representative field values to anchor the meaning, but do **not** dump the whole
   file's contents into the note (it is copyrighted data; keep it to illustrative samples).

5. **Hand off to promotion.** A committed spec is authored separately: run `re-promote` to rewrite
   this `.raw.md` into `Docs/RE/formats/<name>.md` (neutral, citable, `status`/confidence tags), and
   journal it via `re-session-log`. Engineers then implement the parser citing
   `// spec: Docs/RE/formats/<name>.md`.

6. **Report**: the entry path + size, the delimiter/header/encoding verdict, the column count, the
   key join columns discovered, and the `_dirty/formats/<name>.raw.md` path you wrote. State clearly
   that promotion (the committed spec) is the next, separate step.

## Hard rules

- Write findings ONLY under `Docs/RE/_dirty/formats/` — never directly into the committed
  `Docs/RE/formats/` tree. Promotion is `re-promote`'s job (rewrite, never copy).
- Never commit the source text file or paste its full contents anywhere. Quote only short,
  illustrative field values needed to explain a column's meaning.
- Decode as **CP949** (code page 949), always. Do not record mojibake produced by a wrong-encoding
  read as if it were the real column value.
- This is observation of the user's own legally-owned client files — no IDA, no decompiler. If a
  column's meaning truly cannot be inferred from the text and would require reading the loader code,
  mark it `UNVERIFIED` and stop; do not cross into IDA here (that is the `re-*-analyst` agents' lane).
