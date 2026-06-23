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

internal sealed class RelayInputHandler : IInputHandler
{
    private volatile IInputHandler? _target;

    public bool TryHandle(in InputEvent e)
    {
        return _target?.TryHandle(in e) ?? false;
    }

    public void SetTarget(IInputHandler target)
    {
        _target = target;
    }
}

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
        GD.Print($"[Net←] {(ushort)(packedOpcode >> 16)}/{(ushort)(packedOpcode & 0xFFFF)} payload={payload.Length}B");

        var totalSize = 8 + payload.Length;
        Span<byte> frame = stackalloc byte[totalSize <= 256 ? totalSize : 0];
        var heapFrame = totalSize > 256 ? new byte[totalSize] : null;
        var dest = heapFrame is not null ? heapFrame.AsSpan() : frame;

        BinaryPrimitives.WriteUInt32LittleEndian(dest[..], (uint)totalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], (ushort)(packedOpcode >> 16));
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


internal sealed class RelayEnterWorldEmitter
{
    private volatile Func<byte, CancellationToken, ValueTask>? _target;

    public ValueTask Invoke(byte slotIndex, CancellationToken cancellationToken)
    {
        return _target is { } t ? t(slotIndex, cancellationToken) : ValueTask.CompletedTask;
    }

    public void SetTarget(Func<byte, CancellationToken, ValueTask> target)
    {
        _target = target;
    }
}

internal sealed class GodotLoadingSoundSink : ILoadingSoundSink
{
    public void PlayLooping(int soundCueId)
    {
        if (AudioService.Instance is { } audio)
        {
            audio.CallDeferred(AudioService.MethodName.StartBgm, (uint)soundCueId);
            GD.Print($"[ClientContext] Loading sound sink requested looping cue {soundCueId}.");
            return;
        }

        GD.Print($"[ClientContext] Loading sound sink: AudioService unavailable; cue {soundCueId} skipped.");
    }
}