using System.Text;
using MartialHeroes.Assets.Parsers;
using Xunit;

namespace MartialHeroes.Assets.Parsers.Tests;

/// <summary>
/// Fixture tests for <see cref="SkinTxtParser"/>.
/// spec: Docs/RE/formats/text_tables.md §skin.txt; Docs/RE/formats/texture.md §The skin chain.
/// </summary>
public sealed class SkinTxtParserTests
{
    [Fact]
    public void ParseText_CountPrefixedSixIntegerRows_DecodesMeshGidAndTextureId()
    {
        const string text =
            "2\r\n" +
            "0\t1\t2\t3\t101100001\t419000410\r\n" +
            "1\t4\t5\t6\t101100002\t419000411\r\n";

        var catalog = SkinTxtParser.ParseText(text);

        Assert.Equal(2, catalog.Count);
        Assert.Equal(101100001, catalog.Entries[0].MeshGid);
        Assert.Equal(419000410, catalog.Entries[0].TextureId);
    }

    [Fact]
    public void GetByMeshGid_ImplementsSknIdAToSkinTxtCol4Join()
    {
        const string text =
            "2\n" +
            "0\t0\t0\t0\t111\t9001\n" +
            "0\t0\t0\t0\t222\t9002\n";

        var catalog = SkinTxtParser.ParseText(text);

        Assert.Equal(9002, catalog.GetByMeshGid(222)!.TextureId);
        Assert.Null(catalog.GetByMeshGid(333));
    }

    [Fact]
    public void Parse_RawBytes_DecodesCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        byte[] bytes = Encoding.GetEncoding(949).GetBytes("1\r\n0\t1\t2\t3\t10\t20\r\n");

        var catalog = SkinTxtParser.Parse(bytes.AsMemory());

        Assert.Single(catalog.Entries);
        Assert.Equal(20, catalog.GetByMeshGid(10)!.TextureId);
    }
}