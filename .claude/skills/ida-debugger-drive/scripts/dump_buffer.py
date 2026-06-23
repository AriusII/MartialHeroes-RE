# dump_buffer.py — one-shot "read a buffer at a live register pointer" for the ida-debugger-drive
# skill. Use when a breakpoint is HIT (the process is stopped) to read N bytes at [REG]+OFFSET and
# emit a hex/byte summary — the cipher pre/post-image read and the struct-at-pointer read, without
# hand-writing the dbg_read each time.
#
# RUN THIS INSIDE IDA PRO 9.3, via the IDA MCP script-execution tool (e.g.
# mcp__ida__execute_script / run_python / py_eval), while a LIVE debug session the maintainer
# F9-launched is STOPPED at a breakpoint. It imports ida_dbg/ida_idd/ida_bytes which only exist
# inside IDA — it does NOT run under a plain CPython, and it does NOT start a session.
#
# HOW TO USE
#   1. Set REG / OFFSET / LENGTH / SLUG in the CONFIG block.
#   2. Run it once at the break; it prints one line prefixed "RESULT_JSON:" and best-effort-writes
#      to Docs/RE/_dirty/dbg/. To capture a cipher transform, run it BEFORE the transform, step
#      over the transform (dbg_step_over), then run it AGAIN — the byte-diff is the ground truth.
#
# DIRTY: every byte read here is runtime data from the copyrighted binary. It belongs ONLY under
# Docs/RE/_dirty/dbg/ and must never be committed or copied into clean specs / C#. Treat any login
# credential bytes as SESSION-ONLY — redact them; never record them.
#
# This script READS ONLY. It never dbg_write()s, never patches, never starts/kills the session.

# === CONFIG ===
REG = "eax"               # general register holding the buffer/object POINTER (e.g. eax/esi/edi/ecx)
OFFSET = 0                # byte offset added to the pointer in REG before reading
LENGTH = 64               # number of bytes to read (keep small — a header/buffer slice, not a dump)
SLUG = "buffer"           # short descriptive name -> dump.<slug>.json (e.g. recv_pre_cipher)
DIRECT_EA = None          # optional: read at this absolute EA instead of [REG]+OFFSET (None = use REG)
OUT_DIR = r"Docs\RE\_dirty\dbg"
# ==============

import json
import datetime

try:
    import ida_dbg
    import ida_idd
    import ida_bytes
    import ida_nalt
except ImportError as exc:
    raise SystemExit("dump_buffer.py must run inside IDA Pro (IDAPython): %s" % exc)


def _session_is_live():
    """A debugger session is live iff a process is being debugged. We never start one."""
    try:
        return ida_dbg.get_process_state() != ida_dbg.DSTATE_NOTASK
    except Exception:
        # Older API fallback: dbg_get_registers only works mid-session.
        return True


def _read_register(name):
    """Read one general register from the stopped session, or None if unavailable."""
    try:
        rv = ida_idd.regval_t()
        if ida_dbg.get_reg_val(name, rv):
            return int(rv.ival) & 0xFFFFFFFFFFFFFFFF
    except Exception:
        pass
    return None


def main():
    result = {
        "schema": "ida-debugger-drive/dump_buffer/1",
        "slug": SLUG,
        "ok": True,
        "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "binary": ida_nalt.get_root_filename() or "",
        "reg": REG,
        "offset": OFFSET,
        "length": LENGTH,
    }
    try:
        # Refuse to run unless a session is actually live — this skill never starts one.
        if not _session_is_live():
            result["ok"] = False
            result["error"] = "NO_LIVE_SESSION: ask the maintainer to F9-launch the client; never dbg_start."
            print("RESULT_JSON:" + json.dumps(result, ensure_ascii=False))
            return

        if DIRECT_EA is not None:
            base = int(DIRECT_EA)
            result["source"] = "direct_ea"
        else:
            ptr = _read_register(REG)
            if ptr is None:
                result["ok"] = False
                result["error"] = "REG_UNREADABLE: '%s' not available — is the process stopped at a break?" % REG
                print("RESULT_JSON:" + json.dumps(result, ensure_ascii=False))
                return
            base = ptr
            result["source"] = "register"
            result["reg_value"] = "0x%08X" % base

        ea = (base + OFFSET) & 0xFFFFFFFFFFFFFFFF
        result["read_ea"] = "0x%08X" % ea

        # dbg_read_memory reads THROUGH PAGE_NOACCESS in a live session — the whole point of
        # reading a post-build no-access packet buffer here rather than statically.
        raw = ida_dbg.dbg_read_memory(ea, LENGTH)
        if raw is None:
            result["ok"] = False
            result["error"] = "READ_FAILED: dbg_read_memory returned nothing at %s" % result["read_ea"]
        else:
            b = bytes(raw)
            result["bytes_read"] = len(b)
            result["hex"] = b.hex()
            # ASCII gloss for spotting opcodes/markers; control bytes shown as '.'.
            result["ascii"] = "".join(chr(c) if 32 <= c < 127 else "." for c in b)
    except Exception as exc:
        result["ok"] = False
        result["error"] = "%s: %s" % (type(exc).__name__, exc)

    print("RESULT_JSON:" + json.dumps(result, ensure_ascii=False))

    # Best-effort dirty-only write so the analyst keeps a file even if stdout capture is lossy.
    try:
        import os
        import re
        os.makedirs(OUT_DIR, exist_ok=True)
        slug = re.sub(r"[^A-Za-z0-9_]+", "_", SLUG)[:60] or "buffer"
        path = os.path.join(OUT_DIR, "dump.%s.json" % slug)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
        print("[dump_buffer] wrote %s  (DIRTY — never commit; redact any credential bytes)" % path)
    except Exception as exc:
        print("[dump_buffer] could not write file (%s); save the RESULT_JSON above via Write." % exc)


main()
