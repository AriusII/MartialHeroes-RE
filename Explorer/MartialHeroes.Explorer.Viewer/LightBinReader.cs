using System.Buffers.Binary;
using Godot;

namespace MartialHeroes.Explorer.Viewer;

public static class LightBinReader
{
    public const int BlobSize = 5312;
    private const int KeyframeCount = 48;
    private const int DirTableOffset = 0;
    private const int DirDisableFlagsOffset = 2304;
    private const int AmbientByteTableOffset = 5088;
    private const int SunDirectionOffset = 5288;
    private const int BytesPerDirKeyframe = 48;
    private const int DiffuseIntraOffset = 16;
    private const int AmbientByteStride = 4;

    public static LightFrame? TryRead(ReadOnlySpan<byte> blob, int kf)
    {
        if (blob.Length < BlobSize) return null;
        if ((uint)kf >= KeyframeCount) return null;

        var directionalEnabled = blob[DirDisableFlagsOffset + kf] == 0;

        var dcBase = DirTableOffset + BytesPerDirKeyframe * kf + DiffuseIntraOffset;
        var dr = BinaryPrimitives.ReadSingleLittleEndian(blob[dcBase..]);
        var dg = BinaryPrimitives.ReadSingleLittleEndian(blob[(dcBase + 4)..]);
        var db = BinaryPrimitives.ReadSingleLittleEndian(blob[(dcBase + 8)..]);

        var ambBase = AmbientByteTableOffset + AmbientByteStride * kf;
        var ar = blob[ambBase] / 255f;
        var ag = blob[ambBase + 1] / 255f;
        var ab = blob[ambBase + 2] / 255f;

        var sx = BinaryPrimitives.ReadSingleLittleEndian(blob[SunDirectionOffset..]);
        var sy = BinaryPrimitives.ReadSingleLittleEndian(blob[(SunDirectionOffset + 4)..]);
        var sz = BinaryPrimitives.ReadSingleLittleEndian(blob[(SunDirectionOffset + 8)..]);
        var rawDir = new Vector3(sx, sy, -sz);
        var sunDirGodot = rawDir.LengthSquared() > 1e-6f ? rawDir.Normalized() : Vector3.Up;

        return new LightFrame(sunDirGodot, new Color(dr, dg, db), directionalEnabled, new Color(ar, ag, ab));
    }

    public readonly record struct LightFrame(
        Vector3 SunDirectionGodot,
        Color DirectionalColor,
        bool DirectionalEnabled,
        Color AmbientColor);
}