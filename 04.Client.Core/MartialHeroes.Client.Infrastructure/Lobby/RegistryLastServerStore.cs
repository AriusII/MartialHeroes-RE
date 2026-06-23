using MartialHeroes.Client.Application.Login;
using Microsoft.Win32;

namespace MartialHeroes.Client.Infrastructure.Lobby;

public sealed class RegistryLastServerStore : ILastServerStore
{
    private const string WriteKeyPath = @"software\crspace\do";
    private const string ReadKeyPath = @"SOFTWARE\crspace\do";
    private const string LastServerValueName = "Lastserver";

    public void Save(ushort serverId)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(WriteKeyPath, true);
            key.SetValue(LastServerValueName, (int)serverId, RegistryValueKind.DWord);
        }
        catch
        {
        }
    }

    public ushort Load()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ReadKeyPath, false);
            if (key?.GetValue(LastServerValueName) is int raw && raw > 0)
                return (ushort)raw;
        }
        catch
        {
        }

        return 0;
    }
}