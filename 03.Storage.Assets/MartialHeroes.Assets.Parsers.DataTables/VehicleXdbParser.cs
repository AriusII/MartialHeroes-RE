using MartialHeroes.Assets.Parsers.DataTables.Models;

namespace MartialHeroes.Assets.Parsers.DataTables;

public sealed class VehicleXdbTable
{
    private readonly Dictionary<uint, VehicleXdbRecord> _byVehicleId;

    private VehicleXdbTable(VehicleXdbRecord[] records)
    {
        Records = records;
        _byVehicleId = new Dictionary<uint, VehicleXdbRecord>(records.Length);
        foreach (var r in records)
            _byVehicleId[r.VehicleId] = r;
    }

    public IReadOnlyList<VehicleXdbRecord> Records { get; }

    public static VehicleXdbTable Parse(ReadOnlyMemory<byte> data)
    {
        var records = XdbParser.ParseVehicleXdb(data);
        return new VehicleXdbTable(records);
    }

    public VehicleXdbRecord? TryGetByVehicleId(uint vehicleId)
    {
        return _byVehicleId.TryGetValue(vehicleId, out var r) ? r : null;
    }
}

public static class VehicleXdbParser
{
    public static VehicleXdbTable Parse(ReadOnlyMemory<byte> data)
    {
        return VehicleXdbTable.Parse(data);
    }
}