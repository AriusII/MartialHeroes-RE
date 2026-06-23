using System.Runtime.InteropServices;
using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

// EffectRenderer — partial: effect cast / spawn / stop + xeffect.lst registry + .xeff loader.
// spec: Docs/RE/specs/effects.md §15 — skill-cast effect chain.
public sealed partial class EffectRenderer
{
    // ─────────────────────────────────────────────────────────────────────────
    // Effect registry builder
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Builds (or re-uses) the effect_id → vfs-path registry from xeffect.lst.
    ///     Reads the manifest: u32 count, then count × char[30] CP949 NUL-padded names.
    ///     For each name, opens the corresponding .xeff file and reads its header first u32 as effect_id.
    ///     spec: Docs/RE/formats/effects.md §C.2 — registry keyed by header effect_id; CONFIRMED.
    ///     spec: Docs/RE/formats/effects.md §A.9 — xeffect.lst format; HIGH confidence.
    ///     spec: Docs/RE/formats/effects.md §A.14 — XEFF_LST_NAME_LEN = 30.
    ///     Returns null if VFS unavailable or xeffect.lst is absent.
    /// </summary>
    private Dictionary<uint, string>? BuildEffectRegistry(RealClientAssets assets)
    {
        if (_registryBuildAttempted) return _effectRegistry;
        _registryBuildAttempted = true;

        var lstRaw = assets.GetRaw(XeffectLstPath);
        if (lstRaw.IsEmpty)
        {
            GD.Print($"[EffectRenderer] xeffect.lst not found in VFS ({XeffectLstPath}) — " +
                     "effect registry unavailable; numeric-path fallback will be used.");
            return null;
        }

        var span = lstRaw.Span;
        if (span.Length < 4)
        {
            GD.PrintErr($"[EffectRenderer] xeffect.lst too short ({span.Length} bytes) — skipping registry build.");
            return null;
        }

        // u32 LE count — number of name records.
        // spec: Docs/RE/formats/effects.md §A.9 — "u32 count" at offset 0.
        var count = MemoryMarshal.Read<uint>(span[..4]);
        var expectedLen = 4 + (int)count * XeffLstNameLen;
        if (span.Length < expectedLen)
        {
            GD.PrintErr($"[EffectRenderer] xeffect.lst size mismatch: have {span.Length} bytes, " +
                        $"need {expectedLen} for {count} records — truncated manifest.");
            // Proceed with as many records as fit.
            count = (uint)((span.Length - 4) / XeffLstNameLen);
        }

        // CP949 encoding for NUL-padded name records.
        // spec: Docs/RE/formats/effects.md §A.9 — names are ASCII/CP949; all text is CP949.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        var registry = new Dictionary<uint, string>((int)count);
        var mapped = 0;
        var skipped = 0;

        for (uint i = 0; i < count; i++)
        {
            var offset = 4 + (int)i * XeffLstNameLen;
            var nameBytes = span.Slice(offset, XeffLstNameLen);

            // Trim NUL padding.
            var nullPos = nameBytes.IndexOf((byte)0);
            if (nullPos == 0)
            {
                skipped++;
                continue;
            }

            var trimmed = nullPos > 0 ? nameBytes[..nullPos] : nameBytes;

            string name;
            try
            {
                name = cp949.GetString(trimmed);
            }
            catch
            {
                skipped++;
                continue;
            }

            // Build the VFS path: "data/effect/xeff/<name>"
            // spec: Docs/RE/formats/effects.md §C.2 — boot loader: "data/effect/xeff/<name>" concat.
            var vfsPath = $"data/effect/xeff/{name}";

            // Read the .xeff header to extract effect_id (first u32).
            // spec: Docs/RE/formats/effects.md §A.2 — effect_id u32 at offset 0; CONFIRMED.
            var xeffRaw = assets.GetRaw(vfsPath);
            if (xeffRaw.IsEmpty)
            {
                skipped++;
                continue;
            }

            var xeffSpan = xeffRaw.Span;
            if (xeffSpan.Length < 4)
            {
                skipped++;
                continue;
            }

            var effectId = MemoryMarshal.Read<uint>(xeffSpan[..4]);

            // Reject the anti-magic sentinel (0x46464558 = "XEFF" ASCII).
            // spec: Docs/RE/formats/effects.md §A.2 — XEFF_INVALID_MAGIC = 0x46464558; CONFIRMED.
            if (effectId == 0x46464558u)
            {
                skipped++;
                continue;
            }

            // On duplicate effect_id, keep the first entry (matches likely runtime "first insert wins").
            if (!registry.ContainsKey(effectId))
            {
                registry[effectId] = vfsPath;
                mapped++;
            }
        }

        _effectRegistry = registry;
        GD.Print($"[EffectRenderer] Effect registry built from xeffect.lst: {mapped} effect_ids mapped " +
                 $"({skipped} skipped / duplicates). spec: Docs/RE/formats/effects.md §C.2 / §A.9.");
        return registry;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API — PlayCast / StopCast
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Spawns a looping actor-anchored effect for the given cast.
    ///     Attempts to load and render the real .xeff; renders nothing when unavailable (no-placeholder doctrine).
    ///     Called when action code 0xC8 (cast-enable) is received.
    ///     spec: Docs/RE/specs/effects.md §15.3 — 0xC8 = cast-enable; CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/effects.md §15.4 — looping UserXEffect, actor-anchored; CODE-CONFIRMED.
    /// </summary>
    /// <param name="actor">The caster's scene node.</param>
    /// <param name="effectId">
    ///     cast_effect_id from the skill record (byte offset 1136).
    ///     spec: Docs/RE/specs/effects.md §15.2 — cast_effect_id at byte offset 1136; CODE-CONFIRMED.
    ///     The numeric .xeff filename = decimal(effectId).
    ///     spec: Docs/RE/formats/effects.md §A.2 — filename = decimal(effect_id); SAMPLE-VERIFIED.
    /// </param>
    public void PlayCast(Node3D actor, uint effectId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var key = ResolveActorKey(actor);
        StopCast(actor); // idempotent restart

        // spec: Docs/RE/specs/effects.md §15.4 — effect origin follows caster's world position; CODE-CONFIRMED.
        var origin = actor.GlobalPosition + new Vector3(0f, EmitterHeightOffset, 0f);

        // Attempt to load and parse the .xeff file.
        var subEffects = TryLoadXeff(effectId);

        LiveEffect live;
        if (subEffects is { Length: > 0 })
        {
            // Build one MeshInstance3D per CPU sub-effect (billboard/mesh geometry)
            // and one GpuParticleSimNode per GPU-particle sub-effect.
            var meshInstances = new MeshInstance3D?[subEffects.Length];
            var simNodes = new GpuParticleSimNode?[subEffects.Length];
            var textures = new ImageTexture?[subEffects.Length][];

            for (var i = 0; i < subEffects.Length; i++)
            {
                var se = subEffects[i];
                // Coalesce to empty: the downstream mesh builders treat null and empty arrays
                // identically (all gated by `Length > 0`), so this preserves behaviour and keeps
                // the array element type honest (silences CS8601).
                textures[i] = LoadSubEffectTextures(se) ?? Array.Empty<ImageTexture?>();

                if (se.ResourceId >= XeffResourceParticleThreshold)
                {
                    // GPU particle element (resource_id >= 10000): render via GpuParticleSimNode,
                    // which runs stepwise Euler integration from the particleEmitter.eff descriptor.
                    // spec: Docs/RE/specs/effects.md §17.2 — resource_id >= 10000 → GPU particle; CONFIRMED.
                    // spec: Docs/RE/formats/effects.md §E.2.2 — per-particle Euler integration: CODE-CONFIRMED.
                    // spec: Docs/RE/formats/effects.md §E.4 — raw entry_id equality lookup: CONFIRMED.
                    var simNode = TryBuildParticleSimNode(se.ResourceId, origin);
                    if (simNode is not null)
                    {
                        simNodes[i] = simNode;
                        AddChild(simNode);
                    }
                }
                else
                {
                    // CPU billboard or mesh: build initial ArrayMesh.
                    meshInstances[i] = BuildSubEffectMesh(se, origin, textures[i], 0);
                    if (meshInstances[i] is not null)
                        AddChild(meshInstances[i]!);
                }
            }

            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = actor,
                SubEffects = subEffects,
                MeshInstances = meshInstances,
                // GpuParticles: null (field left default) — the legacy GpuParticles3D placeholder
                // is fully superseded by SimNodes (GpuParticleSimNode). Teardown guards null.
                SimNodes = simNodes,
                Textures = textures,
                ElapsedMs = 0
            };

            var gpuCount = simNodes.Count(s => s is not null);
            GD.Print($"[EffectRenderer] PlayCast: effectId={effectId} actor={key.RawId} " +
                     $"— loaded real .xeff ({subEffects.Length} sub-effects, {gpuCount} GPU-particle sims) origin={origin}. " +
                     "spec: Docs/RE/specs/effects.md §15.4 looping UserXEffect; CODE-CONFIRMED.");
        }
        else
        {
            // No-placeholder doctrine: when the .xeff file is missing or parse fails, render nothing.
            // A silent LiveEffect (no SubEffects, no Particles) tracks the anchor without emitting.
            // The original engine would simply not draw an effect for an unknown effectId;
            // emitting a synthetic orange burst is a fabrication with zero fidelity value.
            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = actor,
                ElapsedMs = 0
            };

            GD.Print($"[EffectRenderer] PlayCast: effectId={effectId} actor={key.RawId} " +
                     $"— .xeff unavailable or parse failed; rendering nothing (no-placeholder doctrine). origin={origin}.");
        }

        _live[key] = live;
    }

    /// <summary>
    ///     Soft-stops the running cast effect for the given actor.
    ///     Called when action code 0xC9 or 0xCB (cast-disable) is received.
    ///     spec: Docs/RE/specs/effects.md §15.3 — 0xC9/0xCB = cast-disable; CODE-CONFIRMED.
    ///     spec: Docs/RE/specs/effects.md §15.5 — soft-stop: active flag cleared, removed next frame; CODE-CONFIRMED.
    /// </summary>
    public void StopCast(Node3D actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var key = ResolveActorKey(actor);
        if (!_live.Remove(key, out var live))
            return;

        // Soft-stop: clear active flag and begin teardown.
        // spec: Docs/RE/specs/effects.md §15.5 — "soft-stop: active flag cleared; removed next frame".
        live.Active = false;
        TeardownLiveEffect(live);

        GD.Print($"[EffectRenderer] StopCast: actor={key.RawId} effectId={live.EffectId} soft-stopped. " +
                 "spec: Docs/RE/specs/effects.md §15.5 soft-stop; CODE-CONFIRMED.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Map ambient effects — position-anchored UserXEffect (FIX 15a)
    // ─────────────────────────────────────────────────────────────────────────

    // Synthetic ActorKey discriminator for map ambient effects. The legacy MapXEffect spawn
    // (MapXEffect_SpawnFactory_Ambient @0x49e4ef) is anchored at a FIXED world position with an
    // identity orientation — it is NOT actor-anchored. Our LiveEffect.Anchor is a Node3D, so we
    // wrap each descriptor world position in a lightweight static Node3D anchor and key it in the
    // shared _live dictionary using EntitySort.None + the descriptor index as the raw id. Real
    // actors always carry a server-assigned (id, sort) with sort PC/Mob/NPC, so the None-sort key
    // space is collision-free for ambient effects.
    // spec: IDA MapXEffect_SpawnFactory_Ambient @0x49e4ef — UserXEffect_setupTimedWithPos at a4=pos,
    //   identity quat {0,0,0,1}, type tag *(slot+4)=2, descriptor index *(slot+84)=idx.
    private static ActorKey AmbientKey(int descriptorIndex)
    {
        return new ActorKey((uint)descriptorIndex, EntitySort.None);
    }

    /// <summary>
    ///     Spawns a position-anchored ambient map effect (the same .xeff path as a cast effect),
    ///     keyed by the descriptor index. Idempotent: a second call for the same index restarts it.
    ///     Renders nothing when the .xeff is missing or parse fails (no-placeholder doctrine).
    ///     spec: IDA sub_49E5A1 @0x49e6e0 — MapXEffect_SpawnFactory_Ambient on proximity+TOD gate.
    ///     spec: IDA MapXEffect_SpawnFactory_Ambient @0x49e4ef — UserXEffect_setupTimedWithPos reuses
    ///       the .xeff parser + GPU particle sim, identical to a cast effect (just position-anchored).
    /// </summary>
    /// <param name="descriptorIndex">The map<N>.txt descriptor index (stable identity for stop).</param>
    /// <param name="godotPos">Anchor world position in Godot-space (legacy Z already negated).</param>
    /// <param name="effectId">The descriptor's effectId (raw, resolved via the xeffect.lst registry).</param>
    public void PlayAmbient(int descriptorIndex, Vector3 godotPos, uint effectId)
    {
        var key = AmbientKey(descriptorIndex);
        StopAmbient(descriptorIndex); // idempotent restart

        // Lightweight static anchor at the descriptor world position (identity orientation, no lift).
        // Unlike a cast effect, an ambient effect does NOT add EmitterHeightOffset — it sits exactly
        // at the authored descriptor position (sub_49E5A1 passes the descriptor pos vec3 verbatim).
        var anchor = new Node3D { Name = $"MapAmbientAnchor{descriptorIndex}", GlobalPosition = godotPos };
        AddChild(anchor);

        var subEffects = TryLoadXeff(effectId);

        LiveEffect live;
        if (subEffects is { Length: > 0 })
        {
            var meshInstances = new MeshInstance3D?[subEffects.Length];
            var simNodes = new GpuParticleSimNode?[subEffects.Length];
            var textures = new ImageTexture?[subEffects.Length][];

            for (var i = 0; i < subEffects.Length; i++)
            {
                var se = subEffects[i];
                textures[i] = LoadSubEffectTextures(se) ?? Array.Empty<ImageTexture?>();

                if (se.ResourceId >= XeffResourceParticleThreshold)
                {
                    var simNode = TryBuildParticleSimNode(se.ResourceId, godotPos);
                    if (simNode is not null)
                    {
                        simNodes[i] = simNode;
                        AddChild(simNode);
                    }
                }
                else
                {
                    meshInstances[i] = BuildSubEffectMesh(se, godotPos, textures[i], 0);
                    if (meshInstances[i] is not null)
                        AddChild(meshInstances[i]!);
                }
            }

            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = anchor,
                AmbientAnchorOwned = true,
                SubEffects = subEffects,
                MeshInstances = meshInstances,
                SimNodes = simNodes,
                Textures = textures,
                ElapsedMs = 0
            };

            var gpuCount = simNodes.Count(s => s is not null);
            GD.Print($"[EffectRenderer] PlayAmbient: idx={descriptorIndex} effectId={effectId} " +
                     $"— loaded real .xeff ({subEffects.Length} sub-effects, {gpuCount} GPU-particle sims) pos={godotPos}. " +
                     "spec: IDA MapXEffect_SpawnFactory_Ambient @0x49e4ef.");
        }
        else
        {
            // No-placeholder doctrine: missing/parse-failed .xeff renders nothing (anchor kept for stop).
            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = anchor,
                AmbientAnchorOwned = true,
                ElapsedMs = 0
            };

            GD.Print($"[EffectRenderer] PlayAmbient: idx={descriptorIndex} effectId={effectId} " +
                     $"— .xeff unavailable or parse failed; rendering nothing (no-placeholder doctrine). pos={godotPos}.");
        }

        _live[key] = live;
    }

    /// <summary>
    ///     Soft-stops and tears down an ambient map effect by its descriptor index, including the
    ///     owned static anchor node. No-op when the index is not currently playing.
    ///     spec: IDA sub_49E5A1 @0x49e6f8 — sub_49D7C2 despawns the slot when the gate goes false.
    /// </summary>
    public void StopAmbient(int descriptorIndex)
    {
        var key = AmbientKey(descriptorIndex);
        if (!_live.Remove(key, out var live))
            return;

        live.Active = false;
        TeardownLiveEffect(live);

        if (live.AmbientAnchorOwned && live.Anchor is { } anchor && IsInstanceValid(anchor))
            anchor.QueueFree();

        GD.Print($"[EffectRenderer] StopAmbient: idx={descriptorIndex} effectId={live.EffectId} stopped. " +
                 "spec: IDA sub_49E5A1 @0x49e6f8 despawn.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GPU-particle simulation node builder
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Loads (lazily) the <c>particleEmitter.eff</c> table and builds a
    ///     <see cref="GpuParticleSimNode" /> for the entry whose <c>entry_id</c> equals
    ///     <paramref name="resourceId" /> (raw equality — NO −10000 subtraction).
    ///     Returns null on VFS miss, table parse failure, or entry miss (renders nothing, faithful).
    ///     spec: Docs/RE/formats/effects.md §E.1 — particleEmitter.eff path: CONFIRMED.
    ///     spec: Docs/RE/formats/effects.md §E.4 — raw entry_id equality: CONFIRMED.
    ///     spec: Docs/RE/formats/effects.md §E.2.2 — per-particle Euler integration: CODE-CONFIRMED.
    /// </summary>
    private GpuParticleSimNode? TryBuildParticleSimNode(uint resourceId, Vector3 origin)
    {
        if (_assets is null) return null;

        // Lazy-load the table once.
        // spec: Docs/RE/formats/effects.md §E.1 — particleemitter.eff (VFS-lowercased): CONFIRMED.
        if (!_particleEmitterTableAttempted)
        {
            _particleEmitterTableAttempted = true;
            var raw = _assets.GetRaw(ParticleEmitterEffPath);
            if (!raw.IsEmpty)
                try
                {
                    _particleEmitterTable = ParticleEmitterParser.Parse(raw);
                    GD.Print($"[EffectRenderer] particleEmitter.eff loaded: " +
                             $"{_particleEmitterTable.Entries.Length} entries. " +
                             "spec: Docs/RE/formats/effects.md §E.1.");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[EffectRenderer] particleEmitter.eff parse failed: {ex.Message}");
                }
            else
                GD.Print("[EffectRenderer] particleEmitter.eff not found in VFS — GPU particles unavailable.");
        }

        if (_particleEmitterTable is null) return null;

        // Raw entry_id equality lookup (NO −10000 subtraction).
        // spec: Docs/RE/formats/effects.md §E.4 — raw-id equality: CONFIRMED.
        var entry = _particleEmitterTable.TryGetById(resourceId);
        if (entry is null)
        {
            GD.Print($"[EffectRenderer] particleEmitter.eff: no entry for resource_id={resourceId} " +
                     "(raw-id miss → render nothing). spec: Docs/RE/formats/effects.md §E.4.");
            return null;
        }

        // Resolve the entry's texture by its stored full path (exact string, no prefix added).
        // spec: Docs/RE/formats/effects.md §E.2.3 — texture_name is the FULL path, used verbatim: CONFIRMED.
        ImageTexture? tex = null;
        if (!string.IsNullOrEmpty(entry.TextureName))
            tex = _assets.LoadTexture(entry.TextureName);

        var simNode = new GpuParticleSimNode(entry, tex)
        {
            GlobalPosition = origin
        };

        GD.Print($"[EffectRenderer] GPU particle sim: resource_id={resourceId} entry_id={entry.EntryId} " +
                 $"numParticles={entry.NumFrames} spriteSize=({entry.SpriteSizeX},{entry.SpriteSizeY}) " +
                 $"tex='{entry.TextureName}'. spec: Docs/RE/formats/effects.md §E.2.2.");
        return simNode;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // .xeff loading (mini-parser, corrected 8-byte header spec)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Attempts to load and parse a .xeff file by raw effect_id, resolving ONLY through the registry
    ///     (built from xeffect.lst at boot): <c>_effectRegistry[effectId]</c> → vfs path. The original
    ///     ALWAYS resolves through this registry keyed by raw effect_id; there is NO numeric-name sprintf
    ///     path in the binary, so a registry miss renders nothing (no fabricated path probe).
    ///     spec: Docs/RE/formats/effects.md §C.2 — "runtime ALWAYS resolves through registry keyed by raw
    ///     effect_id; NO numeric-name sprintf in original (Option A REJECTED); CONFIRMED."
    ///     spec: Docs/RE/formats/effects.md §A.9 — xeffect.lst manifest.
    /// </summary>
    private SubEffectDesc[]? TryLoadXeff(uint effectId)
    {
        if (_assets is null) return null;

        // 1) Registry resolve (primary — spec §C.2 CONFIRMED).
        string? vfsPath = null;
        if (_effectRegistry is { } reg && reg.TryGetValue(effectId, out var regPath))
        {
            vfsPath = regPath;
            GD.Print($"[EffectRenderer] Registry hit: effectId={effectId} → {vfsPath}. " +
                     "spec: Docs/RE/formats/effects.md §C.2 registry resolve.");
        }

        // No numeric-name fallback: the original ALWAYS resolves through the registry keyed by raw
        // effect_id; there is no sprintf("%d.xeff") path in the binary (spec §C.2 Option A REJECTED).
        // A registry miss therefore renders nothing — the faithful behaviour, no fabricated path probe.
        if (vfsPath is null)
        {
            GD.Print(
                $"[EffectRenderer] effectId={effectId}: not in registry — rendering nothing " +
                "(spec §C.2: registry is the sole resolver; no numeric-name fallback).");
            return null;
        }

        var raw = _assets.GetRaw(vfsPath);
        if (raw.IsEmpty)
        {
            GD.Print($"[EffectRenderer] .xeff not found in VFS: {vfsPath} — rendering nothing.");
            return null;
        }

        try
        {
            // Use the shared layer-03 parser (corrected 8-byte header).
            // spec: Docs/RE/formats/effects.md §A.2 — XEFF_HEADER_SIZE = 8 (0x08); VERIFIED.
            var data = XeffParser.ParseXeff(raw);
            if (data.SubEffects.Length == 0)
                return [];

            // Map XeffSubEffect → SubEffectDesc (presentation view-model).
            var results = new SubEffectDesc[data.SubEffects.Length];
            for (var i = 0; i < data.SubEffects.Length; i++)
                results[i] = MapSubEffect(data.SubEffects[i]);
            return results;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EffectRenderer] .xeff parse failed ({vfsPath}): {ex.Message} — rendering nothing.");
            return null;
        }
    }

    /// <summary>
    ///     Maps a shared <see cref="XeffSubEffect" /> (layer-03 parse output) into a presentation
    ///     <see cref="SubEffectDesc" /> view-model, applying all boundary conversions:
    ///     ALPHA INVERSION: shared AlphaKeys are RAW file values (0.0=opaque, 1.0=transparent per
    ///     spec §A.6). EffectRenderer renders with 0=transparent/1=opaque convention.
    ///     Boundary: inMemory = 1.0f − fileValue.
    ///     spec: Docs/RE/formats/effects.md §A.6 — alpha inversion; CONFIRMED.
    ///     TexCount (presentation) == EntryCount (shared model) — the name-table/keyframe count.
    ///     spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14; CONFIRMED.
    ///     TotalTime derived: EntryCount × AnimStride + AnimBaseTime.
    ///     spec: Docs/RE/formats/effects.md §A.4.3 — total_time = tex_count × anim_stride + anim_base_time; CONFIRMED.
    ///     ScrollU/V derived from low byte of EntryCount (bit0=U, bit1=V).
    ///     spec: Docs/RE/formats/effects.md §A.13 — bit 0 = scroll U, bit 1 = scroll V; MEDIUM.
    /// </summary>
    private static SubEffectDesc MapSubEffect(XeffSubEffect se)
    {
        // Alpha inversion: shared model stores file values (0=opaque); presentation expects 1=opaque.
        // spec: Docs/RE/formats/effects.md §A.6 — alpha stored as 1.0−opacity; CONFIRMED.
        var alphaKeys = new float[se.AlphaKeys.Length];
        for (var i = 0; i < se.AlphaKeys.Length; i++)
            alphaKeys[i] = 1f - se.AlphaKeys[i]; // un-invert at consumption boundary

        // TexCount == EntryCount (number of keyframes/name-table entries).
        // spec: Docs/RE/formats/effects.md §A.4.0 — tex_count u32 @ element+0x14; CONFIRMED.
        var texCount = se.EntryCount;

        // TotalTime = tex_count × anim_stride + anim_base_time.
        // spec: Docs/RE/formats/effects.md §A.4.3 — total_time derivation; CONFIRMED.
        var totalTime = texCount * se.AnimStride + se.AnimBaseTime;

        // ScrollU/V from low byte of EntryCount: bit 0 = U, bit 1 = V.
        // spec: Docs/RE/formats/effects.md §A.13 — bit 0 = scroll U, bit 1 = scroll V; MEDIUM.
        var scrollU = (texCount & 1u) != 0;
        var scrollV = (texCount & 2u) != 0;

        return new SubEffectDesc
        {
            EmitterType = se.EmitterType,
            ResourceId = se.ResourceId,
            AnimFlag = se.AnimFlag,
            TexCount = texCount,
            TextureNames = se.TextureNames,
            AlphaKeys = alphaKeys,
            // Per-keyframe diffuse-RGB tint curve carried straight through (already assembled in
            // R/G/B order by the layer-03 parser). NOT a scale — see §17.3.
            // spec: Docs/RE/formats/effects.md §A.4.2 — pass 2/3/4 = per-keyframe diffuse R/G/B.
            DiffuseR = se.DiffuseR,
            DiffuseG = se.DiffuseG,
            DiffuseB = se.DiffuseB,
            AnimLoop = se.AnimLoop,
            AnimStride = se.AnimStride,
            AnimBaseTime = se.AnimBaseTime,
            TotalTime = totalTime,
            Keyframes = se.Keyframes,
            ScrollU = scrollU,
            ScrollV = scrollV
        };
    }
}