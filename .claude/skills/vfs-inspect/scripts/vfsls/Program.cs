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

using System.Globalization;
using System.Text;
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

// --- Parse arguments. Positional tokens are AND'd substring filters. ---
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

using MappedVfsArchive archive = MappedVfsArchive.Open(infPath, vfsPath);
ReadOnlySpan<VfsEntry> entries = archive.GetEntries();

Console.WriteLine($"vfsls: mounted {entries.Length:N0} entries from {infPath}");

// --- --contains: just yes/no for one path. ---
if (containsPath is not null)
{
    bool present = archive.Contains(containsPath);
    Console.WriteLine($"contains(\"{containsPath}\") = {present.ToString().ToLowerInvariant()}");
    return present ? 0 : 1;
}

// --- --head: hex + CP949 preview of one entry's first N bytes. ---
if (headPath is not null)
{
    if (!archive.Contains(headPath))
    {
        Console.Error.WriteLine($"vfsls: no entry '{headPath}'.");
        return 1;
    }

    ReadOnlyMemory<byte> content = archive.GetFileContent(headPath);
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

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

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
        // Pad the hex column so the ASCII gutter lines up on the last short row.
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
    // 1. Explicit env override.
    string? env = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
    if (!string.IsNullOrWhiteSpace(env)) yield return env;

    // 2. The Godot project's embedded clientdata/ — walk up from the harness binary
    //    (…/.claude/skills/vfs-inspect/scripts/vfsls/bin/…) towards the repo root and
    //    probe the project-local folder at each ancestor.
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        yield return Path.Combine(
            dir.FullName, "05.Presentation", "MartialHeroes.Client.Godot", "clientdata");
    }

    // 3. Legacy external install locations.
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
          --inf <path>        override data.inf (default: probe MH_CLIENT_DIR, then the repo's
                              05.Presentation/MartialHeroes.Client.Godot/clientdata/, then D:/MartialHeroesClient).
          --vfs <path>        override data/data.vfs (same probe order).
          -h, --help          this help.
        """);
}
