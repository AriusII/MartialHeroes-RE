---
status: confirmed
sample_verified: partial   # game.ver version-token sample-verified (login_flow.md); all UI flow/constants are CODE-CONFIRMED; wire bytes capture-unverified
subsystems: [login_scene, server_select, character_select, enter_world, frontend_state_flow]
networked: partial         # the UI flow here is client-local; the wire shapes it triggers are owned by login_flow.md / opcodes.md / packets
encoding_note: All account, server, character-name, dialog and label text is CP949 (legacy MS949 code page), not UTF-8.
---

# Front-End Scenes — Clean-Room Specification

> Neutral, rewritten behavioural specification, promoted from dirty-room analyst notes under
> **EU Software Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve
> interoperability). It contains **no decompiler output, no pseudo-code, no legacy symbol names,
> and no binary virtual addresses**. Struct field offsets (`+0x..` inside an object/record) and
> file offsets are retained because they are interoperability facts, not code addresses. Every
> behaviour and constant below is re-expressed in this author's own words and tables.
>
> **Scope.** The complete front-end (out-of-world) scene flow at the **UI / control / flow level**:
> the login form, server selection, character selection / creation / deletion / rename, and the
> enter-world handoff. This is the connective tissue between the screens — what each widget does,
> what each local validation rule is, what message-catalogue dialog id each outcome shows, and
> which engine-state transition each step drives.
>
> **Cross-spec ownership — this spec never re-derives wire bytes.** Where a topic already has a
> committed spec, that spec is the authority and this file only summarises and cross-links:
> - **Wire packet shapes & opcodes** (login blob, char list, enter-game, create/select/rename/
>   delete, char-spawn): `opcodes.md` + `packets/*.yaml` + `specs/login_flow.md`. This spec cites
>   opcodes by their `major/minor` and canonical name but **defines none** and **edits neither**.
> - **The lobby mini-protocol** (synchronous port-10000 server-list & channel-endpoint fetch, the
>   8-byte frame wrapper, inbound LZ4, the 8-byte server record decode): `specs/login_flow.md`
>   §2. This spec references it as a black box (start → wait → consume) and does not re-decode it.
> - **The engine/scene state machine** (the 0..8 engine states, the loop run-flag mechanism, the
>   construct/destroy ledger per transition): `specs/client_runtime.md` §7. **All engine-state
>   numbers used here (1 Login, 2 Load, 3 Opening, 4 Select, 5 In-game, 6 Quit, 7 Error, 8 Exit)
>   are defined there**; this spec only states which transition each front-end action drives.
> - **The widget tree / atlas layout / fonts / IME charset**: `specs/ui_system.md` §2, §3, §6.
>   This spec adds the **action-dispatch and flow layer** that `ui_system.md` leaves open, and
>   **corrects** two of its sub-state labels (see the conflict note in §1.5 and §6).
> - **Camera for the select preview**: `specs/client_runtime.md` §7.3.
> - **Skin/bind/motion asset chains the preview reuses**: `specs/skinning.md`, `formats/mesh.md`.
> - **Sound ids**: `specs/sound_runtime.md` (referenced by id only).
>
> The message-catalogue (`msg.xdb`) **caption strings** are in the client VFS, are CP949, and are
> **not** reproduced here. Only the numeric message **ids** are recorded — they are
> interoperability facts; the localized text behind them must be supplied from a VFS extract.

---

## Status and verification banner

Per-claim confidence model (used inline below), matching the repo's vocabulary:

- **CODE-CONFIRMED** — read directly from the binary's control flow / immediate operands; the
  default tier for every flow rule, widget action, validation threshold, message id, state
  transition, and constant in this spec.
- **SAMPLE-VERIFIED** — additionally byte-checked against a real shipped sample. Only the
  enter-game version token (computed from the on-disk `data/cursor/game.ver`) reaches this tier
  here; see `specs/login_flow.md` §3.3.
- **CAPTURE-VERIFIED** — confirmed against a network capture. **No capture was available** to any
  source lane; every wire-direction claim below is therefore the cross-referenced static read from
  `login_flow.md`, **not** capture-verified.
- **PLAUSIBLE** — single-source inference; implement but keep tunable / behind a flag. Flagged
  inline and re-listed in **Open questions**.

> **Whole-spec caveat.** The numeric ids, lengths, thresholds, and transitions here are reliable as
> *values present in the legacy code*. The screen flow itself is fully recovered; the **packet bytes
> these screens trigger remain capture-unverified** and are owned by `login_flow.md`.

---

# 1. The login scene

**Engine state: 1 (Login)** — see `client_runtime.md` §7.1. A single window object (the
"login window") owns the login form, server selection, and the channel-resolve handoff, all
in-process. It is built once on entering state 1, from the UI config script
(`data/script/uiconfig.lua`), and runs until it hands off to the game connection or quits.

The login window carries **two independent internal counters** that must not be confused:

- a **UI page index** (which form/option page is visible), and
- a **flow sub-state** (the connect → animate → validate → EULA → server-list → endpoint → submit
  machine). The flow sub-state is the load-bearing one for this spec; its values are catalogued in
  §1.5. (Both are *below* the engine state and are never visible as an engine-state value — a point
  already reconciled in `client_runtime.md` §7.1 and `ui_system.md` §6.)

## 1.1 Widget action dispatch — how a click reaches behaviour (CODE-CONFIRMED)

Each interactive widget is registered with a small **numeric action id** when it is parented into
the window. On a click that lands on an enabled widget and is released inside its bounds, the
window's command handler walks the active panel's child list, finds the hit child, and reads its
action id into a window-local field. The window's input handler then dispatches on that id.

A load-bearing legacy quirk: **the action id is the ASCII code of a letter**, and the input handler
switches on it *as a character*. The numbers below therefore look like ASCII codes; a fresh
implementation may use any enum and need not preserve the literal values. They are listed so an
engineer can map the legacy `uiconfig.lua` ids to behaviour.

The input handler also classifies events by a leading **event-class byte**:

| Event class | Meaning |
|---|---|
| 1 | keyboard / system action (id 9 = swap focused textbox; id 10 = "confirm/advance", i.e. Enter) |
| 6 | widget click-release (dispatch on the action-id character, per §1.2) |
| 7 | list pick (server-name strip selection) |
| 13 | engine event id `10001` = loading-complete signal → break the engine main loop |

## 1.2 Login widget → action table (CODE-CONFIRMED)

Widget geometry and atlas sources are owned by `ui_system.md`; reproduced minimally here only where
needed to identify a control. "Action id" is the legacy numeric id (= ASCII char shown in
parentheses). Decorative widgets (labels, banner slices, option backdrops) carry no action.

| Widget (role) | Action id | Behaviour on activate |
|---|---|---|
| **ID / account textbox** | input only | editable; first credential token (see §1.3) |
| **Password textbox** | input only | editable, masked; second credential token (see §1.3) |
| **OK / Login button** | 103 (`g`) | run the `game.ver` version gate (§1.4), then advance flow sub-state to **29** (credential validation). **Sends no packet itself.** |
| **Server-list button** | 102 (`f`) | reveal the server-list panel |
| **Save-ID checkbox** | 104 (`h`) | toggle; persist/clear the saved id (§1.6) |
| **Focus ID box** | 109 (`m`) | focus the ID box, clear PW focus (mutually exclusive) |
| **Focus PW box** | 110 (`n`) | focus the PW box, clear ID focus |
| **Quit-confirm "Yes" #1** | 113 (`q`) | hide quit popup; (re)start the server-list path (advance to sub-state 34) |
| **Quit-confirm "Yes" #2** | 114 (`r`) | hide quit popup; same as above |
| **Help button** | 105 (`i`) | throttled (~10 s) server-list re-fetch path (advance to sub-state 34) |
| **Option strip tab 1** | 111 (`o`) | option-page select |
| **Option strip tab 2** | 112 (`p`) | option-page select / sub-panel toggle |
| **Server name-strip buttons ×5** | 115..119 (range) | server entry select; active only when the server list is shown (sub-state 37). Covered in §2.4. |
| **Help-strip range** | 115..124 | contextual help-strip ids |

Keyboard/system class (event-class byte = 1): id **9** swaps the focused textbox (ID ↔ PW); id
**10** on the login form page runs the same logic as the OK button (version gate → sub-state 29),
i.e. **Enter = Login**; id **10** on the option page advances the option page. The action ids
`106` (`j`), `107`, `108` are registered on the panning intro-banner sub-buttons but their handler
cases are no-ops / page-advance only — the intro banner is decorative, not a control.

## 1.3 The ID and password editboxes (CODE-CONFIRMED)

Two text-entry boxes, both routed through the Korean IME composition (CP949):

| Box | IME composition slot | Max length | Render | Validation field |
|---|---|---|---|---|
| ID / account | 16 | **6** characters | plain | length read for the ID-length rule |
| Password | 12 | **129** characters | masked | length read for the empty-password rule |

- Focus is **mutually exclusive**: focusing one clears the other (actions `m`/`n`, or keyboard
  id 9). Korean composition routes to whichever box holds focus.
- The ID box max length of **6** and the PW box max length of **129** are genuine legacy constants.
  They are recorded as the original values; **a revival may relax both** (the validation only
  requires ID length ≥ 4 and password length ≥ 1, §1.4). Treat these caps as legacy trivia, not as
  protocol limits. *(PLAUSIBLE that 6 was a fixed-width legacy account id — Open question 2.)*
- On scene build, if a saved id is present (§1.6) and is not the literal `"(null)"`, the ID box is
  pre-filled with it (the Save-ID round-trip).

> **Wire-side capacity note (cross-reference, owned by `login_flow.md` §4.2).** These caps are the
> *UI editbox* limits, not the protocol limits. The login blob's runtime-confirmed capacities are
> account **< 20**, password **< 17** (staged in an exactly-17-byte zero-padded buffer), and the
> second-password / PIN **< 5** (≤ 4 chars). They live in `login_flow.md`; do not re-derive them
> here.

## 1.4 The Login click — local version gate then validation (CODE-CONFIRMED)

The OK/Login button (and the Enter key on the form page) run this exact sequence. **None of it is a
network send to the game server**; the only sends in the whole login scene are the lobby fetches and
the post-handoff handshake (§1.7).

1. **Version gate (local, runs first).** If the VFS is mounted, the client compares the version file
   inside the VFS (`data/cursor/game.ver`) against the on-disk `game.ver`.
   - On **mismatch**: a Win32 modal error box is shown using message id **2204**; pressing OK runs
     the quit-from-load path (plays SFX **861010106**, writes engine state **6 / substate 2**, i.e.
     quits the client). See §1.8.
   - On **match** (or if the VFS is not mounted): continue.
2. **Persist Save-ID** if the checkbox is set (§1.6).
3. Advance the flow sub-state to **29**.
4. **Next tick, sub-state 29 = local credential validation:**
   - If **ID length < 4** → show the in-window timed popup with message id **4025**, return to
     sub-state 6 (stay on the form). **No network send.**
   - Else if **password length < 1** (empty) → show message id **4026**, return to sub-state 6.
     **No network send.**
   - Else → persist Save-ID again, advance to sub-state **31** (the EULA/accept overlay).
5. The EULA → server-list → channel-endpoint → submit chain then runs (sub-states 31 → 41, §1.5).
   The actual account-login wire send and the cryptographic reply happen only at the **tail** of
   that chain (sub-state 40), on the main game connection — owned by `login_flow.md`. The
   **second-password / PIN** modal (§1.4a) is collected as part of this tail, before the login blob
   is built.

The version check is **inline in the Login handler only** — there is no separate startup version
popup, and no background patch dialog (patching is the external launcher's job; see
`client_runtime.md` §6 on the `-Start`/launcher gate).

## 1.4a The second-password / PIN modal (RUNTIME-CONFIRMED via `login_flow.md`)

After the primary account + password submit succeeds the credential validation (§1.4, sub-state 29),
and **before** the account-login blob is built and sent (sub-state 40), the client raises a dedicated
**second-password / PIN** modal — the legacy "secondary password" dialog. It collects a short,
typically numeric, PIN.

- **What it is.** A first-class third login input, separate from the account id and the account
  password. The client models the PIN as its own input concept (an input/name object carries an
  explicit "is-PIN" flag distinguishing it from an ordinary text field), which is why it is a
  distinct modal rather than a third box on the main form.
- **Where it sits in the flow.** It is shown **after** the primary login submit and **before** the
  credential-submit / handshake join point — i.e. between §1.4 (validation passes) and the §1.5
  sub-state-40 join handoff. Conceptually it is the same boundary `login_flow.md` §1 step 1a places
  the PIN at.
- **Capacity.** The PIN is **≤ 4 characters** (the login blob validates it as length `< 5`, i.e.
  ≤ 4 chars plus a NUL — `login_flow.md` §4.2 / §7). It is numeric in practice.
- **Where its value goes.** The PIN's value becomes the **optional length-prefixed field of the
  login blob** (`1/6 CmsgLoginRequest`): the blob is `[0x2B][u32len account\0]([u32len PIN\0])`.
  This is the field a prior reading called the "optional auxiliary string"; a runtime read of the
  live client identifies it as the PIN / second-password. **The PIN is *not* the account password**
  — the account password is staged separately as the RSA `1/4` plaintext and never appears in the
  blob. The byte layout, the u32-LE NUL-inclusive length prefix, and the field capacities are all
  owned by `login_flow.md` §4.2; this spec owns only the UI modal and its place in the flow.
- **Asset.** The dialog uses the secondary-password art catalogued in `formats/ui_manifests.md`
  (`data/ui/password.dds`, 1024×1024 DXT3 — listed there as "Secondary password dialog"). The
  caption strings are CP949 in the VFS and are not reproduced here.

> **Confidence.** The PIN's existence, its first-class "is-PIN" input modelling, its ≤ 4-char
> capacity, and the fact that its value lands in the optional login-blob field are **RUNTIME-
> CONFIRMED** against the live client (read from the client's process at login time; no addresses).
> The modal's exact widget layout / action ids are not yet swept (its `uiconfig.lua` controls were
> not catalogued in this pass) — treat the modal's widget tree as **PLAUSIBLE / to-be-swept**
> (Open question 10) while treating the PIN's flow position and wire destination as confirmed.

## 1.5 The login flow sub-state machine (CODE-CONFIRMED)

Stored in a single window field (the flow counter at `+0x238`; the field is named in
`ui_system.md` §6.3). The table below is the corrected, authoritative meaning of each sub-state for
the front-end flow. **It supersedes two CODE-CONFIRMED-but-wrong labels in `ui_system.md` §6.3**
(sub-state 29 and 31) — see the conflict note at the end of this section and in §6.

| Sub-state | Meaning | Notes |
|---|---|---|
| 2 | Connect / play login-enter SFX **861010105**; reset banner Y | scene entry |
| 3, 4, 5 | Animate the two banner panels into place | banner pan; also the option-page select target |
| 6 | **Login form active** — waiting for user input | the resting state of the form |
| **29** | **OK-button credential validation** | ID len ≥ 4 (else msg **4025** → 6); PW len ≥ 1 (else msg **4026** → 6); persist Save-ID; advance to 31. **(corrects `ui_system.md`: NOT "server-list trigger")** |
| 30 | Quit-confirm "Yes" path | writes engine state **6 / substate 8** (quit) |
| **31** | **Show EULA / terms-of-service accept overlay** | advances toward 32. **(corrects `ui_system.md`: NOT "Help screen")** |
| 32 | Wait for EULA "agree" | overlay visible AND accept flag set → advance to 33 |
| 33 | Press-OK transition → begin server-list fetch | sets up the fetch (advance to 34) |
| 34 | Start the server-list fetch thread (lobby port 10000) | thread = a black box; see `login_flow.md` §2.1 |
| 35 | Wait for server-list reply | thread sets 36 on completion |
| 36 | Consume server list (§2.3 / §2.4) | empty → msg **4027** → 6; connect-fail → msg **4028** → 6; else render and go 37 |
| 37 | Server selected | entry click commits the selection + persists `Lastserver` (§2.5) |
| 38 | Start the channel-endpoint fetch thread (lobby port `10000 + selected id`) | see `login_flow.md` §2.2 |
| 39 | Wait for endpoint reply | thread sets 40 |
| 40 | **Join handoff** | collect the second-password / PIN (§1.4a) if not already entered; build the TAB credential string, rebuild the secure context, hand to the game connection; engine state set to **7** as a guard. The login window then exits. |
| 41 | Transition complete; login window exits | — |

> **Sub-state 40 detail.** The TAB string is `account⟨TAB⟩option⟨TAB⟩field⟨TAB⟩"host port"`, where
> the optional middle field carries the **second-password / PIN** (§1.4a) and the 4th field is the
> channel endpoint text obtained by the channel-endpoint fetch (§2.6). The secure-context rebuild
> stages the credential session on the main (overlapped) game connection; from there the
> `0/0 → 1/4 → 1/6` handshake proceeds. The byte layout of all of those is owned by
> `login_flow.md` §3–§4 and `crypto.md`; this spec only marks the boundary. (`PLAUSIBLE` for msg id
> **4029** as the endpoint-fetch-failure analogue at sub-state 39 — not pinned to a call site —
> Open question 1.)

> **CONFLICT with `specs/ui_system.md` §6.3 (I do not own that file).** That table marks sub-state
> **29 = "Server-list trigger point"** and **31 = "Help screen"** as CODE-CONFIRMED. The
> login-scene lane shows both are wrong: **29 = OK-button credential validation** (ID/PW length
> checks → msg 4025/4026), and **31 = show EULA/accept overlay** (the help button is a *separate*
> control, action id 105 `i`, not a sub-state). The remaining `ui_system.md` rows (2–6, 30,
> 32–41) agree with this spec. **An orchestrator/owner should correct `ui_system.md` §6.3 rows 29
> and 31.** Recorded here, not edited there.

## 1.6 Save-ID persistence (CODE-CONFIRMED)

Toggling the Save-ID checkbox (action `h`) and a successful credential validation persist the
account id to a loose client INI file:

- **File:** `DoOption.ini`, section `[DO_OPTION]`, key `OPTION_ID`.
- On scene build the box is pre-filled from this key (unless it holds the literal `"(null)"`).
- Clearing the checkbox clears/over-writes the key.

This is a **client-local convenience only** — never sent on the wire.

## 1.7 What the Login click sends (cross-reference, not owned here)

For an engineer's mental model only; the byte shapes are owned by `login_flow.md`:

- The **OK/Login button sends nothing directly to the game server.**
- The only client→server traffic in the entire login scene is:
  1. the two **lobby fetches** (synchronous, port 10000 then `10000 + selected id`; no
     `major/minor` opcode — an 8-byte frame wrapper + LZ4 payload), at sub-states 34 and 38, and
  2. the **game-socket handshake** at sub-state 40: the cryptographic key exchange and reply,
     then the account-login send (`1/6 CmsgLoginRequest` per `opcodes.md`), whose blob carries the
     account name and the second-password / PIN (§1.4a) — the account password rides only in the
     RSA `1/4` reply.
- **There is no dedicated "login" opcode fired by the button click.**

## 1.8 The quit paths (CODE-CONFIRMED)

Two distinct quit triggers, both terminating at engine state **6 (Quit)** → **8 (Exit)** (see
`client_runtime.md` §3):

1. **Version-mismatch quit.** OK/Enter with a `game.ver` mismatch → Win32 modal error box (msg
   **2204**) → on OK, the quit-from-load path: SFX **861010106**, a ~1000 ms UI fade, then engine
   state **6 / substate 2**.
2. **User quit-confirm.** The login window has a quit-confirm modal (the "Yes" buttons are actions
   `q`/`r`, captions msg ids **4023 / 4024**). The Yes path drives flow sub-state **30**, which
   writes engine state **6 / substate 8** directly.

The session never returns to the login scene after the first visit — post-in-game logout returns to
**character select (state 4)**, not login (`client_runtime.md` §4).

## 1.9 The login-scene message-catalogue id table (CODE-CONFIRMED ids; captions VFS-only)

All ids resolve through the message-catalogue lookup against `data/script/msg.xdb` (CP949). The
**caption text is in the VFS and is not reproduced** — record the ids, supply text from an extract.

| Message id | Used at | Meaning | Confidence |
|---|---|---|---|
| **2204** | version gate | `game.ver` mismatch / wrong client version (Win32 error box) | CODE-CONFIRMED |
| **4001–4022** | builder static labels | the 22 login-form label captions (ID / PW / server / option labels) | CODE-CONFIRMED (ids) |
| **4023** | quit-confirm popup #1 | quit-confirm prompt | CODE-CONFIRMED |
| **4024** | quit-confirm popup #2 | second quit-confirm prompt | CODE-CONFIRMED |
| **4025** | sub-state 29 | ID / account too short (length < 4) | CODE-CONFIRMED |
| **4026** | sub-state 29 | password empty (length < 1) | CODE-CONFIRMED |
| **4027** | sub-state 36 | server list empty / no servers returned | CODE-CONFIRMED |
| **4028** | sub-state 36 | server-list connection failed | CODE-CONFIRMED |
| **4029** | adjacent literal | likely the channel-endpoint-fetch failure analogue (sub-state 39) | PLAUSIBLE |
| **101** | every timed in-window popup | the "OK / seconds remaining" countdown suffix (`%s - %d`) | CODE-CONFIRMED |

So the family is: **4001–4024 = login labels & quit prompts**, **4025–4028 (and likely 4029) =
login error toasts**, **2204 = the version-mismatch error box**, **101 = the timed-popup suffix**.

---

# 2. Server selection

Server selection is owned by the **same login window** (still engine state 1); it is not a separate
scene. The selectable list is a **runtime network query**, not a VFS file and not a hardcoded array
— the on-the-wire decode is owned by `login_flow.md` §2. This section specifies the **presentation
and selection flow** only.

## 2.1 Two renderers, one decode (CODE-CONFIRMED)

There are **two visual variants** of the server-list renderer:

- a **classic** renderer, and
- a **NEW_SERVER** variant (which adds a "NEW" badge — §2.7).

Which one is active depends on which login-window subclass/atlas configuration is instantiated.
**Both decode the identical 8-byte server record and apply the identical status/load/open-time
presentation rules** — there is no behavioural difference except the badge. A reimplementation needs
one decode path and a presentation toggle.

## 2.2 The server record (decode owned by `login_flow.md` §2.1)

Each server entry is an **8-byte little-endian record** fetched over the lobby socket. Reproduced
here **only** so the presentation rules below are self-contained; the authoritative byte spec and
its packet YAML are owned by `login_flow.md` / `packets`:

| Offset | Size | Field | Used by presentation as |
|---|---|---|---|
| +0 | 2 (u16) | `server_id` | index **1..40** into the client-local localized-name table (§2.8) |
| +2 | 2 (i16) | `status_code` | availability; special values in §2.3 |
| +4 | 2 (i16) | `load` | population gauge; color thresholds in §2.3; also HH source for scheduled-open |
| +6 | 2 (i16) | `open_time` | only meaningful when `status_code == 3`; MM source for scheduled-open |

A `server_id` outside 1..40 is treated as an error/invalid entry.

## 2.3 Status & load presentation rules (CODE-CONFIRMED; wire bytes capture-unverified)

These rules are **UI-only** — they color and label an entry; they are not part of any codec.

**Load color thresholds** (population gauge):

| `load` value | Bucket |
|---|---|
| > 1200 | highest / "full" tier |
| > 800 | high tier |
| > 500 | medium tier |
| ≤ 500 | default tier |

**Status-code special values:**

| `status_code` | Presentation |
|---|---|
| 0 | normal/open — falls through to the load-color path |
| 2 / 3 / 4 (when `open_time == 0`) | fixed "status as label" branch (alternate labels with fixed text colors) — used when there is no scheduled open time |
| 3 (when `open_time != 0`) | **scheduled open**. If `load == 24` → show a "preparing / under check" label; else render a clock `HH:MM` (§2.4) |
| 100 | **auto-connect sentinel** — "this is the connected / current selection"; combined with the auto-advance flag it enables a connect-confirm cluster and re-renders. Used by the single-server auto-connect path (§2.4). |

> The full status enum beyond `{0, 2, 3, 4, 24, 100}` is unknown without a capture (Open question 3).

## 2.4 Scheduled-open clock packing (CODE-CONFIRMED math; capture-unverified semantics)

When `status_code == 3` and `load != 24`, the entry shows a four-digit `HH:MM` clock formatted as
`HH:MM` where each half is a two-digit decimal produced by a `/10`, `%10` split:

- **HH** (hours) = two digits from the **`load`** field (`load/10`, `load%10`),
- **MM** (minutes) = two digits from the **`open_time`** field (`open_time/10`, `open_time%10`).

So for `status_code == 3`, the `load` field is repurposed as the hours of the scheduled open time
and `open_time` as the minutes. The `load == 24` case (a 24-hours sentinel) instead shows the
"preparing" label. *(The render math is firm; whether the server truly packs HH into the load field
this way is `PLAUSIBLE` and capture-unverified — Open question in `login_flow.md`; carried here as
the presentation rule.)*

## 2.5 Selection commit and `Lastserver` (CODE-CONFIRMED)

When a server is selected (flow sub-state 37, or auto-selected when the list has exactly one entry):

- The selected `server_id` is written into the window's **selected-server field** (it is later added
  to 10000 to derive the channel-fetch port — §2.6).
- The selected id is persisted to the registry value **`Lastserver`** under
  `HKLM\software\crspace\do` (a `u32`).
- On the **next** visit, if `Lastserver` is present, the list is shown in a **randomized display
  order that pins the remembered server** (§2.7); otherwise a plain sequential order is used.

`status_code == 100` plus an auto-advance flag drives the **single-server auto-connect** path:
when the list has one entry whose status is "current", the client persists `Lastserver` and advances
straight to the channel-endpoint fetch (sub-state 38) without a manual click.

## 2.6 Channel-endpoint resolve → join (CODE-CONFIRMED; payload capture-unverified)

After a server is committed, a **second** lobby fetch (the channel-endpoint fetch, sub-state 38)
connects to port **`10000 + selected_server_id`** and returns the chosen game server's endpoint as a
**fixed 30-byte NUL-padded ASCII string** of the form `"host port"` (decode owned by `login_flow.md`
§2.2). This endpoint becomes the **4th TAB field** of the join string built at sub-state 40 (§1.5),
and the secure-context rebuild parses `host` and `port` out of it. After this, normal
`major/minor` traffic begins on the game socket.

> The server_id (1..40) is added directly to 10000 as the channel port — i.e. the server provisions
> lobby ports `10001..10040` per server. Capture-unverified (Open question, carried from
> `login_flow.md`).

## 2.7 Randomized display order & the `NEW_SERVER_INDEX` badge (CODE-CONFIRMED)

- **Randomized order.** Both renderers map screen slot → record through a **display-order index
  array** built per refresh. When `Lastserver` is present the order is **shuffled (seeded from the
  clock) with the remembered server re-anchored** to a stable slot; otherwise it is sequential. The
  intent is load-spreading across servers. A reimplementation should treat the *presentation* order
  as decoupled from the *record* order.
- **`NEW_SERVER_INDEX` badge.** A Lua global of that name in `data/script/uiconfig.lua` (value 5 in
  the sampled client) names **which `server_id` to flag as "NEW"**. It is read at scene build and
  stored on the window. In the NEW renderer, the record whose `server_id` equals this value gets an
  extra **"NEW" badge widget** drawn beside it. It is **not** a renderer toggle and **not** an
  address of any server — purely a presentation flag. *(This answers the long-standing "what is
  NEW_SERVER_INDEX" question from the data-census lane.)*

## 2.8 Localized server names — client-local, never on the wire (CODE-CONFIRMED)

The wire carries only the numeric `server_id` (1..40). The **display name** is resolved entirely
client-side through a **41-entry name table** (indices 0..40) built from UI **string-resource
banks**, looked up via the same message-catalogue accessor as everything else:

- The active bank is string ids **5001..5040**.
- Parallel **per-region/locale** banks are pre-touched: 5101–5120, 5201–5220, 5301–5320,
  5401–5440 (and an overlapping 5421–5440). The active locale selects which bank's names show.

**Implication for the revival:** a fresh implementation must supply its own `server_id → name` map;
the names are localized resources, not protocol data. (Corroborated by `login_flow.md` §2.1.)

> **Lobby host discovery (where the *lobby* socket connects), for completeness.** Before any list
> query, the lobby connect helper resolves its host in strict priority: (1) a loose `ip.txt` token
> in the working dir (truncated to 19 chars), else (2) a record from a loose `list.dat` file keyed
> by the registry value `HKLM\SOFTWARE\crspace\do\servername` (each `list.dat` record is 768 bytes:
> a CP949 name match key at +0, the host string at +0x100; file = `u32 count` then `count × 768`),
> else (3) the hardcoded fallback host. This resolves the **lobby** host only; the **game** server
> host comes from the channel-endpoint fetch (§2.6). The `list.dat` byte layout is
> `static`-derived and on-disk-unverified (Open question 5). `do.ini` is **not** referenced by the
> server-selection path on the available evidence.

---

# 3. Character selection

**Engine state: 4 (Select)** — entered when the character-list packet arrives. The select scene is
owned by a dedicated **select window** object. Its widget tree and the 5 live 3D preview actors are
**built only when the character-list packet (`3/1 SmsgCharacterList`) arrives** — the scene does not
exist until then (a quirk also noted in `ui_system.md` §6.4 and `client_runtime.md` §7).

> **The char-select scene is a full 3D world, not a 2D screen.** The select window does **not**
> paint a flat backdrop behind a 2D character portrait; it **builds a named 3D scene ("select") on
> the real game world `data/map000`, frozen at afternoon (14:30), with up to five live, animated 3D
> character models standing on a stage in front of a perspective camera.** The 2D chrome (slot
> frames, Create/Delete/Enter buttons, info plates — §11.5) only *dresses* that 3D scene; selection
> itself is the 3D row (§3.3). The 3D composition (world, cell, coordinates, assets) is specified in
> **§3.7**; the environment/lighting in **§3.6**; the camera in **§3.5**; per-slot placement/pose in
> **§3.3**.

## 3.1 Where the slots come from & the per-slot record (CODE-CONFIRMED)

The inbound `3/1 SmsgCharacterList` (S2C, byte shape owned by `login_flow.md` §3.2 / `opcodes.md`)
is the message that **forces the select scene** (it writes engine state 4 / substate 8). Its body is
a **3-byte header** (the third byte is a 5-bit slot mask) followed, **for each set bit**, by one
per-slot record read in four parts:

| Part | Size | Role |
|---|---|---|
| **Spawn descriptor** | **880 bytes** (`0x370`) | the full character record (§3.2) |
| **Stats block** | **96 bytes** (`0x60`) | per-slot stat block |
| **Slot flag** | **1 byte** | per-slot availability/relation flag |
| **Timing value** | **4 bytes** (u32) | per-slot timing (e.g. a cooldown/relation timestamp) |

> **Reconciliation with `login_flow.md` §1 / `opcodes.md`.** `login_flow.md` records the char-list
> per-slot stride as **981 bytes**. That figure is the **sum of these four parts** (880 + 96 + 1 +
> 4 = 981). They describe the **same record** at two granularities: `login_flow.md` owns the wire
> stride; this spec documents how the select scene splits it into descriptor / stats / flag /
> timing arrays. **No conflict** — recorded so the two specs read consistently.

There are at most **5 slots**. On entering the scene the select window **copies** these four arrays
into its own storage, then builds the 124-widget UI (owned by `ui_system.md` §2.2) and the 5 preview
actors.

## 3.2 The 880-byte spawn descriptor — fields the select scene uses (CODE-CONFIRMED)

Field offsets are **inside the 880-byte record** (interoperability facts; the full struct table is
owned by the struct cartographer). Only the fields the front-end touches are listed:

| Offset | Field | Meaning at select |
|---|---|---|
| +0x00 | `name` (CP949, ≤17 bytes incl. terminator) | character name; the sentinel **`"@BLANK@"`** marks an **empty slot** |
| +0x2C | `sex` (u8) | gender (also written by the create form, §4.2) |
| +0x2E | `faceA` (u16) | appearance param A (face). **Nonzero ⇒ the slot is occupied** (the preview occupancy test) |
| +0x30 | `faceB` (u16) | appearance param B (hair / second appearance seed — exact meaning unresolved, Open question 4) |
| +0x34 | `class` (u16) | internal class id (1..4) |
| +0x3A | `level` (u16) | shown on the slot info line ("0" if zero) |
| +0x58 | `equipment table` | **20 × 16-byte** worn-gear/visual slots; the **first dword of each** is an actor/equipment id resolved by the preview (§3.3) |
| +0xA0..0xA8 | `position` (two floats) | the character's **last in-world X/Z**, shown on the slot info line as `"X , Y"` |
| +0x88 / +0x98 / +0x108 / +0xB8 | starter-equipment id slots | seeded on **create** per class (§4.3); empty on existing characters |

The slot info line shows, for the active slot: **name**, **level**, and **position** (the two
floats above). Toggling the enter/create/delete buttons depends on the slot's lock flag.

## 3.3 The live 3D preview actors — placement, facing, pose (CODE-CONFIRMED)

The select scene renders each occupied slot as a **live, animated 3D character** standing in a **row**
on the 3D stage (§3.7) — **not** a 2D portrait, and **not** a separate asset path. The preview reuses
the exact in-world player-actor build path, so skin / bind / idle-motion resolve through the normal
`.skn` / `.bnd` / `.mot` chains (owned by `specs/skinning.md`, `formats/mesh.md`); char-select adds
**no new asset loading and no dedicated "select" motion clip** (§3.3.4 / §3.7.5). The preview-character
asset set for the four starter classes is catalogued in **§3.7.5**.

### 3.3.1 Per-slot world placement (CODE-CONFIRMED)

Each preview actor's world position is the **stage origin** (§3.7.2; world `(2048, 0, −6144)`) plus a
**baked per-slot offset**. The row runs along **world +X**; the Z component arcs very slightly toward
the camera at the centre slot (a shallow ~1.5-unit bow). **Y is exactly 0.0 for every slot** — the
actors stand on the stage-origin plane; there is **no terrain sample and no per-slot ground lookup**.

| Slot | ΔX | ΔY | ΔZ | World X | World Y | World Z | Confidence |
|---|---|---|---|---|---|---|---|
| 0 | −1560.0 | 0.0 | −3593.0  | 488.0 | 0.0 | −9737.0 | CONFIRMED |
| 1 | −1548.0 | 0.0 | −3594.0  | 500.0 | 0.0 | −9738.0 | CONFIRMED |
| 2 | −1536.0 | 0.0 | −3594.5  | 512.0 | 0.0 | −9738.5 | CONFIRMED |
| 3 | −1524.0 | 0.0 | −3594.0  | 524.0 | 0.0 | −9738.0 | CONFIRMED |
| 4 | −1512.0 | 0.0 | −3593.0  | 536.0 | 0.0 | −9737.0 | CONFIRMED |

- **X spacing:** adjacent slots are **12 world units** apart; with the **×3.0 preview scale** (below)
  the on-screen separation is **36 units**.
- **Z arc:** the Z offsets `{−3593, −3594, −3594.5, −3594, −3593}` dip to the centre slot, bowing the
  row very slightly toward the camera. (This refines the earlier "Z ≈ −3593" approximation.)
- **Scale:** the per-slot preview actor's scale factor is multiplied by **3.0**.
- **Spin:** the slot previews do **not** auto-rotate. (The separate single **create-preview** actor at
  §4.2 *does* idle-spin.)

### 3.3.2 Facing — pure yaw, fixed at build (CODE-CONFIRMED placement; front/back UNVERIFIED)

Orientation is a **pure-yaw quaternion** (rotation about the world up axis Y only; no pitch, no roll),
built once at actor creation and not changed by hover. The yaw is chosen from the slot's lock flag:

| Slot lock flag | Yaw | Meaning |
|---|---|---|
| set (locked / new / creating slot) | **π** (180°) | faces **away** from the camera (back to viewer) |
| clear (existing, occupied, playable slot) | **0** | faces **front** (toward the camera) |

- The front yaw is **literally 0.0** (not a camera-relative offset); the camera (§3.5) is posed so
  that yaw-0 shows the character's front.
- **UNVERIFIED (MEDIUM):** the project's mesh-local `.skn` X-negation convention (`formats/mesh.md`)
  can flip apparent facing. Whether a yaw-0 preview shows the **front** (expected) or the **back**
  after the importer's X-negation must be confirmed against a live frame; if it shows the back, the
  importer must add π / mirror consistently with the X-negation. (Open question 13.)

### 3.3.3 Selection is the 3D row itself (CODE-CONFIRMED)

There is **no 2D slot widget that drives selection** — the row of 3D models *is* the clickable
selector. On each mouse move the cursor is unprojected and, for each preview actor, a screen-space
**axis-aligned bounding box** is built around the actor's projected position (`X ± 6`, `Z ± 6`, with a
**Y band from 70.0 to 92.0** = the standing-height range) and tested against the cursor. A hit sets the
hovered/selected slot. **The actor transform is never re-written on hover** — neither its position nor
its yaw changes; the visible "turn" is an animation-clip swap only (§3.3.4). The 2D slot frame / dim
chrome (§11.5) runs in parallel as cosmetic dressing.

### 3.3.4 Pose / motion — idle vs select-turn clip swap (CODE-CONFIRMED)

The preview uses the standard in-world animation pipeline; **there is no hardcoded "select stand"
motion id.** The clip is chosen indirectly through the per-class animation-catalogue **visual record**,
selected by the actor's **motion-state byte** (descriptor field `+0x3C4`):

- **idle / default cycle** → the visual record's **idle field (`+0x44`)** when the motion-state byte is
  0. This is what plays at scene entry for **every** occupied slot.
- **select / "turn-to-front" cycle** → the visual record's **select field (`+0x58`)** when the
  motion-state byte is 1.

On hover/selection, the hit-test handler swaps the hit actor's clip **idle → select** (record `+0x44`
→ `+0x58`) and forces **every other** slot back to **idle**. The apparent "turn to face the player" is
**baked into the select clip**, not a transform change. The concrete `g{id}.mot` that each visual-record
field resolves to is owned by `specs/skinning.md` + the animation-catalogue struct; for the starter
classes the idle clip is `g111100010.mot` ("peace", 30 frames @ 10 fps; §3.7.5).

| State | 3D-actor effect |
|---|---|
| Unselected occupied slot | idle cycle (record `+0x44`); yaw 0 (or π if locked); scale ×3 |
| Selected / hovered slot | select/"turn-to-front" cycle (record `+0x58`); **same transform** (no move, no extra rotation) |
| Locked / new / creating slot | yaw π (faces away); otherwise idle handling |

### 3.3.5 Worn gear & default highlight (CODE-CONFIRMED)

- **Worn gear** is overlaid by scanning the descriptor's 20 × 16-byte equipment table at +0x58; each
  slot's first dword is resolved to a visual id and attached (gear/visual sub-mesh channels are
  re-armed after the build), gated by a class/sex check.
- After building, the **default slot** is auto-highlighted and its info line (name / level / position,
  §3.2) shown. The default-slot index source is **MEDIUM** (not load-bearing for placement/pose).

> **Coordinate convention reminder (for the Godot bridge).** These are raw legacy stage-world
> coordinates with up = Y and the row along +X. Apply the project's world-to-engine convention when
> porting — `Helpers/WorldCoordinates.ToGodot` negates Z `(x,y,z) → (x,y,−z)` (after which the row Z
> becomes +9737..+9738.5). The 12-unit X spacing and the ×3.0 scale are convention-neutral. The
> mesh-local `.skn` X-negation is internal to skin building and is the source of the §3.3.2 front/back
> caveat.

## 3.4 Slot availability vs lock flags

Two per-slot flag arrays gate enter/render. One marks a slot **selectable for enter**; the other
marks a slot **creating/locked** (which also drives the "faces away" idle facing). The precise
difference (selectable vs creating/locked/cooldown) is **inferred, not byte-confirmed** (Open
question 6).

## 3.5 The character-select preview camera (CODE-CONFIRMED geometry; framing partly confirmed)

The select window builds a dedicated preview camera (a derived "third-person" camera manipulator) to
frame the row of 3D character previews. Its **orbit geometry is statically recovered in full**; the
runtime **framing law** (which keyframe is live, the easing, the look-at target) is now largely
recovered too — see §3.5.4. This **supersedes** the earlier approximate "7-waypoint" reading
referenced from `client_runtime.md §7.3`: the orbit is **6 keyframes, not 7**, and the **live keyframe
is index 1** (§3.5.2).

### 3.5.1 Scene & projection (CODE-CONFIRMED)

> **CORRECTION (CODE-CONFIRMED) — the char-select is a 3D GScene built on `map000`, NOT
> "map area 52200".** Earlier readings of this spec recorded the active map area as **52200** with a
> sub-area of **0x30 (48)**. A re-read of the scene builder shows those two values are **not** a map
> area id: the scene activates **area code 0**, which is rendered into the three-digit map-folder
> string **`"000"`** → the world folder **`data/map000`**. The triple that earlier readings mistook
> for "area 52200 / sub 0x30" is the **game-clock / weather** argument:
> - **52200** is a **time-of-day** value — **52200 seconds = 14:30** (afternoon) — fed to the world
>   clock setter, which validates it against the seconds-in-a-day bound (86400).
> - **48** is a **time / weather sub-index** (a discrete value bounded at 48), not a map id.
>
> So the char-select scene **reuses the real in-world environment, frozen at 14:30**, on `map000` — a
> full 3D world backdrop, not a flat 2D screen. The afternoon clock is why the backdrop is lit and
> sunny. The 3D scene composition / world coordinates / cells / assets are specified in the new
> **§3.7**; the environment & lighting in **§3.6**. The struck-through "52200 / 0x30 = area" row below
> is retained only as a redirect.

| Property | Value | Notes |
|---|---|---|
| Scene name | `"select"` | the named char-select 3D scene root |
| Base world | **`data/map000`** (area code **0** → folder string `"000"`) | the real world map, reused as the backdrop — see §3.7 |
| ~~Active map area 52200 / sub 0x30~~ | **NOT a map area** | superseded: `52200` = time-of-day clock (14:30), `48` = weather sub-index — see the correction above and §3.6 |
| Camera type | perspective | a perspective camera node |
| Vertical FOV | **50°** | `π · 50 / 180`, then **divided by the aspect ratio** (screen width / screen height) before being set as the projection field-of-view |
| Near clip | **5.0** | |
| Far clip | **15000.0** | |

> The FOV-over-aspect form means the legacy projection FOV scales with the window aspect. A
> reimplementation that uses a standard "vertical FOV + aspect" projection should set vertical FOV =
> 50° and let the renderer apply aspect normally; the legacy `fov / aspect` is the same framing on a
> 4:3 reference canvas.

### 3.5.2 The 6-keyframe orbit (CODE-CONFIRMED; live keyframe = 1)

The camera holds a table of **6 position keyframes**, each a 3-float `(x, y, z)` triple. An **anchor
offset of `(+2048, 0, −6144)`** (= the stage origin, §3.7.2) is added to every raw keyframe to place
the orbit in stage-world space. The 6 anchored keyframe positions:

| Keyframe | Anchored position (x, y, z) |
|---|---|
| 0 | (515.55, 137.27, −9397.71) |
| **1 (live)** | **(512.00, 87.00, −9652.00)** |
| 2 | (343.00, 104.00, −9734.00) |
| 3 | (471.00, 115.00, −9812.00) |
| 4 | (622.00, 75.00, −9802.50) |
| 5 | (662.00, 130.00, −9746.00) |

> **Live keyframe (CODE-CONFIRMED).** The camera constructor's default active keyframe is **0**, but
> after the scene is built the camera-wire step sets the active keyframe to **1**. That wire step runs
> on the character-management response path and the select-window rebuild paths, so the keyframe the
> player actually sees is **index 1** ≈ world `(512, 87, −9652)`. (Earlier versions of this spec
> recorded only the constructor default of 0.)

> **Coordinate convention reminder.** These are stage-world coordinates as the legacy client stores
> them. Apply the project's world-to-engine convention (world geometry negates Z — see
> `Helpers/WorldCoordinates`) when porting; do not silently re-sign them here.

### 3.5.3 The 12 PI-scaled angle multipliers (CODE-CONFIRMED values; yaw/pitch split MEDIUM)

The camera also holds **12 angle multipliers**, each multiplied by π to yield an angle in radians.
With 6 keyframes the natural reading is **6 yaw + 6 pitch** (one yaw and one pitch per keyframe),
but the exact assignment of the second six (indices 6..11) to yaw vs pitch is **MEDIUM** until
confirmed against the manipulator's update law.

| Index | Multiplier | × π (rad) | ≈ degrees |
|------:|-----------:|----------:|----------:|
| 0 | −0.03333334 | −0.104720 | −6.000 |
| 1 | −0.01483333 | −0.046600 | −2.670 |
| 2 |  0.00333333 |  0.010472 |  0.600 |
| 3 | −0.01111111 | −0.034907 | −2.000 |
| 4 |  0.04333333 |  0.136136 |  7.800 |
| 5 | −0.07666667 | −0.240855 | −13.800 |
| 6 |  0.01333333 |  0.041888 |  2.400 |
| 7 |  0.00436111 |  0.013701 |  0.785 |
| 8 | −0.20333332 | −0.638790 | −36.600 |
| 9 | −0.44444445 | −1.396263 | −80.000 |
| 10 |  0.41276109 |  1.296727 |  74.297 |
| 11 |  0.29111111 |  0.914553 |  52.400 |

Other constructor scalars (CODE-CONFIRMED): a `1.0` and a `10.0` speed/rate scalar pair, identity
initial scale/orientation values, and a constructor-default active keyframe index of 0 (wired to
**1** at runtime — §3.5.2). The **`1.0`** scalar is a time→input-rate multiplier (it scales the
per-frame millisecond delta when damping manual-orbit input); the **`10.0`** scalar is the manual
zoom/yaw/pitch input-rate constant. **Neither drives the automatic keyframe framing** — the keyframe
tween uses its own normalizer (§3.5.4).

### 3.5.4 Framing law — look-at, eye, easing (CODE-CONFIRMED structure; eye MEDIUM)

The camera manipulator is a scene-graph node, not an explicit eye/target pair. Each frame it computes:

- a current **orbit point** (a keyframe-derived world position), and
- an **orientation** (yaw/pitch quaternion),

then sets the camera eye to **`eye = orbitPoint + Rotate(orientation, boomVector)`**, where the boom
vector points from the target out to the eye and its length (the zoom distance) is clamped to **≤ 22
units**. **The look-at target is the active orbit point** — *not* the stage origin (the stage origin
is only the anchor the keyframes are added to). The base pitch is **≈ −30°** (downward), modulated by
each keyframe's stored yaw/pitch; so the camera looks slightly **down at the standing row from in
front**.

- **Look-at target (CONFIRMED):** the active (keyframe-1) orbit point ≈ world **(512, 87, −9652)**,
  which sits essentially **over the actor-row pivot ≈ (508, 70, −9759)** (§3.6 / §3.7.2) — slightly
  above and ~100 units in front of the standing row.
- **Eye (MEDIUM):** the exact eye world coordinate depends on the runtime boom vector and the live
  yaw/pitch quaternion; the **look-at identity is CONFIRMED**, the precise eye is MEDIUM (debugger-
  confirmable on a live select frame).
- **Easing (CONFIRMED constants; duration MEDIUM):** when the active keyframe changes, the orbit point
  is **linearly interpolated** and the orientation **spherically interpolated (slerp)** over a
  normalized progress `t`. For transitions among the inner keyframes (both indices ≥ 2) an extra
  **quadratic ease `(1 − t)·(2t)`** is layered over the linear blend ("linear-then-quadratic" /
  ease-in-out); the keyframe-0↔1 transition uses the plain linear blend only. The progress normalizer
  decodes to **≈ 2000 ms (2.0 s)** per transition. **MEDIUM:** an existing tool annotation says
  "0.5 s"; the decoded constant gives 2.0 s — resolve by timing the live transition. (Open question 12.)
- **Auto-advance (MEDIUM):** no timer was found that auto-cycles the keyframe index; statically the
  camera rests on keyframe 1 unless an external caller (e.g. a UI/event handler responding to slot
  selection) requests another index. Whether selecting a different slot switches the keyframe was not
  fully traced.

### 3.5.5 Static-complete vs runtime-pending (explicit)

- **Static-complete (CODE-CONFIRMED):** all 6 keyframe positions, the `(+2048, 0, −6144)` anchor
  offset (= stage origin), the 12 π-scaled angle multipliers, FOV 50° / aspect-divided, near 5.0,
  far 15000.0, the `1.0` (input-time) / `10.0` (manual-zoom) scalars, scene name `"select"`, base
  world `map000`, the **live keyframe index = 1**, the look-at target = active orbit point, the
  boom-zoom clamp ≤ 22, the base pitch ≈ −30°, the lerp/slerp + inner-keyframe quadratic-ease law.
- **Runtime-pending (NOT yet confirmed — do not invent):**
  1. **The precise eye world coordinate** (boom vector + live yaw/pitch quaternion).
  2. **The exact tween duration** (≈ 2.0 s decoded vs a "0.5 s" annotation — Open question 12).
  3. **The yaw-vs-pitch assignment** of angle indices 6..11 vs 0..5.
  4. **Whether slot selection auto-switches the keyframe** (no timer auto-advance found).

An implementer should treat the orbit geometry, the live keyframe (1), the look-at target, and the
easing law as authoritative, and the precise eye / tween duration as tunable until confirmed.

## 3.6 Char-select environment, lighting & ambient FX (CODE-CONFIRMED structure; colours data-driven)

The select scene does **not** author a bespoke select-only sky or light rig. It activates the **real
area-0 world environment** (§3.5.1 correction) and **freezes the world clock at 14:30** (time-of-day
value 52200, weather sub-index 48). The sky, sun direction, fog and ambient colour are therefore
whatever the **area-0 map + sky data + the 14:30 clock** produce — a **parametric sky**, not a `.box`
skybox file (no skybox file exists in the VFS for this scene; §3.7.4).

### 3.6.1 Lighting rig (CODE-CONFIRMED counts; colours UNVERIFIED)

The scene attaches the shared **sky/time manager** singleton's render node into the 3D scene as a
child (the same manager the main world uses). That manager builds the lighting rig:

- **≈ 5 positional lights** (the sun plus fill lights), each with a light range/radius of **≈ 1024**.
- A **white (1.0) colour-scale baseline** (identity colour multipliers) and a **black, full-alpha
  clear colour** baseline, both later tinted by the time-of-day-driven sky data.
- **No hard-coded ambient colour literal** exists in the scene builder — the final on-screen sun /
  ambient / fog **colours are data-driven** through the sky/time manager and the area-0 sky data at
  the frozen 14:30 clock. (Light count, range ≈ 1024, white baseline, black clear = CONFIRMED; the
  **final colours and the per-light directions are UNVERIFIED here** — they come from the sky data /
  per-frame update, debugger-confirmable on a live frame.)

### 3.6.2 Fog / sky (MEDIUM)

The select scene explicitly **zeroes a sky/fog blend scalar** (a fog-density / sky-blend factor set to
0.0) — i.e. **minimal / no distance fog** behind the preview row, so the row reads clearly. The exact
visual meaning of the zeroed scalar is MEDIUM (needs a live read of the resulting fog density/colour).

### 3.6.3 Sky-data asset set for the char-select area (CODE-CONFIRMED paths; black-box VFS)

The sky/environment system is a **per-area index** of binary `.bin` parameter files under
`data/sky/map/` (with human-readable `.txt` companions), **not** a single skybox file. The char-select
area uses the **area-015** sky index. The verified files (sizes are interoperability facts):

| Role | VFS path | Size | Confidence |
|---|---|---|---|
| Fog parameters | `data/sky/map/fog015.bin` | 204 B | CONFIRMED |
| Day-cycle directional light | `data/sky/map/light015.bin` | 5,312 B | CONFIRMED |
| Per-cell light-map references | `data/sky/map/light_map015.txt` | 14,458 B | CONFIRMED |
| Per-area effect placement table | `data/sky/map/map015.txt` | 44,091 B | CONFIRMED |
| Sky render options | `data/sky/map/map_option015.bin` | 40 B | CONFIRMED |
| Sky material / shader params | `data/sky/map/material015.bin` | 9,792 B | CONFIRMED |
| Local point-light sources | `data/sky/map/point_light015.bin` | 4,748 B | CONFIRMED |
| Weather parameters | `data/sky/map/weather015.bin` | 240 B | CONFIRMED |
| Wind direction/strength | `data/sky/map/wind015.bin` | 8 B | CONFIRMED |

Per-area light/material parameters are also embedded directly under `data/map000/`
(`light0.bin` 5,312 B; `material0.bin` 9,792 B). Shared sky textures (cloud / sun / moon / star /
lens-flare / precipitation) live under `data/sky/texture/` and are global to all areas.

> **UNVERIFIED:** whether the char-select clock value `48` and the area-015 sky index are the same
> "area/sub" index, and how the area-0 world maps to the area-015 sky files, was not pinned (black-box
> VFS witness only; no on-disk table mapping was found). The per-family files above are the concrete
> assets the scene's parametric sky reads.

### 3.6.4 Per-cell lightmaps (CODE-CONFIRMED)

The backdrop cell carries a baked lightmap bitmap under `data/effect/map/` named by the cell coordinate
(`d000x{X}z{Z}.bmp`, e.g. `data/effect/map/d000x10000z9990.bmp`, 49,208 B = 128×128 24-bit BMP). These
are pre-baked ambient/occlusion lighting for the terrain. (~3,791 such lightmaps exist across all
areas; the char-select cell's is present.)

### 3.6.5 Ambient VFX anchored at the row centre (CODE-CONFIRMED placement)

A persistent background **map effect** is spun up once at scene build and lives in the active-effect
list for the duration of the select screen (it is a standing background effect, not a one-shot). It is
anchored at world **(508.48, 69.89, −9758.57)** — the **centre of the preview character row**, framed
dead-centre by the camera (this is the same point as the terrain-init pivot, §3.7.2). The concrete
VFS asset for this effect is the yellow lens-flare / glow effect file
`data/effect/xeff/380003001.xeff` (23,675 B) — the client's numeric effect id is the family root
**380003000** (no file of that exact name exists; the shipped variant is `…001`). A companion ring
particle effect `data/effect/xeff/zone_sel_u.xeff` (26,947 B) is also part of the char-select ambient
set, drawn around the platform centre. A map ambient **sound/effect cue** (a numeric map-cue id) is
also pushed at scene start.

> **MEDIUM:** whether the effect-id-to-filename resolution drops the last digit (family `380003000` →
> first variant `380003001.xeff`) or selects `…001` directly is not byte-pinned; and which of
> `380003001.xeff` / `char_select-u.xeff` / `zone_sel_u.xeff` fires as the primary ambient vs the ring
> overlay depends on the scene-config slot (the placement at the row centre is CONFIRMED). The
> effect-file byte format is owned by `formats/xeff.md` / the effect catalogue — not this spec.

## 3.7 Char-select 3D scene composition — world, cell, stage, assets (CODE-CONFIRMED + black-box VFS)

This section is the implementable composition of the char-select 3D backdrop: the world, the single
backdrop cell and its textures, the stage coordinate frame, and the preview-character asset set. It is
what an engineer rebuilds the scene from as a **3D scene**, not a 2D screen.

### 3.7.1 Base world & backdrop cell (CODE-CONFIRMED world; black-box VFS for the cell/textures)

- **Base world:** `data/map000` (area code 0 → folder string `"000"`, §3.5.1). Textures under
  `map000` are global to the whole client.
- **Backdrop cell:** the scene seeds a **3×3 first terrain ring** around the centre cell, but `map000`
  is **sparse** — only the single cell **`d000x10000z9990`** exists; the engine requests all 9 ring
  cells and silently skips the 8 absent neighbours. The backdrop is therefore rendered from this one
  purpose-built cell. Its cell-list manifest records the same key twice (a pre-compute + render pass
  pair).
- **Cell addressing (CONFIRMED):** cells are **1024 world units** on a side; `1024 / 64 = 16` is the
  intra-cell vertex spacing on the 65×65 grid. The cell naming/key convention is:
  - `mapX = 10000 + cx`, `mapZ = 10000 + cz` (so cell `(cx=0, cz=−10)` → `mapX=10000, mapZ=9990`),
  - `cell_key = mapX · 100000 + mapZ`,
  - file stem `d000x{mapX}z{mapZ}`, cell world origin `(cx·1024, cz·1024)` = `(0, −10240)` for the
    backdrop cell.
- **Centre cell:** `(mapX=10000, mapZ=9990)` = world X ∈ [0, 1024], Z ∈ [−10240, −9216]; the row pivot
  (508, −9734) sits inside it.

The backdrop cell's component files (under `data/map000/dat/`), as black-box VFS observations:

| File | Role |
|---|---|
| `d000x10000z9990.map` | Cell manifest (ASCII text): origin, terrain/building/FX section pointers, per-cell texture-id table; ORIGIN `0.000, −10240.000` |
| `d000x10000z9990.ted` | Height-field (binary): 64×64, 16×16 patches |
| `d000x10000z9990.bud` | Building / prop geometry (binary): the decorative 3D props (walls, pillars, ornaments) |
| `d000x10000z9990.exd` | Extra terrain data (binary) |
| `d000x10000z9990.fx1` | Terrain layer FX1 (binary) |
| `d000x10000z9990.fx3` | Terrain layer FX3 — water/reflection layer (binary) |
| `d000x10000z9990.fx5` | Terrain layer FX5 — secondary water layer (binary) |
| `d000x10000z9990.sod` | Collision wall segments (2D XZ ray-parity; minimal) |

(No `.mud`/`.pre`/`.post`/`.fx2` etc. for this cell — it is a purpose-built backdrop, not a full play
cell. The terrain/building/water binary formats are owned by their own `formats/*.md`.)

### 3.7.2 Stage coordinate frame (CODE-CONFIRMED)

| Quantity | Value | Notes |
|---|---|---|
| Stage world origin (X, Y, Z) | **(2048.0, 0.0, −6144.0)** | the preview-stage origin; per-slot offsets (§3.3.1) and the camera keyframe anchor (§3.5.2) are added to this |
| Terrain-ring centre / row pivot (X, Z) | **(508.0, −9734.0)** | = stage origin minus the ring-centre constants (X−1540, Z−3590); the focal point of the backdrop |
| Ambient-FX / look-at anchor (X, Y, Z) | **(508.48, 69.89, −9758.57)** | row centre lifted ~70 in Y (§3.6.5); the camera look-at (§3.5.4) sits essentially over it |
| Cell stride | **1024** world units / cell / axis | |

> The stage origin `(2048, 0, −6144)` is the anchor; the camera keyframes (§3.5.2) and the per-slot
> placements (§3.3.1) are both expressed relative to it. The **row pivot (508, −9734)** is the visual
> focus and the centre of the standing row.

### 3.7.3 Backdrop textures (black-box VFS; CONFIRMED present)

The backdrop cell's textures resolve through the standard terrain chain (`.map` texture-id →
`data/map000/texture/bgtexture.txt[id]` rel-path → `data/map000/texture/<rel>.dds`). The 11 textures
this cell references (all confirmed present):

| Section | Rel path → VFS `.dds` |
|---|---|
| Terrain | `terrain/g3` |
| Buildings | `building/haha`, `building/suksang01`, `building/suksang02`, `building/suksang03`, `building/suksang04`, `building/walll04`, `building/walll04_2` |
| Water (FX3/FX5, animated) | `terrain/_water_new01`, `terrain/_water_new03`, `terrain/_water_new04` |

(The water rows carry the animated-texture flag in `bgtexture.txt`. The terrain/building/texture-chain
formats are owned by their own specs; this is the concrete asset list for the backdrop.)

### 3.7.4 No skybox file (black-box VFS)

There is **no `.box` / `skybox.bin` skybox file** anywhere relevant in the VFS (none under
`data/effect/`, `data/sky/`, or `data/map000/`). The sky is **parametric** — assembled at runtime from
the per-area sky-parameter `.bin` files (§3.6.3) and the frozen 14:30 clock — not a pre-baked cube/box
texture. A revival must render the sky parametrically (or substitute an equivalent), not look for a
skybox asset.

### 3.7.5 Preview-character assets — the four starter classes (black-box VFS; CONFIRMED present)

All four playable starter classes use the **default appearance `IdA = 1`**, which shares a single
**skeleton** and a single **idle motion**; only the mesh and texture differ per class:

| Class (tag) | Mesh `.skn` | Texture (1024²) |
|---|---|---|
| 3 — Bichimi / Dosa (`b`) | `data/char/skin/g202110001.skn` | `data/char/tex10241024/402110001.png` |
| 4 — Monk (`p`) | `data/char/skin/g203110001.skn` | `data/char/tex10241024/403110001.png` |
| 6 — Archer (`a`) | `data/char/skin/g209110001.skn` | `data/char/tex10241024/409110001.png` |
| 11 — Sorceress / Summoner (`s`) | `data/char/skin/g206110001.skn` | `data/char/tex10241024/406110001.png` |

Shared across all four starter previews:

- **Skeleton (bind):** `data/char/bind/g1.bnd` — **84 bones**, 1 root.
- **Idle motion:** `data/char/mot/g111100010.mot` — "peace", **30 frames @ 10 fps** (3.0 s loop). This
  is the in-world idle clip the preview plays by default (§3.3.4); there is **no dedicated char-select
  clip**. (The reference/bind-pose clip `g101100001.mot`, 3 frames, is the rest-state anchor, **not**
  the visible idle.)

The class → skin → texture / bind / motion chain is the normal in-world chain (owned by
`specs/skinning.md`, `formats/mesh.md`); char-select adds no new asset. Higher-tier appearances
(`IdA` 11/16/26) have distinct skeletons and idle clips, but the **char-select preview uses only
`IdA=1`** (simplest mesh/rig).

> **Note (single-source).** The preview-asset chain above is a **black-box VFS / production-parser
> observation** (no IDA cross-check yet for this specific lookup chain). The per-class mesh/texture
> paths and the shared `g1.bnd` / `g111100010.mot` are confirmed present and decode cleanly; treat the
> chain as CONFIRMED-present, the col-index → role mapping of `skin.txt` / `actormotion.txt` as owned
> by the data-table / skinning specs.

---

# 4. Character creation

Triggered by the **Create** button (UI action **4**, CODE-CONFIRMED — see correction note below), which opens a create sub-form drawn over the
select window: a class/appearance picker plus a name-entry textbox. The empty-slot path (§3 sentinel
`"@BLANK@"`) also routes here (§7).

> **Correction (CODE-CONFIRMED, widget-atlas sweep).** Previous versions of this spec and of
> `ui_system.md` recorded the Create button action id as **413** and the Delete button as **531**.
> Those values are in fact the **atlas src-X coordinates** of the respective button HOVER frames
> (Create HOVER src-X = 354+59 = 413; Delete HOVER src-X = 472+59 = 531), not action ids.
> The actual `Panel_AddChildWithAction` ids recovered from the builder call sites are:
> **Create = 4**, **Delete = 5**, **Enter = 6**. `ui_system.md §8.2` has been corrected accordingly.

## 4.1 Class selection & the UI→internal class map (CODE-CONFIRMED)

The create sub-form offers **four classes**, chosen by a UI selection index `0..3`. The legacy code
maps the UI index to an **internal class id** and plays a per-class voice cue:

| UI index | Internal class id | Per-class voice SFX |
|---|---|---|
| 0 | 4 | 910065000 |
| 1 | 1 | 910062000 |
| 2 | 3 | 910064000 |
| 3 | 2 | 910063000 |

> The UI→internal mapping is **not the identity** — UI `{0,1,2,3}` → internal `{4,1,3,2}`. A
> reimplementation must preserve the *internal* id (1..4) when it seeds the descriptor and builds the
> create packet, regardless of how the four buttons are laid out in the UI.

Selecting a class also sets the class **label** from message ids **14003..14007**, shows the class
name/description strings from the class template, plays the voice cue, and rebuilds the create
preview (§4.2). *(Human-readable class names are CP949 in `msg.xdb`, not reproduced — Open
question 7.)*

## 4.2 The create preview & appearance seeds (CODE-CONFIRMED)

A **single** create-preview actor (separate from the 5 slot previews) is built from a freshly zeroed
spawn descriptor seeded with the current create choices:

| Descriptor offset | Source | Meaning |
|---|---|---|
| +0x2C | sex selector | **sex/gender** — `1`, or `2` for internal class 2 (the female/alt-gender class) |
| +0x2E | face selector | **faceA** — face index, clamped to **1..7** (the `+`/`−` face buttons) |
| +0x30 | second appearance selector | **faceB** — hair / alt appearance seed (exact meaning unresolved, Open question 4) |
| +0x34 | class selector | **internal class id (1..4)** |

The create preview is placed at the stage centre (X ≈ origin − 1536.5, Z ≈ origin − 3538 — i.e. between
slots 2 and 3 in X and ~56 units nearer the camera than the row) and **idle-rotates** (a spin rate is
set on this single actor, unlike the standing slot previews), so the player sees their would-be
character before naming it. A per-class **stat preview** (six stat-label groups) is filled from the
class template (pure display).

## 4.3 Per-class starter equipment (CODE-CONFIRMED)

On create, four starter-equipment/visual ids are seeded into the descriptor by internal class id.
These are visual/equipment ids in the same id space as the item catalogue:

| Internal class | desc +0x88 | desc +0x98 | desc +0x108 | desc +0xB8 |
|---|---|---|---|---|
| 1 | 202110003 | 203110002 | 206110002 | 209110001 |
| 2 | 202220003 | 203220002 | 206220002 | 209220001 |
| 3 | 202130003 | 203130002 | 206130002 | 209130001 |
| 4 | 202140003 | 203140002 | 206140002 | 209140001 |

The `202xxx / 203xxx / 206xxx / 209xxx` families are the default weapon/armor/etc visual ids; which
descriptor slot is which equipment category is for the asset/struct authors to pin.

## 4.4 Name validation (CODE-CONFIRMED)

On create-confirm (and on rename, §6), the entered name is validated locally before any send:

- **Minimum length: 2** characters; an empty first character fails.
- **Allowed characters only:**
  - ASCII **lowercase `a`–`z`** (0x61–0x7A),
  - ASCII **digits `0`–`9`** (0x30–0x39),
  - **CP949 double-byte Hangul** (a valid lead byte + valid trail byte; a lone lead byte with an
    out-of-range trail fails).
- **Rejected:** uppercase Latin, punctuation, and any other byte.

So legacy character names are **Korean Hangul and/or lowercase-alphanumeric only**. A revival should
keep the same rule (or deliberately relax it) and surface the rejection as a message-id toast.

## 4.5 Create send & result (cross-reference, not owned here)

On a valid name, the client copies the CP949 name into a **52-byte** create buffer, sets a net-busy
guard, plays click SFX **861010101**, and sends the **create** message: **`major 1 / minor 6`,
52-byte body** (`{name + class/appearance seed fields}`).

> **CONFLICT to resolve at the wire layer (I do not own `opcodes.md` / `packets`).** `names.yaml`
> and `login_flow.md` map **`1/6 = CmsgLoginRequest`** (the ~52-byte login blob, first byte `0x2B`).
> The char-select lane shows the **character-create send is *also* `1/6`, 52 bytes** — and the
> create body did **not** start with the login `0x2B` sentinel. Two readings: (a) opcode `1/6` is
> **session-phase-dependent** (login phase = login blob; select phase = create char), or (b) the
> login analyst conflated the 52-byte create send with the login blob. **This must be disambiguated
> by the protocol analyst with a capture.** Until then, an engineer must treat `1/6` as carrying
> **both** meanings by session phase and must **not** assume the create body starts with `0x2B`.
> Recorded for `conflictsFlagged`; not resolved here. *(Note: the runtime read of the login blob
> in `login_flow.md` §4.2 resolved the **login-blob structure** and identified its optional field
> as the second-password / PIN, but it did **not** reach the character-create send and therefore
> does **not** resolve this collision.)*

The create result is the inbound **`3/23 SmsgCharCreateResult`** (owned by `login_flow.md`):
accept codes drive a scene refresh (and increment the account character count); failure codes map to
`msg.xdb` error strings in the ~200–212 id range.

---

# 5. Character deletion

Triggered by the **Delete** button (UI action **5**, CODE-CONFIRMED — see correction note below) → a confirm popup whose **Yes** runs the
delete. Guards: a valid selected slot and the net-busy flag clear. On confirm it plays SFX
**861010101**, sets net-busy, and sends the **delete** message: **`major 1 / minor 14`, 1-byte
body** = the slot index.

The delete result arrives on the inbound **8-byte char-manage result** message (`3/4
SmsgCharManageResult` per `opcodes.md`; result / subtype / ready-time):

- **result == 1, subtype == 2 ⇒ delete confirmed**: the account character count is **decremented**,
  the slot is cleared, and the preview row is rebuilt.
- **result == 0 with a future ready-time ⇒ delete cooldown**: a **"deletion forbidden today —
  `%d` hours `%d` minutes"** message is shown for ~5000 ms, where the remaining hours/minutes are
  computed from `(ready_time − now)` (a same-day delete lock). The CP949 format string is in the
  binary's data; the *id/string* is VFS-owned and not reproduced.

> The `3/4` handler is mislabelled "3/7" by some legacy tooling; **anchor to behaviour: the 8-byte
> result/subtype/ready-time message is `3/4 SmsgCharManageResult`** (a naming inconsistency already
> flagged by `login_flow.md`). Recorded for `conflictsFlagged`.

---

# 6. Character rename

A per-slot rename action opens the same name-entry textbox as create. On confirm the new name is
validated by the **same rule as §4.4** (min 2; lowercase a–z + digits + CP949 Hangul), copied to a
buffer, and sent as the **rename** message: **`major 1 / minor 13`, 18-byte body** = the new CP949
name (≤17 bytes + terminator).

The rename result is the inbound **`3/6 SmsgRenameCharResult`** (owned by `login_flow.md`): a nonzero
result is success (carrying the new name); a failure carries an error code that maps to a `msg.xdb`
string. The select screen also routes a rename outcome through the 8-byte char-manage result
(subtype 1) to refresh the displayed name.

> The three char-management C2S messages — **`1/7` select (2 bytes)**, **`1/13` rename (18 bytes)**,
> **`1/14` delete (1 byte)** — are the select-side counterparts of the inbound results, and (per the
> char-select lane) are **new C2S opcodes** relative to the older `names.yaml`. Their catalog/YAML
> are owned by the protocol author; recorded here as a flag (§7 / `conflictsFlagged`).

---

# 7. Enter-world handoff

Triggered by confirming the highlighted slot (double-click the preview, or the enter/OK button).
This is the load-bearing transition out of the front-end.

**Empty-slot branch first.** If the confirmed slot's descriptor name **== `"@BLANK@"`**, the slot is
empty and the action instead **opens the character-creation form** (§4). So "enter on an empty slot"
== "create a character".

**For a real character**, the sequence is (CODE-CONFIRMED, version token SAMPLE-VERIFIED):

1. Play the **enter SFX 920100200**.
2. Guard: the slot is valid (≤ 4), not locked, and not already entering.
3. Latch "entering" so further confirms are ignored.
4. Build the **40-byte** enter-game request: **slot index at +0**, and a **client-version token**
   stamped into the buffer. The token is computed live from the version file as
   **`10 × game.ver[field 5] + 9`**; for the sampled `game.ver` (`field 5 = 2114`) the token is
   **21149** (SAMPLE-VERIFIED — see `login_flow.md` §3.3). Send the **`1/9 CmsgEnterGameRequest`**
   message (40-byte body; owned by `login_flow.md` / `packets`).
5. **Cache the chosen slot locally for the world load (the only "preload" char-select performs):**
   - the **880-byte spawn descriptor** is copied to a global local-player descriptor, and
   - the **96-byte stats block** is copied to a global stats cache.
   Asset (skin/terrain) loading happens **later**, in the load/in-game states, fed by this cache.
6. Set the select window's **confirm-enter flag**. On teardown, the select window writes engine
   state **5 (In-game) / substate 8**, and the 5 preview actors + select camera are destroyed
   (`client_runtime.md` §5).

**What the client waits for after the send** (owned by `login_flow.md` / `client_runtime.md` §7.4):

- It does **not** block — it sends `1/9` and the engine advances to **state 2 (Load)** then **state 5
  (In-game)** which builds the real world.
- It waits for **`3/5 SmsgEnterGameAck`** (entry confirmation), then **`3/7 SmsgCharSpawnResult`**
  drives the actual local-player spawn **from the cached descriptor**.
- The spawn X/Z arrive in the server world-state packet (`4/1`), Y forced to 0; the 3×3 terrain ring
  then streams around the spawn (owned by `client_runtime.md` §7.4).

> A `1/7 CmsgSelectCharacter` (2-byte `{slot, flag}`) send also exists on the select path; whether it
> must precede every `1/9` enter, or only the first selection, is **not** resolved without a capture
> (Open question 8). Its catalog is owned by the protocol author.

---

# 8. The select-scene C2S send map (cross-reference table, not owned here)

For an engineer's mental model. **Byte shapes, catalog rows and packet YAMLs are owned by the
protocol author** (`opcodes.md` / `packets` / `login_flow.md`); this is a flow summary only. Every
send is gated by a **net-busy flag** so the client never has two character operations in flight; the
matching inbound result clears it.

| Action | Message (`major/minor`) | Body size | Trigger |
|---|---|---|---|
| Create character | `1/6` (collision — see §4.5) | 52 bytes | Create form confirm (valid name) |
| Select character | `1/7` | 2 bytes | slot select / pre-enter step |
| Enter game | `1/9` | 40 bytes | confirm a real (non-blank) slot |
| Rename character | `1/13` | 18 bytes | rename confirm (valid name) |
| Delete character | `1/14` | 1 byte | delete confirm |

---

# 9. Consolidated SFX, message-id and texture constants (CODE-CONFIRMED)

For the presentation/Godot engineer. Sound ids resolve through `sound_runtime.md`.

**Sound effect ids:**

| Id | Where |
|---|---|
| 861010101 | generic click / confirm (select scene) |
| 861010105 | login-scene enter / intro cue (login sub-state 2) |
| 861010106 | quit cue (version-mismatch quit, logout) |
| 920100100 | loading-screen cue (Load state) |
| 920100200 | enter-world cue (confirm slot) |
| 910062000 / 910063000 / 910064000 / 910065000 | per-class create voice (classes 1 / 2 / 3 / 4 — see §4.1 map) |

**Message-catalogue (`msg.xdb`) ids** (captions VFS-only):

| Ids | Meaning |
|---|---|
| 2204 | version-mismatch error box (login) |
| 4001–4022 | login form labels |
| 4023 / 4024 | quit-confirm prompts (login) |
| 4025 / 4026 / 4027 / 4028 / (4029) | login error toasts (ID short / PW empty / no servers / connect fail / endpoint fail) |
| 101 | timed-popup countdown suffix |
| 5001–5040 (+ locale banks) | localized server names |
| 14003–14007 | class labels (create form) |
| ~200–212 | character create/rename failure strings |

**Loading-screen textures** (chosen at random on entering the Load state): `loading.dds`,
`loading06.dds`, `loading08.dds`.

**Secondary-password dialog texture:** `data/ui/password.dds` (1024×1024 DXT3, full mips —
catalogued in `formats/ui_manifests.md` as "Secondary password dialog"); used by the
second-password / PIN modal (§1.4a).

**Other pinned constants:** ID-box max length **6**; PW-box max length **129**; IME slots ID **16**
/ PW **12**; face index range **1..7**; max slots **5**; preview stage X offsets
{−1560, −1548, −1536, −1524, −1512}, preview Z offsets {−3593, −3594, −3594.5, −3594, −3593},
preview scale **×3.0** (§3.3.1); preview yaw 0 = front / π = away (§3.3.2); stage origin
**(2048, 0, −6144)** (§3.7.2); row pivot / look-at anchor **(508, −9734)** / **(508.48, 69.89,
−9758.57)**; backdrop world **`data/map000`**, cell **`d000x10000z9990`** (§3.7.1); empty-slot
sentinel **`"@BLANK@"`**; version-token formula **`10 × game.ver[field 5] + 9`**; Save-ID INI
`DoOption.ini` `[DO_OPTION] OPTION_ID`; `Lastserver` & `servername` under registry
`HKLM\software\crspace\do`; `NEW_SERVER_INDEX` Lua global in `data/script/uiconfig.lua`;
second-password / PIN capacity **≤ 4 chars** (login-blob bound `< 5`, owned by `login_flow.md` §4.2).

---

# 10. End-to-end flow & engine-state map (CODE-CONFIRMED)

State numbers are from `client_runtime.md` §7. "→ state N" = an engine-state write that drives the
next scene.

```
[state 1: LOGIN]
  build login window from uiconfig.lua
  flow sub-state: 2 → (3,4,5 banner anim) → 6 (form active)
  OK / Enter:
      version gate (msg 2204 on mismatch → quit: state 6/2)
      → sub-state 29 (validate ID≥4 / PW≥1; fail → msg 4025/4026 → sub-state 6)
      → 31 EULA overlay → 32 wait-agree → 33 → 34 server-list fetch (lobby :10000)
      → 35 wait → 36 consume (empty → 4027; fail → 4028; else render)
  [SERVER SELECT, same window]
      → 37 server selected (persist Lastserver; randomized order; NEW badge)
      → 38 channel-endpoint fetch (lobby :10000+id) → 39 wait
      → second-password / PIN modal (≤4 chars; value → optional login-blob field)   [§1.4a]
      → 40 build TAB join string (account / PIN / host port) + secure context handoff
           (guard state 7); window exits
  game socket handshake (0/0 → 1/4 → 1/6 login blob: [0x2B][account\0][PIN\0])  [owned by login_flow.md / crypto.md]
  on auth OK: server sends 3/5 EnterGameAck → write state 2

[state 2: LOAD] → (optional state 3 OPENING) → [state 4: SELECT] on 3/1 CharacterList

[state 4: SELECT]   (a 3D GScene "select" on data/map000, frozen at 14:30 — §3.5.1/§3.6/§3.7)
  build select window + 5 live 3D preview actors from the 3/1 char list
  per-slot pick = hit-test the 3D row (Y band 70..92)
  Create (action 4) → class 0..3 → internal {4,1,3,2}, face 1..7, sex, starter gear
                      → name validate (min 2; a-z/0-9/Hangul) → send 1/6 (52B) → 3/23 result
  Delete (action 5) → confirm → send 1/14 (1B) → 3/4 result (subtype 2 = deleted; cooldown msg)
  Rename             → name validate → send 1/13 (18B) → 3/6 result
  Enter (confirm slot):
      empty slot ("@BLANK@") → open Create form
      real slot → SFX 920100200; send 1/9 (40B, slot@0, token 21149);
                  cache 880B descriptor + 96B stats; write state 5

[state 5: IN-GAME]  build world; wait 3/5 ack → 3/7 spawn (from cache) → 4/1 world-state X/Z (Y=0)
  logout / disconnect → state 4 (Select), never back to login (state 1)
  explicit quit → state 6 → state 8 (Exit);   fatal error → state 7 → state 8
```

---

## Open questions

1. **Login message id 4029.** An adjacent message-catalogue literal, almost certainly the
   channel-endpoint-fetch failure analogue (sub-state 39 timeout / endpoint connect fail), but not
   pinned to a call site. PLAUSIBLE only — confirm by tracing the sub-state 39 → 40 boundary or via
   a capture.
2. **ID-box max length 6.** Surprisingly short for an account name (validation only requires ≥ 4).
   Whether it reflects a legacy fixed-width account id, or is overwritten elsewhere, is unresolved.
   Wants a real `DoOption.ini` / capture. A revival may relax it.
3. **Full `status_code` enum.** Only `{0, 2, 3, 4, 24, 100}` are special-cased; the meaning of other
   in-range status values (and whether the server ever sends them) is capture-only.
4. **`faceA` vs `faceB` semantics.** Descriptor +0x2E is the face index (clamped 1..7); +0x30 is a
   second appearance seed (plausibly hair or skin tone) but is not labelled. Needs the other
   appearance increment buttons decoded and/or a create capture.
5. **`list.dat` byte layout.** The lobby-host file's 768-byte record (name @ +0, host @ +0x100) is
   `static`-derived and on-disk-unverified; the gap between the name and +0x100 (padding? flags?
   port?) and whether +0x100 also carries a port are unknown without a real file.
6. **Slot availability flag vs lock flag.** Two per-slot flag arrays both gate enter/render; the
   precise difference (selectable vs creating/locked vs cooldown) is inferred, not byte-confirmed.
7. **Class names / labels.** The UI→internal class map `{0,1,2,3} → {4,1,3,2}` is confirmed, but the
   human-readable class names (message ids 14003..14007, CP949, VFS-only) were not decoded. Needs a
   `msg.xdb` extract.
8. **`1/7 CmsgSelectCharacter` role.** The 2-byte select send sets the net-busy flag; whether it is
   a "lock this slot / fetch detail" pre-step that must precede every `1/9` enter, or only the first
   selection, is unclear without a capture or the inbound 2-byte select reply traced.
9. **EULA gating.** The EULA/accept overlay (sub-states 31/32) gates the server-list fetch, but
   whether it is shown every launch or only first-run (an INI/registry guard) was not determined.
10. **Second-password / PIN modal widget tree.** The PIN's existence, its first-class "is-PIN" input
    modelling, its ≤4-char capacity, and the fact that its value becomes the optional login-blob
    field are RUNTIME-CONFIRMED (§1.4a). The modal's **layout is now recovered** (§11.3: modal rect,
    the 2×5 scrambled keypad, the reset/OK/cancel tags and atlas source rects). What remains open is
    the live re-roll-on-Reset confirmation (debugger-testable) and whether the modal can be
    skipped/disabled per account; its labels are baked atlas art (no caption ids).
11. **Char-select 2D class icon.** No standalone class-icon widget keyed by a class index exists in
    the char-select 2D builder; per-slot class is conveyed by the descriptor-driven 3D preview (§3.3)
    plus the slot frame art (§11.5b). If a 2D class badge is desired in the revival, it must be added
    fresh - there is no legacy class→source-rect lookup to reproduce.
12. **Char-select camera tween duration.** The keyframe-transition normalizer decodes to ≈ 2.0 s, but
    an existing tool annotation reads "0.5 s". MEDIUM — resolve by timing the live transition or
    reading the millisecond deltas at the manipulator's update on a live select frame (§3.5.4).
13. **Char-select preview front/back facing.** The slot-preview yaw is literally 0 for occupied
    (front-facing) slots and π for locked slots, but the project's mesh-local `.skn` X-negation can
    flip apparent facing. Confirm on a live frame whether yaw-0 shows the character's **front**
    (expected) or back; if back, the importer must add π / mirror consistently with the X-negation
    (§3.3.2).

### Cross-spec conflicts recorded here (owners must resolve in their files)

- **`specs/ui_system.md` §6.3** marks sub-state **29 = "Server-list trigger"** and **31 = "Help
  screen"** as CODE-CONFIRMED. Both are wrong: **29 = OK-button credential validation**, **31 = show
  EULA overlay** (the help button is the separate action `i` / id 105). The owner of `ui_system.md`
  should correct those two rows. (§1.5)
- **Opcode `1/6` collision** (`opcodes.md` / `names.yaml` / `login_flow.md`): `1/6` is mapped to
  `CmsgLoginRequest` *and* is the 52-byte character-create send; the create body did not start with
  the login `0x2B` sentinel. The runtime login-blob read (`login_flow.md` §4.2) resolved the
  login-blob structure and named its optional field the second-password / PIN, but did **not** reach
  the character-create send and so does **not** resolve this collision. Still needs protocol-author
  disambiguation with a capture. (§4.5)
- **New C2S char-management opcodes** `1/7` (select, 2B), `1/13` (rename, 18B), `1/14` (delete, 1B)
  may be absent from `names.yaml` / `opcodes.md`; the protocol author owns adding them. (§6, §8)
- **Naming inconsistency**: the 8-byte char-manage result handler is labelled "3/7" by some legacy
  tooling but is behaviourally **`3/4 SmsgCharManageResult`** (result/subtype/ready-time). (§5)


---

# 11. Front-end scene layout — pixel-exact implementation tables (CODE-CONFIRMED)

> **What this section adds.** Sections 1-10 specify the front-end *flow* (state machine, validations,
> message ids, sends). This section adds the **layout/composition** layer an engineer rebuilds the
> screens from 1:1: the exact on-screen rectangles, the source sub-rectangles into each atlas DDS,
> the three-state (normal / hover / pressed) frame sources, and which texture each widget reads.
> Every rect below is a **literal layout constant read off the legacy scene builders** - neutral
> coordinate facts, not code. **No caption text is reproduced**; widgets that draw runtime text
> reference a numeric caption id from the section 1.9 / section 9 tables (resolved at runtime from
> `msg.xdb`). Korean labels that are **baked into the atlas art** are noted as "baked art" and carry
> no id.

## 11.0 Common composition model (CODE-CONFIRMED)

- **Design canvas:** `1024 x 768`, top-left anchored. The whole layout is **centered on screen**:
  the scene origin is set to `(screenWidth/2 - 512, screenHeight/2 - 384)` before any widget is
  placed, so all `(X, Y)` below are canvas-local. A handful of background bars are placed at a
  height-scaled Y (`Y = 326 * screenHeight / 768`); those are called out per row.
- **Widget construction convention.** Every widget is built with the same leading argument shape:
  `(textureId, X, Y, W, H, srcU, srcV, [hoverU, hoverV, pressedU, pressedV], zOrder)`. The literal
  `(X, Y, W, H)` is the on-screen rectangle; `(srcU, srcV)` is the top-left pixel of the source
  sub-rectangle in the referenced atlas DDS (its size equals the widget's `W x H` unless a frame is
  scaled). A three-state button carries three such source origins; a checkbox carries two
  (off / on). There is **no external rect table** - every rectangle is an inline construction
  argument, so the tables below are the complete layout source.
- **Widget kinds:** static image / sprite, container panel, single-frame button, three-state button,
  two-state checkbox, text label, and editable text box (the last two are the only runtime-text
  kinds). Dialog wrappers (quit-confirm, error) are panel subclasses.
- **Texture format.** The login / server-list atlases are loaded as DXT5 (the format selector passed
  to the loader is the FourCC `"DXT5"`); the char-select dim sheet uses an explicit
  raw/uncompressed format. Texture file formats and dimensions are catalogued in section 11.1.

## 11.1 Texture inventory - the front-end atlases (CODE-CONFIRMED paths; dims SAMPLE-VERIFIED)

All paths are **concrete VFS paths** (no id resolution). Dimensions/format were read from the
shipped DDS headers by a VFS harness (no pixel data extracted).

| Atlas (VFS path) | Dims | Format | Role |
|---|---|---|---|
| `data/ui/login_slice1.dds` | 1024x1024 | DXT2 | Login background art + stone chrome + **baked Korean label plates** (account / password / confirm / quit / save-id words) + the gold confirm-button face + the bottom bar |
| `data/ui/loginwindow.dds` | 1024x1024 | DXT5 | Login panel chrome (main panel art, listbox frame, scroll arrows + thumb, server-row buttons, lower confirm/cancel buttons); **also the char-select frame atlas** (shared) |
| `data/ui/loginwindow_02.dds` | 1024x1024 | DXT2 | Server-list parchment scroll panel + channel-selector tab blocks (the variant chrome) |
| `data/ui/InventWindow.dds` | 1024x1024 (HUD atlas) | DXT3/DXT5 | Reused for the login notice / error / quit dialogs **and** the PIN modal's framed background quad |
| `data/ui/password.dds` | 1024x1024 | DXT3 (full mips) | PIN modal: all digit-tile glyph art and the reset / OK / cancel button art |
| `data/ui/openning_scenario.dds` | 1024x2048 | DXT5 | Intro vertical-panorama scenario strip (pre-login slideshow; the four `openning_00N.dds` 1024x768 frames are the opening slides) |
| `data/ui/characwindow.dds` | 512x512 | RAW RGBA | Char-select window chrome (standalone char-select atlas; transparent panel) |
| `data/ui/mainwindow.dds` | (HUD atlas) | - | Char-select composited chrome / conditional overlay button source |
| `data/ui/CarrierPigeonPerson.dds`, `CarrierPigeonAll.dds`, `tradekeepwindow.dds` | (HUD/sub-window atlases) | - | Char-select composited chrome (reused sub-window atlases) |
| `data/ui/blacksheet.dds` | - | raw (explicit fmt) | Char-select dim/overlay sheet (dims unhovered slots / fades) |
| `data/ui/server_icon.dds` | 128x128 | DXT2 | Per-server badge icon in the server list |
| `data/cursor/stand.dds` | 32x32 | DXT2 | Default arrow cursor (all front-end screens); the in-engine cursor is re-targeted to the OS cursor position each frame |

> **Char-select chrome sourcing note.** The char-select 2D builder loads the seven shared atlases
> above (`loginwindow`, `mainwindow`, `InventWindow`, `CarrierPigeonPerson`, `CarrierPigeonAll`,
> `tradekeepwindow`, `blacksheet`); its **slot-frame and button art are sub-rects of
> `loginwindow.dds`** (the same login chrome family - section 11.5). A standalone
> `data/ui/characwindow.dds` also exists in the VFS and is the dedicated char-select chrome atlas;
> the heavily-used builder handles are `loginwindow.dds` and `mainwindow.dds`. No bespoke per-scene
> login atlas is needed beyond this set.

> **Fonts.** No font files exist in the VFS. Runtime text widgets render with the OS Korean system
> font (HANGUL charset, code page 949); the specific typeface depends on the host OS's installed
> Korean fonts. A revival must supply a CP949-capable Korean font.

## 11.2 Login scene - widget layout (CODE-CONFIRMED literals)

Atlas shorthand for this subsection: **A** = `login_slice1.dds`, **B** = `loginwindow.dds`,
**C** = `InventWindow.dds`, **D** = `loginwindow_02.dds`. Rect = `(X, Y, W, H)` on the 1024x768
canvas; "src" = `(U, V)` top-left into the named atlas. Three-state buttons list
`normal / hover / pressed` source origins. Action ids are the section 1.2 flow ids.

### 11.2a Upper window - main panel, server listbox, scroll controls

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action / caption |
|---|---|---|---|---|---|---|
| Main panel art | B | 0,110,1024,490 | 0,0 | image | - | - |
| Server dropdown / listbox container | B | 270,85,483,490 | 0,490 | panel | - | - |
| List scroll-up arrow | B | 467,86,13,10 | 483,490 | button | - | 106 |
| List scroll-down arrow | B | 467,455,13,10 | 505,490 | button | - | 107 |
| Scrollbar thumb | B | 469,98,9,9 | 496,490 | button | - | 108 |
| Listbox header / selection bar | B | 207,44,70,17 | 70,980 | image | - | - |
| 22 x server/channel row labels | (text) | X=50, Y=100..478 step 18, 383x50 | - | label | - | captions 4001..4022 |

### 11.2b Background + two channel-selector blocks

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | Action |
|---|---|---|---|---|---|
| Full background art panel | A | 0,0,1024,398 | 0,0 | panel | - |
| Second main-panel layer | B | 270,85,483,490 | 0,490 | panel | - |
| Header strip | B | 207,44,70,17 | 0,980 | image | - |
| Channel block (loop x2): header | D | X,390,174,21 | per-block | image | - |
| Channel block: body | D | X+47,97,100,372 | srcV,6 | image | - |
| Channel block: 3-state toggle | D | X-6,97,202,372 | 9,6 / 220,6 / 220,6 | 3-state button | 400, 401 |
| Channel block: 2 text labels | (text) | X,410,174,20 and X,430,174,20 | - | label | - |

- **Channel-block loop:** two iterations; block X starts at **30**, step **+233**; the body source V
  starts at **448**, step **+124**. The two toggles carry actions **400** and **401**.

### 11.2c Decoration sprites + server-row select buttons

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action |
|---|---|---|---|---|---|---|
| 3 x small badges / arrows | B | 0,0,60,39 | 500,786 | image | - | - |
| Scrollbar thumb (dynamic Y) | D | 0,(runtime),46,168 | 700,18 | image | - | - |
| 8 x server-row select | B | X=13, Y=66, 47x18, X step +47 | 596,985 / 643,985 / 643,985 | 3-state button | 115 + index |
| Large action button | A | 456,-3,111,38 | 792,398 / 602,416 / 602,416 | 3-state button | - |
| Its caption/face image | A | 407,-3,210,70 | 743,398 | image | - |

### 11.2d Notice / error dialogs (shared dialog panel)

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action / caption |
|---|---|---|---|---|---|---|
| Dialog #1 panel (notice) | C | 342,289,340,190 | 318,647 | panel | - | - |
| Dialog #1 body text | (text) | 10,100,330,20 | - | label (center) | - | caption 4023 |
| Dialog #1 OK | C | 120,136,113,40 | 302,900 / 302,900 / 415,900 | 3-state button | 113 |
| Dialog #2 panel (error) | C | 342,289,340,190 | 318,647 | panel | - | - |
| Dialog #2 body text | (text) | 10,100,330,20 | - | label (left) | - | caption 4024 |
| Dialog #2 OK | C | 120,136,113,40 | 302,860 / 302,860 / 415,860 | 3-state button | 114 |

The dialog panel source `(318,647) 340x190` is the shared notice/error/quit frame; the quit-confirm
and generic-error dialogs reuse the same rect (see section 11.2f for the trailing quit/error panels).

### 11.2e Bottom login form (the ID/PW box) - core fidelity target

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States / notes | Action |
|---|---|---|---|---|---|---|
| Bottom login-bar panel | A | 0, 326*H/768, 1024,442 | 0,582 | panel | Y scales with screen height | - |
| **Confirm button** (gold) | A | 456,166,112,39 | 154,398 / 378,398 / 378,398 | 3-state button | login submit; word baked into art | **102** |
| Confirm-button face plate | A | 265,0,494,113 | 0,469 | image | - | - |
| Inner form box (layout only) | (none) | 0,0,1024,100 | - | panel | invisible | - |
| Account-label caption art | A | 340,30,38,13 | 0,398 | image | **baked art** | - |
| Password-label caption art | A | 507,30,49,13 | 38,398 | image | **baked art** | - |
| Small decoration plate | A | 619,86,67,13 | 87,398 | image | **baked art** | - |
| **ID input field** | A | 390,32,102,13 | 615,404 | text box | max length 16 (UI cap; section 1.3) | **109** |
| **Password input field** | A | 568,32,102,13 | 615,404 | text box | max length 12, masked (password filter) | **110** |
| **Save-ID checkbox** | A | 694,86,13,13 | 717,398 (off) / 730,398 (on) | 2-state checkbox | pre-checked from saved-id (section 1.6) | **104** |
| Secondary bottom button (the login quit route, builder button #63) | A | 456,64,112,39 | 266,398,112,39 / 490,398 / 490,398 | 3-state button | advances toward the shared quit-confirm gate (see the quit-verdict note below); gates the shared ExitPanel `C` (318,647,340,190). Baked-glyph identity UNVERIFIED | **103** |

> The account / password / confirm / save-id Korean words are **baked into `login_slice1.dds`** (the
> caption-art plates and the confirm-button face) - they are **not** message-catalogue strings. Only
> the server-row labels (4001..4022) and the dialog bodies (4023/4024) are runtime text.

> **Login quit - there is NO dedicated bottom-bar quit sprite (CORRECTS any earlier assumption).**
> The login scene exposes two quit routes, neither of which is a stand-alone "quit" push-button face
> on the bottom bar:
> 1. **Keyboard accelerator.** A keyboard activation (the legacy input dispatcher's `'e'` accelerator)
>    triggers an immediate engine shutdown. No widget feeds this path - it is keyboard-only.
> 2. **Visible route via builder button #63** (the *Secondary bottom button* row above: `A`
>    src `266,398,112x39`, on-screen `456,64,112,39`). Activating it advances the login flow toward
>    the quit-confirm gate, whose modal box is the **shared ExitPanel** frame - `InventWindow.dds`
>    (`C`) source `(318,647) 340x190` drawn at on-screen `342,289,340,190` (the same notice/error/
>    quit frame, section 11.2d / 11.2f). The ExitPanel is a dialog *panel*, not a button.
> Any earlier note implying a dedicated bottom-bar quit button sprite is superseded: the quit affordance
> is (keyboard) + (button #63 -> shared ExitPanel). **UNVERIFIED:** the baked label glyph on button #63
> art (`login_slice1.dds` 266,398,112x39) - whether it reads the quit word or a register / find-password
> caption - needs a texture peek of `login_slice1.dds`; flag for the VFS/texture lane.

### 11.2f Trailing controls + quit/error dialogs

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind | States (N/H/P) | Action |
|---|---|---|---|---|---|---|
| PIN modal sub-window mount | - | 347,173,329,422 | - | child window | initially hidden (see section 11.3) | - |
| Small sub-panel | (none) | 356,531,313,132 | - | panel | - | - |
| Image plate | A | 67,48,178,13 | 0,437 | image | - | - |
| Image plate | A | 0,100,313,32 | 289,437 | image | - | - |
| Button | B | 40,82,110,38 | 520,492 / 520,492 / 635,492 | 3-state button | 111 |
| Button | B | 164,82,110,38 | 750,492 / 750,492 / 865,492 | 3-state button | 112 |
| Quit-confirm dialog panel | C | 342,289,340,190 | 318,647 | panel | - | - |
| Generic error dialog panel | C | 342,289,340,190 | 318,647 | panel | - | - |

## 11.3 PIN / second-password modal - layout & keypad behaviour (CODE-CONFIRMED)

The PIN modal (section 1.4a) is the second-password child window mounted over the login background.
It uses two atlases only: **`password.dds`** (all digit-tile and reset/OK/cancel button art) and the
shared dialog/frame atlas (`InventWindow.dds`) for the framed background quad - source rect
`(318, 647, 340, 190)` (`srcU=318, srcV=647, W=340, H=190`), the same notice/error/quit frame
(section 11.2d). This is the dragon-frame background quad described in the table below.

- **Modal panel rect:** `347, 173, 329, 422` on the canvas (panel-local coordinates below are
  relative to this panel).
- **Dragon-frame background quad.** The modal background is the framed dragon quad - a sub-rect of
  `InventWindow.dds`, source `(318, 647, 340, 190)`, NOT the whole 1024x1024 texture. The source art
  is `340 x 190` but the on-screen panel is `329 x 422` (taller than the source), so the frame is
  drawn **stretched**: render it as a **NinePatch** (or equivalent corner-preserving stretch) from
  `(318, 647, 340, 190)` up to the `347, 173, 329, 422` panel rect. The keypad tiles and buttons
  below are NOT stretched - they are drawn at their native `password.dds` source sizes
  (52x52 / 154x58 / 58x30).
- **No runtime text - the warning line is baked atlas art (CONFIRMED).** The number-entry caption,
  the **red warning line**, the button faces, and the modal title are all **baked into the atlas
  art** (the digit/button glyphs into `password.dds`; the title + warning line into the
  `InventWindow.dds` dragon-frame quad). The modal performs **no caption lookup at all** - there is
  no message-catalogue id for the warning line. A revival must therefore render the warning line as
  part of the dragon-frame sub-rect art and must NOT wire it to a `msg.xdb` / message-catalogue
  caption. (A dynamic warning string would be a NEW addition, not a fidelity match.) The entered PIN
  is held as an internal string (<= 4 chars) and shown as a masked `*`-per-digit string; there is no
  text-box widget.

### 11.3a Keypad tile grid (2 rows x 5 columns)

| Property | Value |
|---|---|
| Column count | 5 (positions 0..9, row-major) |
| Tile X (panel-local) | `55 * (p mod 5) + 28` -> columns **28, 83, 138, 193, 248** |
| Tile Y (panel-local) | **170** for top row (p < 5), **230** for bottom row (p >= 5) |
| Tile size | **52 x 52** |
| Column spacing | 55 px |
| Row spacing | 60 px |

### 11.3b Scrambled-digit glyphs (sourced from `password.dds`)

The keypad does **not** build one button per position. For **each** of the 10 positions it builds a
stack of **10 overlapping digit-graphic buttons** (one per digit value 0..9) at the same tile rect -
**100 button widgets total**. Per position, exactly one digit graphic is made visible; the rest are
hidden. The visible digit at position `p` is `perm[p]` from the scramble (section 11.3c).

- **Digit glyph source.** For digit value `d`, the source **row** is `d * 52` (rows
  `0, 52, ..., 468`). The three button states read from three source **columns**:
  **normal = 560, hover = 664, pressed = 612**, each tile `52 x 52`. So digit `d`'s normal-state
  glyph is `password.dds` source rect `(560, d*52, 52, 52)`.

### 11.3c Keypad scramble (CODE-CONFIRMED; live re-roll-on-reset UNVERIFIED)

The digit->position mapping is produced **entirely client-side** - there is no server permutation and
no fixed local table:

1. On modal open (and on every Reset press), the C-runtime RNG is seeded from the **current local
   wall-clock time**.
2. A textbook **Fisher-Yates shuffle** permutes the digit pool `[0..9]` (each index draws a random
   value and swaps; the random range is extended past the 15-bit RNG limit).
3. For each position `p`, the digit-button matching `perm[p]` is set visible and the other nine
   hidden - so position `p` displays digit `perm[p]`.

Result: a **fresh random permutation of 0-9 every time the modal opens and every time Reset is
pressed**. The on-open re-roll is statically confirmed; the live re-roll on Reset is debugger-
testable and currently **UNVERIFIED**.

### 11.3d Reset / OK / Cancel buttons + key tags

Button **tags** are integer ids stored on each widget and read back by the keypad event handler.

| Role | Tag | Rect (panel-local, X,Y,W,H) | `password.dds` src (normal / hover / pressed) | Behaviour |
|---|---|---|---|---|
| Digit tiles 0-9 | 0..9 | per section 11.3a (52x52) | 560 / 664 / 612, row = digit*52 | append digit (cap 4), mask `*` |
| Reset (clear + re-shuffle) | 11 | 243,133,58,30 | 663,8 / 663,88 / 663,48 | re-run scramble (section 11.3c) |
| OK (submit) | 12 | 90,290,154,58 | 330,0 / 330,116 / 330,58 | submit second password (PIN -> login blob, section 1.4a) |
| Cancel (close/abort) | 13 | 90,350,154,58 | 486,0 / 486,116 / 486,58 | close / abort modal |

> The OK submit hands the PIN to the protocol layer (the second-password / PIN destination is the
> optional login-blob field - owned by `login_flow.md` section 4.2; the in-game gift-character
> variant of this modal routes its submit through the net handler with a separate constant). The
> modal fires no VFX and no PIN-specific SFX of its own.

## 11.4 Server-list overlay - widget layout (CODE-CONFIRMED literals)

Server selection is a **visibility state (sub-state 37) of the same login window** (section 2), so it
reuses the same four atlases loaded once at login-scene build. Shorthand: **A**=`login_slice1.dds`,
**B**=`loginwindow.dds`, **C**=`InventWindow.dds`, **D**=`loginwindow_02.dds`.

| Role | Atlas | Src rect (U,V,W,H) | Dst (X,Y) | Kind | Action / caption |
|---|---|---|---|---|---|
| Server-list backdrop band | A/D | 0, 326*.., 1024, 442 | 0, 326*H/768 | image (dimmed band) | - |
| Parchment row/tab PLATE - normal state | D | 9,6,202,372 | col0 dst 24,97,202,372 / col1 dst 257,97,202,372 | 3-state plate | row ids 400/401 |
| Parchment row/tab PLATE - hover/pressed state | D | 220,6,202,372 | (same dst as normal) | 3-state plate | row ids 400/401 |
| Parchment scroll BODY - channel column 0 | D | 448,6,100,372 | dst 77,97,100,372 | image | - |
| Parchment scroll BODY - channel column 1 | D | 572,6,100,372 | dst 310,97,100,372 | image | - |
| Parchment scrollbar thumb | D | 700,18,46,168 | dst 0,(runtime),46,168 | image (dynamic Y) | - |
| Server-row buttons x10 (loop) | B | 13,66,47,18, X step +47 | sprite row y=985 | 3-state button | **115..124** (id-115 = index) |
| List column header labels | (text) | - | in scroll | label | captions 4029..4032 |
| List up / down arrows | B | 690,985 / 784,985 | window-anchored | button | - |
| **Refresh button** | A | 456,-3,111,38 | 792,398 | button | **105** (10 s cooldown -> re-enter fetch) |
| Refresh-button label plate | A | 407,-3,210,70 | 743,398 | image (gold plate) | **baked art** |
| Availability indicator (per row) | (text) | - | per row | label | population captions 6001..6005 |
| Connecting-dialog FRAME (endpoint wait, state 39) | C | 318,647,340,190 (== shared notice panel) | 342,289,340,190 (centered) | panel | runtime body caption (msg.xdb id; not a texture rect) |
| Sword/arrow cursor | `data/cursor/stand.dds` | - | follows mouse | sprite | verified vs `data/cursor/game.ver` |

- **Per-server-row record:** 8 bytes/entry (decode owned by section 2.2 / `login_flow.md`): `+0` u16
  server id, `+2` i16 status, `+4` i16 population code (color thresholds 500/800/1200), `+6` i16
  extra/flag. Row count from the window's row-count field. Population captions **6001..6005**; column
  headers **4029..4032**; unknown-id fallback **5901**.
- The Refresh and Cancel button **words** may be baked atlas art (gold plates) rather than caption ids
  - UNVERIFIED which; the rects (Refresh `456,-3,111x38`; Cancel = login action 111) are firm.
- The left-scroll calligraphy header is a runtime caption (integer id, **UNVERIFIED** exact id -
  needs a `msg.xdb` extract).
- **Parchment plate vs server-row button (do not confuse).** The `202x372` row/tab PLATE
  (`loginwindow_02.dds` src normal `9,6` / hover-pressed `220,6`) is the parchment BACKING the row
  face draws over - it is DISTINCT from the small clickable server-row button sprite
  (`loginwindow.dds` src `596,985` / `643,985`, `47x18`, actions 115..124 above). The plate's source-UV
  is FIXED (does not advance per channel column). The `100x372` scroll BODY source-U advances **+124**
  per channel column (`448` -> `572`); `srcV = 6` is fixed for both parchment quads; only two channel
  columns are built (channel-tab count = 2). The parchment chrome lives entirely on
  `loginwindow_02.dds`.
- **Connecting dialog == the shared notice panel (not a distinct sub-rect).** The dialog shown during
  the channel-endpoint wait (state 39) is the SAME `InventWindow.dds` frame sub-rect `(318,647,340,190)`
  used for notice / error / quit dialogs (section 11.2d), drawn at on-screen `342,289,340,190`. There is
  no dedicated connecting-frame texture rect - only the runtime body caption differs (a `msg.xdb`
  caption id, not reproduced here). **Behavioral nuance (UNVERIFIED, static-only):** the server-list
  WAIT (state 35) raises the channel-tab parchment panel + refresh button, NOT the notice frame; only
  the endpoint WAIT (state 39) raises this connecting frame.

## 11.5 Char-select scene - widget layout (CODE-CONFIRMED literals)

The char-select 2D builder composites its chrome from the shared atlases (section 11.1 note). The
slot-frame and Create/Delete/Enter button art are **sub-rects of `loginwindow.dds`** (shorthand **T1**
below); one conditional overlay button is sourced from `mainwindow.dds` (**T2**). The 5 live 3D
preview actors and the preview camera are owned by sections 3.3-3.5 (not layout art).
Rect = `(X, Y, W, H)`.

### 11.5a Window chrome / root panels

| Role | Atlas | Rect (X,Y,W,H) | Src (U,V) | Kind |
|---|---|---|---|---|
| Root window frame panel | (none) | X=(W/2-288), 575, 244,187 | - | panel |
| Title/info chrome plate A | T1 | 0,12,200,46 | 608,793 | image |
| Title/info chrome plate B | T1 | 200,0,176,58 | 608,735 | image |
| Title/info chrome plate C | T1 | 376,12,201,46 | 608,689 | image |
| Centered char-info panel | (none) | X=(W-215)/2, 0, 244,187 | - | panel |
| Char-info background art | T1 | (centered),0,215,147 | 556,542 | image |

### 11.5b Character SLOT tabs (the per-slot frame art) - 3 slots, 113x40, all from `loginwindow.dds`

Each slot-select button **is** the per-slot frame art; its normal-state source rect gives the slot
frame graphic. Slot occupancy and the per-slot 3D preview / name-level-class display are
descriptor-driven (sections 3.2-3.3), not layout art.

| Slot | Action id | Rect (X,Y,W,H) | Normal (U,V) | Hover (U,V) | Pressed (U,V) |
|---|---|---|---|---|---|
| Slot 1 | 1 | 67,17,113,40 | 675,795 | 675,795 | 483,883 |
| Slot 2 | 2 | 232,7,113,40 | 640,742 | 640,742 | 483,923 |
| Slot 3 | 3 | 393,17,113,40 | 625,691 | 625,691 | 483,963 |

### 11.5c Create / Delete / Enter buttons - 59x20, all from `loginwindow.dds`

| Role | Action id | Rect (X,Y,W,H) | Normal (U,V) | Hover (U,V) | Pressed (U,V) |
|---|---|---|---|---|---|
| **Create** | **4** | 130,112,59,20 | 0,1004 | 0,1004 | 59,1004 |
| **Delete** | **5** | 42,112,59,20 | 118,1004 | 118,1004 | 177,1004 |
| **Enter** | **6** | 112,112,59,20 | 236,1004 | 236,1004 | 295,1004 |
| Conditional overlay button | 61 | 20,112,95,20 | (T2 `mainwindow.dds`, computed) | - | pressed V=500 |

The conditional overlay button (action 61, from `mainwindow.dds`) is built only when a slot condition
holds; its role (a "select/play" overlay on the active slot) is **UNVERIFIED**. The Create/Delete/
Enter rects/actions agree with the section 4/section 5 correction (action ids 4/5/6, not atlas-X
coordinates).

### 11.5d Per-slot info plates + number cells (chrome detail)

The per-slot info region draws chrome plates plus a grid of **placeholder number-glyph cells** whose
digits are substituted at runtime from the slot's stat/level values (the build-time source rects are
placeholders). All from `loginwindow.dds` unless noted.

| Role | Rect (X,Y,W,H) | Src (U,V) |
|---|---|---|
| Info plate | 0,142,215,147 | 556,542 |
| Info plate | 215,0,29,22 | 556,729 |
| Info plate | 0,352,29,40 | 556,689 |
| Number-glyph cell | 12,238,34,18 | 297,980 |
| Number-glyph cell | 12,262,34,18 | 331,980 |
| Number-glyph cell | 12,286,34,18 | 365,980 |
| Stat-number cell block (x7) | X=46/51, Y=193..286 step 24, 157x18 | 140,980 (placeholder) |

The per-slot info-line **caption labels** carry caption ids **48001, 48003, 48004, 48005** (the
name/level/position label set); additional chrome captions are **46001, 46002, 14001, 14002, 2206,
63030** (all integer ids; text VFS-only, not reproduced). The scene-ambient VFX id is **380003000**
(section 3.6.5 / effect catalogue); the enter-world cue is SFX **920100200** (section 9).

> **No standalone class-icon-by-index widget exists in the 2D builder.** Per-slot class is conveyed
> by the descriptor-driven 3D preview (section 3.3) and the slot frame art (section 11.5b); the
> inline-source cells in the info region are **number-glyph placeholders**, not class icons. A 2D
> class badge keyed by a class index is **UNVERIFIED / absent** in this builder (Open question 11).

## 11.6 Intro / opening sequence (CODE-CONFIRMED art; sequencing per section 1.5)

Before the login form (login flow sub-states 1-5), the engine plays a fullscreen slideshow and a
banner-pan animation:

- **Slides:** four 1024x768 opening frames (`data/ui/openning_001.dds`..`004.dds`) plus the tall
  1024x2048 scenario strip (`data/ui/openning_scenario.dds`, scrolled vertically).
- **Banner pan:** two banner panels animate into place (their Y advances from off-canvas to a settled
  position) - pure procedural positional animation, no external asset.
- **Intro cue:** the login-enter / intro effect id **861010105** fires at the intro sub-state
  (SFX/VFX; resolves to `data/sound/2d/<id>.ogg` and/or the effect catalogue - section 9 /
  `sound_runtime.md`).
- **Loading transition:** on the credential-submit join (sub-state 40), transition effect ids
  **30000 / 10001** fire (fade into world-load).

> The exact slideshow timing/sequence is owned by the section 1.5 sub-state machine (states 1-5);
> section 11.6 records only which art each step composites.

## 11.7 Layout known-unknowns (carried for the engineer)

- **Refresh / Cancel server-list button text:** baked atlas art vs caption id - UNVERIFIED.
- **Server-list calligraphy header caption id:** an integer caption id, exact value needs a `msg.xdb`
  extract.
- **Char-select conditional overlay button (action 61):** role (select vs play overlay) UNVERIFIED;
  its `mainwindow.dds` source rect is computed at runtime, not a literal.
- **Char-select number-glyph runtime mapping:** the build-time cell source rects are placeholders;
  the per-digit atlas mapping at runtime (analogous to the PIN digit/row scheme) was not chased.
- **2D class-icon-by-index widget:** absent in the char-select 2D builder (descriptor-driven instead)
  - Open question 11.
- **Live PIN re-roll on Reset:** static-confirmed, debugger-testable, UNVERIFIED live.
- **`characwindow.dds` internal frame rects:** the dedicated char-select atlas's sub-rects were not
  individually catalogued (the builder primarily uses `loginwindow.dds` / `mainwindow.dds` sub-rects).
