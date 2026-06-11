# Network Crypto Specification — Wire Cipher, Compression, and Session Handshake

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the protocol spec-author.
> No code, no decompiler identifiers, no addresses. Math and tables only.
> Implementation target: `MartialHeroes.Network.Crypto` (and the inverse needed by a future server).

---

## ⚠ CAPTURE-UNVERIFIED BANNER

**`capture_verified: false`.** Every claim below is reconstructed from static analysis of the
legacy client and has **not** yet been confirmed end-to-end against a live Wireshark capture. The
single most consequential unverified item is the **inbound direction asymmetry** (Section 5): this
client applies the byte cipher only on its send path, which implies server→client payloads may be
**compressed-only, not enciphered**. Do not trust the inbound path — and do not finalize whether
`Network.Crypto` implements an inbound decrypt at all — until a capture confirms it. Treat this
whole document as a build target with one load-bearing open question and a set of numeric constants
still to be recovered (Section 8).

---

## 1. Executive summary

The game protocol is **not plaintext on the wire**, but no transform happens at the socket or
framing layer. Each outbound message payload is run through a **custom, keyless, stateless byte
cipher** and then **LZ4-compressed** before it reaches the send queue. The 8-byte frame header is
**always plaintext**, and header-only packets (heartbeats) bypass all transforms. A separate
**big-integer public-key handshake** (reserved opcode major 0 / minor 0) establishes session/auth
identity but does **not** key the byte cipher. A third, unrelated cryptographic cluster (Windows
CryptoAPI) belongs to anti-cheat and a signed local-config loader and is **out of scope** for
`Network.Crypto`. Several numeric constants (rotation amounts, whitening bytes, LZ4 variant) are
characterized by role here and flagged for a focused, cited follow-up recovery.

---

## 2. Where the transform lives

The wire frame begins with an **8-byte header that is never transformed**:

| Header field | Width | Meaning |
|---|---|---|
| Total frame size | 2 bytes (u16, little-endian) | Size of the entire frame including the header. |
| Opcode — major | 2 bytes | High part of the message opcode. |
| Opcode — minor | 2 bytes | Low part of the message opcode. |
| (remainder of header) | 2 bytes | Completes the 8-byte header region. |

Key facts:

- The cipher and the compressor both operate **only on the payload region** — the bytes that follow
  the 8-byte header. The header (size + major:minor opcode) is written and read in the clear.
- Transforms happen at the **message tier**, inside the outbound packet-build pipeline, *before* the
  packet is enqueued. They are **not** applied at the socket/framing layer; bytes are copied raw to
  and from the wire there.
- **Header-only packets** — a frame whose total length is exactly 8 (zero payload, e.g.
  heartbeats / keepalives) — are a **pass-through**: neither cipher nor compression is applied.
- The 4-byte field at header offset +4 is the **major:minor opcode pair**. It is **not** a cipher
  seed, key, or sequence number (see Section 4).

---

## 3. Outbound pipeline (client → server)

Stages, in this exact order:

```
plaintext payload
   → byte cipher (Section 3.1)
   → LZ4 compression (Section 3.2)
   → prepend 8-byte plaintext header
   → enqueue → send on the wire
```

To recover plaintext from a captured outbound payload, reverse the order: **LZ4-decompress first,
then run the inverse cipher.**

### 3.1 The byte cipher — neutral algorithm description

The cipher is a **keyless, self-contained, position-and-feedback-dependent byte transform** of the
payload. It is **not** a keyed stream/block cipher. Its only state is per-invocation:

- a **running position counter** tied to the byte index within the payload, and
- a **one-byte feedback accumulator** that is **reset to zero at the start of each sweep** and
  chained from byte to byte within that sweep.

The transform is a pure, deterministic function of `(payload bytes, payload length)` alone. No
external key, no per-connection seed, no rolling-key global.

**Structure:**

- The payload is processed in **`R` identical rounds** (analysts report **R = 3**; treat the exact
  count as a constant to confirm — Section 8).
- **Each round is two sweeps over the same buffer**, in this order:
  1. a **forward sweep** (lowest byte index → highest), then
  2. a **backward sweep** (highest byte index → lowest).
- Each sweep starts its feedback accumulator at **0** and updates it at every byte.
- The forward and backward sweeps use **different rotation amounts** and **different fixed whitening
  constants** — the two directions are **not** symmetric.

**Per-byte operation (data-flow, expressed as math over byte arrays):**

For a sweep visiting payload byte positions `i` in sweep order, with feedback accumulator `acc`
(initialised to `0`), position counter `p(i)`, sweep-specific rotation amount `r`, and sweep-specific
whitening constant `W`, the output byte is a fixed composition of: a bit-rotation of the input byte,
an XOR that folds in the position counter and the accumulator, and an additive whitening constant
followed by a second bit-rotation. Schematically:

```
acc := 0                                  # at sweep start
for each position i in sweep order:
    t       := ROTATE(payload[i], r1)             # first rotation by sweep-specific amount
    t       := t XOR acc XOR p(i)                 # fold in feedback accumulator and position counter
    t       := ADD8(t, W)                         # additive/whitening constant (8-bit, wraps)
    out[i]  := ROTATE(t, r2)                      # second rotation by sweep-specific amount
    acc     := out[i]                             # chain feedback to next position
    payload[i] := out[i]                          # in-place
```

> Notation note: `ROTATE` is an 8-bit circular rotate; `ADD8` is 8-bit addition (mod 256); `XOR`
> is bytewise. The two rotation amounts (`r1`, `r2`) and the whitening constant `W` differ between
> the forward and backward sweeps. The exact numeric values of `r1`, `r2`, `W` (per direction) and
> the precise way `p(i)` is derived from the running length counter are **not asserted here** —
> they are firewalled constants (Section 8). The shape above is the data-flow to reproduce; the
> engineer must fill the constants from the cited recovery before the cipher will round-trip.

This per-byte step is applied **forward then backward** within a round, and the whole round repeats
`R` times.

### 3.2 LZ4 compression

After the cipher, the (now enciphered) payload is **LZ4-compressed**. The exact LZ4 variant (raw
block vs. framed; which block-format flags) is a constant to confirm — Section 8. On the receive
side, payloads are LZ4-decompressed before dispatch.

### 3.3 Inverse (decryption)

Decryption of an outbound-style payload is the structural inverse of Section 3.1:

- **`R` rounds**, and within each round the **two sweeps are applied in the opposite order**
  (backward-inverse then forward-inverse) with the **inverse per-byte operation**:
  - the second rotation is undone first (rotate by the negation of `r2`),
  - the additive whitening constant is **subtracted** (8-bit),
  - the XOR with `acc XOR p(i)` is re-applied (XOR is its own inverse), using the **same** chained
    accumulator definition,
  - the first rotation is undone (rotate by the negation of `r1`).
- Because the transform is keyless and the feedback is well-defined, the inverse is **fully
  determined by the forward description** — no separate key material is needed. The engineer
  reconstructs `Decrypt` algebraically from `Encrypt`.

---

## 4. Keyless and stateless — explicit

- The byte cipher carries **no key, no per-connection seed, and no rolling-key state.**
- There is **no key-schedule object** and **no seed plumbing** to implement in `Network.Crypto`.
- Every packet is enciphered **identically and independently** of every other packet.
- The opcode `(major:minor)` at header offset +4 is **NOT** a cipher seed and **NOT** a key. It
  participates in nothing the cipher reads.
- The session handshake of Section 6 **does not** feed the byte cipher. The cipher remains keyless
  regardless of the handshake outcome.

This makes the transform a pure function of `(payload bytes, payload length)` — the single most
important fact for the engineer.

---

## 5. ⚠ Direction asymmetry — the #1 open question

Observed in the legacy **client** binary:

| Path | Cipher applied? | Compression applied? |
|---|---|---|
| **Send (client → server)** | **Yes** — enciphered, then compressed. | Yes (after cipher). |
| **Receive (server → client)** | **No inverse cipher call exists on the client receive path.** | Yes — LZ4-decompress only, then dispatch. |

**Implication:** in this build the byte cipher protects **client-originated** payloads only.
Server→client payloads reaching this client appear to be **compressed-only, not enciphered**. If
that holds, the client's inbound path needs LZ4-decompress and **no inverse cipher at all.**

**This decision is unresolved and load-bearing:** it determines whether `Network.Crypto` must
implement an inbound decrypt for the client at all.

- **It MUST be confirmed against a live capture before the inbound path is trusted.** Concretely:
  verify that server→client payloads decode as sensible packets after LZ4-decompress *without* the
  byte cipher, and that client→server payloads only make sense after de-compress *and* de-cipher.
- Until confirmed: **`capture_verified: false`** for the inbound path.
- **Server-side note:** a future `MartialHeroes.Server.Console` must run the **inverse cipher** to
  *read* client packets. Therefore **both `Encrypt` and `Decrypt` should exist** in the shared crypto
  surface even though this client only ever exercises `Encrypt`. The open question is only about
  whether the *client's inbound* path invokes `Decrypt`.

---

## 6. Session handshake (opcode major 0 / minor 0) — separate from the wire cipher

There is a cryptographic handshake at connection start. It is a **distinct subsystem** from the byte
cipher and **does not key it**. It is a **custom big-integer (multi-precision) public-key exchange**
implemented with the binary's own bignum library — it does **not** use the Windows CryptoAPI.

A dedicated "secure session" state object is owned by the network client. The exchange runs over the
game protocol's own packets using reserved opcode **major 0 / minor 0** (KeyExchange).

**Order of operations (behavioral):**

1. **Server → client, opcode 0/0:** the server sends a key-exchange payload. The client reads, in
   order: a **modulus-sized public-key blob** (a multi-byte buffer), then **two small fixed-size
   scalars** — an **exponent** and a **server-supplied token / nonce** (timestamp-class value).
   These are imported into the secure object's bignum slots. The client records a local timestamp.
2. **Client → server, sent as an Auth packet (opcode major 1 / minor 4):** the client builds its
   secure reply payload, performs a **modular exponentiation** (its session material raised under the
   server-provided exponent and modulus — i.e. public-key encryption of the session secret toward the
   server), serializes the resulting big integer into the packet, and additionally applies a **light
   per-dword XOR whitening** over a fixed-size leading region of the packet (each 32-bit word XORed
   with a fixed small key value, with a conditional one's-complement of that key selected by a fixed
   bit test). The reply is then shipped through the normal send pipeline — so it is itself
   byte-ciphered and LZ4-compressed like any other outbound packet.
3. The client marks the session "secure established" and proceeds with normal traffic.

**Key provenance:** the public-key material (modulus + exponent) is **server-supplied at handshake
time**, not embedded in the client. The client contributes its own session value; the shared/session
secret is established by the modular-exp step — classic public-key-exchange shape.

**Do not treat developer placeholders as keys.** The secure object's constructor contains obvious
**placeholder/test seed strings** (`"1234…"` / `"abcd…"`-style) for its bignum slots. These are
overwritten by the real server-supplied values at handshake time and are **not** live key material.

**Scope boundary:** this handshake secures **session establishment / auth** and conveys a session
secret to the server. It is **independent of the per-packet byte cipher**, which stays keyless
regardless. No linkage from the session secret into the byte cipher exists in this client.

---

## 7. CryptoAPI is NOT the network layer (out of scope)

The Windows ADVAPI32 / Microsoft Base Cryptographic Provider cluster is **anti-cheat support plus a
signed/encrypted local config-file loader**. It is **not** the wire cipher and **not** the login key
exchange. It is mentioned here **only** so the engineer does not mistake it for the protocol cipher.
**Do not implement any of this in `Network.Crypto`.**

What it actually is:

- An anti-cheat orchestrator (X-Trap support): reads local files, writes an obfuscated cheat-log
  file using its own trivial position-keyed XOR-over-text scheme (a **log obfuscation**, not a wire
  cipher), spawns the X-Trap helper child process, waits on named events, and performs winsock-hook
  detection.
- A **signed-file verifier**: reads a whole file, inspects a fixed-size **trailer** gated by **two
  adjacent sentinel magic dwords** (one exactly one less than the other) plus an embedded payload
  length and signed-region length, then runs the standard **hash-then-verify-signature** chain
  against an **embedded public key**. Role: authenticated/signed data-file loader.
- A **derive-key + decrypt** pair: decrypts a signed config blob, whose plaintext is parsed as
  **key/value config entries**. Role: configuration protection.
- A separate X-Trap content check: memory-maps a file and compares fixed-offset blobs against locally
  computed values using a hardcoded hex key string. Same category — anti-tamper content signature.

All key/seed material in this cluster is **embedded in the binary** (the opposite of the handshake's
server-exchanged keys), consistent with offline file authentication. Out of interop scope.

---

## 8. Recovery TODO — constants still to be recovered

The following are characterized by **role only** in this spec. Numeric values are **deliberately not
invented** and must arrive through a focused, **cited** clean recovery (analyst → neutral note →
promotion) before the engineer can make the cipher round-trip. Each item names a placeholder.

| Placeholder | Role | Where used |
|---|---|---|
| `R` | Round count for the byte cipher. | Section 3.1 (analysts report 3 — confirm). |
| `r1_fwd`, `r2_fwd` | The two bit-rotation amounts in the **forward** sweep. | Section 3.1 per-byte step. |
| `r1_bwd`, `r2_bwd` | The two bit-rotation amounts in the **backward** sweep. | Section 3.1 per-byte step. |
| `W_fwd` | Additive/whitening constant for the **forward** sweep. | Section 3.1 per-byte step. |
| `W_bwd` | Additive/whitening constant for the **backward** sweep. | Section 3.1 per-byte step. |
| `p(i)` definition | Exact derivation of the position counter mixed into the XOR (how the running length counter maps to a per-byte value). | Section 3.1 per-byte step. |
| `LZ4_variant` | LZ4 format details: raw block vs. framed, and any block-format flags / max block size. | Section 3.2. |
| `XOR_word_key` | The fixed small per-dword XOR whitening key and the bit test that selects its one's-complement. | Section 6, step 2 (handshake reply). |
| `auth_whitened_len` | Size of the leading region of the 1/4 Auth packet that receives the per-dword XOR whitening. | Section 6, step 2. |
| Handshake field layout | Exact byte layout / bignum serialization of the 0/0 payload and the 1/4 reply (modulus length, scalar widths, endianness). | Section 6 (defer to a protocol/capture pass if a wire field spec is needed). |

**Inbound-direction confirmation** (Section 5) is a *capture* task, not a constant recovery, but it
gates the same milestone: until it is confirmed, the inbound path stays `capture_verified: false`.

---

## 9. Status

- Byte cipher located, isolated, and structurally described: **HIGH confidence** (static).
- Cipher keyless / stateless; handshake separate; CryptoAPI = anti-cheat/config: **HIGH confidence**
  (static).
- Direction asymmetry (inbound compressed-only): **observed statically, capture-unverified.**
- Numeric constants (Section 8): **not yet recovered.**
- End-to-end confirmation against captures: **not yet performed.** `capture_verified: false`.
