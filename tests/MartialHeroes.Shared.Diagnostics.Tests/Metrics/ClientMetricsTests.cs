using System.Diagnostics.Metrics;
using MartialHeroes.Shared.Diagnostics.Metrics;

namespace MartialHeroes.Shared.Diagnostics.Tests.Metrics;

/// <summary>
/// Pins the <see cref="ClientMetrics"/> public contract via a <see cref="MeterListener"/>:
/// the stable meter name and the full set of instrument names + units. These strings are the
/// scrape/export surface — ClientMetrics.cs warns "Do not change it without a migration plan".
/// </summary>
public sealed class ClientMetricsTests
{
    private const string MeterName = "MartialHeroes.Client";

    /// <summary>Subscribes a listener to the ClientMetrics meter and returns every published instrument.</summary>
    private static List<Instrument> EnumerateInstruments()
    {
        var instruments = new List<Instrument>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == MeterName)
                {
                    instruments.Add(instrument);
                }
            },
        };

        listener.Start();

        // Touch every static instrument so the meter publishes them to the listener.
        // (Static-field instruments are created on first type access.)
        _ = ClientMetrics.BytesReceived;
        _ = ClientMetrics.BytesSent;
        _ = ClientMetrics.PacketsProcessed;
        _ = ClientMetrics.PacketSizeBytes;
        _ = ClientMetrics.AssetsLoaded;
        _ = ClientMetrics.AssetBytesRead;
        _ = ClientMetrics.AssetDecodeMs;

        return instruments;
    }

    [Fact]
    public void MeterName_IsStableContract()
    {
        // spec: ClientMetrics.cs:28 — meter name "MartialHeroes.Client", "Do not change it without a migration plan".
        Assert.Equal(MeterName, ClientMetrics.Meter.Name);
    }

    [Fact]
    public void Meter_Publishes_AllSevenInstruments()
    {
        // spec: ClientMetrics.cs — 4 network + 3 asset instruments.
        var instruments = EnumerateInstruments();
        Assert.Equal(7, instruments.Count);
    }

    [Theory]
    // network instruments — spec: ClientMetrics.cs
    [InlineData("network.bytes_received", "bytes")]
    [InlineData("network.bytes_sent", "bytes")]
    [InlineData("network.packets_processed", "{packets}")]
    [InlineData("network.packet_size", "bytes")]
    // asset instruments — spec: ClientMetrics.cs
    [InlineData("assets.loaded", "{assets}")]
    [InlineData("assets.bytes_read", "bytes")]
    [InlineData("assets.decode_duration", "ms")]
    public void Instrument_HasExpectedNameAndUnit(string name, string unit)
    {
        var instruments = EnumerateInstruments();
        var instrument = Assert.Single(instruments, i => i.Name == name);
        Assert.Equal(unit, instrument.Unit);
    }
}
