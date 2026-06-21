using System.Diagnostics.Metrics;

namespace MartialHeroes.Shared.Diagnostics.Metrics;

/// <summary>
///     Shared <see cref="Meter" /> and pre-created instruments for the Martial Heroes client.
/// </summary>
/// <remarks>
///     <para>
///         Instruments are created once at class-initialisation time (static fields) and never
///         re-created. Recording a measurement on a <see cref="Counter{T}" /> or
///         <see cref="Histogram{T}" /> is allocation-free on the hot path — the only allocation is
///         the <c>TagList</c> if you pass tags, and that can be avoided by passing
///         <c>ReadOnlySpan&lt;KeyValuePair&lt;string,object?&gt;&gt;</c> directly.
///     </para>
///     <para>
///         The meter name <c>"MartialHeroes.Client"</c> is the stable string used by OpenTelemetry
///         exporters and metric scrapers to locate these instruments. Do not change it without a
///         migration plan.
///     </para>
/// </remarks>
public static class ClientMetrics
{
    /// <summary>
    ///     The shared <see cref="Meter" /> for all client-side instrumentation.
    ///     Meter name: <c>"MartialHeroes.Client"</c>.
    /// </summary>
    public static readonly Meter Meter = new("MartialHeroes.Client", "1.0.0");

    // -------------------------------------------------------------------------
    // Network instruments
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Counts the total number of raw bytes received from the server socket.
    ///     Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Counter<long> BytesReceived =
        Meter.CreateCounter<long>(
            "network.bytes_received",
            "bytes",
            "Total raw bytes received from the server.");

    /// <summary>
    ///     Counts the total number of raw bytes sent to the server socket.
    ///     Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Counter<long> BytesSent =
        Meter.CreateCounter<long>(
            "network.bytes_sent",
            "bytes",
            "Total raw bytes sent to the server.");

    /// <summary>
    ///     Counts the number of fully decoded packets processed by the protocol layer.
    ///     Unit: <c>{packets}</c>.
    /// </summary>
    public static readonly Counter<long> PacketsProcessed =
        Meter.CreateCounter<long>(
            "network.packets_processed",
            "{packets}",
            "Number of fully decoded inbound packets processed.");

    /// <summary>
    ///     Records the size distribution of inbound packets after LZ4 decompression.
    ///     The inbound (S2C) path is decompress-only — there is no inverse byte cipher, so the
    ///     measurement point is post-decompression, not post-decryption.
    ///     spec: Docs/RE/specs/client_architecture.md §7 — "Inbound (S2C): LZ4-decompress ONLY ... no inverse byte cipher".
    ///     Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Histogram<long> PacketSizeBytes =
        Meter.CreateHistogram<long>(
            "network.packet_size",
            "bytes",
            "Size distribution of inbound packets (post-decompression).");

    // -------------------------------------------------------------------------
    // Asset instruments
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Counts the total number of virtual assets loaded from mounted archives.
    ///     Unit: <c>{assets}</c>.
    /// </summary>
    public static readonly Counter<long> AssetsLoaded =
        Meter.CreateCounter<long>(
            "assets.loaded",
            "{assets}",
            "Total number of virtual assets loaded from pak archives.");

    /// <summary>
    ///     Counts the total number of bytes read from pak archives during asset loading.
    ///     Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Counter<long> AssetBytesRead =
        Meter.CreateCounter<long>(
            "assets.bytes_read",
            "bytes",
            "Total bytes read from pak archives during asset loading.");

    /// <summary>
    ///     Records the decode time distribution for individual assets.
    ///     Unit: <c>ms</c>.
    /// </summary>
    public static readonly Histogram<long> AssetDecodeMs =
        Meter.CreateHistogram<long>(
            "assets.decode_duration",
            "ms",
            "Decode time distribution for individual assets.");
}