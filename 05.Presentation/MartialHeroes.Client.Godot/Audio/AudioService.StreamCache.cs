
using Godot;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService
{

    private AudioStreamOggVorbis? GetOrLoadStream2d(uint id)
    {
        if (_streamCache2d.TryGetValue(id, out var cached))
            return cached;

        if (!_vfsAvailable || _assets is null)
        {
            GD.Print($"[AudioService] [headless-proof] 2D stream not loaded (no VFS): data/sound/2d/{id}.ogg");
            return null;
        }

        var vfsPath = $"data/sound/2d/{id}.ogg";
        var stream = LoadOggFromVfs(vfsPath);

        if (stream is not null)
        {
            _streamCache2d[id] = stream;
            GD.Print($"[AudioService] Cached 2D stream: id={id} vfs='{vfsPath}'.");
        }
        else
        {
            _streamCache2d[id] = null;
            GD.Print($"[AudioService] 2D stream absent in VFS: '{vfsPath}'.");
        }

        return stream;
    }

    private AudioStreamOggVorbis? LoadOggFromVfs(string vfsPath)
    {
        try
        {
            var raw = _assets!.GetRaw(vfsPath);
            if (raw.IsEmpty) return null;

            var bytes = raw.ToArray();

            var stream = AudioStreamOggVorbis.LoadFromBuffer(bytes);
            return stream;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioService] OGG load failed for '{vfsPath}': {ex.Message}");
            return null;
        }
    }
}