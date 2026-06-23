// World/MapXEffectScheduler.cs
//
// FIX 15a — map<N>.txt ambient effect scheduler.
//
// Ports the per-frame ambient-effect scheduler sub_49E5A1 (@0x49e5a1): parses the per-area
// manifest data/effect/map<token>.txt, reloads it on area change, and per-frame proximity- and
// time-of-day-gates each descriptor against the local player, spawning/despawning a position-
// anchored UserXEffect through the existing EffectRenderer .xeff path (PlayAmbient/StopAmbient).
//
// IDA EVIDENCE (static, doida.exe):
//   sub_49E5A1 @0x49e5b3 — on area change (g_EnvCurrentAreaId != this+4): despawn-all,
//     this+4 = areaId, EffectMap_LoadMapTxt(this). Player pos read from g_LocalPlayer+1064 @0x49e5d9.
//   EffectMap_LoadMapTxt sub_49E2A4 — sprintf(Buffer, "data/effect/map%s.txt", byte_844484);
//     u32 count (AssetStream_ReadInt32Field) + count × operator new(0x20) records:
//       +0 i32 effectId, +4..+24 six f32 fields, +28 byte active (set 0 on load).
//   Env_MapSetAndLoadArea sub_4575AF @0x457618 — byte_844484 = sprintf("%d%d%d",
//     areaId/100, areaId/10%10, areaId%10), i.e. the area id as a 3-digit zero-padded decimal.
//     (Same token AreaTag(areaId) uses for terrain paths.)
//   Proximity gate sub_49E5A1 @0x49e623..@0x49e69d — radius = *(TerrainManager+89*4); clamp >1000
//     to 1000; *= 0.8; square it; spawn when radiusSq > Actor_DistanceSqXZ(playerPos, descPos)
//     (XZ distance-squared, sub_407294).
//   TOD gate sub_49CCB7 — minOfDay = min + 60*hour (hour = TODms/0xE10, min = TODms%0xE10/0x3C);
//     active when (start<0.5 && dur<0.5) || (start*60 <= minOfDay < (start+dur)*60)
//     || ((start+dur)*60 > 1440 && minOfDay+1440 < (start+dur)*60).  [start=field+20, dur=field+24]
//   Spawn MapXEffect_SpawnFactory_Ambient sub_49E4EF — UserXEffect_setupTimedWithPos at the descriptor
//     world position with identity orientation, type tag 2, descriptor index stored at slot+84.
//
// FIRING-DOCTRINE: a missing map<N>.txt or a missing/unparseable .xeff is logged + skipped — never
// substituted (no placeholder/test value).
//
// PASSIVE / LAYER 05: pure asset → visual translation. The engine globals this scheduler needs
// (current area id, local-player Godot position, terrain view radius, time-of-day) are NOT exposed
// in the C# layer; the world owner feeds them each frame via the public properties below, then the
// node self-drives in _Process. This keeps the scheduler self-contained.

using System.Buffers.Binary;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;
using Godot;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Node3D that schedules per-area ambient map effects from data/effect/map&lt;token&gt;.txt,
///     gating each descriptor by player proximity and time-of-day and driving spawn/despawn through
///     <see cref="EffectRenderer.PlayAmbient" /> / <see cref="EffectRenderer.StopAmbient" />.
///     spec: IDA sub_49E5A1 (per-frame scheduler) + sub_49E2A4 (manifest parse).
/// </summary>
public sealed partial class MapXEffectScheduler : Node3D
{
    // ── Constants (IDA-confirmed) ──────────────────────────────────────────────

    // Manifest path format: data/effect/map<token>.txt.
    // IDA: sub_49E2A4 — off_79FF54 → "data/effect/map%s.txt".
    private const string MapTxtPathFormat = "data/effect/map{0}.txt";

    // Descriptor record size on disk: 0x20 (32) bytes.
    // IDA: sub_49E2A4 — operator new(0x20u) per record.
    private const int DescriptorRecordSize = 0x20;

    // Terrain view-radius clamp ceiling and proximity scale.
    // IDA: sub_49E5A1 @0x49e633 — if (radius > 1000) radius = 1000; @0x49e652 radius *= 0.8.
    private const float TerrainRadiusCap = 1000f;
    private const float ProximityRadiusScale = 0.8f;

    // Time-of-day arithmetic constants.
    // IDA: sub_49E5A1 @0x49e60c/@0x49e615 — hour = TODms / 0xE10 (3,600,000 ms/hr), min = TODms % 0xE10 / 0x3C.
    private const uint TodMsPerHour = 0xE10; // 3600 in the engine's TOD-ms unit (1 unit = 1000 ms)
    private const uint TodMsPerMinute = 0x3C; // 60
    private const int MinutesPerDay = 1440;

    // TOD "always on" threshold: both start and duration below 0.5 hours → unconditionally active.
    // IDA: sub_49CCB7 — return (v6 < 0.5 && v7 < 0.5) || ...
    private const float TodAlwaysOnThreshold = 0.5f;

    // ── Bound dependencies ─────────────────────────────────────────────────────

    private RealClientAssets? _assets;
    private EffectRenderer? _renderer;

    // ── Per-area state ─────────────────────────────────────────────────────────

    // -1 = no area loaded yet (forces a manifest load on first valid CurrentAreaId).
    // IDA: sub_49E5A1 @0x49e5b3 — reload when g_EnvCurrentAreaId != this+4.
    private int _loadedAreaId = -1;

    // Parsed descriptors for the currently loaded area (empty when no manifest / not loaded).
    private Descriptor[] _descriptors = [];

    // ── Engine-state inputs (fed by the world owner each frame) ────────────────

    /// <summary>
    ///     Current area id (g_EnvCurrentAreaId). Set by the world owner; a change triggers a manifest
    ///     reload + despawn-all on the next <see cref="_Process" />. Negative = no area.
    ///     spec: IDA sub_49E5A1 @0x49e5b3.
    /// </summary>
    public int CurrentAreaId { get; set; } = -1;

    /// <summary>
    ///     Local player position in Godot-space (legacy Z already negated via WorldCoordinates.ToGodot).
    ///     spec: IDA sub_49E5A1 @0x49e5d9 — player pos = g_LocalPlayer+1064.
    /// </summary>
    public Vector3 LocalPlayerGodotPos { get; set; }

    /// <summary>True when a local player exists; when false the proximity gate is skipped (no spawns).</summary>
    public bool HasLocalPlayer { get; set; }

    /// <summary>
    ///     Terrain view radius (TerrainManager+0x164, float offset +89). Clamped to 1000 and scaled by
    ///     0.8 for the proximity gate. spec: IDA sub_49E5A1 @0x49e623.
    ///     When the world layer has not yet wired a real terrain accessor, leave this at the
    ///     binary's own cap (1000) — the conservative default documented as a known gap.
    /// </summary>
    public float TerrainViewRadius { get; set; } = TerrainRadiusCap;

    /// <summary>
    ///     Time-of-day in the engine's TOD-ms unit (g_EnvTime_TODms). hour = value/0xE10,
    ///     minute = value%0xE10/0x3C. spec: IDA sub_49E5A1 @0x49e60c.
    /// </summary>
    public uint TimeOfDayMs { get; set; }

    // ── Binding ────────────────────────────────────────────────────────────────

    /// <summary>
    ///     Binds the renderer and VFS used to load manifests and spawn effects. The scheduler does
    ///     nothing until both are bound (no manifest load, no spawns).
    /// </summary>
    public void Bind(EffectRenderer renderer, RealClientAssets? assets)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _assets = assets;
        GD.Print($"[MapXEffectScheduler] Bound. VFS {(assets is null ? "absent — disabled" : "available")}.");
    }

    // ── Per-frame scheduling (ports sub_49E5A1) ────────────────────────────────

    public override void _Process(double delta)
    {
        if (_renderer is null) return;

        // Area change → despawn all current ambient effects, reload the manifest.
        // IDA: sub_49E5A1 @0x49e5b3 — sub_49D97A (despawn-all); this+4 = areaId; EffectMap_LoadMapTxt.
        if (CurrentAreaId != _loadedAreaId)
        {
            DespawnAll();
            _loadedAreaId = CurrentAreaId;
            LoadManifest(CurrentAreaId);
        }

        if (_descriptors.Length == 0) return;

        // No player → no proximity reference; the legacy code falls back to a sentinel position but
        // never spawns ambient effects without a live player frame. We skip gating until a player exists.
        // IDA: sub_49E5A1 @0x49e5d0 — if (g_LocalPlayer) pos = player+1064 else &dword_897CE4.
        if (!HasLocalPlayer) return;

        // Proximity radius²: clamp terrain radius to 1000, scale by 0.8, square.
        // IDA: sub_49E5A1 @0x49e623..@0x49e65c.
        var radius = TerrainViewRadius;
        if (radius > TerrainRadiusCap) radius = TerrainRadiusCap;
        radius *= ProximityRadiusScale;
        var radiusSq = radius * radius;

        // Time-of-day → minutes-of-day (hour/minute decomposition matches the caller of sub_49CCB7).
        // IDA: sub_49E5A1 @0x49e60c — hour = TODms/0xE10, min = TODms%0xE10/0x3C.
        var hour = (int)(TimeOfDayMs / TodMsPerHour);
        var minute = (int)(TimeOfDayMs % TodMsPerHour / TodMsPerMinute);
        var minutesOfDay = minute + 60 * hour;

        var playerXZ = LocalPlayerGodotPos;

        for (var i = 0; i < _descriptors.Length; i++)
        {
            ref var d = ref _descriptors[i];

            // XZ distance²: radiusSq > dist² AND TOD gate passes → active.
            // IDA: sub_49E5A1 @0x49e69d — Actor_DistanceSqXZ(playerPos, descPos); @0x49e6b5 gate.
            var dx = playerXZ.X - d.GodotPos.X;
            var dz = playerXZ.Z - d.GodotPos.Z;
            var distSq = dx * dx + dz * dz;

            var shouldBeActive = radiusSq > distSq && TimeOfDayActive(d, minutesOfDay);

            if (shouldBeActive)
            {
                // Spawn once on rising edge.
                // IDA: sub_49E5A1 @0x49e6be — if (!desc[28]) { desc[28]=1; spawn }.
                if (!d.Active)
                {
                    d.Active = true;
                    _renderer.PlayAmbient(i, d.GodotPos, d.EffectId);
                }
            }
            else if (d.Active)
            {
                // Despawn on falling edge.
                // IDA: sub_49E5A1 @0x49e6ed — if (desc[28]==1) { desc[28]=0; sub_49D7C2(despawn) }.
                d.Active = false;
                _renderer.StopAmbient(i);
            }
        }
    }

    public override void _ExitTree()
    {
        DespawnAll();
    }

    // ── TOD gate (ports sub_49CCB7) ────────────────────────────────────────────

    /// <summary>
    ///     Time-of-day gate. spec: IDA sub_49CCB7.
    ///     Always-on when start &lt; 0.5h AND duration &lt; 0.5h; otherwise active when
    ///     start*60 &lt;= minutesOfDay &lt; (start+duration)*60, with a midnight-wrap clause when
    ///     (start+duration)*60 &gt; 1440 (0x5A0) and minutesOfDay+1440 &lt; (start+duration)*60.
    /// </summary>
    private static bool TodActive(float startHours, float durationHours, int minutesOfDay)
    {
        if (startHours < TodAlwaysOnThreshold && durationHours < TodAlwaysOnThreshold)
            return true;

        // (start+dur)*60 as the engine computes it: (unsigned)(60.0 * (dur + start)).
        // IDA: sub_49CCB7 — v4 = (u64)(60.0 * (v7 + v6)).
        var endMinutes = (uint)(60.0f * (durationHours + startHours));
        var startMinutes = (uint)(startHours * 60.0f);
        var mod = (uint)minutesOfDay;

        if (startMinutes <= mod && mod < endMinutes)
            return true;

        // Midnight wrap: end past 1440 (0x5A0) and the wrapped minute falls inside the window.
        // IDA: sub_49CCB7 — (u32)v4 > 0x5A0 && v3 + 1440 < (u32)v4.
        return endMinutes > MinutesPerDay && mod + (uint)MinutesPerDay < endMinutes;
    }

    private static bool TimeOfDayActive(in Descriptor d, int minutesOfDay)
    {
        return TodActive(d.TodStartHours, d.TodDurationHours, minutesOfDay);
    }

    // ── Manifest load (ports sub_49E2A4) ───────────────────────────────────────

    private void LoadManifest(int areaId)
    {
        _descriptors = [];

        if (_assets is null || areaId < 0) return;

        // Token = areaId as a 3-digit zero-padded decimal.
        // IDA: Env_MapSetAndLoadArea @0x457618 — sprintf("%d%d%d", id/100, id/10%10, id%10).
        var token = $"{areaId / 100}{areaId / 10 % 10}{areaId % 10}";
        var path = string.Format(MapTxtPathFormat, token);

        var raw = _assets.GetRaw(path);
        if (raw.IsEmpty)
        {
            // Missing manifest = log + skip (no-placeholder doctrine). Many areas have no ambient effects.
            // IDA: sub_49E2A4 — "cannot find file %s" branch (nullsub_2) → empty descriptor set.
            GD.Print($"[MapXEffectScheduler] No ambient manifest for area {areaId} ({path}) — none scheduled.");
            return;
        }

        var span = raw.Span;
        if (span.Length < 4)
        {
            GD.PrintErr($"[MapXEffectScheduler] {path} too short ({span.Length} bytes) — skipped.");
            return;
        }

        // u32 count (LE).
        // IDA: sub_49E2A4 — AssetStream_ReadInt32Field(v13, &count).
        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[..4]);
        if (count < 0) count = 0;

        var available = (span.Length - 4) / DescriptorRecordSize;
        if (count > available)
        {
            GD.PrintErr($"[MapXEffectScheduler] {path} size mismatch: header says {count} records, " +
                        $"only {available} fit ({span.Length} bytes) — reading {available}.");
            count = available;
        }

        var list = new Descriptor[count];
        for (var i = 0; i < count; i++)
        {
            var o = 4 + i * DescriptorRecordSize;
            var rec = span.Slice(o, DescriptorRecordSize);

            // Layout (IDA sub_49E2A4): +0 i32 effectId, +4 f32 posX, +8 f32 posY, +12 f32 posZ,
            //   +16 f32 radius, +20 f32 todStartHours, +24 f32 todDurationHours, +28 byte active(=0).
            var effectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[0..4]);
            var posX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[4..8]));
            var posY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[8..12]));
            var posZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[12..16]));
            var todStart = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[20..24]));
            var todDur = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[24..28]));

            // Descriptor radius field (+16) is read by the binary but the proximity CLAMP uses the
            // TerrainManager radius, not this field (IDA sub_49E5A1 @0x49e623 sources radius from the
            // terrain singleton). We keep the field's value off the gate to stay faithful.

            // Legacy world pos → Godot (negate Z once). spec: WorldCoordinates.ToGodot.
            var (gx, gy, gz) = WorldCoordinates.ToGodot(posX, posY, posZ);

            list[i] = new Descriptor
            {
                EffectId = effectId,
                GodotPos = new Vector3(gx, gy, gz),
                TodStartHours = todStart,
                TodDurationHours = todDur,
                Active = false
            };
        }

        _descriptors = list;
        GD.Print($"[MapXEffectScheduler] Loaded {count} ambient descriptors for area {areaId} from {path}. " +
                 "spec: IDA sub_49E2A4 (u32 count + 32-byte records).");
    }

    private void DespawnAll()
    {
        if (_renderer is null) return;
        for (var i = 0; i < _descriptors.Length; i++)
            if (_descriptors[i].Active)
            {
                _descriptors[i].Active = false;
                _renderer.StopAmbient(i);
            }
    }

    // ── Descriptor model (32-byte record; +28 active is runtime state, not file data) ──

    private struct Descriptor
    {
        public uint EffectId; // +0 i32 effectId (raw; resolved via xeffect.lst registry)
        public Vector3 GodotPos; // +4..+12 f32 posX/Y/Z (legacy → Godot, Z negated)
        public float TodStartHours; // +20 f32 time-of-day start (hours)
        public float TodDurationHours; // +24 f32 time-of-day duration (hours)
        public bool Active; // +28 byte runtime active flag (file value is 0 on load)
    }
}
