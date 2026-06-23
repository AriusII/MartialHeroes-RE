#!/usr/bin/env python3
"""Probe the local IDA Pro 9.3 MCP server for the Martial Heroes RE workflow.

Stdlib only (socket, sys, argparse). Opens a TCP connection to the MCP listener
and reports whether it is reachable. Does NOT speak the MCP/HTTP protocol — a
successful TCP connect is sufficient to distinguish "IDA is up and listening"
from "nothing is there". The actual mcp__ida__* tool calls are made by Claude
Code, not by this script.

Exit codes:
    0  server is UP (socket accepted the connection)
    1  server is DOWN (connection refused / timed out / unreachable)

Usage:
    python check_ida.py
    python check_ida.py --host 127.0.0.1 --port 13337 --timeout 2.0
"""

import argparse
import socket
import sys

DEFAULT_HOST = "127.0.0.1"
DEFAULT_PORT = 13337
DEFAULT_TIMEOUT = 2.0

# Printed verbatim when the server is unreachable.
ADD_COMMAND = "claude mcp add --transport http ida http://127.0.0.1:13337/mcp"


def probe(host: str, port: int, timeout: float) -> tuple[bool, str]:
    """Attempt a TCP connect. Return (is_up, detail)."""
    try:
        with socket.create_connection((host, port), timeout=timeout):
            return True, "connection accepted"
    except (ConnectionRefusedError,) as exc:
        return False, f"connection refused ({exc})"
    except socket.timeout:
        return False, f"timed out after {timeout:g}s"
    except OSError as exc:
        return False, f"socket error ({exc})"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Probe the IDA Pro MCP server for the Martial Heroes RE workflow.")
    parser.add_argument("--host", default=DEFAULT_HOST, help="MCP host (default %(default)s)")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT,
                        help="MCP port (default %(default)s)")
    parser.add_argument("--timeout", type=float, default=DEFAULT_TIMEOUT,
                        help="connect timeout in seconds (default %(default)s)")
    args = parser.parse_args(argv)

    endpoint = f"{args.host}:{args.port}"
    is_up, detail = probe(args.host, args.port, args.timeout)

    if is_up:
        print(f"IDA MCP: UP   ({endpoint}) — {detail}")
        print("Next: confirm a database is loaded and enumerate the mcp__ida__* tools,")
        print("then proceed with the requested RE workflow. Treat all IDA output as DIRTY.")
        return 0

    print(f"IDA MCP: DOWN ({endpoint}) — {detail}")
    print("")
    print("Do NOT proceed with analysis. Fix one of the following:")
    print("  1. Open IDA Pro 9.3 with the Martial Heroes database (Main.exe.i64) loaded")
    print("     and ensure its MCP/server plugin is started (it only listens while a DB is open).")
    print("  2. If IDA is open but mcp__ida__* tools are missing, register the server with:")
    print(f"       {ADD_COMMAND}")
    print("     then restart the Claude Code session.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
