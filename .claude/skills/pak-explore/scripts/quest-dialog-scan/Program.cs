// quest-dialog-scan PASS 3 — final confirmations.
// THROWAWAY, not in solution.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding cp949 = Encoding.GetEncoding(949);

string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";
foreach (string root in DefaultClientRoots())
{
    string ci = Path.Combine(root, "data.inf");
    string cv = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(ci) && File.Exists(cv)) { infPath = ci; vfsPath = cv; break; }
}
using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted {archive.GetEntries().Length:N0} entries");
Console.WriteLine();

// =========================================================
// PASS3-A: quests.scr — the stride=3720 gives 488 records
// but only 123 distinct IDs out of 488 slots.
// Quest IDs at stride 3720 are SPARSE — not sequential.
// The file likely has 488 "real" record slots (one per quest definition)
// but IDs can be non-sequential and 0 = empty slot.
// Let's: (1) list all non-zero quest records, (2) map field positions in detail
// =========================================================
Console.WriteLine("=== PASS3-A: quests.scr — sparse record map (stride=3720) ===");
{
    const string path = "data/script/quests.scr";
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    long sz = raw.Length;
    const int stride = 3720;
    int nrec = (int)(sz / stride);

    // Find all non-empty records (u16@0 != 0)
    var quests = new List<(int idx, ushort id, ReadOnlyMemory<byte> data)>();
    for (int i = 0; i < nrec; i++)
    {
        ushort id = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * stride, 2));
        if (id != 0) quests.Add((i, id, mem.Slice(i * stride, stride)));
    }
    Console.WriteLine($"  Total slots: {nrec}, non-empty: {quests.Count}");
    Console.WriteLine($"  First 20 quest IDs: {string.Join(", ", quests.Take(20).Select(q => q.id))}");
    Console.WriteLine($"  Last 5 quest IDs: {string.Join(", ", quests.TakeLast(5).Select(q => q.id))}");
    Console.WriteLine($"  ID range: {quests.Min(q => q.id)}..{quests.Max(q => q.id)}");
    Console.WriteLine();

    // Now fully decode the first 10 non-empty quests
    Console.WriteLine("  First 10 non-empty quest records:");
    foreach (var (idx, qid, qmem) in quests.Take(10))
    {
        ReadOnlySpan<byte> rec = qmem.Span;

        // Based on the hex head, we can see the structure:
        // +0x00: u16 quest_id
        // +0x02: CP949 quest name (null-terminated)
        // [then zeros to +0x40]
        // +0x40..+0x45: bytes 05 06 07 08 09 0A — these look like sequential step indices!
        //   Could be an array of quest step IDs or action codes.
        // +0x54: 88 71 84 2B = some u32 value (730100104)
        //   As Unix timestamp: 1993-02-19 — likely NOT a timestamp
        //   Could be a hash of a linked field or a bitmask
        // +0x58: 89 71 84 2B = 730100105 — consecutive with above (might be 2 u32 pointers or indices)
        // +0x62: u16 = 2 = something (objective count? step count?)
        // +0x64: CP949 text "마을 촌장과 대화" = "Talk to village elder"
        //   This is OBJECTIVE/STEP TEXT
        // +0xB0: 01 00 00 00 — a u32 = 1 (could be step sub-index or reward count)
        // +0xE4: 30 00 00 00 = 48 (could be item ID or EXP reward count)

        string name = ReadNull(rec.Slice(2), cp949);
        // Decode step bytes at +0x40:
        string stepBytes = $"{rec[0x40]:X2} {rec[0x41]:X2} {rec[0x42]:X2} {rec[0x43]:X2} {rec[0x44]:X2} {rec[0x45]:X2}";
        // The pair of u32 at +0x54:
        uint v54 = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x54, 4));
        uint v58 = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0x58, 4));
        // The u16 at +0x62 (the "step" count or NPC reference):
        ushort v62 = BinaryPrimitives.ReadUInt16LittleEndian(rec.Slice(0x62, 2));
        // The text at +0x64 (first quest objective/step text):
        string obj1 = ReadNull(rec.Slice(0x64), cp949);
        // The u32 at +0xB0:
        uint vB0 = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0xB0, 4));
        // The u32 at +0xE4:
        uint vE4 = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(0xE4, 4));

        Console.WriteLine($"  Quest [{idx}] id={qid} name=[{name}]");
        Console.WriteLine($"    step-bytes@0x40=[{stepBytes}]");
        Console.WriteLine($"    u32@0x54={v54} u32@0x58={v58}");
        Console.WriteLine($"    u16@0x62={v62} obj-text@0x64=[{(obj1.Length>60?obj1[..60]:obj1)}]");
        Console.WriteLine($"    u32@0xB0={vB0} u32@0xE4={vE4}");
    }
    Console.WriteLine();

    // FIELD OCCUPANCY across all non-empty quest records
    // Focus on u32-aligned fields
    int nq = quests.Count;
    var occupancy = new int[stride / 4];
    var sumValues = new long[stride / 4];
    foreach (var (idx, qid, qmem) in quests)
    {
        ReadOnlySpan<byte> rec = qmem.Span;
        for (int off = 0; off + 4 <= stride; off += 4)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(off, 4));
            if (v != 0) { occupancy[off / 4]++; sumValues[off / 4] += v; }
        }
    }
    Console.WriteLine("  u32-field occupancy for non-empty records (>10% only):");
    for (int idx2 = 0; idx2 < occupancy.Length; idx2++)
    {
        int off = idx2 * 4;
        double pct = 100.0 * occupancy[idx2] / nq;
        if (pct > 10)
        {
            uint firstVal = 0;
            foreach (var (idx, qid, qmem) in quests.Take(1))
                firstVal = BinaryPrimitives.ReadUInt32LittleEndian(qmem.Span.Slice(off, 4));
            Console.WriteLine($"    +{off:X3} ({off,4}):  {pct,6:F1}%  first={firstVal}");
        }
    }
    Console.WriteLine();

    // Print the high-offset region of quest 1 (the end of the 3720-byte record)
    // to understand reward and completion fields
    Console.WriteLine("  Quest 0 final 256 bytes (+0xD78..+0xE87):");
    {
        ReadOnlySpan<byte> rec = quests[0].data.Span;
        int start = stride - 256;
        for (int row = 0; row * 16 < 256; row++)
        {
            int rowStart = start + row * 16;
            int rowEnd = Math.Min(rowStart + 16, stride);
            Console.Write($"    +{rowStart:X3}: ");
            for (int b = rowStart; b < rowEnd; b++) Console.Write($"{rec[b]:X2} ");
            Console.Write(" | ");
            byte[] buf = rec.Slice(rowStart, rowEnd - rowStart).ToArray();
            Console.WriteLine(cp949.GetString(buf).Replace('\0', '.').Replace('\n', '.'));
        }
    }
}
Console.WriteLine();

// =========================================================
// PASS3-B: npc.scr — stride=404 detailed decode
// stride=404 gives 2510 records, IDs appear sequential-ish
// =========================================================
Console.WriteLine("=== PASS3-B: npc.scr — stride=404 full decode ===");
{
    const string path = "data/script/npc.scr";
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    long sz = raw.Length;
    const int stride = 404;
    int nrec = (int)(sz / stride);
    Console.WriteLine($"  stride={stride}, nrec={nrec}");

    // Print first 10 records
    for (int i = 0; i < Math.Min(10, nrec); i++)
    {
        ReadOnlySpan<byte> rec = raw.Slice(i * stride, stride);
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
        uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..8]);
        uint f8 = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..12]);
        uint f12 = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..16]);
        string text = ReadNull(rec.Slice(16), cp949);
        // Count non-zero bytes
        int nz = 0; for (int b = 0; b < stride; b++) if (rec[b] != 0) nz++;
        Console.WriteLine($"    [{i,3}]: id={id} f4={f4} f8={f8} f12={f12} text=[{(text.Length>60?text[..60]:text)}] nz={nz}");
    }
    Console.WriteLine();

    // Field occupancy
    var occ = new int[stride / 4];
    for (int i = 0; i < nrec; i++)
    {
        ReadOnlySpan<byte> rec = raw.Slice(i * stride, stride);
        for (int off = 0; off + 4 <= stride; off += 4)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(rec.Slice(off, 4));
            if (v != 0) occ[off / 4]++;
        }
    }
    Console.WriteLine("  High-occupancy u32 fields (>40%):");
    for (int idx = 0; idx < occ.Length; idx++)
    {
        double pct = 100.0 * occ[idx] / nrec;
        if (pct > 40) Console.WriteLine($"    +{idx*4:X3} ({idx*4,4}): {pct:F1}%");
    }
    Console.WriteLine();

    // Print first 3 records in full hex
    for (int i = 0; i < 3; i++)
    {
        ReadOnlySpan<byte> rec = raw.Slice(i * stride, stride);
        Console.WriteLine($"  --- NPC Record {i} ---");
        for (int row = 0; row * 16 < stride; row++)
        {
            int start = row * 16;
            int end = Math.Min(start + 16, stride);
            Console.Write($"    +{start:X3}: ");
            for (int b = start; b < end; b++) Console.Write($"{rec[b]:X2} ");
            Console.Write(" | ");
            byte[] buf = rec.Slice(start, end - start).ToArray();
            Console.WriteLine(cp949.GetString(buf).Replace('\0', '.').Replace('\n', '.'));
        }
    }
}
Console.WriteLine();

// =========================================================
// PASS3-C: events.scr — real structure analysis
// The first value (u16@0=10551, u16@2=0) followed by
// (u16@4=1, u16@6=7) looks like: u32 file-version/magic + u16 type + u16 subtype?
// Or: first record has ID=10551 (event #10551)?
// Let's check stride=96 more carefully (10010 records starting from 0x000CB895 area?)
// Actually: let's look at the structure as a flat array where record[0].id = 10551
// and see if record[1].id is also plausible
// =========================================================
Console.WriteLine("=== PASS3-C: events.scr — deeper structure analysis ===");
{
    const string path = "data/script/events.scr";
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    long sz = raw.Length; // 960,960

    // stride=91 gives 10560 records. 91 = 7×13 — unusual stride.
    // stride=96 gives 10010 records. 96 = 3×32.
    // Given the head shows mostly zeros and a few scattered values, let's check stride=96:
    // Record 0: 37 29 00 00 | 01 00 07 00 | 00 00 00 00 | 00 00 00 00 | ...
    //   u32@0=10551 (=0x2937), u16@4=1, u16@6=7, u32@8=0, u32@12=0...
    //   At +0x4C: 64 00 00 00 = 100
    //   At +0x50: E8 03 00 00 = 1000
    // Actually for stride=96, record 0 spans bytes 0..95.
    // But bytes 0x44..0x5F were shown and contained 100, 1000 etc.
    // If the record is 96 bytes, fields at 0x44=68 and 0x50=80 are within the first record.

    // Let's look at stride=960 (1001 records - divisor of 960960):
    Console.WriteLine("  Testing stride=960 (1001 records):");
    {
        int s = 960;
        int n = (int)(sz / s);
        for (int i = 0; i < Math.Min(6, n); i++)
        {
            ReadOnlySpan<byte> rec = raw.Slice(i * s, s);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
            uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..8]);
            uint f8 = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..12]);
            int nz = 0; for (int b = 0; b < s; b++) if (rec[b] != 0) nz++;
            string text = ReadNull(rec.Slice(4), cp949);
            Console.WriteLine($"    [{i}]: id=0x{id:X8} f4=0x{f4:X8} f8=0x{f8:X8} nz={nz} text@4=[{(text.Length>40?text[..40]:text)}]");
        }
    }
    Console.WriteLine();

    // The u16@6 = 7 in the header (u16 at offset 6 = 7). Could be "7 event types" or "entry count=7"?
    // The bytes at +0x64..+0xAF contain u32 values that look like file offsets in the range
    // 0x000CB895 (= 833,685) ... 0x000E7E00 (= 950,784)
    // Those ARE within the 960,960 byte file!
    // Let's check: if 0x64 = start of an offset table, and the offsets point within the file...
    Console.WriteLine("  Offset table hypothesis: u32 values at +0x60..+0xB0:");
    for (int i = 0; i < 20; i++)
    {
        int off = 0x60 + i * 4;
        if (off + 4 > sz) break;
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(off, 4));
        string inRange = (v > 0 && v < sz) ? $" --> points to +0x{v:X6}" : "";
        Console.Write($"    [{off:X3}]={v} (0x{v:X8}){inRange}");
        if (v > 0 && v < sz)
        {
            // peek the target
            ReadOnlySpan<byte> target = raw.Slice((int)v, Math.Min(24, (int)(sz - v)));
            string txt = ReadNull(target, cp949);
            if (txt.Length > 0) Console.Write($" text=[{(txt.Length>30?txt[..30]:txt)}]");
        }
        Console.WriteLine();
    }
    Console.WriteLine();

    // If the offset table values ARE within the file, this is a variable-length record file
    // with an index at the front. The count would be the number of non-zero offsets.
    // Let's count valid offsets in the first 1024 bytes:
    Console.WriteLine("  Counting valid in-file offsets in first 4096 bytes:");
    int validOffsets = 0;
    for (int i = 0; i + 4 <= Math.Min(4096, (int)sz); i += 4)
    {
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(i, 4));
        if (v > 100 && v < (uint)sz) validOffsets++;
    }
    Console.WriteLine($"  Valid offsets (100 < v < {sz}): {validOffsets}");

    // Alternative: stride = 96 with event type=action
    Console.WriteLine();
    Console.WriteLine("  Stride=96 first 8 records (with field scan):");
    {
        int s = 96;
        int n = (int)(sz / s);
        for (int i = 0; i < Math.Min(8, n); i++)
        {
            ReadOnlySpan<byte> rec = raw.Slice(i * s, s);
            // Find first non-zero cluster
            int firstNZ = -1;
            for (int b = 0; b < s; b++) { if (rec[b] != 0) { firstNZ = b; break; } }
            if (firstNZ < 0) continue;
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
            uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..8]);
            if (id == 0 && f4 == 0) continue;
            string text = ReadNull(rec.Slice(8), cp949);
            Console.WriteLine($"    [{i}]: id=0x{id:X8} f4=0x{f4:X8} firstNZ@{firstNZ} text=[{(text.Length>40?text[..40]:text)}]");
        }
    }
}
Console.WriteLine();

// =========================================================
// PASS3-D: discript.sc — the real structure
// hex head showed: 08 00 00 00 03 00 00 00 B9 AB B8 AE 20 B1 C7 C0
// u32@0=8, u32@4=3, then "무리 권유" (Guild/party join invite text)
// Then zeros until +0x26=38: 30 00 00 00 = u32=0x30=48
// At +0x40=64: 09 00 00 00 03 00 00 00 "무리 탈퇴" (Leave party)
// Pattern: each record is 64 bytes. But 2244 / 64 = 35.0625 (not exact).
// The CP949 text decoded showed items until "따라가기" — these are PARTY MENU items.
// Let's try stride=44 or 48 or related:
// 2244 = 4 × 561 = 4 × 3 × 11 × 17
// Divisors: 4, 11, 12, 17, 33, 34, 44, 51, 66, 68, 102, 132, 187
// stride=44 (51 records): check hex
// At +0x00: [08 00 00 00][03 00 00 00][무리 권유 14 bytes null-term] then zeros
// Total needed: 4+4+14+1+? = 23+ bytes → stride=44 is plausible
// =========================================================
Console.WriteLine("=== PASS3-D: discript.sc — correct stride hunt ===");
{
    const string path = "data/script/discript.sc";
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    long sz = raw.Length; // 2244

    Console.WriteLine($"  Size: {sz:N0} = 4×3×11×17 = {4*3*11*17}");
    // Try all divisors as strides
    foreach (int s in new[] { 4, 11, 12, 17, 33, 34, 44, 51, 66, 68, 102, 132, 187 })
    {
        int n = (int)(sz / s);
        ReadOnlySpan<byte> rec0 = raw.Slice(0, s);
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec0[0..Math.Min(4, s)]);
        string text = s >= 8 ? ReadNull(rec0.Slice(8), cp949) : "";
        Console.WriteLine($"  stride={s,4} ({n,4} rec): rec0.id={id} text=[{(text.Length>30?text[..30]:text)}]");
    }
    Console.WriteLine();

    // The text "무리 권유" appears at +8, so stride must be >= 8+15+null+... Let's say 68 bytes:
    // At stride=68: 2244/68 = 33 records. Let's check:
    if (sz % 68 == 0)
    {
        Console.WriteLine("  stride=68:");
        int n = (int)(sz / 68);
        for (int i = 0; i < Math.Min(8, n); i++)
        {
            ReadOnlySpan<byte> rec = raw.Slice(i * 68, 68);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
            uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..8]);
            string text = ReadNull(rec.Slice(8), cp949);
            Console.WriteLine($"    [{i}]: id={id} f4={f4} text=[{text}]");
        }
    }
    else Console.WriteLine("  68 does not divide 2244");

    // Try stride=44:
    if (sz % 44 == 0)
    {
        Console.WriteLine("  stride=44 (51 records):");
        int n = (int)(sz / 44);
        for (int i = 0; i < Math.Min(8, n); i++)
        {
            ReadOnlySpan<byte> rec = raw.Slice(i * 44, 44);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
            uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(rec[4..8]);
            string text = ReadNull(rec.Slice(8), cp949);
            Console.WriteLine($"    [{i}]: id={id} f4={f4} text=[{text}]");
        }
    }

    // Full hex dump of discript.sc (small file)
    Console.WriteLine($"\n  Full hex dump of discript.sc ({sz} bytes):");
    for (int row = 0; row * 16 < sz; row++)
    {
        int start = row * 16;
        int end = Math.Min(start + 16, (int)sz);
        Console.Write($"  +{start:X3}: ");
        for (int b = start; b < end; b++) Console.Write($"{raw[b]:X2} ");
        Console.Write(" | ");
        byte[] buf = raw.Slice(start, end - start).ToArray();
        Console.WriteLine(cp949.GetString(buf).Replace('\0', '.').Replace('\n', '.'));
    }
}
Console.WriteLine();

// =========================================================
// PASS3-E: helps.scr — hierarchical structure
// The structure seems to be:
//   Outer page header (16 bytes): [page_id u32][section_id u32][? u32][sub_count u32]
//   Then sub_count × inner entries (each ≈ 48 bytes):
//     [entry_id u32][f4 u32][f8 u32][type_byte u8][CP949 text null-terminated, padded to 39 bytes]
// The outer header says: page_id=1, section_id=1, ?=0, sub_count=11
// Then 11 inner entries, each 48 bytes = 528 bytes total inner
// Total first page = 16 + 11*48 = 16 + 528 = 544 bytes
// Let's verify: byte 544 should be the start of the second outer page
// =========================================================
Console.WriteLine("=== PASS3-E: helps.scr — hierarchical page structure ===");
{
    const string path = "data/script/helps.scr";
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    long sz = raw.Length; // 66,144

    // Inner entry stride candidates: 48 or 52
    // From the hex: inner entry at +0x10 starts with [01 00 00 00 01 00 00 00 00 00 00 00]
    // = [sub_id=1][f4=1][f8=0] then 01 = type? then "목차 1" null-term
    // "목차 1" = B8 F1 C2 F7 20 31 = 6 bytes + 00 = 7 bytes → padded
    // At +0x40 = 64: [00 00 00 00 00] B1 E2... → sub_id=0??? or this is a different format

    // Actually looking at the bytes carefully:
    // +0x10 = offset 16: 01 00 00 00 01 00 00 00 00 00 00 00 01 B8 F1 C2
    // So: sub_id@16=1, f4@20=1, f8@24=0, then @28=01 (type byte), @29=B8 F1 C2 F7 20 31 00 (text)
    // That's 12-byte header + 1-byte type + text. If text buffer is 35 bytes → 48 total.
    // At +0x40 = offset 64: 00 00 00 00 00 B1 E2 BA BB 20 C1 B6 C0 DB 20 B9 E6 B9 FD 31 00...
    // = sub_id=0?? That doesn't match. OR: the inner entry at +0x40 has:
    //   sub_id=0x00000000 (= 0!), but wait — entry starts with 00...
    //   The offset 0x40 = 64 = 16(outer) + 48(one inner) → second inner entry
    //   Second inner: sub_id@64=0? No that can't be right if it's a sequential list.
    //
    // Alternative: inner entry is 0x30 = 48 bytes, but the sub_id is NOT at +0.
    // Let me look at what 48-byte chunk at +0x40 looks like again:
    //   0x40: 00 00 00 00 00 B1 E2 BA BB 20 C1 B6 C0 DB 20 B9
    //   0x50: E6 B9 FD 31 00 00 00 00 00 00 00 00 00 00 00 00
    // = sub_id=0, f4=0, f8=3135418624 → garbage? Or it's 5 bytes of zeros then text.
    // Actually: 00 00 00 00 [00] B1 E2 BA BB ... → 4-byte zeros + 1-byte = 0 + text
    // So maybe: sub_id(u32)=0, then type_byte=0 (not 1 this time), then text "기본 조작 방법1"
    // But sub_id=0 with sequential numbering from 1... unless the sub_ids ARE sequential 0,1,2...

    // REVISED hypothesis: inner entry = [sub_id u32][f4 u32][f8 u32][text buffer 36 bytes] = 48 bytes
    // sub_id of entry[0] = 0 (zero-indexed!)
    // sub_id at offset 16 = 01 00 00 00 → but that reads as 1, not 0

    // Let me try a different inner structure: the outer header at +0 is 16 bytes, and
    // WITHIN those 16 bytes we have [page_id][section_id][?=0][count].
    // After the 16-byte header, the entries follow. But the FIRST entry at +0x10 starts with:
    //   01 00 00 00 01 00 00 00 00 00 00 00 01 B8 F1...
    // which looks like [sub_id=1][f4=1][f8=0][type=01][text="목차 1"]
    // And the SECOND entry at +0x10+0x30=+0x40 starts with:
    //   00 00 00 00 00 B1 E2 BA BB 20 C1 B6 C0 DB 20 B9...
    // which looks like [sub_id=0][f4=0][type=0][text="기본 조작 방법1"]
    // So sub_ids go: 1, 0, 0, 0... This might mean the FIRST inner entry is a "title" entry,
    // and subsequent entries are "content" entries with sub_id=0.

    // Let's try inner stride = 0x30 = 48, starting at offset 16:
    Console.WriteLine("  Decoding page structure with outer(16B) + inner-entries(48B):");
    int pos = 0;
    int pageCount = 0;
    while (pos + 16 <= sz && pageCount < 5)
    {
        // Outer page header
        uint pageId = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(pos, 4));
        uint secId = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(pos + 4, 4));
        uint field8 = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(pos + 8, 4));
        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(pos + 12, 4));

        Console.WriteLine($"\n  Page {pageCount}: @+{pos:X5} pageId={pageId} secId={secId} f8={field8} entryCount={entryCount}");

        if (entryCount > 200 || entryCount == 0) { Console.WriteLine("  [stopping — invalid entry count]"); break; }

        pos += 16;
        for (int e = 0; e < entryCount && pos + 48 <= sz; e++)
        {
            ReadOnlySpan<byte> entry = raw.Slice(pos, 48);
            uint subId = BinaryPrimitives.ReadUInt32LittleEndian(entry[0..4]);
            uint ef4 = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..8]);
            uint ef8 = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12]);
            byte typeB = entry[12];
            string text = ReadNull(entry.Slice(13), cp949);
            Console.WriteLine($"    Entry[{e}] @+{pos:X5}: subId={subId} f4={ef4} f8={ef8} type=0x{typeB:X2} text=[{(text.Length>50?text[..50]:text)}]");
            pos += 48;
        }
        pageCount++;
    }
    Console.WriteLine();

    // Verify the total computed size matches the file size:
    Console.WriteLine($"  Position after {pageCount} pages: {pos} (file size: {sz})");
}
Console.WriteLine();

// =========================================================
// PASS3-F: helps.scr — try the real inner structure
// From the decoded bytes, the "내용 항목" inner entry structure is:
// [sub_id:4][ef4:4][ef8:4][type:1][text:up to ?]
// But the text "기본 조작 방법1" appeared to start at offset +5 in the inner entry.
// Maybe: [sub_id:4][ef4:4][type:1][text]? = 9 bytes header.
// OR the inner entry is exactly 48 bytes but structured as:
// [u32 sub_id][u32 f4][u32 f8][u8 type][char[35] text_buffer]
// = 4+4+4+1+35 = 48 bytes exactly.
// The character at entry[12]=0x01 is the type=1 for the first entry (title).
// For the second entry: entry[12]=0x00 (type=0 for content).
// =========================================================
Console.WriteLine("=== PASS3-F: helps.scr — sub-entry field decode ===");
{
    const string path = "data/script/helps.scr";
    ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
    ReadOnlySpan<byte> raw = mem.Span;
    long sz = raw.Length;

    // Print the first 3×48 sub-entries starting at +0x10:
    int baseOff = 16; // after outer header
    for (int e = 0; e < 12; e++)
    {
        int off = baseOff + e * 48;
        if (off + 48 > sz) break;
        ReadOnlySpan<byte> entry = raw.Slice(off, 48);
        uint subId = BinaryPrimitives.ReadUInt32LittleEndian(entry[0..4]);
        uint ef4 = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..8]);
        uint ef8 = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12]);
        byte type = entry[12];
        // text at 13:
        string text13 = ReadNull(entry.Slice(13), cp949);
        // Also: text at 12 (if type byte is actually first text byte):
        string text12 = ReadNull(entry.Slice(12), cp949);
        Console.WriteLine($"  Entry[{e}] @+{off:X3}: subId={subId} ef4={ef4} ef8={ef8} type=0x{type:X2}({type}) text@13=[{text13}] text@12=[{text12}]");
    }
}
Console.WriteLine();

Console.WriteLine("Done.");
return 0;

static string ReadNull(ReadOnlySpan<byte> span, Encoding cp949)
{
    int end = span.IndexOf((byte)0);
    if (end == 0) return "";
    if (end < 0) end = span.Length;
    return cp949.GetString(span.Slice(0, end).ToArray());
}

static IEnumerable<string> DefaultClientRoots()
{
    string? env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrWhiteSpace(env)) yield return env;
    for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        yield return Path.Combine(d.FullName, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
    yield return "D:/MartialHeroesClient";
}
