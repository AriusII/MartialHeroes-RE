namespace MartialHeroes.Client.Application.Login;

/// <summary>
///     Contract for the lobby host resolution chain that mirrors the original client's three-tier
///     lookup used before any lobby TCP connection is opened.
/// </summary>
/// <remarks>
///     <para>
///         The original resolves the lobby host in strict priority order (first hit wins):
///         <list type="number">
///             <item>
///                 <description>
///                     <b>Tier 1 — <c>ip.txt</c> override.</b> If the file is present in the working directory,
///                     reads a single whitespace-free token (truncated to 19 characters) and uses it as the lobby IP.
///                     spec: Docs/RE/specs/login_flow.md §2.0 Tier 1; Docs/RE/specs/frontend_layout_tables.md §8.
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <b>Tier 2 — <c>list.dat</c> CIPList keyed by registry server name.</b> If <c>ip.txt</c> is
///                     absent, loads the loose client-root file <c>list.dat</c>; the active record is selected by the
///                     registry value <c>HKLM\SOFTWARE\crspace\do : servername</c> (REG_SZ); the selected record's
///                     host is read at record offset +256. File invariant: internal length == 768 × count + 4.
///                     spec: Docs/RE/specs/login_flow.md §2.0 Tier 2; Docs/RE/packets/lobby.yaml §RECORD SHAPE C.
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <b>Tier 3 — hardcoded fallback.</b> If <c>ip.txt</c> is absent <em>and</em> the
///                     <c>list.dat</c> load fails, falls back to <c>211.196.150.4</c>.
///                     spec: Docs/RE/specs/login_flow.md §2.0 Tier 3; Docs/RE/specs/frontend_layout_tables.md §8.
///                 </description>
///             </item>
///         </list>
///         The resolved host is a <b>dotted-decimal IPv4 literal</b> — the lobby socket uses
///         <c>inet_addr</c> (no DNS). spec: Docs/RE/specs/login_flow.md §2.0 item 3.
///     </para>
///     <para>
///         <b>Implemented by:</b> <c>Client.Infrastructure</c> (file + registry I/O). The
///         Application layer declares this seam; Infrastructure provides the concrete class.
///     </para>
/// </remarks>
public interface ILobbyHostResolver
{
    /// <summary>
    ///     Resolves the lobby host IP string by running the three-tier lookup (ip.txt →
    ///     list.dat/registry → hardcoded fallback) and returns the result.
    /// </summary>
    /// <returns>
    ///     A dotted-decimal IPv4 address string that names the lobby host, never null or empty.
    ///     The fallback <c>"211.196.150.4"</c> is returned when neither override tier succeeds.
    ///     spec: Docs/RE/specs/login_flow.md §2.0.
    /// </returns>
    string Resolve();
}