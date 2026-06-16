#!/usr/bin/env python
"""SessionStart hook — inject a terse orientation banner into the session context.

Advisory only. Surfaces branch/dirty state, IDA MCP reachability, capture presence,
remaining placeholder count, and the clean-room + French-language reminders.
"""
import _hooklib as h


def main():
    ev = h.read_event()
    pdir = h.project_dir(ev)

    branch = h.git_branch(pdir)
    dirty = h.git_dirty_count(pdir)
    dirty_txt = "clean" if dirty == 0 else ("{} uncommitted change(s)".format(dirty) if dirty > 0 else "unknown")
    pcap, tsv = h.find_captures(pdir)
    placeholders = h.count_placeholders(pdir)

    lines = [
        "MartialHeroes — clean-room preservation project (D.O. Online, 2004–2008). MISSION: total RE of the ENTIRE doida.exe client -> faithful 1:1 re-creation on Godot 4.6.3.",
        "Git: {} ({}).".format(branch, dirty_txt),
        h.ida_status_line(),
        "IDA reverse runs UNBRIDLED: fan out read analysts AND IDB writers massively in parallel — no ~3 cap, no one-writer rule; retry failed/conflicting calls (only the live MCP server's throughput limits you).",
        "Captures: {} .pcapng / {} .tsv present locally (protocol oracle; never read raw bytes into context, never commit).".format(pcap, tsv),
        "Skeleton: {} Class1.cs placeholder(s) still present (12 = nothing implemented yet).".format(placeholders),
        h.CLEAN_ROOM_BLURB,
        "RE knowledge lives in Docs/RE/ (firewall: _dirty/ is tainted & gitignored; specs are clean). Skills: /re-workspace-init, /ida-mcp-connect, /pcap-extract. Agents via @ (re-*, *-engineer, *-author, *-guardian).",
        "User prefers French for conversation; repo artifacts stay in English.",
    ]
    h.additional_context("SessionStart", "\n".join(lines))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # never crash a session start
        h.fail_open(exc)
