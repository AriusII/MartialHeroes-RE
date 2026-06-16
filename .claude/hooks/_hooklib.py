"""Shared helpers for MartialHeroes Claude Code hooks.

Design contract (see Docs/RE plan): every hook is ADVISORY ONLY. Nothing here ever
blocks a tool or a stop. Helpers exit 0 in all normal paths; on any internal error a
hook should call fail_open() which also exits 0. Standard-library only (hooks are shared
via git with no install step).

Windows notes: stdin is read as UTF-8 bytes (the console code page would corrupt accents);
stdout is reconfigured to UTF-8; on-disk files (csproj/slnx/gitignore) may carry a UTF-8 BOM
which is stripped before parsing; paths use backslashes and compare case-insensitively.
"""

import json
import os
import re
import socket
import sys

try:  # force UTF-8 output regardless of Windows console code page
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

IDA_HOST = "127.0.0.1"
IDA_PORT = 13337

MCP_ADD_HINT = (
    "claude mcp add --transport http ida http://127.0.0.1:13337/mcp"
)

CLEAN_ROOM_BLURB = (
    "Clean-room rule #1: never paste IDA/Hex-Rays pseudo-code into C#. Document the "
    "behavior/layout in Docs/RE prose, then re-implement fresh from that spec."
)

GROUND_TRUTH_BLURB = (
    "Ground-Truth Doctrine: doida.exe via IDA is the SINGLE absolute truth for the original's "
    "behavior/data/layout — confirm open facts there (static forms the hypothesis, the ?ext=dbg "
    "debugger confirms; never guess; STOP if MCP down). The committed Docs/RE specs "
    "(formats/packets/structs/specs + opcodes.md) are the DERIVED truth and the only thing C# reads; "
    "binary-vs-spec conflict => the binary wins, fix the spec. C#/Godot are measured against IDA+specs "
    "(official captures are the oracle for rendered pixels)."
)


# --------------------------------------------------------------------------- I/O

def read_event():
    """Parse the hook event JSON from stdin. Returns {} on any failure (fail-open)."""
    try:
        raw = sys.stdin.buffer.read()
        if not raw:
            return {}
        return json.loads(raw.decode("utf-8", errors="replace"))
    except Exception:
        return {}


def _emit(obj):
    try:
        sys.stdout.write(json.dumps(obj, ensure_ascii=False))
    except Exception:
        pass
    sys.exit(0)


def ok():
    """No-op success: allow the action, say nothing."""
    sys.exit(0)


def system_message(text):
    """Non-blocking, user-visible advisory. Honors 'conseil uniquement'."""
    _emit({"systemMessage": text})


def additional_context(event_name, text):
    """Inject context for SessionStart / UserPromptSubmit (added to Claude's context)."""
    _emit({"hookSpecificOutput": {"hookEventName": event_name, "additionalContext": text}})


def fail_open(exc):
    """Report an internal hook error to the debug log only, then allow the action."""
    try:
        sys.stderr.write("[hook] internal error (ignored): {}\n".format(exc))
    except Exception:
        pass
    sys.exit(0)


# ----------------------------------------------------------------------- context

def project_dir(ev=None):
    d = os.environ.get("CLAUDE_PROJECT_DIR")
    if d and os.path.isdir(d):
        return d
    if ev:
        cwd = ev.get("cwd")
        if cwd and os.path.isdir(cwd):
            return cwd
    return os.getcwd()


def state_dir(pdir):
    d = os.path.join(pdir, ".claude", "hooks", "state")
    try:
        os.makedirs(d, exist_ok=True)
    except Exception:
        pass
    return d


def append_jsonl(pdir, filename, obj):
    try:
        path = os.path.join(state_dir(pdir), filename)
        with open(path, "a", encoding="utf-8") as fh:
            fh.write(json.dumps(obj, ensure_ascii=False) + "\n")
    except Exception:
        pass


def strip_bom(text):
    return text.lstrip("﻿") if text else text


# --------------------------------------------------------------- tool-input access

def tool_name(ev):
    return ev.get("tool_name", "")


def tool_input(ev):
    ti = ev.get("tool_input")
    return ti if isinstance(ti, dict) else {}


def added_text(ev):
    """The text being WRITTEN by a Write/Edit/MultiEdit (never the removed text)."""
    ti = tool_input(ev)
    parts = []
    if "content" in ti and isinstance(ti["content"], str):
        parts.append(ti["content"])
    if "new_string" in ti and isinstance(ti["new_string"], str):
        parts.append(ti["new_string"])
    edits = ti.get("edits")
    if isinstance(edits, list):
        for e in edits:
            if isinstance(e, dict) and isinstance(e.get("new_string"), str):
                parts.append(e["new_string"])
    return "\n".join(parts)


def file_path(ev):
    return tool_input(ev).get("file_path", "") or ""


# ------------------------------------------------------------------- C# stripping

_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
_LINE_COMMENT = re.compile(r"//[^\n]*")
_VERBATIM_STR = re.compile(r'@"(?:[^"]|"")*"')
_NORMAL_STR = re.compile(r'"(?:\\.|[^"\\])*"')
_CHAR_LIT = re.compile(r"'(?:\\.|[^'\\])'")


def strip_comments_strings(text):
    """Best-effort removal of C# comments and string/char literals so that detectors do
    not trip on documentation or quoted examples (e.g. a comment mentioning *(_DWORD *))."""
    if not text:
        return ""
    text = _BLOCK_COMMENT.sub(" ", text)
    text = _LINE_COMMENT.sub(" ", text)
    text = _VERBATIM_STR.sub(" ", text)
    text = _NORMAL_STR.sub(" ", text)
    text = _CHAR_LIT.sub(" ", text)
    return text


# --------------------------------------------------------------------- layer logic

# Folder prefix → layer number is ground truth on disk.
_LAYER_RE = re.compile(r"(?:^|[\\/])0(\d)\.[^\\/]+[\\/]MartialHeroes\.([^\\/]+)[\\/]", re.I)


def layer_of(path):
    """Return (layer_number:int, project_suffix:str) for a path inside a layer project,
    or (None, None). e.g. '.../02.Network.Layer/MartialHeroes.Network.Protocol/X.cs'
    -> (2, 'Network.Protocol')."""
    if not path:
        return (None, None)
    norm = path.replace("\\", "/")
    if not norm.endswith("/"):
        norm_probe = norm + "/"
    else:
        norm_probe = norm
    m = _LAYER_RE.search(norm_probe)
    if not m:
        return (None, None)
    return (int(m.group(1)), m.group(2))


def layer_of_reference(include_path):
    """Layer number of a ProjectReference Include="..\\..\\0N.Foo\\MartialHeroes.X\\X.csproj"."""
    if not include_path:
        return None
    norm = include_path.replace("\\", "/")
    m = re.search(r"(?:^|/)0(\d)\.[^/]+/", norm)
    return int(m.group(1)) if m else None


# ----------------------------------------------------------------------- IDA / caps

def ida_mcp_up(timeout=0.4):
    try:
        with socket.create_connection((IDA_HOST, IDA_PORT), timeout):
            return True
    except Exception:
        return False


def ida_status_line():
    if ida_mcp_up():
        return "IDA MCP: UP (127.0.0.1:13337)."
    return "IDA MCP: DOWN. Open IDA on the DB; if not registered for Claude Code: " + MCP_ADD_HINT


def find_captures(pdir):
    """Count capture/extract files WITHOUT reading them. Returns (pcapng_count, tsv_count)."""
    pc = tv = 0
    try:
        for root, dirs, files in os.walk(pdir):
            # skip heavy/irrelevant trees
            dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", ".godot")]
            for f in files:
                low = f.lower()
                if low.endswith(".pcapng"):
                    pc += 1
                elif low.endswith(".tsv"):
                    tv += 1
    except Exception:
        pass
    return (pc, tv)


def count_placeholders(pdir):
    n = 0
    try:
        for root, dirs, files in os.walk(pdir):
            dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj", ".godot")]
            for f in files:
                if f == "Class1.cs":
                    n += 1
    except Exception:
        pass
    return n


def git_branch(pdir):
    import subprocess
    try:
        out = subprocess.run(
            ["git", "rev-parse", "--abbrev-ref", "HEAD"],
            cwd=pdir, capture_output=True, text=True, timeout=4,
        )
        return out.stdout.strip() or "(detached)"
    except Exception:
        return "(unknown)"


def git_dirty_count(pdir):
    import subprocess
    try:
        out = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=pdir, capture_output=True, text=True, timeout=5,
        )
        return len([l for l in out.stdout.splitlines() if l.strip()])
    except Exception:
        return -1


def staged_files(pdir):
    """Names of files staged in the git index (git diff --cached --name-only)."""
    import subprocess
    try:
        out = subprocess.run(
            ["git", "diff", "--cached", "--name-only"],
            cwd=pdir, capture_output=True, text=True, timeout=6,
        )
        return [l.strip() for l in out.stdout.splitlines() if l.strip()]
    except Exception:
        return []


# ------------------------------------------------------------- path classification

def _low(path):
    return (path or "").replace("\\", "/").lower()


def is_godot_cs(path):
    """A C# file inside the Godot presentation project (05.Presentation)."""
    p = _low(path)
    return p.endswith(".cs") and "/05.presentation/martialheroes.client.godot/" in p


def is_tscn(path):
    return _low(path).endswith(".tscn")


def is_layer_cs(path):
    """A C# file inside one of the numbered layer projects (00..05)."""
    return _low(path).endswith(".cs") and layer_of(path)[0] is not None


def is_spec(path):
    """A committed clean RE spec under Docs/RE (NOT the _dirty/ quarantine)."""
    p = _low(path)
    return p.endswith(".md") and "/docs/re/" in p and "/_dirty/" not in p


def is_dirty_path(path):
    return "/docs/re/_dirty/" in _low(path)


def is_parser_cs(path):
    """A C# file in the Assets.Parsers project (CP949 text decoding lives here)."""
    return _low(path).endswith(".cs") and "/martialheroes.assets.parsers/" in _low(path)


# --------------------------------------------------------------- Godot / build env

# The user's Godot 4.6.3 console build (prints stdout; used by the headless verify loop).
GODOT_CONSOLE_EXE = (
    r"C:\Users\Arius\Desktop\Godot_v4.6.3-stable_mono_win64"
    r"\Godot_v4.6.3-stable_mono_win64_console.exe"
)
DOTNET_EXE = r"C:\Program Files\dotnet\dotnet.EXE"


def godot_console_exe():
    return GODOT_CONSOLE_EXE if os.path.isfile(GODOT_CONSOLE_EXE) else None


def godot_project_dir(pdir):
    return os.path.join(pdir, "05.Presentation", "MartialHeroes.Client.Godot")


def dotnet_exe():
    return DOTNET_EXE if os.path.isfile(DOTNET_EXE) else "dotnet"


# ----------------------------------------------------------------------- detectors
# These power the advisory Godot / clean-room / perf nudges. All best-effort.

# In a Godot 4 .tscn, `script` is a PROPERTY LINE under the node header, NOT a header
# attribute. `[node ... script=ExtResource("1")]` is SILENTLY IGNORED (node ends up
# script-less -> gray screen). This catches the broken inline form.
_TSCN_INLINE_SCRIPT = re.compile(r"\[node\b[^\]]*\bscript\s*=", re.I)

# Inside namespace MartialHeroes.Client.Godot.*, a bare `Input.` / `Environment.` / `Time.`
# resolves to the sibling project namespace, NOT the Godot class -> CS0234. Use global::Godot.
_GODOT_NS_COLLISION = re.compile(r"(?<![\w.\"])(Input|Environment|Time)\s*\.\s*[A-Z]")

# The native Godot GLB importer crashes on this project's generated GLBs (no managed stack).
# AppendFromBuffer is a GltfDocument instance method; real code calls it on a local
# (doc.AppendFromBuffer(...)), so anchor on the distinctive method name, not the type.
_GLTF_CRASH = re.compile(r"\bAppendFromBuffer\s*\(")

# A magic numeric constant (hex, or a float/int literal) that should cite a spec.
_MAGIC = re.compile(r"\b0x[0-9A-Fa-f]{2,}\b|\b\d+\.\d+f?\b|(?<![\w.])\d{3,}\b")
_SPEC_CITE = re.compile(r"//\s*spec\s*:", re.I)

# CP949 / Korean-text decoding signals.
_CP949_NEEDED = re.compile(r"GetEncoding\s*\(\s*949\s*\)|RegisterProvider")

# Coordinate-convention signals in Godot World code.
_VECTOR3 = re.compile(r"\bnew\s+Vector3\b|\bRotationDegrees\b|\bToGodot\b")


def tscn_has_inline_script(text):
    return bool(_TSCN_INLINE_SCRIPT.search(text or ""))


def godot_ns_collisions(text):
    """Distinct bare Godot-class references that will collide with project namespaces."""
    seen = set()
    for m in _GODOT_NS_COLLISION.finditer(text or ""):
        seen.add(m.group(1))
    return sorted(seen)


def uses_gltf_appendfrombuffer(text):
    return bool(_GLTF_CRASH.search(text or ""))


def has_uncited_magic(text):
    """True when code text has magic numeric constants but no nearby `// spec:` citation.
    Best-effort: text should already be comment/string-stripped for the magic scan, but the
    citation check runs on the RAW text (comments included)."""
    return bool(_MAGIC.search(text or "")) and not _SPEC_CITE.search(text or "")


def mentions_korean_or_txt_read(text):
    """Heuristic: code likely decodes CP949 game text (a .scr/.txt/.csv read) without the provider."""
    t = text or ""
    reads_text = bool(re.search(r"\.(?:txt|csv|scr)\b|GetString\s*\(", t))
    return reads_text and not _CP949_NEEDED.search(t)


def has_coordinate_math(text):
    return bool(_VECTOR3.search(text or ""))


# ------------------------------------------------- .claude/ kit self-consistency
# Classifiers + a tiny frontmatter parser + advisory issue-finders that power the
# meta-guard hooks (agent_md_guard / skill_md_guard / hook_advisory_guard /
# settings_wiring_nudge). See .claude/KIT.md sections 6 and 7. All best-effort,
# std-lib only, returning simple values; advisory only.

def _claude_rel(path):
    """Path portion after '.claude/' (forward-slash, lower-cased), or '' if not under .claude/."""
    p = _low(path)
    i = p.find("/.claude/")
    if i != -1:
        return p[i + len("/.claude/"):]
    if p.startswith(".claude/"):
        return p[len(".claude/"):]
    return ""


def is_agent_md(path):
    """True for an agent definition file under .claude/agents/*.md (not the kit README/KIT)."""
    rel = _claude_rel(path)
    return rel.startswith("agents/") and rel.endswith(".md")


def is_skill_md(path):
    """True for a skill definition file under .claude/skills/<name>/SKILL.md."""
    rel = _claude_rel(path)
    return rel.startswith("skills/") and rel.endswith("/skill.md")


def is_hook_py(path):
    """True for a hook script under .claude/hooks/*.py (including _hooklib.py)."""
    rel = _claude_rel(path)
    return rel.startswith("hooks/") and rel.endswith(".py")


def is_claude_settings(path):
    """True for .claude/settings.json (or settings.local.json)."""
    rel = _claude_rel(path)
    return rel in ("settings.json", "settings.local.json")


def parse_frontmatter(text):
    """Best-effort parse of a leading YAML frontmatter block (--- ... ---).

    Returns a dict of top-level `key: value` pairs (values as strings; surrounding
    quotes stripped; simple `[a, b]` or `a, b` lists left as the raw string). Returns
    {} when there is no frontmatter. Std-lib only — NOT a full YAML parser; good enough
    for advisory frontmatter checks on agent/skill files."""
    if not text:
        return {}
    t = strip_bom(text).lstrip("\n")
    if not t.startswith("---"):
        return {}
    lines = t.splitlines()
    if not lines or lines[0].strip() != "---":
        return {}
    fm = {}
    for line in lines[1:]:
        if line.strip() == "---":
            break
        m = re.match(r"^([A-Za-z0-9_-]+)\s*:\s*(.*)$", line)
        if m:
            key = m.group(1).strip().lower()
            val = m.group(2).strip()
            if len(val) >= 2 and val[0] in "\"'" and val[-1] == val[0]:
                val = val[1:-1]
            fm[key] = val
    return fm


_VALID_AGENT_MODELS = ("opus", "sonnet", "haiku", "fable", "inherit")
_VALID_EFFORTS = ("low", "medium", "high", "xhigh", "max")


def agent_frontmatter_issues(text):
    """Advisory problems in an agent .md frontmatter, per .claude/KIT.md §1. Returns
    a list of human-readable strings (empty = clean)."""
    fm = parse_frontmatter(text)
    if not fm:
        return ["no YAML frontmatter found"]
    issues = []
    mv = (fm.get("model") or "").strip()
    if not mv:
        issues.append("missing `model:` (KIT §1 — every agent declares opus or sonnet)")
    else:
        if not (mv in _VALID_AGENT_MODELS or mv.startswith("claude-")):
            issues.append("invalid `model:` '{}' (opus/sonnet/haiku/fable/inherit or a full id)".format(mv))
        if re.match(r"claude-3", mv):
            issues.append("stale model id '{}' (use the `opus`/`sonnet` alias)".format(mv))
    ev = (fm.get("effort") or "").strip()
    if not ev:
        issues.append("missing `effort:` (KIT §1 — low/medium/high/xhigh/max)")
    elif ev not in _VALID_EFFORTS:
        issues.append("invalid `effort:` '{}'".format(ev))
    if "allowed-tools" in fm:
        issues.append("agents use `tools:`, not `allowed-tools:` (that is a skill field)")
    return issues


def skill_frontmatter_issues(text):
    """Advisory problems in a SKILL.md frontmatter, per .claude/KIT.md §0/§5."""
    fm = parse_frontmatter(text)
    if not fm:
        return ["no YAML frontmatter found"]
    issues = []
    if "tools" in fm and "allowed-tools" not in fm:
        issues.append("skills use `allowed-tools:`, not `tools:`")
    if len((fm.get("description") or "").strip()) < 12:
        issues.append("missing/short `description:` (lead with a when-to-use trigger)")
    return issues


_HOOK_BLOCK = re.compile(
    r"sys\.exit\(\s*2\s*\)"
    r"|(?<![\w.])exit\(\s*2\s*\)"
    r'|["\']decision["\']\s*:\s*["\']block["\']'
    r'|["\']permissionDecision["\']\s*:\s*["\'](?:deny|ask)["\']'
)


def hook_can_block(text):
    """True if hook source could BLOCK a tool/stop (violates the advisory-only rule).
    Scans raw text: the block signatures (`{"decision": "block"}`, a deny/ask
    `permissionDecision`) live inside string literals, so stripping strings would hide
    them. `sys.exit(2)` is matched as code. A rare false positive on a comment that
    literally writes `sys.exit(2)` is acceptable for an advisory nudge."""
    return bool(_HOOK_BLOCK.search(text or ""))
