# Macro file format — `.mhm` (Martial Heroes Macro)

> **Project-owned format.** This is the clean-room client's own user-facing macro/keybind
> format — it is NOT reverse-engineered from the original `doida.exe`. No binary offsets or
> magic constants from the legacy client appear here. The format is defined by this project
> and is forward-compatible: new rule sections may be appended without breaking older files.
>
> ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> ida_reverified: 2026-06-27 — CYCLE 14 re-anchor review: project-owned format (.mhm); outside IDA ground-truth scope; no binary artifact exists to confirm or refute; unchanged by definition

---

## Overview

`.mhm` files are UTF-8 plain-text files that define a list of named macro blocks. Each block
specifies a macro name, an optional trigger key, and an ordered list of action commands.
They are the user's own local content and are never packed into game assets.

---

## Parsing Rules

1. **BOM stripping.** A leading UTF-8 byte-order mark (`U+FEFF`) is silently stripped before
   any other processing. Files may be saved with or without a BOM; both forms are valid.

2. **Line endings.** Both `CR+LF` (Windows) and `LF` (Unix) line endings are accepted. A lone
   `CR` is treated as part of the preceding line, not a line break.

3. **Blank lines.** Lines that are empty after trimming leading and trailing whitespace are
   silently ignored at every nesting level (outside and inside a block).

4. **Comment lines.** Lines whose first non-whitespace character is `#` are comment lines and
   are silently ignored. Comments may appear at file level or inside a macro block; they are
   never part of the command list.

5. **Block header.** A line whose first non-whitespace character is `[` opens a new macro
   block. The syntax is:
   ```
   [MacroName] OptionalTriggerKey
   ```
   - `MacroName` is the text between `[` and `]`, trimmed of leading and trailing whitespace.
     Names are case-sensitive.
   - `OptionalTriggerKey` is the remaining text after `]`, trimmed of leading and trailing
     whitespace. If the text after `]` is empty (or absent), the trigger key is `null` (unbound).
   - If the `]` is missing or appears at position 0 (i.e. `[]`), the entire remainder of the
     line after `[` is used as the macro name and the trigger key is `null`.
   - Opening a new block header implicitly closes the previous block (see rule 7).

6. **Command lines.** Any line inside a block that is not blank and not a comment is a command
   line. Command lines are trimmed of leading and trailing whitespace and appended to the
   current block's command list in order.

7. **Duplicate macro names — last-definition wins.** If the same `MacroName` appears in more
   than one block header, all definitions are processed in file order. The dictionary always
   holds the last-seen definition for any given name. The output ordering places each name at
   its **first-appearance position**; the block content (trigger key + commands) is taken from
   the **last definition** for that name.

8. **Lines before the first header.** Any non-blank, non-comment lines that appear before the
   first `[…]` header are silently ignored.

---

## Example

```
# Martial Heroes keybind file — personal layout

[SkillAttack] F1
UseSkill 101
PlayAnimation attack_heavy

[OpenInventory] I
ToggleWindow Inventory

[Greeting]
EmoteChat /wave
EmoteChat /bow
```

---

## Encoding

Files are written and read as **UTF-8** (optionally with a BOM; see rule 1). This is the
project's own text format and has no legacy encoding constraint.

---

## File extension

`.mhm` (Martial Heroes Macro). The extension is conventional; the parser accepts any path.
