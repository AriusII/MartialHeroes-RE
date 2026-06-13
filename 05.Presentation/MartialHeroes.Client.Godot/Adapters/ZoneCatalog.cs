// Adapters/ZoneCatalog.cs
//
// Loads mapsetting.scr (52 zones: id, CP949 name, bounding box, fog) and
// regiontableNNN.bin (sub-region centre points + CP949 labels) from the VFS and exposes
// a current-zone-name lookup by legacy world XZ position.
//
// spec: Docs/RE/specs/minimap.md §6.3 mapsetting.scr — zone names live here, NOT in msg.xdb.
// spec: Docs/RE/formats/misc_data.md §7.1 — stride 84 bytes, 52 records, CP949 zone_name.
// spec: Docs/RE/formats/misc_data.md §7.2 — stride 32 bytes, 52 records, CP949 sub_zone_name.
//
// Design rules:
//   - Lazy, cached — files are read at most once per session.
//   - Null-safe offline — when the VFS is absent or a file fails to parse, all lookups
//     return empty strings or null (never throw to the caller).
//   - No Godot types — this class is pure C#, engine-free, usable from unit tests.
//   - Zero game logic — only reads data; the caller decides what to do with zone names.
//   - CP949 text is decoded by the underlying parsers before reaching this class;
//     no byte decoding happens here.
//
// Threading: designed to be called on the Godot main thread; it is NOT thread-safe.
// The caller (MinimapPanel) drains this during _Process.

using Godot;
using MartialHeroes.Assets.Parsers;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Godot.Dev;

namespace MartialHeroes.Client.Godot.Adapters;

/// <summary>
/// Resolves the display name of the zone (and optionally a nearby sub-zone label) for a
/// given legacy world XZ position. Used by <see cref="MartialHeroes.Client.Godot.HUD.MinimapPanel"/>
/// to caption the minimap footer with the current area name.
///
/// <para>
/// Zone names come from <c>data/script/mapsetting.scr</c> — spec: Docs/RE/formats/misc_data.md §7.1.
/// Sub-zone labels come from <c>data/mapNNN/regiontableNNN.bin</c> — spec: Docs/RE/formats/misc_data.md §7.2.
/// </para>
///
/// <para>
/// Both files are absent from layers 01–04 (only layer 05 may read game assets at runtime).
/// This class is therefore placed in the <c>Adapters/</c> folder of <c>Client.Godot</c>.
/// </para>
/// </summary>
public sealed class ZoneCatalog
{
    // ── VFS path constants ──────────────────────────────────────────────────
    // spec: Docs/RE/formats/misc_data.md §7.1 — "logical path: data/script/mapsetting.scr".
    private const string MapSettingPath = "data/script/mapsetting.scr"; // spec: misc_data.md §7.1

    // spec: Docs/RE/formats/misc_data.md §7.2 — "logical path pattern: data/mapNNN/regiontableNNN.bin".
    // The three-digit area tag is zero-padded (e.g. "001", "002", "003").
    private const string RegionTablePathFmt = "data/map{0:D3}/regiontable{0:D3}.bin"; // spec: misc_data.md §7.2

    // Maximum area id we attempt to load regiontable files for (areas 0–47 + specials up to 300).
    // We load on-demand rather than pre-loading all areas.
    private const int RegionTableAreaCeiling = 300; // spec: misc_data.md §7.1 — "ids 100, 203–208, 300 present"

    // ── Lazy state ──────────────────────────────────────────────────────────

    private readonly RealClientAssets? _assets;

    // mapsetting.scr: all zone records, loaded once. Null means "not yet attempted".
    private MapZoneRecord[]? _zones;
    private bool _zonesAttempted;

    // regiontable cache: areaId → records. Loaded on-demand per area. Null value = load failed.
    private readonly Dictionary<int, RegionTableRecord[]?> _regionCache = new();

    // ── Constructor ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="ZoneCatalog"/> backed by the supplied <paramref name="assets"/> handle.
    /// Pass <see langword="null"/> for offline/no-VFS mode; all lookup methods return empty strings.
    /// </summary>
    public ZoneCatalog(RealClientAssets? assets)
    {
        _assets = assets;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the CP949-decoded display name of the zone whose bounding box contains the given
    /// world XZ position, or an empty string when the position is outside all zones or the VFS is
    /// offline.
    ///
    /// The bounding-box fields are PLAUSIBLE — see spec grade in:
    /// spec: Docs/RE/formats/misc_data.md §7.1 — world_min_x/z i32 @ 0x28/0x2C: PLAUSIBLE;
    ///       world_max_x/z i32 @ 0x30/0x34: PLAUSIBLE.
    ///
    /// Because the world geometry negates Z (spec: CLAUDE.md — "World geometry negates Z"),
    /// <paramref name="legacyWorldZ"/> is the LEGACY (left-handed) Z coordinate, not the Godot one.
    /// </summary>
    /// <param name="legacyWorldX">Legacy world X position.</param>
    /// <param name="legacyWorldZ">Legacy world Z position (before Godot Z-negate).</param>
    public string GetZoneName(float legacyWorldX, float legacyWorldZ)
    {
        MapZoneRecord[]? zones = EnsureZones();
        if (zones is null) return string.Empty;

        // Walk all zone records; return the first whose bounding box contains the position.
        // The bounding box is in legacy world space (X-Z plane), fields PLAUSIBLE.
        // spec: Docs/RE/formats/misc_data.md §7.1 — bounding box: PLAUSIBLE.
        foreach (MapZoneRecord z in zones)
        {
            // Guard against degenerate records (zone_id == 0 or empty name).
            if (z.ZoneId == 0 || string.IsNullOrEmpty(z.ZoneName)) continue;

            if (legacyWorldX >= z.WorldMinX && legacyWorldX <= z.WorldMaxX &&
                legacyWorldZ >= z.WorldMinZ && legacyWorldZ <= z.WorldMaxZ)
            {
                return z.ZoneName;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the zone record whose bounding box contains <paramref name="legacyWorldX"/> /
    /// <paramref name="legacyWorldZ"/>, or <see langword="null"/> when none matches.
    ///
    /// spec: Docs/RE/formats/misc_data.md §7.1 — zone_id: SAMPLE-VERIFIED.
    /// </summary>
    public MapZoneRecord? GetZoneRecord(float legacyWorldX, float legacyWorldZ)
    {
        MapZoneRecord[]? zones = EnsureZones();
        if (zones is null) return null;

        foreach (MapZoneRecord z in zones)
        {
            if (z.ZoneId == 0 || string.IsNullOrEmpty(z.ZoneName)) continue;
            if (legacyWorldX >= z.WorldMinX && legacyWorldX <= z.WorldMaxX &&
                legacyWorldZ >= z.WorldMinZ && legacyWorldZ <= z.WorldMaxZ)
                return z;
        }

        return null;
    }

    /// <summary>
    /// Returns the nearest sub-zone label from <c>regiontableNNN.bin</c> for the given area and
    /// world position, or an empty string when the file is absent / VFS is offline.
    ///
    /// spec: Docs/RE/formats/misc_data.md §7.2 — sub_zone_name char[16] CP949 @ 0x10: PLAUSIBLE.
    ///
    /// Coordinates in the regiontable records are PLAUSIBLE; garbage-coordinate records are
    /// filtered by validating against the area bounding box when a zone record is available.
    ///
    /// spec: Docs/RE/formats/misc_data.md §7.2 — "two sub-types under one stride (open)": UNKNOWN discriminator.
    /// </summary>
    /// <param name="areaId">The zone/area id (from <see cref="MapZoneRecord.ZoneId"/>).</param>
    /// <param name="legacyWorldX">Legacy world X position.</param>
    /// <param name="legacyWorldZ">Legacy world Z position.</param>
    /// <param name="zoneRecord">Optional zone record for bounding-box validation of coordinates.</param>
    public string GetNearestSubZoneName(int areaId, float legacyWorldX, float legacyWorldZ,
        MapZoneRecord? zoneRecord = null)
    {
        RegionTableRecord[]? recs = EnsureRegionTable(areaId);
        if (recs is null || recs.Length == 0) return string.Empty;

        string best = string.Empty;
        float bestDist = float.MaxValue;

        foreach (RegionTableRecord r in recs)
        {
            if (string.IsNullOrWhiteSpace(r.SubZoneName)) continue;

            // Validate that the coordinate is plausible (within the area bounding box).
            // spec: Docs/RE/formats/misc_data.md §7.2 — "validate CenterX/CenterZ against the
            //       area bounding box before trusting them as coordinates."
            if (zoneRecord is not null)
            {
                if (r.CenterX < zoneRecord.WorldMinX || r.CenterX > zoneRecord.WorldMaxX ||
                    r.CenterZ < zoneRecord.WorldMinZ || r.CenterZ > zoneRecord.WorldMaxZ)
                    continue; // garbage-float record — skip.
            }

            float dx = r.CenterX - legacyWorldX;
            float dz = r.CenterZ - legacyWorldZ;
            float dist = dx * dx + dz * dz; // squared distance — no sqrt needed for comparison
            if (dist < bestDist)
            {
                bestDist = dist;
                best = r.SubZoneName;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns all loaded zone records (may be empty when VFS is offline).
    /// The caller must not mutate the returned array.
    /// spec: Docs/RE/formats/misc_data.md §7.1.
    /// </summary>
    public IReadOnlyList<MapZoneRecord> AllZones => EnsureZones() ?? Array.Empty<MapZoneRecord>();

    // ── Lazy loaders ───────────────────────────────────────────────────────

    private MapZoneRecord[]? EnsureZones()
    {
        if (_zonesAttempted) return _zones;
        _zonesAttempted = true;

        if (_assets is null) return null;

        try
        {
            // spec: Docs/RE/formats/misc_data.md §7.1 — "logical path: data/script/mapsetting.scr".
            ReadOnlyMemory<byte> raw = _assets.GetRaw(MapSettingPath);
            if (raw.IsEmpty)
            {
                GodotPrint($"[ZoneCatalog] {MapSettingPath} absent from VFS — zone names unavailable.");
                return null;
            }

            // spec: Docs/RE/formats/misc_data.md §7.1 — stride 84 bytes, 52 records: SAMPLE-VERIFIED.
            _zones = MapSettingScrParser.Parse(raw);
            GodotPrint($"[ZoneCatalog] mapsetting.scr loaded: {_zones.Length} zone records. " +
                       "spec: Docs/RE/formats/misc_data.md §7.1 SAMPLE-VERIFIED.");
        }
        catch (InvalidDataException ex)
        {
            GodotPrintErr($"[ZoneCatalog] mapsetting.scr parse failed: {ex.Message}");
            _zones = null;
        }
        catch (Exception ex)
        {
            GodotPrintErr($"[ZoneCatalog] mapsetting.scr load error: {ex.Message}");
            _zones = null;
        }

        return _zones;
    }

    private RegionTableRecord[]? EnsureRegionTable(int areaId)
    {
        if (_regionCache.TryGetValue(areaId, out RegionTableRecord[]? cached)) return cached;

        if (_assets is null)
        {
            _regionCache[areaId] = null;
            return null;
        }

        if (areaId < 0 || areaId > RegionTableAreaCeiling)
        {
            _regionCache[areaId] = null;
            return null;
        }

        try
        {
            // spec: Docs/RE/formats/misc_data.md §7.2 — "path pattern: data/mapNNN/regiontableNNN.bin".
            string path = string.Format(RegionTablePathFmt, areaId);
            ReadOnlyMemory<byte> raw = _assets.GetRaw(path);
            if (raw.IsEmpty)
            {
                // Area has no regiontable — normal for some areas.
                _regionCache[areaId] = null;
                return null;
            }

            // spec: Docs/RE/formats/misc_data.md §7.2 — stride 32 bytes, 52 records: SAMPLE-VERIFIED.
            RegionTableRecord[] recs = RegionTableParser.Parse(raw);
            _regionCache[areaId] = recs;
            GodotPrint($"[ZoneCatalog] regiontable{areaId:D3}.bin loaded: {recs.Length} records. " +
                       "spec: Docs/RE/formats/misc_data.md §7.2 SAMPLE-VERIFIED.");
            return recs;
        }
        catch (InvalidDataException ex)
        {
            GodotPrintErr($"[ZoneCatalog] regiontable{areaId:D3}.bin parse failed: {ex.Message}");
            _regionCache[areaId] = null;
            return null;
        }
        catch (Exception ex)
        {
            GodotPrintErr($"[ZoneCatalog] regiontable{areaId:D3}.bin load error: {ex.Message}");
            _regionCache[areaId] = null;
            return null;
        }
    }

    // ── Thin Godot-print shims ─────────────────────────────────────────────
    // Isolated so the class is testable without Godot — swap for Console.Write in tests.

    private static void GodotPrint(string msg) => GD.Print(msg);
    private static void GodotPrintErr(string msg) => GD.PrintErr(msg);
}