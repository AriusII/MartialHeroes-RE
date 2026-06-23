using System.Diagnostics.Metrics;

namespace MartialHeroes.Shared.Diagnostics.Metrics;

public static class ClientMetrics
{
    public static readonly Meter Meter = new("MartialHeroes.Client", "1.0.0");


    public static readonly Counter<long> BytesReceived =
        Meter.CreateCounter<long>(
            "network.bytes_received",
            "bytes",
            "Total raw bytes received from the server.");

    public static readonly Counter<long> BytesSent =
        Meter.CreateCounter<long>(
            "network.bytes_sent",
            "bytes",
            "Total raw bytes sent to the server.");

    public static readonly Counter<long> PacketsProcessed =
        Meter.CreateCounter<long>(
            "network.packets_processed",
            "{packets}",
            "Number of fully decoded inbound packets processed.");

    public static readonly Histogram<long> PacketSizeBytes =
        Meter.CreateHistogram<long>(
            "network.packet_size",
            "bytes",
            "Size distribution of inbound packets (post-decompression).");


    public static readonly Counter<long> AssetsLoaded =
        Meter.CreateCounter<long>(
            "assets.loaded",
            "{assets}",
            "Total number of virtual assets loaded from pak archives.");

    public static readonly Counter<long> AssetBytesRead =
        Meter.CreateCounter<long>(
            "assets.bytes_read",
            "bytes",
            "Total bytes read from pak archives during asset loading.");

    public static readonly Histogram<long> AssetDecodeMs =
        Meter.CreateHistogram<long>(
            "assets.decode_duration",
            "ms",
            "Decode time distribution for individual assets.");
}