#!/usr/bin/env python
"""extract_streams.py -- stdlib wrapper around the canonical tshark invocation.

Turns a Martial Heroes .pcapng capture into per-TCP-stream .tsv extracts. The exact tshark
command this runs is version-controlled in ../references/tshark.md -- keep the two in sync.

The capture is a protocol ORACLE (observed wire bytes), not copyrighted code, so this is
clean-room-safe. The .pcapng input and every .tsv output are gitignored and regenerable;
nothing produced here is ever committed.

Outputs (into --out-dir):
  streams_all.tsv      every payload-bearing segment, with a header + derived `dir` column
  stream_<N>.tsv       one file per tcp.stream index, same schema

Columns: frame.time_epoch  tcp.stream  ip.src  tcp.srcport  tcp.len  data.data  dir

Stdlib only (subprocess/argparse/csv/os/sys). No pip. Requires `tshark` on PATH.
"""
from __future__ import annotations

import argparse
import csv
import os
import subprocess
import sys

# Field order MUST match references/tshark.md exactly.
FIELDS = [
    "frame.time_epoch",
    "tcp.stream",
    "ip.src",
    "tcp.srcport",
    "tcp.len",
    "data.data",
]
HEADER = FIELDS + ["dir"]

# Column indices for readability.
I_STREAM = FIELDS.index("tcp.stream")
I_SRCIP = FIELDS.index("ip.src")
I_SRCPORT = FIELDS.index("tcp.srcport")


def build_tshark_cmd(capture: str, stream: int | None) -> list[str]:
    """Construct the canonical tshark command. See references/tshark.md for the rationale
    behind every flag; this MUST stay byte-for-byte equivalent to that doc."""
    display_filter = "tcp.len > 0"
    if stream is not None:
        display_filter += f" and tcp.stream == {stream}"
    cmd = [
        "tshark",
        "-r", capture,
        "-Y", display_filter,
        "-T", "fields",
        "-E", "separator=/t",
        "-E", "occurrence=f",
    ]
    for f in FIELDS:
        cmd += ["-e", f]
    return cmd


def run_tshark(cmd: list[str]) -> list[list[str]]:
    """Run tshark and return rows split on tab. Fails loudly if tshark is missing."""
    try:
        proc = subprocess.run(
            cmd, check=True, capture_output=True, text=True, encoding="utf-8"
        )
    except FileNotFoundError:
        sys.exit(
            "error: 'tshark' not found on PATH. Install Wireshark/tshark and retry. "
            "(See the pcap-extract SKILL.md.)"
        )
    except subprocess.CalledProcessError as exc:
        sys.exit(f"error: tshark failed (exit {exc.returncode}):\n{exc.stderr.strip()}")

    rows: list[list[str]] = []
    for line in proc.stdout.splitlines():
        if not line:
            continue
        cols = line.split("\t")
        # Pad/truncate to the expected width so a missing trailing field is just empty.
        if len(cols) < len(FIELDS):
            cols += [""] * (len(FIELDS) - len(cols))
        elif len(cols) > len(FIELDS):
            cols = cols[: len(FIELDS)]
        rows.append(cols)
    return rows


def derive_dir(row: list[str], server_ip: str | None, server_port: str | None) -> str:
    """Direction from the CLIENT's point of view (matches Docs/RE/opcodes.md):
    a segment whose SOURCE is the server is S2C; otherwise C2S. '?' if undeterminable."""
    if server_port is not None and row[I_SRCPORT] == server_port:
        return "S2C"
    if server_ip is not None and row[I_SRCIP] == server_ip:
        return "S2C"
    if server_port is not None or server_ip is not None:
        return "C2S"
    return "?"


def write_tsv(path: str, rows: list[list[str]]) -> None:
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh, delimiter="\t", lineterminator="\n")
        w.writerow(HEADER)
        w.writerows(rows)


def print_endpoint_hint(rows: list[list[str]]) -> None:
    """When no server endpoint was given, list distinct (ip.src, tcp.srcport) pairs so the
    user can identify the server (the endpoint every stream talks to) and re-run with a tag."""
    pairs: dict[tuple[str, str], int] = {}
    for r in rows:
        key = (r[I_SRCIP], r[I_SRCPORT])
        pairs[key] = pairs.get(key, 0) + 1
    print("\nno --server-ip/--server-port given; 'dir' left as '?'.")
    print("distinct (ip.src, tcp.srcport) endpoints seen (segments):")
    for (ip, port), n in sorted(pairs.items(), key=lambda kv: -kv[1]):
        print(f"  {ip:>15}:{port:<6}  {n} segments")
    print("re-run with e.g. --server-port <port> (or --server-ip <ip>) to tag C2S/S2C.")


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Extract Martial Heroes TCP streams from a .pcapng into per-stream .tsv."
    )
    ap.add_argument("--in", dest="capture", required=True, help="path to the .pcapng (gitignored)")
    ap.add_argument(
        "--out-dir",
        default="_dirty/captures",
        help="output dir for .tsv files (gitignored; default: _dirty/captures)",
    )
    ap.add_argument("--stream", type=int, default=None, help="extract only this tcp.stream index")
    ap.add_argument("--server-ip", default=None, help="server IP -> its segments are S2C")
    ap.add_argument("--server-port", default=None, help="server TCP port -> its segments are S2C")
    args = ap.parse_args()

    if not os.path.isfile(args.capture):
        sys.exit(f"error: capture not found: {args.capture}")

    cmd = build_tshark_cmd(args.capture, args.stream)
    print("running:", " ".join(cmd))
    rows = run_tshark(cmd)
    if not rows:
        sys.exit("no payload-bearing TCP segments matched. Check the filter / capture.")

    for r in rows:
        r.append(derive_dir(r, args.server_ip, args.server_port))

    os.makedirs(args.out_dir, exist_ok=True)

    all_path = os.path.join(args.out_dir, "streams_all.tsv")
    write_tsv(all_path, rows)

    by_stream: dict[str, list[list[str]]] = {}
    for r in rows:
        by_stream.setdefault(r[I_STREAM], []).append(r)
    for sid, srows in sorted(by_stream.items(), key=lambda kv: int(kv[0] or -1)):
        write_tsv(os.path.join(args.out_dir, f"stream_{sid}.tsv"), srows)

    print(f"\nwrote {all_path}  ({len(rows)} segments)")
    for sid, srows in sorted(by_stream.items(), key=lambda kv: int(kv[0] or -1)):
        tagged = {"C2S": 0, "S2C": 0, "?": 0}
        for r in srows:
            tagged[r[-1]] = tagged.get(r[-1], 0) + 1
        print(
            f"  stream_{sid}.tsv  {len(srows):>6} segments  "
            f"(C2S={tagged['C2S']} S2C={tagged['S2C']} ?={tagged['?']})"
        )

    if args.server_ip is None and args.server_port is None:
        print_endpoint_hint(rows)

    print("\nreminder: .pcapng and .tsv are gitignored and regenerable -- do not commit them.")


if __name__ == "__main__":
    main()
