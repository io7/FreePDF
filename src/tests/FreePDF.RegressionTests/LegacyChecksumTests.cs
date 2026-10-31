// Original tests: RFC 1321 published digest vectors and the platform MD5 oracle.
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public class LegacyChecksumTests
{
    static HashAlgorithm Create() => (HashAlgorithm)typeof(PdfDocument).Assembly
        .GetType("PdfSharp.Pdf.Security.MD5Managed", true)!.GetMethod("Create", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!.Invoke(null, null)!;

    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("a", "0cc175b9c0f1b6a831c399e269772661")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("message digest", "f96b697d7cb7938d525a2f31aaf161d0")]
    [InlineData("abcdefghijklmnopqrstuvwxyz", "c3fcd3d76192e4007dfb496cca67e13b")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", "d174ab98d277d9f5a5611c2c9f419d9f")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345678901234567890", "57edf4a22be3c955ac49da2e2107b67a")]
    public void Published_vectors_match(string value, string digest)
    {
        using var hash = Create();
        Assert.Equal(128, hash.HashSize);
        Assert.Equal(digest, BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(value))).Replace("-", "").ToLowerInvariant());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(7)] [InlineData(55)] [InlineData(56)] [InlineData(57)]
    [InlineData(63)] [InlineData(64)] [InlineData(65)] [InlineData(119)] [InlineData(120)]
    [InlineData(127)] [InlineData(128)] [InlineData(129)] [InlineData(1000)] [InlineData(65536)]
    public void Offsets_padding_chunking_and_reuse_match_the_platform(int length)
    {
        var bytes = Enumerable.Range(0, length + 17).Select(i => (byte)(i * 71 + 23)).ToArray();
        using var platform = MD5.Create();
        var expected = platform.ComputeHash(bytes, 7, length);
        using var actual = Create();
        Assert.Equal(expected, actual.ComputeHash(bytes, 7, length));
        actual.Initialize();
        var buffer = new byte[257];
        for (var position = 0; position < length;)
        {
            var count = Math.Min(1 + position % 257, length - position);
            actual.TransformBlock(bytes, 7 + position, count, buffer, 0);
            position += count;
        }
        actual.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        Assert.Equal(expected, actual.Hash);
        Assert.Equal(expected, actual.ComputeHash(bytes, 7, length));
    }

    [Fact]
    public void Million_a_vector_matches()
    {
        using var hash = Create();
        Assert.Equal("7707d6ae4e027c70eea2a935c2296f21", BitConverter.ToString(hash.ComputeHash(Enumerable.Repeat((byte)'a', 1000000).ToArray())).Replace("-", "").ToLowerInvariant());
    }

#if NETFRAMEWORK
    [Fact]
    public void Framework_dictionary_helper_preserves_null_values_and_existing_keys()
    {
        var values = new Dictionary<string, string?>();
        Assert.True(PdfSharp.DotNetFrameworkExtensions.DictionaryExtensions.TryAdd(values, "key", null));
        Assert.False(PdfSharp.DotNetFrameworkExtensions.DictionaryExtensions.TryAdd(values, "key", "changed"));
        Assert.Null(values["key"]);
    }
#endif
}
