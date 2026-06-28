---
verification: confirmed
ida_reverified: 2026-06-24
ida_reverified: 2026-06-27   # CYCLE 14 re-anchor (f61f66a9): confirmatory - subsystem cleanly relocated, 1 re-confirmed SAME, 0 corrected
ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
ida_cycle7: re-verified against doida.exe IDB SHA 263bd994, CYCLE 7 (2026-06-20)
evidence: [static-ida]
conflicts: the embedded packet-buffer internal header/body split is PENDING (only the major/minor header words are proven); the bignum digit encodings are PENDING
---

# SecureContext object layout (clean-room spec)

Neutral, rewritten offset model of the legacy client's **handshake / crypto context** — the
separately-allocated object that holds the inbound key-exchange blob, the imported public-key
material (two arbitrary-precision integers), the server-supplied scalars, and the staged encrypted
credential. Promoted from dirty-room notes; **rewritten** — no decompiler identifiers, no binary
addresses. This file is the offset-table backing for the IDB struct typing of the crypto context
and the design input for `Network.Crypto` / the login handshake.

The object is reached only via `NetClient.secure_context_ptr` (`structs/net_client.md`, +0x141F8);
it is **not** embedded in `NetClient`. The whole object lives on a **guarded memory page** that is
toggled read-write ↔ no-access around each access (an anti-tamper measure) — a re-implementation
does not reproduce the page guard, only the field layout.

Refs: `specs/crypto.md` §6.2.2 (header B as PKCS#1 block size `k`), §6.2.3 (modexp argument order),
§6.3 (reply build), §6a (lifecycle).

All offsets are expressed **relative to the start of the object** (the `SecureContext` instance,
addressed as `this`). They are never binary addresses. The decimal column is provided because the
deep crypto-region offsets exceed 0x2D00.

Cross-references: `specs/login_flow.md` and `specs/crypto.md` (the handshake/key-exchange and cipher
behaviour); `structs/net_client.md` (the owning connection object).

## Status header

| Aspect | State |
|---|---|
| Overall object size | **CONFIRMED** — 0x2E20 bytes (11 808 decimal); the page-guard call protects exactly this span. |
| Embedded packet buffer | **CONFIRMED (presence) / split PENDING** — the object embeds a packet buffer at +0x00; the major/minor header words at +0x04/+0x06 are proven, but the buffer's internal header/body split is PENDING. The body extends up to the crypto region at +0x2DA8. |
| Key object | **CONFIRMED** — at +0x2DA8: two inline arbitrary-precision-integer (bignum) structures (each a digit-buffer pointer + word-count pair, **not** a raw byte buffer) plus two 2-byte headers. |
| Key-exchange blob | **CONFIRMED** — a 0x36-byte (54-byte) inline fixed buffer at +0x2DBC holding the inbound key-exchange value before import. |
| Server scalars / staged credential | **CONFIRMED** — two 4-byte server scalars at +0x2E08/+0x2E0C, a recv-key timestamp at +0x2E10, and a staged-password pointer/pad-width pair at +0x2E14/+0x2E18 (pad width = 17 = 0x11). |
| Object allocation | **CONFIRMED (CYCLE 7)** — a fresh 0x2E20-byte secure-context page is allocated per login attempt by the key-string→secure-context builder. |
| Credential plaintext layout | **CONFIRMED (CYCLE 7)** — the 1/4 login-packet builder writes the plaintext region as `SubOpcode 0x2B` + `[u32 LE len] ACCOUNT(+NUL)` + optional `[u32 LE len] PIN(+NUL)`; PASSWORD is staged separately at +0x2E14 as the RSA plaintext M (appended later as ciphertext). |
| Modexp argument order | **CONFIRMED** — `Bignum_ModExp` is `base^exp mod modulus`; `Rsa_PadAndModExp` passes exponent = key_object+0x00 (second-read), modulus = key_object+0x08 (first-read), base = padded message. Confirmed by control-flow + operand mapping in the current IDB. |
| Bignum digit encodings | **PENDING** — the on-the-wire digit encoding inside each bignum and inside the key-exchange blob is not byte-decoded here. |

Confidence per field is given inline in each table (`CONFIRMED`, `UNVERIFIED`). Field VALUE
semantics and bignum digit encodings remain PENDING.

---

## Object model overview

`SecureContext` is a single separately-allocated object of **0x2E20 bytes (11 808)** with the
following top-level regions:

| Range | Size | Region |
|---|---|---|
| +0x0000 .. ~+0x2DA7 | ~0x2DA8 | **Embedded packet buffer** (the object is/embeds a packet buffer). The major/minor header words sit at +0x04/+0x06; the body buffer extends to the crypto region. Internal split PENDING. |
| +0x2DA8 .. +0x2DBB | ~0x14 | **Key object** — two inline bignum structures + two 2-byte headers. |
| +0x2DBC .. +0x2DF1 | 0x36 | **Key-exchange blob** — 54-byte inline fixed buffer (inbound key-exchange value). |
| +0x2E08 .. +0x2E1B | ~0x14 | Server scalars, recv-key timestamp, staged-message pointer/size. |

The object lives on a guarded page (read-write ↔ no-access toggled around each access). A managed
re-implementation models the bignums with a big-integer type and ignores the page guard.

---

## Top-level SecureContext field table

Offsets relative to the start of the object.

| Offset | dec | Size | Type | Field | Confidence | Notes |
|--------|-----|------|------|-------|------------|-------|
| +0x0000 | 0 | ~0x2DA8 (region) | obj | `packet_buffer` | CONFIRMED (presence) / split PENDING | The object embeds a packet buffer at +0x00 (the packet-buffer append/read primitives take this object directly). The header words below sit inside it; the body buffer extends up to the crypto region at +0x2DA8. Internal header/body split PENDING. |
| +0x0004 | 4 | 2 | uint16 | `hdr_major` | CONFIRMED | Packet-buffer major field; stamped by the secure-auth-reply builder. |
| +0x0006 | 6 | 2 | uint16 | `hdr_minor` | CONFIRMED | Packet-buffer minor field; stamped by the secure-auth-reply builder. |
| +0x2DA8 | 11 688 | ~0x14 | obj | `key_object` | CONFIRMED | The imported public-key material; see the key-object sub-table below. Also passed as the key argument to the modular-exponentiation routine. |
| +0x2DBC | 11 708 | 0x36 (54) | bytes[54] | `key_exchange_blob` | CONFIRMED | Inline fixed buffer; the 54-byte inbound key-exchange value is read here, then imported into the key object. (Blob layout for reference only — see note.) |
| +0x2E08 | 11 784 | 4 | uint32 | `server_scalar_1` | CONFIRMED | First server-supplied scalar, read off the recv-key packet. Value meaning PENDING. |
| +0x2E0C | 11 788 | 4 | uint32 | `server_scalar_2` | CONFIRMED | Second server-supplied scalar. Value meaning PENDING. |
| +0x2E10 | 11 792 | 4 | uint32 | `recvkey_timestamp` | CONFIRMED | Millisecond timestamp captured when the recv-key value is parsed. |
| +0x2E14 | 11 796 | 4 | ptr | `staged_password_ptr` (`staged_M_ptr`) | CONFIRMED | Pointer to a separately-allocated, zero-padded staged-password buffer that holds the **RSA plaintext M**. The login-packet builder allocates it at the pad width below, zero-fills it, then `memcpy`s the PASSWORD into it **without a trailing NUL**. Read/zeroed/freed by the credential-reply encryptor. The PASSWORD is **NOT** placed in the plaintext region — it lives here and is appended later as the RSA ciphertext block. |
| +0x2E18 | 11 800 | 4 | uint32 | `pad_width_latch` (`staged_M_size`) | CONFIRMED | Pad / capacity width of the staged-password buffer, latched to **17 (0x11)**; also the byte length passed as the length argument to the modular-exponentiation routine. Adjacent to `staged_password_ptr`. |

---

## Key object (embedded at +0x2DA8)

Offsets below are **relative to the key-object base** (= SecureContext +0x2DA8). Each bignum slot is
a fixed inline arbitrary-precision-integer **structure** of ~8 bytes — a {digit-buffer pointer +
word-count} pair; the public-key material is a pointer+length bignum, **not** a raw fixed-byte
buffer. The two 2-byte headers are per-value header words from the blob; header B (`+0x12`) is
read by the padder as the PKCS#1 block size `k` (see `specs/crypto.md` §6.2.2). The padder reads
`key_header_2` directly — it does **not** derive `k` from the modulus bignum's word-count.

| Rel offset | Abs offset | Size | Type | Field | Confidence | Notes |
|------------|-----------|------|------|-------|------------|-------|
| +0x00 | +0x2DA8 | ~8 | bignum struct | `exponent_bignum` | CONFIRMED | The importer assigns the **second-read** bignum here. `Rsa_PadAndModExp` passes this slot as the exponent argument to `Bignum_ModExp`. Role confirmed by control-flow operand mapping (see `specs/crypto.md` §6.2.3). |
| +0x08 | +0x2DB0 | ~8 | bignum struct | `modulus_bignum` | CONFIRMED | The importer assigns the **first-read** bignum here. `Rsa_PadAndModExp` passes this slot as the modulus argument to `Bignum_ModExp`. Supplies the modular-reduction operand only — **does not** size the padded block (see key_header_2 below). |
| +0x10 | +0x2DB8 | 2 | uint16 | `key_header_1` | CONFIRMED | Opaque 2-byte per-value header, copied from the blob (bytes 0–1). Value meaning PENDING. |
| +0x12 | +0x2DBA | 2 | uint16 | `key_header_2` | CONFIRMED | 2-byte per-value header, copied from the blob (bytes 2–3). **Read as a little-endian u16 by `Rsa_PadAndModExp` to derive the PKCS#1 block size `k`** (block is built to `key_header_2 − 1` bytes). This is header B of the blob as described in `specs/crypto.md` §6.2.2. The padder reads this field directly, **not** the modulus bignum's word-count. Value meaning PENDING beyond its role as `k`. |

> **Key-exchange blob layout (for reference — NOT a SecureContext field).** The importer consumes
> the 54-byte `key_exchange_blob` as: 2-byte header 1 + 2-byte header 2 + 4-byte little-endian
> modulus length + modulus digits (big-endian) + 4-byte little-endian exponent length + exponent
> digits; the importer asserts it consumed exactly 0x36 (54) bytes. The exact digit encoding of the
> bignums stays **PENDING**.

---

## Credential staging — RSA plaintext M and the 1/4 plaintext region (CYCLE 7)

The login-packet (1/4) builder fills the secure-context page from a single tab-delimited login-key
string assembled by the login window. Field order in that key string is
**ACCOUNT \t PASSWORD \t PIN \t HOST:PORT** (HOST:PORT is a side store, not part of the payload).

**Password is staged separately from the plaintext region.** The builder allocates a small buffer at
the pad width = **17 (0x11)**, zero-fills it, and `memcpy`s the PASSWORD into it **without a trailing
NUL**. That zero-padded buffer is the RSA plaintext M; its pointer is `staged_password_ptr` (+0x2E14)
and its width is `pad_width_latch` (+0x2E18). The PASSWORD never appears in the plaintext region — it
is appended to the packet later as the RSA ciphertext block (`[u32 LE ciphertext-length][big-endian
digit bytes]`) by the crypto stage (see `specs/crypto.md`).

**Plaintext region layout (written by the 1/4 builder into the embedded packet buffer).** All length
prefixes are u32 little-endian and **include** the trailing NUL byte they prefix:

| Order | Width | Type | Field | Notes |
|-------|-------|------|-------|-------|
| 1 | 1 | uint8 | `SubOpcode` | Value **0x2B (43)** at payload offset 0. |
| 2 | 4 | u32 LE | `AccountLength` | Length prefix; counts ACCOUNT bytes + the trailing NUL. |
| 3 | N | bytes | `Account` (+NUL, CP949) | From key-string field 1. |
| 4 | 4 | u32 LE | `PinLength` *(optional)* | Present only when a PIN is configured (PIN cap nonzero). |
| 5 | N | bytes | `PIN` (+NUL) *(optional)* | From key-string field 3. |

> **PASSWORD is NOT in the plaintext region** — it is the RSA plaintext M staged at +0x2E14 and
> appended later as the ciphertext block.

**Validation caps (login-key parse + 1/4 builder gate).** Account width < **20 (0x14)**, password
width < **17 (0x11)**, PIN width < **5**; both account and password additionally require length
**>= 2**. These caps are hard-coded immediates on the join path (the password cap 17 is the same
value latched into `pad_width_latch` at +0x2E18 — it is a static literal, not merely a runtime
observation). The UI password textbox `maxlen` (129) is unrelated to the wire cap (17) — do not
mis-size the field from the UI control width.

---

## Notes for the crypto / login engineer

- **The object is page-guarded.** The whole 0x2E20-byte object lives on a memory page toggled
  read-write ↔ no-access around each access. A managed re-implementation does not reproduce this;
  it is an anti-tamper detail, not a protocol field.
- **The key material is a pointer+length bignum pair, not raw bytes.** Model `exponent_bignum` and
  `modulus_bignum` with an arbitrary-precision-integer type; do not assume a fixed byte buffer. The
  digit encoding (word size, endianness within the digit array) is PENDING.
- **Modexp argument order is CONFIRMED.** `Bignum_ModExp` is `base^exp mod modulus`. `Rsa_PadAndModExp`
  passes: base = padded message, exponent = bignum at key_object+0x00 (second-read), modulus = bignum
  at key_object+0x08 (first-read). Confirmed by control-flow + FLINT-primitive operand mapping (see
  `specs/crypto.md` §6.2.3).
- **The staged credential is a separate allocation** referenced by
  `staged_password_ptr`/`pad_width_latch` (+0x2E14/+0x2E18); it is the zero-padded RSA plaintext M
  (pad width 17, password `memcpy`d with no trailing NUL), modular-exponentiated, then appended as
  the ciphertext block by the credential-reply encryptor. The PASSWORD is **not** in the 1/4
  plaintext region — see the credential-staging section above.
- **The embedded packet buffer's internal split is PENDING** — only the major/minor header words at
  +0x04/+0x06 are proven; the rest of the buffer's header/body structure is not decoded here.

---

## Open questions (PENDING)

1. **Embedded packet-buffer internal split (PENDING).** The buffer's exact header/body field shape
   (beyond the major/minor header words at +0x04/+0x06) is not decoded.
2. **Bignum digit encodings (PENDING).** The on-the-wire and in-memory digit encoding inside each
   bignum, and inside the 54-byte key-exchange blob, is not byte-decoded here.

Previously listed: modexp argument order — **RESOLVED CONFIRMED** (see key-object sub-table and
`specs/crypto.md` §6.2.3).
