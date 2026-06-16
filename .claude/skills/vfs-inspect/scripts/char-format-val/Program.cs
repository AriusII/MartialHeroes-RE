// char-format-val — THROWAWAY harness.
// Validates .skn / .bnd / .mot / bindlist.txt layout hypotheses across all real VFS instances.
// NEVER committed. Run with: dotnet run -c Release --project <this dir>
//
// Observations: metadata and counts only. No geometry or payload bytes are printed.

using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Vfs;

// CP949 provider required for any text decode.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

const string InfPath = "D:/MartialHeroesClient/data.inf";
const string VfsPath = "D:/MartialHeroesClient/data/data.vfs";

Console.WriteLine("=== char-format-val: .skn / .bnd / .mot / bindlist.txt ===");
Console.WriteLine($"VFS: {VfsPath}");
Console.WriteLine();

using var archive = MappedVfsArchive.Open(InfPath, VfsPath);
var entries = archive.GetEntries();

// ─────────────────────────────────────────────────────────────────────────────
// 1. COUNT ENTRIES PER EXTENSION
// ─────────────────────────────────────────────────────────────────────────────

var sknPaths = new List<string>();
var bndPaths = new List<string>();
var motPaths = new List<string>();
string? bindlistPath = null;

foreach (var e in entries)
{
    string name = e.Name;
    if (name.EndsWith(".skn", StringComparison.Ordinal)) sknPaths.Add(name);
    else if (name.EndsWith(".bnd", StringComparison.Ordinal)) bndPaths.Add(name);
    else if (name.EndsWith(".mot", StringComparison.Ordinal)) motPaths.Add(name);
    else if (name == "data/char/bindlist.txt") bindlistPath = name;
}

Console.WriteLine($"Extension census:");
Console.WriteLine($"  .skn  : {sknPaths.Count}");
Console.WriteLine($"  .bnd  : {bndPaths.Count}");
Console.WriteLine($"  .mot  : {motPaths.Count}");
Console.WriteLine($"  bindlist.txt found: {bindlistPath != null}");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────────────────────
// 2. BINDLIST.TXT — entry count
// ─────────────────────────────────────────────────────────────────────────────

if (bindlistPath != null)
{
    var raw = archive.GetFileContent(bindlistPath);
    var parsed = BindlistParser.Parse(raw);
    Console.WriteLine($"=== bindlist.txt ===");
    Console.WriteLine($"  Entries : {parsed.Count}");
    Console.WriteLine($"  Names   : {string.Join(", ", parsed.Entries)}");
    Console.WriteLine();
}
else
{
    Console.WriteLine("WARNING: data/char/bindlist.txt not found in VFS.");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. .BND — parse all, verify 36B/bone stride, collect actor_id range
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine($"=== .bnd ({bndPaths.Count} files) ===");
{
    int ok = 0, failed = 0, residualNonZero = 0;
    int minActorId = int.MaxValue, maxActorId = int.MinValue;
    var actorIds = new SortedSet<int>();
    var boneCountDist = new SortedDictionary<int, int>();
    var failures = new List<string>();

    foreach (var path in bndPaths)
    {
        try
        {
            var raw = archive.GetFileContent(path);
            int fileSize = raw.Length;
            var skel = BndParser.Parse(raw);

            // Recompute expected size from what the parser consumed:
            // actor_id(4) + lenstr_prefix(4) + name_body(len) + bone_count(4) + bones(N×36)
            int nameLen = Encoding.ASCII.GetByteCount(skel.ActorName);
            int expectedSize = 4 + 4 + nameLen + 4 + skel.Bones.Length * 36;
            int residual = fileSize - expectedSize;

            if (residual != 0)
            {
                residualNonZero++;
                if (failures.Count < 10)
                    failures.Add($"  RESIDUAL {residual:+#;-#;0}B  {path}  (actorId={skel.ActorId} bones={skel.Bones.Length} size={fileSize})");
            }

            int aid = (int)skel.ActorId;
            actorIds.Add(aid);
            if (aid < minActorId) minActorId = aid;
            if (aid > maxActorId) maxActorId = aid;

            int bc = skel.Bones.Length;
            boneCountDist.TryGetValue(bc, out int bcc);
            boneCountDist[bc] = bcc + 1;

            ok++;
        }
        catch (Exception ex)
        {
            failed++;
            if (failures.Count < 10)
                failures.Add($"  PARSE ERROR  {path}  {ex.Message[..Math.Min(80, ex.Message.Length)]}");
        }
    }

    Console.WriteLine($"  Parsed OK         : {ok}");
    Console.WriteLine($"  Parse errors      : {failed}");
    Console.WriteLine($"  Residual != 0     : {residualNonZero}");
    Console.WriteLine($"  actor_id range    : {(ok > 0 ? $"{minActorId} .. {maxActorId}" : "N/A")}");
    Console.WriteLine($"  Distinct actor_ids: {actorIds.Count}");
    Console.WriteLine($"  Bone count distribution:");
    foreach (var kv in boneCountDist)
        Console.WriteLine($"    {kv.Key,3} bones : {kv.Value} file(s)");
    if (failures.Count > 0)
    {
        Console.WriteLine($"  First failures/residuals (up to 10):");
        foreach (var f in failures) Console.WriteLine(f);
    }
    Console.WriteLine($"  36-byte-per-bone stride CONFIRMED across all: {(residualNonZero == 0 && failed == 0 ? "YES" : "NO — see above")}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. .SKN — parse all, verify face/vertex/weight stride, collect id_b range
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine($"=== .skn ({sknPaths.Count} files) ===");
{
    int ok = 0, failed = 0, residualNonZero = 0;
    int minIdB = int.MaxValue, maxIdB = int.MinValue;
    var idBSet = new SortedSet<int>();
    var failures = new List<string>();

    foreach (var path in sknPaths)
    {
        try
        {
            var raw = archive.GetFileContent(path);
            int fileSize = raw.Length;
            var mesh = SknParser.Parse(raw);

            // Expected size:
            //   id_a(4) + id_b(4) + lenstr_prefix(4) + name_body + face_count(4) + faces*(36)
            //   + vertex_count(4) + vertices*(24) + weight_count(4) + weights*(12)
            int nameLen = Encoding.ASCII.GetByteCount(mesh.Name);
            int expectedSize = 4 + 4 + 4 + nameLen
                             + 4 + (int)mesh.FaceCount * 36
                             + 4 + mesh.Positions.Length * 24
                             + 4 + mesh.Weights.Length * 12;
            int residual = fileSize - expectedSize;

            if (residual != 0)
            {
                residualNonZero++;
                if (failures.Count < 10)
                    failures.Add($"  RESIDUAL {residual:+#;-#;0}B  {path}  (idA={mesh.IdA} idB={mesh.IdB} faces={mesh.FaceCount} verts={mesh.Positions.Length} wts={mesh.Weights.Length} size={fileSize})");
            }

            int idb = (int)mesh.IdB;
            idBSet.Add(idb);
            if (idb < minIdB) minIdB = idb;
            if (idb > maxIdB) maxIdB = idb;

            ok++;
        }
        catch (Exception ex)
        {
            failed++;
            if (failures.Count < 10)
                failures.Add($"  PARSE ERROR  {path}  {ex.Message[..Math.Min(80, ex.Message.Length)]}");
        }
    }

    Console.WriteLine($"  Parsed OK          : {ok}");
    Console.WriteLine($"  Parse errors       : {failed}");
    Console.WriteLine($"  Residual != 0      : {residualNonZero}");
    Console.WriteLine($"  id_b (IdB) range   : {(ok > 0 ? $"{minIdB} .. {maxIdB}" : "N/A")}");
    Console.WriteLine($"  Distinct id_b vals : {idBSet.Count}");
    Console.WriteLine($"  All id_b values    : {string.Join(", ", idBSet)}");
    if (failures.Count > 0)
    {
        Console.WriteLine($"  First failures/residuals (up to 10):");
        foreach (var f in failures) Console.WriteLine(f);
    }
    bool strideOk = residualNonZero == 0 && failed == 0;
    Console.WriteLine($"  face=36B / vert=24B / weight=12B stride CONFIRMED: {(strideOk ? "YES" : "NO — see above")}");
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. .MOT — parse all, verify 28B keyframe stride, report residuals
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine($"=== .mot ({motPaths.Count} files) ===");
{
    int ok = 0, failed = 0, residualNonZero = 0;
    var frameCountDist = new SortedDictionary<int, int>();  // frame_count bucket → file count
    var trackCountDist = new SortedDictionary<int, int>();  // track_count → file count
    var failures = new List<string>();

    // For checking stub files (track_count == 0 or frame_count == 0)
    int stubCount = 0;

    foreach (var path in motPaths)
    {
        try
        {
            var raw = archive.GetFileContent(path);
            int fileSize = raw.Length;
            var clip = AnimationParser.Parse(raw);

            // Expected size:
            //   id_a(4) + id_b(4) + lenstr_prefix(4) + name_body + frame_count(4) + track_count(4)
            //   + sum over tracks: (8 preamble + key_count × 28)
            int nameLen = Encoding.ASCII.GetByteCount(clip.Name);
            int expectedSize = 4 + 4 + 4 + nameLen + 4 + 4;
            foreach (var track in clip.Tracks)
                expectedSize += 8 + track.Keyframes.Length * 28;

            int residual = fileSize - expectedSize;

            if (residual != 0)
            {
                residualNonZero++;
                if (failures.Count < 10)
                    failures.Add($"  RESIDUAL {residual:+#;-#;0}B  {path}  (idA={clip.IdA} frames={clip.FrameCount} tracks={clip.Tracks.Length} size={fileSize})");
            }

            // Bucket frame_count (coarse — 10-frame buckets)
            int fBucket = (int)(clip.FrameCount / 10) * 10;
            frameCountDist.TryGetValue(fBucket, out int fc);
            frameCountDist[fBucket] = fc + 1;

            int tc = clip.Tracks.Length;
            trackCountDist.TryGetValue(tc, out int tcc);
            trackCountDist[tc] = tcc + 1;

            if (clip.FrameCount == 0 || tc == 0) stubCount++;

            ok++;
        }
        catch (Exception ex)
        {
            failed++;
            if (failures.Count < 10)
                failures.Add($"  PARSE ERROR  {path}  {ex.Message[..Math.Min(80, ex.Message.Length)]}");
        }
    }

    Console.WriteLine($"  Parsed OK           : {ok}");
    Console.WriteLine($"  Parse errors        : {failed}");
    Console.WriteLine($"  Residual != 0       : {residualNonZero}");
    Console.WriteLine($"  Stub files (0-frame/0-track): {stubCount}");
    Console.WriteLine($"  Frame count distribution (bucket × 10):");
    foreach (var kv in frameCountDist)
        Console.WriteLine($"    [{kv.Key,4}..{kv.Key+9,4}] frames : {kv.Value} file(s)");
    Console.WriteLine($"  Track count distribution:");
    foreach (var kv in trackCountDist)
        Console.WriteLine($"    {kv.Key,3} tracks : {kv.Value} file(s)");
    if (failures.Count > 0)
    {
        Console.WriteLine($"  First failures/residuals (up to 10):");
        foreach (var f in failures) Console.WriteLine(f);
    }
    bool strideOk = residualNonZero == 0 && failed == 0;
    Console.WriteLine($"  28B keyframe / 8B track-preamble stride CONFIRMED: {(strideOk ? "YES" : "NO — see above")}");
    Console.WriteLine();
}

Console.WriteLine("=== DONE ===");
