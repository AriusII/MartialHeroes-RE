using System.Runtime.InteropServices;
using Godot;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Assets.Parsers.Effects;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class EffectRenderer
{
    private const string EffectScaleXdbPath = "data/script/effectscale.xdb";

    private EffectScaleTable? _effectScaleTable;

    private bool _effectScaleAttempted;

    private float ResolveBaseScale(uint effectId)
    {
        if (!_effectScaleAttempted)
        {
            _effectScaleAttempted = true;
            if (_assets is not null)
            {
                var raw = _assets.GetRaw(EffectScaleXdbPath);
                if (!raw.IsEmpty)
                    try
                    {
                        _effectScaleTable = EffectScaleParser.Parse(raw);
                        GD.Print($"[EffectRenderer] effectscale.xdb loaded: {_effectScaleTable.Records.Count} records. " +
                                 "spec: Docs/RE/formats/effects.md §D (REPLACE descriptor scale_default at parse).");
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[EffectRenderer] effectscale.xdb parse failed: {ex.Message}");
                    }
                else
                    GD.Print("[EffectRenderer] effectscale.xdb absent — base scale defaults to 1.0. " +
                             "spec: Docs/RE/formats/effects.md §D.");
            }
        }

        var rec = _effectScaleTable?.TryGetByEffectKey(effectId);
        return rec?.Scale ?? 1f;
    }

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

        EffectNameManifest manifest;
        try
        {
            manifest = XeffectLstParser.Parse(lstRaw);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EffectRenderer] xeffect.lst parse failed: {ex.Message} — skipping registry build.");
            return null;
        }

        var registry = new Dictionary<uint, string>(manifest.Count);
        var mapped = 0;
        var skipped = 0;

        foreach (var nameEntry in manifest.Entries)
        {
            if (string.IsNullOrEmpty(nameEntry.Name))
            {
                skipped++;
                continue;
            }

            var vfsPath = $"data/effect/xeff/{nameEntry.Name}";

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

            if (effectId == 0x46464558u)
            {
                skipped++;
                continue;
            }

            if (registry.TryAdd(effectId, vfsPath))
                mapped++;
        }

        _effectRegistry = registry;
        GD.Print($"[EffectRenderer] Effect registry built from xeffect.lst: {mapped} effect_ids mapped " +
                 $"({skipped} skipped / duplicates). spec: Docs/RE/formats/effects.md §C.2 / §A.9.");
        return registry;
    }


    public void PlayCast(Node3D actor, uint effectId)
    {
        SpawnAnchoredEffect(actor, effectId, false);
    }

    public void PlayCastAura(Node3D actor, uint effectId)
    {
        SpawnAnchoredEffect(actor, effectId, true);
    }

    private void SpawnAnchoredEffect(Node3D actor, uint effectId, bool forceLoop)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var key = ResolveActorKey(actor);
        StopCast(actor);

        var yOffset = 0f;
        var effScale = ResolveBaseScale(effectId);
        var origin = actor.GlobalPosition + new Vector3(0f, yOffset, 0f);

        var subEffects = TryLoadXeff(effectId);

        if (forceLoop && subEffects is { Length: > 0 })
            for (var i = 0; i < subEffects.Length; i++)
                subEffects[i].AnimLoop = 1;

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
                    var simNode = TryBuildParticleSimNode(se.ResourceId, origin);
                    if (simNode is not null)
                    {
                        simNodes[i] = simNode;
                        AddChild(simNode);
                    }
                }
                else
                {
                    meshInstances[i] = BuildSubEffectMesh(se, origin, textures[i], 0, effScale);
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
                SimNodes = simNodes,
                Textures = textures,
                EffectiveScale = effScale,
                YOffset = yOffset,
                ElapsedMs = 0
            };

            var gpuCount = 0;
            for (var s = 0; s < simNodes.Length; s++)
                if (simNodes[s] is not null)
                    gpuCount++;
            GD.Print($"[EffectRenderer] SpawnAnchoredEffect: effectId={effectId} actor={key.RawId} " +
                     $"loopForced={forceLoop} — loaded real .xeff ({subEffects.Length} sub-effects, " +
                     $"{gpuCount} GPU-particle sims) origin={origin}. " +
                     "spec: Docs/RE/specs/effects.md §15.4 looping actor-anchored UserXEffect (0xC8); CODE-CONFIRMED.");
        }
        else
        {
            live = new LiveEffect
            {
                EffectId = effectId,
                Active = true,
                Anchor = actor,
                ElapsedMs = 0
            };

            GD.Print($"[EffectRenderer] SpawnAnchoredEffect: effectId={effectId} actor={key.RawId} " +
                     $"loopForced={forceLoop} — .xeff unavailable or parse failed; rendering nothing " +
                     $"(no-placeholder doctrine). origin={origin}.");
        }

        _live[key] = live;
    }

    public void StopCast(Node3D actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var key = ResolveActorKey(actor);
        if (!_live.Remove(key, out var live))
            return;

        live.Active = false;
        TeardownLiveEffect(live);

        GD.Print($"[EffectRenderer] StopCast: actor={key.RawId} effectId={live.EffectId} soft-stopped. " +
                 "spec: Docs/RE/specs/effects.md §15.5 soft-stop; CODE-CONFIRMED.");
    }

    public void StopCastAura(Node3D actor, uint effectId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var key = ResolveActorKey(actor);
        if (!_live.TryGetValue(key, out var live))
            return;

        if (live.EffectId != effectId || !ReferenceEquals(live.Anchor, actor))
        {
            GD.Print($"[EffectRenderer] StopCastAura: actor={key.RawId} effectId={effectId} " +
                     $"no matching active cast aura (active effectId={live.EffectId}) — left alone. " +
                     "spec: Docs/RE/specs/effects.md §15.5 matched soft-stop (0xC9/0xCB).");
            return;
        }

        _live.Remove(key);
        live.Active = false;
        TeardownLiveEffect(live);

        GD.Print($"[EffectRenderer] StopCastAura: actor={key.RawId} effectId={effectId} " +
                 "matched looping cast aura soft-stopped (no fade). " +
                 "spec: Docs/RE/specs/effects.md §15.5 matched soft-stop (0xC9/0xCB).");
    }


    private static ActorKey AmbientKey(int descriptorIndex)
    {
        return new ActorKey((uint)descriptorIndex, EntitySort.None);
    }

    public void PlayAmbient(int descriptorIndex, Vector3 godotPos, uint effectId, float ambientScale)
    {
        var key = AmbientKey(descriptorIndex);
        StopAmbient(descriptorIndex);

        var anchor = new Node3D { Name = $"MapAmbientAnchor{descriptorIndex}", GlobalPosition = godotPos };
        AddChild(anchor);

        var effScale = ResolveBaseScale(effectId) * ambientScale;

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
                    meshInstances[i] = BuildSubEffectMesh(se, godotPos, textures[i], 0, effScale);
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
                EffectiveScale = effScale,
                ElapsedMs = 0
            };

            var gpuCount = 0;
            for (var s = 0; s < simNodes.Length; s++)
                if (simNodes[s] is not null)
                    gpuCount++;
            GD.Print($"[EffectRenderer] PlayAmbient: idx={descriptorIndex} effectId={effectId} scale={effScale} " +
                     $"— loaded real .xeff ({subEffects.Length} sub-effects, {gpuCount} GPU-particle sims) pos={godotPos}. " +
                     "spec: Docs/RE/specs/effect-scheduling.md (ambient spawn) + formats/text_tables.md §6 col5 scale.");
        }
        else
        {
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
                 "spec: Docs/RE/specs/effect-scheduling.md (ambient despawn).");
    }


    private GpuParticleSimNode? TryBuildParticleSimNode(uint resourceId, Vector3 origin)
    {
        if (_assets is null) return null;

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

        var entry = _particleEmitterTable.TryGetById(resourceId);
        if (entry is null)
        {
            GD.Print($"[EffectRenderer] particleEmitter.eff: no entry for resource_id={resourceId} " +
                     "(raw-id miss → render nothing). spec: Docs/RE/formats/effects.md §E.4.");
            return null;
        }

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


    private SubEffectDesc[]? TryLoadXeff(uint effectId)
    {
        if (_assets is null) return null;

        string? vfsPath = null;
        if (_effectRegistry is { } reg && reg.TryGetValue(effectId, out var regPath))
        {
            vfsPath = regPath;
            GD.Print($"[EffectRenderer] Registry hit: effectId={effectId} → {vfsPath}. " +
                     "spec: Docs/RE/formats/effects.md §C.2 registry resolve.");
        }

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
            var data = XeffParser.ParseXeff(raw);
            if (data.SubEffects.Length == 0)
                return [];

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

    private static SubEffectDesc MapSubEffect(XeffSubEffect se)
    {
        var texCount = se.EntryCount;

        var totalTime = texCount * se.AnimStride + se.AnimBaseTime;

        var scrollU = (texCount & 1u) != 0;
        var scrollV = (texCount & 2u) != 0;

        return new SubEffectDesc
        {
            EmitterType = se.EmitterType,
            ResourceId = se.ResourceId,
            AnimFlag = se.AnimFlag,
            BlendMode = se.BlendMode,
            BlendModeKind = se.BlendModeKind,
            TexCount = texCount,
            TextureNames = se.TextureNames,
            Opacity = se.Opacity,
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