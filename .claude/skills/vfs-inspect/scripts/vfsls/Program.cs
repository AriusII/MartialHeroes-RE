// vfsls — throwaway VFS inspection harness for the Martial Heroes client.
//
// Mounts the real client archive (data.inf + data/data.vfs) through the production
// MartialHeroes.Assets.Vfs.MappedVfsArchive API and answers one-off questions: list entries by
// substring, count by extension, test for a path, peek at head bytes. Prints METADATA only
// (names / offsets / sizes) plus, on request, a short hex + CP949-decoded head preview.
//
// This is NOT a solution member. Run it in place:
//     dotnet run -c Release --project <this dir> -- <args>
//
// All client text is CP949 (Korean code page 949); the provider is registered below so that
// --head previews decode without mojibake.

using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

// CP949 lives in the CodePages provider, which is opt-in on modern .NET.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Encoding cp949 = Encoding.GetEncoding(949);

// --- Defaults: the bring-your-own client location. Override with --inf / --vfs. ---
// Probe order: MH_CLIENT_DIR env → the Godot project's embedded clientdata/ folder
// (found by walking up from the harness binary to the repo root) → D:/MartialHeroesClient.
string infPath = "D:/MartialHeroesClient/data.inf";
string vfsPath = "D:/MartialHeroesClient/data/data.vfs";

foreach (string root in DefaultClientRoots())
{
    string candidateInf = Path.Combine(root, "data.inf");
    string candidateVfs = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(candidateInf) && File.Exists(candidateVfs))
    {
        infPath = candidateInf;
        vfsPath = candidateVfs;
        break;
    }
}

// --- Parse arguments ---
if (args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) &&
    args[0] is "scan-mot" or "scan-bnd" or "scan-skn" or "scan-ui" or
               "dump-msgxdb" or "dump-uitex" or "scan-xeff" or "scan-sound" or "scan-fx" or
               "dump-do" or "scan-minimap" or "scan-quest")
{
    // Subcommand routing; handle --inf / --vfs overrides from the tail of args first.
    string subcmd = args[0];
    string[] subcmdArgs = args[1..];

    // Extract --inf / --vfs overrides from subcmd args.
    for (int i = 0; i < subcmdArgs.Length - 1; i++)
    {
        if (subcmdArgs[i] == "--inf") infPath = subcmdArgs[i + 1];
        else if (subcmdArgs[i] == "--vfs") vfsPath = subcmdArgs[i + 1];
    }

    if (!File.Exists(infPath) || !File.Exists(vfsPath))
    {
        Console.Error.WriteLine(
            $"vfsls: client files not found.\n  inf: {infPath} (exists={File.Exists(infPath)})\n" +
            $"  vfs: {vfsPath} (exists={File.Exists(vfsPath)})");
        return 2;
    }

    using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
    Console.WriteLine($"vfsls: mounted {archive.GetEntries().Length:N0} entries from {infPath}");

    return subcmd switch
    {
        "scan-mot"    => RunScanMot(archive, subcmdArgs, cp949),
        "scan-bnd"    => RunScanBnd(archive, subcmdArgs),
        "scan-skn"    => RunScanSkn(archive, subcmdArgs),
        "scan-ui"     => RunScanUi(archive, subcmdArgs),
        "dump-msgxdb" => RunDumpMsgXdb(archive, subcmdArgs, cp949),
        "dump-uitex"  => RunDumpUitex(archive, subcmdArgs, cp949),
        "scan-xeff"    => RunScanXeff(archive, subcmdArgs),
        "scan-sound"   => RunScanSound(archive, subcmdArgs),
        "scan-fx"      => RunScanFx(archive, subcmdArgs),
        "dump-do"      => RunDumpDo(archive, subcmdArgs, cp949),
        "scan-minimap" => RunScanMinimap(archive, subcmdArgs, cp949),
        "scan-quest"   => RunScanQuest(archive, subcmdArgs, cp949),
        _              => 2,
    };
}

// --- Existing generic commands below ---

var substrings = new List<string>();
var extensions = new List<string>();
bool countOnly = false;
bool census = false;
string? headPath = null;
int headBytes = 256;
string? containsPath = null;
int limit = 200;

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    switch (a)
    {
        case "--count":
            countOnly = true;
            break;
        case "--census":
            census = true;
            break;
        case "--ext":
            extensions.Add(NormExt(RequireValue(args, ref i, "--ext")));
            break;
        case "--head":
            headPath = NormPath(RequireValue(args, ref i, "--head"));
            break;
        case "--head-bytes":
            headBytes = ParseInt(RequireValue(args, ref i, "--head-bytes"), "--head-bytes");
            break;
        case "--contains":
            containsPath = NormPath(RequireValue(args, ref i, "--contains"));
            break;
        case "--limit":
            limit = ParseInt(RequireValue(args, ref i, "--limit"), "--limit");
            break;
        case "--inf":
            infPath = RequireValue(args, ref i, "--inf");
            break;
        case "--vfs":
            vfsPath = RequireValue(args, ref i, "--vfs");
            break;
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
        default:
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"vfsls: unknown option '{a}'. Use --help.");
                return 2;
            }
            substrings.Add(a.ToLowerInvariant());
            break;
    }
}

if (!File.Exists(infPath) || !File.Exists(vfsPath))
{
    Console.Error.WriteLine(
        $"vfsls: client files not found.\n  inf: {infPath} (exists={File.Exists(infPath)})\n" +
        $"  vfs: {vfsPath} (exists={File.Exists(vfsPath)})\n" +
        "Drop the bring-your-own client into 05.Presentation/MartialHeroes.Client.Godot/clientdata/ " +
        "(preferred), set MH_CLIENT_DIR, mount it at D:/MartialHeroesClient, or pass --inf/--vfs.");
    return 2;
}

using MappedVfsArchive archive2 = MappedVfsArchive.Open(infPath, vfsPath);
ReadOnlySpan<VfsEntry> entries = archive2.GetEntries();

Console.WriteLine($"vfsls: mounted {entries.Length:N0} entries from {infPath}");

// --- --contains: just yes/no for one path. ---
if (containsPath is not null)
{
    bool present = archive2.Contains(containsPath);
    Console.WriteLine($"contains(\"{containsPath}\") = {present.ToString().ToLowerInvariant()}");
    return present ? 0 : 1;
}

// --- --head: hex + CP949 preview of one entry's first N bytes. ---
if (headPath is not null)
{
    if (!archive2.Contains(headPath))
    {
        Console.Error.WriteLine($"vfsls: no entry '{headPath}'.");
        return 1;
    }

    ReadOnlyMemory<byte> content = archive2.GetFileContent(headPath);
    int n = Math.Min(headBytes, content.Length);
    ReadOnlySpan<byte> head = content.Span[..n];

    Console.WriteLine($"head of \"{headPath}\" ({content.Length:N0} bytes total, showing {n}):");
    Console.WriteLine();
    Console.WriteLine("-- hex --");
    PrintHexDump(head);
    Console.WriteLine();
    Console.WriteLine("-- CP949 decoded (control chars shown as '.') --");
    Console.WriteLine(DecodePreview(head, cp949));
    return 0;
}

// --- Build the filtered match list (AND of all substrings, OR within --ext set). ---
var matches = new List<VfsEntry>();
foreach (VfsEntry e in entries)
{
    if (!MatchesSubstrings(e.Name, substrings)) continue;
    if (extensions.Count > 0 && !MatchesAnyExt(e.Name, extensions)) continue;
    matches.Add(e);
}

// --- --census (or default with no filters): extension histogram. ---
if (census || (substrings.Count == 0 && extensions.Count == 0 && !countOnly))
{
    PrintCensus(matches);
    if (census) return 0;
    // Fall through with no further listing when run with zero args (summary already shown).
    Console.WriteLine();
    Console.WriteLine("Pass substrings, --ext, --head, --contains or --count to drill in. --help for all.");
    Console.WriteLine("Subcommands: scan-mot | scan-bnd | scan-skn | scan-ui | dump-msgxdb | dump-uitex | scan-xeff | scan-sound | scan-fx | dump-do | scan-minimap | scan-quest");
    return 0;
}

Console.WriteLine($"matched {matches.Count:N0} entr{(matches.Count == 1 ? "y" : "ies")}.");

if (countOnly)
    return matches.Count > 0 ? 0 : 1;

int shown = 0;
foreach (VfsEntry e in matches)
{
    if (limit > 0 && shown >= limit)
    {
        Console.WriteLine($"… ({matches.Count - shown:N0} more; raise --limit or 0 for all)");
        break;
    }

    Console.WriteLine($"  {e.Name,-64}  off={e.DataOffset,12:N0}  size={e.DataSize,11:N0}");
    shown++;
}

return matches.Count > 0 ? 0 : 1;

// ============================================================================
// SUBCOMMAND: scan-mot
// ============================================================================
// Census .mot animation files; reports stub vs real distribution, frame/track
// count distribution, BANI-variant detection.
// spec: Docs/RE/formats/animation.md
// ============================================================================

static int RunScanMot(MappedVfsArchive archive, string[] args, Encoding cp949)
{
    // Optional --id filter to show decoded details for one numeric id_a value.
    uint? idFilter = null;
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == "--id" && uint.TryParse(args[i + 1], out uint v))
            idFilter = v;

    // BANI magic: "BANI" as LE u32 = 0x494E4142.
    // spec: Docs/RE/formats/animation.md §BANI variant — magic "BANI" (42 41 4E 49): SAMPLE-VERIFIED.
    const uint BaniMagic = 0x494E4142;

    var motEntries = new List<VfsEntry>();
    foreach (VfsEntry e in archive.GetEntries())
        if (e.Name.EndsWith(".mot", StringComparison.Ordinal))
            motEntries.Add(e);

    int total = motEntries.Count;
    int realClips = 0, stubs = 0, baniVariant = 0, parseErrors = 0;

    // Histograms for distribution summary.
    var frameCountBuckets = new SortedDictionary<string, int>();
    var trackCountBuckets = new SortedDictionary<string, int>();

    foreach (VfsEntry e in motEntries)
    {
        ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
        if (raw.Length < 4) { parseErrors++; continue; }

        // Detect BANI variant.
        // spec: Docs/RE/formats/animation.md §BANI variant — sniff first 4 bytes: SAMPLE-VERIFIED.
        uint firstU32 = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]);
        if (firstU32 == BaniMagic)
        {
            baniVariant++;
            // BANI: anim_id at offset 8, unknown_field at 12 (always 7830), track_count confirmed 52.
            // spec: Docs/RE/formats/animation.md §BANI header layout: SAMPLE-VERIFIED.
            if (idFilter is not null)
            {
                if (raw.Length >= 16)
                {
                    uint version = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(4, 4));
                    uint animId = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(8, 4));
                    uint unknownField = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(12, 4));
                    Console.WriteLine($"  BANI {e.Name}: version={version} anim_id={animId} unknown_field={unknownField}");
                }
            }
            continue;
        }

        try
        {
            AnimationClip clip = AnimationParser.Parse(raw);

            if (idFilter is not null && clip.IdA == idFilter)
            {
                Console.WriteLine($"  {e.Name}:");
                Console.WriteLine($"    id_a={clip.IdA} id_b={clip.IdB} name=\"{clip.Name}\"");
                Console.WriteLine($"    frame_count={clip.FrameCount} track_count={clip.Tracks.Length}");
                Console.WriteLine($"    duration={clip.FrameCount * 0.1f:F1}s");
            }

            bool isReal = clip.FrameCount > 0 && clip.Tracks.Length > 0;
            if (isReal) realClips++;
            else stubs++;

            // Bucket frame counts: 0, 1-10, 11-50, 51-100, 101-500, 501+
            // spec: Docs/RE/formats/animation.md §Corpus census — frame count distribution.
            string fcBucket = clip.FrameCount switch
            {
                0 => "0 (stub)",
                <= 10 => "1-10",
                <= 50 => "11-50",
                <= 100 => "51-100",
                <= 500 => "101-500",
                _ => "501+",
            };
            frameCountBuckets.TryGetValue(fcBucket, out int fcN);
            frameCountBuckets[fcBucket] = fcN + 1;

            // Bucket track counts: 0, 1-10, 11-30, 31-50, 51+
            string tcBucket = clip.Tracks.Length switch
            {
                0 => "0 (stub)",
                <= 10 => "1-10",
                <= 30 => "11-30",
                <= 50 => "31-50",
                _ => "51+",
            };
            trackCountBuckets.TryGetValue(tcBucket, out int tcN);
            trackCountBuckets[tcBucket] = tcN + 1;
        }
        catch
        {
            parseErrors++;
        }
    }

    Console.WriteLine($"\nscan-mot: {total:N0} total .mot files");
    Console.WriteLine($"  real clips (frame_count>0 AND track_count>0): {realClips:N0}");
    Console.WriteLine($"  stubs (frame_count=0 or track_count=0):       {stubs:N0}");
    Console.WriteLine($"  BANI-magic variant (dead, unloadable):         {baniVariant:N0}");
    Console.WriteLine($"  parse errors:                                  {parseErrors:N0}");

    Console.WriteLine("\n  frame_count distribution (standard variant):");
    foreach ((string bucket, int cnt) in frameCountBuckets)
        Console.WriteLine($"    {bucket,-12}  {cnt,6:N0}  ({cnt * 100.0 / Math.Max(total - baniVariant, 1):F1}%)");

    Console.WriteLine("\n  track_count distribution (standard variant):");
    foreach ((string bucket, int cnt) in trackCountBuckets)
        Console.WriteLine($"    {bucket,-12}  {cnt,6:N0}  ({cnt * 100.0 / Math.Max(total - baniVariant, 1):F1}%)");

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-bnd
// ============================================================================
// Census .bnd skeleton files: bone counts, base ids, hierarchy depth summary.
// spec: Docs/RE/formats/mesh.md §Format: .bnd
// ============================================================================

static int RunScanBnd(MappedVfsArchive archive, string[] args)
{
    var bndEntries = new List<VfsEntry>();
    foreach (VfsEntry e in archive.GetEntries())
        if (e.Name.EndsWith(".bnd", StringComparison.Ordinal))
            bndEntries.Add(e);

    int total = bndEntries.Count;
    int singleBone = 0, multiBone = 0, parseErrors = 0;
    int minBones = int.MaxValue, maxBones = 0;
    var boneCountBuckets = new SortedDictionary<string, int>();
    // baseId = minimum self_id low byte across all bones in a skeleton.
    int baseIdZero = 0, baseIdNonZero = 0;

    foreach (VfsEntry e in bndEntries)
    {
        try
        {
            ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
            Skeleton skel = BndParser.Parse(raw);

            int bc = skel.Bones.Length;
            if (bc <= 1) singleBone++;
            else multiBone++;

            minBones = Math.Min(minBones, bc);
            maxBones = Math.Max(maxBones, bc);

            // Bone-count buckets.
            string bucket = bc switch
            {
                1 => "1 (root-only)",
                <= 10 => "2-10",
                <= 30 => "11-30",
                <= 60 => "31-60",
                <= 90 => "61-90",
                _ => "91+",
            };
            boneCountBuckets.TryGetValue(bucket, out int bn);
            boneCountBuckets[bucket] = bn + 1;

            // Base id: low byte of the minimum self_id.
            // spec: Docs/RE/formats/mesh.md §Bone addressing — base_id = first bone's self_id: CONFIRMED.
            if (bc > 0)
            {
                byte baseId = (byte)(skel.Bones[0].SelfId & 0xFF);
                if (baseId == 0) baseIdZero++;
                else baseIdNonZero++;
            }
        }
        catch
        {
            parseErrors++;
        }
    }

    Console.WriteLine($"\nscan-bnd: {total:N0} total .bnd files");
    Console.WriteLine($"  single-bone (root-only): {singleBone:N0}");
    Console.WriteLine($"  multi-bone:              {multiBone:N0}");
    Console.WriteLine($"  parse errors:            {parseErrors:N0}");
    if (total - parseErrors > 0)
        Console.WriteLine($"  bone count range:        {minBones}–{maxBones}");

    Console.WriteLine("\n  bone count distribution:");
    foreach ((string bucket, int cnt) in boneCountBuckets)
        Console.WriteLine($"    {bucket,-16}  {cnt,5:N0}");

    Console.WriteLine($"\n  base_id == 0 (first bone self_id low byte): {baseIdZero:N0}");
    Console.WriteLine($"  base_id != 0:                                {baseIdNonZero:N0}");

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-skn
// ============================================================================
// Census .skn skinned meshes: IdA/IdB pairs, vertex/face counts, weights presence.
// spec: Docs/RE/formats/mesh.md §Format: .skn
// ============================================================================

static int RunScanSkn(MappedVfsArchive archive, string[] args)
{
    var sknEntries = new List<VfsEntry>();
    foreach (VfsEntry e in archive.GetEntries())
        if (e.Name.EndsWith(".skn", StringComparison.Ordinal))
            sknEntries.Add(e);

    int total = sknEntries.Count;
    int rigidNoSkel = 0, hasSkel = 0, multiWeightSkin = 0, parseErrors = 0;
    long totalVerts = 0, totalFaces = 0;
    int maxWeightsPerVert = 0;

    // Distinct id_b values → skeleton count.
    var distinctIdB = new HashSet<uint>();

    foreach (VfsEntry e in sknEntries)
    {
        try
        {
            ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
            SkinnedMesh mesh = SknParser.Parse(raw);

            // spec: Docs/RE/formats/mesh.md §id_b — 0 = rigid/no skeleton, non-zero = binds to skeleton: CONFIRMED.
            if (mesh.IdB == 0) rigidNoSkel++;
            else
            {
                hasSkel++;
                distinctIdB.Add(mesh.IdB);
            }

            totalVerts += mesh.Positions.Length;
            totalFaces += mesh.FaceCount;

            // weight_count vs vertex_count ratio.
            // spec: Docs/RE/formats/mesh.md §Multi-bone weighted skinning: weight_count > vertex_count
            //       for character skins; SAMPLE-VERIFIED.
            if (mesh.Positions.Length > 0 && mesh.Weights.Length > mesh.Positions.Length)
                multiWeightSkin++;

            // Estimate max influences per vertex.
            if (mesh.Positions.Length > 0)
            {
                double ratio = (double)mesh.Weights.Length / mesh.Positions.Length;
                int est = (int)Math.Ceiling(ratio);
                if (est > maxWeightsPerVert) maxWeightsPerVert = est;
            }
        }
        catch
        {
            parseErrors++;
        }
    }

    int parsed = total - parseErrors;
    Console.WriteLine($"\nscan-skn: {total:N0} total .skn files");
    Console.WriteLine($"  rigid (id_b=0, no skeleton):              {rigidNoSkel:N0}");
    Console.WriteLine($"  with skeleton (id_b!=0):                  {hasSkel:N0}");
    Console.WriteLine($"  multi-weight skins (weights>verts):       {multiWeightSkin:N0}");
    Console.WriteLine($"  parse errors:                             {parseErrors:N0}");
    Console.WriteLine($"  distinct skeleton id_b values:            {distinctIdB.Count:N0}");
    if (parsed > 0)
    {
        Console.WriteLine($"  total vertices (parsed):                  {totalVerts:N0}");
        Console.WriteLine($"  total faces (parsed):                     {totalFaces:N0}");
        Console.WriteLine($"  max est. weights/vertex:                  {maxWeightsPerVert}");
    }

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-ui
// ============================================================================
// Census data/ui/ DDS files: dimensions + format from DDS header (fourCC/size),
// grouped by subdirectory; flag non-power-of-2.
// spec: Docs/RE/formats/texture.md §DDS §DDS_HEADER layout
// spec: Docs/RE/formats/ui_manifests.md §6 DDS atlas physical format summary
// ============================================================================

static int RunScanUi(MappedVfsArchive archive, string[] args)
{
    // DDS header layout (128 bytes total: 4 magic + 124 DDS_HEADER).
    // spec: Docs/RE/formats/texture.md §DDS_HEADER layout — all fields little-endian u32.
    const int DdsMagicOff = 0;        // "DDS " = 44 44 53 20
    const int DdsHeightOff = 12;      // DDS_HEADER.dwHeight
    const int DdsWidthOff = 16;       // DDS_HEADER.dwWidth
    const int DdsPfFourCcOff = 0x54;  // DDS_PIXELFORMAT.dwFourCC
    const int DdsMinHeaderSize = 0x58; // enough to read fourCC

    // Magic: "DDS " = 0x20534444 LE.
    // spec: Docs/RE/formats/texture.md §DDS — "Magic bytes 44 44 53 20": SAMPLE-VERIFIED.
    const uint DdsMagic = 0x20534444;

    var byDir = new SortedDictionary<string, List<(string name, uint w, uint h, string fourcc, bool npot)>>(StringComparer.Ordinal);
    int totalDds = 0, totalNpot = 0, nonDds = 0;

    foreach (VfsEntry e in archive.GetEntries())
    {
        if (!e.Name.StartsWith("data/ui/", StringComparison.Ordinal)) continue;
        if (!e.Name.EndsWith(".dds", StringComparison.Ordinal)) continue;

        ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
        if (raw.Length < DdsMinHeaderSize) { nonDds++; continue; }

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(DdsMagicOff, 4));
        if (magic != DdsMagic) { nonDds++; continue; }

        uint h = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(DdsHeightOff, 4));
        uint w = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(DdsWidthOff, 4));
        uint fourccRaw = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(DdsPfFourCcOff, 4));

        // Decode fourCC as ASCII, falling back to hex.
        // spec: Docs/RE/formats/ui_manifests.md §6 — DXT1/DXT3 fourCC values confirmed.
        string fourcc = fourccRaw == 0
            ? "RAW"
            : Encoding.ASCII.GetString(BitConverter.GetBytes(fourccRaw)).TrimEnd('\0');

        // Non-power-of-2 check.
        bool npot = !IsPowerOf2(w) || !IsPowerOf2(h);
        if (npot) totalNpot++;
        totalDds++;

        // Group by directory (everything between "data/ui/" and the filename).
        string subdir = "data/ui/";
        int lastSlash = e.Name.LastIndexOf('/');
        if (lastSlash > 8) subdir = e.Name[..lastSlash];

        if (!byDir.TryGetValue(subdir, out var list))
            byDir[subdir] = list = [];

        // Store just the bare filename for readability.
        string fname = e.Name[(lastSlash + 1)..];
        list.Add((fname, w, h, fourcc, npot));
    }

    Console.WriteLine($"\nscan-ui: {totalDds:N0} DDS files under data/ui/");
    Console.WriteLine($"  non-DDS (magic mismatch or too short): {nonDds:N0}");
    Console.WriteLine($"  non-power-of-2 (NPOT) textures:       {totalNpot:N0}");

    foreach ((string dir, var items) in byDir)
    {
        Console.WriteLine($"\n  [{dir}]  ({items.Count} files)");
        foreach ((string name, uint w, uint h, string fourcc, bool npot) in items)
        {
            string npotMark = npot ? " *** NPOT ***" : "";
            Console.WriteLine($"    {name,-48}  {w,4}x{h,-4}  {fourcc}{npotMark}");
        }
    }

    return 0;
}

// ============================================================================
// SUBCOMMAND: dump-msgxdb
// ============================================================================
// Dump msg.xdb records (u32 id + CP949 string, 516B stride).
// Options: --id N (one record), --range A B (inclusive range).
// spec: Docs/RE/formats/misc_data.md §6 msg.xdb
// ============================================================================

static int RunDumpMsgXdb(MappedVfsArchive archive, string[] args, Encoding cp949)
{
    // spec: Docs/RE/formats/misc_data.md §6 — logical path: data/script/msg.xdb: CODE-CONFIRMED.
    const string MsgXdbPath = "data/script/msg.xdb";

    uint? filterId = null;
    uint? rangeA = null, rangeB = null;

    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == "--id" && uint.TryParse(args[i + 1], out uint v))
            filterId = v;
        else if (args[i] == "--range" && i + 2 < args.Length &&
                 uint.TryParse(args[i + 1], out uint ra) && uint.TryParse(args[i + 2], out uint rb))
        {
            rangeA = ra; rangeB = rb;
        }
    }

    if (!archive.Contains(MsgXdbPath))
    {
        Console.Error.WriteLine($"dump-msgxdb: '{MsgXdbPath}' not found in VFS.");
        return 1;
    }

    ReadOnlyMemory<byte> raw = archive.GetFileContent(MsgXdbPath);
    MsgXdbCatalog catalog = MsgXdbParser.Parse(raw);

    Console.WriteLine($"\ndump-msgxdb: {catalog.Records.Count:N0} records ({raw.Length:N0} bytes, stride 516)");
    Console.WriteLine($"  source: {MsgXdbPath}");
    Console.WriteLine();

    int shown = 0;
    foreach (MsgXdbRecord rec in catalog.Records)
    {
        if (filterId.HasValue && rec.Id != filterId.Value) continue;
        if (rangeA.HasValue && (rec.Id < rangeA.Value || rec.Id > rangeB!.Value)) continue;

        // Escape CR/LF in text to keep output single-line.
        // spec: Docs/RE/formats/misc_data.md §6 Text encoding — CP949 NUL-terminated: CODE-CONFIRMED.
        string text = rec.Text.Replace("\r", "\\r", StringComparison.Ordinal)
                               .Replace("\n", "\\n", StringComparison.Ordinal);
        Console.WriteLine($"  {rec.Id,8}  {text}");
        shown++;
    }

    if (shown == 0 && (filterId.HasValue || rangeA.HasValue))
        Console.WriteLine("  (no records matched the filter)");
    else if (!filterId.HasValue && !rangeA.HasValue)
        Console.WriteLine($"\n  ({shown:N0} records printed)");

    return 0;
}

// ============================================================================
// SUBCOMMAND: dump-uitex
// ============================================================================
// Parse and print UiTex.txt manifest (tex_id → vfs_path), tolerant of the
// known malformed entry (id 0029, missing closing quote).
// spec: Docs/RE/formats/ui_manifests.md §1 data/ui/UiTex.txt
// ============================================================================

static int RunDumpUitex(MappedVfsArchive archive, string[] args, Encoding cp949)
{
    // spec: Docs/RE/formats/ui_manifests.md §1.1 — logical path: data/ui/UiTex.txt: PARSER-CONFIRMED.
    const string UiTexPath = "data/ui/uitex.txt";

    if (!archive.Contains(UiTexPath))
    {
        // Try mixed-case variant.
        Console.Error.WriteLine($"dump-uitex: '{UiTexPath}' not found in VFS.");
        return 1;
    }

    ReadOnlyMemory<byte> raw = archive.GetFileContent(UiTexPath);
    UiTexManifest manifest = UiTexManifestParser.Parse(raw.Span);

    Console.WriteLine($"\ndump-uitex: {manifest.Count:N0} entries from {UiTexPath}");
    Console.WriteLine($"  DDS entries: {manifest.DdsEntries.Count}  MSK entries: {manifest.MskEntries.Count}");
    Console.WriteLine();
    Console.WriteLine($"  {"tex_id",-8}  {"block",-4}  path");
    Console.WriteLine($"  {"------",-8}  {"-----",-4}  ----");

    foreach (UiTexEntry entry in manifest.DdsEntries.Concat(manifest.MskEntries))
    {
        // Check whether the referenced path actually exists in the VFS.
        string vfsLookup = entry.VfsPath.ToLowerInvariant().Replace('\\', '/');
        bool exists = archive.Contains(vfsLookup);
        string mark = exists ? "" : "  [NOT IN VFS]";
        string block = entry.BlockKind == UiTexBlockKind.Dds ? "DDS" : "MSK";
        Console.WriteLine($"  {entry.TexId,8}  {block,-4}  {entry.VfsPath}{mark}");
    }

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-xeff
// ============================================================================
// Census .xeff particle effect files: count, header effect_id, element_count
// distribution, file size distribution, 9-digit skill-code pattern.
// spec: Docs/RE/formats/effects.md §Section A
// ============================================================================

static int RunScanXeff(MappedVfsArchive archive, string[] args)
{
    // 9-digit pattern: [0-9]{9}  — matches the numeric filename component.
    // spec: Docs/RE/formats/effects.md §A.2 effect_id — "numeric, matches decimal portion of filename".
    var nineDigitRx = new Regex(@"^\d{9}$", RegexOptions.Compiled);

    // Anti-magic: 0x46464558 = "XEFF" in LE.
    // spec: Docs/RE/formats/effects.md §A.1 Anti-magic 0x46464558: CONFIRMED.
    const uint XeffInvalidMagic = 0x46464558;

    var xeffEntries = new List<VfsEntry>();
    foreach (VfsEntry e in archive.GetEntries())
        if (e.Name.EndsWith(".xeff", StringComparison.Ordinal))
            xeffEntries.Add(e);

    int total = xeffEntries.Count;
    int emptyEffects = 0, nonEmptyEffects = 0, antiMagicHits = 0, parseErrors = 0;
    int nineDigitCount = 0;
    var sizeBuckets = new SortedDictionary<string, int>();
    // element_count distribution.
    var elemCountBuckets = new SortedDictionary<string, int>();
    // effect_id duplicate tracking.
    var seenIds = new Dictionary<uint, int>();

    foreach (VfsEntry e in xeffEntries)
    {
        // 9-digit filename stem check.
        string stem = Path.GetFileNameWithoutExtension(e.Name);
        if (nineDigitRx.IsMatch(stem)) nineDigitCount++;

        // File size buckets.
        string sizeBucket = e.DataSize switch
        {
            <= 8 => "≤8 (header-only/stub)",
            <= 100 => "9-100",
            <= 1000 => "101-1000",
            <= 10000 => "1001-10000",
            _ => "10001+",
        };
        sizeBuckets.TryGetValue(sizeBucket, out int sbn);
        sizeBuckets[sizeBucket] = sbn + 1;

        ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
        if (raw.Length < 8) { parseErrors++; continue; }

        uint effectId = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span[..4]);
        uint elementCount = BinaryPrimitives.ReadUInt32LittleEndian(raw.Span.Slice(4, 4));

        // Anti-magic check.
        // spec: Docs/RE/formats/effects.md §A.1 — "effect_id == 0x46464558 → invalid": CONFIRMED.
        if (effectId == XeffInvalidMagic) { antiMagicHits++; continue; }

        // Duplicate effect_id tracking.
        seenIds.TryGetValue(effectId, out int idCount);
        seenIds[effectId] = idCount + 1;

        if (elementCount == 0) emptyEffects++;
        else nonEmptyEffects++;

        string elemBucket = elementCount switch
        {
            0 => "0 (stub/empty)",
            1 => "1",
            <= 5 => "2-5",
            <= 10 => "6-10",
            _ => "11+",
        };
        elemCountBuckets.TryGetValue(elemBucket, out int ebn);
        elemCountBuckets[elemBucket] = ebn + 1;
    }

    int duplicateIds = seenIds.Values.Count(c => c > 1);

    Console.WriteLine($"\nscan-xeff: {total:N0} total .xeff files");
    Console.WriteLine($"  stub/empty effects (element_count=0):   {emptyEffects:N0}");
    Console.WriteLine($"  non-empty effects (element_count>0):    {nonEmptyEffects:N0}");
    Console.WriteLine($"  anti-magic sentinel hits (invalid):     {antiMagicHits:N0}");
    Console.WriteLine($"  parse errors (file too short):          {parseErrors:N0}");
    Console.WriteLine($"  9-digit numeric filename pattern:       {nineDigitCount:N0} / {total:N0}");
    Console.WriteLine($"  distinct effect_id values:              {seenIds.Count:N0}");
    Console.WriteLine($"  effect_id values with duplicates:       {duplicateIds:N0}");

    Console.WriteLine("\n  element_count distribution:");
    foreach ((string b, int cnt) in elemCountBuckets)
        Console.WriteLine($"    {b,-22}  {cnt,5:N0}");

    Console.WriteLine("\n  file size distribution (bytes):");
    foreach ((string b, int cnt) in sizeBuckets)
        Console.WriteLine($"    {b,-28}  {cnt,5:N0}");

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-sound
// ============================================================================
// Census .ogg under data/sound/2d|3d + parse the 48-byte-stride per-area
// sound tables (.bgm/.bge/.eff/.wlk/.run) listing non-null entries.
// spec: Docs/RE/formats/sound_tables.md
// ============================================================================

static int RunScanSound(MappedVfsArchive archive, string[] args)
{
    // Fixed stride for sound table entries: 48 bytes.
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — "Entry stride: 48 bytes. Confirmed."
    const int SoundTableEntryStride = 48;
    // Fixed number of entries per file.
    // spec: Docs/RE/formats/sound_tables.md §Entry count — "Fixed: 256 entries."
    const int SoundTableEntryCount = 256;
    // Loader reads exactly 256 × 48 = 12288 bytes.
    // spec: Docs/RE/formats/sound_tables.md §Overall structure — "runtime loader reads exactly 12288 bytes."
    const int SoundTableDataSize = SoundTableEntryCount * SoundTableEntryStride;

    // sound_entry_id at +0x00 (u32 LE), 0 = null/disabled.
    // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — sound_entry_id @ +0x00: CONFIRMED.
    const int IdOffset = 0;

    // --- Census .ogg files under data/sound/2d and data/sound/3d ---
    int ogg2d = 0, ogg3d = 0, otherSound = 0;
    foreach (VfsEntry e in archive.GetEntries())
    {
        if (e.Name.StartsWith("data/sound/2d/", StringComparison.Ordinal))
        {
            if (e.Name.EndsWith(".ogg", StringComparison.Ordinal)) ogg2d++;
            else otherSound++;
        }
        else if (e.Name.StartsWith("data/sound/3d/", StringComparison.Ordinal))
        {
            if (e.Name.EndsWith(".ogg", StringComparison.Ordinal)) ogg3d++;
            else otherSound++;
        }
    }

    Console.WriteLine($"\nscan-sound: .ogg audio files");
    Console.WriteLine($"  data/sound/2d/*.ogg:  {ogg2d:N0}");
    Console.WriteLine($"  data/sound/3d/*.ogg:  {ogg3d:N0}");
    Console.WriteLine($"  other in data/sound/: {otherSound:N0}");

    // --- Parse per-area sound tables (.bgm / .bge / .wlk / .run / .eff sound variant) ---
    // The .eff variant lives under data/map*/soundtable*.eff — NEVER under data/effect/obj/
    // spec: Docs/RE/formats/sound_tables.md §CRITICAL DISAMBIGUATION — .eff path disambiguation.
    var soundTableExts = new HashSet<string>(StringComparer.Ordinal) { ".bgm", ".bge", ".wlk", ".run" };
    var tableEntries = new List<VfsEntry>();
    foreach (VfsEntry e in archive.GetEntries())
    {
        // data/map*/soundtable*.ext
        if (!e.Name.StartsWith("data/map", StringComparison.Ordinal)) continue;
        string ext = ExtOf(e.Name);
        // Also match .eff only under soundtable* naming pattern.
        // spec: Docs/RE/formats/sound_tables.md §Identification — "data/map*/soundtable*.eff": CONFIRMED.
        bool isEff = e.Name.Contains("/soundtable", StringComparison.Ordinal) &&
                     ext == ".eff";
        if (soundTableExts.Contains(ext) || isEff)
            tableEntries.Add(e);
    }

    Console.WriteLine($"\n  sound table files (.bgm/.bge/.wlk/.run/.eff): {tableEntries.Count:N0}");
    Console.WriteLine();

    // Group by extension for summary.
    var tableByExt = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (VfsEntry e in tableEntries)
    {
        string ext = ExtOf(e.Name);
        tableByExt.TryGetValue(ext, out int n);
        tableByExt[ext] = n + 1;
    }
    foreach ((string ext, int cnt) in tableByExt)
        Console.WriteLine($"    {ext,-8}  {cnt,4:N0} files");

    // Parse each table and list non-null entries.
    Console.WriteLine("\n  Non-null sound entries (sound_entry_id != 0):");
    Console.WriteLine($"  {"file",-48}  {"idx",4}  {"sound_id",12}  exists?");

    foreach (VfsEntry e in tableEntries)
    {
        ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
        if (raw.Length < SoundTableDataSize) continue;

        ReadOnlySpan<byte> tableBytes = raw.Span[..SoundTableDataSize];

        for (int idx = 0; idx < SoundTableEntryCount; idx++)
        {
            // sound_entry_id u32 LE @ entry+0x00.
            // spec: Docs/RE/formats/sound_tables.md §Per-entry layout — sound_entry_id @ +0x00: CONFIRMED.
            uint soundId = BinaryPrimitives.ReadUInt32LittleEndian(
                tableBytes.Slice(idx * SoundTableEntryStride + IdOffset, 4));

            if (soundId == 0) continue;

            // Build the expected VFS path: data/sound/3d/<sound_id>.ogg
            // spec: Docs/RE/formats/sound_tables.md §Sound ID semantics:
            //   "data/sound/3d/<sound_entry_id>.ogg" — CONFIRMED path construction.
            string expectedPath3d = $"data/sound/3d/{soundId}.ogg";
            string expectedPath2d = $"data/sound/2d/{soundId}.ogg";
            bool exists3d = archive.Contains(expectedPath3d);
            bool exists2d = archive.Contains(expectedPath2d);
            string existsMark = exists3d ? "3d-ok" : (exists2d ? "2d-ok" : "MISSING");

            Console.WriteLine($"  {e.Name,-48}  {idx,4}  {soundId,12}  {existsMark}");
        }
    }

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-fx
// ============================================================================
// Census all .fx1–.fx7 terrain layer files: count per layer, file-size
// distribution, and — critically — header offset 0x0C (field[3]) value
// histogram across all files of each layer. This resolves the FX2 field[3]
// conflict documented in effects.md (IDA says 15; sample observation says 50).
// spec: Docs/RE/formats/terrain_layers.md §Section 1
// ============================================================================

static int RunScanFx(MappedVfsArchive archive, string[] args)
{
    // FX layer extensions and their nominal header size in bytes.
    // spec: Docs/RE/formats/terrain_layers.md §1.5 FX1 Header = 24 bytes: CONFIRMED.
    // spec: Docs/RE/formats/terrain_layers.md §1.6 FX2 Header = 24 bytes: CONFIRMED.
    // spec: Docs/RE/formats/terrain_layers.md §1.7 FX3 Header = 48 bytes: CONFIRMED.
    // spec: Docs/RE/formats/terrain_layers.md §1.8 FX5 Header = 40+12 bytes per section: CONFIRMED.
    // FX4/FX6/FX7 header sizes: PLAUSIBLE from pattern observation.
    var layerExts = new[] { ".fx1", ".fx2", ".fx3", ".fx4", ".fx5", ".fx6", ".fx7" };

    // Per-layer accumulators: count, size total, field[3] histogram (at header offset 0x0C)
    var counts     = new Dictionary<string, int>(StringComparer.Ordinal);
    var sizeTotals = new Dictionary<string, long>(StringComparer.Ordinal);
    // field3 value → count, per layer
    var field3Histo = new Dictionary<string, SortedDictionary<uint, int>>(StringComparer.Ordinal);
    // Also track field[0] (type_tag) histogram per layer
    var field0Histo = new Dictionary<string, SortedDictionary<uint, int>>(StringComparer.Ordinal);
    // Track field[4] (mesh_count equivalent for FX1/FX2) for size formula verification
    var field4Histo = new Dictionary<string, SortedDictionary<uint, int>>(StringComparer.Ordinal);
    // Track field[5] (index_count equivalent for FX1/FX2) for size formula verification
    var field5Histo = new Dictionary<string, SortedDictionary<uint, int>>(StringComparer.Ordinal);
    int parseErrors = 0;

    foreach (string ext in layerExts)
    {
        counts[ext]     = 0;
        sizeTotals[ext] = 0;
        field3Histo[ext] = new SortedDictionary<uint, int>();
        field0Histo[ext] = new SortedDictionary<uint, int>();
        field4Histo[ext] = new SortedDictionary<uint, int>();
        field5Histo[ext] = new SortedDictionary<uint, int>();
    }

    foreach (VfsEntry e in archive.GetEntries())
    {
        string ext = ExtOf(e.Name);
        if (!counts.ContainsKey(ext)) continue;

        counts[ext]++;
        sizeTotals[ext] += e.DataSize;

        // Need at least 24 bytes to read the first 6 uint32 fields (6 × 4 = 24).
        ReadOnlyMemory<byte> raw = archive.GetFileContent(e.Name);
        if (raw.Length < 24) { parseErrors++; continue; }

        ReadOnlySpan<byte> hdr = raw.Span[..24];

        // field[0] at +0x00 (type_tag): u32 LE
        // spec: Docs/RE/formats/terrain_layers.md §1.5 — type_tag @ 0x00: CONFIRMED constant=1 in FX1.
        uint f0 = BinaryPrimitives.ReadUInt32LittleEndian(hdr[0..4]);
        // field[3] at +0x0C (render_state / contested field): u32 LE
        // spec: Docs/RE/formats/terrain_layers.md §1.6 — render_state @ 0x0C value=15 for FX2: UNVERIFIED.
        // hypothesis: June 2026 sample saw 50 for FX2; this scan will arbitrate.
        uint f3 = BinaryPrimitives.ReadUInt32LittleEndian(hdr[12..16]);
        // field[4] at +0x10 (mesh_count in FX1/FX2): u32 LE
        // spec: Docs/RE/formats/terrain_layers.md §1.5 — mesh_count @ 0x10: CONFIRMED.
        uint f4 = BinaryPrimitives.ReadUInt32LittleEndian(hdr[16..20]);
        // field[5] at +0x14 (index_count in FX1/FX2): u32 LE
        // spec: Docs/RE/formats/terrain_layers.md §1.5 — index_count @ 0x14: CONFIRMED.
        uint f5 = BinaryPrimitives.ReadUInt32LittleEndian(hdr[20..24]);

        field0Histo[ext].TryGetValue(f0, out int f0n); field0Histo[ext][f0] = f0n + 1;
        field3Histo[ext].TryGetValue(f3, out int f3n); field3Histo[ext][f3] = f3n + 1;
        field4Histo[ext].TryGetValue(f4, out int f4n); field4Histo[ext][f4] = f4n + 1;
        field5Histo[ext].TryGetValue(f5, out int f5n); field5Histo[ext][f5] = f5n + 1;
    }

    Console.WriteLine($"\nscan-fx: terrain layer file census");
    Console.WriteLine($"  parse errors: {parseErrors:N0}");
    Console.WriteLine();

    foreach (string ext in layerExts)
    {
        int cnt = counts[ext];
        Console.WriteLine($"  {ext}: {cnt:N0} files, {sizeTotals[ext]:N0} bytes total");
        if (cnt == 0) continue;
        Console.WriteLine($"    field[0] (type_tag @ 0x00) distinct values:");
        foreach ((uint v, int n) in field0Histo[ext])
            Console.WriteLine($"      {v,6} (0x{v:X4}) × {n,5:N0}");
        Console.WriteLine($"    field[3] (@ 0x0C) distinct values:");
        foreach ((uint v, int n) in field3Histo[ext])
            Console.WriteLine($"      {v,6} (0x{v:X4}) × {n,5:N0}");
        Console.WriteLine($"    field[4] (@ 0x10) distinct values (top 10):");
        int shown4 = 0;
        foreach ((uint v, int n) in field4Histo[ext].OrderByDescending(p => p.Value))
        {
            Console.WriteLine($"      {v,6} (0x{v:X4}) × {n,5:N0}");
            if (++shown4 >= 10) break;
        }
        Console.WriteLine($"    field[5] (@ 0x14) distinct values (top 10):");
        int shown5 = 0;
        foreach ((uint v, int n) in field5Histo[ext].OrderByDescending(p => p.Value))
        {
            Console.WriteLine($"      {v,6} (0x{v:X4}) × {n,5:N0}");
            if (++shown5 >= 10) break;
        }
        Console.WriteLine();
    }

    return 0;
}

// ============================================================================
// SUBCOMMAND: dump-do
// ============================================================================
// Census the 12 per-class stance .do files (icon/skill sprite-sheet records).
// Reports per-file record count + a compact field census across all records:
// the known header fields (instanceKey/groupSub/slotIndex/classStanceRef/groupId,
// iconSrcX @+0x18, iconSrcY @+0x1C) plus per-u32-offset distinct/min/max/zero%.
// Prints COUNTS and short decoded structural fields only — NO raw byte dumps.
// spec: Docs/RE/formats/ui_manifests.md §2.7
// ============================================================================

static int RunDumpDo(MappedVfsArchive archive, string[] args, Encoding cp949)
{
    // Record stride = 116 bytes (0x74). Ground truth: 12 per-class stance files,
    // each a flat array of 116-byte records.
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — stride 0x74: SAMPLE-VERIFIED.
    const int Stride = 0x74;
    // Icon sprite-sheet geometry: 23-px cells on a 512×512 sheet → coords in [0..489].
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — cell 23, sheet 512, max coord 489.
    const int Cell = 23, Sheet = 512, MaxCoord = Sheet - Cell;
    const int NumU32 = Stride / 4; // 29 dword-aligned offsets (0x00..0x70)

    // The 12 per-class stance files (jung/sa/ma × musa/assasin/wizard/monk).
    // spec: Docs/RE/formats/ui_manifests.md §2.7 — class-stance .do file set: SAMPLE-VERIFIED.
    string[] doFiles =
    {
        "data/script/musajung.do",   "data/script/musasa.do",     "data/script/musama.do",
        "data/script/assasinjung.do","data/script/assasinsa.do",  "data/script/assasinma.do",
        "data/script/wizardjung.do", "data/script/wizardsa.do",   "data/script/wizardma.do",
        "data/script/monkjung.do",   "data/script/monksa.do",     "data/script/monkma.do",
    };

    // Per-u32-offset accumulators across ALL records of ALL files.
    var distinctU32 = new HashSet<uint>[NumU32];
    var minU32 = new uint[NumU32];
    var maxU32 = new uint[NumU32];
    var zeroU32 = new long[NumU32];
    for (int d = 0; d < NumU32; d++) { distinctU32[d] = new HashSet<uint>(); minU32[d] = uint.MaxValue; }

    int totalFiles = 0, totalRecords = 0, totalValidIcon = 0, missing = 0;

    Console.WriteLine($"\ndump-do: per-class stance .do census (stride {Stride} = 0x74)");
    Console.WriteLine($"  {"file",-22}  {"bytes",10}  {"records",8}  {"tail",5}  {"iconValid",10}  classStanceRef");

    foreach (string path in doFiles)
    {
        if (!archive.Contains(path)) { Console.WriteLine($"  {Path.GetFileName(path),-22}  MISSING"); missing++; continue; }

        ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
        ReadOnlySpan<byte> raw = mem.Span;
        int nrec = raw.Length / Stride;
        int tail = raw.Length % Stride;
        totalFiles++;
        totalRecords += nrec;

        int validIcon = 0;
        var classRefSet = new SortedSet<uint>();

        for (int i = 0; i < nrec; i++)
        {
            ReadOnlySpan<byte> r = raw.Slice(i * Stride, Stride);

            // iconSrcX @+0x18, iconSrcY @+0x1C (i16); valid when both in [0..489].
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — iconSrcX @0x18 / iconSrcY @0x1C: CODE-CONFIRMED.
            short sx = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x18, 2));
            short sy = BinaryPrimitives.ReadInt16LittleEndian(r.Slice(0x1C, 2));
            if (sx >= 0 && sx <= MaxCoord && sy >= 0 && sy <= MaxCoord) validIcon++;

            // classStanceRef @+0x0C (u32 enum, e.g. 1001..1012).
            // spec: Docs/RE/formats/ui_manifests.md §2.7 — classStanceRef @0x0C: SAMPLE-VERIFIED.
            classRefSet.Add(BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(0x0C, 4)));

            // Per-u32-offset census.
            for (int d = 0; d < NumU32; d++)
            {
                uint v = BinaryPrimitives.ReadUInt32LittleEndian(r.Slice(d * 4, 4));
                distinctU32[d].Add(v);
                if (v < minU32[d]) minU32[d] = v;
                if (v > maxU32[d]) maxU32[d] = v;
                if (v == 0) zeroU32[d]++;
            }
        }
        totalValidIcon += validIcon;

        string classRefStr = string.Join(",", classRefSet.Take(5)) + (classRefSet.Count > 5 ? "…" : "");
        Console.WriteLine($"  {Path.GetFileName(path),-22}  {raw.Length,10:N0}  {nrec,8:N0}  {tail,5}  {validIcon,10:N0}  [{classRefStr}]");
    }

    Console.WriteLine($"\n  totals: {totalFiles} files, {totalRecords:N0} records, {missing} missing");
    if (totalRecords > 0)
        Console.WriteLine($"  iconSrcX/Y in [0..{MaxCoord}]: {totalValidIcon:N0} / {totalRecords:N0} ({100.0 * totalValidIcon / totalRecords:F1}%)");

    if (totalRecords == 0)
        return missing == doFiles.Length ? 1 : 0;

    // Compact per-offset field census (counts only, no raw bytes).
    Console.WriteLine("\n  per-u32-offset census (across all records):");
    Console.WriteLine($"    {"off",4}  {"#distinct",9}  {"min",12}  {"max",12}  {"zero%",6}  field");
    for (int d = 0; d < NumU32; d++)
    {
        int o = d * 4;
        double zp = 100.0 * zeroU32[d] / totalRecords;
        string field = o switch
        {
            0x00 => "instanceKey (primary map key)",
            0x04 => "groupSubIndex (0..N)",
            0x08 => "slotIndex (secondary map key)",
            0x0C => "classStanceRef (1001..1012)",
            0x10 => "groupId (skill family)",
            0x14 => "secXvar+unknown (u16 pair)",
            0x18 => "iconSrcX @0x18 / pad @0x1A (CONFIRMED)",
            0x1C => "iconSrcY @0x1C / pad @0x1E (CONFIRMED)",
            0x20 => "secondarySpriteX (u16 pair)",
            0x24 => "secondarySpriteY (u16 pair)",
            _    => "",
        };
        Console.WriteLine($"    {o,4:X2}  {distinctU32[d].Count,9:N0}  {minU32[d],12:N0}  {maxU32[d],12:N0}  {zp,5:F1}%  {field}");
    }

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-minimap
// ============================================================================
// Census the minimap/worldmap asset chain:
//   1. data/script/mapsetting.scr — 84-byte-stride zone table (id + CP949 name + bounds)
//   2. data/mapNNN/regiontableNNN.bin — 32-byte-stride sub-region records
//   3. data/ui map DDS inventory (dimensions + fourCC)
// Prints zone counts + strides + decoded structural fields only (NO raw bytes).
// spec: Docs/RE/formats/misc_data.md (mapsetting/regiontable strides SAMPLE-VERIFIED)
// ============================================================================

static int RunScanMinimap(MappedVfsArchive archive, string[] args, Encoding cp949)
{
    // mapsetting.scr stride = 84 (0x54): 4368 / 84 = 52 zones exactly.
    // spec: Docs/RE/formats/misc_data.md — mapsetting.scr stride 84: SAMPLE-VERIFIED.
    const int MapSettingStride = 84;
    // regiontable stride = 32: 1664 / 32 = 52 records exactly.
    // spec: Docs/RE/formats/misc_data.md — regiontable stride 32: PLAUSIBLE.
    const int RegionStride = 32;

    // --- 1. mapsetting.scr zone table ---
    Console.WriteLine("\nscan-minimap: zone/worldmap asset chain");
    const string mapSettingPath = "data/script/mapsetting.scr";
    if (archive.Contains(mapSettingPath))
    {
        ReadOnlyMemory<byte> mem = archive.GetFileContent(mapSettingPath);
        ReadOnlySpan<byte> data = mem.Span;
        int zoneCount = data.Length / MapSettingStride;
        int tail = data.Length % MapSettingStride;
        Console.WriteLine($"\n  [1] {mapSettingPath}: {data.Length:N0} bytes, {zoneCount} zones (stride {MapSettingStride}, tail {tail})");
        Console.WriteLine($"      {"id",3}  {"name(CP949)",-20}  {"minX",8} {"minY",8} {"maxX",8} {"maxY",8}");
        for (int i = 0; i < zoneCount; i++)
        {
            ReadOnlySpan<byte> rec = data.Slice(i * MapSettingStride, MapSettingStride);
            // [+0x00] id (i32), [+0x04] name (36B CP949), [+0x28..0x37] bounds (4×i32).
            // spec: Docs/RE/formats/misc_data.md — mapsetting record layout: SAMPLE-VERIFIED.
            int id = BinaryPrimitives.ReadInt32LittleEndian(rec[0x00..]);
            string name = cp949.GetString(rec.Slice(4, 36)).TrimEnd('\0');
            int minX = BinaryPrimitives.ReadInt32LittleEndian(rec[0x28..]);
            int minY = BinaryPrimitives.ReadInt32LittleEndian(rec[0x2C..]);
            int maxX = BinaryPrimitives.ReadInt32LittleEndian(rec[0x30..]);
            int maxY = BinaryPrimitives.ReadInt32LittleEndian(rec[0x34..]);
            // Cap the per-zone listing to keep output compact.
            if (i < 8 || i >= zoneCount - 2)
                Console.WriteLine($"      {id,3}  {name,-20}  {minX,8} {minY,8} {maxX,8} {maxY,8}");
            else if (i == 8)
                Console.WriteLine($"      … ({zoneCount - 10} more zones)");
        }
    }
    else Console.WriteLine($"\n  [1] MISSING: {mapSettingPath}");

    // --- 2. regiontableNNN.bin sub-region records ---
    Console.WriteLine("\n  [2] regiontableNNN.bin sub-region tables (stride 32):");
    int regionFiles = 0, regionRecords = 0;
    foreach (VfsEntry e in archive.GetEntries())
    {
        string nl = e.Name.ToLowerInvariant();
        if (!nl.StartsWith("data/map", StringComparison.Ordinal)) continue;
        if (!nl.Contains("/regiontable", StringComparison.Ordinal)) continue;
        if (!nl.EndsWith(".bin", StringComparison.Ordinal)) continue;

        int recs = (int)(e.DataSize / RegionStride);
        long rtail = e.DataSize % RegionStride;
        regionFiles++;
        regionRecords += recs;
        Console.WriteLine($"      {e.Name,-40}  {e.DataSize,8:N0} bytes  {recs,4} records  tail {rtail}");
    }
    Console.WriteLine($"      ({regionFiles} regiontable files, {regionRecords:N0} records total)");

    // --- 3. data/ui map DDS inventory ---
    // spec: Docs/RE/formats/texture.md §DDS_HEADER — height@0x0C, width@0x10, fourCC@0x54.
    const uint DdsMagic = 0x20534444; // "DDS "
    Console.WriteLine("\n  [3] data/ui map DDS inventory (dimensions + fourCC):");
    int mapDds = 0;
    foreach (VfsEntry e in archive.GetEntries())
    {
        string nl = e.Name.ToLowerInvariant();
        if (!nl.StartsWith("data/ui/", StringComparison.Ordinal)) continue;
        if (!nl.EndsWith(".dds", StringComparison.Ordinal)) continue;
        if (!nl.Contains("map", StringComparison.Ordinal) &&
            !nl.Contains("world", StringComparison.Ordinal) &&
            !nl.Contains("direction", StringComparison.Ordinal)) continue;

        ReadOnlyMemory<byte> mem = archive.GetFileContent(e.Name);
        if (mem.Length < 0x58) continue;
        ReadOnlySpan<byte> d = mem.Span;
        if (BinaryPrimitives.ReadUInt32LittleEndian(d[..4]) != DdsMagic) continue;

        uint h = BinaryPrimitives.ReadUInt32LittleEndian(d[0x0C..]);
        uint w = BinaryPrimitives.ReadUInt32LittleEndian(d[0x10..]);
        uint fourccRaw = BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(0x54, 4));
        string fourcc = fourccRaw == 0 ? "RAW"
            : Encoding.ASCII.GetString(BitConverter.GetBytes(fourccRaw)).TrimEnd('\0');
        mapDds++;
        Console.WriteLine($"      {e.Name,-40}  {w,4}x{h,-4}  {fourcc}");
    }
    Console.WriteLine($"      ({mapDds} map DDS files)");

    return 0;
}

// ============================================================================
// SUBCOMMAND: scan-quest
// ============================================================================
// Census the quest/dialog script tables by their fixed strides:
//   quests.scr        — stride 3720  (sparse: u16 id @0 != 0 marks an occupied slot)
//   npc.scr           — stride  404
//   autoquestion_cl.scr — stride 92
//   discript.sc       — stride  68
// Reports record/slot counts + occupied counts + strides + a few decoded ids.
// Prints COUNTS and short decoded fields only — NO raw byte dumps.
// spec: Docs/RE/formats/misc_data.md (quest/dialog script strides SAMPLE-VERIFIED)
// ============================================================================

static int RunScanQuest(MappedVfsArchive archive, string[] args, Encoding cp949)
{
    // quests.scr: fixed 3720-byte record slots; u16 quest_id @+0x00, 0 = empty slot.
    // spec: Docs/RE/formats/misc_data.md — quests.scr stride 3720: SAMPLE-VERIFIED.
    ScanQuestsScr(archive, "data/script/quests.scr", 3720, cp949);

    // npc.scr: fixed 404-byte records; u32 id @+0x00, CP949 text @+0x10.
    // spec: Docs/RE/formats/misc_data.md — npc.scr stride 404: SAMPLE-VERIFIED.
    ScanFixedStride(archive, "data/script/npc.scr", 404, cp949);

    // autoquestion_cl.scr: fixed 92-byte records.
    // spec: Docs/RE/formats/misc_data.md — autoquestion_cl.scr stride 92: SAMPLE-VERIFIED.
    ScanFixedStride(archive, "data/script/autoquestion_cl.scr", 92, cp949);

    // discript.sc: fixed 68-byte records; u32 id @+0x00, CP949 menu text @+0x08.
    // spec: Docs/RE/formats/misc_data.md — discript.sc stride 68: SAMPLE-VERIFIED.
    ScanFixedStride(archive, "data/script/discript.sc", 68, cp949);

    return 0;

    // ---- local helpers ----
    static void ScanQuestsScr(MappedVfsArchive archive, string path, int stride, Encoding cp949)
    {
        if (!archive.Contains(path)) { Console.WriteLine($"\nscan-quest: MISSING {path}"); return; }
        ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
        ReadOnlySpan<byte> raw = mem.Span;
        int slots = raw.Length / stride;
        int tail = raw.Length % stride;

        int occupied = 0;
        ushort minId = ushort.MaxValue, maxId = 0;
        var firstIds = new List<ushort>();
        for (int i = 0; i < slots; i++)
        {
            // u16 quest_id @+0x00; 0 = empty slot.
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * stride, 2));
            if (id == 0) continue;
            occupied++;
            if (id < minId) minId = id;
            if (id > maxId) maxId = id;
            if (firstIds.Count < 12) firstIds.Add(id);
        }

        Console.WriteLine($"\nscan-quest: {path}");
        Console.WriteLine($"  {raw.Length:N0} bytes, stride {stride}, {slots} slots (tail {tail}), {occupied} occupied (u16 id@0 != 0)");
        if (occupied > 0)
        {
            Console.WriteLine($"  quest_id range: {minId}..{maxId}");
            Console.WriteLine($"  first occupied ids: {string.Join(", ", firstIds)}");
        }
    }

    static void ScanFixedStride(MappedVfsArchive archive, string path, int stride, Encoding cp949)
    {
        if (!archive.Contains(path)) { Console.WriteLine($"\nscan-quest: MISSING {path}"); return; }
        ReadOnlyMemory<byte> mem = archive.GetFileContent(path);
        ReadOnlySpan<byte> raw = mem.Span;
        int nrec = raw.Length / stride;
        int tail = raw.Length % stride;

        Console.WriteLine($"\nscan-quest: {path}");
        Console.WriteLine($"  {raw.Length:N0} bytes, stride {stride}, {nrec:N0} records (tail {tail})");

        // Decode the first few record ids (u32 @+0x00) as a sanity readout — no raw bytes.
        var ids = new List<uint>();
        for (int i = 0; i < nrec && ids.Count < 8; i++)
            ids.Add(BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(i * stride, 4)));
        if (ids.Count > 0)
            Console.WriteLine($"  first record ids (u32@0): {string.Join(", ", ids)}");
    }
}

// ============================================================================
// Helpers (shared)
// ============================================================================

static bool IsPowerOf2(uint v) => v > 0 && (v & (v - 1)) == 0;

static bool MatchesSubstrings(string name, List<string> subs)
{
    foreach (string s in subs)
        if (!name.Contains(s, StringComparison.Ordinal))
            return false;
    return true;
}

static bool MatchesAnyExt(string name, List<string> exts)
{
    foreach (string ext in exts)
        if (name.EndsWith(ext, StringComparison.Ordinal))
            return true;
    return false;
}

static void PrintCensus(List<VfsEntry> entries)
{
    var byExt = new SortedDictionary<string, (int count, long bytes)>(StringComparer.Ordinal);
    foreach (VfsEntry e in entries)
    {
        string ext = ExtOf(e.Name);
        byExt.TryGetValue(ext, out (int count, long bytes) cur);
        byExt[ext] = (cur.count + 1, cur.bytes + e.DataSize);
    }

    Console.WriteLine($"extension census ({entries.Count:N0} entries, {byExt.Count} distinct extensions):");
    foreach ((string ext, (int count, long bytes)) in byExt)
        Console.WriteLine($"  {ext,-12}  {count,8:N0} files  {bytes,16:N0} bytes");
}

static string ExtOf(string name)
{
    int dot = name.LastIndexOf('.');
    int slash = name.LastIndexOf('/');
    if (dot < 0 || dot < slash) return "(none)";
    return name[dot..];
}

static void PrintHexDump(ReadOnlySpan<byte> data)
{
    const int width = 16;
    for (int off = 0; off < data.Length; off += width)
    {
        var sb = new StringBuilder();
        sb.Append(off.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
        int end = Math.Min(off + width, data.Length);
        for (int i = off; i < end; i++)
        {
            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            if (i - off == 7) sb.Append(' ');
        }
        int produced = end - off;
        int pad = (width - produced) * 3 + (produced <= 8 ? 1 : 0);
        sb.Append(' ', pad).Append(" |");
        for (int i = off; i < end; i++)
        {
            byte b = data[i];
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        sb.Append('|');
        Console.WriteLine(sb.ToString());
    }
}

static string DecodePreview(ReadOnlySpan<byte> data, Encoding cp949)
{
    string text = cp949.GetString(data);
    var sb = new StringBuilder(text.Length);
    foreach (char c in text)
        sb.Append(char.IsControl(c) && c != '\n' && c != '\t' ? '.' : c);
    return sb.ToString();
}

static IEnumerable<string> DefaultClientRoots()
{
    string? env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrWhiteSpace(env)) yield return env;

    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        yield return Path.Combine(
            dir.FullName, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
    }

    yield return "D:/MartialHeroesClient";
    yield return "C:/MartialHeroesClient";
}

static string NormPath(string p) => p.Replace('\\', '/').ToLowerInvariant();

static string NormExt(string e)
{
    e = e.ToLowerInvariant();
    return e.StartsWith('.') ? e : "." + e;
}

static string RequireValue(string[] args, ref int i, string opt)
{
    if (i + 1 >= args.Length)
    {
        Console.Error.WriteLine($"vfsls: option {opt} needs a value.");
        Environment.Exit(2);
    }
    return args[++i];
}

static int ParseInt(string s, string opt)
{
    if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < 0)
    {
        Console.Error.WriteLine($"vfsls: {opt} expects a non-negative integer, got '{s}'.");
        Environment.Exit(2);
    }
    return v;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        vfsls — throwaway Martial Heroes VFS inspector (metadata + short head previews only).

        Usage:
          dotnet run -c Release --project <dir> -- [substrings…] [options]
          dotnet run -c Release --project <dir> -- <subcommand> [subcommand-args]

        Positional:
          <substring>…        list entries whose name contains EVERY substring (AND).

        Options:
          --ext .skn          filter to one extension (repeatable, OR within the set).
          --count             print only the match count.
          --census            print entry count + bytes grouped by extension.
          --head <path>       hex + CP949 preview of one entry's first N bytes.
          --head-bytes <n>    head preview length (default 256).
          --contains <path>   print true/false for one exact virtual path.
          --limit <n>         cap listed entries (default 200; 0 = unlimited).
          --inf <path>        override data.inf location.
          --vfs <path>        override data/data.vfs location.
          -h, --help          this help.

        Subcommands (each accept --inf/--vfs overrides):

          scan-mot [--id <id_a>]
            Census .mot animation files: count, stub vs real (frame/track counts),
            BANI-variant detection, distribution histograms.
            spec: Docs/RE/formats/animation.md

          scan-bnd
            Census .bnd skeletons: bone counts, base ids, single vs multi-bone breakdown.
            spec: Docs/RE/formats/mesh.md

          scan-skn
            Census .skn skinned meshes: IdA/IdB pairs, vertex/face totals, weights/verts ratio.
            spec: Docs/RE/formats/mesh.md

          scan-ui
            Census data/ui/ DDS files: dimensions + fourCC, grouped by subdirectory.
            Flags non-power-of-2 textures.
            spec: Docs/RE/formats/texture.md, Docs/RE/formats/ui_manifests.md

          dump-msgxdb [--id N] [--range A B]
            Dump msg.xdb records (u32 id + CP949 string, 516B stride).
            No filter = print all; --id = one record; --range A B = inclusive range.
            spec: Docs/RE/formats/misc_data.md §6

          dump-uitex
            Parse and print the UiTex.txt manifest (tex_id → vfs_path).
            Tolerant of the known malformed entry (id 0029, missing closing quote).
            Checks each path against the VFS.
            spec: Docs/RE/formats/ui_manifests.md §1

          scan-xeff
            Census .xeff particle effect files: count, effect_id, element_count
            distribution, file size buckets, 9-digit skill-code pattern.
            spec: Docs/RE/formats/effects.md §Section A

          scan-sound
            Census .ogg under data/sound/2d|3d + parse 48-byte-stride per-area
            sound tables (.bgm/.bge/.eff/.wlk/.run) listing non-null entries.
            Reports whether each referenced ogg exists in the VFS.
            spec: Docs/RE/formats/sound_tables.md

          scan-fx
            Census all .fx1–.fx7 terrain layer files: counts per layer, total bytes,
            and header field histograms — especially field[3] at offset 0x0C which is
            disputed (IDA says 15; June 2026 sample says 50 for FX2).
            spec: Docs/RE/formats/terrain_layers.md §Section 1

          dump-do
            Census the 12 per-class stance .do files (icon/skill sprite-sheet records,
            stride 116/0x74): per-file record count, iconSrcX/Y validity, classStanceRef,
            plus a compact per-u32-offset field census. No raw bytes.
            spec: Docs/RE/formats/ui_manifests.md §2.7

          scan-minimap
            Census the minimap/worldmap asset chain: mapsetting.scr 52×84B zone table
            (id + CP949 name + bounds), regiontableNNN.bin 32B sub-region records, and
            the data/ui map DDS inventory (dimensions + fourCC). Strides + counts only.
            spec: Docs/RE/formats/misc_data.md

          scan-quest
            Census the quest/dialog script tables by fixed stride: quests.scr (3720B,
            sparse — u16 id@0 marks an occupied slot), npc.scr (404B), autoquestion_cl.scr
            (92B), discript.sc (68B). Record/slot/occupied counts + a few decoded ids.
            spec: Docs/RE/formats/misc_data.md
        """);
}
