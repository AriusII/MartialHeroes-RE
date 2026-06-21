---
name: python-tooling
description: Use when authoring, linting, or running the Martial Heroes project's Python scripts and harnesses — check_dag.py, codegen, the vfs harness, .claude hook scripts — std-lib-first and fail-open.
allowed-tools: Read Write Edit Grep Glob Bash(python *)
model: sonnet
effort: high
---

# python-tooling — author / lint / run the project's Python scripts

The single home for how Martial Heroes' **Python tooling** is written and run: the DAG checker
(`.claude/skills/scaffold-project/scripts/check_dag.py`), codegen drivers, the VFS / asset harnesses,
and the `.claude/hooks/*.py` advisory scripts treated as *runnable* tooling. These scripts must run on
the maintainer's machine and in IDA's restricted Python with **no install step**, so the rules are
narrow: **std-lib only**, **fail gracefully with clear messages**, **quote every path**, and **prove it
parses (`ast.parse` / `py_compile`) and runs before declaring done**.

This is execution tooling, not a place to assert truth about the binary. Anything a script *encodes*
about the original (layer map, opcode constants, format offsets) must trace to a committed `Docs/RE/`
spec or to `CLAUDE.md`/`KIT.md` — never to memory and never to pasted decompiler output.

## Firewall placement — clean-room, no IDA

Layer-05/clean-room tooling: **no `mcp__ida__*`**, never paste IDA/Hex-Rays output (`sub_`/`loc_`/
`_DWORD`/`__thiscall`/addresses/mangled names) or copyrighted bytes into a script, a docstring, or a
test fixture. A harness that *reads* the real VFS prints metadata (sizes/offsets/ids), never raw asset
bytes. The clean-room firewall is non-negotiable.

## Conventions (the contract every script obeys)

- **Std-lib only.** No `pip install`, no third-party imports. `argparse`, `pathlib`, `xml.etree`,
  `struct`, `json`, `re`, `hashlib`, `os`, `sys` are the toolbox. (IDA-side scripts: IDAPython +
  std-lib only — they run in a restricted interpreter.)
- **Fail-open / fail-graceful, mirroring the `.claude` advisory-hook contract.** A *hook* script must
  `exit 0` on every path and wrap `main()` in `try/except → h.fail_open(exc)` (it warns, never blocks).
  A *CLI tool* (e.g. `check_dag.py`) is allowed a non-zero exit to signal a real failure to CI, but
  must still degrade cleanly — never traceback-and-die on a missing file or malformed input. Either
  way: a clear, actionable message, never a bare stack trace as the user-facing result.
- **Quote every path.** Paths carry spaces (`05.Presentation/...`) and the repo lives under
  `C:\Users\Arius\...`. Use `pathlib.Path`, pass paths as `argparse` args, and quote them in any
  shelled command. Compare case-insensitively on Windows.
- **Windows/UTF-8 hygiene.** Game text is CP949; tool I/O is UTF-8. Reconfigure stdout to UTF-8
  (`sys.stdout.reconfigure(encoding="utf-8")` in a `try/except`), strip a UTF-8 BOM before parsing
  csproj/slnx/cfg, and open files with an explicit `encoding=`.
- **Idempotent + self-describing.** A module docstring states purpose/usage/exit codes (see
  `check_dag.py`); codegen writes deterministic output so re-running is a no-op diff.
- **Hooks import `_hooklib`.** Hook scripts do `import _hooklib as h` and route I/O via
  `h.read_event()`/`h.system_message()`/`h.additional_context()`/`h.fail_open()` — never raw
  `print()` of JSON. Shared predicates go *into* `_hooklib.py`; don't fork them.

## Procedure

1. **Read the need.** Identify the exact script and its kind — **CLI tool** (`check_dag.py`, codegen,
   harness: `python <file> ...`, may exit non-zero) vs **advisory hook** (`.claude/hooks/*.py`: stdin
   event JSON, `exit 0` always). Read the existing file and the nearest sibling for its shape; for a
   hook, read `_hooklib.py` first so you reuse helpers.
2. **Implement std-lib-first.** Write/extend with the conventions above. CLI tools: `argparse` with
   sane defaults, a module docstring, a `main()` returning an exit code, `if __name__ == "__main__":
   sys.exit(main())`. Hooks: one concern, `import _hooklib as h`, `try/except → h.fail_open(exc)`.
3. **Parse-check (you cannot skip this).** Prove it's syntactically clean before running anything that
   has side effects:
   ```powershell
   python -m py_compile ".claude/skills/scaffold-project/scripts/check_dag.py"
   python -c "import ast; ast.parse(open(r'.claude/hooks/python_tooling_lint.py', encoding='utf-8').read())"
   ```
   Either errors out loudly on bad syntax; clean = no output / exit 0.
4. **Run it.** Execute the tool on real inputs (quote paths). For a hook, feed it a representative
   event JSON on stdin and confirm it emits the right `systemMessage`/`additionalContext` and
   `exit 0`. For a harness, point it at the real VFS or a small fixture.
5. **Validate the output.** Check the result is *correct and actionable*, not merely that it ran:
   exit code matches intent, the message names the offending file/edge, re-running is idempotent, and
   no raw asset bytes / decompiler artifacts leaked into stdout.

## Examples

### A. Lint a script before declaring it done

```powershell
# syntax gate — every Python change passes this before "done"
python -m py_compile ".claude/hooks/csharp_guard.py"
# advisory-hook sanity: feed a fake event, confirm exit 0 + a nudge (never a block)
echo '{"tool_name":"Write","tool_input":{"file_path":"x.cs"}}' | python ".claude/hooks/csharp_guard.py"
echo "exit=$LASTEXITCODE"   # must be 0
```
A non-zero exit, a `decision:block`, or a `permissionDecision` in the output means the hook violates
the advisory-only contract → fix it (wrap in `try/except → h.fail_open`, emit `systemMessage` only).

### B. Run the DAG checker

```powershell
# exit 0 = the ProjectReference graph is downward-only + acyclic + known-projects-only
python ".claude/skills/scaffold-project/scripts/check_dag.py" .
echo "exit=$LASTEXITCODE"
```
On drift it prints the offending edge (e.g. an upward `04 → 05` reference or a `.Pipe` name) and exits
non-zero. Report the printed diff line — the offending `src -> dst` edge — not the whole walk; that is
the actionable part the C# engineers fix by re-wiring the csproj.

## Decision points

- **Hook vs CLI tool?** Hook → `exit 0` always, `_hooklib`, advisory-only. CLI → non-zero allowed for
  a real failure, but still degrade cleanly on bad input. Never make a hook block; never make a CI tool
  silently swallow a real failure.
- **Tempted to `pip install`?** Stop — find the std-lib equivalent (`xml.etree`, `struct`, `hashlib`,
  `urllib`). A third-party dep breaks IDA's restricted interpreter and the no-install contract.
- **A shared predicate already exists in `_hooklib`?** Reuse it; if it's new, add it to `_hooklib.py`
  and have every hook consume it — never fork divergent copies.
- **A script needs a binary fact** (a layer, an opcode, a format offset)? It comes from a committed
  `Docs/RE/` spec or `CLAUDE.md` — cite it in a comment; if it's missing, route the gap to the RE
  domain, don't guess.

## Done when

- [ ] Std-lib only; no third-party import; runs with no install step.
- [ ] Hook scripts `exit 0` on every path (`try/except → h.fail_open`), advisory-only, use `_hooklib`;
      CLI tools degrade cleanly with a clear message.
- [ ] `python -m py_compile` / `ast.parse` is clean, AND the script was actually run on real input.
- [ ] Output validated: correct exit code, actionable message, idempotent, paths quoted, no leaked
      asset bytes or decompiler artifacts.

## Anti-patterns

- **Never** add a third-party dependency or assume a venv — std-lib only, no install step.
- **Never** declare a script done on inspection alone — `ast.parse`/`py_compile` *and* a real run.
- **Never** author a *blocking* hook (`exit 2`, `decision:block`, `permissionDecision:deny/ask`) —
  advisory-only + fail-open, always.
- **Never** let a script die on a bare traceback as its user-facing result — catch and report cleanly.
- **Never** hard-code an unquoted Windows path or paste IDA output / raw asset bytes into a script.
- **Never** edit `settings.json` / `.mcp.json` here — that wiring is the main session's.

> North star: serves **N1+N2** — sharp, std-lib-first, fail-open Python keeps the tooling that gates the
> clean-room reverse (DAG checker, codegen, harnesses, advisory hooks) lawful and trustworthy so the
> whole RE→port fleet runs wide and stays safe.
