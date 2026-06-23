using MartialHeroes.Assets.Parsers.Audio;
using MartialHeroes.Assets.Parsers.Audio.Models;
using MartialHeroes.Assets.Parsers.Terrain;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Assets.Parsers.World;

namespace MartialHeroes.Assets.Mapping;

public sealed class AreaComposer
{
    public const int PoolSize = 34;
    public const int RingSize = 25;
    public const int RingEdge = 5;
    public const int CenterSlot = 12;
    private readonly PoolEntry[] _pool = CreatePool();
    private readonly int[] _ring = CreateRing();
    private int _centerMapX;
    private int _centerMapZ;
    private bool _ringInitialized;

    public AssembledCell? CenterCell
    {
        get
        {
            var poolIdx = _ring[CenterSlot];
            return poolIdx >= 0 ? _pool[poolIdx].Cell : null;
        }
    }

    private static PoolEntry[] CreatePool()
    {
        var p = new PoolEntry[PoolSize];
        for (var i = 0; i < PoolSize; i++) p[i] = new PoolEntry();
        return p;
    }

    private static int[] CreateRing()
    {
        var r = new int[RingSize];
        for (var i = 0; i < RingSize; i++) r[i] = -1;
        return r;
    }


    public AssembledArea ComposeArea(IAreaAssemblySource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var cellKeys = source.AreaCellKeys;

        var spawns = BuildSpawns(source);

        var cells = new Dictionary<(int MapX, int MapZ), AssembledCell>(cellKeys.Count);
        foreach (var (mapX, mapZ) in cellKeys)
        {
            var cell = ComposeCell(source, mapX, mapZ);
            cells[(mapX, mapZ)] = cell;
        }

        return new AssembledArea
        {
            AreaId = source.AreaId,
            CellKeys = cellKeys.ToList(),
            Spawns = spawns,
            Cells = cells
        };
    }


    public AssembledCell ComposeCell(IAreaAssemblySource source, int mapX, int mapZ)
    {
        ArgumentNullException.ThrowIfNull(source);

        MudSoundGrid? soundGrid = null;
        if (source.TryGetCellFile(mapX, mapZ, ".mud", out var mudBytes))
            try
            {
                soundGrid = MudSoundGridParser.Parse(mudBytes);
            }
            catch (InvalidDataException)
            {
            }


        if (!source.TryGetCellFile(mapX, mapZ, ".map", out var mapBytes))
            return new AssembledCell
            {
                MapX = mapX,
                MapZ = mapZ,
                SoundGrid = soundGrid
            };

        var mapDesc = MapDescriptorParser.Parse(mapBytes);

        TerrainCell? slot0Ted = null;
        BudScene? slot1Bud = null;
        Fx1Layer? slot2Fx1 = null;
        Fx2Layer? slot3Fx2 = null;
        Fx3Layer? slot4Fx3 = null;
        Fx4Layer? slot5Fx4 = null;
        Fx5Layer? slot6Fx5 = null;
        Fx6Layer? slot7Fx6 = null;
        Fx7Layer? slot8Fx7 = null;
        SodBlob? collision = null;
        CollisionTriangleList? extraTerrainTriangles = null;
        CollisionTriangleList? overhangTriangles = null;
        (int Flag, int TexId)[]? terrainTextures = null;
        (int Flag, int TexId)[]? buildingTextures = null;

        foreach (var section in mapDesc.Sections)
        {
            var key = section.Keyword;

            if (key == "TERRAIN" && section.Textures.Length > 0)
                terrainTextures = section.Textures;

            if (key == "BUILDING" && section.Textures.Length > 0)
                buildingTextures = section.Textures;

            if (section.DataFile is null)
                continue;

            if (!source.TryGetCellFileByName(section.DataFile, out var subBytes))
                continue;

            try
            {
                switch (key)
                {
                    case "TERRAIN":
                        slot0Ted = TedTerrainParser.Parse(subBytes);
                        break;

                    case "BUILDING":
                        slot1Bud = TerrainSceneParser.Parse(subBytes);
                        break;

                    case "SOLID":
                        collision = SodBlobParser.Parse(subBytes);
                        break;

                    case "EXTRA_TERRAIN":
                        extraTerrainTriangles = TerrainLayerParsers.ParseUpOrExd(subBytes);
                        break;

                    case "UP_TERRAIN":
                        overhangTriangles = TerrainLayerParsers.ParseUpOrExd(subBytes);
                        break;

                    case "FX1":
                        slot2Fx1 = TerrainLayerParsers.ParseFx1(subBytes);
                        break;

                    case "FX2":
                        slot3Fx2 = TerrainLayerParsers.ParseFx2(subBytes);
                        break;

                    case "FX3":
                        slot4Fx3 = TerrainLayerParsers.ParseFx3(subBytes);
                        break;

                    case "FX4":
                        slot5Fx4 = TerrainLayerParsers.ParseFx4(subBytes);
                        break;

                    case "FX5":
                        slot6Fx5 = TerrainLayerParsers.ParseFx5(subBytes);
                        break;

                    case "FX6":
                        slot7Fx6 = TerrainLayerParsers.ParseFx6(subBytes);
                        break;

                    case "FX7":
                        slot8Fx7 = TerrainLayerParsers.ParseFx7(subBytes);
                        break;
                }
            }
            catch (InvalidDataException)
            {
            }
        }

        var bgCatalog = BgTextureCatalog.FromLst(source.TerrainTextureCatalog);

        string?[]? resolvedPaths = null;
        if (slot0Ted is not null && terrainTextures is not null && terrainTextures.Length > 0)
            resolvedPaths = ResolveTexturePaths(slot0Ted.TextureIndexGrid, terrainTextures, bgCatalog);

        string?[]? resolvedBuildingPaths = null;
        if (buildingTextures is not null && buildingTextures.Length > 0)
            resolvedBuildingPaths = ResolveBuildingTexturePaths(buildingTextures, bgCatalog);

        return new AssembledCell
        {
            MapX = mapX,
            MapZ = mapZ,
            Slot0GroundTexGrid = slot0Ted,
            Slot1BuildingObjectGrid = slot1Bud,
            Slot2Fx1 = slot2Fx1,
            Slot3Fx2 = slot3Fx2,
            Slot4Fx3 = slot4Fx3,
            Slot5Fx4 = slot5Fx4,
            Slot6Fx5 = slot6Fx5,
            Slot7Fx6 = slot7Fx6,
            Slot8Fx7 = slot8Fx7,
            Collision = collision,
            ExtraTerrainTriangles = extraTerrainTriangles,
            OverhangTriangles = overhangTriangles,
            SoundGrid = soundGrid,
            ResolvedTexturePaths = resolvedPaths,
            ResolvedBuildingTexturePaths = resolvedBuildingPaths
        };
    }


    public int AcquirePoolSlot(int mapX, int mapZ, int areaId)
    {
        for (var i = 0; i < PoolSize; i++)
            if (_pool[i].Loaded
                && _pool[i].MapX == mapX
                && _pool[i].MapZ == mapZ
                && _pool[i].AreaId == areaId)
                return i;

        for (var i = 0; i < PoolSize; i++)
            if (!_pool[i].Loaded)
            {
                _pool[i].MapX = mapX;
                _pool[i].MapZ = mapZ;
                _pool[i].AreaId = areaId;
                return i;
            }

        return -1;
    }

    public void RecyclePoolSlot(int poolIndex)
    {
        if ((uint)poolIndex >= PoolSize)
            throw new ArgumentOutOfRangeException(nameof(poolIndex),
                $"Pool index {poolIndex} is out of range [0, {PoolSize - 1}]. " +
                "spec: Docs/RE/structs/terrain-manager.md — pool_slots[34]: CONFIRMED.");

        _pool[poolIndex].Loaded = false;
        _pool[poolIndex].Cell = null;
    }

    public int LoadCellIntoPool(IAreaAssemblySource source, int mapX, int mapZ)
    {
        ArgumentNullException.ThrowIfNull(source);

        var slot = AcquirePoolSlot(mapX, mapZ, source.AreaId);
        if (slot < 0)
            return -1;

        if (!_pool[slot].Loaded)
        {
            _pool[slot].Cell = ComposeCell(source, mapX, mapZ);
            _pool[slot].Loaded = true;
        }

        return slot;
    }


    public void RecenterRing(IAreaAssemblySource source, int centerMapX, int centerMapZ)
    {
        ArgumentNullException.ThrowIfNull(source);

        var halfEdge = RingEdge / 2;

        var newRingCoords = new (int MapX, int MapZ)[RingSize];
        for (var row = 0; row < RingEdge; row++)
        for (var col = 0; col < RingEdge; col++)
        {
            var slot = RingEdge * row + col;
            newRingCoords[slot] = (centerMapX - halfEdge + col, centerMapZ - halfEdge + row);
        }

        if (_ringInitialized)
            for (var i = 0; i < RingSize; i++)
            {
                var oldPoolIdx = _ring[i];
                if (oldPoolIdx < 0)
                    continue;

                var oldEntry = _pool[oldPoolIdx];
                var stillNeeded = false;
                for (var j = 0; j < RingSize; j++)
                    if (newRingCoords[j].MapX == oldEntry.MapX
                        && newRingCoords[j].MapZ == oldEntry.MapZ)
                    {
                        stillNeeded = true;
                        break;
                    }

                if (!stillNeeded)
                    RecyclePoolSlot(oldPoolIdx);
            }

        var memberSet = new HashSet<(int MapX, int MapZ)>(source.AreaCellKeys);

        for (var i = 0; i < RingSize; i++)
        {
            var (mx, mz) = newRingCoords[i];

            if (!memberSet.Contains((mx, mz)))
            {
                _ring[i] = -1;
                continue;
            }

            var poolIdx = LoadCellIntoPool(source, mx, mz);
            _ring[i] = poolIdx;
        }

        _centerMapX = centerMapX;
        _centerMapZ = centerMapZ;
        _ringInitialized = true;
    }


    public int GetRingPoolIndex(int ringSlot)
    {
        if ((uint)ringSlot >= RingSize)
            throw new ArgumentOutOfRangeException(nameof(ringSlot),
                $"Ring slot {ringSlot} is out of range [0, {RingSize - 1}]. " +
                "spec: Docs/RE/structs/terrain-manager.md — ring_slots[25]: CONFIRMED.");
        return _ring[ringSlot];
    }

    public AssembledCell? GetRingCell(int ringSlot)
    {
        var poolIdx = GetRingPoolIndex(ringSlot);
        return poolIdx >= 0 ? _pool[poolIdx].Cell : null;
    }


    private static string?[] ResolveTexturePaths(
        byte[] textureIndexGrid,
        (int Flag, int TexId)[] terrainTextures,
        BgTextureCatalog bgCatalog)
    {
        var count = terrainTextures.Length;
        var paths = new string?[textureIndexGrid.Length];

        for (var i = 0; i < textureIndexGrid.Length; i++)
        {
            var rawByte = textureIndexGrid[i];

            if (rawByte < 1 || rawByte > count)
                rawByte = 1;

            var listIdx = rawByte - 1;

            if (listIdx < 0 || listIdx >= terrainTextures.Length)
            {
                paths[i] = null;
                continue;
            }

            var intTexId = terrainTextures[listIdx].TexId;

            paths[i] = bgCatalog.ResolveTexturePath(intTexId);
        }

        return paths;
    }

    private static string?[] ResolveBuildingTexturePaths(
        (int Flag, int TexId)[] buildingTextures,
        BgTextureCatalog bgCatalog)
    {
        var paths = new string?[buildingTextures.Length];

        for (var i = 0; i < buildingTextures.Length; i++)
        {
            var intTexId = buildingTextures[i].TexId;
            paths[i] = bgCatalog.ResolveTexturePath(intTexId);
        }

        return paths;
    }


    private static IReadOnlyList<SpawnDescriptor> BuildSpawns(IAreaAssemblySource source)
    {
        var spawns = new List<SpawnDescriptor>();
        var areaId = source.AreaId;

        var npcArrPath = $"data/map{areaId:D3}/npc{areaId:D3}.arr";
        if (source.TryGetCellFileByName(npcArrPath, out var npcBytes))
        {
            var npcArr = NpcSpawnParser.Parse(npcBytes);
            foreach (var rec in npcArr.Records)
            {
                var yaw = MathF.PI / 2f - rec.Facing;

                spawns.Add(new SpawnDescriptor
                {
                    WorldX = rec.WorldX,
                    WorldZ = rec.WorldZ,
                    Yaw = yaw,
                    VisualId = rec.MobId,
                    IsNpc = true
                });
            }
        }

        return spawns;
    }


    private sealed class PoolEntry
    {
        public int MapX { get; set; }
        public int MapZ { get; set; }
        public int AreaId { get; set; }
        public bool Loaded { get; set; }
        public AssembledCell? Cell { get; set; }
    }
}