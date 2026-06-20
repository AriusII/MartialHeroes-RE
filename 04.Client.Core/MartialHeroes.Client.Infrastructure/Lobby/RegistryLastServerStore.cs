using MartialHeroes.Client.Application.Login;
using Microsoft.Win32;

namespace MartialHeroes.Client.Infrastructure.Lobby;

/// <summary>
///     Concrete implementation of <see cref="ILastServerStore" /> that reads and writes the
///     <c>Lastserver</c> REG_DWORD value under <c>HKLM\SOFTWARE\crspace\do</c> — the exact registry
///     path the original client uses to persist the most-recently-selected lobby server id.
/// </summary>
/// <remarks>
///     <para>
///         All registry operations are best-effort: a failure (e.g. permission denied, non-Windows
///         platform) is silently swallowed; <see cref="Save" /> becomes a no-op and <see cref="Load" />
///         returns 0. The caller must not rely on a throw on failure.
///     </para>
///     <para>
///         The write path uses the lowercase spelling <c>software\crspace\do</c> as observed in the
///         original (the registry is case-insensitive so it addresses the same key).
///         spec: Docs/RE/specs/login_flow.md §2.0 Registry note; Docs/RE/packets/lobby.yaml
///         §RECORD SHAPE B coupling note.
///     </para>
/// </remarks>
public sealed class RegistryLastServerStore : ILastServerStore
{
    // spec: Docs/RE/specs/login_flow.md §2.0 Registry note — the write uses lowercase, reads use uppercase;
    //   the registry is case-insensitive, so both paths address HKLM\SOFTWARE\crspace\do.
    private const string WriteKeyPath = @"software\crspace\do"; // spec: login_flow.md §2.0.
    private const string ReadKeyPath = @"SOFTWARE\crspace\do"; // spec: login_flow.md §2.0.
    private const string LastServerValueName = "Lastserver"; // spec: login_flow.md §2.1; lobby.yaml §RECORD SHAPE B.

    /// <inheritdoc />
    public void Save(ushort serverId)
    {
        if (!OperatingSystem.IsWindows())
            return; // Registry is Windows-only; silently no-op on other platforms.

        try
        {
            // The write path uses lowercase key spelling, matching the original.
            // spec: Docs/RE/specs/login_flow.md §2.0 Registry note.
            using var key = Registry.LocalMachine.CreateSubKey(WriteKeyPath, true);
            // REG_DWORD stores a 32-bit value; serverId fits in the low 16 bits.
            // spec: login_flow.md §2.1 (Lastserver REG_DWORD).
            key.SetValue(LastServerValueName, (int)serverId, RegistryValueKind.DWord);
        }
        catch
        {
            // Best-effort: silently ignore permission errors.
        }
    }

    /// <inheritdoc />
    public ushort Load()
    {
        if (!OperatingSystem.IsWindows())
            return 0; // Registry is Windows-only; return unset sentinel on other platforms.

        try
        {
            // Read path uses the uppercase spelling; the registry is case-insensitive.
            // spec: Docs/RE/specs/login_flow.md §2.0 Registry note.
            using var key = Registry.LocalMachine.OpenSubKey(ReadKeyPath, false);
            if (key?.GetValue(LastServerValueName) is int raw && raw > 0)
                return (ushort)raw;
        }
        catch
        {
            // Best-effort: silently ignore.
        }

        return 0;
    }
}