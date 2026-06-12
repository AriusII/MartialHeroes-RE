#!/usr/bin/env python3
"""Probe the Martial Heroes 'godot' MCP bridge (editor + game ports).

Stdlib only (socket, sys, argparse). TCP-connects to the two bridge ports exposed
by the slangwald/godot-mcp mcp_bridge editor plugin:

    9600  editor bridge  — up whenever the Godot editor is open with the plugin enabled
    9601  game  bridge   — up only while a game is actually RUNNING (run_project)

A successful TCP connect is enough to tell "the bridge is listening" from "nothing is
there". The actual mcp__godot__* tool calls are made by Claude Code, not this script.

Exit codes:
    0  editor bridge (9600) is UP  (the precondition for any editor/scene work)
    1  editor bridge (9600) is DOWN

Usage:
    python check_godot.py
    python check_godot.py --host 127.0.0.1 --editor-port 9600 --game-port 9601 --timeout 2.0
"""

import argparse
import socket
import sys

DEFAULT_HOST = "127.0.0.1"
DEFAULT_EDITOR_PORT = 9600
DEFAULT_GAME_PORT = 9601
DEFAULT_TIMEOUT = 2.0

PROJECT = "C:/Users/Arius/RiderProjects/MartialHeroes/05.Presentation/MartialHeroes.Client.Godot"
ADD_COMMAND = (
    "claude mcp add godot -- uv run --directory "
    "C:/Users/Arius/godot-mcp/mcp python godot_mcp_server.py"
)


def probe(host: str, port: int, timeout: float) -> tuple[bool, str]:
    """Attempt a TCP connect. Return (is_up, detail)."""
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True, "connection accepted"
    except ConnectionRefusedError as exc:
        return False, f"connection refused ({exc})"
    except socket.timeout:
        return False, f"timed out after {timeout:g}s"
    except OSError as exc:
        return False, f"socket error ({exc})"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Probe the Martial Heroes Godot MCP bridge (editor + game ports).")
    parser.add_argument("--host", default=DEFAULT_HOST, help="bridge host (default %(default)s)")
    parser.add_argument("--editor-port", type=int, default=DEFAULT_EDITOR_PORT,
                        help="editor bridge port (default %(default)s)")
    parser.add_argument("--game-port", type=int, default=DEFAULT_GAME_PORT,
                        help="game bridge port (default %(default)s)")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT,
                        help="connect timeout in seconds (default %(default)s)")
    args = parser.parse_args(argv)

    editor_up, editor_detail = probe(args.host, args.editor_port, args.timeout)
    game_up, game_detail = probe(args.host, args.game_port, args.timeout)

    print(f"GODOT EDITOR BRIDGE: {'UP' if editor_up else 'DOWN'} "
          f"({args.host}:{args.editor_port}) — {editor_detail}")
    print(f"GODOT GAME   BRIDGE: {'UP' if game_up else 'DOWN'} "
          f"({args.host}:{args.game_port}) — {game_detail}")

    if editor_up:
        print("")
        print("Editor bridge ready. Next: enumerate the live mcp__godot__* tools, confirm the open")
        print("project is the Martial Heroes World scene, then proceed.")
        if not game_up:
            print("(Game bridge DOWN is normal while idle — it only listens while a game is running;")
            print(" call run_project first if you need the game tools / a runtime screenshot.)")
        return 0

    print("")
    print("Do NOT proceed with scene/runtime work. Fix one of the following:")
    print(f"  1. Open the Godot 4.6.3-mono editor on:\n       {PROJECT}")
    print("     and confirm the 'mcp_bridge' editor plugin is enabled (ports 9600/9601).")
    print("  2. If the editor is open but mcp__godot__* tools are missing from this session,")
    print("     the MCP server is not registered/loaded. (Re)add it:")
    print(f"       {ADD_COMMAND}")
    print("     then RESTART the Claude Code session (MCP servers load at startup), and ensure")
    print("     'uv' is on PATH and the godot-mcp directory exists.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
