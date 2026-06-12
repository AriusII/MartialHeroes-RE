---
name: vfs-inspect
description: Use to open the REAL Martial Heroes VFS (data.inf + data/data.vfs at D:/MartialHeroesClient) and list/inspect entries by substring — the throwaway console harness for "does this path exist?", "how big is it?", "what's the first N bytes?", "how many .skn/.ted/.txt files are there?". Bundles a ready-made net10.0 harness so you never hand-rebuild it again.
allowed-tools: Read Write Bash(dotnet *) Bash(mkdir *) Bash(copy *) Bash(xcopy *)
model: sonnet
---

# vfs-inspect — open the real VFS and inspect entries

The single home for the throwaway VFS browser that has been hand-rebuilt five times. It mounts the
real client archive through the production VFS API and lets you list entries, count by extension,
test for a path, and peek at the head bytes of any file — without writing a test or touching Godot.

It drives the production library directly:
`MartialHeroes.Assets.Vfs.MappedVfsArchive.Open(infPath, vfsPath)` with
`infPath = "D:/MartialHeroesClient/data.inf"` and `vfsPath = "D:/MartialHeroesClient/data/data.vfs"`.
`GetEntries()` returns a `ReadOnlySpan<VfsEntry>` (each `.Name` is the lower-cased virtual path,
`.DataOffset`/`.DataSize` describe the blob slice); `GetFileContent(path)` returns a zero-copy
`ReadOnlyMemory<byte>`; `Contains(path)` tests membership. The VFS holds ~43,347 entries and all
text payloads are CP949 (Korean code page 949), so the harness registers the code-pages provider.

## What this is — and is NOT

- It is a **THROWAWAY diagnostic harness**. It is bundled under this skill's `scripts/vfsls/`, NOT
  under the five numbered layer folders, and it is **never added to `MartialHeroes.slnx`** and
  **never committed** as a solution member. It exists to answer one-off questions fast.
- It is **read-only** over the VFS. It never extracts or rewrites archive payloads, and it prints
  only metadata plus, on request, a short hex/decoded **head** preview — never a full file dump and
  never a copyrighted asset in full.
- It is **not** a clean-room concern: it reads the user's own legally-owned client files at
  `D:/MartialHeroesClient/`, the same bytes the production VFS reads. No IDA, no `_dirty/`.

## Preconditions

1. The real client must be present at `D:/MartialHeroesClient/` with both `data.inf` and
   `data/data.vfs`. If the drive/path differs, pass `--inf` / `--vfs` overrides (see below). If the
   files are absent, STOP and tell the user the harness needs the bring-your-own client mounted.
2. A .NET 10 SDK must be installed (`dotnet --version` ≥ 10). The harness targets `net10.0`.
3. The production VFS project must build: the harness `ProjectReference`s
   `03.Storage.Assets/MartialHeroes.Assets.Vfs/MartialHeroes.Assets.Vfs.csproj` and
   `03.Storage.Assets/MartialHeroes.Assets.Parsers/MartialHeroes.Assets.Parsers.csproj` by absolute
   path, so it always tracks the live API. If the API drifted, the harness fails to compile —
   that is a feature, fix the call site.

## Steps

1. **Read the bundled harness** so you know what it does and can tweak it if needed:
   - `${CLAUDE_SKILL_DIR}/scripts/vfsls/Program.cs` — the parametrized inspector.
   - `${CLAUDE_SKILL_DIR}/scripts/vfsls/vfsls.csproj` — `net10.0` exe with the two absolute
     `ProjectReference`s.

2. **Run it in place** with `dotnet run -c Release`. The skill dir IS the project dir — run from
   `scripts/vfsls/`:

   ```powershell
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- <args>
   ```

   First build pulls in the two referenced projects; subsequent runs are fast. Use `-c Release`
   so the memory-mapped read path is not debug-throttled.

3. **Pick the query via args** (all optional; with no args it prints a summary + extension census):

   | Arg | Effect |
   |---|---|
   | `<substring> [<substring> …]` | List entries whose lower-cased name contains EVERY given substring (AND). |
   | `--ext .skn` | Filter to a single extension (repeatable: `--ext .ted --ext .map`). |
   | `--count` | Print only the match count, not the per-entry lines. |
   | `--census` | Print the entry count grouped by extension (the inventory view). |
   | `--head <path>` | Print the first 256 bytes of one entry as hex + a CP949-decoded preview. |
   | `--head-bytes <n>` | Change the head preview length (default 256). |
   | `--contains <path>` | Print just `true`/`false` for one exact virtual path. |
   | `--limit <n>` | Cap the number of listed entries (default 200; `0` = unlimited). |
   | `--inf <path>` / `--vfs <path>` | Override the default `D:/MartialHeroesClient` locations. |

   Examples:

   ```powershell
   # Census of every extension (the 49-extension inventory):
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --census

   # Every skin-table-ish text file under data/char:
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- data/char .txt

   # Does the global texture catalog exist, and what's in its head?
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --contains data/map000/bgtexture.txt
   dotnet run -c Release --project "${CLAUDE_SKILL_DIR}/scripts/vfsls" -- --head data/map000/bgtexture.txt
   ```

4. **Report findings as metadata**: counts, names, sizes (and, for `--head`, the decoded preview).
   Quote the exact virtual path strings so the next caller can re-query. Do not paste large dumps.

## Hard rules

- **Never register this harness in `MartialHeroes.slnx`** and never `git add` its build output. It
  is intentionally a loose, gitignored-by-convention diagnostic under `.claude/skills/`. (Its
  `bin/`/`obj/` are the usual transient build dirs.)
- Print **metadata and short head previews only** — never extract a full asset or dump an entire
  file's bytes. This harness inspects; it does not export.
- Always decode text via the CP949 provider the harness wires up
  (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` then `Encoding.GetEncoding(949)`).
  Do not assume UTF-8 — Korean game text will mojibake.
- If the harness will not compile because the VFS API changed, fix the harness call site to match
  the live `MappedVfsArchive` surface; do not patch the production library to suit the harness.
