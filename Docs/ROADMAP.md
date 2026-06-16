# Docs/ROADMAP.md — Live Campaign Run Record

> The dated, in-place record of every campaign run. The **method** is `Docs/CAMPAIGN_TEMPLATE.md`;
> the **active charter** is `Docs/PLAN.md`. Update phase/block statuses **in place** as waves land.
> Prior cycles live in git history + `Docs/RE/journal.md`.

---

# CAMPAIGN 13 — Zero-Trust Ground-Truth Rebuild (*make the client work*) (launched 2026-06-16)

**The correction:** C11–C12 treated the existing C#/Godot as the baseline to patch and gated on
`build 0/0 + 1944 tests green` — a **circular** measure (the tests assert what the code already does;
C12 had to *rewrite* the chat/`DisplayFramerate`/`Flag`/`BillingState` tests because they froze wrong
behaviour). **C13 law: zero trust in the current C#, Godot render, and tests.** Rebuild as if from zero,
driven only by ground truth (**IDA** = source of truth · **C10-re-verified specs** · **official
captures** = visual oracle). **Success = the client runs end-to-end and behaves/looks like the
original — not "tests green".**

**Anti-circularity rule:** a subsystem is done only when its behaviour is **re-derived from an IDA
address or `spec:` citation** (the code is never evidence for itself); tests are re-derived from the
spec. Where code already matches ground truth it stays — now *verified*, not trusted.

**"Make it work" target (no live server → client-side fidelity):** (1) the scene spine actually flows
Boot→Login→PIN→ServerList→CharSelect→(Create)→World; (2) **character skinning/animation works** (the
headline broken thing); (3) world renders correctly vs specs+captures; (4) wire layer byte-exact vs
IDA; (5) client systems behave per spec.

**Scope — 8 priority lanes:** 1 scene-spine ★ · 2 skinning/anim ★ · 3 world-render ★ · 4 front-end-vs-
captures · 5 wire(proto/opcodes/crypto) · 6 asset/VFS/parsers · 7 HUD/UI · 8 domain/gameplay/tables.

**Method:** the `Workflow` tool drives the audit + rebuild fan-outs (Ultracode); clean-room firewall,
the downward-only DAG, zero-alloc discipline, and the build/test/headless/screenshot gates all hold.

**Out of scope:** the game server; live debugger/capture (facts stay flagged-pending); re-RE of settled
specs (unless an audit lane finds one *wrong*); blanket-naming the ~19k unnamed IDB functions.

## Phase 0 — Charter & honest baseline — **DONE 2026-06-16**
- [x] Continues on branch `campaign12` (last commit `b71060f`, tree clean at start).
- [x] IDA MCP UP on `doida.exe` `263bd994` (Hex-Rays ready) — the ground-truth source.
- [x] `Docs/PLAN.md` rewritten to the C13 charter (zero-trust law); this ROADMAP section prepended.
- [x] **Honest baseline observed** (nuke 48 bin/obj excl `.godot` → `dotnet build --no-incremental` +
  `dotnet test --no-build`): **0 warn / 0 err · 1944 tests green** across 12 suites. Recorded as the
  *diff target* — NOT asserted correct (this is exactly the circular gate C13 distrusts).
- [x] Scaffold `_dirty/campaign13/<lane>/` namespaces (8 lanes: scene-spine/skinning/world-render/
  frontend-scenes/wire/asset-vfs/hud-ui/domain-gameplay).
- [→] Windowed per-scene screenshot **folded into Phase 3 (V)** — captured there for direct compare to
  the official captures, rather than as throwaway before-pictures (the official captures are the oracle).

## Phase 1 — Ground-truth divergence audit (Workflow, read-only) — **DONE 2026-06-16**
`campaign13-divergence-audit` (`wf_28e4af66-0f4`): 8 parallel lanes, each re-derived required behaviour
from IDA (live reads, anchor 263bd994) + the clean spec and confronted the C#/Godot impl — the code was
NEVER treated as evidence for itself. **45 citation-backed divergences: 6 breaks-function · 27
wrong-fidelity · 12 cosmetic** (ledgers in `_dirty/campaign13/<lane>/divergences.md`).
- **6 BREAKS-FUNCTION (the "all-green-but-broken" the circular gate hid):** (1) char-CREATE preview
  force-disables skinning → static pose (the LBS math is CONFIRMED CORRECT vs IDA `0x437fb6`/`0x4387fb`;
  the "explodes" excuse was reasoning-from-render); (2) create-preview invents non-existent `.skn` for
  3/4 classes → only Musa renders; (3) terrain/building/water textures read the `bgtexture.txt` mirror
  ABSENT from a real packed VFS while the IDA-validated `BgtextureLstParser` (`0x4458bc`) is dead code →
  untextured world; (4) "UI is the gate" hit-test never wired → HUD clicks leak to world; (5) ChatWindow
  send path is a `GD.Print` stub → typed chat never sent; (6) quest accept-gate polarity INVERTED (passes
  ≥26, should pass <26) + missing billing bypass.
- **Notable VERIFIED-OK (zero-trust confirmation, not trust):** the **wire lane (5) = 0 divergences,
  byte-exact vs IDA** — cipher (`0x63e903`), 8-byte frame header, opcode dispatch (3/4·3/7·3/14 ladder),
  RSA handshake (`0x63ef05`) all confirmed; skinning LBS math confirmed; VFS container + parser corpus
  (two-witness corrections) confirmed. ~142 checks across the 8 lanes landed VERIFIED-OK.
- **Top wrong-fidelity clusters:** world-render invents brightening (sun energy-floor 1.6 + ×4 lum,
  Filmic tonemap@1.15, Additive glow — original applies color_A RAW, no tonemap, opaque present) + fog
  shape + effect all-Additive; skinning invented `DeriveStandUpBasis` + over-broad translation lock;
  scene-spine hosts server-list/PIN as top-level screens (original runs them INSIDE the Login scene,
  state 1) + a debug green-plane/red-cube in every world frame; progression XP (5/9, 5/11) unmodelled.
**W gate PASS** — every lane returned a citation-backed ledger; triaged into the Phase-2 rebuild waves.

## Phase 2 — Rebuild to ground truth (Workflow, staged) — **DONE 2026-06-16**
`campaign13-rebuild` (`wf_3e07b3c5-662`): rebuild each divergence to IDA/spec + rewrite tests from spec
(`// spec:` cite, never old behaviour). Disjoint-file lanes, Wave A (core contracts) → barrier → Wave B
(Godot presentation):
- **Wave A (3 lanes):** A1 Domain+Application (quest-gate polarity invert + bypass; new ProgressionState
  for 5/9·5/11 XP; InputEvent press/release/click taxonomy; handler doc) · A2 Infrastructure (runtime
  catalogue from items.scr not items.csv; ScrStat proxy→Empty) · A3 Assets (new `.lst`-backed
  `BgTextureCatalog` — the contract that fixes the untextured-world break).
- **Wave B (5 lanes, after A):** B1 skinning+char-3D-scenes (un-disable create skinning; unify class→mesh
  onto the §3.7.5 four meshes; remove `DeriveStandUpBasis`; interior-bone lock; re-enable carved backdrop)
  · B2 world-env (sun RAW, Linear tonemap, Screen glow, LINEAR fog, alpha effect blend, terrain clamp)
  · B3 world-textures (consume `BgTextureCatalog`/`.lst`-first; value<1→slot1) · B4 HUD+input (wire
  hit-test gate; wire chat send; inventory 318×732; single dispatcher) · B5 scene-spine (dev-gate the
  debug baseline; world-exit hook; re-home server-list/PIN inside Login, layouts preserved).
- SPEC GAPS flagged for promotion (no invention): full skin.txt appearance chain; skinning +84 node-scale
  source; effect per-drawable blend byte; uitex manifest rects; userlevel.scr HP/MP; .fx3/.fx5 water.
**E gate (Tier-1, after the waves) — PASS:** nuke + build `--no-incremental` **0 err / 0 warn**;
**1979 tests green** (was 1944; **+35 net new spec-derived tests** — progression XP, quest-polarity
rewrite, InputType taxonomy, items.scr fields, BgTextureCatalog). All 6 breaks + the fidelity cluster
rebuilt to IDA/spec; spec gaps flagged for promotion, **never invented**. **Tier-1 reconciliation:** the
two 3D char screens (lane-boundary residual) switched to the new `.lst` `BgTextureCatalog`
(`ResolveRelativePath`, 1-based, consistent with `RealWorldRenderer`); `ConnectionState`/`StatAllocationView`
doc nits fixed. Transient mid-wave cross-lane errors (`_world`, `CullForegroundOccluders`) resolved by
end-of-wave (final nuke build confirms). DAG downward-only held; every constant cites a spec.

## Phase 3 — Verify against the original — **DONE 2026-06-16 (verdict: functional PASS, 3 visual residuals)**
**Functional (headless) — PASS.** Boot smoke is clean end-to-end, **zero script errors**; the B5 re-home
is confirmed: *"Showing LoginScreen (owns the in-login PIN + server-list sub-views)"* (IDA-faithful
state-1 structure). World boot logs confirm the rebuild is **live**: `bgtexture pool loaded from
bgtexture.lst: 1222 slots` (break #3 fixed — binary `.lst`, not the absent `.txt`); `tonemap=Linear/1.0
glow=Screen`, `sunColorA=(0.4,…) energy=1.0 RAW` (B2 env fixes); `HudInputHandler.HitTest wired to
GameHud.HitTest` + `ChatWindow … SendChatRequested wired to UseCases` (breaks #4/#5 fixed); HUD renders
fully (panels, hotbar icons, minimap, gauges, inventory W=318).

**Visual (windowed screenshot oracle) — the doctrine working: it caught 3 residuals the build/tests/logs
could not.** Captures: `%TEMP%/mh-c13-{create,charselect,world}.png`.
1. **World too dark** (pre-existing debt #3, now isolated): geometry IS present (terrain ring + 92-obj
   BUD + char spawned) but the 3D world renders near-black. `ApplyAmbient` correctly sets white ambient
   energy 1.0, but the world uses **cel-shaded** materials (`CelShade.gdshader`) whose lighting keys off
   the (spec-correctly) dimmed directional and does **not** pick up the OPTION_BRIGHT ambient floor →
   B2's spec-correct directional-raw exposed the cel/ambient gap. Needs `godot-shader-specialist` +
   windowed iteration (`.gdshader` is invisible to `dotnet build`). spec: environment.md §6.2a/§6.2b.
2. **Create-preview framing**: the char now **renders** + the carved-wall backdrop is back (B1 fixed the
   force-static + invisible-classes breaks), but the camera frames only the **boots/feet** — B1 removed
   the boom per the IDA "actor-only" finding but the actor placement frames the feet (the C12
   "legs-only" geometry returns). Needs a framing pass (IDA create-offset `anchor+(-1536.5,0,-3538.0)`
   or restore figure framing) + windowed iteration. spec: frontend_scenes.md §4.2/§3.5.4.
3. **Skinning idle animation flat**: chars **render** (not exploded — INV1 rest + INV3 AABB PASS), but
   3-frame human idle clips report `INV2 liveDelta=0` (static) while multi-frame mob clips (36f/121f)
   animate (`liveDelta>0 PASS`). Either the human idle is genuinely near-static or the probe samples a
   static vertex — needs `godot-skinning-specialist` to confirm/fix. Pre-existing skinning-anim debt.

**∥ Phase-D IDB legibility + `names.yaml` pull:** NOT YET RUN (deferred to P4/follow-up).
**V gate:** functional + wire + VFS-texture + HUD = PASS; visual fidelity = 3 open residuals (above),
each routed to its specialist with the screenshot evidence. char-select ≈ oracle (dark temple, braziers,
water, 3 chars present).

### Phase 3b — visual-residual fix wave (`wf_3438313c-eb9`) + re-capture — **PARTIAL (build-clean, but visuals NOT resolved)**
`campaign13-visual-residuals`: 2 specialist lanes landed **spec-correct, build-clean (0/0), no-regression**
changes — but the **windowed re-capture (the oracle) confirms the 3 residuals are NOT visually fixed**.
Honest outcome (oracle > spec; the changes are correct partial steps, kept, not reverted):
- **World (R1):** added an explicit `ambient_floor` uniform to `CelShade.gdshader` + wired it from
  OPTION_BRIGHT (the shader runs `unshaded` so it never auto-received WorldEnvironment ambient). Correct
  per environment.md §6.2a. **But** the re-capture (`mh-c13-world2.png`) is still near-black: the world's
  **terrain (vertex-colour unshaded) + buildings (PBR) are NOT cel-shaded**, so the bulk of the world was
  never the cel path. Root cause of the black world is elsewhere — terrain material/texture binding and/or
  the night star-dome at noon (`stardome=1` @ keyframe 24) and/or the `boot_flow=world` camera (`State:
  Login, Actors: 0`). This is the pre-existing **debt #3**, now narrowed (NOT cel) — needs a dedicated
  world-render investigation with the **official area-2 capture** as the target.
- **Create framing (R2):** added `AimCameraAtActorCentre` (measures the figure AABB, aims the held KF1
  camera). **But** `mh-c13-create2.png` is unchanged — still only the **boots**: at ~30u camera distance,
  scale 3.471 makes the figure too large to frame, and aiming alone can't fit it. Needs scale/distance
  tuning iterated against the **official create capture** (full-figure vs bust is itself unconfirmed).
- **Skinning idle (R2):** the all-vertex probe rewrite is a genuine diagnostic upgrade and **confirms** the
  3-frame human idle clips produce **zero displacement across ALL vertices** (`liveDelta=0` everywhere),
  while 36f/121f mob clips animate (`1.51`/`0.67`). So chars **render correctly (not exploded)** but the
  human idle is **genuinely flat** — the long-standing skinning-*animation* debt (truly-static idle data or
  deeper RE), not a regression.
**Conclusion (superseded by P3c):** initially framed as needing the official captures — but the maintainer
corrected the doctrine: **IDA is the absolute truth**; recover the exact behaviour from `doida.exe` and fix
the C# to it (captures only confirm rendered pixels). Pivoted to IDA recovery (P3c).

### Phase 3c — IDA-driven residual recovery (`wf_9608b6e9-804`) + fixes — **PARTIAL (1 real fix landed; 2 root-caused deeper)**
3 analysts read `doida.exe` directly (every fact address-cited; notes in `_dirty/campaign13/residuals/`):
- **World texture indexing — FIXED (real off-by-one, IDA-decisive).** IDA (`0x445833`/`0x44a46d`/store
  `0x44b267`): the bgtexture pool is indexed by `intTexId` **DIRECTLY (0-based, NO −1)**; the only `−1` is on
  the `.ted` byte (`Ted_ResolvePatchTextures 0x44b296`: clamp `[1,count]` → `perCellTexList[byte−1]`). The C#
  `BgTextureCatalog.ResolveRelativePath` did `intTexId−1` → `intTexId=0` cells resolved to slot −1 → null →
  missing textures (would break a real packed VFS). **`bgtexture_lst.md §Cross-file join` was WRONG** (it said
  "1-based minus 1", contradicting the correct `terrain.md §3.5/§5.6`). Fixed: catalog now direct 0-based;
  `RealWorldRenderer`/char-screens/water consume it directly; `.ted` byte clamp `[1,count]→1`; spec corrected
  with IDA addresses; **`BgTextureCatalogTests` rewritten to direct-0-based (135 green)**. Build **0/0**.
  *(Note: a windowed world capture is STILL near-black — the texture fix is correct + necessary but NOT the
  black-world cause; the black is lighting/camera, see below.)*
- **Create actor X — FIXED.** Anchor `(2048,0,−6144)` (`0x54824a`) → create actor world `(511.5, 0, −9682)`
  (`0x545e1e`); C# used X=508.5 (the look-at pivot). Corrected to 511.5. Scale 81/70/50 ratio confirmed
  (`0x545e3e`/`0x548555`). *(Does NOT cure the "boots" — that's the camera, below.)*
- **DEEPER ROOT CAUSES (named with addresses, NOT yet fixed — next IDA recovery):**
  - **World black = LIGHTING/camera, not texturing.** IDA: the world is *config-lit* via `DISPLAY_BASE` /
    `GLOW_BRIGHT_MULTI` brightness scalars (`0x72e234`/`0x72e218`) + sun color (`0x721224`) + OPTION_BRIGHT
    ambient — B2 stripped the "invented brightening" that approximated these without recovering the real
    scalars. Need to recover + apply them (and confirm the `boot_flow=world` camera frames the town —
    HUD shows `State: Login, Actors: 0`).
  - **Create "boots" = the boom-rig eye.** `ApplyClassSelection` sets NO camera; the eye `(512,87,−9652)` is
    NOT a binary literal (`find_bytes`=0) — it's computed each frame by a **boom rig** (`this+6204`, vtable+64,
    `GPerspectiveCamera 0x60B917`). Our static eye is a guess; need the boom-rig algorithm.
  - **Idle flat = data check pending.** IDA (`0x40d709`/`0x4029a5`/`0x41ede0`): the original animates a
    displayed human EVERY frame (loop = `fmod(t, frame_count*0.1s)`, `floor(t*10)`). So the flat idle is either
    a port loop/advance bug OR genuinely-identical idle `.mot` frames — decisive test = parse a real human idle
    `g{id}.mot` and diff its keyframes (not in the binary; data-pending).
**Net:** 1 real correctness fix landed (texture off-by-one + spec) + create-X; build **0/0 · tests green**; the
3 *visible* symptoms have IDA-named deeper causes queued for the next recovery. No regressions.

## Phase 4 — Review + hard gates + consolidate — **PENDING**
Parallel read-only reviewers → fix wave → hard gates (build 0/0, spec-conformance suites green, firewall
PASS, functional+visual checklist). ROADMAP in place, journal entry, stage `names.yaml`, update memory.
**Commit only on explicit maintainer request**, targeted paths.

---

# CAMPAIGN 12 — C#/Godot Fidelity Completion ("everything possible") (launched 2026-06-16)

**Mandate (maintainer):** "Continue the C11 direction — finish everything still possible on the C# (core
01–04) AND Godot (05). Base it on what the **official game client** does (+ the IDA comprehension, the
source of truth, + the C10-re-verified specs); **query IDA when unsure**. (1) Delete useless/wrong
elements; (2) improve/correct/optimise to the cleanest + TRUEST vs IDA/spec; (3) deploy lots of agents +
all needed skills; (4) **rewrite PLAN + ROADMAP** to set this direction. Make the C# the cleanest, most
excellent, optimised and functional possible."

**North stars:** N1 = total clean-room RE of `doida.exe` (DONE through C10; IDA stays queryable for
confirmation only). **N2 (active driver)** = the faithful 1:1 port (core + Godot) must match the
re-verified specs **and the official client's observable behaviour** exactly — clean-room-pure, zero-alloc
on hot paths, idiomatic C#14/.NET10, no cruft.

**Relationship to C10/C11:** C10 made the specs the truth; C11 ran a broad core-weighted audit→fix→gate
(1944 tests green) but left non-blocking follow-ups and only **headless-smoke** verified the front-end.
**C12 = the completion pass**: close the follow-ups, give Godot 05 the deep per-scene fidelity treatment,
and **prove fidelity with the screenshot oracle** (campaign-9 lesson: spec-faithful ≠ pixel-faithful).

**Method:** the `Workflow` tool drives the discovery + fix fan-outs (Ultracode). Clean-room firewall, the
downward-only DAG, zero-alloc discipline, and the build/test/headless/screenshot gates all hold throughout.

**Scope — 5 lane groups:** **V** visual fidelity (screenshot oracle) ★ · **F** deep C#/Godot fidelity
(Godot-weighted) ★ · **C** cleanup/cruft · **W** wire/data paths (kill DEV-seed-only) · **R** RE legibility
(`names.yaml` sync, IDB-only, parallel).

**Out of scope:** the game server; live debugger/capture (debugger-pending facts stay pending, incl.
`3/14`-vs-`4/1` spawn ordering); re-RE of settled specs; blanket-naming the ~19k unnamed IDB functions.

## Phase 0 — Charter & pre-flight — **DONE 2026-06-16**
- [x] On `master` after the campaign3→master merge (PR #1 `970e0a7`, + formatting `6cf31c5`); tree clean.
- [x] Branched **`campaign12`** off master (no work on the default branch).
- [x] IDA MCP UP on `doida.exe` `263bd994` (Hex-Rays ready) — queryable for confirmation.
- [x] `Docs/PLAN.md` rewritten to the C12 charter; this ROADMAP section prepended; C11 Phase 5 closed.
- [ ] Baseline gate re-confirm (trust C11's 1944-green; cheap re-confirm before the fix waves land).

## Phase 1 — Discovery audit (Workflow, read-only, massively parallel) — **DONE 2026-06-16**
`campaign12-discovery` (`wf_60ff8fa3-311`): 15 lanes (7 Godot-weighted + 5 core + 3 cross-cutting) →
**106 findings** (11 high / 29 medium / 66 low). By category: fidelity 44, delete 18, optimize 14, bug 10,
cleanroom 10, wire 9, test-gap 1. Triaged by owning area into a fix-lane worklist. Tier-1 RESOLVED the
load-bearing `actormotion` idle off-by-one (4 C# sites + 2 docs read `cols[16]`; the IDB-confirmed format
spec says **col15 = motion_ids_a[0] @ +0x40** — `cols[16]→cols[15]`).

## Phase 2 — Fix waves (Workflow, one writer per area) — **DONE 2026-06-16**
`campaign12-fix` (`wf_b2ecb46b-aec`): 7 disjoint-file lanes → **50 applied / 7 skipped / 4 deferred**.
Headlines: GameHud invented ZoneIndicator pill DELETED (+ dead Unknown chrome); `ServerListDrainer.cs`
NEW wire→view adapter (ServerListReceivedEvent → ServerSelectScreen); chat everyday channels → 2/7 (was
3/21) + CP949 body/name + ChatRouting.Validate; LuaConfigRecord CP949 doc + DISPLAY_FRAMERATE→ShowFpsCounter;
EnvironmentNode per-frame fog scalar (s×3.0); glow→Additive; World.tscn procedural-sky removed; CelShade
post-process gate; effect placeholder removed; CmsgSelectCharacter→manage+delete Mode; outbound single-rental
+ pooled keepalive header; InputRouter modifier bitmask (Alt=0x8); idle `cols[15]` ×3; LocalAppPaths deleted.
**Tier-1 reconciliation** (test/source coordinated, the lanes couldn't cross files): rewrote the chat test to
2/7; `DisplayFramerate→ShowFpsCounter`; `CmsgSelectCharacter.Flag→Mode`; `BillingState→BillingFlag` (field +
GamePacketHandler + PacketRouterTests); removed the new CS0649 (dead `LiveEffect.Particles`). Docs (Tier-1):
`frontend_scenes.md §3.3.4` idle col15 + `CLAUDE.md` skeleton `g{id_b}.bnd` disambiguation (+ removed a
`_dirty/` citation leak in CLAUDE.md). Deferred (noted): `LobbyServerRecord.ServerId` ushort→i16 (wide
ripple, runtime-neutral); `FrameSplitter` zero-alloc (primitive `DecompressPayloadInto` ready — needs a
scratch-lifetime decision + perf-reviewer); SoundTable `>=`/Bgtexture reject-0 (kept strict — files are
always exact size); slot-lock yaw-π (no lock model).

## Phase 3 — Screenshot fidelity loop (Tier-1 Godot windowed) — **DONE 2026-06-16**
Windowed capture of login / server / charselect / create vs the maintainer-verified oracle `mh-cs2-final`.
**Caught a real C12 regression the C# build cannot see:** L3's CelShade post-process gate used `return` in
`fragment()` (illegal in Godot shaders) → `SHADER ERROR` → all cel-shaded chars failed; fixed (select, not
early-return). **Reverted 2 L2 spec-driven visual regressions** (oracle > spec, the campaign-9 doctrine):
the free-look Euler camera (−32.67° looked at the ground, not the row; LookAt's −9° matches the oracle + KF
geometry) → reverted `CharSelectCameraRig` to LookAt; and the create-preview boom removal (framed only the
legs) → reverted `CharCreatePreview3D` to the campaign-9d boom framing (re-applied only the `cols[15]` idle
fix). KEPT L3's horizontal water plane (reads as the temple-over-water and is more §3.6.5-correct than the
old vertical curtain). Re-captured: charselect ≈ oracle (dark temple, braziers, 3 chars, water); create
framing fixed. FLAGGED (pre-existing, NOT a C12 regression): the create close-up magnifies the known
skinning static-pose/distortion debt → godot-skinning-specialist follow-up. Headless smoke clean; autoload +
`client_dir.cfg` restored byte-exact.

## Phase 4 — RE legibility: names.yaml pull (IDB-only) — **DONE 2026-06-16**
`ida-naming-sync` pull (SHA match `263bd994`): the IDB carries **6981** analyst-named symbols (3585 funcs +
3396 globals) absent from the 3343-entry glossary (campaigns 8–11 annotated far more than was ever
re-synced). Staged to `Docs/RE/_dirty/names-pulled-263bd994.yaml` (gitignored) for **maintainer hand-merge**
— NOT auto-merged into `names.yaml` (orchestrator-owned; the skill stages for review). No IDB writes.

## Phase 5 — Hard gates + consolidate + commit — **DONE 2026-06-16**
Authoritative nuke + `--no-incremental`: build **0 error / 0 warning** (improved from the baseline's 1
pre-existing CS8600 — both it and the transient CS0649 cleared) · **1944 tests green** (12 suites, 0 failed).
DAG PASS; firewall clean; headless boot clean; screenshots are the visual evidence. ROADMAP + memory updated;
committed targeted paths on `campaign12`.

---

# CAMPAIGN 11 — C# Excellence & Fidelity (core layers 01–04 + Godot 05) (launched 2026-06-16)

**Mandate (maintainer):** "With all the IDA Pro comprehension (the source of truth) and the now
re-verified `Docs/` SPEC behind us, focus on the C# — both the core projects AND Godot. (1) Delete
useless elements that should not be there. (2) Improve, correct, optimise the code so it is the
cleanest and TRUEST possible vs IDA / the spec. (3) Deploy lots of agents — use all needed agents and
skills. (4) Improve the plan/roadmap/campaign as needed. Make the C# part the cleanest, most
excellent, most optimised and functional possible."

**North stars:** N1 = total clean-room RE of `doida.exe` (DONE through C10 — specs are the truth);
**N2 = the faithful 1:1 re-implementation is now the focus** — the C# (core + Godot) must match the
re-verified specs exactly, be clean-room-pure, zero-alloc on hot paths, idiomatic C#14/.NET10, and
carry no cruft.

**Method:** the `Workflow` tool drives every phase as a massively-parallel fan-out (Ultracode).
Audit → adversarially verify → fix (one writer per project) → hard gates. Clean-room firewall, the
downward-only DAG, zero-alloc discipline, and the build/test/headless gates all hold throughout.

**Out of scope:** the game server; re-RE of already-verified specs (read them, do not re-derive);
live debugger/capture (flagged-pending facts stay pending).

## Phase 0 — Baseline & charter — **DONE 2026-06-16**
- [x] Authoritative gate: build **0 err / 1 pre-existing warn** (`RealWorldRenderer.cs:1058` CS8600) ·
  **1859 tests green** (10 suites).
- [x] Baseline commit **`b236830`** (banks campaign3 front-end WIP + C10 Stage-C BootFlow fix + kit/doc
  updates) → clean tree for the C# pass. Local `client_dir.cfg` left unstaged.
- [x] CAMPAIGN 11 charter recorded (this section).

## Phase 1 — Audit (Workflow, read-only, massively parallel) — **DONE**
`campaign11-csharp-audit` (`wf_4551aa3c-544`): 20 lanes → **170 findings** (2 critical + 23 high + 39
medium + 106 low). By category: fidelity 52, delete 42, test-gap 27, cleanroom 14, optimize 13, arch 9,
bug 7, modernize 6. Triaged + grouped by owning project into per-project briefs.

## Phase 2 — Verify & prioritise — **DONE (folded into the fix lanes)**
Verification embedded in each fix lane (adversarial re-confront-to-spec before editing). Baked Tier-1
decisions: VFS keep-mmap (port choice) + fix the real leak; defer the cross-project arch DAG refactor to
3b; defer the 3/14-vs-4/1 spawn ordering (debugger-pending). 33 findings rightly skipped (false positives,
test-infra needing a new csproj, dev-tool paths, debugger-pending) — all logged.

## Phase 3 — Fix (Workflow, one writer per project) — **DONE**
`campaign11-csharp-fix-3a` (`wf_e8f23be9-ea4`, 15 lanes) — **114 fixes applied**, 33 skipped, 13
cross-project ripples flagged. `godot-world-fx` re-run (effect diffuse tint, velocity Z-negate, demo-noise
delete). Reconciliation `campaign11-reconcile` (`wf_81832c4b-235`, 3 lanes): LobbyServerRecord re-model
per lobby.yaml, ZoneType.Unknown→Safe, CharacterClass renumber consumers. **Committed `707ce31`.**
### Phase 3b — arch DAG (maintainer: accept + document) — **DONE**
Removed the spurious `Diagnostics→Kernel` ref; ACCEPTED the 3 by-design downward edges
(`Application→Protocol/Crypto`, `Infrastructure→Parsers/Vfs`, `Godot→Infrastructure`) and documented them
in CLAUDE.md + `check_dag.py` INTENDED. `check_dag.py` now PASSES (24 core projects, acyclic, downward-only).

## Phase 4 — Hard gates — **DONE (GREEN)**
Authoritative nuke + `--no-incremental` build **0 err / 1 pre-existing warn**; **1944 tests green**, 0
failed, 0 skipped (was 1859; +2 new suites Network.Abstractions/Shared.Diagnostics, +85 tests). DAG PASS.
Firewall: the two committed `_dirty/` citations removed. (Headless/screenshot Godot re-verify = follow-up.)

## Phase 5 — Consolidate & commit — **DONE 2026-06-16**
Three milestones committed on campaign3: `b236830` (baseline) → `707ce31` (fix + reconcile, 135 files) →
`8055b52` (arch). Then campaign3 was merged to `master` via PR #1 (`970e0a7`), with a formatting-only
commit `6cf31c5` riding along. Memory `campaign11-csharp-excellence.md` written. **Follow-ups handed to
CAMPAIGN 12** (see below): names.yaml sync, GameHud "Unknown" dead pill, ServerSelect wire-adapter, the
stale CLAUDE.md skeleton claim, and the never-run **screenshot** fidelity verification.

---

# CAMPAIGN 10 — Total Client Comprehension & Doc Re-Verification (`doida.exe`) (launched 2026-06-16)

**Mandate (maintainer):** "Deploy lots of agents to deep-analyze the whole `doida.exe` client —
how it is constructed and boots, what it does, the management of functions/modules/scopes, every
scene (= window), with ultra-precise attention to the UI/UX (GUI) window construction, plus a deep
refinement of the `data.vfs` pipeline. Don't trust the current docs — IDA Pro 9.3 (MCP) is the source
of truth. Re-verify and rewrite the entire `Docs/RE/` to 100% certainty; then align the code."

**Master deliverable:** `Docs/RE/specs/client_architecture.md` — the top-level synthesis (entry →
init scopes → `GameState` scene machine → window framework → VFS/resource pipeline → frame loop),
cross-linking every re-verified subject spec.

**Out of scope (deferred):** the game server; live debugger / capture confirmation; blanket-naming
all ~19k unnamed functions.

**Command structure:** Tier-1 (main session) drives phase sequencing + gates + the Tier-1 serialized
files. Tier-2 `re-cleanroom-orchestrator` (per-block dirty→spec RE) and `re-annotation-orchestrator`
(Phase-D IDB writes). Research/engineering fan-outs run via the `Workflow` tool over the §13 fleet.

## Evidence baseline (Phase 0)
- **IDB:** `doida.exe` SHA `263bd994c927c20a38624cf0ca452eaef365057fa9db1543d8f668c14a6fd8ee`;
  imagebase `0x400000`; image `0x64d000`; **25,792 functions** (4,801 named / 1,901 lib /
  19,090 unnamed); entry `start` @ `0x66959c`; 4,800 strings. `names.yaml` pinned to this build.
- **Tool baseline (2026-06-16):** IDA MCP **UP** · build **0 err / 1 pre-existing warn**
  (`RealWorldRenderer.cs:1058` CS8600, Godot, not from this campaign) · tests **1855 green / 0 fail /
  0 skip** (10 suites) · VFS reachable.
- **Corpus to re-verify:** 37 `specs/` + 32 `formats/` + 10 `structs/` + ~80 `packets/` + `opcodes.md`.

## The "100% sure" gate — verification banner (added to every touched doc)
`verification: confirmed | sample-verified | static-hypothesis | capture/debugger-pending` +
`ida_reverified` + `ida_anchor: 263bd994` + `evidence` + `conflicts`. Phase-R fails any touched doc
without it. (Schema in `Docs/PLAN.md`.)

---

## Phase 0 — Mandate & Pre-flight — **DONE 2026-06-16**
- [x] `Docs/PLAN.md` (charter) + `Docs/ROADMAP.md` (this record) created.
- [x] Baseline captured: build 0/1(pre-existing) · 1855 tests green · IDA MCP UP.
- [x] `_dirty/campaign10/{A..G}/` + `glossary.yaml` scaffolded.
- Pre-existing baseline note: `RealWorldRenderer.cs:1058` CS8600 warning (carry; fix opportunistically in E).

---

## Phase W — Giga-research (dirty room, blocks A→G) — **IN PROGRESS**
Massively-parallel static-IDA + VFS lanes. One analyst lane per doc → `_dirty/campaign10/<block>/<lane>.md`.
Ledger: one writer per `_dirty` path. Each lane re-confronts the current doc to the IDB: marks every
claim/constant/offset confirmed / sample-verified / static-hypothesis / capture-pending; raises
`CONFLICT:` on any disagreement; surfaces what the doc MISSED.

### Block A — Boot & Runtime Construction ★ — **W+P DONE 2026-06-16 (firewall PASS); D deferred(batched)**
| # | Lane (re-verify) | Type | Agent | Deliverable | Status |
|---|---|---|---|---|---|
| A0 | `WinMain` entry + scene machine + init tiers (the spine) | IDA-S | re-static-analyst | `_dirty/campaign10/A/winmain_state_machine.md` | ✓ |
| A1 | `specs/client_runtime.md` (boot + runtime engine behaviour) | IDA-S | re-static-analyst | `_dirty/campaign10/A/client_runtime.md` | ✓ |
| A2 | `specs/game_loop.md` (per-frame ordering + timing) | IDA-S | re-static-analyst | `_dirty/campaign10/A/game_loop.md` | ✓ |
| A3 | `specs/intro_sequence.md` (boot/logo/intro sequence) | IDA-S | re-static-analyst | `_dirty/campaign10/A/intro_sequence.md` | ✓ |
| A4 | `specs/client_workflow.md` (scene transitions end-to-end) | IDA-S | re-static-analyst | `_dirty/campaign10/A/client_workflow.md` | ✓ |
| A5 | `specs/resource_pipeline.md` (boot-load worker / loading screen, init side) | IDA-S | re-static-analyst | `_dirty/campaign10/A/resource_pipeline.md` | ✓ |
| A6 | `structs/runtime_singletons.md` (global singletons / service slots / scopes) | IDA-S | re-struct-cartographer | `_dirty/campaign10/A/runtime_singletons.md` | ✓ |

**A — reconciled findings (the docs were ~90% accurate but had real, load-bearing errors):**
- **Scene machine = exactly 8 cases, GameState 0..7** (`WinMain` switch `cmp eax,7; ja default; jmp jpt[eax*4]`) — the docs' "0..8" is WRONG; the value **8 is a sub-state** (`GameState+4`), not a 9th case. `[confirmed]` (Tier-1 disasm of `0x5fe34a`).
- **Frame loop is software-capped at a FIXED 60 FPS** (QPC limiter; engine ctor seeds the rate field = `60.0f`, never overwritten) — the docs say "uncapped." `DISPLAY_FRAMERATE` has **no consumer reaching the throttle** (inert, static). Loop has **4 phases not 3**. (A1+A2 concur.)
- **VFS:** `data.inf` 24-byte header, **entry_count at header +0xC (+12)** not +8; opened **RANDOM_ACCESS** not SEQUENTIAL_SCAN. (A0+A6 concur; byte-witness deferred to Block C.)
- **Login sub-state 31 = PIN/second-password modal, NOT EULA**; keepalive = **C2S 2/112** not 2/10000; login credential = tab-separated **KEY-string secure-context** (account/password/PIN/host port) not literal 2/1; **no 4/3 BillingInfo** (4/1 is two-form). (A4 — wire-level items capture-pending → Block E.)
- **Intro:** Opening = **GameState 3**; alpha inits **250** (fade-out first) applied via **D3D TEXTUREFACTOR RS60** (not per-vertex); skip button **top-right**; second mouse-scrub path.
- **Singletons:** MainWindow (1464 B, 223 slots) is a **separate object** from the 16-B MainHandler hub — the doc conflates them; VFS state = **4 globals + 3-word progress block** (not 3 flat); undocumented **AppService** singleton (136 B); MainWindow **+0xBC** secondary vtable.
- **Resource:** boot worker INSTALLED in LoadHandler ctor but STARTED in the loading-window sub-init (ABOVE_NORMAL); progress is an **integer quotient → near-static bar**, completion driven by the thread-flag; OPENNING/SKIP resolved (`GetPrivateProfileIntA`).
- ~80 name proposals captured → `_dirty/campaign10/glossary.yaml` (block A). Notable rename: `0x5fe063` (mis-named `Diamond_OpeningWindow_ctor`) is the **window-manager registration / cleanup-push helper**; the real Opening ctor is `0x54581a`.
- **P (promotion): DONE** — 6 Block-A specs (`client_runtime`, `game_loop`, `intro_sequence`, `client_workflow`, `resource_pipeline`, `structs/runtime_singletons`) rewritten to 100% + verification banners; **Tier-1 firewall scan PASS** (no decompiler artifacts / image-range addresses leaked). Phase-D IDB annotation for A's cluster deferred to the batched annotation wave.

### Block B — Scene/Window State Machine & UI Framework ★ — **W+P DONE 2026-06-16 (firewall PASS); D deferred(batched)**
Covered `specs/ui_system`, `ui_hud_layout`, `input_ui`, `frontend_scenes`, `login`;
`structs/guwindow`, `gucomponent`; `formats/ui_manifests` + 3 deep `construct()` element walks
(LoginWindow 73 widgets, SelectWindow 279 elements, MainMaster HUD 178 slots).

**B — reconciled findings (the UI docs had real, load-bearing errors):**
- **GUComponent geometry was TRANSPOSED** (SEVERE): correct is **+0x1C=width, +0x20=height, +0x24=posX, +0x28=posY** (+0x14/+0x18 local, +0x2C/+0x30 world, +0x44 64B transform matrix, +0x0C tint+forced-alpha@+0x0F, +0x84 parent). **No sized ctor exists** (only a default zero-init; geometry via setters). Auto-hide timer pinned (+0x95/+0x98/+0x9C=3000ms/+0xA0). — would have mis-laid-out every Godot widget.
- **UI event taxonomy was WRONG**: full = **1=key-down, 2=key-up, 3=move, 4=press, 5=release, 6=click(synth, same-widget = click-vs-drag), 7=dbl-click, 8=wheel** (doc had {3,5,7,8}, mislabeled 5 as press). **DirectInput8 is the KEYBOARD path** (doc inverted it). Wheel delta at record +4. Recovered all prior-UNVERIFIED constants (dbl-click 300ms/2px, modifier bits).
- **UI layout is CODE-BAKED** — NO on-disk layout manifest. Each window's `BuildScene` (vtable slot 14 / +56) builds children with integer-literal coords via `Build*(tex,dstX,dstY,w,h,srcX,srcY,color)` (1:1 src/dst). Registries (uitex.txt) map id→path only.
- **NO EULA panel** (B9 construct walk supersedes a B7 inference): the msg ids 4001–4022 are the **server-list/channel row captions**.
- **Cross-block CONFLICT resolved (Tier-1):** A3's "login=4" REFUTED → **Login=GameState 1, Opening=3, char-select=4**; the Opening is **post-login** (login→load→opening→char-select→in-game). `intro_sequence.md` corrected accordingly.
- **MainMaster HUD = 3 routines** (docs blurred into 1): ctor (vtables+zeroed 223 slots, builds nothing) / `BuildAndRegisterPanels` (178 slots) / per-GameState reconfig (text/sound, no rects). Inventory **W=318 not 732**. ~150 named panel ctors de-anonymized.
- **`0x5fe063`** finally pinned: **SceneDisposeList_Push** (std::list teardown push), NOT a ctor and NOT window-manager attach (the manager is MainMaster's ~223-slot table). game.ver gate = **single u32 index-5 equality**. login sub-states 29/30/31 recovered (31 = PIN modal show, 32 = PIN poll). password field cap 129 (validate at submit).
- ~35 curated name proposals → `_dirty/campaign10/glossary.yaml` (block B). Full element tables in `_dirty/campaign10/B/construct_{login,select,hud}.md`.
- **P (promotion): DONE** — 8 Block-B specs (`structs/gucomponent`, `structs/guwindow`, `ui_system`, `ui_hud_layout`, `input_ui`, `frontend_scenes`, `login`, `formats/ui_manifests`) rewritten to 100% + verification banners; **Tier-1 firewall scan PASS** (zero decompiler artifacts / image-range addresses). The geometry-transposition + event-taxonomy fixes are load-bearing for the Godot UI port (Phase E). Phase-D annotation deferred to the batched wave.

### Block C — VFS / Asset I/O & Resource Pipeline ★ — **W+P DONE 2026-06-16 (firewall PASS, sample-verified); D deferred(batched)**
Covered `formats/pak`, `specs/vfs_overview`, `asset_pipeline`, `structs/terrain-manager` (read side).
The two-witness gate (IDB loader + real `data.inf`/`data.vfs` sample) delivered **sample-verified** results.

**C — reconciled findings:**
- **VFS `data.inf`/`data.vfs` header DECODED** (the doc said "magic cannot be inferred"): **24-byte header = magic `"VFS001"` (8B null-padded) + u32(=39, role TBD) + u32 entry_count + u32 blob-size**. The same header is **echoed at `data.vfs` offset 0** (entry-0 payload at offset 24). **Sample-verified.**
- **Entry-count +0xC RESOLVED both ways** (IDB stack-offset proof + `24+144×43347 = 6,241,992` = exact `data.inf` size); the `+0x08` reading is **refuted**. Vindicates Block A.
- **TOC entry = 144 B**: name[100], pad_100 (≈0; ~14/43347 nonzero = build-tool residue), dataOffset i64@104, dataSize i64@112 (low dword only), **3× FILETIME** (ctime/atime/mtime)@120/128/136 — NOT padding.
- **Storage is RAW** (no compression/encryption/codec flag) via **ReadFile-into-buffer, NOT mmap** (the only `MapViewOfFile` user is an unrelated anti-tamper self-check). One global read lock. Named class `CVFSManager`. Mount = `game.lua vfsmode` toggle.
- **terrain-manager.md conflated TWO singletons** → split into **TerrainLoader** (streamer; worker thread **DORMANT** after init; FIFO; **34-slot** pool; cell-key RB-tree) and **TerrainManager** (9 grid sub-objects; **25-slot** ring; 2 GFrustum; stream-radius clamp ≥15000→1000; map-option/region @+464/+468, NOT spawn coords). Cell = 24,712 B; key = `mapZ+100000*mapX`.
- **Loading bar is near-static** (normalized = bytes / 9,395,240 **integer**; bar px = `223*v/100`; fills only ~939 MB-in) — completion driven by the worker done-flag, **not** the bar (cross-confirms A5). The "~9.4 MB → 100%" framing was wrong.
- ~30 curated name proposals → glossary (block C). Full notes in `_dirty/campaign10/C/`.
- **P (promotion): DONE** — 4 Block-C specs (`formats/pak`, `vfs_overview`, `asset_pipeline`, `structs/terrain-manager`) rewritten to 100% with **sample-verified** banners; **Tier-1 firewall scan PASS** (zero artifacts). The `pak.md` header decode (`VFS001` magic, FILETIME TOC, RAW storage) and the terrain two-singleton split are the load-bearing corrections. Phase-D annotation deferred to the batched wave.

### Block D — Asset Format Corpus (two-witness) ★ — **W+P DONE 2026-06-16 (firewall PASS, sample-verified); D-annot deferred(batched)**
Re-confirmed every `formats/*.md` against the IDA loader AND a real VFS sample. The corpus **RE-CONFIRMS
sample-verified** on build `263bd994` (vindicates the campaign-8 two-witness work) with mostly minor drifts.

**D — reconciled findings (corrections, not rewrites):**
- **terrain:** `.ted` = 46987 B (5 reads, no header); texidx stored **RAW** (re-confirms the render-domain fix); **EXTRA_TERRAIN → `.exd`** (not `.ted`); `.map` WIDTH/HEIGHT/GRID/ORIGIN present-on-disk but **not parser-consumed**; `.mud` confirmed.
- **mesh/anim:** `.skn` **normal-first** verified, LenStr **4-byte** prefix (binary-mode bit); bindlist **string-sorted**, 349 entries; BANI drifts (name_len 10, frame_count 24); `+0x40/+0x64` int[9] arrays naming reconciled across animation/actormotion.
- **texture/effects:** no header test (both loaders feed D3DX); **effectscale.xdb REPLACES** the .xeff base-scale at parse; `.xeff` **9-byte** track header; particleEmitter.eff variable-length.
- **env/region:** region cell byte = **INDEX (0..31)** not a 0/1 mask; zoneType `{0,1,2,9}`; per-map = **`map<NNN>.bin` (520B)** not `mapsetting<NNN>.bin`; 3 `.tol` **inside** the VFS; fog always 204B; ambient floor 1.0 (white); all env-bin sizes byte-exact.
- **sound:** stride **48** DEFINITIVE (52 refuted by both witnesses); ~301 tables; two loader entry points.
- **items/config:** `items.csv` **NOT runtime-loaded** (authoring-only, now CONFIRMED); items.scr discriminator on-disk **+0xD2**; citems `+0` = **item_id** (not slot_index); **`.do` stride 116** confirmed (166 refuted); citems desc paragraphs **6-vs-10 UNRESOLVED** (carried open).
- **scr:** strides resolved by arithmetic (helps=48, dashs=199/28, userlevel=60/300); events.scr 520B/1848, 4 consumed fields; 6 new record counts.
- **xdb/mi:** msg.xdb 516B/2644 confirmed; **`mobinfo.mi` IS in the VFS** (`data/ui/mobinfo.mi`, 592B = 4+21×28) — the doc's "absent" verdict REVERTED (no client loader still holds); buff-icon spacing **25** not 27.
- **npc:** `npc.arr` 28B/559 records; spawn_type enum **0..11** (not {0,7}); `mob.arr` 20B has **no client loader** (client uses `mobs.scr`); map207 240B anomaly.
- **indices:** area count **60** (not 63), 2503 cells; game.ver 28B/7-u32 (index-5 compare); bgtexture.lst 48B, kind byte → 2 pools.
- ~28 curated loader name proposals → glossary (block D). Full notes in `_dirty/campaign10/D/`.
- **P (promotion): DONE** — 14 Block-D lanes rewrote the formats corpus to 100% with **sample-verified** banners; **Tier-1 firewall scan PASS** (zero real artifacts; the only grep hits were the field name `sub_effect_count` and the neutral label `unk_dist`, both legitimate). **★ MILESTONE: all four priority blocks A/B/C/D are now W+P+firewall complete.**

### Block E — Network / Protocol / Crypto — **W+P DONE 2026-06-16 (firewall PASS); D-annot deferred(batched)**
Covered `specs/network_dispatch`, `crypto`, `handlers`; `opcodes.md`; the C2S+S2C `packets/*.yaml`;
entity + item/skill/npc structs. Static + VFS only — packet field **VALUE** semantics stay capture-pending
(no wire this campaign); routing/sizes/offsets are control-flow **confirmed**.

**E — reconciled findings:**
- **Frame header RESOLVED = 8 B `[u32 size @0][u16 major @4][u16 minor @6]`** — the size is **u32** (settles the long-standing u16-vs-u32 question).
- **Crypto open-question #1 RESOLVED (open since June):** the **inbound path applies NO inverse cipher** — it is LZ4-decompress-only; the byte cipher has **exactly one xref** (the outbound send gate), a positive single-caller proof. `crypto.md` was otherwise **already correct** (3-round keyless cipher; FLINT-bignum handshake 0/0→1/4, PKCS#1 v1.5, XOR-whitened, does not key the cipher).
- **Major-3 opcode ladder CORRECTED** (matches Block A): **3/4 = SceneEntityUpdate, 3/7 = CharManageResult (8B), 3/14 = CharSpawnResponse (16B)**, 3/100 = generic action-result (no case 32). The doc/YAMLs had these swapped (incl. the misnamed `3-4_char_manage_result.yaml`).
- **Keepalive = TWO mechanisms**: the armed `(2,10000)`@20s compressed frame AND the C2S **2/112** 1-byte toggle (g_KeepaliveEnabled). Both real; on-wire cadence capture-pending. `opcodes.md` gains a 2/112 row.
- **Dispatch shape:** majors 1/3 inline switches; 4/5 **table-driven** (two 154-slot tables, base+1246/+1400, inert no-op default, minor≥154 undispatched, 4/500+4/50000 outside); major-0 = hardwired (0,0) handshake branch. Response ~99 / Push 65 slots.
- **Packet sizes/offsets confirmed** at send/handler sites (1/6=52, 1/9=40, 2/13=16, **2/28=12 fixed** not "var", 2/52=24+arrays, 5/13=40, **5/52 header offsets fixed** ActionCode@0x10/TargetCount@0x14, 3/1=3B+5×981B, 3/5=44B, 4/29=36B, 4/102=476B). Text length-prefix: 2/7 EXCLUDES NUL, 3/21 INCLUDES it. ~60 additional major-2 C2S senders found (coverage gap noted).
- **Structs:** Actor/SpawnDescriptor confirmed (local-player global, spatial index +0x3EC, equip table 20×16@+0xCC); skills.scr 1504+N×8 / 1508B obj; mobs.scr 488B / +0xF8 HP+=10 / +0x144==11 boss.
- ~35 curated network/crypto/handler name proposals → glossary (block E). Full notes in `_dirty/campaign10/E/`.
- **P (promotion): DONE** — 8 Block-E lanes (network_dispatch, crypto, handlers, `opcodes.md`, C2S+S2C packet YAMLs, entity + item/skill/npc structs) rewritten to 100%; **Tier-1 firewall scan PASS** after fixing one real leak (a Hex-Rays `_QWORD` type name in `structs/npc.md` → neutral "64-bit value (`u64`)"). The crypto OQ#1 resolution + the 3/4·3/7·3/14 opcode ladder are the load-bearing corrections. (Pre-existing minor: `journal.md` carries a `__thiscall` provenance mention from a prior campaign — append-only, left intact; flag for the Phase-R clean-room-auditor.)

### Block F — Gameplay Systems — **W+P DONE 2026-06-16 (12/12, firewall PASS); D-annot deferred(batched)**
Covered combat, skills, inventory_trade, equipment_visuals, progression, quests, npc_interaction,
chat, social, minimap, camera_movement, lua_scripting/lua-config, world_systems. Static + VFS only —
client-side routing/sizes/offsets/formulas **confirmed**; server-authored magnitudes + wire VALUE
semantics stay capture-pending.

**F — reconciled findings (largely confirmed; offsets re-pinned from prior-build drift):**
- **Combat:** melee = **C2S 2/52 slot 0xFF** (slot byte = `(stance!=1)-56`); cadence = **100ms×skill_cadence** (rec +1332), **550ms** lockout; 4/100 = 188B. Default basic-attack skill **121100050**. CONFLICT: the attack-flag clear is **4/13** (LocalPlayerStateSync) on this build, not 4/2 (capture-pending which arms vs releases). Controller offsets re-pinned (+136/+140 → +36/+40).
- **Skills:** the hotbar is **ONE 240×8B record array** (id@+0, points@+4) — resolves the old "two parallel arrays / second-int unverified" open question; skills.scr 1504+N×8 / 1508B obj; 4/102 = 476B snapshot.
- **Inventory/trade:** **THREE** item arrays (bag `40*(bag_count+3)` / equip 20 / a new 120-entry @lp+1260). Two offset fixes: 4/23 reads selector@+8 + reason@+9 + phase@+10 (all live); 4/25 phase@+8 / count@+0x18.
- **Equipment→visual:** off-hand node **flag = 1** (flag-2 = main-hand); slot-15 rebuild byte @+0x0C (16B) vs +0x0B (20B); both GID formulas confirmed.
- **Progression:** **the stat-EDITOR row labels were swapped** (correct: +376=STR/+380=DEX/+384=INT/+388=AGI/+392=CON) — BUT the **2/29 WIRE body (STR,INT,AGI,DEX,CON) is CORRECT and untouched**; 5/32 also broadcasts chat 10081; rank cap-25 per-level table.
- **Others (quests/npc/chat/minimap/camera/lua/world):** confirmed with offset re-pins; events.scr quest model; chat length-prefix rule (2/7 excl-NUL / 3/21 incl-NUL); 5 camera modes; region cell = INDEX (cross-confirms Block D).
- ~28 curated name proposals → glossary (block F). Full notes in `_dirty/campaign10/F/`.
- **P (promotion): DONE 12/12** (resumed after the session reset — the 5 stalled lanes completed). All 14 F specs rewritten to 100% + verification banners; **Tier-1 firewall scan PASS** (zero artifacts). Several F specs were found already-reconciled from a prior pass and only needed surgical residual fixes (e.g. camera FOV no-/2, the chat NUL off-by-one resolution). Phase-D annotation deferred to the batched wave.

### Block G — Rendering / Effects / Terrain / Skinning / Environment / Sound — **W+P DONE 2026-06-16 (6/6, firewall PASS); D-annot deferred(batched)**
Covered `specs/rendering`, `effects`+`effect-scheduling`, `terrain-streaming`, `environment`,
`skinning`, `sound` (behaviour/runtime; the on-disk formats are Block D). Notes in `_dirty/campaign10/G/`.

**G — reconciled findings (research done; promotion pending on resume):**
- **Rendering:** glow/bloom = 3 RTs, bright-extract is a **plain fixed-function copy (no PS, no threshold)**, one downscaled glow-blur, composite TEX1+TEX2 with code-uploaded c0/c1; cel/toon gated on the **offscreen** path (**TWO** cel pixel shaders dotoonshading.psh+2.psh). Anchor fixes: `0x61bd42` = device-step+**Present**+device-lost recovery (not "render frame"); real draw fork `0x61139E` → offscreen `0x6104cb` / direct `0x610f7c`. **Frame-cap reconciliation with Block A: the rate field is `engine+0x30` = `scene+48` (0x30 hex = 48 dec, SAME field), seeded 60.0f & never overwritten → effective fixed ~60, but mechanically a configurable per-scene rate** (QPC Sleep to 1/rate). Device-lost lifecycle (TestCooperativeLevel → Reset/Sleep) newly documented.
- **Effects:** keyframe sampler (sprite STEPPED, alpha/color/vel/size LERP, rotation SLERP); **passes 2/3/4 = diffuse R/G/B** confirmed at parse; effectscale.xdb **REPLACE at lazy-parse** (closes effects §14.9); **the campaign9c "sub-effect Z-negation" is PORT-SIDE only — the binary applies NO Z-negation** (treats anchor & offset uniformly); vertex diffuse pack order is **B,G,R,A** (load-bearing for on-screen colour); Euler keys are DEGREES. The **10001 timed-event** is a **sorted RB-tree** scene/connection deferred trigger (NOT an effect spawn) — distinct from the linear effect lists.
- **Terrain-streaming:** worker thread **DORMANT** (init clears keep_running); synchronous per-frame main-thread streaming; cell pool **34** vs spatial ring **25** (confirms Block C two-singleton split); cell key `mapZ+100000*mapX` + a **+10000 cell-index origin offset** the doc omitted; radius clamp ≥15000→1000; 5×5-vs-3×3 by radius>1000; 4-function ring (cold 3×3/5×5 + per-frame 3×3/5×5).
- **Environment / Skinning / Sound (G4/G5/G6):** research complete (`_dirty/campaign10/G/`); **skinning** (the load-bearing Godot avatar-explosion debt) consolidated — bind-pose / inverse-bind-baked-into-vertex / LBS deform math for the Phase-E fix; details to be folded in at G promotion.

**W EXIT (per block):** all lanes returned; confidence rated; conflicts flagged; promotion map drafted.

---

## Phase P — Promotion / rewrite to 100% + master synthesis — **DONE 2026-06-16**
- All 7 blocks promoted (one author per spec file, verification banner on each); **Tier-1 firewall scan PASS per block.**
- **Master synthesis `specs/client_architecture.md` written** (478 lines, 12 sections, firewall PASS) — the top-level vision: entry → init scopes → GameState 0..7 scene machine → Diamond UI framework → VFS/resource pipeline → frame loop → network/gameplay/render subsystems, cross-linking every re-verified subject spec.
- Tier-1 serialized: `opcodes.md` reconciled (Block-E lane); `journal.md` CAMPAIGN-10 provenance entry appended. **`names.yaml` merge** = the one remaining mechanical step (pull the ~195 live IDB names via `ida-naming-sync` + adjudicate the 6 ctor collisions) — do at Consolidation/commit.

## Phase D — IDB annotation (legibility) — **DONE 2026-06-16 (batched)**
`re-annotation-orchestrator` applied `_dirty/campaign10/glossary.yaml` to the live IDB (build `263bd994`,
SHA-gate passed) — 216 entries → **201 unique addresses** (183 functions + 18 globals): **113 renamed**
(sub_ → canonical), 82 already-canonical (CRT `start`/`g_GameState` left as-is), **201 repeatable comments
set**, **0 unresolved / 0 failures**. `sub_` count **19,090 → 19,020** (−70 made legible). IDB not committed.
- **⚖️ 6 cross-campaign name collisions — TIER-1 ADJUDICATION at the names.yaml sync:** the campaign
  re-identified the REAL address of 6 ctors and the prior-campaign holder still owns the unsuffixed name,
  so the C10 addresses got a non-destructive `_0`/`_2` suffix. Re-point + demote the stale holders:
  `Diamond_GUComponent__ctor` 0x615135 (vs prior 0x52db68), `Diamond_GUWindow__ctor` 0x61d852 (vs 0x61d71e),
  `Diamond_GULabel__ctor` 0x6162be (vs 0x61626c), `Diamond_GUTextbox__ctor` 0x616df7 (vs 0x616d90),
  `Diamond_GUCheckBox__ctor` 0x617d2e (vs 0x617cbc), `TerrainManager_GetSingleton` 0x445694 (vs 0x445890).
  The comments DID land on the correct C10 addresses, so the IDB reads correctly regardless.
- **Next:** `ida-naming-sync` pulls the 195 live IDB names → `Docs/RE/names.yaml` (Consolidation).

## Phase E — Engineering: align code to corrected specs — PENDING
Staged pipeline (contracts → components → integration), one engineer per project per wave. Only where
a spec changed. `test-engineer` coverage alongside.

## Phase T — Tooling (parallel) — PENDING
Fold scanners into `vfs-inspect`; register the missing orchestrator agent-types
(`godot-client-`, `network-stack-`, `assets-pipeline-`, `client-core-`, `tooling-`, `quality-gate-`);
new parsers for any newly-found format. `tooling-auditor` PASS.

## Phase R — Review + Fix + Hard Gates — PENDING
4 reviewers (render / C# / clean-room / architecture; + perf if hot paths) → fix wave → hard gates:
build 0/0 (`--no-incremental`) · tests green · firewall PASS · verification-banner audit · headless boot.

## Phase C — Consolidation — **IN PROGRESS 2026-06-16**
- [x] ROADMAP statuses updated in place (all blocks W+P+firewall; Phase D done).
- [x] `journal.md` — CAMPAIGN-10 provenance entry appended.
- [x] memory — `campaign10-doc-reverification.md` written + `MEMORY.md` index line.
- [ ] `names.yaml` — sync the ~195 live IDB names (`ida-naming-sync`) + adjudicate the 6 ctor collisions (do at commit).
- [ ] `preservation-archivist` pass + **commit ONLY on maintainer request** (targeted paths; never `_dirty/`, `.godot/`, or originals).

**DOCUMENTATION MILESTONE REACHED:** the user's core ask — re-understand all of `doida.exe` against IDA and
re-work every `Docs/RE/*.md` to 100% — is COMPLETE (7 blocks W+P+firewall, master synthesis, IDB annotated).
**Phase E (engineering) is the next stage** (align C#/Godot to the corrected specs; needs build/test/Godot gates).

### ⏸ RESUME ANCHOR — paused 2026-06-16 on a session limit (resets ~04:50 Europe/Paris)

**Where we are:** all 7 research blocks (W) are COMPLETE (A,B,C,D,E,F,G — every `_dirty/campaign10/*` note
written). Promotion (P) is done + **firewall PASS** for **A, B, C, D, E**; **F is 7/12 promoted**
(firewall scan still pending on those 7); **G is researched but not promoted**. The glossary
`_dirty/campaign10/glossary.yaml` holds ~190 reconciled name proposals across all 7 blocks.

**To resume (in order):**
1. **Finish Block-F promotion** — the 5 stalled lanes: `camera_movement`, `chat`+`social`,
   `lua_scripting`+`lua-config`, `minimap`, `world_systems` (resume `campaign10-block-f-promote` via
   `resumeFromRunId: wf_6c8bf579-1a7` so the 7 done lanes return cached). Then **firewall-scan all 12 F specs**.
2. **Promote Block G** (6 lanes from `_dirty/campaign10/G/`): `rendering`, `effects`+`effect-scheduling`,
   `terrain-streaming`, `environment`, `skinning`, `sound`. Then firewall-scan.
3. **Master synthesis** — write `Docs/RE/specs/client_architecture.md` (the top-level map: entry → init
   scopes → GameState 0..7 scene machine → Diamond GU* window framework → VFS/resource pipeline → frame
   loop → network/gameplay/render subsystems), cross-linking every re-verified subject spec.
4. **Tier-1 serialized post-promotion** — reconcile `opcodes.md` (final sort/dedup), merge the glossary's
   canonical names into `Docs/RE/names.yaml`, append the CAMPAIGN-10 `journal.md` provenance entry.
5. **Phase D — IDB annotation** (batched, now that all research is done): `re-annotation-orchestrator`
   → `re-ida-annotator` applies `_dirty/campaign10/glossary.yaml` to the live IDB via `/ida-annotate-batch`
   (dry-run → apply; unbridled; idempotent). Raises the named-function count on the construction/UI/VFS/net clusters.
6. **Phase E — engineering alignment** — only where a spec CHANGED: the load-bearing ones are the **GUComponent
   geometry transposition** (UI layout), the **skinning** math (avatar explosion), the **3/4·3/7·3/14 opcode
   ladder** + frame-header u32, the **VFS header** (`VFS001`/FILETIME), and the inbound-no-cipher fact.
   One engineer per project; staged; `test-engineer` alongside.
7. **Phase R — review + hard gates** — 4 reviewers + the **verification-banner audit** (every touched doc
   stamped) + build 0/0 (`--no-incremental`) + tests green + clean-room PASS. (Pre-existing minor for the
   clean-room-auditor: a `__thiscall` provenance mention in `journal.md`.)
8. **Phase C — consolidation** — finalize ROADMAP, journal, names.yaml, memory; **commit only on the
   maintainer's explicit request** (targeted paths).

**Do NOT restart from Phase 0** — the campaign is ~85% through W+P. Resume at step 1 above.

### ⚖️ PENDING MAINTAINER DECISION
(none yet)

— *Maintained by the orchestrator (Tier-1). Update block/phase statuses in place as waves complete.*
