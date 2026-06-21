using System.Buffers.Binary;
using Godot;
using MartialHeroes.Assets.Vfs;
using MartialHeroes.Client.Application.Assets;
using MartialHeroes.Client.Application.Ingestion;
using MartialHeroes.Client.Application.Input;
using MartialHeroes.Client.Godot.Audio;
using MartialHeroes.Network.Abstractions.Protocol;
using MartialHeroes.Network.Abstractions.Session;
using InputEvent = MartialHeroes.Client.Application.Input.InputEvent;

namespace MartialHeroes.Client.Godot.Autoload;
// -------------------------------------------------------------------------
// Internal adapter types shared across ClientContext partial files.
// These were formerly file-scoped (file sealed) in the original monolithic
// ClientContext.cs. They are promoted to internal so they are accessible from
// ClientContext.ApplicationGraph.cs (construction) and
// ClientContext.NetworkSetup.cs (DispatcherFrameSink usage).
// -------------------------------------------------------------------------

/// <summary>
///     A late-binding <see cref="MartialHeroes.Client.Application.Input.IInputHandler" /> relay whose
///     target can be set after construction.
///     Used to defer the world input handler assignment until InputRouter is initialised.
///     spec: Docs/RE/specs/input_ui.md §3 — world handler registered after UI in the bus.
/// </summary>
internal sealed class RelayInputHandler : IInputHandler
{
    private volatile IInputHandler? _target;

    /// <inheritdoc />
    public bool TryHandle(in InputEvent e)
    {
        return _target?.TryHandle(in e) ?? false;
    }

    /// <summary>Sets the delegate handler. Thread-safe (volatile write).</summary>
    public void SetTarget(IInputHandler target)
    {
        _target = target;
    }
}

/// <summary>
///     Adapter that bridges the transport-layer <see cref="MartialHeroes.Network.Abstractions.Protocol.IFrameSink" />
///     contract to the application-layer
///     <see cref="MartialHeroes.Client.Application.Ingestion.InboundFrameDispatcher.Enqueue" />
///     method.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="MartialHeroes.Client.Application.Ingestion.InboundFrameDispatcher" /> does not implement
///         <see cref="MartialHeroes.Network.Abstractions.Protocol.IFrameSink" /> directly
///         because it operates on a full frame (header + payload) while
///         <see cref="MartialHeroes.Network.Abstractions.Protocol.IFrameSink.OnFrame" />
///         receives only the payload with the opcode separated. This adapter packs the opcode back into an
///         8-byte wire header ahead of the payload before enqueuing, which is what
///         <see cref="MartialHeroes.Client.Application.Ingestion.InboundFrameDispatcher.Enqueue" /> expects
///         (the channel contains full frames).
///     </para>
///     <para>
///         The packed header written here is the same 8-byte layout the FrameSplitter already parsed:
///         +0 u32 LE totalSize, +4 u16 LE major, +6 u16 LE minor.
///         spec: Docs/RE/specs/crypto.md §2 — wire frame header layout.
///     </para>
/// </remarks>
internal sealed class DispatcherFrameSink : IFrameSink
{
    private readonly InboundFrameDispatcher _dispatcher;

    public DispatcherFrameSink(InboundFrameDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public void OnFrame(SessionId sessionId, uint packedOpcode,
        ReadOnlySpan<byte> payload)
    {
        // Diagnostic trace of every inbound opcode (CYCLE 4 live-loop instrument). spec: opcodes.md.
        GD.Print($"[Net←] {(ushort)(packedOpcode >> 16)}/{(ushort)(packedOpcode & 0xFFFF)} payload={payload.Length}B");

        // Reconstruct the 8-byte header + payload frame that InboundFrameDispatcher.Enqueue expects.
        // spec: Docs/RE/specs/crypto.md §2 — +0 u32 LE size (total incl. header), +4 u16 major, +6 u16 minor.
        var totalSize = 8 + payload.Length; // spec: crypto.md §2 — size includes the 8-byte header
        Span<byte> frame = stackalloc byte[totalSize <= 256 ? totalSize : 0]; // stack for small frames
        var heapFrame = totalSize > 256 ? new byte[totalSize] : null;
        var dest = heapFrame is not null ? heapFrame.AsSpan() : frame;

        // +0: u32 LE total size. spec: Docs/RE/specs/crypto.md §2 [CODE-CONFIRMED u32].
        BinaryPrimitives.WriteUInt32LittleEndian(dest[..], (uint)totalSize);
        // +4: u16 LE major opcode. spec: Docs/RE/opcodes.md.
        BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], (ushort)(packedOpcode >> 16));
        // +6: u16 LE minor opcode. spec: Docs/RE/opcodes.md.
        BinaryPrimitives.WriteUInt16LittleEndian(dest[6..], (ushort)(packedOpcode & 0xFFFF));
        if (!payload.IsEmpty)
            payload.CopyTo(dest[8..]);

        _dispatcher.Enqueue(dest);
    }
}

internal sealed class VfsLoadResourceSource : ILoadResourceSource
{
    private readonly VfsResourcePipeline? _pipeline;

    public VfsLoadResourceSource(VfsResourcePipeline? pipeline)
    {
        _pipeline = pipeline;
    }

    public ValueTask<long> LoadAsync(string logicalPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pipeline is null) return ValueTask.FromResult(0L);

        try
        {
            var bytes = _pipeline.OpenRead(logicalPath);
            return ValueTask.FromResult((long)bytes.Length);
        }
        catch (Exception ex)
        {
            GD.Print($"[ClientContext] Load resource skipped '{logicalPath}': {ex.Message}");
            return ValueTask.FromResult(0L);
        }
    }
}

// Note: AssembledCellViewAdapter (the public layer-05 adapter used by the bake delegate in
// ClientContext.ApplicationGraph.cs) is defined in Adapters/AssembledCellViewAdapter.cs.
// It was moved out of the original ClientContext.cs in CYCLE 2 Phase 2-A so that
// RealWorldRenderer can down-cast to the concrete type for 9-slot access without reflection.
// spec: Docs/RE/specs/assembly_graph.md §4 — layer-05 composition root adapts AssembledCell as IAssembledCellView.

internal sealed class GodotLoadingSoundSink : ILoadingSoundSink
{
    public void PlayLooping(int soundCueId)
    {
        // Loading cue 920100100 is a category-0 looping BGM. Route through AudioService's BGM slot
        // so the next front-end BGM replaces it cleanly.
        // spec: Docs/RE/specs/sound.md §15.6a; frontend_scenes.md §9.1.
        if (AudioService.Instance is { } audio)
        {
            audio.CallDeferred(AudioService.MethodName.StartBgm, (uint)soundCueId);
            GD.Print($"[ClientContext] Loading sound sink requested looping cue {soundCueId}.");
            return;
        }

        GD.Print($"[ClientContext] Loading sound sink: AudioService unavailable; cue {soundCueId} skipped.");
    }
}