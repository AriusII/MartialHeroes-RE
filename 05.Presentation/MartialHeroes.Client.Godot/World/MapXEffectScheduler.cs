using System.Buffers.Binary;
using Godot;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class MapXEffectScheduler : Node3D
{
    private const string MapTxtPathFormat = "data/effect/map{0}.txt";

    private const int DescriptorRecordSize = 0x20;

    private const float TerrainRadiusCap = 1000f;
    private const float ProximityRadiusScale = 0.8f;

    private const uint TodMsPerHour = 0xE10;
    private const uint TodMsPerMinute = 0x3C;
    private const int MinutesPerDay = 1440;

    private const float TodAlwaysOnThreshold = 0.5f;


    private RealClientAssets? _assets;

    private Descriptor[] _descriptors = [];


    private int _loadedAreaId = -1;
    private EffectRenderer? _renderer;


    public int CurrentAreaId { get; set; } = -1;

    public Vector3 LocalPlayerGodotPos { get; set; }

    public bool HasLocalPlayer { get; set; }

    public float TerrainViewRadius { get; set; } = TerrainRadiusCap;

    public uint TimeOfDayMs { get; set; }


    public void Bind(EffectRenderer renderer, RealClientAssets? assets)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
        _assets = assets;
        GD.Print($"[MapXEffectScheduler] Bound. VFS {(assets is null ? "absent — disabled" : "available")}.");
    }


    public override void _Process(double delta)
    {
        if (_renderer is null) return;

        if (CurrentAreaId != _loadedAreaId)
        {
            DespawnAll();
            _loadedAreaId = CurrentAreaId;
            LoadManifest(CurrentAreaId);
        }

        if (_descriptors.Length == 0) return;

        if (!HasLocalPlayer) return;

        var radius = TerrainViewRadius;
        if (radius > TerrainRadiusCap) radius = TerrainRadiusCap;
        radius *= ProximityRadiusScale;
        var radiusSq = radius * radius;

        var hour = (int)(TimeOfDayMs / TodMsPerHour);
        var minute = (int)(TimeOfDayMs % TodMsPerHour / TodMsPerMinute);
        var minutesOfDay = minute + 60 * hour;

        var playerXZ = LocalPlayerGodotPos;

        for (var i = 0; i < _descriptors.Length; i++)
        {
            ref var d = ref _descriptors[i];

            var dx = playerXZ.X - d.GodotPos.X;
            var dz = playerXZ.Z - d.GodotPos.Z;
            var distSq = dx * dx + dz * dz;

            var shouldBeActive = radiusSq > distSq && TimeOfDayActive(d, minutesOfDay);

            if (shouldBeActive)
            {
                if (!d.Active)
                {
                    d.Active = true;
                    _renderer.PlayAmbient(i, d.GodotPos, d.EffectId);
                }
            }
            else if (d.Active)
            {
                d.Active = false;
                _renderer.StopAmbient(i);
            }
        }
    }

    public override void _ExitTree()
    {
        DespawnAll();
    }


    private static bool TodActive(float startHours, float durationHours, int minutesOfDay)
    {
        if (startHours < TodAlwaysOnThreshold && durationHours < TodAlwaysOnThreshold)
            return true;

        var endMinutes = (uint)(60.0f * (durationHours + startHours));
        var startMinutes = (uint)(startHours * 60.0f);
        var mod = (uint)minutesOfDay;

        if (startMinutes <= mod && mod < endMinutes)
            return true;

        return endMinutes > MinutesPerDay && mod + MinutesPerDay < endMinutes;
    }

    private static bool TimeOfDayActive(in Descriptor d, int minutesOfDay)
    {
        return TodActive(d.TodStartHours, d.TodDurationHours, minutesOfDay);
    }


    private void LoadManifest(int areaId)
    {
        _descriptors = [];

        if (_assets is null || areaId < 0) return;

        var token = $"{areaId / 100}{areaId / 10 % 10}{areaId % 10}";
        var path = string.Format(MapTxtPathFormat, token);

        var raw = _assets.GetRaw(path);
        if (raw.IsEmpty)
        {
            GD.Print($"[MapXEffectScheduler] No ambient manifest for area {areaId} ({path}) — none scheduled.");
            return;
        }

        var span = raw.Span;
        if (span.Length < 4)
        {
            GD.PrintErr($"[MapXEffectScheduler] {path} too short ({span.Length} bytes) — skipped.");
            return;
        }

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

            var effectId = BinaryPrimitives.ReadUInt32LittleEndian(rec[..4]);
            var posX = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[4..8]));
            var posY = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[8..12]));
            var posZ = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[12..16]));
            var todStart = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[20..24]));
            var todDur = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(rec[24..28]));


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


    private struct Descriptor
    {
        public uint EffectId;
        public Vector3 GodotPos;
        public float TodStartHours;
        public float TodDurationHours;
        public bool Active;
    }
}