---
name: add-test-project
description: Use to add an xUnit test project for a MartialHeroes core library, named MartialHeroes.<Project>.Tests, placed under the tests/ tree, registered in the slnx /Tests/ folder, referencing the SUT, with one passing smoke test.
allowed-tools: Read Write Bash(dotnet new *) Bash(dotnet sln *) Bash(dotnet add *) Bash(dotnet test *)
model: sonnet
effort: medium
---

# add-test-project

Everything below layer 05 is engine-free, so the whole core is headlessly testable
with `dotnet test` — no Godot editor required. This skill establishes and applies the
test-project convention for a single subject-under-test (SUT) library.

## The convention (define and enforce it)

- **Location.** All test projects live under a top-level `tests/` directory:
  `tests/MartialHeroes.<Project>.Tests/`. They do NOT sit beside the SUT in the
  numbered layer folders — keeping production layers clean of test code.
- **Name.** `MartialHeroes.<Project>.Tests`, where `<Project>` is the SUT's short name
  (e.g. SUT `MartialHeroes.Network.Crypto` -> test `MartialHeroes.Network.Crypto.Tests`).
- **Framework.** xUnit (the mandated test framework for this repo). Use the `xunit`
  SDK template so `xunit`, `xunit.runner.visualstudio`, and the Microsoft test SDK are
  wired automatically.
- **Reference.** A `ProjectReference` to the SUT and nothing higher. A test project may
  reference the SUT and any libraries the SUT already legally depends on; it must never
  pull in a higher layer than the SUT, and must never reference `Godot`.
- **slnx registration.** Registered under a top-level `/Tests/` solution folder in
  `MartialHeroes.slnx` (create the folder if it does not yet exist), kept separate from
  the numbered production folders.
- **Smoke test.** Exactly one trivial passing test on first creation, proving the SUT
  is reachable and the harness runs. Real tests are added later by engineers.

## Hard rules

1. Test projects target `net10.0` like everything else — the core is engine-free, so
   tests run headless on any OS.
2. Never reference Godot or any presentation assembly from a core test project.
3. One test project per SUT. If `MartialHeroes.<Project>.Tests` already exists, stop
   and report — do not duplicate; add cases to the existing project instead.
4. The smoke test must actually touch the SUT (reference a public type/namespace) so a
   broken reference fails the build, not just assert `true`.

## Steps

1. **Resolve the SUT.** Confirm the SUT short name and its csproj path, e.g.
   `04.Client.Core/MartialHeroes.Client.Domain/MartialHeroes.Client.Domain.csproj`.
   Derive the test name `MartialHeroes.<Project>.Tests`.

2. **Create the xUnit project** under `tests/`:

   ```powershell
   dotnet new xunit --name MartialHeroes.<Project>.Tests --output "tests/MartialHeroes.<Project>.Tests" --framework net10.0
   ```

3. **Normalize the generated csproj** to the repo's minimal shape, keeping the test
   packages the template added. Target shape (4-space indent, matching repo style):

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
           <TargetFramework>net10.0</TargetFramework>
           <ImplicitUsings>enable</ImplicitUsings>
           <Nullable>enable</Nullable>
           <IsPackable>false</IsPackable>
       </PropertyGroup>

       <ItemGroup>
           <PackageReference Include="Microsoft.NET.Test.Sdk" />
           <PackageReference Include="xunit" />
           <PackageReference Include="xunit.runner.visualstudio" />
       </ItemGroup>

   </Project>
   ```

   Preserve whatever version attributes/`coverlet` entries the template emitted if your
   repo does not centralize versions; only collapse the `PropertyGroup` to the
   canonical four properties plus `IsPackable=false`.

4. **Reference the SUT** (relative path climbs out of `tests/` into the layer folder):

   ```powershell
   dotnet add "tests/MartialHeroes.<Project>.Tests/MartialHeroes.<Project>.Tests.csproj" reference "NN.LayerFolder/MartialHeroes.<Project>/MartialHeroes.<Project>.csproj"
   ```

5. **Write one smoke test** that touches the SUT. Replace the template's default test
   file with `tests/MartialHeroes.<Project>.Tests/SmokeTests.cs`, e.g. for
   `Shared.Kernel`:

   ```csharp
   using MartialHeroes.Shared.Kernel;
   using Xunit;

   namespace MartialHeroes.Shared.Kernel.Tests;

   public sealed class SmokeTests
   {
       [Fact]
       public void Sut_Assembly_Is_Referenced_And_Loads()
       {
           // Touch a type from the SUT so a broken reference fails the build.
           var asm = typeof(/* a public type in the SUT */).Assembly;
           Assert.NotNull(asm);
       }
   }
   ```

   If the SUT still only has the placeholder `Class1`, reference that type for now and
   leave a `// TODO` to retarget once a real public type exists. Delete the template's
   `UnitTest1.cs`.

6. **Register in the slnx** under a `/Tests/` solution folder. Read `MartialHeroes.slnx`
   first; if there is no `<Folder Name="/Tests/">`, add one (sibling to `/Docs/`). Then:

   ```powershell
   dotnet sln MartialHeroes.slnx add "tests/MartialHeroes.<Project>.Tests/MartialHeroes.<Project>.Tests.csproj"
   ```

   Re-Read the slnx and confirm the new `<Project Path=...>` landed inside the `/Tests/`
   `<Folder>` with a forward-slash path. If `dotnet sln add` placed it elsewhere or the
   `/Tests/` folder was not created, fix the slnx with a precise Write that leaves every
   other line untouched.

7. **Run the test** to prove green:

   ```powershell
   dotnet test "tests/MartialHeroes.<Project>.Tests/MartialHeroes.<Project>.Tests.csproj"
   ```

8. **Report** the test csproj path, the SUT it references, the slnx folder used, and the
   `dotnet test` result (1 passing smoke test expected).

## Decision points

- **If `MartialHeroes.<Project>.Tests` already exists**, STOP — do not duplicate; tell the
  caller to add cases to the existing project.
- **If the SUT still has only the placeholder `Class1`**, touch `Class1` in the smoke test
  and leave a `// TODO` to retarget; never assert bare `true` (a broken reference must fail
  the build, not silently pass).
- **If the `/Tests/` slnx folder is missing**, create it (sibling to `/Docs/`) before adding.
- **If the SUT is a Domain library**, prefer the smoke test exercise a deterministic public
  type — that primes the project for the real behavior-parity tests engineers add later.

## Verify / Done when

- [ ] The test project lives under `tests/`, NOT inside a numbered layer folder.
- [ ] It references the SUT and nothing higher than the SUT; no Godot/presentation reference.
- [ ] The slnx `<Project Path=…>` is inside the `/Tests/` `<Folder>`, forward-slashed.
- [ ] `dotnet test` shows exactly one passing test that actually touches a SUT type.

## Pitfalls (anti-patterns)

- **Never** place a test project in a numbered production layer folder.
- **Never** reference Godot or a layer above the SUT from a core test project.
- **Never** write a smoke test that asserts `true` without touching the SUT.
- **Never** create a second test project for a SUT that already has one.

> North star: serves **N2** — headless xUnit coverage is how the re-implemented core proves
> byte-/behavior-fidelity to the original without ever opening the Godot editor.

## Do not

- Do not put test projects inside the numbered layer folders.
- Do not reference Godot or any layer above the SUT.
- Do not run `git` or IDA tooling.
