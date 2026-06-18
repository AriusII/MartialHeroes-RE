namespace MartialHeroes.Client.Application.Login;

/// <summary>
/// Contract for persisting and reading the most-recently-selected lobby server id, mirroring the
/// original client's use of registry value <c>HKLM\SOFTWARE\crspace\do : Lastserver</c>
/// (REG_DWORD).
/// </summary>
/// <remarks>
/// <para>
/// When the player selects a server and the commit guard passes
/// (<c>status_code == 0 &amp;&amp; load &lt; 2400</c>), the selected server id is written to the
/// registry so that the next session re-highlights the same server on the list.
/// spec: Docs/RE/specs/login_flow.md §2.0 Registry note; §2.1 (Lastserver persisted on channel-endpoint
/// fetch); Docs/RE/specs/frontend_layout_tables.md §2.2 sub-state 37; Docs/RE/packets/lobby.yaml
/// §RECORD SHAPE B coupling note.
/// </para>
/// <para>
/// The key path is <c>HKLM\SOFTWARE\crspace\do</c>. The spec notes that the write path uses the
/// lowercase <c>software\crspace\do</c> spelling and read paths use <c>SOFTWARE\crspace\do</c>; the
/// Windows registry is case-insensitive so they address the same key.
/// spec: Docs/RE/specs/login_flow.md §2.0 Registry note.
/// </para>
/// <para>
/// <b>Implemented by:</b> <c>Client.Infrastructure</c> (Windows registry I/O). An in-memory stub
/// can be injected in headless/cross-platform tests.
/// </para>
/// </remarks>
public interface ILastServerStore
{
    /// <summary>
    /// Persists <paramref name="serverId"/> as the remembered last-selected lobby server id.
    /// No-op on any platform that does not support the registry (the caller must not throw on
    /// failure). spec: Docs/RE/specs/login_flow.md §2.0 Registry note (Lastserver REG_DWORD);
    /// Docs/RE/packets/lobby.yaml §RECORD SHAPE B coupling note.
    /// </summary>
    /// <param name="serverId">The selected server id (values 1..40).</param>
    void Save(ushort serverId);

    /// <summary>
    /// Reads the remembered last-selected lobby server id, or returns <c>0</c> when the key is
    /// absent or the platform does not support the registry. spec: Docs/RE/specs/login_flow.md §2.0.
    /// </summary>
    /// <returns>The previously saved server id, or <c>0</c> when unset.</returns>
    ushort Load();
}