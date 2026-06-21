<!--
verification: pvp/fame/public-peace/faction presence-and-absence findings [confirmed] (static-IDA):
  the public-peace NPC is GOLD-FUNDED public-order contribution (NOT player karma); PK is a binary
  MODE TOGGLE with NO numeric karma score; the two-side faction is a SERVER-ASSIGNED brood-war side
  byte (via 4/126) that is ACTIVE ONLY on special (brood-war) maps; a personal/global faction
  allegiance is ABSENT; and the PK-penalty / revenge subsystem is DISTINCT from the FATE relation
  bond bytes — all control-flow / presence-confirmed on build 263bd994.
  OPEN: the RAW fame currency offset on the player struct is UNVERIFIED (the buy flow routes through a
  generic confirm-dialog category and never reads the stat directly) — recorded as an open question.
  RUNTIME-ONLY: the on-wire VALUE meanings (the public-peace zone semantics, the brood-war side
  contract values, the fame cost/charge) stay capture/debugger-pending.
ida_anchor: 263bd994
ida_reverified: 2026-06-20
evidence: [static-ida]
sample_verified: false
note: |
  IDB SHA 263bd994, CYCLE 7 (2026-06-20) — new spec. Promotes the PvP / fame / public-peace / PK /
  two-side-faction material from dirty-room analyst notes. The FATE bonded-relationship system itself
  is owned by social.md and only cross-referenced here (FATE rivalry is the relation-type that touches
  PvP). The raw fame currency offset is left explicitly OPEN/UNVERIFIED — not invented.
-->

# PvP — Fame, Public-Peace, PK Mode & Two-Side Faction — Clean-Room Specification

> Neutral, rewritten specification promoted from dirty-room analyst notes under **EU Software
> Directive 2009/24/EC Art. 6** (decompilation permitted solely to achieve interoperability). It
> contains **no decompiler output, no pseudo-code, no legacy symbol names, and no binary virtual
> addresses**.
>
> **Spec path (cite this):** `// spec: Docs/RE/specs/pvp.md`
>
> **Scope.** The reputation / law / allegiance axes that surround player-versus-player play in the
> original client: the **fame** spend-side reputation, the **public-peace** gold-funded public-order
> system, the **PK mode toggle**, and the **two-side brood-war faction**. It also records the two
> deliberate **absences** the binary proves (no numeric karma; no personal/global faction
> allegiance) and pins the **distinction** between the PK-penalty / revenge subsystem and the FATE
> relation bond. Neighbours own and are cited, not duplicated:
> - `social.md` — the **FATE bonded-relationship system** (couple / master / disciple / rival /
>   training): the 11 relation types, the relation-type byte at actor +96, and the relation-pair-state
>   byte at actor +172. This spec only notes that FATE **rivalry** is the relation-type that relates to
>   PvP, and that the PK subsystem is separate from those FATE bytes.
> - `world_systems.md` — the brood-war shared-map / dark-subsystems family the two-side faction lives
>   inside.
> - `npc_interaction.md` (Lane 2) — the NPC-interaction substrate that the fame-buff NPC and the
>   public-order contribution NPC are built on.
> - `opcodes.md` / `packets/` — the wire catalogue and field specs for the `4/126` side-assignment
>   message.

---

## 1. The four axes at a glance

The original client surfaces **four distinct** reputation / law / allegiance mechanics around PvP.
They are independent of one another and independent of the FATE bond system. *([confirmed]* on the
presence/absence of each axis via static analysis.)*

| Axis | What it is | Numeric on the player? | Where it lives |
|---|---|---|---|
| **Fame (명성)** | a **spend-side reputation** — earned standing that is *spent* at a fame-buff NPC and donated to a guild | yes (a fame value exists), but the **raw offset is OPEN** (§2) | §2 |
| **Public-peace (치안)** | a **gold-funded public-order** meter raised by paying gold to a public-order NPC — **not** player karma | a zone/region meter (u16, cap ~700), **not** a per-player karma score | §3 |
| **PK / peace-attack** | a **binary mode toggle** (peace mode ↔ attack mode) — **no** accumulating karma value | a single mode flag, not a score | §4 |
| **Two-side faction** | a **brood-war side byte** assigned by the server, **active only on special maps** | a side byte `{1,2}`, conditional | §5 |

Two systems the binary proves are **absent** are recorded in §4 (no numeric karma) and §5 (no
personal/global faction allegiance). The PK-penalty / revenge subsystem versus the FATE bond bytes is
pinned in §6.

---

## 2. Fame (명성) — a spend-side reputation

Fame is **present** and behaves as a **spend-side reputation**: standing the player accrues and then
**spends** at an NPC for temporary buffs, and **donates** to a guild. *([confirmed]* that fame is a
spend-side reputation surfaced by the panels and the data table below.)*

### 2.1 The fame surfaces

| Surface | Role |
|---|---|
| **Fame-buff NPC** | the NPC interaction where **fame is spent** to purchase temporary buffs. It presents a paged grid of purchasable buffs; selecting one opens a confirm-purchase dialog (a generic info/confirm dialog category — see §2.2). Built on the NPC-interaction substrate (`npc_interaction.md`, Lane 2). |
| **Fame-state HUD strip** | a small HUD strip of active **fame-buff indicator icons** (up to a few slots), drawn from a contiguous block of buff-icon sprite indices. Shows which fame buffs are currently active. |
| **Rank-state HUD strip** | a sibling HUD strip of active **rank** indicator icons, drawn from an adjacent block of buff-icon sprite indices. The rank counterpart to the fame-state strip. |
| **Guild fame donation panel** | the panel where fame is **donated to the player's guild**. |
| **`nicktofame.scr`** | a CP949 data table (under the script data corpus) mapping a nick/name to fame/title information — loaded as part of the boot data-table corpus. |

### 2.2 The fame-buy flow — server-authoritative charge

Selecting a buff on the fame-buff NPC grid does **not** read or decrement a fame stat client-side.
The purchase is routed through the client's **generic confirm-dialog** path (an info/confirm dialog
category), and the **server validates and charges** the fame cost. The client merely requests the
purchase and shows the confirm dialog. *([confirmed]* that the buy flow goes through the generic
confirm-dialog category and that the panel functions never read a fame stat directly.)*

### 2.3 OPEN — the raw fame currency offset (UNVERIFIED)

> **OPEN QUESTION (UNVERIFIED).** The **raw fame currency field on the player struct is not pinned.**
> Because the buy flow rides the generic confirm-dialog/charge path (§2.2) and never reads the fame
> stat directly, the fame value's storage offset was **not** recovered statically. **Do not invent an
> offset.** Settling it requires either the confirm-dialog category builder or a server-driven
> fame-update handler (a netcode lane) — or a live read. Until then, fame is known to **exist** and to
> be **spend-side**, but its on-struct location and its cost/charge values stay
> **capture/debugger-pending**.

No dedicated "fame" opcode was isolated statically; fame buy/donate ride the generic confirm-dialog /
send path. The category→opcode mapping for the fame confirm is an open netcode-lane follow-up
(UNVERIFIED).

---

## 3. Public-peace (치안) — gold-funded public order, NOT karma

Public-peace is **present** and is a **gold-funded public-order contribution system**, surfaced by a
dedicated NPC contribution dialog. **It is emphatically not a player karma / wanted-level score.**
*([confirmed]* that public-peace is a gold contribution NPC raising a capped meter, not a per-player
penalty stat.)*

### 3.1 What it is

- A **public-order contribution NPC** presents a **quantity-selector donation dialog** (a `+` / `-`
  stepper over a contribution amount, with confirm / cancel and a description). Built on the
  NPC-interaction substrate (`npc_interaction.md`, Lane 2).
- The player **pays gold** to raise a zone/region **public-peace meter**. The contribution amount is
  stepped in fixed increments per click; the gold cost scales with the amount via a price table.
- The **current public-peace value** is read from a **UI value field** — a **16-bit (u16)** quantity
  with a **cap of approximately 700**. The dialog refuses a contribution that would push the meter
  past the cap, and the confirm path refuses if the player's gold wallet cannot cover the cost.

### 3.2 The decisive distinction

> **Public-peace is PUBLIC-ORDER FUNDING, not player karma.** It is a **gold-funded zone/region
> meter** the player contributes toward — there is **no** per-player karma, wanted-level, or
> PK-penalty score behind it. Any earlier reading of public-peace as "karma" is **wrong**: the binary
> shows a gold→public-order-meter contribution, capped near 700, not an accumulating personal penalty.

### 3.3 RUNTIME-ONLY

The **zone/region semantics** of the public-peace meter (what raising it actually does in the world,
the exact gold-per-point price, and the precise cap) are **server-contract-dependent** and stay
**capture/debugger-pending** (RUNTIME-ONLY). The structural facts — gold-funded, u16, cap ~700,
stepped amount — are static-confirmed.

---

## 4. PK / peace-attack — a MODE TOGGLE, not a karma value

PK is modelled as a **binary mode toggle**, not as an accumulating score. The client tracks a
**peace-mode ↔ attack-mode** flag and renders it as an animated HUD mode badge. *([confirmed]* that
PK is a binary mode flag with no numeric karma score found statically.)*

### 4.1 The PK mode flag

- A single **local PK / peace-attack mode flag** drives an **animated HUD mode badge**: in one state
  the badge shows the **attack (PK) mode** animation, in the other the **peace mode** animation. The
  flag is binary — there are only the two modes.
- A **"no-PK-penalty" alarm banner** is shown on screen when PK currently incurs **no penalty** (a
  zone/state warning) — i.e. the law axis is a **mode + penalty-state warning**, not a number.

### 4.2 The decisive absence

> **There is NO numeric karma value.** The PK axis is a **mode toggle plus a penalty-state alarm** —
> there is **no** karma point, **no** wanted/murderer count, and **no** accumulating PK-reputation
> stat on the player. **Do not build a numeric karma system.** No such score was found statically;
> the law axis is entirely the binary mode flag and the penalty-state warning. *([confirmed]* the
> absence of a karma score.)*

The PK-penalty / revenge **tracking** that does exist is a *separate* subsystem, distinct from both
this mode flag and the FATE bond bytes — see §6.

---

## 5. Two-side faction (brood-war side) — present but conditional

A **two-side faction** is **present but CONDITIONAL**: it is a **server-assigned side byte** that is
**active only on special (brood-war) maps**. A **personal / global faction allegiance is absent.**
*([confirmed]* both halves: the map-scoped side byte exists; a global allegiance does not.)*

### 5.1 The brood-war side byte — server-assigned, map-scoped

- The player carries a **side byte** with value `{1,2}`. It is **server-assigned**, delivered via the
  inbound message **`4/126`** (a small body that, when its set-flag is on, writes the side onto the
  local player and refreshes a dependent panel). *([confirmed]* the side is assigned by `4/126`; see
  `opcodes.md` / `packets/` for the wire identity and field spec.)*
- The side rule is **enforced only on special maps** — maps that carry a particular map-record flag.
  On such a map, an action against a target of the **opposing side** is blocked (the enemy-side
  guard); off such a map the side byte has no enforcement effect.
- The side also drives **nameplate side-coloring** on those maps (own side vs enemy side rendered in
  different colours, with the two side names from the message table).
- The whole two-side mechanic belongs to the **brood-war / guild-war** zone subsystem (the brood-war
  UI family of ally-state / list / map-info panels), documented under `world_systems.md` — it is the
  **two-camp split inside the war event**, gated by the special-map flag, **not** a standing personal
  faction.

### 5.2 The decisive absence — no global faction allegiance

> **There is NO account-wide / personal faction allegiance.** There is **no** "choose your faction"
> at character creation, **no** permanent server-wide allegiance stat, and **no** faction-rank ladder.
> The **only** two-side mechanic is the **map-scoped brood-war side** assigned by `4/126`, active
> only on the special (brood-war) maps. The genre's "faction" role is filled instead by guild +
> brood-war. **Both halves hold:** the conditional brood-war side is present; a global allegiance is
> absent. *([confirmed]* the absence of a personal/global allegiance.)*

### 5.3 RUNTIME-ONLY

The **contract values** behind the side byte (what the side assignment means in a given war event,
the side-vs-side combat semantics, the precise special-map flag conditions on the wire) are
**server-contract-dependent** and stay **capture/debugger-pending** (RUNTIME-ONLY). The structural
facts — a `{1,2}` side byte, assigned by `4/126`, enforced only on special maps — are
static-confirmed.

---

## 6. PK-penalty / revenge vs the FATE bond — separate subsystems

The PK-penalty / revenge tracking is a **distinct subsystem** from the FATE bonded-relationship
bytes. They must not be conflated. *([confirmed]* that the PK/penalty subsystem is separate from the
FATE relation bytes.)*

- The **PK-penalty / revenge** subsystem tracks a **revenge target** record (who killed you — an
  actor/team identification) and presents the **PK-penalty / no-PK-penalty alarm banners**. This is
  the **law / retaliation** axis. The full revenge record fields and any PK-penalty status enum are
  **not** literal in the binary and stay **capture/debugger-pending** (RUNTIME-ONLY).
- The **FATE bond** bytes are a **separate, social** axis owned by `social.md`: the **relation-type
  byte at actor +96** (which FATE relation a related actor holds — couple / master / disciple / rival
  / training / …) and the **relation-pair-state byte at actor +172** (the paired relation state used
  by nameplate/relation colouring). These are **bonded-relationship** state, not PK state.
- The only intersection is conceptual: FATE **rivalry** (the rival relation-type) is the FATE relation
  that relates to PvP. But the **rival relation byte is not the PK-penalty record** — setting a rival
  bond does not write the revenge/PK state, and vice versa. *([confirmed]* the rival relation is
  tracked as a FATE roster relation, separate from the PK-penalty subsystem.)*

> **Do not merge these.** The PK-penalty / revenge subsystem (this spec) and the FATE relation bytes
> at actor +96 / +172 (`social.md`) are independent. Cross-reference `social.md` for the full FATE
> relation-type enum; this spec only records that the two are separate subsystems.

---

## 7. Cross-reference map

| This spec covers | Owned elsewhere — cite, don't duplicate |
|---|---|
| Fame spend-side reputation, fame-buff NPC, fame/rank HUD strips, `nicktofame.scr` (§2) | `npc_interaction.md` (Lane 2 — the NPC substrate); `opcodes.md` / `packets/` (any eventual fame opcode) |
| Public-peace gold-funded public-order NPC, u16 cap ~700 (§3) | `npc_interaction.md` (Lane 2 — the contribution NPC) |
| PK mode toggle + no-PK-penalty alarm; absence of numeric karma (§4) | — (this spec owns the law-axis behavioural record) |
| Two-side brood-war faction, side byte via `4/126`, special-maps only; absence of global allegiance (§5) | `world_systems.md` (brood-war shared maps / dark-subsystems); `opcodes.md` / `packets/` (`4/126` identity + field spec) |
| PK-penalty / revenge tracking, distinct from the FATE bond bytes (§6) | `social.md` (the FATE relation-type enum + the actor +96 / +172 bytes) |

---

## 8. Open items

- **OPEN (UNVERIFIED):** the **raw fame currency offset** on the player struct (§2.3) — route via the
  confirm-dialog category builder or a server-driven fame-update handler, or a live read.
- **RUNTIME-ONLY:** the fame cost/charge values (§2.2); the public-peace zone/region semantics and
  exact price/cap (§3.3); the brood-war side-vs-side contract values and the special-map flag
  conditions on the wire (§5.3); the revenge record fields and any PK-penalty status enum (§6).
- **Netcode follow-up:** the confirm-dialog category → opcode mapping for the fame buy/donate sends
  (UNVERIFIED) — for the netcode lanes.
