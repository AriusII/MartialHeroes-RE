# VFS Archive Container Format

The Virtual File System is split across a two-file index and data pair: `data.inf` (Index / Table of Contents) and `data/data.vfs` (Data blob).

---

## 1. File Layout Overview

The VFS archive uses a flat, uncompressed, unencrypted tiling scheme. The files are organized as follows:

```
[ data.inf ]
  ├── Header (24 bytes)
  └── TOC Array (144 bytes × entry_count)

[ data/data.vfs ]
  ├── Header Echo (24 bytes, identical duplicate of data.inf header)
  └── Payload Tiles (Contiguous data blocks starting at offset 24)
```

Both files are opened by the client using the read-only random-access flags:
`FILE_FLAG_RANDOM_ACCESS | FILE_ATTRIBUTE_READONLY` (`0x10000001`).

---

## 2. Container Header (24 Bytes)

Both `data.inf` and `data.vfs` lead with an identical 24-byte header. The client reads this entire header at mount-time but only extracts the `entry_count` field; all other fields are ignored (read-and-discarded).

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---|:---|:---|:---|:---|
| `+0x00` | 8 | `char[8]` | `magic` | Null-padded ASCII signature: `"VFS001\0\0"`. (Present on disk but **not validated** by the client). |
| `+0x08` | 4 | `u32` LE | `field_08` | Opaque build/version tag. Value is `39` (`0x27`) in the reference archive. Read-and-discarded. |
| `+0x0C` | 4 | `u32` LE | `entry_count` | Number of entries in the TOC. Drives the memory allocation and bulk read size. Reference archive contains `43,347`. |
| `+0x10` | 8 | `u64` LE | `total_blob_size` | Total length of `data/data.vfs` in bytes. Equals `3,802,182,193` in the reference archive. Read-and-discarded. |

### Header Layout Notes:
- **No FILETIME in header:** Historical readings claiming a timestamp was present in the header are incorrect; timestamps live exclusively in the per-entry TOC records.
- **`entry_count` position:** Firmly located at `+0x0C` (4th dword). The file size calculation `24 + 144 * 43,347 = 6,241,992` matching `data.inf` on disk confirms this layout.

---

## 3. Table of Contents Array (TOC)

Immediately following the 24-byte header in `data.inf` is a contiguous array of `entry_count` records. Each record is exactly **144 bytes (0x90)** in size, structured as follows:

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---|:---|:---|:---|:---|
| `+0x00` | 100 | `char[100]` | `name` | Null-terminated ASCII virtual path. Stored in lowercase at compile time to support case-insensitive binary search lookup. |
| `+0x64` | 4 | `u8[4]` | `pad_100` | Alignment padding between the path string and the 8-byte aligned offset. Never read by the client. |
| `+0x68` | 8 | `i64` LE | `dataOffset` | Byte offset of the entry's payload within `data/data.vfs`. Since the first 24 bytes of the data blob are reserved for the header echo, the first entry offset is always `24`. |
| `+0x70` | 8 | `i64` LE | `dataSize` | Byte size of the payload. The client only consumes the low 32 bits (refuses reads if high dword is non-zero). |
| `+0x78` | 8 | `u64` LE | `creation_time` | NTFS `FILETIME` (100-ns intervals since 1601-01-01) recording the source file's creation timestamp. Never read by the client. |
| `+0x80` | 8 | `u64` LE | `last_access_time`| NTFS `FILETIME` recording the source file's last access timestamp. Never read by the client. |
| `+0x88` | 8 | `u64` LE | `last_write_time` | NTFS `FILETIME` recording the source file's last modification timestamp. Never read by the client. |

---

## 4. Storage Model and Contiguity

- **No Compression or Encryption:** Payload blocks are stored fully raw inside the data blob (`data/data.vfs`). Stored size is equal to memory size.
- **Perfect Tiling:** There is no inter-entry padding. The payload offset of entry *N+1* is exactly `entry[N].dataOffset + entry[N].dataSize`. The final entry terminates precisely at the end of the `data/data.vfs` file.
- **Deduplication:** The VFS archive does not implement payload deduplication. Duplicate assets (if any) are stored as separate entries with separate offsets.
