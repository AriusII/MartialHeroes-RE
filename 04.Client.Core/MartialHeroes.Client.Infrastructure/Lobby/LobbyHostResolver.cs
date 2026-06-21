using System.Runtime.Versioning;
using System.Text;
using MartialHeroes.Client.Application.Login;
using Microsoft.Win32;

namespace MartialHeroes.Client.Infrastructure.Lobby;

/// <summary>
///     Concrete implementation of <see cref="ILobbyHostResolver" /> that mirrors the original client's
///     three-tier lobby-host lookup sequence (ip.txt → list.dat CIPList → hardcoded fallback).
/// </summary>
/// <remarks>
///     <para>
///         The resolved host is a dotted-decimal IPv4 literal — the lobby socket uses <c>inet_addr</c>
///         (no DNS) so only a numeric quad is valid. spec: Docs/RE/specs/login_flow.md §2.0 item 3.
///     </para>
///     <para>
///         <b>Tier 1 — <c>ip.txt</c> override.</b> If <c>ip.txt</c> is present in the client root
///         directory, reads a single whitespace-free token, truncated to 19 characters, and uses it.
///         spec: Docs/RE/specs/login_flow.md §2.0 Tier 1; Docs/RE/specs/frontend_layout_tables.md §8.
///     </para>
///     <para>
///         <b>Tier 2 — <c>list.dat</c> CIPList keyed by registry server name.</b> Loads the binary
///         <c>list.dat</c> file (count u32 + count × 768-byte records). File-size invariant: internal
///         length == 768 × count + 4. The active record is selected by matching the CP949 server name at
///         record offset +0 against the registry value <c>HKLM\SOFTWARE\crspace\do : servername</c>
///         (REG_SZ); the selected record's host is read at record offset +256.
///         spec: Docs/RE/specs/login_flow.md §2.0 Tier 2; Docs/RE/packets/lobby.yaml §RECORD SHAPE C.
///     </para>
///     <para>
///         <b>Tier 3 — hardcoded fallback <c>211.196.150.4</c>.</b>
///         spec: Docs/RE/specs/login_flow.md §2.0 Tier 3; Docs/RE/specs/frontend_layout_tables.md §8.
///     </para>
/// </remarks>
public sealed class LobbyHostResolver : ILobbyHostResolver
{
    // spec: Docs/RE/specs/login_flow.md §2.0 Tier 3 — hardcoded default lobby host.
    private const string FallbackHost = "211.196.150.4";

    // spec: Docs/RE/specs/login_flow.md §2.0 Tier 1 (ip.txt, single token ≤19 chars).
    private const string IpTxtFileName = "ip.txt";
    private const int IpTxtMaxLength = 19; // spec: login_flow.md §2.0 Tier 1 ("truncated to 19 chars").

    // spec: Docs/RE/specs/login_flow.md §2.0 Tier 2 — list.dat layout.
    private const string ListDatFileName = "list.dat";
    private const int ListDatRecordSize = 768; // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE C (768-byte records).
    private const int ListDatHostOffset = 256; // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE C (+256 = host string).
    private const int ListDatHeaderSize = 4; // spec: §RECORD SHAPE C (+0 u32 count header).

    // spec: Docs/RE/specs/login_flow.md §2.0 Tier 2 registry key path and value name.
    private const string RegistryKeyPath = @"SOFTWARE\crspace\do"; // spec: login_flow.md §2.0.
    private const string RegistryServerNameValue = "servername"; // spec: login_flow.md §2.0.

    private static readonly Encoding Cp949 = CreateCp949();

    private readonly string _clientRoot;

    /// <summary>
    ///     Creates the resolver.
    /// </summary>
    /// <param name="clientRoot">
    ///     The client working directory (where <c>ip.txt</c> and <c>list.dat</c> are searched).
    ///     Defaults to <see cref="Directory.GetCurrentDirectory" /> when <see langword="null" /> or
    ///     empty.
    /// </param>
    public LobbyHostResolver(string? clientRoot = null)
    {
        _clientRoot = string.IsNullOrWhiteSpace(clientRoot)
            ? Directory.GetCurrentDirectory()
            : clientRoot;
    }

    /// <inheritdoc />
    public string Resolve()
    {
        // Tier 1: ip.txt override. spec: Docs/RE/specs/login_flow.md §2.0 Tier 1.
        var ipTxtPath = Path.Combine(_clientRoot, IpTxtFileName);
        if (File.Exists(ipTxtPath))
        {
            var host = TryReadIpTxt(ipTxtPath);
            if (!string.IsNullOrWhiteSpace(host))
                return host;
        }

        // Tier 2: list.dat CIPList keyed by registry servername.
        // The registry lookup (HKLM\SOFTWARE\crspace\do : servername) is Windows-only;
        // on non-Windows platforms the tier-2 path silently falls through to tier 3.
        // spec: Docs/RE/specs/login_flow.md §2.0 Tier 2; lobby.yaml §RECORD SHAPE C.
        if (OperatingSystem.IsWindows())
        {
            var listDatPath = Path.Combine(_clientRoot, ListDatFileName);
            if (File.Exists(listDatPath))
            {
                var host = TryReadListDat(listDatPath);
                if (!string.IsNullOrWhiteSpace(host))
                    return host;
            }
        }

        // Tier 3: hardcoded fallback. spec: Docs/RE/specs/login_flow.md §2.0 Tier 3.
        return FallbackHost;
    }

    // ── Tier 1 helpers ──────────────────────────────────────────────────────────────────────────

    private static string? TryReadIpTxt(string path)
    {
        try
        {
            // Read the first non-empty whitespace-free token, truncated to 19 chars.
            // spec: Docs/RE/specs/login_flow.md §2.0 Tier 1 ("single whitespace-free token, truncated to 19 characters").
            var text = File.ReadAllText(path, Encoding.ASCII).Trim();
            // Take the first whitespace-delimited token.
            var wsIdx = -1;
            for (var i = 0; i < text.Length; i++)
                if (char.IsWhiteSpace(text[i]))
                {
                    wsIdx = i;
                    break;
                }

            var token = wsIdx < 0 ? text : text[..wsIdx];
            if (token.Length == 0)
                return null;

            // Truncate to 19 characters. spec: login_flow.md §2.0 Tier 1.
            if (token.Length > IpTxtMaxLength)
                token = token[..IpTxtMaxLength];

            return token;
        }
        catch
        {
            return null;
        }
    }

    // ── Tier 2 helpers ──────────────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private string? TryReadListDat(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < ListDatHeaderSize)
                return null;

            // Read the record count (u32 little-endian at +0).
            // spec: Docs/RE/packets/lobby.yaml §RECORD SHAPE C (+0 u32 count).
            var count = BitConverter.ToUInt32(data, 0);

            // File-size invariant: length == 768 * count + 4.
            // spec: lobby.yaml §RECORD SHAPE C ("an internal length field must equal 768 × count + 4 or the load is rejected").
            var expectedSize = (long)ListDatRecordSize * count + ListDatHeaderSize;
            if (data.Length != expectedSize)
                return null; // invariant violated: reject the load.

            // Resolve the active key from the registry.
            // spec: login_flow.md §2.0 Tier 2 (HKLM\SOFTWARE\crspace\do : servername, REG_SZ).
            var registryName = TryReadRegistryServerName();

            // Find the matching record by CP949 server name at record offset +0.
            // spec: lobby.yaml §RECORD SHAPE C (+0 string = CP949 server NAME, match key).
            for (uint i = 0; i < count; i++)
            {
                var recordStart = ListDatHeaderSize + (int)(i * ListDatRecordSize);

                // The CP949 name fills [recordStart .. recordStart+256) (up to NUL).
                // spec: lobby.yaml §RECORD SHAPE C (+0 CP949 server NAME).
                var recordName = ReadNulTerminatedCp949(data, recordStart, ListDatHostOffset);

                var matches = registryName is not null
                    ? string.Equals(recordName, registryName, StringComparison.Ordinal)
                    : false; // when registry is missing, fall through to Tier 3.

                if (!matches)
                    continue;

                // Host string at record offset +256. spec: lobby.yaml §RECORD SHAPE C (+256 lobby host string).
                var host = ReadNulTerminatedCp949(
                    data,
                    recordStart + ListDatHostOffset,
                    ListDatRecordSize - ListDatHostOffset);

                return string.IsNullOrWhiteSpace(host) ? null : host;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Reads the <c>servername</c> REG_SZ value from <c>HKLM\SOFTWARE\crspace\do</c>.
    ///     Returns <see langword="null" /> when the key or value is absent or on a non-Windows platform.
    ///     spec: Docs/RE/specs/login_flow.md §2.0 Tier 2 (registry key HKLM\SOFTWARE\crspace\do, value servername REG_SZ).
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? TryReadRegistryServerName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, false);
            return key?.GetValue(RegistryServerNameValue) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Decodes a NUL-terminated CP949 string from <paramref name="data" /> starting at
    ///     <paramref name="offset" />, reading at most <paramref name="maxBytes" /> bytes.
    /// </summary>
    private static string ReadNulTerminatedCp949(byte[] data, int offset, int maxBytes)
    {
        var end = offset;
        var limit = Math.Min(offset + maxBytes, data.Length);
        while (end < limit && data[end] != 0)
            end++;

        var length = end - offset;
        return length <= 0 ? string.Empty : Cp949.GetString(data, offset, length);
    }

    private static Encoding CreateCp949()
    {
        // spec: Docs/RE/specs/login_flow.md §7; lobby.yaml §RECORD SHAPE C (CP949 server name).
        // All game text is CP949 (Korean). Register the provider once.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }
}