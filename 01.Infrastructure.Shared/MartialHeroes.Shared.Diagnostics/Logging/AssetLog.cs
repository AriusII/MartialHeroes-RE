using Microsoft.Extensions.Logging;

namespace MartialHeroes.Shared.Diagnostics.Logging;

/// <summary>
/// Source-generated, allocation-free log methods for the asset loading pipeline.
/// </summary>
/// <remarks>
/// All methods are compile-time generated via <c>[LoggerMessage]</c>. Parameters are kept
/// to primitive types (<see cref="string"/>, <see cref="int"/>, <see cref="long"/>) to avoid
/// boxing on the hot path.
/// </remarks>
public static partial class AssetLog
{
    // EventId range 2000–2099 reserved for asset/VFS concerns.

    /// <summary>
    /// Logged when a <c>.pak</c> archive is successfully memory-mapped and its directory
    /// index has been built.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The file-system path of the mounted archive.</param>
    /// <param name="entryCount">The number of virtual file entries found in the archive.</param>
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Archive mounted: path='{Path}' entries={EntryCount}")]
    public static partial void ArchiveMounted(ILogger logger, string path, int entryCount);

    /// <summary>
    /// Logged when an individual virtual asset has been decoded and is ready for use.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="virtualPath">The virtual path within the archive (e.g. <c>mesh/player/warrior.msh</c>).</param>
    /// <param name="bytes">The size of the decoded asset in bytes.</param>
    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Debug,
        Message = "Asset loaded: '{VirtualPath}' ({Bytes} bytes)")]
    public static partial void AssetLoaded(ILogger logger, string virtualPath, long bytes);

    /// <summary>
    /// Logged when decoding an asset fails (e.g. corrupt header, unsupported version).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="virtualPath">The virtual path of the asset that failed to decode.</param>
    /// <param name="reason">A short description of the decode failure.</param>
    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "Asset decode failed: '{VirtualPath}' — {Reason}")]
    public static partial void AssetDecodeFailed(ILogger logger, string virtualPath, string reason);

    /// <summary>
    /// Logged when a requested virtual path cannot be found in any mounted archive.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="virtualPath">The virtual path that was requested.</param>
    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Asset not found: '{VirtualPath}'")]
    public static partial void AssetNotFound(ILogger logger, string virtualPath);

    /// <summary>
    /// Logged when an archive is unmounted and its memory-mapped resources are released.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="path">The file-system path of the archive being unmounted.</param>
    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Information,
        Message = "Archive unmounted: path='{Path}'")]
    public static partial void ArchiveUnmounted(ILogger logger, string path);
}
