// assetprobe — raw asset dump tool for byte-exact validation against the real client VFS.
//
// Phase 0 of the asset-fidelity campaign: writes JSON/CSV dumps of raw field values from
// the production VFS parsers. Downstream phases diff these dumps against the C# parsers and
// Docs/RE specs to detect parser drift or spec errors.
//
// Usage:
//   assetprobe mot-frames   <vfs-path> [--track N] --out <file>
//   assetprobe bnd-matrices <vfs-path>             --out <file>
//   assetprobe skn-weights  <vfs-path>             --out <file>
//   assetprobe ted-blocks   <vfs-path>             --out <file>
//   assetprobe xeff-emitters <vfs-path>            --out <file>
//
// VFS mount: default probes MH_CLIENT_DIR env → clientdata/ → D:/MartialHeroesClient.
// Override with --inf / --vfs.
//
// Output guard: refuses to write inside the repo tree. Dumps go to an EXTERNAL path only
// (e.g. %TEMP%/assetprobe/ or D:/dump/). Never commit game asset dumps.

using System.Text;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Tools.AssetProbe;

// CP949 lives in the CodePages provider, which is opt-in on modern .NET.
// spec: CLAUDE.md §Core engineering constraints — "Register it once: Encoding.RegisterProvider(...)"
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// --- Defaults: bring-your-own client location. Override with --inf / --vfs. ---
// Probe order: MH_CLIENT_DIR env → clientdata/ → D:/MartialHeroesClient.
var infPath = "D:/MartialHeroesClient/data.inf";
var vfsPath = "D:/MartialHeroesClient/data/data.vfs";

foreach (var root in DefaultClientRoots())
{
    var candidateInf = Path.Combine(root, "data.inf");
    var candidateVfs = Path.Combine(root, "data", "data.vfs");
    if (File.Exists(candidateInf) && File.Exists(candidateVfs))
    {
        infPath = candidateInf;
        vfsPath = candidateVfs;
        break;
    }
}

// --- Route subcommand ---
if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

var subcmd = args[0];
var subcmdArgs = args[1..];

// Extract --inf / --vfs overrides from subcmd args.
for (var i = 0; i < subcmdArgs.Length - 1; i++)
    if (subcmdArgs[i] == "--inf") infPath = subcmdArgs[i + 1];
    else if (subcmdArgs[i] == "--vfs") vfsPath = subcmdArgs[i + 1];

if (!File.Exists(infPath) || !File.Exists(vfsPath))
{
    Console.Error.WriteLine(
        $"assetprobe: client files not found.\n  inf: {infPath} (exists={File.Exists(infPath)})\n" +
        $"  vfs: {vfsPath} (exists={File.Exists(vfsPath)})\n" +
        "Set MH_CLIENT_DIR, mount at D:/MartialHeroesClient, or pass --inf/--vfs.");
    return 2;
}

using var archive = MappedVfsArchive.Open(infPath, vfsPath);
Console.WriteLine($"assetprobe: mounted {archive.GetEntries().Length:N0} entries from {infPath}");

return subcmd switch
{
    "mot-frames" => Commands.MotFrames(archive, subcmdArgs),
    "bnd-matrices" => Commands.BndMatrices(archive, subcmdArgs),
    "skn-weights" => Commands.SknWeights(archive, subcmdArgs),
    "ted-blocks" => Commands.TedBlocks(archive, subcmdArgs),
    "xeff-emitters" => Commands.XeffEmitters(archive, subcmdArgs),
    _ => UnknownSubcommand(subcmd)
};

// ── helpers ───────────────────────────────────────────────────────────────────

static int UnknownSubcommand(string subcmd)
{
    Console.Error.WriteLine($"assetprobe: unknown subcommand '{subcmd}'. Use --help.");
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
                      assetprobe — raw asset dump tool for byte-exact parser validation

                      Usage:
                        assetprobe mot-frames    <vfs-path> [--track N] --out <file>
                        assetprobe bnd-matrices  <vfs-path>             --out <file>
                        assetprobe skn-weights   <vfs-path>             --out <file>
                        assetprobe ted-blocks    <vfs-path>             --out <file>
                        assetprobe xeff-emitters <vfs-path>             --out <file>

                      Options:
                        --inf <path>    Override data.inf path
                        --vfs <path>    Override data/data.vfs path
                        --out <path>    Output file path (MUST be outside the repo tree)
                        --track N       (mot-frames only) restrict output to track index N

                      VFS probe order: MH_CLIENT_DIR env → clientdata/ → D:/MartialHeroesClient
                      Output guard: refuses to write inside the repository tree.
                      WARNING: dumps are extracted client data — never commit them.
                      """);
}

// Probe order for default VFS root: MH_CLIENT_DIR env → clientdata/ (repo-embedded) → D:/MartialHeroesClient.
static IEnumerable<string> DefaultClientRoots()
{
    var envDir = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrEmpty(envDir)) yield return envDir;

    // clientdata/ relative to the repo root (walk up from binary).
    var repoRoot = RepoRootFinder.Find();
    if (repoRoot is not null)
        yield return Path.Combine(repoRoot, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");

    yield return "D:/MartialHeroesClient";
}