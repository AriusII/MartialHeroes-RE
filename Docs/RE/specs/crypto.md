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
**dynamic handshake modulus/exponent values** (Section 6, capture-gated: the concrete `n`, `e`, and
the `L1`/`L2` split are read live off the wire, not hardcoded). Do not trust the inbound path until a
capture confirms it. The numeric constants are sufficient for a round-tripping cipher and for the
full handshake reply build; treat the items above as the only blockers to closing this spec
(Section 8).

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
  A single live `0/0` capture already corroborates the inbound `0/0` payload was *not* whitened/
  ciphered (62 bytes, plain after decompress), but a **multi-packet** inbound capture is still needed
  to generalize "no inverse cipher" to all server→client traffic.
- Until confirmed: **`capture_verified: false`** for the inbound path.
- **Server-side note:** a future `MartialHeroes.Server.Console` must run the **inverse cipher** to
  *read* client packets. Therefore **both `Encrypt` and `Decrypt` should exist** in the shared crypto
  surface even though this client only ever exercises `Encrypt`. The open question is only about
  whether the *client's inbound* path invokes `Decrypt`.

---

## 6. Session handshake (opcode major 0 / minor 0 in, major 1 / minor 4 out)

There is a cryptographic handshake at connection start. It is a **distinct subsystem** from the byte
cipher and **does not key it**. It is a **textbook RSA public-key encryption** of the user's login
credential toward a **server-supplied** public key, implemented with the binary's own big-integer
(multi-precision) library — it does **not** use the Windows CryptoAPI, and it is **not** a
Diffie-Hellman-style mutual exchange.

A dedicated "secure session" state object is owned by the network client. The exchange runs over the
game protocol's own packets: the server sends its public key on reserved opcode **major 0 / minor 0**
(KeyExchange); the client answers with an **Auth** packet on opcode **major 1 / minor 4**.

The single most important provenance fact, now pinned: the value the client RSA-encrypts is **the
user's login credential (password) string itself** — staged at login-form time, *not* a random
session nonce and *not* a derived value. The **only** randomness the client injects is the PKCS#1
v1.5 type-2 padding.

### 6.1 Order of operations (behavioral)

1. **Login-form stage (before any `0/0` packet).** When the player submits the login form, the
   client builds a fresh secure-session object and **pre-stages the credential**: it copies the
   **password / credential string** into a fixed-size zero-filled buffer and records that buffer (and
   its declared length) as the object's staged "message". This staged credential is the plaintext
   `M` that will later be RSA-encrypted. (The login form also carries the account name, a login
   sub-opcode byte, and a host:port — none of which are part of the crypto and none of which are
   recorded here.)
2. **Server → client, opcode `0/0` (KeyExchange).** The server sends the **62-byte** key-exchange
   payload (6.2). The client imports the 54-byte key blob into two bignum slots — **modulus `n`** then
   **exponent `e`** — and stores the two trailing 4-byte scalars; it also stamps its own local
   timestamp into an adjacent slot. The concrete values of `n`, `e`, and the `L1`/`L2` split are
   **read live from this packet** — the client hardcodes none of them.
3. **Client → server, opcode `1/4` (Auth).** In the same dispatch branch, the client builds the
   reply (6.3): PKCS#1 v1.5 type-2 pad the staged credential, compute `c = M_padded^e mod n`,
   serialize `c` as `[u32 LE length][big-endian digit bytes]`, apply the per-dword XOR `0x29`
   whitening (6.4), then ship the result through the normal send pipeline (so the reply is itself
   byte-ciphered + LZ4-compressed like any other outbound packet). After sending, the staged
   credential buffer is zeroed and freed.
4. The client marks the session "secure established" and proceeds with normal traffic.

### 6.2 The 62-byte `0/0` payload (server → client)

Total fixed payload = **62 bytes** (this size is corroborated by a single live `0/0` capture). It is
read in this order, *after* the inbound LZ4-decompress and with **no** inverse byte cipher (Section
5):

| Offset | Width | Field | Meaning |
|---|---|---|---|
| 0 | 54 | Key blob | Two-bignum container: modulus then exponent (see 6.2.1). |
| 54 | 4 | Server scalar #1 | u32. A server-supplied token / nonce / session-class value. Stored, not interpreted by the client crypto. |
| 58 | 4 | Server scalar #2 | u32. A second server scalar; on a successful read the client also stamps its own local timestamp into an adjacent slot. Stored, not interpreted. |

#### 6.2.1 The 54-byte key blob — internal layout (pinned)

The blob is consumed strictly as follows:

| Sub-offset | Width | Field | Meaning |
|---|---|---|---|
| 0 | 2 | header A | Per-value serialization tag for value #1 (the modulus). Opaque — see 6.2.2. |
| 2 | 2 | header B | Per-value serialization tag for value #2 (the exponent). Opaque — see 6.2.2. |
| 4 | 4 | `L1` (u32, little-endian) | Byte length of the modulus digit array. |
| 8 | `L1` | modulus digits | Big-endian digit bytes of `n` (see 6.2.3). |
| 8 + `L1` | 4 | `L2` (u32, little-endian) | Byte length of the exponent digit array. |
| 12 + `L1` | `L2` | exponent digits | Big-endian digit bytes of `e` (see 6.2.3). |

**Hard constraint:** the parser requires the blob to consume exactly 54 bytes, i.e.
`2 + 2 + 4 + L1 + 4 + L2 == 54`, which fixes **`L1 + L2 == 42`**. The 42-byte sum is a fixed constant
of this client; the **individual `L1`/`L2` split is server wire data**, read live and used as-is to
size each bignum. The client contains **no hardcoded modulus length, no clamp, and no default** — the
only enforced invariant is the sum `L1 + L2 == 42`. A ~40-byte (~320-bit) modulus with a small
(~1–4-byte) exponent fits the envelope, but the exact split needs a live `0/0` capture to read
(Section 8.2).

#### 6.2.2 The two 2-byte per-value headers (header A / header B) — opaque, ignorable

Header A and header B are **per-value serialization tags** emitted by the bignum library's own
serialized-integer format (a sign / word-count / type descriptor that prefixes each serialized big
integer). The client **stores them but never reads them back**: they do **not** participate in the
bignum reconstruction, the modular exponentiation, or the reply serialization. The RSA computation is
driven **entirely** by the `[u32 len][digits]` bodies.

**Implication for the implementer:** for **decoding** the server's key and for **building** the
client's reply, header A and header B can be treated as an **ignorable opaque 2-byte prefix** per
value. (They would matter only for a byte-exact *re-encode* of the blob, which this client never
performs; that would additionally require the library's tag convention, which is not specified here.)

#### 6.2.3 Bignum byte order — pinned

- **Digit arrays** (both `n` and `e`, inbound; and the ciphertext `c`, outbound) are **big-endian**
  (most-significant byte first). The importer reconstructs each value as
  `accumulator = accumulator * 256 + next_byte` starting from the first byte, so the first byte read
  is the most significant. The outbound serializer emits the value and reverses it in place, yielding
  big-endian as well. Inbound and outbound therefore agree on big-endian digit order.
- **Length prefixes** (`L1`, `L2`, and the reply's `[u32 length]`) are **little-endian** u32.

### 6.3 The `1/4` Auth reply build (client → server) — fully pinned

Given the parsed `0/0` blob (modulus `n`, exponent `e`, both big-endian, delimited by their
little-endian length prefixes; headers ignored) and the staged credential plaintext `M`, the client
produces the reply payload in this exact order:

**Step 1 — PKCS#1 v1.5 padding, block type 2.**
Build a padded block of size **`k − 1` bytes**, where **`k = modulus_bytes`** is the imported
modulus's own byte width (i.e. `L1`). The block has the canonical type-2 shape, **minus its leading
`0x00` octet** (the block is built one byte shorter than the modulus and then interpreted numerically
as an integer below `n`):

```
padded block (length k − 1) :=  0x02 ‖ PS ‖ 0x00 ‖ M
```

| Element | Rule |
|---|---|
| `0x02` | Block-type-2 marker, first byte. |
| `PS` | Padding string: **random, guaranteed-nonzero** bytes from the library PRNG, length chosen so the whole block is exactly `k − 1` bytes: `len(PS) = (k − 1) − 1 − 1 − len(M) = k − 3 − len(M)`. |
| `0x00` | Single zero separator between `PS` and the message. |
| `M` | The staged credential (password) plaintext. |

**PS-length rule (enforced):** `len(PS) ≥ 8`. If fewer than 8 padding bytes would remain (i.e. the
message is too long for the modulus), the build **must fail** rather than emit a short pad — the
client bails in that case. (A block-type-1 variant — `0x01` then a run of `0xFF` — exists in the same
routine but is **not** used by this reply.)

**Step 2 — Modular exponentiation (RSA public-key encryption).**
Interpret the `k − 1`-byte padded block as a big-endian big integer `m` and compute:

```
c = m ^ e  mod  n
```

using the **server-supplied** exponent `e` and modulus `n` from the `0/0` blob. No client-side key is
involved; the base is the padded credential and the modular parameters are entirely server-provided.

**Step 3 — Serialize into the reply payload.**
Emit the ciphertext as:

```
[u32 length, little-endian]  ‖  c as big-endian digit bytes
```

where `length` is the byte count of the big-endian digit array of `c`.

**Step 4 — Per-dword XOR whitening (6.4).**
Apply the `0x29` per-dword XOR over the dword-aligned reply payload built in Step 3.

**Step 5 — Normal send pipeline.**
Hand the whitened payload to the standard outbound path: byte cipher (Section 3.1) then LZ4
(Section 3.2), with the 8-byte plaintext header carrying opcode major 1 / minor 4. Enqueue and send.

In one line, the reply body before the normal send pipeline is:

```
reply = whiten_dwords( [u32_LE len(c)] ‖ BE_digits(c) ),   where c = PKCS1v15_type2(M, k−1) ^ e mod n
```

This is **implementable end-to-end from this prose alone**, given a runtime-parsed `(n, e)`. The only
runtime inputs are server-supplied and arrive on the `0/0` wire — an implementation reads them at
handshake time rather than hardcoding them.

### 6.4 Per-dword XOR whitening of the reply payload

A small helper whitens the reply (Step 4 above) just before it enters the normal send pipeline:

| Constant | Value | Role |
|---|---|---|
| XOR key | **`0x29`** (41) | The 32-bit XOR key applied to each dword: `dword ^= 0x00000029` (little-endian byte pattern `29 00 00 00` repeated). |
| Selector | **`0x40`** (64) | Input to the complement test below. (It is the **selector**, not a length.) |
| Complement test | `(selector & key & 0x1F) == 1` | Selects whether the key is replaced by its one's-complement. With the recovered values: `0x40 & 0x29 & 0x1F = 0`, which is **not 1**, so the **key is used as-is (`0x29`)**; the complement branch is **not taken** for this client. |
| Whitened span | The **entire dword-aligned payload**: `floor(payload_size / 4)` dwords, i.e. `payload_size & ~3` bytes. **No fixed length cap** — any trailing 1–3 bytes are left untouched. |

Net effect for this client: **XOR every 32-bit word of the reply payload with `0x00000029`** over
`size >> 2` dwords. To decode, re-apply the identical XOR (XOR is its own inverse). The complement
test is documented so a server implementation handles other `(selector, key)` pairs correctly, but
no other pair occurs in this build.

### 6.5 Provenance and placeholders

The public-key material (modulus `n` + exponent `e`) is **server-supplied at handshake time**, not
embedded in the client and not constrained by it. The plaintext is the **staged login credential**;
the reply is its RSA encryption toward the server's public key — classic public-key shape.

**Do not treat developer placeholders as keys.** The secure object's constructor contains obvious
**placeholder/test seed strings** for its bignum slots. These are overwritten by the real
server-supplied values at handshake time and are **not** live key material; their bytes are
deliberately not recorded.

**Scope boundary:** this handshake secures **session establishment / auth** and conveys the
credential to the server. It is **independent of the per-packet byte cipher**, which stays keyless
regardless. No linkage from the credential or the handshake into the byte cipher exists in this
client.

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

**Handshake structure & reply build (now fully pinned — no capture needed for the algorithm):**

| Item | Value |
|---|---|
| Flow | Server `0/0` (62-byte fixed S2C key payload) → client `1/4` Auth reply. |
| RSA plaintext | The **staged login credential (password) string** — not a nonce, not derived. Only randomness is the PKCS#1 type-2 padding. |
| `0/0` payload | 54-byte key blob + 4-byte scalar #1 + 4-byte scalar #2 = **62 bytes**. |
| Key blob layout | `header A(2) ‖ header B(2) ‖ [u32 LE L1] ‖ modulus[L1] ‖ [u32 LE L2] ‖ exponent[L2]`; **`L1 + L2 = 42`**. |
| Two 2-byte headers | Opaque per-value serialization tags; **stored but never read** → ignorable for decode and reply. |
| Digit byte order | Bignum digit arrays (`n`, `e`, ciphertext `c`) **big-endian**; length prefixes **little-endian** u32. |
| Reply padding | PKCS#1 v1.5 **block type 2** (random nonzero PS, **PS ≥ 8**); padded block = **`modulus_bytes − 1`**. |
| Reply exponentiation | `c = m^e mod n` with server-sent `e`, `n`. |
| Reply serialization | `[u32 LE length] ‖ big-endian digits of c`, then per-dword XOR `0x29`, then normal cipher + LZ4 send pipeline. |

### 8.2 Still unresolved (capture-dependent — do not block the cipher or the reply build)

The handshake **structure, serialization, PKCS#1 layout, the role of the two 2-byte headers, and the
full reply build are now PINNED** and implementable without a capture. The reply algorithm reads its
modular parameters live from the `0/0` wire, so only the **concrete server values** and a couple of
**behavioral semantics** remain capture-only:

| Item | Why still open | Gate |
|---|---|---|
| Concrete `n` and `e` values | Carried in the `0/0` blob; server data, never hardcoded by the client. Needed only for a static fixture/test, not for the algorithm. | One live `0/0` capture. |
| Individual `L1`/`L2` split | Only the **sum** `L1 + L2 = 42` is a client constant; the modulus/exponent byte split is server wire data. | Same live `0/0` capture (the split is implied by the captured `n`/`e` lengths). |
| Meaning of the two server scalars (#1 / #2) | Read and stored by the client (token / nonce / session-id / timestamp class) but their server-side use cannot be inferred from the client. | Behavioral / capture. |
| Inbound "no inverse cipher" generalization | Structurally absent on the client receive path, and corroborated for `0/0` by a single capture; not yet confirmed across **multiple** inbound packet types. | A **multi-packet** inbound capture oracle (Section 5). |

These open items do **not** block `Network.Crypto` from implementing `Encrypt` / `Decrypt`, the LZ4
codec, the reply whitening, **or the full handshake reply build** — the build reads `n`, `e`, and the
`L1`/`L2` split from the live `0/0` packet at runtime. The whole spec stays **`capture_verified:
false`** until a live capture supplies concrete `(n, e)` and closes the inbound-direction question.

---

## 9. Status

- Byte cipher located, isolated, structurally described, **and all constants recovered**:
  **HIGH confidence** (static).
- LZ4 variant (raw block), acceleration, and inbound capacity recovered: **HIGH confidence** (static).
- Handshake flow, `0/0` + `1/4` field layout, PKCS#1 v1.5 type-2 reply build, big-endian digit order,
  reply whitening, and credential-as-plaintext provenance recovered: **HIGH confidence** (static).
  The two 2-byte headers are characterized as opaque/ignorable; concrete `n`/`e` and the `L1`/`L2`
  split remain capture-only (Section 8.2).
- Cipher keyless / stateless; handshake separate; CryptoAPI = anti-cheat/config: **HIGH confidence**
  (static).
- Direction asymmetry (inbound compressed-only): **observed statically, single `0/0` capture
  corroboration, multi-packet capture-unverified.**
- End-to-end confirmation against captures: **not yet performed.** `capture_verified: false`.
