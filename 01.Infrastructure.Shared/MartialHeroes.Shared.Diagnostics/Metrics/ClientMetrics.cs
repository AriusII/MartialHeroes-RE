using System.Diagnostics.Metrics;

namespace MartialHeroes.Shared.Diagnostics.Metrics;

/// <summary>
/// Shared <see cref="Meter"/> and pre-created instruments for the Martial Heroes client.
/// </summary>
/// <remarks>
/// <para>
/// Instruments are created once at class-initialisation time (static fields) and never
/// re-created. Recording a measurement on a <see cref="Counter{T}"/> or
/// <see cref="Histogram{T}"/> is allocation-free on the hot path — the only allocation is
/// the <c>TagList</c> if you pass tags, and that can be avoided by passing
/// <c>ReadOnlySpan&lt;KeyValuePair&lt;string,object?&gt;&gt;</c> directly.
/// </para>
/// <para>
/// The meter name <c>"MartialHeroes.Client"</c> is the stable string used by OpenTelemetry
/// exporters and metric scrapers to locate these instruments. Do not change it without a
/// migration plan.
/// </para>
/// </remarks>
public static class ClientMetrics
{
    /// <summary>
    /// The shared <see cref="Meter"/> for all client-side instrumentation.
    /// Meter name: <c>"MartialHeroes.Client"</c>.
    /// </summary>
    public static readonly Meter Meter = new("MartialHeroes.Client", version: "1.0.0");

    // -------------------------------------------------------------------------
    // Network instruments
    // -------------------------------------------------------------------------

    /// <summary>
    /// Counts the total number of raw bytes received from the server socket.
    /// Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Counter<long> BytesReceived =
        Meter.CreateCounter<long>(
            name: "network.bytes_received",
            unit: "bytes",
            description: "Total raw bytes received from the server.");

    /// <summary>
    /// Counts the total number of raw bytes sent to the server socket.
    /// Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Counter<long> BytesSent =
        Meter.CreateCounter<long>(
            name: "network.bytes_sent",
            unit: "bytes",
            description: "Total raw bytes sent to the server.");

    /// <summary>
    /// Counts the number of fully decoded packets processed by the protocol layer.
    /// Unit: <c>{packets}</c>.
    /// </summary>
    public static readonly Counter<long> PacketsProcessed =
        Meter.CreateCounter<long>(
            name: "network.packets_processed",
            unit: "{packets}",
            description: "Number of fully decoded inbound packets processed.");

    /// <summary>
    /// Records the size distribution of inbound packets after decryption.
    /// Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Histogram<long> PacketSizeBytes =
        Meter.CreateHistogram<long>(
            name: "network.packet_size",
            unit: "bytes",
            description: "Size distribution of inbound packets (post-decryption).");

    // -------------------------------------------------------------------------
    // Asset instruments
    // -------------------------------------------------------------------------

    /// <summary>
    /// Counts the total number of virtual assets loaded from mounted archives.
    /// Unit: <c>{assets}</c>.
    /// </summary>
    public static readonly Counter<long> AssetsLoaded =
        Meter.CreateCounter<long>(
            name: "assets.loaded",
            unit: "{assets}",
            description: "Total number of virtual assets loaded from pak archives.");

    /// <summary>
    /// Counts the total number of bytes read from pak archives during asset loading.
    /// Unit: <c>bytes</c>.
    /// </summary>
    public static readonly Counter<long> AssetBytesRead =
        Meter.CreateCounter<long>(
            name: "assets.bytes_read",
            unit: "bytes",
            description: "Total bytes read from pak archives during asset loading.");

    /// <summary>
    /// Records the decode time distribution for individual assets.
    /// Unit: <c>ms</c>.
    /// </summary>
    public static readonly Histogram<long> AssetDecodeMs =
        Meter.CreateHistogram<long>(
            name: "assets.decode_duration",
            unit: "ms",
            description: "Decode time distribution for individual assets.");
}