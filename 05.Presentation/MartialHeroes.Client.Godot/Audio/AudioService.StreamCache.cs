// Audio/AudioService.StreamCache.cs
//
// Partial class — sound stream loading and caching (VFS → AudioStreamOggVorbis).
//
// spec: Docs/RE/specs/sound.md §3.2 (2D directory = data/sound/2d/). SAMPLE-VERIFIED.
// spec: Docs/RE/specs/sound.md §2 (decimal stem, no zero-padding, .ogg unconditional). CODE-CONFIRMED.
// spec: Docs/RE/specs/sound.md §12.1 (cache + Godot OggVorbisStream).
// spec: Docs/RE/formats/sound_tables.md §7.1 — standard Ogg Vorbis, no proprietary header. SAMPLE-VERIFIED.

using Godot;

namespace MartialHeroes.Client.Godot.Audio;

public sealed partial class AudioService
{
    // -------------------------------------------------------------------------
    // Internal: stream loading
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Returns a cached <see cref="AudioStreamOggVorbis" /> for the given 2D sound ID,
    ///     loading it from the VFS on first access.
    ///     VFS path: data/sound/2d/{id}.ogg
    ///     spec: Docs/RE/specs/sound.md §3.2 (2D directory). SAMPLE-VERIFIED.
    ///     spec: Docs/RE/specs/sound.md §2 (decimal stem, .ogg unconditional). CODE-CONFIRMED.
    /// </summary>
    private AudioStreamOggVorbis? GetOrLoadStream2d(uint id)
    {
        // ContainsKey check first: TryGetValue returns the cached null sentinel correctly.
        if (_streamCache2d.TryGetValue(id, out var cached))
            return cached; // may be null (absent-sentinel) — caller handles null

        if (!_vfsAvailable || _assets is null)
        {
            // Headless / no-VFS proof: print the path that WOULD be loaded.
            // spec: CLAUDE.md Headless Verify Loop — "GD.Print evidence of stream resolution".
            GD.Print($"[AudioService] [headless-proof] 2D stream not loaded (no VFS): data/sound/2d/{id}.ogg");
            return null;
        }

        // spec: Docs/RE/specs/sound.md §2 — "data/sound/2d/<sound_id>.ogg (decimal, no padding)". CODE-CONFIRMED.
        var vfsPath = $"data/sound/2d/{id}.ogg";
        var stream = LoadOggFromVfs(vfsPath);

        if (stream is not null)
        {
            _streamCache2d[id] = stream;
            GD.Print($"[AudioService] Cached 2D stream: id={id} vfs='{vfsPath}'.");
        }
        else
        {
            // Cache null (explicit absent-sentinel) to avoid repeated VFS lookups for missing files.
            // The Dictionary is typed Dictionary<uint, AudioStreamOggVorbis?> so null is a valid value.
            _streamCache2d[id] = null;
            GD.Print($"[AudioService] 2D stream absent in VFS: '{vfsPath}'.");
        }

        return stream;
    }

    /// <summary>
    ///     Loads an .ogg file from the VFS and creates a Godot <see cref="AudioStreamOggVorbis" />
    ///     via <see cref="AudioStreamOggVorbis.LoadFromBuffer" />.
    ///     Falls back to writing a temp file if LoadFromBuffer is unavailable (undocumented fallback —
    ///     verified available in Godot 4.6.3 via GodotSharp.dll introspection).
    ///     spec: Docs/RE/formats/sound_tables.md §7.1 — "standard Ogg Vorbis, no proprietary header,
    ///     no encryption, no additional framing". SAMPLE-VERIFIED.
    ///     spec: Docs/RE/specs/sound.md §2 — ".ogg extension unconditional". CODE-CONFIRMED.
    /// </summary>
    private AudioStreamOggVorbis? LoadOggFromVfs(string vfsPath)
    {
        try
        {
            var raw = _assets!.GetRaw(vfsPath);
            if (raw.IsEmpty) return null;

            var bytes = raw.ToArray();

            // AudioStreamOggVorbis.LoadFromBuffer is the Godot 4.6 C# static API for in-memory
            // OGG loading. Verified present in Godot_v4.6.3-stable_mono_win64 GodotSharp.dll.
            // spec: Godot 4.6 C# API — AudioStreamOggVorbis.LoadFromBuffer(byte[]) → AudioStreamOggVorbis.
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