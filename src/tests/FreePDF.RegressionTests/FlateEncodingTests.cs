// FreePDF regression tests. Licensed under MIT; see LICENSE.
using System.IO.Compression;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Filters;
using Xunit;

namespace FreePDF.RegressionTests;

public class FlateEncodingTests
{
    public static IEnumerable<object[]> ChecksumVectors()
    {
        foreach (var mode in new[] { PdfFlateEncodeMode.Default, PdfFlateEncodeMode.BestSpeed, PdfFlateEncodeMode.BestCompression })
        {
            yield return new object[] { "", 0x00000001u, mode };
            yield return new object[] { "a", 0x00620062u, mode };
            yield return new object[] { "abc", 0x024d0127u, mode };
            yield return new object[] { "Wikipedia", 0x11e60398u, mode };
        }
    }

    [Theory]
    [MemberData(nameof(ChecksumVectors))]
    public void Encoded_stream_has_zlib_header_final_block_and_checksum(string text, uint expectedChecksum, PdfFlateEncodeMode mode)
    {
        var data = Encoding.ASCII.GetBytes(text);
        var encoded = new FlateDecode().Encode(data, mode);
        Assert.True(encoded.Length >= 8, "A zlib stream needs a header, a final DEFLATE block, and a checksum.");
        Assert.Equal(8, encoded[0] & 15);
        Assert.Equal(0, ((encoded[0] << 8) | encoded[1]) % 31);
        var last = encoded.Length - 4;
        var checksum = ((uint)encoded[last] << 24) | ((uint)encoded[last + 1] << 16) |
                       ((uint)encoded[last + 2] << 8) | encoded[last + 3];
        Assert.Equal(expectedChecksum, checksum);
        Assert.Equal(data, new FlateDecode().Decode(encoded, (FilterParms?)null));
#if NET8_0_OR_GREATER
        using var input = new MemoryStream(encoded);
        using var decoder = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        decoder.CopyTo(output);
        Assert.Equal(data, output.ToArray());
#endif
    }

#if NET8_0_OR_GREATER
    [Theory]
    [InlineData(5551, false)]
    [InlineData(5552, false)]
    [InlineData(5553, false)]
    [InlineData(65536, false)]
    [InlineData(5552, true)]
    [InlineData(5553, true)]
    [InlineData(65536, true)]
    public void Large_stream_matches_the_platform_zlib_checksum(int length, bool maximumBytes)
    {
        var data = new byte[length];
        if (maximumBytes)
            Array.Fill(data, byte.MaxValue);
        else
            new Random(625).NextBytes(data);
        var encoded = new FlateDecode().Encode(data);
        using var reference = new MemoryStream();
        using (var encoder = new ZLibStream(reference, CompressionLevel.Optimal, true))
            encoder.Write(data, 0, data.Length);
        Assert.Equal(reference.ToArray().Skip((int)reference.Length - 4), encoded.Skip(encoded.Length - 4));
        using var decoder = new ZLibStream(new MemoryStream(encoded), CompressionMode.Decompress);
        using var output = new MemoryStream();
        decoder.CopyTo(output);
        Assert.Equal(data, output.ToArray());
    }
#endif
}
