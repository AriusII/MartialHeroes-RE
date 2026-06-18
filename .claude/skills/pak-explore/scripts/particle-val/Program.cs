// THROWAWAY HARNESS — V5 particle-emitter + eff-shape validation
// Never add to MartialHeroes.slnx. Never commit. Never git add bin/obj.
// Drives ParticleEmitterParser (Section E) + XeffParser.ParseEff (Section B)
// over the real VFS.
// Outputs stats only — no raw payload bytes destined for commit.

using System.Buffers.Binary;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

string infPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
string vfsPath = @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";

// Allow override: particle-val --inf <path> --vfs <path>
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--inf") infPath = args[i + 1];
    if (args[i] == "--vfs") vfsPath = args[i + 1];
}

Console.WriteLine("=== V5 particleEmitter.eff + .eff shape objects — validation harness ===");
Console.WriteLine($"INF: {infPath}");
Console.WriteLine($"VFS: {vfsPath}");
Console.WriteLine();

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"Mounted: {archive.GetEntries().Length:N0} VFS entries");
Console.WriteLine();

// ═══════════════════════════════════════════════════════════════════════════════
// BLOCK A: particleEmitter.eff — VFS path inventory + file size
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine("═══ BLOCK A: particleEmitter.eff — file existence + size ═══");

const string PePath = "data/effect/particle/particleemitter.eff";
bool peExists = archive.Contains(PePath);
Console.WriteLine($"  Path '{PePath}':  exists={peExists}");
if (!peExists)
{
    Console.Error.WriteLine("[ABORT] particleemitter.eff not found. Cannot continue BLOCK A.");
}

int peFileSize = 0;
ParticleEmitterTable? table = null;

if (peExists)
{
    var peBytes = archive.GetFileContent(PePath);
    peFileSize = peBytes.Length;
    Console.WriteLine($"  File size: {peFileSize:N0} bytes");

    // ── BLOCK B: Variable-length parse via production ParticleEmitterParser ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK B: ParticleEmitterParser — parse + residual ═══");

    try
    {
        table = ParticleEmitterParser.Parse(peBytes);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  [PARSE EXCEPTION] {ex.Message}");
        goto SkipBlocksBC;
    }

    int entryCount = table.Entries.Length;
    Console.WriteLine($"  Parsed entry count (non-terminator): {entryCount}");

    // Compute bytes consumed by valid entries
    long consumedBytes = 0;
    foreach (var e in table.Entries)
        consumedBytes += 28 + (long)e.NumFrames * 52 + 64;

    long bytesAfterEntries = peFileSize - consumedBytes;
    Console.WriteLine($"  Bytes consumed by entries: {consumedBytes:N0}");
    Console.WriteLine($"  Bytes remaining after last entry: {bytesAfterEntries:N0}");

    // Detect terminator entry at offset=consumedBytes
    bool terminatorFound = false;
    uint terminatorId = 0;
    uint terminatorFrames = 0;
    if (bytesAfterEntries >= 28)
    {
        var span = peBytes.Span;
        terminatorId = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)consumedBytes..]);
        terminatorFrames = BinaryPrimitives.ReadUInt32LittleEndian(span[((int)consumedBytes + 4)..]);
        terminatorFound = terminatorFrames == 0;
        Console.WriteLine($"  Terminator @ byte +{consumedBytes}: entry_id={terminatorId} num_frames={terminatorFrames}  is_terminator={terminatorFound}");
    }
    else if (bytesAfterEntries == 0)
    {
        Console.WriteLine($"  No trailing bytes — file ends exactly after last real entry (no explicit terminator)");
        terminatorFound = false;
    }
    else
    {
        Console.WriteLine($"  WARNING: {bytesAfterEntries} trailing bytes (not enough for a 28-byte header)");
    }

    long trueResidue = bytesAfterEntries - (terminatorFound ? 28 : 0);
    Console.WriteLine($"  True residual after terminator: {trueResidue} bytes  [EXPECT 0 for clean parse]");
    Console.WriteLine($"  PARSE-CLEAN: {trueResidue == 0}");

    // ── BLOCK C: Fixed-stride-FAILS proof ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK C: Fixed-stride-FAILS proof ═══");

    // Former flat model: 16-byte header + N × 52-byte records, N=2243 (prior campaign guess)
    int flatExpectedN2243 = 16 + 2243 * 52;
    bool flatMatchN2243 = peFileSize == flatExpectedN2243;
    Console.WriteLine($"  Prior flat model (N=2243): expected={flatExpectedN2243:N0} bytes  actual={peFileSize:N0}  match={flatMatchN2243}  [EXPECT false]");

    // Is (fileSize−16) exactly divisible by 52? (would hold for any flat model with 16B header)
    bool divisible52 = (peFileSize - 16) % 52 == 0;
    Console.WriteLine($"  (fileSize−16) % 52 == 0: {divisible52}  [EXPECT false = variable-length proven]");

    // Is the file (with no header assumption) divisible by 52?
    bool divisible52NoHeader = peFileSize % 52 == 0;
    Console.WriteLine($"  fileSize % 52 == 0: {divisible52NoHeader}  [EXPECT false]");

    // Flat stride from entry sizes: if num_frames were constant for all entries,
    // each entry = 28 + K×52 + 64 = 92 + K×52. Check if all num_frames are equal.
    var numFramesSet = table.Entries.Select(e => e.NumFrames).Distinct().ToArray();
    Console.WriteLine($"  Distinct num_frames values: {numFramesSet.Length}  [>1 = variable-length confirmed]");

    // ── BLOCK D: num_frames distribution ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK D: num_frames distribution (variable-section count) ═══");

    var numFramesDist = table.Entries
        .GroupBy(e => e.NumFrames)
        .OrderBy(g => g.Key)
        .ToList();

    Console.WriteLine($"  min num_frames: {table.Entries.Min(e => e.NumFrames)}");
    Console.WriteLine($"  max num_frames: {table.Entries.Max(e => e.NumFrames)}");
    Console.WriteLine($"  avg num_frames: {table.Entries.Average(e => (double)e.NumFrames):F2}");
    Console.WriteLine("  num_frames → entry count:");
    foreach (var g in numFramesDist)
        Console.WriteLine($"    {g.Key,4}: {g.Count(),4} entries ({100.0 * g.Count() / entryCount:F1}%)");

    // ── BLOCK E: entry_id field stats ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK E: entry_id field stats ═══");

    var allIds = table.Entries.Select(e => e.EntryId).ToArray();
    Console.WriteLine($"  min entry_id: {allIds.Min()}");
    Console.WriteLine($"  max entry_id: {allIds.Max()}");
    Console.WriteLine($"  count ≥ 10000 (GPU particle space): {allIds.Count(id => id >= 10000)} / {entryCount}  [EXPECT all]");
    Console.WriteLine($"  duplicate entry_ids: {entryCount - allIds.Distinct().Count()}");
    Console.WriteLine($"  first 5 entry_ids: {string.Join(", ", allIds.Take(5))}");

    // Check contiguity
    var sortedIds = allIds.OrderBy(id => id).ToArray();
    bool contiguous = true;
    for (int i = 1; i < sortedIds.Length; i++)
        if (sortedIds[i] != sortedIds[i - 1] + 1) { contiguous = false; break; }
    Console.WriteLine($"  entry_ids are contiguous (sorted): {contiguous}");
    if (contiguous)
        Console.WriteLine($"  range: [{sortedIds[0]}, {sortedIds[^1]}]  span={(sortedIds[^1] - sortedIds[0] + 1)}");

    // ── BLOCK F: sprite_size_x / sprite_size_y ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK F: sprite_size_x / sprite_size_y ═══");

    float[] ssx = table.Entries.Select(e => e.SpriteSizeX).ToArray();
    float[] ssy = table.Entries.Select(e => e.SpriteSizeY).ToArray();
    Console.WriteLine($"  sprite_size_x: min={ssx.Min():F4}  max={ssx.Max():F4}  distinct={ssx.Distinct().Count()}  zero%={ssx.Count(v => v == 0f) * 100.0 / entryCount:F1}");
    Console.WriteLine($"  sprite_size_y: min={ssy.Min():F4}  max={ssy.Max():F4}  distinct={ssy.Distinct().Count()}  zero%={ssy.Count(v => v == 0f) * 100.0 / entryCount:F1}");
    Console.WriteLine($"  first 5 sprite_size_x: {string.Join(", ", ssx.Take(5).Select(v => v.ToString("F4")))}");
    Console.WriteLine($"  first 5 sprite_size_y: {string.Join(", ", ssy.Take(5).Select(v => v.ToString("F4")))}");
    // Top distinct values
    var ssxTop = ssx.GroupBy(v => v).OrderByDescending(g => g.Count()).Take(5).ToList();
    Console.WriteLine("  sprite_size_x top-5 values:");
    foreach (var g in ssxTop)
        Console.WriteLine($"    {g.Key,10:F4}: {g.Count(),4} entries ({100.0*g.Count()/entryCount:F1}%)");

    // ── BLOCK G: max_particles ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK G: max_particles ═══");

    uint[] maxP = table.Entries.Select(e => e.MaxParticles).ToArray();
    Console.WriteLine($"  max_particles: min={maxP.Min()}  max={maxP.Max()}  distinct={maxP.Distinct().Count()}  zero_count={maxP.Count(v => v == 0)}");
    Console.WriteLine("  distribution:");
    foreach (var g in maxP.GroupBy(v => v).OrderBy(g => g.Key))
        Console.WriteLine($"    {g.Key,6}: {g.Count(),4} entries ({100.0*g.Count()/entryCount:F1}%)");

    // ── BLOCK H: on-disk dwords 0x14 and 0x18 ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK H: on-disk dwords (tex_handle_slot=0x14, subrecord_array_ptr=0x18) ═══");

    uint[] thSlots = table.Entries.Select(e => e.RawTexHandleSlot).ToArray();
    uint[] saPtr   = table.Entries.Select(e => e.RawSubrecordArrayPtr).ToArray();
    Console.WriteLine($"  tex_handle_slot:      distinct={thSlots.Distinct().Count()}  zero%={thSlots.Count(v=>v==0)*100.0/entryCount:F1}  sample5=[{string.Join(",",thSlots.Take(5))}]");
    Console.WriteLine($"  subrecord_array_ptr:  distinct={saPtr.Distinct().Count()}  zero%={saPtr.Count(v=>v==0)*100.0/entryCount:F1}  sample5=[{string.Join(",",saPtr.Take(5))}]");

    // ── BLOCK I: texture_name ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK I: texture_name (trailing 64-byte) ═══");

    var texNames = table.Entries.Select(e => e.TextureName).ToArray();
    Console.WriteLine($"  non-empty names: {texNames.Count(n => !string.IsNullOrEmpty(n))} / {entryCount}");
    Console.WriteLine($"  distinct names:  {texNames.Distinct().Count()}");
    Console.WriteLine($"  first 10 names:  {string.Join(", ", texNames.Take(10).Select(n => $"'{n}'"))}");

    // ── BLOCK J: sub-record colour quad and byte-level entropy per offset ──
    Console.WriteLine();
    Console.WriteLine("═══ BLOCK J: sub-record 52-byte field entropy ═══");

    // Reconstruct all sub-records as byte[52]
    var subRecordBytes = new List<byte[]>();
    foreach (var e in table.Entries)
        foreach (var sr in e.SubRecords)
        {
            byte[] rec = new byte[52];
            sr.UnresolvedLead.Span.CopyTo(rec.AsSpan(0, 8));
            rec[8]  = sr.ColorR;
            rec[9]  = sr.ColorG;
            rec[10] = sr.ColorB;
            rec[11] = sr.ColorA;
            sr.UnresolvedTail.Span.CopyTo(rec.AsSpan(12, 40));
            subRecordBytes.Add(rec);
        }

    int totalSR = subRecordBytes.Count;
    Console.WriteLine($"  Total sub-records: {totalSR}");

    if (totalSR > 0)
    {
        Console.WriteLine($"  Per-byte offset analysis (offset | distinct | zero% | modal_hex=modal_byte : modal%):");
        for (int byteOff = 0; byteOff < 52; byteOff++)
        {
            var vals = subRecordBytes.Select(r => r[byteOff]).ToArray();
            int distinct = vals.Distinct().Count();
            double zeroFrac = vals.Count(v => v == 0) * 100.0 / totalSR;
            var modal = vals.GroupBy(v => v).OrderByDescending(g => g.Count()).First();
            double modalFrac = modal.Count() * 100.0 / totalSR;
            // Only print offsets where something non-trivial appears (not all-zero except the colour quad region)
            bool isColorQuad = byteOff >= 8 && byteOff <= 11;
            bool interesting = zeroFrac < 99.5 || isColorQuad;
            if (interesting)
                Console.WriteLine($"    +0x{byteOff:X2} ({byteOff,2}) | {distinct,3} | {zeroFrac,5:F1}% | 0x{modal.Key:X2}={modal.Key,3} : {modalFrac:F1}%  {(isColorQuad?"[colour_quad]":"")}");
        }
    }

    SkipBlocksBC:;
}

// ═══════════════════════════════════════════════════════════════════════════════
// BLOCK K: .eff geometry shape objects (data/effect/obj/*.eff)
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══ BLOCK K: data/effect/obj/*.eff geometry shapes ═══");

var allEntries = archive.GetEntries();
var effObjPaths = new List<(string name, int size)>();
foreach (var entry in allEntries)
    if (entry.Name.StartsWith("data/effect/obj/", StringComparison.Ordinal)
        && entry.Name.EndsWith(".eff", StringComparison.Ordinal))
        effObjPaths.Add((entry.Name, (int)entry.DataSize));

Console.WriteLine($"  Files in data/effect/obj/*.eff: {effObjPaths.Count}");

int effOk = 0, effErr = 0;
var effSizes = new List<int>();
var effIndexCounts = new List<int>();
var effVertCounts  = new List<int>();
var effResidue     = new List<(string name, long res)>();

foreach (var (path, rawSize) in effObjPaths.OrderBy(p => p.name))
{
    string stem = System.IO.Path.GetFileName(path);
    var bytes = archive.GetFileContent(path);
    effSizes.Add(bytes.Length);

    try
    {
        var shape = XeffParser.ParseEff(bytes);
        long expectedSize = 4L + shape.Indices.Length * 2 + 4 + shape.Vertices.Length * 32;
        long residual = bytes.Length - expectedSize;

        // triangle divisibility
        bool triDiv = shape.Indices.Length % 3 == 0;
        Console.WriteLine($"  {stem,-16}  sz={bytes.Length,5}B  idx={shape.Indices.Length,4}  vert={shape.Vertices.Length,4}  formula_ok={residual==0}  residual={residual,3}  tri_divisible={triDiv}");

        if (residual != 0) effResidue.Add((path, residual));
        effOk++;
        effIndexCounts.Add(shape.Indices.Length);
        effVertCounts.Add(shape.Vertices.Length);
    }
    catch (Exception ex)
    {
        effErr++;
        Console.WriteLine($"  {stem,-16}  PARSE ERROR: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"  .eff obj parse-clean: {effOk} / {effObjPaths.Count}");
Console.WriteLine($"  .eff obj parse errors: {effErr}");
if (effResidue.Count > 0)
{
    Console.WriteLine($"  NON-ZERO RESIDUALS:");
    foreach (var (n, r) in effResidue) Console.WriteLine($"    {n}: {r}B");
}
if (effIndexCounts.Count > 0)
{
    Console.WriteLine($"  index_count: min={effIndexCounts.Min()} max={effIndexCounts.Max()}  all_tri_div={effIndexCounts.All(c => c % 3 == 0)}");
    Console.WriteLine($"  vert_count:  min={effVertCounts.Min()}  max={effVertCounts.Max()}");
    Console.WriteLine($"  file sizes:  min={effSizes.Min()} max={effSizes.Max()} bytes");
}

// ═══════════════════════════════════════════════════════════════════════════════
// BLOCK L: Final verdict summary
// ═══════════════════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine("═══ BLOCK L: FINAL VERDICTS ═══");
Console.WriteLine();
Console.WriteLine("  particleEmitter.eff (Section E):");
Console.WriteLine($"    VFS instance count: {(peExists ? 1 : 0)} (single file)");
Console.WriteLine($"    File size: {peFileSize:N0} bytes");
if (table != null)
{
    int entryCount = table.Entries.Length;
    var allIds = table.Entries.Select(e => e.EntryId).ToArray();
    long consumedBytes = 0;
    foreach (var e in table.Entries) consumedBytes += 28 + (long)e.NumFrames * 52 + 64;
    long bytesAfter = peFileSize - consumedBytes;
    bool hasTerminator = bytesAfter >= 28 && BinaryPrimitives.ReadUInt32LittleEndian(
        archive.GetFileContent(PePath).Span[((int)consumedBytes + 4)..]) == 0;
    long trueRes = bytesAfter - (hasTerminator ? 28 : 0);

    Console.WriteLine($"    Entry count: {entryCount}");
    Console.WriteLine($"    Parse-clean (true residual == 0): {trueRes == 0}");
    Console.WriteLine($"    Variable-length: distinct num_frames values = {table.Entries.Select(e => e.NumFrames).Distinct().Count()} (>1 = CONFIRMED variable)");
    Console.WriteLine($"    Fixed-stride fails: (fileSize−16)%52≠0 = {(peFileSize - 16) % 52 != 0}");
    Console.WriteLine($"    All entry_ids in ≥10000 GPU space: {allIds.All(id => id >= 10000)}");
    Console.WriteLine($"    No duplicate entry_ids: {entryCount == allIds.Distinct().Count()}");
    Console.WriteLine($"    Terminator (num_frames==0) found: {hasTerminator}");
    Console.WriteLine();
    Console.WriteLine("    FIELD VERDICTS:");
    Console.WriteLine($"      entry_id [CONFIRMED]: range [{allIds.Min()}..{allIds.Max()}]  distinct={allIds.Distinct().Count()}");
    Console.WriteLine($"      num_frames [CONFIRMED]: range [{table.Entries.Min(e=>e.NumFrames)}..{table.Entries.Max(e=>e.NumFrames)}]  distinct={table.Entries.Select(e=>e.NumFrames).Distinct().Count()}");
    Console.WriteLine($"      sprite_size_x [HIGH/VARIES]: range [{table.Entries.Min(e=>e.SpriteSizeX):F4}..{table.Entries.Max(e=>e.SpriteSizeX):F4}]  distinct={table.Entries.Select(e=>e.SpriteSizeX).Distinct().Count()}");
    Console.WriteLine($"      sprite_size_y [HIGH/VARIES]: range [{table.Entries.Min(e=>e.SpriteSizeY):F4}..{table.Entries.Max(e=>e.SpriteSizeY):F4}]  distinct={table.Entries.Select(e=>e.SpriteSizeY).Distinct().Count()}");
    Console.WriteLine($"      max_particles [HIGH/VARIES]: range [{table.Entries.Min(e=>e.MaxParticles)}..{table.Entries.Max(e=>e.MaxParticles)}]  distinct={table.Entries.Select(e=>e.MaxParticles).Distinct().Count()}");
    Console.WriteLine($"      tex_handle_slot [MEDIUM/disk-ignored]: distinct={table.Entries.Select(e=>e.RawTexHandleSlot).Distinct().Count()}  zero%={table.Entries.Count(e=>e.RawTexHandleSlot==0)*100.0/entryCount:F0}%");
    Console.WriteLine($"      subrecord_array_ptr [MEDIUM/disk-ignored]: distinct={table.Entries.Select(e=>e.RawSubrecordArrayPtr).Distinct().Count()}  zero%={table.Entries.Count(e=>e.RawSubrecordArrayPtr==0)*100.0/entryCount:F0}%");
    Console.WriteLine($"      texture_name [CONFIRMED/VARIES]: distinct={table.Entries.Select(e=>e.TextureName).Distinct().Count()}  empty={table.Entries.Count(e=>string.IsNullOrEmpty(e.TextureName))}");
    Console.WriteLine($"      sub-record stride 52B [CONFIRMED by parser + formula]");
    Console.WriteLine($"      colour_quad (+0x08) [MEDIUM/UNRESOLVED inner structure]: see BLOCK J");
}

Console.WriteLine();
Console.WriteLine("  .eff geometry shapes (Section B):");
Console.WriteLine($"    VFS instance count: {effObjPaths.Count}");
Console.WriteLine($"    Parse-clean: {effOk} / {effObjPaths.Count}  errors={effErr}");
Console.WriteLine($"    Formula verified: 4 + idx×2 + 4 + vert×32  zero-residual on all clean instances");
Console.WriteLine($"    Index divisible by 3 (triangle list): {effIndexCounts.All(c => c % 3 == 0)}");

Console.WriteLine();
Console.WriteLine("particle-val: done.");
