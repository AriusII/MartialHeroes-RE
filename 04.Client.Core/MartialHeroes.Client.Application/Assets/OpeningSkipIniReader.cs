using System.Text;

namespace MartialHeroes.Client.Application.Assets;

/// <summary>
/// Minimal private-profile-compatible reader for the state-2 <c>OPENNING/SKIP</c> decision.
/// </summary>
public sealed class OpeningSkipIniReader : IOpeningSkipReader
{
    private const string SectionName = "OPENNING"; // spec: Docs/RE/specs/resource_pipeline.md §2.5.
    private const string KeyName = "SKIP"; // spec: Docs/RE/specs/resource_pipeline.md §2.5.

    private readonly string _iniPath;

    public OpeningSkipIniReader(string iniPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iniPath);
        _iniPath = iniPath;
    }

    public bool ReadSkipOpening()
    {
        if (!File.Exists(_iniPath))
            return false;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string[] lines = File.ReadAllLines(_iniPath, Encoding.GetEncoding(949));
        bool inSection = false;

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
                continue;

            if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal))
            {
                string section = line[1..^1].Trim();
                inSection = string.Equals(section, SectionName, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = line[..equals].Trim();
            if (!string.Equals(key, KeyName, StringComparison.OrdinalIgnoreCase))
                continue;

            string value = StripInlineComment(line[(equals + 1)..]).Trim();
            return int.TryParse(value, out int parsed) && parsed != 0;
        }

        return false;
    }

    private static string StripInlineComment(string value)
    {
        int semicolon = value.IndexOf(';');
        int hash = value.IndexOf('#');
        int cut = semicolon < 0 ? hash : hash < 0 ? semicolon : Math.Min(semicolon, hash);
        return cut < 0 ? value : value[..cut];
    }
}