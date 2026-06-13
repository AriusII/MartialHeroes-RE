// chaintrace — existence tracer for a Martial Heroes asset-chain hop.
//
// Walks a resolved VFS path (one hop of a recovered asset chain) and reports ONLY its
// index metadata: membership (Contains), size (DataSize), offset (DataOffset), and — for the
// `stride` mode — a derived record count. It NEVER reads, decodes, or prints payload bytes:
// there is intentionally no GetFileContent call anywhere in this file. Keep it that way.
//
// The chain resolution (which id maps to which path) is performed by the SKILL operator from the
// committed specs (Docs/RE/formats/ + CLAUDE.md "Recovered asset mappings"); this harness just
// confirms each resolved path exists in the real client archive and how big it is.
//
// Run in place (never added to MartialHeroes.slnx):
//   dotnet run -c Release --project <thisdir> -- exists       <vfs-path>
//   dotnet run -c Release --project <thisdir> -- exists-many  <p1> <p2> ...
//   dotnet run -c Release --project <thisdir> -- stride       <vfs-path> <record-bytes>
//   dotnet run -c Release --project <thisdir> -- census                       (extension counts)
// Optional anywhere: --inf <data.inf> --vfs <data/data.vfs> to override client location.

using System.Text;
using MartialHeroes.Assets.Vfs;

// === CONFIG ===
// Default client locations, tried in the ClientPathResolver order (project-local clientdata/
// first, then the legacy external install). Override either with --inf / --vfs on the CLI.
static class Config
{
    // Project-local clientdata/ (the native, recommended location — gitignored).
    public const string ClientDataInf =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data.inf";
    public const string ClientDataVfs =
        @"C:\Users\Arius\RiderProjects\MartialHeroes\05.Presentation\MartialHeroes.Client.Godot\clientdata\data\data.vfs";

    // Legacy external fallback.
    public const string LegacyInf = @"D:\MartialHeroesClient\data.inf";
    public const string LegacyVfs = @"D:\MartialHeroesClient\data\data.vfs";
}

static class Program
{
    static int Main(string[] args)
    {
        // All legacy client text (skin.txt, actormotion.txt, bgtexture.txt, …) is CP949 (Korean).
        // Register the provider once so any future table read decodes correctly.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var positional = new List<string>();
        string? infOverride = null, vfsOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--inf": infOverride = NextArg(args, ref i); break;
                case "--vfs": vfsOverride = NextArg(args, ref i); break;
                default: positional.Add(args[i]); break;
            }
        }

        if (positional.Count == 0)
        {
            PrintUsage();
            return 2;
        }

        if (!ResolveClient(infOverride, vfsOverride, out string inf, out string vfs))
        {
            Console.Error.WriteLine(
                "[chaintrace] No valid client VFS found (need data.inf + data/data.vfs). " +
                "Pass --inf <path> --vfs <path>, or mount clientdata/ / D:/MartialHeroesClient.");
            return 3;
        }

        using MappedVfsArchive archive = MappedVfsArchive.Open(inf, vfs);

        string verb = positional[0];
        switch (verb)
        {
            case "exists":
                if (positional.Count < 2) { PrintUsage(); return 2; }
                ReportPath(archive, positional[1]);
                return 0;

            case "exists-many":
                if (positional.Count < 2) { PrintUsage(); return 2; }
                for (int i = 1; i < positional.Count; i++)
                    ReportPath(archive, positional[i]);
                return 0;

            case "stride":
                if (positional.Count < 3 || !int.TryParse(positional[2], out int rec) || rec <= 0)
                {
                    Console.Error.WriteLine("[chaintrace] stride needs: stride <vfs-path> <record-bytes>");
                    return 2;
                }
                ReportStride(archive, positional[1], rec);
                return 0;

            case "census":
                ReportCensus(archive);
                return 0;

            default:
                PrintUsage();
                return 2;
        }
    }

    // --- existence (index metadata only — never payload) -----------------------------------

    static void ReportPath(MappedVfsArchive archive, string vfsPath)
    {
        string norm = vfsPath.Replace('\\', '/').ToLowerInvariant();
        bool present = archive.Contains(norm);
        if (!present)
        {
            Console.WriteLine($"MISSING  {norm}   <-- BROKEN HOP: not in VFS; recheck the mapping/spec for this hop");
            return;
        }

        // Find the entry to report size/offset — still index-only, no payload read.
        long size = -1, off = -1;
        foreach (VfsEntry e in archive.GetEntries())
        {
            if (e.Name == norm) { size = e.DataSize; off = e.DataOffset; break; }
        }
        Console.WriteLine($"OK       {norm}   size={size}  offset={off}");
    }

    static void ReportStride(MappedVfsArchive archive, string vfsPath, int recordBytes)
    {
        string norm = vfsPath.Replace('\\', '/').ToLowerInvariant();
        if (!archive.Contains(norm))
        {
            Console.WriteLine($"MISSING  {norm}   <-- BROKEN HOP: not in VFS");
            return;
        }
        foreach (VfsEntry e in archive.GetEntries())
        {
            if (e.Name != norm) continue;
            long size = e.DataSize;
            long count = size / recordBytes;
            long rem = size % recordBytes;
            string flag = rem == 0
                ? ""
                : $"   <-- WARNING: size not a multiple of {recordBytes} (remainder {rem}); recheck the record stride spec";
            Console.WriteLine($"OK       {norm}   size={size}  stride={recordBytes}  records={count}{flag}");
            return;
        }
    }

    static void ReportCensus(MappedVfsArchive archive)
    {
        var byExt = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int total = 0;
        foreach (VfsEntry e in archive.GetEntries())
        {
            total++;
            int dot = e.Name.LastIndexOf('.');
            string ext = dot >= 0 ? e.Name[dot..] : "<none>";
            byExt[ext] = byExt.TryGetValue(ext, out int c) ? c + 1 : 1;
        }
        Console.WriteLine($"VFS entries: {total}");
        foreach (var kv in byExt)
            Console.WriteLine($"  {kv.Key,-12} {kv.Value}");
    }

    // --- client resolution -----------------------------------------------------------------

    static bool ResolveClient(string? infOverride, string? vfsOverride, out string inf, out string vfs)
    {
        if (infOverride is not null && vfsOverride is not null)
        {
            inf = infOverride; vfs = vfsOverride;
            return File.Exists(inf) && File.Exists(vfs);
        }
        // ClientPathResolver order: project-local clientdata/ first, then the legacy external install.
        if (File.Exists(Config.ClientDataInf) && File.Exists(Config.ClientDataVfs))
        {
            inf = Config.ClientDataInf; vfs = Config.ClientDataVfs;
            return true;
        }
        if (File.Exists(Config.LegacyInf) && File.Exists(Config.LegacyVfs))
        {
            inf = Config.LegacyInf; vfs = Config.LegacyVfs;
            return true;
        }
        inf = vfs = "";
        return false;
    }

    static string? NextArg(string[] args, ref int i) => (i + 1 < args.Length) ? args[++i] : null;

    static void PrintUsage()
    {
        Console.Error.WriteLine(
            "chaintrace — existence-check one or more resolved asset-chain hops (index metadata only).\n" +
            "  exists      <vfs-path>                  membership + size + offset for one path\n" +
            "  exists-many <p1> <p2> ...               same, one line per path (a whole resolved chain)\n" +
            "  stride      <vfs-path> <record-bytes>   existence + record count (size / bytes); e.g. .arr 28 or 20\n" +
            "  census                                  entry count grouped by extension\n" +
            "Options: --inf <data.inf> --vfs <data/data.vfs>  override the auto-resolved client location.\n" +
            "This harness NEVER prints payload bytes. To dump raw bytes to an EXTERNAL file, use vfs-inspect's guarded extract.");
    }
}
