using System.Runtime.Versioning;
using System.Text;
using MartialHeroes.Client.Application.Login;
using Microsoft.Win32;

namespace MartialHeroes.Client.Infrastructure.Lobby;

public sealed class LobbyHostResolver(string? clientRoot = null) : ILobbyHostResolver
{
    private const string FallbackHost = "211.196.150.4";

    private const string IpTxtFileName = "ip.txt";
    private const int IpTxtMaxLength = 19;

    private const string ListDatFileName = "list.dat";
    private const int ListDatRecordSize = 768;
    private const int ListDatHostOffset = 256;
    private const int ListDatHeaderSize = 4;

    private const string RegistryKeyPath = @"SOFTWARE\crspace\do";
    private const string RegistryServerNameValue = "servername";

    private static readonly Encoding Cp949 = CreateCp949();

    private readonly string _clientRoot = string.IsNullOrWhiteSpace(clientRoot)
        ? Directory.GetCurrentDirectory()
        : clientRoot;

    public string Resolve()
    {
        var ipTxtPath = Path.Combine(_clientRoot, IpTxtFileName);
        if (File.Exists(ipTxtPath))
        {
            var host = TryReadIpTxt(ipTxtPath);
            if (!string.IsNullOrWhiteSpace(host))
                return host;
        }

        if (!OperatingSystem.IsWindows()) return FallbackHost;
        {
            var listDatPath = Path.Combine(_clientRoot, ListDatFileName);
            if (!File.Exists(listDatPath)) return FallbackHost;
            var host = TryReadListDat(listDatPath);
            if (!string.IsNullOrWhiteSpace(host))
                return host;
        }

        return FallbackHost;
    }


    private static string? TryReadIpTxt(string path)
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.ASCII).Trim();
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

            if (token.Length > IpTxtMaxLength)
                token = token[..IpTxtMaxLength];

            return token;
        }
        catch
        {
            return null;
        }
    }


    [SupportedOSPlatform("windows")]
    private static string? TryReadListDat(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < ListDatHeaderSize)
                return null;

            var count = BitConverter.ToUInt32(data, 0);

            var expectedSize = (long)ListDatRecordSize * count + ListDatHeaderSize;
            if (data.Length != expectedSize)
                return null;

            var registryName = TryReadRegistryServerName();

            for (uint i = 0; i < count; i++)
            {
                var recordStart = ListDatHeaderSize + (int)(i * ListDatRecordSize);

                var recordName = ReadNulTerminatedCp949(data, recordStart, ListDatHostOffset);

                var matches = registryName is not null &&
                              string.Equals(recordName, registryName, StringComparison.Ordinal);

                if (!matches)
                    continue;

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
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }
}