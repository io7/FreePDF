// PdfMetadata.SetMetadata(string) with the XMP packet headers that the XMP specification allows: the begin attribute
// in single or double quotes, empty or holding the byte order mark U+FEFF. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using Xunit;

namespace FreePDF.RegressionTests;

public class XmpPacketHeaderTests
{
    const string Body = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><header-check/></x:xmpmeta>";

    static byte[] Written(string xml)
    {
        var document = new PdfDocument();
        document.AddPage();
        var metadata = document.Catalog.GetOrCreateMetadata();
        metadata.SetMetadata(xml);
        return metadata.Stream!.Value;
    }

    // ISO 32000-1 14.3.2: metadata in a PDF file is UTF-8, and the packet header holds its byte order mark.
    static void AssertUtf8Packet(byte[] written, char quote)
    {
        var expected = new byte[] { (byte)'<', (byte)'?', (byte)'x', (byte)'p', (byte)'a', (byte)'c', (byte)'k', (byte)'e', (byte)'t', (byte)' ',
            (byte)'b', (byte)'e', (byte)'g', (byte)'i', (byte)'n', (byte)'=', (byte)quote, 0xEF, 0xBB, 0xBF, (byte)quote };
        Assert.Equal(expected, written.Take(expected.Length).ToArray());
        var text = TestPdf.Latin1.GetString(written);
        Assert.Contains("<header-check/>", text);
        Assert.Contains("id=" + quote + "W5M0MpCehiHzreSzNTczkc9d" + quote, text);
    }

    [Theory]
    [InlineData('"', "")]
    [InlineData('\'', "")]
    [InlineData('"', "\uFEFF")]
    [InlineData('\'', "\uFEFF")]
    [InlineData('\'', "UTF8")]
    public void A_valid_packet_header_is_accepted(char quote, string begin)
    {
        var xml = $"<?xpacket begin={quote}{begin}{quote} id={quote}W5M0MpCehiHzreSzNTczkc9d{quote}?>{Body}<?xpacket end={quote}w{quote}?>";

        AssertUtf8Packet(Written(xml), quote);
    }

    [Fact]
    public void Single_quotes_are_read_back_as_they_were_written()
    {
        var document = new PdfDocument();
        document.AddPage();
        var metadata = document.Catalog.GetOrCreateMetadata();
        metadata.SetMetadata($"<?xpacket begin='' id='W5M0MpCehiHzreSzNTczkc9d'?>{Body}<?xpacket end='w'?>");

        var text = metadata.ToString();
        Assert.StartsWith("<?xpacket begin='UTF8' id='W5M0MpCehiHzreSzNTczkc9d'?>", text);
        metadata.SetMetadata(text);
        AssertUtf8Packet(metadata.Stream!.Value, '\'');
    }

    [Theory]
    [InlineData("<?xpacket begin=")]
    [InlineData("<?xpacket begin=\"")]
    [InlineData("<?xpacket begin='\" id='x'?>")]
    [InlineData("<?xpacket begin=x id='x'?>")]
    public void A_broken_packet_header_is_refused(string xml)
    {
        var document = new PdfDocument();
        var metadata = document.Catalog.GetOrCreateMetadata();

        Assert.Throws<InvalidOperationException>(() => metadata.SetMetadata(xml));
    }
}
