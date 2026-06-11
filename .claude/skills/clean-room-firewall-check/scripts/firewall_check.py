#!/usr/bin/env python3
"""Clean-room firewall gate for the Martial Heroes preservation project.

Stdlib only. Read-only except for `git` queries. Enforces three invariants and
exits nonzero on any violation so it can drive a pre-commit hook or CI step:

  1. No tracked/staged file path contains 'Docs/RE/_dirty/', and no copyrighted
     original (*.pak, *.pcapng, *.tsv, *.exe, *.dll) is tracked/staged.
  2. No C# file under a numbered layer folder references a '_dirty/' path.
  3. Every changed committed spec under Docs/RE/ has a matching mention in
     Docs/RE/journal.md (or journal.md is in the same change set).

Exit codes:
    0  all invariants held
    1  one or more violations (commit/merge should be blocked)
    2  could not run (not a git repo / git unavailable)

Usage:
    python firewall_check.py --mode staged    # pre-commit (default)
    python firewall_check.py --mode tracked   # full-repo audit (CI)
"""

import argparse
import os
import posixpath
import re
import subprocess
import sys

DIRTY_FRAGMENT = "Docs/RE/_dirty/"
ORIGINAL_SUFFIXES = (".pak", ".pcapng", ".tsv", ".exe", ".dll")
LAYER_PREFIXES = (
    "01.Infrastructure.Shared/", "02.Network.Layer/", "03.Storage.Assets/",
    "04.Client.Core/", "05.Presentation/",
)
SPEC_DIRS = ("Docs/RE/packets/", "Docs/RE/formats/", "Docs/RE/structs/", "Docs/RE/specs/")
SPEC_FILES = ("Docs/RE/opcodes.md", "Docs/RE/names.yaml")
JOURNAL = "Docs/RE/journal.md"
DIRTY_REF = re.compile(r"_dirty/")


def run_git(args: list[str]) -> tuple[int, str]:
    try:
        proc = subprocess.run(["git", *args], capture_output=True, text=True)
        return proc.returncode, proc.stdout
    except FileNotFoundError:
        return 127, ""


def norm(path: str) -> str:
    return path.replace("\\", "/").strip()


def changed_files(mode: str) -> list[str] | None:
    if mode == "staged":
        code, out = run_git(["diff", "--cached", "--name-only", "--diff-filter=ACMR"])
    else:  # tracked
        code, out = run_git(["ls-files"])
    if code != 0:
        return None
    return [norm(p) for p in out.splitlines() if p.strip()]


def repo_root() -> str | None:
    code, out = run_git(["rev-parse", "--show-toplevel"])
    return norm(out.strip()) if code == 0 and out.strip() else None


# --- Invariant 1 -------------------------------------------------------------

def check_quarantine(files: list[str]) -> list[str]:
    violations = []
    for f in files:
        if DIRTY_FRAGMENT in f:
            violations.append(f"tracked/staged quarantine file: {f}")
        elif f.lower().endswith(ORIGINAL_SUFFIXES):
            violations.append(f"tracked/staged copyrighted original: {f}")
    return violations


# --- Invariant 2 -------------------------------------------------------------

def check_cs_dirty_refs(files: list[str], root: str) -> list[str]:
    violations = []
    for f in files:
        if not f.lower().endswith(".cs"):
            continue
        if not any(f.startswith(p) for p in LAYER_PREFIXES):
            continue
        full = os.path.join(root, f) if root else f
        try:
            with open(full, "r", encoding="utf-8", errors="replace") as fh:
                for i, line in enumerate(fh, 1):
                    if DIRTY_REF.search(line):
                        violations.append(f"{f}:{i} references a _dirty/ path: {line.strip()[:120]}")
        except OSError:
            # File staged for deletion or unreadable — nothing to check.
            continue
    return violations


# --- Invariant 3 -------------------------------------------------------------

def is_spec(path: str) -> bool:
    return path in SPEC_FILES or any(path.startswith(d) for d in SPEC_DIRS)


def check_spec_journaled(files: list[str], root: str) -> list[str]:
    changed_specs = [f for f in files if is_spec(f)]
    if not changed_specs:
        return []
    if JOURNAL in files:
        # Journal changed in the same set — provenance is being recorded now.
        return []
    journal_path = os.path.join(root, JOURNAL) if root else JOURNAL
    try:
        with open(journal_path, "r", encoding="utf-8", errors="replace") as fh:
            journal_text = fh.read()
    except OSError:
        return [f"changed spec '{s}' but {JOURNAL} is missing/unreadable" for s in changed_specs]

    violations = []
    for spec in changed_specs:
        base = posixpath.basename(spec)
        if spec not in journal_text and base not in journal_text:
            violations.append(
                f"changed spec '{spec}' has no mention in {JOURNAL} "
                f"(run the re-session-log skill to record provenance)")
    return violations


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Clean-room firewall gate (read-only).")
    parser.add_argument("--mode", choices=("staged", "tracked"), default="staged")
    args = parser.parse_args(argv)

    root = repo_root()
    if root is None:
        print("firewall-check: not a git repository (or git unavailable).", file=sys.stderr)
        return 2

    files = changed_files(args.mode)
    if files is None:
        print("firewall-check: failed to list files via git.", file=sys.stderr)
        return 2

    v1 = check_quarantine(files)
    v2 = check_cs_dirty_refs(files, root)
    v3 = check_spec_journaled(files, root)

    def report(name: str, viols: list[str]) -> None:
        if viols:
            print(f"[FAIL] {name}")
            for v in viols:
                print(f"       - {v}")
        else:
            print(f"[ OK ] {name}")

    print(f"clean-room-firewall-check (mode={args.mode}, {len(files)} file(s))")
    report("1. quarantine & originals stay out of git", v1)
    report("2. clean-room C# never references _dirty/", v2)
    report("3. changed specs are journaled", v3)

    total = len(v1) + len(v2) + len(v3)
    if total:
        print(f"\nFIREWALL BREACH: {total} violation(s). Commit/merge should be blocked.")
        return 1
    print("\nFirewall held. (Asserts these three invariants only — deeper review still required.)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
