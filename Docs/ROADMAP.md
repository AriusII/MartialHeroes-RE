# ROADMAP — Live run record

Charter: `Docs/PLAN.md`. Branch: `major-campaign`. Status legend: ⬜ todo ·
🟦 in-progress · ✅ done · ⏸️ deferred · ❓ decision-needed.

---

# CYCLE 7 — GIGA DEEP STATIC-IDA CARTOGRAPHY & DOCS REFINEMENT ✅

**Parallel Docs/RE-only RE workstream** (no C#/Godot touched — does not disturb the
STRICT 1:1 re-arch cycle below). STATIC IDA only; ground truth `doida.exe` IDB SHA `263bd994`.
Full provenance: `Docs/RE/journal.md` (CYCLE 7 entry). Plan: the approved planning doc.

**Goal:** map every important/impactful element and refine/correct ALL of `Docs/RE`; aggressively
re-attack the ~59 "capture-pending" items statically; restore provenance.

- ✅ **Phase 0** — preflight: SHA gate (263bd994), census (25,792 fns), `_dirty/cycle7/` quarantine.
- ✅ **Phase 1 (W)** — 7 parallel research blocks A–G (+ 20 gap-audit sub-lanes), all RESOLVED;
  findings staged to `_dirty/cycle7/{A..G}`. Key binary-won reversals: scene `8`=terminal sentinel
  (not "0..8"); combat = 2/52 + **4/99·4/100 = Cube-Gamble, not combat**; buff = 5/31 (not 4/102);
  move emitter = 2/13 (not 2/112); quest record 4960B; **idle motion = actormotion col16, not col15**;
  citems = 10 paragraphs; VFS "VFS001" unvalidated + no index FILETIME; mobinfo.mi DEAD; ambient-×3 &
  cel-outline REFUTED; 3/23 = 28B by-name status; 2/60 = couple (not mail); mounts/auction/dungeons/
  housing/arena ABSENT. **Skinning DEBT #1 CLOSED** (full LBS deform chain pinned, engine Y-up).
- ✅ **Phase 2 (P)** — 7 promotion lanes; ~50 specs corrected + **9 NEW** (buffs, crafting, mail, pets,
  character_creation, skill_trees, pvp, quest_record, skn); opcodes.md reconciled. All firewall-clean.
- ✅ **Phase 3 (L)** — IDB legibility: 89 renames + 31 comments + 3 types; `sub_` 18,835→18,791.
- ✅ **Phase 4 (R)** — hard gate: clean-room firewall audit + cross-spec consistency → fix wave
  (3 raw addresses + 2 dangling `_dirty/` `spec:` fields + 81 provenance comments + 3 stale survivors
  all cleared). Re-grep clean.
- ✅ **Phase 5 (C)** — `journal.md` restored; `names.yaml` synced (3,352→3,393, SHA 263bd994); this record.
- ⏸️ **Follow-ups (owed, not blockers):** col15→col16 CLAUDE.md + C# re-flip (~4 sites) + IDB; HUD-II
  target-plate = MopGagePanel slot 35 / pet = slot 52; optional `4-100` YAML `git mv`; ~25 runtime-only
  residuals in the capture/debugger register (future `?ext=dbg`). See `journal.md` CYCLE 7 "Follow-ups owed".

**Gate:** STATIC-only; firewall PASS; cross-spec consistency PASS; zero C#/Godot files modified.
**Commit:** targeted `Docs/RE/**` only — maintainer-gated.

---

# CYCLE — STRICT 1:1 RECONSTRUCTION & C#/GODOT EXCELLENCE

**Goal:** purge all dev/offline/synthetic scaffolding, then re-architect the
whole C# + Godot solution to a professional, legible, performant bar — no tests.

**Ground-truth note:** tests already absent on disk (no `tests/`, no `/Tests/`
slnx folder). The "remove tests" mandate reduces to cleaning dangling refs.

## Phase 0 — Mandate & ground truth ✅
- ✅ 0.1 Reformulate mandate; lock the 3 decisions (delete tests / aggressive
  re-arch / build-nuke+headless+live gate).
- ✅ 0.2 Explore core (01–04), Godot (05), governance/specs.
- ✅ 0.3 Verify disk reality (no test projects; 13 projects; no PLAN/ROADMAP).
- ✅ 0.4 Write `PLAN.md` + this `ROADMAP.md`.

## Phase 1 — Adversarial offline/noise purge ✅ → `port-orchestrator`
**Doctrine:** reconstruct-not-validate. Delete dev/offline/synthetic; flag, don't
guess, on real-behaviour ambiguity. Keep build green after the wave.

**Result (✅):** build-nuke `dotnet build MartialHeroes.slnx` = **0 errors** (4×
`NU1903` SQLite NuGet advisory only). Headless spine **0→5 clean against the real
VFS** (43,347 entries; 90,937 items / 2,000 skills / 3,997 mobs / 52 zones). No
`SyntheticWorldFeeder`, no synthetic substitution, firewall clean. **Deleted:**
SyntheticWorldFeeder (+World.tscn node +GameLoop fallback), SknMeshBuilder
(NOT-SHIPPED), VisualActor cyan-capsule, RealWorldRenderer OFFLINE-DEMO branch,
RealClientAssets synthetic substitution, AreaComposer `mob.arr` offline spawn,
VfsCatalogueLoader items.csv/test-stub, ItemCatalogue CSV dev-builder,
ScrStatCatalogue fabricated stat-curve, 2 dangling `InternalsVisibleTo …Tests`.
**Kept (real, not scaffolding):** StubSceneController (live base of all 8 filled
controllers — Phase-2 *rename* only), ClientPathResolver, LiveLogin env knobs,
ClientContext live `Dispatcher`.

**Carry-over decisions → Phase 3 (decide by strict-1:1 = packed-VFS-only):**
- ⬜ `BgTextureCatalog.FromTxt` loose-tree `.txt` mirror → delete + de-wire 3
  layer-05 callers (`.lst` is the real packed source; spec `bgtexture_lst.md`).
- ⬜ `TerrainNode` solid-colour texture-null guard → delete (not spec-described).
- ⏸️ `AreaComposer.Spawns` channel vs server 4/4 actors → **RE question** (does the
  real client drive live actors from npc.arr or only 4/4?) → route `re-orchestrator`
  if/when it blocks; npc.arr IS a real client binary, so kept for now.
- ⏸️ `ScrStatCatalogue` 0-entry HP/MP curve = genuinely empty (fabrication removed,
  correct); real-curve wiring is a world-fidelity follow-up.

- ⬜ 1.1 **Dangling test residue** — remove every `InternalsVisibleTo …Tests`,
  any `.Tests` `ProjectReference`, and confirm no `/Tests/` slnx folder remains.
- ⬜ 1.2 **Godot synthetic/offline (layer 05)** — delete:
  - `Debug/SyntheticWorldFeeder.cs` (+ its `World.tscn` node + `GameLoop`
    fallback branch + `ClientContext` dispatcher-exposed-for-feeder if unused).
  - `Scene/Controllers/StubSceneController.cs` (no stub controllers remain).
  - `Dev/RealClientAssets.cs` synthetic-substitution (delete file if purely a
    synthetic stand-in; keep only genuine real-asset resolution).
  - `World/RealWorldRenderer.cs` "OFFLINE-DEMO" area-assembly branch.
  - `World/VisualActor.cs` placeholder cyan-capsule path.
  - `World/SknMeshBuilder.cs` "NOT SHIPPED" static fallback (if unused by the
    real avatar path).
  - **Verify vs spec (flag if ambiguous):** `TerrainNode` solid-colour fallback,
    `EnvironmentNode` neutral-grey, `CharCreatePreview3D`/`ClassAppearanceResolver`
    starter-mesh fallback, `NpcScrDescriptions` graceful-empty.
  - **Keep (not offline):** `Dev/ClientPathResolver.cs` (resolves the *real* VFS
    path) — but relocate out of `Dev/` to a neutral folder.
- ⬜ 1.3 **Core/Assets offline (layers 03–04)** — adjudicate & purge:
  `Assets.Mapping`/`AreaComposer`/`AssembledArea` synthetic fixtures + offline
  MOB `.arr` + "(offline path)" spawn branches; `VfsCatalogueLoader` dev-export
  / "test stub — no VFS" handling. Keep genuine runtime robustness only if the
  spec/IDA confirms the real client does it.
- ⬜ 1.4 **Whole-tree adversarial sweep** — grep `offline|synthetic|dev|fake|
  stub|mock|placeholder|demo|dummy|smoke|sample` across 01–05; adjudicate each
  hit → delete / keep-real / flag-RE. Produce a kill-list with per-hit rationale.
- ⬜ 1.5 **Gate** — build-nuke 0/0 · headless spine 0→5 · `/clean-room-check` ·
  `check_dag.py`.

## Phase 2 — Architecture design ✅ → `planning-orchestrator` (+ decision gate)

**Result (✅):** `Docs/ARCHITECTURE_TARGET.md` written + `plan-reviewer`-validated.
**14 → 34 projects** (proven clean maximum), DAG topologically sorted (acyclic,
downward-only), seams grounded in real dependency greps. Tier-1 accepted §8.2
(Application stays one project; `GamePacketHandler` → partial class), §8.4
(borderline mesh builders stay in 05), §8.5 (`journal.md` stays deleted).
**Open to user:** §8.1 project-count ceiling (34 vs trim) + §8.3 `Client.Presentation`
engine-free lib (extract to 04.x vs keep as 05 folder). Two execution gates noted:
rewrite hard-coded `check_dag.py` to the 34-graph (Wave 0, Tier-1) + verify the
source-gen router is non-empty after the `Network.Protocol` dissolve (Wave 1).

- ⬜ 2.1 Define the canonical taxonomy *Solution-folder → Project →
  pattern-folder → file* and the folder vocabulary (e.g. `Handlers/`, `Codecs/`,
  `Composition/`, `Rendering/`, `Hud/`, `Scenes/`, `Widgets/`).
- ❓ 2.2 **New-projects proposal (DECISION GATE — AskUserQuestion).** Candidate
  splits, e.g.: decompose the 161-file `Network.Protocol` by major/domain;
  extract `Client.Application.Handlers`; a `Client.Presentation.Core` engine-free
  view-model lib referenced by Godot. Present options + trade-offs; the user
  picks before any project is created.
- ⬜ 2.3 **God-class split map** (each → focused files in pattern folders):
  `RealWorldRenderer.cs` 2415 · `GamePacketHandler.cs` 1849 · `LoginWindow.cs`
  1583 · `CharSelectWindow.cs` 1416 · `ClientContext.cs` 1292 · `EffectRenderer.cs`
  1180 · `HudMaster.cs` 1171 · `NpcRenderer.cs` 1074 · `EnvironmentNode.cs` 931 ·
  `GameLoop.cs` 929 · `AudioService.cs` 919 · `CameraController.cs` 915.
- ⬜ 2.4 **Reference-graph cleanup map** — dead/duplicate `ProjectReference`s,
  package refs, and the layer-05→layer-02 documented exception.
- ⬜ 2.5 `plan-reviewer` validates the design; Tier-1 approves.

## Phase 3 — Re-architecture execution ✅ → `port-orchestrator`

**Wave log:**
- ✅ **Wave 0** (Tier-1): `check_dag.py` rewritten to the target graph (layer +
  sub-order model, transitional umbrellas, `.claude`/`.agents` skipped); validated.
- ✅ **Wave 1**: `Network.Protocol` → Core + Routing + Packets.{Login,World,Social}
  (source-gen rewritten to a symbol-walk → router **19 typed arms**, was empty);
  `Assets.Parsers` → Core + 8 families. **26 projects**, build-nuke **0 err**,
  generator builds clean (LSP CS0234 was stale), check_dag PASS, Godot OK.
  Multi-type "grab-bag" files relocated to consuming family (not via cross-edges).
- ✅ **Wave 2**: `Client.Domain` → 8 aggregates (zero forced merges/relocations;
  4 inter-aggregate edges all downward to Stats/Simulation). 32 core projects,
  build-nuke 0 err, check_dag PASS, Godot OK.
- ✅ **Wave 3**: `Application.Contracts` (13 files) extract + `GamePacketHandler` → 9
  partials. **Tier-1 certified**: nuke build 0 err, check_dag PASS (33), headless 0→6
  clean. `IApplicationUseCases` kept in Application (cycle-guard: pulls Domain.Inventory).
- ✅ **Wave 4**: `Client.Presentation` extracted (12 files; `ClientPathResolver` correctly
  stayed in 05 — hard `using Godot;`) + 11 god-classes → ~50 partials + `Dev/`→`Composition/`
  + `Screens/`→`Ui/Scenes/`. **Tier-1 certified**: nuke build 0 err, check_dag PASS (34
  core, acyclic), `Client.Presentation` Godot-free, headless 0→6 clean, firewall PASS.

**Phase 3 ✅ COMPLETE — 14 → 35 projects** (34 core + Godot), maximal decomposition,
DAG acyclic + downward, firewall clean, build-nuke 0 err, headless 0→6 clean.
Carry-overs to handle in Phase 4: BgTexture `.txt` loose-tree mirror delete (+de-wire 3
callers), TerrainNode solid-colour guard delete, ~9 over-400-line single-method partials
to decompose, unused `ProjectReference` prune, redundant-`using` cleanup.

- ⬜ 3.1 Create approved new projects; register in `.slnx`; wire downward-only
  refs; `check_dag.py` green.
- ⬜ 3.2 Execute god-class splits (one project/owner per lane; ledgered).
- ⬜ 3.3 Move files into pattern folders; align namespaces to folders.
- ⬜ 3.4 Delete dead code; prune dead/duplicate references.
- ⬜ 3.5 Gate per wave: build-nuke 0/0 · headless · firewall · DAG.

## Phase 4 — Deep code-quality & performance pass ✅ → `port-orchestrator`

**Result (✅):** `code-reviewer` **0 BLOCKER**; build-nuke **0 err**; headless 0→6 clean;
firewall PASS. Strict-1:1 carry-overs deleted (`BgTextureCatalog.FromTxt` + 3 callers
de-wired; `TerrainNode` solid-colour guard). Method-decompositions on the worst
single-method partials (EnvironmentNode/NpcRenderer/RealWorldRenderer/CharSelect).
Findings: CS8019 compiler-redundant-usings **0** (the reorg left none); **0** unused
`ProjectReference` (all generous edges proven consumed); 4 stale `SyntheticWorldFeeder`
comments removed. Residual: ~5 stale `.txt`→`.lst` doc-comment strings (cosmetic);
unified-namespace-vs-project decision (Phase 5).
- ⬜ 4.1 Per-project: zero-alloc hot paths, `Span`/`StructLayout` hygiene,
  primary ctors / collection expressions / nullability (C#14/.NET10).
- ⬜ 4.2 Comment quality — every magic constant cites `// spec:`; remove stale /
  noise comments; document non-obvious intent only.
- ⬜ 4.3 File-size discipline — no remaining single-responsibility violations.
- ⬜ 4.4 `code-reviewer` + `render-reviewer` full pass; resolve BLOCKERs.

## Phase 5 — Verification & consolidation ✅ → Tier-1

**Result (✅):** `check_dag.py` transitional umbrellas removed → **OK 34 core, acyclic,
0 warn**. Final Tier-1 certify: build-nuke **0 err**, headless **0→6** clean.
**Decisions (user):** namespaces **kept unified-per-domain** (no realignment — lower
risk, projects isolate dependencies, folders give the legibility); **NOT committed**
(left for inspection). 5 stale comments/log-strings scrubbed (`.txt`→`.lst`,
synthetic-feeder). **Flagged residual:** `BgTextureTxtParser` now has zero callers
(post-`FromTxt` deletion) → dead code unless retained for RE format-coverage — user to
decide. **Persistent stale LSP** (CS0234/CS0246 on Contracts/Presentation/event types):
the IDE/OmniSharp project model is out of sync after the reorg — **reload the
solution / restart the language server** to clear; the nuked `dotnet build` is truth.

---

# CAMPAIGN COMPLETE — STRICT 1:1 RECONSTRUCTION & C#/GODOT EXCELLENCE
All 5 phases done & Tier-1-certified. **14 → 35 projects**, maximal decomposition,
DAG acyclic + downward, engine-free below 05, clean-room firewall PASS, all dev/offline
purged, 12 god-classes → ~57 partials, build-nuke **0 err**, headless **0→6** clean.
**Uncommitted on `major-campaign`** per user.
- ⬜ 5.1 Final build-nuke 0/0 across `MartialHeroes.slnx`.
- ⬜ 5.2 Godot headless spine 0→5 clean; windowed screenshot sanity.
- ⬜ 5.3 Live login→char-select→enter-world vs replica (if up).
- ⬜ 5.4 `/clean-room-check` + `check_dag.py` PASS.
- ⬜ 5.5 Commit (targeted paths) — **only on explicit user request**.

## File-ownership ledger (one writer per path per wave)
| Wave | Owner | Path namespace |
|---|---|---|
| _(filled per wave at launch)_ | | |

---

# ADDENDUM — 00.SourcesGenerators + Tools (tooling/source-gen population)

User mandate: populate the two new solution folders with genuinely useful C#14/.NET10
artifacts — `00.SourcesGenerators` (Roslyn generators, best-practice) and `Tools`
(real CLI tooling moved out of `.claude/` skills) — **no noise, must help the now-split
projects**. Done & Tier-1-certified; **uncommitted** on `major-campaign`.

## 00.SourcesGenerators (2 generators)
- **Relocated** `MartialHeroes.Network.Protocol.Generators` (PacketRouter incremental
  source-gen) out of `02.Network.Layer/` → `00.SourcesGenerators/` (git mv; Routing
  analyzer ref repointed; check_dag layer 0).
- **NEW** `MartialHeroes.Shared.Kernel.Generators` — `[StronglyTypedId]` incremental
  generator (`ForAttributeWithMetadataName`, post-init attribute injection, value-equatable
  model, netstandard2.0 `IsExternalInit` polyfill). Wired as analyzer into `Shared.Kernel`;
  the 4 ids in `Ids/EntityIds.cs` migrated 1:1 to one-line `[StronglyTypedId] partial`
  declarations (the generator now emits `IComparable<T>` / `None` / `Prefix(Value)` ToString —
  the exact prior surface, RE provenance comments kept). De-risked by an isolated Shared.Kernel
  build **0/0**. Packet (de)serialization deliberately NOT generated — already covered by the
  committed `.g.cs` packet files (would be redundant noise).

## Tools (2 CLI tools)
- **`MartialHeroes.Tools.VfsExplorer`** (was `.claude/.../scripts/vfsls`) — first-class VFS
  browse/decode/extract/convert/coverage CLI on the production parsers. Promotion **fixed real
  breakage**: its csproj still referenced the pre-split monolith `Assets.Parsers.csproj` (gone)
  → re-pointed to the 9 `Assets.Parsers.*` families + Vfs + Mapping (relative refs). Promotion
  also surfaced **API drift** (`AnimationParser.Parse` → `AnimationClip?`) → null-guarded both
  call sites. Tool builds **0 warn / 0 err**; `coverage` = 41 formats; reads the real VFS.
- **`MartialHeroes.Tools.AssetChainTrace`** (was `.claude/.../scripts/chaintrace`) — index/existence
  chain tracer (Vfs-only, no payload). Runs: `exists data/map000/texture/bgtexture.lst` → OK 58660 B.
- **Left in place (NOT promoted):** the ~34 one-shot `pak-explore` probe harnesses — promoting
  them would be the "bruit inutile" the mandate forbids; they stay skill scaffolding.

## Wiring / skills
- `MartialHeroes.slnx`: generator moved 02→00, new generator + 2 tools registered.
- `check_dag.py`: layer-0 generators band + layer-6 Tools leaf band → **OK 37 projects**.
- `pak-explore` + `asset-chain-trace` SKILL.md: run-commands repointed to `Tools/…` via
  `${CLAUDE_PROJECT_DIR}`; "throwaway / never-in-slnx" doctrine rewritten for the 2 promoted
  tools (kept for the ~34 probes). `.agents/` is gitignored — untouched.

## Gate (all ✅)
build-nuke `MartialHeroes.slnx` **0 err** (only NU1903 SQLite advisory) · VfsExplorer
**0 warn/0 err** · `check_dag` **OK 37, downward-only, acyclic** · firewall **CLEAN** (committed
source) · Godot headless **Init(0)→Login(1)→sub-states clean, exit 0** · both tools run e2e.

## ADDENDUM wave 2 — exhaustive skills cleanup + 3rd tool (network)

User pushed for a REAL cleanup of the `.claude/` skills + more tools. Done & certified.

- **Skills cleanup:** verified the audit — only 2 reusable tools existed under `.claude/skills/`
  (already promoted), plus **35 one-shot probe harnesses** in `pak-explore/scripts/` that were
  (a) BROKEN (csproj referenced the pre-split `Assets.Parsers.csproj` monolith), (b) redundant
  (their routine capability lives in the VfsExplorer `scan-*`/`dump-*` subcommands; their findings
  are promoted into `Docs/RE/formats/*.md`), (c) spent one-shot RE scratch. **`git rm`'d all 35**;
  only `pak_index.py` (Mode A) remains. **Zero csproj remain under `.claude/`.** `pak-explore`
  SKILL.md rewritten: sibling-harnesses section deleted, all references repointed to the tool.
- **NEW tool `MartialHeroes.Tools.PacketInspect`** (network layer — completes the Tools coverage
  assets+network): `opcodes` lists all 46 `[PacketOpcode]`-tagged wire structs (reflected, never a
  hand-kept copy of opcodes.md) with direction + wire size; `decode <hex>` parses an 8-byte
  FrameHeader from a pasted/captured frame, identifies the opcode, validates the size, and dumps the
  struct's primitive fields. Verified: decoded a forged 3/7 frame → `SmsgCharManageResult [S2C]`,
  ReadyTime=0x12345678. Directly aids the live-networking campaign. Reflection is fine (tool, not hot
  path). slnx + check_dag updated.
- **Completeness audit:** only 2 source generators exist in the whole repo (both in 00.SourcesGenerators);
  the other skills' `scripts/` are `.py`/IDAPython skill machinery (not portable "tools") — left intact.

## Gate (all ✅) — final state
solution **39 projects** (34 core + 2 generators + 3 tools + Godot); nuke `slnx` build **0 err /
0 CS warn** (only NU1903 SQLite advisory); `check_dag` **OK 38** (downward-only, acyclic); firewall
**CLEAN**; Godot headless **Init(0)→Login(1) exit 0**; all 3 tools run e2e (VfsExplorer coverage=41
formats; AssetChainTrace reads the VFS index; PacketInspect catalogues 46 opcodes + decodes a frame).
