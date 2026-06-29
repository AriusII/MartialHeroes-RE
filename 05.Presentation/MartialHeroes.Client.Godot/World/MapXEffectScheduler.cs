using System.Globalization;
using System.Text;
using Godot;
using MartialHeroes.Client.Godot.Composition;
using MartialHeroes.Client.Presentation.Helpers;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class MapXEffectScheduler : Node3D
{
    private const string MapTxtPathFormat = "data/effect/map{0}.txt";

    private const int FieldsPerRecord = 7;

    private const float TerrainRadiusCap = 1000f;
    private const float ProximityRadiusScale = 0.8f;

    private const uint TodMsPerHour = 3_600_000;
    private const uint TodMsPerMinute = 60_000;
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
        var minute = (int)(TimeOfDayMs / TodMsPerMinute % 60);
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
                    _renderer.PlayAmbient(i, d.GodotPos, d.EffectId, d.Scale);
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

        var tokens = Tokenize(raw.Span);
        if (tokens.Count == 0)
        {
            GD.PrintErr($"[MapXEffectScheduler] {path} produced no tokens — skipped.");
            return;
        }

        var idx = 0;
        var count = ParseInt(tokens[idx++]);
        if (count < 0) count = 0;

        var list = new List<Descriptor>(count);
        var read = 0;
        for (var i = 0; i < count; i++)
        {
            if (idx + FieldsPerRecord > tokens.Count)
            {
                GD.PrintErr($"[MapXEffectScheduler] {path}: header says {count} records but tokens ran out " +
                            $"after {read} — reading {read}.");
                break;
            }

            var effectId = (uint)ParseInt(tokens[idx++]);
            var posX = ParseFloat(tokens[idx++]);
            var posY = ParseFloat(tokens[idx++]);
            var posZ = ParseFloat(tokens[idx++]);
            var scale = ParseFloat(tokens[idx++]);
            var todStart = ParseFloat(tokens[idx++]);
            var todDur = ParseFloat(tokens[idx++]);

            var (gx, gy, gz) = WorldCoordinates.ToGodot(posX, posY, posZ);

            list.Add(new Descriptor
            {
                EffectId = effectId,
                GodotPos = new Vector3(gx, gy, gz),
                Scale = scale,
                TodStartHours = todStart,
                TodDurationHours = todDur,
                Active = false
            });
            read++;
        }

        _descriptors = [.. list];
        GD.Print($"[MapXEffectScheduler] Loaded {read} ambient descriptors for area {areaId} from {path}. " +
                 "spec: Docs/RE/formats/text_tables.md §6 (TAB CP949 text: line0=count, col1=effect_id, " +
                 "col2/3/4=world XYZ, col5=scale, col6=TOD start hrs, col7=TOD duration hrs).");
    }

    private static List<string> Tokenize(ReadOnlySpan<byte> span)
    {
        var tokens = new List<string>(64);
        var sb = new StringBuilder(32);

        for (var i = 0; i < span.Length; i++)
        {
            var b = span[i];
            if (b == 0x0D)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
                if (i + 1 < span.Length && span[i + 1] == 0x0A) i++;
            }
            else if (b == 0x0A || b == 0x09)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append((char)b);
            }
        }

        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static int ParseInt(string s)
    {
        return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static float ParseFloat(string s)
    {
        return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
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
        public float Scale;
        public float TodStartHours;
        public float TodDurationHours;
        public bool Active;
    }
}
