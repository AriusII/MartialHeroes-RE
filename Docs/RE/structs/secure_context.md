---
verification: confirmed
ida_reverified: 2026-06-19
ida_anchor: 263bd994
evidence: [static-ida]
conflicts: the modulus-vs-exponent argument order into the modular-exponentiation call is mildly ambiguous (resolved here from the assignment evidence — first-read bignum → +0x08 = modulus, second-read → +0x00 = exponent — but the exact modexp argument positions stay PENDING); the embedded packet-buffer internal header/body split is PENDING (only the major/minor header words are proven); the bignum digit encodings are PENDING
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
| Server scalars / staged credential | **CONFIRMED** — two 4-byte server scalars at +0x2E08/+0x2E0C, a recv-key timestamp at +0x2E10, and a staged-message pointer/size pair at +0x2E14/+0x2E18. |
| Modexp argument order | **UNVERIFIED** — modulus-vs-exponent positions mildly ambiguous; resolved here from the assignment evidence but the exact modexp argument roles stay PENDING. |
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
| +0x2E14 | 11 796 | 4 | ptr | `staged_M_ptr` | CONFIRMED | Pointer to a separately-allocated staged (credential) message buffer; read/zeroed/freed by the credential-reply encryptor. |
| +0x2E18 | 11 800 | 4 | uint32 | `staged_M_size` | CONFIRMED | Byte length of the staged message; passed as the length argument to the modular-exponentiation routine. Adjacent to `staged_M_ptr`. |

---

## Key object (embedded at +0x2DA8)

Offsets below are **relative to the key-object base** (= SecureContext +0x2DA8). Each bignum slot is
a fixed inline arbitrary-precision-integer **structure** of ~8 bytes — a {digit-buffer pointer +
word-count} pair; the public-key material is a pointer+length bignum, **not** a raw fixed-byte
buffer. The two 2-byte headers carry opaque per-value header words (one of which is also read as the
modulus word-count by the padder).

| Rel offset | Abs offset | Size | Type | Field | Confidence | Notes |
|------------|-----------|------|------|-------|------------|-------|
| +0x00 | +0x2DA8 | ~8 | bignum struct | `exponent_bignum` | CONFIRMED (store) / role PENDING | The importer assigns the **second-read** bignum to the key-object base (+0x00). In the modular-exponentiation call it is the base operand argument. Whether it is the exponent vs the modulus is the ambiguity noted below (resolved here as exponent). |
| +0x08 | +0x2DB0 | ~8 | bignum struct | `modulus_bignum` | CONFIRMED (store) / role CF via padding | The importer assigns the **first-read** bignum to +0x08. Its word-count sizes the padded block (as modulus-bytes − 1) and it is the modulus passed to the modular-exponentiation routine. The modulus role is control-flow-confirmed via the padded-block sizing. |
| +0x10 | +0x2DB8 | 2 | uint16 | `key_header_1` | CONFIRMED | Opaque 2-byte per-value header, copied from the blob (bytes +0). Value meaning PENDING. |
| +0x12 | +0x2DBA | 2 | uint16 | `key_header_2` | CONFIRMED | Opaque 2-byte per-value header, copied from the blob (bytes +2). Also read as the modulus word-count by the padder. Value meaning PENDING. |

> **Key-exchange blob layout (for reference — NOT a SecureContext field).** The importer consumes
> the 54-byte `key_exchange_blob` as: 2-byte header 1 + 2-byte header 2 + 4-byte little-endian
> modulus length + modulus digits (big-endian) + 4-byte little-endian exponent length + exponent
> digits; the importer asserts it consumed exactly 0x36 (54) bytes. The exact digit encoding of the
> bignums stays **PENDING**.

---

## Notes for the crypto / login engineer

- **The object is page-guarded.** The whole 0x2E20-byte object lives on a memory page toggled
  read-write ↔ no-access around each access. A managed re-implementation does not reproduce this;
  it is an anti-tamper detail, not a protocol field.
- **The key material is a pointer+length bignum pair, not raw bytes.** Model `exponent_bignum` and
  `modulus_bignum` with an arbitrary-precision-integer type; do not assume a fixed byte buffer. The
  digit encoding (word size, endianness within the digit array) is PENDING.
- **Modexp argument order is resolved-but-PENDING.** The assignment evidence places the first-read
  bignum at +0x08 (modulus, confirmed via padded-block sizing) and the second-read at +0x00
  (exponent). The exact argument positions in the modular-exponentiation call are mildly ambiguous
  and stay PENDING.
- **The staged credential is a separate allocation** referenced by `staged_M_ptr`/`staged_M_size`;
  it is padded and modular-exponentiated, then used by the credential-reply encryptor.
- **The embedded packet buffer's internal split is PENDING** — only the major/minor header words at
  +0x04/+0x06 are proven; the rest of the buffer's header/body structure is not decoded here.

---

## Open questions (UNVERIFIED / PENDING)

1. **Modexp argument order (UNVERIFIED).** Which inline bignum is the modulus vs the exponent in the
   modular-exponentiation call order is mildly ambiguous; resolved here from the assignment evidence
   (first-read → modulus @ +0x08, second-read → exponent @ +0x00) but the exact argument positions
   stay PENDING.
2. **Embedded packet-buffer internal split (PENDING).** The buffer's exact header/body field shape
   (beyond the major/minor header words at +0x04/+0x06) is not decoded.
3. **Bignum digit encodings (PENDING).** The on-the-wire and in-memory digit encoding inside each
   bignum, and inside the 54-byte key-exchange blob, is not byte-decoded here.
