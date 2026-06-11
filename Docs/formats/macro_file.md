# Martial Heroes Client Macro File Format (`.mhm`)

**Status:** New format — this is the Martial Heroes reconstruction project's own design.
It is NOT a reverse-engineered legacy format. No offsets or magic numbers are derived
from the original binary.

**File extension:** `.mhm` (Martial Heroes Macro)

---

## Overview

A `.mhm` file is a UTF-8, line-oriented text file that defines one or more named macros.
Each macro declares a trigger key and an ordered list of action command lines. Players
hand-edit these files; the parser is intentionally permissive about whitespace and
blank lines.

---

## Grammar (BNF-style)

```
file         ::= { blank-or-comment | macro-block }
macro-block  ::= header-line { blank-or-comment | command-line }
header-line  ::= "[" macro-name "]" [ whitespace trigger-key ]
macro-name   ::= non-bracket-chars+           ; no "[" or "]" allowed inside name
trigger-key  ::= non-whitespace-chars+        ; e.g. "F5", "Ctrl+1", "None"
command-line ::= non-comment non-blank text   ; any line not starting with "#" or "["
blank-or-comment ::= empty-line | comment-line
comment-line ::= "#" rest-of-line
```

---

## Parsing Rules

1. **Encoding:** Files MUST be UTF-8. A BOM (U+FEFF) at the start is silently skipped.
2. **Line endings:** CR+LF and LF are both accepted. CR-only is not supported.
3. **Comment lines:** Any line whose first non-whitespace character is `#` is ignored.
4. **Blank lines:** Lines that are empty or contain only whitespace are ignored.
   They may appear freely between macros or inside a macro's command list.
5. **Macro header:** A line matching `[name]` (optionally followed by a trigger key)
   starts a new macro block. The name is trimmed of surrounding whitespace.
   - If a trigger key token follows (separated by one or more spaces/tabs), it is
     recorded as `TriggerKey`. The special token `None` is stored as-is; the
     Application layer interprets it.
   - If no trigger key token is present, `TriggerKey` is `null`.
6. **Command lines:** Every non-blank, non-comment line that appears after a header
   and before the next header (or end-of-file) is appended to the current macro's
   command list. Leading and trailing whitespace is trimmed.
7. **Duplicate macro names:** If the same name appears more than once, the second
   occurrence replaces the first (last-definition wins).
8. **Empty command lists** are legal; a macro may declare just a name and key.

---

## Example

```
# My keybinds - last edited 2026-06-11

[SkillAttack] F1
UseSkill 101
PlayAnimation attack_heavy

[OpenInventory] I
ToggleWindow Inventory

[Greeting] None
# This macro has no hotkey.
EmoteChat /wave
EmoteChat /bow

[EmptyMacro] F10
# No commands here yet.
```

Parsed result:

| Name            | TriggerKey | Commands                                      |
|-----------------|-----------|-----------------------------------------------|
| SkillAttack     | F1        | ["UseSkill 101", "PlayAnimation attack_heavy"] |
| OpenInventory   | I         | ["ToggleWindow Inventory"]                    |
| Greeting        | None      | ["EmoteChat /wave", "EmoteChat /bow"]         |
| EmptyMacro      | F10       | []                                            |

---

## Parser implementation reference

`MartialHeroes.Client.Infrastructure.Macros.MacroFileParser`
Interface: `MartialHeroes.Client.Infrastructure.Macros.IMacroFileParser`
