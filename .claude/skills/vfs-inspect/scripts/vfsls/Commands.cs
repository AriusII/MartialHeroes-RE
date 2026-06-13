// Commands — the five registry-driven subcommands that turn vfsls from an INSPECT-only browser
// into an "understand the file" tool: decode (auto-detect + structured summary), extract (raw
// bytes → an explicit EXTERNAL out-file), convert (→ glTF/PNG/JSON via Assets.Mapping), hexdump
// (windowed head/region), and coverage (the honest registry of what is/ isn't understood).
//
// All of these share the single FormatRegistry. None of them ever print a full copyrighted
// payload: decode/hexdump emit counts/header fields/a small byte window; extract/convert write
// the user's own bytes to a path OUTSIDE the repo tree (guarded) and warn never to commit them.

using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Assets.Vfs;

namespace Vfsls;

internal static class Commands
{
    // ════════════════════════════════════════════════════════════════════════════════════════
    // decode <vfs-path>
    // Auto-detect the format and print a concise structured summary. No raw payload bytes.
    // ════════════════════════════════════════════════════════════════════════════════════════
    public static int Decode(MappedVfsArchive archive, string[] args, Encoding cp949)
    {
        string? vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine("decode: needs a <vfs-path>. e.g. decode data/char/0.skn");
            return 2;
        }

        vfsPath = NormPath(vfsPath);
        if (!archive.Contains(vfsPath))
        {
            Console.Error.WriteLine($"decode: no entry '{vfsPath}'.");
            return 1;
        }

        ReadOnlyMemory<byte> content = archive.GetFileContent(vfsPath);
        string ext = FormatRegistry.ExtOf(vfsPath);
        CapabilityEntry? entry = FormatRegistry.Lookup(vfsPath, content);

        Console.WriteLine($"decode: {vfsPath}");
        Console.WriteLine($"  size: {content.Length:N0} bytes   extension: {ext}");

        if (entry is null)
        {
            Console.WriteLine($"  format: UNREGISTERED — no decoder for '{ext}'.");
            Console.WriteLine("  (use `hexdump` to peek, `coverage` to see what is understood)");
            return 1;
        }

        Console.WriteLine($"  format: {entry.Name}   // spec: {entry.Spec}");

        if (entry.Decode is null)
        {
            Console.WriteLine("  (no structured decoder; raw passthrough only — use hexdump / convert)");
            return 0;
        }

        try
        {
            foreach (string line in entry.Decode(vfsPath, content))
                Console.WriteLine($"  {line}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  decode error: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // extract <vfs-path> <out-file>
    // Write the entry's RAW bytes to an explicit external path. GUARDED: refuses the repo tree.
    // ════════════════════════════════════════════════════════════════════════════════════════
    public static int Extract(MappedVfsArchive archive, string[] args)
    {
        var pos = Positionals(args);
        if (pos.Count < 2)
        {
            Console.Error.WriteLine("extract: needs <vfs-path> <out-file>. e.g. extract data/char/0.skn D:/dump/0.skn");
            return 2;
        }

        string vfsPath = NormPath(pos[0]);
        string outFile = pos[1];

        if (!archive.Contains(vfsPath))
        {
            Console.Error.WriteLine($"extract: no entry '{vfsPath}'.");
            return 1;
        }

        if (!GuardOutputPath(outFile, isDirectory: false, out string reason))
        {
            Console.Error.WriteLine($"extract: refusing to write '{outFile}': {reason}");
            return 3;
        }

        ReadOnlyMemory<byte> content = archive.GetFileContent(vfsPath);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
        if (dir is not null) Directory.CreateDirectory(dir);

        using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
            fs.Write(content.Span);

        Console.WriteLine($"extract: wrote {content.Length:N0} bytes → {Path.GetFullPath(outFile)}");
        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // convert <vfs-path> <out-dir>
    // Convert via Assets.Mapping where supported (mesh→GLB, texture→PNG, xeff→JSON, …).
    // ════════════════════════════════════════════════════════════════════════════════════════
    public static int Convert(MappedVfsArchive archive, string[] args)
    {
        var pos = Positionals(args);
        if (pos.Count < 2)
        {
            Console.Error.WriteLine("convert: needs <vfs-path> <out-dir>. e.g. convert data/char/0.skn D:/dump");
            return 2;
        }

        string vfsPath = NormPath(pos[0]);
        string outDir = pos[1];

        if (!archive.Contains(vfsPath))
        {
            Console.Error.WriteLine($"convert: no entry '{vfsPath}'.");
            return 1;
        }

        ReadOnlyMemory<byte> content = archive.GetFileContent(vfsPath);
        CapabilityEntry? entry = FormatRegistry.Lookup(vfsPath, content);

        if (entry is null || entry.Convert == ConvertKind.None)
        {
            string ext = FormatRegistry.ExtOf(vfsPath);
            Console.WriteLine($"convert: SKIP {vfsPath} — no converter for '{ext}'" +
                              (entry is null ? " (unregistered)." : $" ({entry.Name})."));
            return 1;
        }

        if (!GuardOutputPath(outDir, isDirectory: true, out string reason))
        {
            Console.Error.WriteLine($"convert: refusing to write into '{outDir}': {reason}");
            return 3;
        }

        Directory.CreateDirectory(outDir);

        // out file = <stem><convert-out-ext> inside out-dir.
        string stem = Path.GetFileNameWithoutExtension(vfsPath);
        string outFile = Path.Combine(outDir, stem + entry.ConvertOutExt);

        try
        {
            string note;
            using (var fs = new FileStream(outFile, FileMode.Create, FileAccess.Write))
                note = FormatRegistry.RunConvert(entry, vfsPath, content, idB => ResolveBnd(archive, idB), fs);

            long written = new FileInfo(outFile).Length;
            Console.WriteLine($"convert: {note}");
            Console.WriteLine($"  wrote {written:N0} bytes → {Path.GetFullPath(outFile)}");
        }
        catch (Exception ex)
        {
            // Clean up a half-written file so a failed convert leaves no garbage.
            try { if (File.Exists(outFile)) File.Delete(outFile); } catch { /* best-effort */ }
            Console.Error.WriteLine($"convert: FAILED {vfsPath}: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        WarnNeverCommit();
        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // hexdump <vfs-path> [--at <off>] [--len <n>] [--header]
    // Windowed hexdump (default a small head). --header annotates the leading bytes lightly.
    // ════════════════════════════════════════════════════════════════════════════════════════
    public static int Hexdump(MappedVfsArchive archive, string[] args, Encoding cp949)
    {
        string? vfsPath = FirstPositional(args);
        if (vfsPath is null)
        {
            Console.Error.WriteLine("hexdump: needs a <vfs-path>. e.g. hexdump data/char/0.skn --at 0 --len 64");
            return 2;
        }

        int at = 0, len = 64;
        bool headerMode = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--at" && i + 1 < args.Length) int.TryParse(args[i + 1], out at);
            else if (args[i] == "--len" && i + 1 < args.Length) int.TryParse(args[i + 1], out len);
            else if (args[i] == "--header") headerMode = true;
        }

        // Cap the window so this never becomes a full-payload dump.
        const int MaxWindow = 512;
        if (len > MaxWindow) len = MaxWindow;
        if (len < 0) len = 0;
        if (at < 0) at = 0;

        vfsPath = NormPath(vfsPath);
        if (!archive.Contains(vfsPath))
        {
            Console.Error.WriteLine($"hexdump: no entry '{vfsPath}'.");
            return 1;
        }

        ReadOnlyMemory<byte> content = archive.GetFileContent(vfsPath);
        if (headerMode)
        {
            at = 0;
            len = Math.Min(content.Length, 32); // header window
        }

        if (at >= content.Length)
        {
            Console.WriteLine($"hexdump: offset {at} is past EOF ({content.Length:N0} bytes).");
            return 1;
        }

        int end = Math.Min(at + len, content.Length);
        ReadOnlySpan<byte> window = content.Span[at..end];

        Console.WriteLine($"hexdump: {vfsPath}  ({content.Length:N0} bytes total)");
        Console.WriteLine($"  window: offset {at} .. {end} ({window.Length} bytes shown; capped at {MaxWindow})");
        Console.WriteLine();
        PrintHexDump(window, at, cp949);

        if (headerMode)
        {
            Console.WriteLine();
            Console.WriteLine("  -- header annotation (structural hint) --");
            foreach (string line in AnnotateHeader(vfsPath, content))
                Console.WriteLine($"  {line}");
        }

        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // coverage
    // Print the registry: extension → (decode? convert?) plus documented-but-unparsed formats.
    // ════════════════════════════════════════════════════════════════════════════════════════
    public static int Coverage(string[] args, string repoRoot)
    {
        Console.WriteLine("coverage: vfsls format registry (what this tool can understand)\n");
        Console.WriteLine($"  {"ext",-10}  {"decode",6}  {"convert",10}  {"spec",-34}  name");
        Console.WriteLine($"  {"---",-10}  {"------",6}  {"-------",10}  {"----",-34}  ----");

        var citedSpecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int withDecode = 0, withConvert = 0, total = 0;

        foreach (CapabilityEntry e in FormatRegistry.All)
        {
            total++;
            string dec = e.Decode is not null ? "yes" : "—";
            string conv = e.Convert == ConvertKind.None ? "—" : e.ConvertOutExt.TrimStart('.');
            if (e.Decode is not null) withDecode++;
            if (e.Convert != ConvertKind.None) withConvert++;
            if (!string.IsNullOrEmpty(e.Spec)) citedSpecs.Add(Path.GetFileName(e.Spec));

            Console.WriteLine($"  {e.Extension,-10}  {dec,6}  {conv,10}  {e.Spec,-34}  {e.Name}");
        }

        Console.WriteLine();
        Console.WriteLine($"  registry: {total} extension entries — {withDecode} decode, {withConvert} convert.");

        // Cross-reference Docs/RE/formats/*.md: which documented formats have NO registry entry
        // citing them? Those are the honest "documented-but-unparsed" follow-up targets.
        string formatsDir = Path.Combine(repoRoot, "Docs", "RE", "formats");
        if (Directory.Exists(formatsDir))
        {
            Console.WriteLine();
            Console.WriteLine("  Docs/RE/formats/*.md cross-reference (spec doc → cited by a registry entry?):");
            var unreferenced = new List<string>();
            foreach (string md in Directory.EnumerateFiles(formatsDir, "*.md").OrderBy(p => p, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(md);
                bool cited = citedSpecs.Contains(name);
                Console.WriteLine($"    {(cited ? "[cited]   " : "[NO ENTRY]")}  {name}");
                if (!cited) unreferenced.Add(name);
            }

            if (unreferenced.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Documented but NOT yet wired into the decode registry (follow-up wave):");
                foreach (string n in unreferenced)
                    Console.WriteLine($"    - {n}");
            }
        }
        else
        {
            Console.WriteLine($"  (Docs/RE/formats not found at {formatsDir}; skipping cross-reference)");
        }

        return 0;
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // Shared helpers
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a .bnd skeleton for a skinned mesh by its id_b, via the documented bind path.
    /// spec: Docs/RE/formats/mesh.md §Header — id_b → data/char/bind/g{id_b}.bnd: CONFIRMED.
    /// </summary>
    private static Skeleton? ResolveBnd(MappedVfsArchive archive, uint idB)
    {
        string bndPath = $"data/char/bind/g{idB}.bnd";
        if (!archive.Contains(bndPath)) return null;
        try { return BndParser.Parse(archive.GetFileContent(bndPath)); }
        catch { return null; }
    }

    /// <summary>
    /// Refuses to write into the repo tree or a git-tracked path; only explicit external dirs.
    /// </summary>
    private static bool GuardOutputPath(string outPath, bool isDirectory, out string reason)
    {
        reason = "";
        string full;
        try { full = Path.GetFullPath(outPath); }
        catch (Exception ex) { reason = $"invalid path ({ex.Message})"; return false; }

        // Refuse anything inside the repo root (the firewall: extracted originals never enter the tree).
        string? repoRoot = RepoRoot.Find();
        if (repoRoot is not null)
        {
            string rootFull = Path.GetFullPath(repoRoot);
            string candidate = isDirectory ? full : (Path.GetDirectoryName(full) ?? full);
            if (IsUnder(candidate, rootFull))
            {
                reason = $"path is inside the repository tree ({rootFull}). Extracted originals must " +
                         "stay OUTSIDE the repo (e.g. D:/dump). Never commit game assets.";
                return false;
            }
        }

        // Refuse a path inside any .git directory (belt-and-braces).
        if (full.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase))
        {
            reason = "path is inside a .git directory.";
            return false;
        }

        return true;
    }

    private static bool IsUnder(string candidate, string root)
    {
        string c = NormalizeDir(candidate);
        string r = NormalizeDir(root);
        return c.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               c.StartsWith(r + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDir(string p) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));

    private static void WarnNeverCommit() =>
        Console.WriteLine(
            "  WARNING: this is an original copyrighted asset extracted from YOUR client. " +
            "Never commit it to the repo (it is gitignored for a reason).");

    // ── header annotation: a LIGHT structural hint, not a decode ─────────────────────────────
    private static IReadOnlyList<string> AnnotateHeader(string vfsPath, ReadOnlyMemory<byte> content)
    {
        CapabilityEntry? entry = FormatRegistry.Lookup(vfsPath, content);
        var lines = new List<string>();
        if (entry is null)
        {
            lines.Add($"extension {FormatRegistry.ExtOf(vfsPath)} is unregistered — no structural hint.");
            return lines;
        }

        lines.Add($"format: {entry.Name}   // spec: {entry.Spec}");
        // Re-use the structured decoder for the annotation when one exists (counts only).
        if (entry.Decode is not null)
        {
            try { lines.AddRange(entry.Decode(vfsPath, content)); }
            catch (Exception ex) { lines.Add($"(decode hint unavailable: {ex.Message})"); }
        }
        else
        {
            lines.Add("(no structured decoder; raw passthrough type)");
        }

        return lines;
    }

    // ── hexdump renderer (offset-aware) ──────────────────────────────────────────────────────
    private static void PrintHexDump(ReadOnlySpan<byte> data, int baseOffset, Encoding cp949)
    {
        const int width = 16;
        for (int off = 0; off < data.Length; off += width)
        {
            var sb = new StringBuilder();
            sb.Append((baseOffset + off).ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
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

    // ── arg parsing ──────────────────────────────────────────────────────────────────────────
    private static string? FirstPositional(string[] args)
    {
        var p = Positionals(args);
        return p.Count > 0 ? p[0] : null;
    }

    // Positional args = those not starting with "--" and not consumed as a flag VALUE.
    // Flags that take a value: --inf, --vfs, --at, --len.
    private static List<string> Positionals(string[] args)
    {
        var result = new List<string>();
        var valueFlags = new HashSet<string>(StringComparer.Ordinal) { "--inf", "--vfs", "--at", "--len" };
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (valueFlags.Contains(a)) i++; // skip its value
                continue;
            }
            result.Add(a);
        }
        return result;
    }

    private static string NormPath(string p) => p.Replace('\\', '/').ToLowerInvariant();
}

/// <summary>
/// Locates the repository root (the directory containing <c>MartialHeroes.slnx</c>) by walking up
/// from the harness binary. Used by the output-path guard (refuse the repo tree) and the
/// coverage cross-reference (find Docs/RE/formats).
/// </summary>
internal static class RepoRoot
{
    private static string? _cached;
    private static bool _resolved;

    public static string? Find()
    {
        if (_resolved) return _cached;
        _resolved = true;
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MartialHeroes.slnx")))
            {
                _cached = dir.FullName;
                return _cached;
            }
        }
        return null;
    }
}
