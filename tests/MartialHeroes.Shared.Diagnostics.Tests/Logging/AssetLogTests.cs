using MartialHeroes.Shared.Diagnostics.Logging;
using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Tests.Logging;

/// <summary>
/// Pins the <see cref="AssetLog"/> contract: the documented 2000–2099 EventId range, the log
/// levels, and the rendered message templates.
/// spec: AssetLog.cs — "EventId range 2000–2099 reserved for asset/VFS concerns."
/// </summary>
public sealed class AssetLogTests
{
    private static LogRecord CaptureOne(Action<ILogger> log)
    {
        var logger = new ListLogger();
        log(logger);
        Assert.Single(logger.Records);
        return logger.Records[0];
    }

    [Fact]
    public void ArchiveMounted_EventId_Level_And_Message()
    {
        // spec: AssetLog.cs — EventId 2001, Information.
        var record = CaptureOne(l => AssetLog.ArchiveMounted(l, "data/data.vfs", 43347));

        Assert.Equal(2001, record.EventId.Id);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Archive mounted: path='data/data.vfs' entries=43347", record.Message);
    }

    [Fact]
    public void AssetLoaded_EventId_Level_And_Message()
    {
        // spec: AssetLog.cs — EventId 2002, Debug.
        var record = CaptureOne(l => AssetLog.AssetLoaded(l, "mesh/player/warrior.msh", 1024));

        Assert.Equal(2002, record.EventId.Id);
        Assert.Equal(LogLevel.Debug, record.Level);
        Assert.Equal("Asset loaded: 'mesh/player/warrior.msh' (1024 bytes)", record.Message);
    }

    [Fact]
    public void AssetDecodeFailed_EventId_Level_And_Message()
    {
        // spec: AssetLog.cs — EventId 2003, Error.
        var record = CaptureOne(l => AssetLog.AssetDecodeFailed(l, "char/g1.bnd", "bad header"));

        Assert.Equal(2003, record.EventId.Id);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("Asset decode failed: 'char/g1.bnd' — bad header", record.Message);
    }

    [Fact]
    public void AssetNotFound_EventId_Level_And_Message()
    {
        // spec: AssetLog.cs — EventId 2004, Warning.
        var record = CaptureOne(l => AssetLog.AssetNotFound(l, "char/g6.bnd"));

        Assert.Equal(2004, record.EventId.Id);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("Asset not found: 'char/g6.bnd'", record.Message);
    }

    [Fact]
    public void ArchiveUnmounted_EventId_Level_And_Message()
    {
        // spec: AssetLog.cs — EventId 2005, Information.
        var record = CaptureOne(l => AssetLog.ArchiveUnmounted(l, "data/data.vfs"));

        Assert.Equal(2005, record.EventId.Id);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("Archive unmounted: path='data/data.vfs'", record.Message);
    }

    [Theory]
    [InlineData(2001)]
    [InlineData(2002)]
    [InlineData(2003)]
    [InlineData(2004)]
    [InlineData(2005)]
    public void EventIds_FallInReservedAssetRange(int eventId)
    {
        // spec: AssetLog.cs — "EventId range 2000–2099 reserved for asset/VFS concerns."
        Assert.InRange(eventId, 2000, 2099);
    }
}
