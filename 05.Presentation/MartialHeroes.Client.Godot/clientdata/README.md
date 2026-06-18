# clientdata/ — bring your own Martial Heroes client data

This folder is the **project-local home for the original game's VFS**, so the Godot client
finds its assets without any environment variable or external path:

```
clientdata/
├── data.inf         ← copy from your original client install   (VFS index,   ~6 MB)   REQUIRED
├── data/
│   └── data.vfs     ← copy from <client>/data/data.vfs          (VFS archive, ~3.8 GB) REQUIRED
└── companion config (optional — copied from the client root for a self-contained mirror):
    option.ini  DoOption.ini  do.ini  combo.ini  panel.ini  TSIDX.ini  game.ver
```

**Required:** only `data.inf` + `data/data.vfs` — that is the single path the runtime resolves
(everything the client renders lives inside the VFS). **Companion `.ini`/`.ver`:** optional copies
of the client-root config so this folder mirrors the original install completely. The runtime does
**not** read them from here today — the writable options (`option.ini` / `DoOption.ini`) are mapped
to a Godot-native `user://options.cfg` `ConfigFile`, and `game.ver` is read from inside the VFS
(`data/cursor/game.ver`). They are kept as a faithful reference / future-proofing only; they change
no current behaviour. **Nothing is ever read from `D:\d.o online` (or any external path) at runtime.**

`ClientPathResolver` (see `Dev/ClientPathResolver.cs`) probes, in order:
`MH_CLIENT_DIR` env override → `client_dir.cfg` → **this folder** → `D:\MartialHeroesClient`
→ `C:\MartialHeroesClient` → `LegacyClient/`. If nothing is found the client boots in
synthetic mode (generated placeholder scene).

## Non-distribution contract (non-negotiable)

These files are **copyrighted originals supplied by you, the user**. They are never
committed, never redistributed, never uploaded. The local `.gitignore` ignores everything
in this folder except itself and this README, on top of the repository-wide `*.vfs` /
`*.inf` rules. The repository only ships **neutral format documentation**
(`Docs/RE/formats/*.md`) under the EU Software Directive 2009/24/EC Art. 6
interoperability exception.
