using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Logging;

public static partial class AssetLog
{

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Archive mounted: path='{Path}' entries={EntryCount}")]
    public static partial void ArchiveMounted(ILogger logger, string path, int entryCount);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Asset loaded: '{VirtualPath}' ({Bytes} bytes)")]
    public static partial void AssetLoaded(ILogger logger, string virtualPath, long bytes);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Asset decode failed: '{VirtualPath}' — {Reason}")]
    public static partial void AssetDecodeFailed(ILogger logger, string virtualPath, string reason);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Asset not found: '{VirtualPath}'")]
    public static partial void AssetNotFound(ILogger logger, string virtualPath);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Archive unmounted: path='{Path}'")]
    public static partial void ArchiveUnmounted(ILogger logger, string path);
}