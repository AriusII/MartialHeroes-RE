---
name: dotnet-build-test
description: Use to build and test the MartialHeroes solution the canonical way — full build/test of MartialHeroes.slnx, or a tightly scoped single-project / single-test run via the dotnet test --filter form. The one place the project's build & test invocations live, so you stop guessing flags.
allowed-tools: Read Bash(dotnet *)
model: sonnet
---

# dotnet-build-test — canonical build & test invocations

This is the single home for how MartialHeroes is built and tested. The solution is a `.slnx`
(XML solution format) targeting **.NET 10 / C# 14**; tests use **xUnit** (the mandated framework).
Use this skill instead of improvising `dotnet` flags — the wrong filter syntax or a debug build can
hide or fabricate failures.

## Preconditions

1. A .NET 10 SDK is installed and on `PATH` (`dotnet --version` ≥ `10`). The `.slnx` solution format
   requires a recent SDK; an older one will reject the solution with a parse error.
2. Run from the repository root (where `MartialHeroes.slnx` lives), or pass an explicit path to the
   solution / a single `*.csproj`.
3. Note the greenfield caveat: not every layer project is wired with `ProjectReference`s yet. A
   project that does not build because its references are missing is an *architecture* gap (fix with
   the `wire-references` / `new-layer-project` skills), not a flag problem.

## Steps

### A. Full solution build

```powershell
dotnet build MartialHeroes.slnx
```

For a clean, warnings-visible release build (use before judging perf or shipping):

```powershell
dotnet build MartialHeroes.slnx -c Release
```

Read the summary line. Report `Build succeeded` with the warning count, or quote the first error
(`error CSxxxx`) with its `file(line,col)` — that is the actionable part.

### B. Full test run

```powershell
dotnet test MartialHeroes.slnx
```

This restores, builds, and runs every xUnit test project under `tests/`. Report the totals
(`Passed! - Failed: N, Passed: M, Skipped: K`). For a failing run, quote the failing test's fully
qualified name and the assertion message — not the entire log.

### C. Scope to ONE project

Point the same commands at a single `*.csproj` to iterate fast without rebuilding the world:

```powershell
dotnet build 02.Network.Layer/MartialHeroes.Network.Protocol/MartialHeroes.Network.Protocol.csproj
dotnet test  tests/MartialHeroes.Network.Protocol.Tests/MartialHeroes.Network.Protocol.Tests.csproj
```

### D. Scope to ONE test (or a subset) with `--filter`

`dotnet test --filter` selects by xUnit's test metadata. The most useful forms:

```powershell
# A single test method, matched by a substring of its fully qualified name:
dotnet test --filter "FullyQualifiedName~MyTestClass.MyTest"

# Every test in one class:
dotnet test --filter "FullyQualifiedName~VfsArchiveTests"

# By xUnit display name or trait, when the FQN is awkward:
dotnet test --filter "DisplayName~round-trips a TOC record"
dotnet test --filter "Category=Crypto"
```

Filter operators: `~` (contains), `=` (exact), `!=` (not equal); combine with `&` / `|`. Always
scope `--filter` to one test project's `.csproj` when you can — running it against the whole
solution still builds every test project first:

```powershell
dotnet test tests/MartialHeroes.Network.Protocol.Tests/MartialHeroes.Network.Protocol.Tests.csproj \
    --filter "FullyQualifiedName~OpcodeRouterTests.Routes_known_opcode"
```

### E. Useful flags

| Flag | When |
|---|---|
| `-c Release` | perf work, pre-ship; the debug JIT is not representative. |
| `--no-restore` / `--no-build` | tighten the inner loop when nothing changed upstream. |
| `-v minimal` / `-v quiet` | shrink noisy logs to just the result line. |
| `--logger "console;verbosity=detailed"` | expand a single failing test to see the full stack. |

## Reporting

- State the exact command you ran.
- Build: succeeded/failed, warning count, and the first error if any (with `file(line,col)`).
- Test: pass/fail totals; for failures, the FQN + assertion message of each failing test.
- If `dotnet` itself failed (missing SDK, unparseable `.slnx`, missing project reference), say so
  plainly and point at the cause — do not report it as a test failure.

## Hard rules

- Read-only over source. This skill **builds and tests**; it never edits code to make a build pass.
  If a test fails, report it; fixing is the engineer's call.
- Never pass `--no-build` together with a stale build to claim a green run — only when you have
  just built successfully.
- Do not silence warnings or disable tests to get a green result. A real failure is a finding.
