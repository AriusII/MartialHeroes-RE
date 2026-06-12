#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — OPT-IN suggestion to run the targeted test slice
for the project you just edited.

Advisory only; never blocks; never runs anything. Disabled unless env MH_TEST_ON_EDIT=1, so the
default greenfield workflow stays fast. When enabled and a non-test core C# file (layers 01-04)
is edited, it emits a one-line systemMessage with the exact `dotnet test --filter` command for
that project's test assembly — it merely SUGGESTS the command for the author to run, keeping the
hook itself instantaneous. Test files and the Godot layer (05) are skipped.
"""
import os
import _hooklib as h


def _is_test_path(low):
    """A path that is itself test code (so editing it should not re-suggest its own slice)."""
    return (
        "/tests/" in low
        or low.endswith(".tests.cs")
        or ".tests/" in low
        or "/test/" in low
    )


def main():
    ev = h.read_event()
    if h.tool_input(ev) is None:
        h.ok()
        return

    # Opt-in only.
    import os
    if os.environ.get("MH_TEST_ON_EDIT") != "1":
        h.ok()
        return

    path = h.file_path(ev)
    low = path.replace("\\", "/").lower()
    if not low.endswith(".cs"):
        h.ok()
        return

    layer, proj = h.layer_of(path)
    if layer is None or layer < 1 or layer > 4:
        h.ok()
        return
    if _is_test_path(low):
        h.ok()
        return

    # proj is the project suffix, e.g. 'Network.Protocol' -> assembly MartialHeroes.Network.Protocol,
    # test class names live under MartialHeroes.Network.Protocol.Tests. A namespace filter scopes
    # the run to just that project's tests.
    filter_expr = "FullyQualifiedName~MartialHeroes.{}.Tests".format(proj)
    h.system_message(
        "ℹ MH_TEST_ON_EDIT: you edited core layer 0{} ({}). Suggested targeted run:\n"
        "  dotnet test --filter \"{}\"\n"
        "Not run automatically — kept advisory/fast.".format(layer, proj, filter_expr)
    )


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
