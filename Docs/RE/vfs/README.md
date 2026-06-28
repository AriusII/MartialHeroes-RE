# Legacy Client VFS Subsystem Dossier

This directory contains the complete reverse-engineering specifications and structural audits for the legacy *Martial Heroes* client's Virtual File System (VFS). 

The dossier serves as the clean-room reference guide for the `Assets.Vfs` and `Assets.Parsers` packages in the fresh implementation, outlining exactly how files are organized, indexed, streamed, and parsed.

## Dossier Index

1. **[VFS Master Manual](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/vfs/vfs_master_manual.md)**
   - Complete consolidated specification: container layouts, in-memory struct maps, C++ CVFSManager and DiskFile lifecycle and algorithms, sub-asset format registry, and global linkages.
2. **[Archive Container layout (`data.inf` & `data.vfs`)](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/vfs/archive_container.md)**
   - Header mapping (24 B, no FILETIME), 144-byte TOC stride incl. per-entry FILETIME triplet, and payload contiguity.
3. **[VFS I/O Subsystem Runtime Mechanics](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/vfs/io_subsystem.md)**
   - Analysis of `CVFSManager`, the mount sequence, lowercase binary-search lookup, the three-branch read primitive (slurp, streaming, and loose fallbacks), the `vfsmode` config, and load-progress tracking arithmetic.
4. **[Sub-Asset Census & Format Directory](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/vfs/census_and_formats.md)**
   - A classified index of the 49 file extensions found inside the reference archive, matching them to their purpose and parser specs.
5. **[Linkage Layer & Usage Map](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/vfs/linkage_and_usage.md)**
   - High-level view of how assets reference each other (mesh, animations, lightmaps, textures, localization) and who consumes them.


## Provenance and Anchor

- **Target Build:** `doida.exe` (SHA-256: `f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963`)
- **Verification Tier:** `sample-verified` (Facts corroborated by both control-flow disassembly and structural parsing of the 43,347-entry reference archive).
- **Rule Compliance:** Contains only clean-room neutral prose, layout offsets, and logic flows. No decompiler code, raw signatures, or proprietary logic.
