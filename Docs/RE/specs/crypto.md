# Network Crypto Specification — Wire Cipher, Compression, and Session Handshake

> Clean-room neutral spec. Promoted from dirty-room analyst notes by the protocol spec-author.
> No code, no decompiler identifiers, no addresses. Math and tables only.
> Implementation target: `MartialHeroes.Network.Crypto` (and the inverse needed by a future server).

---

## ⚠ CAPTURE-UNVERIFIED BANNER

**`capture_verified: false`.** The cipher constants, the LZ4 variant, and the handshake reply
whitening below are now **pinned** from static analysis, but no claim here has yet been confirmed
end-to-end against a live Wireshark capture. The remaining load-bearing open items are: (a) the
**inbound direction asymmetry** (Section 5) — this client applies the byte cipher only on its send
path, which implies server→client payloads may be **compressed-only, not enciphered** — and (b) the
**dynamic handshake modulus/exponent split** (Section 6, capture-gated). Do not trust the inbound
path until a capture confirms it. The numeric constants are sufficient for a round-tripping cipher;
treat the two open items above as the only blockers to closing this spec (Section 8).

---

## 1. Executive summary

The game protocol is **not plaintext on the wire**, but no transform happens at the socket or
framing layer. Each outbound message payload is run through a **custom, keyless, stateless byte
cipher** and then **LZ4-compressed** before it reaches the send queue. The 8-byte frame header is
**always plaintext**, and header-only packets (heartbeats) bypass all transforms. A separate
**big-integer public-key handshake** (reserved opcode major 0 / minor 0) establishes session/auth
identity but does **not** key the byte cipher. A third, unrelated cryptographic cluster (Windows
CryptoAPI) belongs to anti-cheat and a signed local-config loader and is **out of scope** for
`Network.Crypto`. All byte-cipher constants, the LZ4 variant, and the handshake reply whitening are
now recovered and tabulated (Section 8.1).

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
- The (compressed) payload length is carried by the **header's size field**; the LZ4 layer itself
  carries no length.

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

- a **position counter that is a remaining-length countdown** (see the load-bearing note below), and
- a **one-byte feedback accumulator** that is **reset to zero at the start of each sweep** and
  chained from byte to byte within that sweep.

The transform is a pure, deterministic function of `(payload bytes, payload length)` alone. No
external key, no per-connection seed, no rolling-key global.

**Structure:**

- The payload is processed in **`R = 3` identical rounds**.
- **Each round is two sweeps over the same buffer**, in this order:
  1. a **forward sweep** (lowest byte index → highest), then
  2. a **backward sweep** (highest byte index → lowest).
- Each sweep starts its feedback accumulator at **0** and updates it at every byte.
- The forward and backward sweeps use **different rotation amounts** and **different whitening
  steps** — the two directions are **not** symmetric. The forward whitening is an
  additive/complement step; the backward whitening is an XOR step.

#### ⚠ Load-bearing detail — the position counter `p(i)` is a remaining-length countdown

`p(i)` is **NOT the forward byte index.** It is a running **remaining-length** counter:

- Initialized to the **payload length** at the **start of each sweep**.
- **Decremented by 1 after every byte processed.**
- Only its **low 8 bits** participate (it is added with 8-bit wrap into the byte being processed).

So for a payload of length `L`, the first byte a sweep touches mixes in `L`, the next mixes in
`L − 1`, and so on down toward `1`. This is true for **both** the forward and the backward sweep
(each restarts its own countdown at `L`). Implementing `p(i)` as a forward index will **not**
round-trip — this is the single easiest place to get the cipher wrong.

#### Forward sweep — per-byte operation

For each payload position `i` in forward (low→high) order, with feedback accumulator `acc`
(initialised to `0` at sweep start) and remaining-length counter `p`:

```
acc := 0                                  # at sweep start
p   := L                                  # remaining-length countdown
for each position i, low → high:
    t       := ROL8(payload[i], 3)        # rotate-left by 3
    t       := ADD8(t, p)                 # add the 8-bit remaining-length counter
    t       := t XOR acc                  # fold in the running feedback accumulator
    acc     := t                          # the post-XOR value becomes the new accumulator
    out     := 0x48 + NOT8(ROR8(t, 1))    # rotate-right 1, one's-complement, add 0x48
    payload[i] := out                     # store in place
    p       := SUB8(p, 1)                 # decrement the countdown
```

> Whitening algebra: `0x48 + NOT8(x)` is identically `0x47 − x` (mod 256), i.e. `71 − x`. So the
> forward output byte equals **`71 − ROR8(t, 1)`** (mod 256), where `t` is the post-feedback value.
> Either form (`add 0x48` after one's-complement, or `subtract ROR(t,1) from 71`) is correct and
> equivalent; implement whichever reads cleaner and unit-test the identity.

#### Backward sweep — per-byte operation

For each payload position `i` in backward (high→low) order, with feedback accumulator `acc`
(initialised to `0` at sweep start) and remaining-length counter `p`:

```
acc := 0                                  # at sweep start
p   := L                                  # remaining-length countdown (restarts at L)
for each position i, high → low:
    t       := ROL8(payload[i], 4)        # rotate-left by 4
    t       := ADD8(t, p)                 # add the 8-bit remaining-length counter
    t       := t XOR acc                  # fold in the running feedback accumulator
    acc     := t                          # the post-XOR value becomes the new accumulator
    out     := ROR8(t XOR 0x13, 3)        # XOR 0x13, then rotate-right 3
    payload[i] := out                     # store in place
    p       := SUB8(p, 1)                 # decrement the countdown
```

> Notation: `ROL8`/`ROR8` are 8-bit circular rotates; `ADD8`/`SUB8` are 8-bit add/subtract
> (mod 256); `NOT8` is 8-bit one's-complement; `XOR` is bytewise. The accumulator is the value
> *after* the rotate/add/XOR mixing and *before* the whitening output step — i.e. it chains the
> pre-whitening intermediate, not the stored output byte. (Stated this way the inverse is exact.)

This forward-then-backward pair is **one round**; the whole round repeats `R = 3` times.

### 3.2 LZ4 compression

After the cipher, the (now enciphered) payload is **LZ4-compressed** using the **stock raw-block
LZ4 format** — the canonical single-file `lz4.c` (`LZ4_compress_default` / `LZ4_decompress_safe`).
There is **no LZ4 frame format**: no `0x184D2204` frame magic, no frame descriptor, no block-size
prefix, no checksum.

| LZ4 parameter | Value |
|---|---|
| Format | Raw block (NOT framed). |
| Acceleration (compress) | `1` (default fast mode). |
| Length source | The 8-byte header's size field; the source length handed to the codec is `frame_size − 8`. |
| Inbound max decompressed size | `0x2DA0` = **11680** bytes — the `dstCapacity` to supply on decode; a single inbound payload never exceeds this. |

> Practical note: use a standard LZ4 raw-block codec. On decode, supply the known output capacity
> (11680). Do not look for an LZ4 frame header — there is none.

### 3.3 Inverse (decryption)

Decryption is the structural inverse of Section 3.1. Apply **`R = 3` rounds**; within each round the
**two sweeps run in the opposite order** (invert the backward sweep first, then the forward sweep),
each visiting bytes in the **reverse of its forward sweep order** so the chained accumulator unwinds
correctly. The per-byte inverse undoes the output step, restores the accumulator, removes the
position add, and undoes the first rotation.

**Inverse of the forward sweep** (now applied as the *second* inverse step within a round, visiting
high→low so the accumulator chain matches encryption's low→high feed):

```
acc := 0                                  # at sweep start, matching encrypt
p   := L
# NOTE: reconstruct acc / p in the SAME order encrypt produced them; the algebra below is the
# per-byte inverse once acc and p for position i are known.
t   := NOT8(out - 0x48)                    # undo "0x48 + NOT8(...)"  →  t-after-ROR
t   := ROL8(t, 1)                          # undo ROR8(.,1)          →  post-feedback value (== acc_i)
in  := SUB8( (t XOR acc_prev), p )         # undo XOR feedback, then undo ADD8(.,p)
in  := ROR8(in, 3)                         # undo ROL8(.,3)
payload[i] := in
```

**Inverse of the backward sweep** (applied as the *first* inverse step within a round):

```
t   := ROL8(out, 3)                        # undo ROR8(.,3)
t   := t XOR 0x13                          # undo XOR 0x13           →  post-feedback value (== acc_i)
in  := SUB8( (t XOR acc_prev), p )         # undo XOR feedback, then undo ADD8(.,p)
in  := ROR8(in, 4)                         # undo ROL8(.,4)
payload[i] := in
```

Because the transform is keyless and the feedback is a well-defined chain of the pre-whitening
intermediates, `Decrypt` is **fully determined** by `Encrypt` — no separate key material. The
recommended implementation strategy is to mirror the encrypt loop exactly (same sweep order, same
`p` countdown, same `acc` chaining) and substitute the per-byte inverse above, then verify
`Decrypt(Encrypt(x)) == x` over random payloads of several lengths (including length 1 and a
header-only length-0 payload, which is a pass-through). XOR is its own inverse; `ADD8`/`SUB8` and
`ROL8`/`ROR8` are exact 8-bit inverses, so the round-trip is bit-exact.

---

## 4. Keyless and stateless — explicit

- The byte cipher carries **no key, no per-connection seed, and no rolling-key state.**
- There is **no key-schedule object** and **no seed plumbing** to implement in `Network.Crypto`.
- Every packet is enciphered **identically and independently** of every other packet (the only
  per-packet input is the payload bytes and their length).
- The opcode `(major:minor)` at header offset +4 is **NOT** a cipher seed and **NOT** a key. It
  participates in nothing the cipher reads.
- The session handshake of Section 6 **does not** feed the byte cipher. The cipher remains keyless
  regardless of the handshake outcome.

This makes the transform a pure function of `(payload bytes, payload length)` — the single most
important fact for the engineer.

---

## 5. ⚠ Direction asymmetry — open question #1

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

### 6.1 Order of operations (behavioral)

1. **Server → client, opcode 0/0:** the server sends a key-exchange payload. The client reads, in
   order: a **54-byte key blob** (the server's public modulus + exponent, see 6.2), then **two
   4-byte scalars** — a server-supplied token / nonce / timestamp-class value and a second scalar
   (the client also records its local timestamp into the second slot). Total 0/0 payload =
   **62 bytes** (54 + 4 + 4) before compression/cipher. The blob is imported into the secure
   object's bignum slots.
2. **Client → server, sent as an Auth packet (opcode major 1 / minor 4):** the client builds its
   secure reply payload (see 6.3), performs a **modular exponentiation** of its padded session
   secret under the server-provided modulus and exponent (public-key encryption of the session
   secret toward the server), serializes the resulting big integer into the packet, and applies a
   **light per-dword XOR whitening** (6.4). The reply is then shipped through the normal send
   pipeline — so it is itself byte-ciphered and LZ4-compressed like any other outbound packet.
3. The client marks the session "secure established" and proceeds with normal traffic.

### 6.2 The 54-byte key blob (server → client, in the 0/0 payload)

The blob is a two-bignum container: a modulus followed by an exponent. Layout, in order:

| Sub-field | Width | Meaning |
|---|---|---|
| header A | 2 bytes | Per-bignum metadata for the first value (modulus). Exact bit meaning **not yet decoded** (sign / word-count class — unresolved). |
| header B | 2 bytes | Per-bignum metadata for the second value (exponent). Same caveat. |
| len(value1) = `L1` | 4 bytes, little-endian | Byte length of the first bignum's digit array (the modulus). |
| value1 digits | `L1` bytes | First bignum digit array (the modulus). |
| len(value2) = `L2` | 4 bytes, little-endian | Byte length of the second bignum's digit array (the exponent). |
| value2 digits | `L2` bytes | Second bignum digit array (the exponent). |

**Hard constraint:** the parser asserts the whole blob is exactly 54 bytes, i.e.
`2 + 2 + 4 + L1 + 4 + L2 == 54`, which fixes **`L1 + L2 == 42`**. The 42-byte sum is a fixed
constant of this client; the **individual L1/L2 split is server data, carried on the wire, and is
NOT a recoverable client constant** (Section 8.2). A ~40-byte (~320-bit) modulus with a small
(~2-byte) exponent fits the envelope, but the exact split needs a live 0/0 capture to read.

### 6.3 The 1/4 Auth reply body (client → server)

| Item | Value / shape |
|---|---|
| Padding scheme | **PKCS#1 v1.5, block type 2** (random nonzero padding): `[0x02][random nonzero bytes…][0x00][message]`, applied to the session secret before modular exponentiation. (A block-type-1 branch exists in the binary but the reply uses type 2.) |
| Padded block size | **`modulus_bytes − 1`** (one byte shorter than the modulus — standard for this RSA padding). |
| Reply serialization | `[u32 length][bignum digit bytes]`; the length is **little-endian**. The bignum is the modular-exponentiation (ciphertext) result. |
| Reply whitening | Per-dword XOR (6.4) over the dword-aligned payload, then the normal send pipeline (byte cipher + LZ4). |

### 6.4 Per-dword XOR whitening of the reply payload

A small helper whitens the reply just before it is shipped:

| Constant | Value | Role |
|---|---|---|
| XOR key | **`0x29`** (41) | The 32-bit XOR key applied to each dword: `dword ^= 0x00000029` (little-endian byte pattern `29 00 00 00` repeated). |
| Selector | **`0x40`** (64) | Input to the complement test below. (This corrects an earlier note that called `0x40` a length — it is the **selector**, not a length.) |
| Complement test | `(selector & key & 0x1F) == 1` | Selects whether the key is replaced by its one's-complement. With the recovered values: `0x40 & 0x29 & 0x1F = 0`, which is **not 1**, so the **key is used as-is (`0x29`)**; the complement branch is **not taken** for this client. |
| Whitened span | The **entire dword-aligned payload**: `floor(payload_size / 4)` dwords, i.e. `payload_size & ~3` bytes. **No fixed length cap** — any trailing 1–3 bytes are left untouched. |

Net effect for this client: **XOR every 32-bit word of the reply payload with `0x00000029`** over
`size >> 2` dwords. To decode, re-apply the identical XOR (XOR is its own inverse). The complement
test is documented so a server implementation handles other `(selector, key)` pairs correctly, but
no other pair occurs in this build.

### 6.5 Provenance and placeholders

The public-key material (modulus + exponent) is **server-supplied at handshake time**, not embedded
in the client. The client contributes its own session value; the shared/session secret is
established by the modular-exp step — classic public-key-exchange shape.

**Do not treat developer placeholders as keys.** The secure object's constructor contains obvious
**placeholder/test seed strings** for its bignum slots. These are overwritten by the real
server-supplied values at handshake time and are **not** live key material; their bytes are
deliberately not recorded.

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

## 8. Recovered constants and remaining open items

### 8.1 Pinned constants (sufficient to implement a round-tripping cipher)

**Byte cipher (send-path payload transform):**

| Constant | Value | Role |
|---|---|---|
| `R` (round count) | **3** | Full passes; each pass = one forward sweep then one backward sweep. |
| `r1_fwd` | **ROL 3** | First rotation, forward sweep. |
| `r2_fwd` | **ROR 1** | Second rotation, forward sweep (before whitening output). |
| `W_fwd` | **`0x48` additive after one's-complement** ≡ output `= 71 − ROR8(t,1)` | Forward whitening. |
| `r1_bwd` | **ROL 4** | First rotation, backward sweep. |
| `r2_bwd` | **ROR 3** | Final rotation, backward sweep. |
| `W_bwd` | **XOR `0x13`** | Backward whitening (applied before the final ROR 3). |
| `p(i)` | **Remaining-length countdown**: starts at payload length, decremented per byte, low 8 bits added. NOT the forward index. | Position mixing (both sweeps). |
| feedback | **1-byte accumulator**, reset to 0 per sweep, set to the per-byte post-feedback intermediate. | Chaining within a sweep. |

**LZ4 compression:**

| Constant | Value |
|---|---|
| Variant | Raw block (`LZ4_compress_default` / `LZ4_decompress_safe`); no frame format/magic/checksum. |
| Acceleration | **1**. |
| Inbound max decompressed size | **11680** (`0x2DA0`). |
| Length source | Header size field; codec source length = `frame_size − 8`. |

**Handshake reply (1/4) whitening:**

| Constant | Value |
|---|---|
| XOR key | **`0x29`** (used as-is). |
| Selector | **`0x40`**. |
| Complement test | `(selector & key & 0x1F) == 1` → **false** here → key not complemented. |
| Whitened span | Whole dword-aligned payload (`size >> 2` dwords). |

**Handshake field layout:**

| Constant | Value |
|---|---|
| 0/0 payload | 54-byte key blob + 4-byte scalar + 4-byte scalar = **62 bytes**. |
| Key blob internal | `header A(2) + header B(2) + [u32 len][digits] (modulus) + [u32 len][digits] (exponent)`; lengths little-endian; **`L1 + L2 = 42`**. |
| Reply padding | PKCS#1 v1.5 block type 2 (random); padded block = `modulus_bytes − 1`. |
| Reply body | `[u32 len][bignum digits]`; length little-endian. |

### 8.2 Still unresolved (capture-dependent — do not block the cipher)

| Item | Why open | Gate |
|---|---|---|
| Individual `L1`/`L2` modulus/exponent split | Only the **sum** `L1 + L2 = 42` is a client constant; the split is server wire data. | Needs a live 0/0 capture to read the actual lengths the server sends. |
| Meaning of the two 2-byte per-bignum headers (header A / header B) | Role is "bignum metadata" (sign vs. word-count class); exact bit meaning not decoded. Not needed for the cipher or whitening — only for a byte-exact handshake re-encode. | Deferred unless a wire-exact handshake spec is requested. |
| Inbound decrypt presence (Section 5) | Structurally **absent** on the client receive path, but **capture-unverified**. | A capture oracle task, not a constant. |

These open items do **not** block `Network.Crypto` from implementing `Encrypt` / `Decrypt`, the LZ4
codec, or the reply whitening. The whole spec stays **`capture_verified: false`** until a live
capture closes the inbound-direction question.

---

## 9. Status

- Byte cipher located, isolated, structurally described, **and all constants recovered**:
  **HIGH confidence** (static).
- LZ4 variant (raw block), acceleration, and inbound capacity recovered: **HIGH confidence** (static).
- Handshake reply whitening constants and 0/0 + 1/4 field layout recovered: **HIGH confidence**
  (static); the `L1`/`L2` split and the two 2-byte headers remain partially pinned (Section 8.2).
- Cipher keyless / stateless; handshake separate; CryptoAPI = anti-cheat/config: **HIGH confidence**
  (static).
- Direction asymmetry (inbound compressed-only): **observed statically, capture-unverified.**
- End-to-end confirmation against captures: **not yet performed.** `capture_verified: false`.
