namespace MartialHeroes.Network.Abstractions.Protocol;

/// <summary>
/// Canonical dispatch seam for typed, decoded packet views produced by
/// <c>Network.Protocol</c>'s <c>PacketRouter</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Implemented by:</b> <c>Client.Application</c>. The application layer registers one or
/// more typed handler methods that the router calls after reinterpreting the raw payload bytes
/// as a fixed-size wire struct (via <c>MemoryMarshal.AsRef&lt;T&gt;</c>; no copy, no allocation).
/// </para>
/// <para>
/// <b>Migration path from <c>Network.Protocol</c>'s local seam:</b>
/// <c>Network.Protocol.Routing.IPacketHandler</c> currently lives inside the Protocol project
/// as a temporary seam (it carries a TODO to migrate here). The shapes are aligned:
/// <list type="bullet">
///   <item>Both expose typed <c>Handle(in T packet)</c> overloads for each specced struct.</item>
///   <item>Both expose <c>OnUnhandled(uint packedOpcode, ReadOnlySpan&lt;byte&gt; payload)</c>
///   for opcodes without a specced struct.</item>
/// </list>
/// To migrate: add a <c>ProjectReference</c> from <c>Network.Protocol</c> to
/// <c>Network.Abstractions</c>, change <c>PacketRouter.Route</c> to accept
/// <c>MartialHeroes.Network.Abstractions.Protocol.IPacketHandler</c> instead of the local
/// type, and delete the local interface. No change is required in <c>Client.Application</c>
/// because it will implement this interface directly.
/// </para>
/// <para>
/// <b>Extending for new packets:</b> each time a new specced struct is added to
/// <c>Network.Protocol</c>, add a corresponding typed overload here. The source-generated
/// router emits the matching <c>Handle(in T)</c> call at compile-time.
/// </para>
/// <para>
/// All parameters are pass-by-ref-readonly (no copy) or span-only (stack-bound). Implementors
/// must not store the <paramref name="payload"/> span across the call boundary.
/// </para>
/// </remarks>
public interface IPacketHandler
{
    /// <summary>
    /// Called for every opcode that does not yet have a specced fixed-size struct, or for
    /// opcodes that the current version of the router does not recognise.
    /// </summary>
    /// <param name="packedOpcode">
    /// The <c>(major &lt;&lt; 16) | minor</c> packed opcode. Matches the constants in
    /// <c>Network.Protocol.Opcodes.Opcodes</c>.
    /// </param>
    /// <param name="payload">
    /// The raw, decrypted payload slice (header stripped). Zero-copy; must not be stored or
    /// read after this method returns.
    /// </param>
    void OnUnhandled(uint packedOpcode, ReadOnlySpan<byte> payload);
}
