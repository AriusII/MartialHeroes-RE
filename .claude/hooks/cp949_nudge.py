#!/usr/bin/env python
"""PostToolUse(Write|Edit|MultiEdit) hook — remind Assets.Parsers code to decode legacy text
as CP949 (code page 949), not the platform default.

Advisory only; never blocks. Every line of game text in the Martial Heroes client (.txt/.csv
tables, .scr scripts, embedded strings) is Korean encoded in CP949. On .NET that code page is
not registered by default, so a `GetString`/`GetEncoding(949)` call throws unless the program
has called `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` once at startup.
This nudge fires when a parser file appears to read/decode such text without the CP949 signal.
"""
import _hooklib as h


def main():
    ev = h.read_event()
    path = h.file_path(ev)
    if not h.is_parser_cs(path):
        h.ok()
        return

    # NOTE: pass the RAW added text here, not the comment/string-stripped form. The table
    # filenames the detector keys on (".txt"/".csv"/".scr") live inside C# string literals,
    # which strip_comments_strings deliberately removes — stripping would blind this check.
    raw = h.added_text(ev)
    if not raw.strip():
        h.ok()
        return

    # h.mentions_korean_or_txt_read returns True only when the code looks like it decodes
    # text (a .txt/.csv/.scr read or a GetString call) AND lacks the CP949 provider/encoding.
    if h.mentions_korean_or_txt_read(raw):
        h.system_message(
            "ℹ CP949: all Martial Heroes game text is Korean in code page 949. Register the "
            "provider once (`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`) and "
            "decode with `Encoding.GetEncoding(949)` — the .NET default will mangle the bytes. "
            "Advisory only."
        )
        return
    h.ok()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        h.fail_open(exc)
