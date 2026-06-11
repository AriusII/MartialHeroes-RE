#!/usr/bin/env python
"""PostToolUse(mcp__ida__*) hook — append a provenance record for every IDA MCP call.

Log-only, never blocks. Stores a SHA-256 DIGEST of any returned pseudo-code, never the
pseudo-code itself — recording the decompiler output would defeat the clean-room firewall.
This local journal backs the EU Art. 6 "analysis performed for interoperability" trail.
"""
import datetime
import hashlib
import json
import _hooklib as h


def _digest(obj):
    try:
        if obj is None:
            return None
        s = obj if isinstance(obj, str) else json.dumps(obj, ensure_ascii=False, sort_keys=True)
        return "sha256:" + hashlib.sha256(s.encode("utf-8", "replace")).hexdigest()[:24]
    except Exception:
        return None


def _hint(ti):
    if not isinstance(ti, dict):
        return None
    for k in ("name", "function_name", "function", "address", "ea", "offset", "symbol"):
        if k in ti and ti[k] not in (None, ""):
            return {k: ti[k]}
    return None


def main():
    ev = h.read_event()
    pdir = h.project_dir(ev)
    record = {
        "ts": datetime.datetime.now().isoformat(timespec="seconds"),
        "session": ev.get("session_id", ""),
        "tool": h.tool_name(ev),
        "target": _hint(h.tool_input(ev)),
        "response_digest": _digest(ev.get("tool_response")),
    }
    h.append_jsonl(pdir, "re_journal.jsonl", record)
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
