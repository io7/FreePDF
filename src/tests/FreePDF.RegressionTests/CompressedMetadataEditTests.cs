// PdfMetadata.SetMetadata kept the /Filter of the old content, so plain XML set on compressed metadata was written
// under /FlateDecode and could not be read back. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class CompressedMetadataEditTests
{
    const string Xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
        "<original/></x:xmpmeta><?xpacket end=\"w\"?>";

    static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.Append)]
    public void Metadata_set_on_compressed_metadata_can_be_read(PdfDocumentOpenMode mode)
    {
        byte[] original;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            var metadata = document.Catalog.GetOrCreateMetadata();
            metadata.Elements.SetName("/Filter", "/FlateDecode");
            metadata.CreateStream(Filtering.FlateDecode.Encode(TestPdf.Latin1.GetBytes(Xmp), PdfFlateEncodeMode.Default));
            original = Save(document);
        }

        byte[] result;
        using (var document = PdfReader.Open(new MemoryStream(original), mode))
        {
            var metadata = document.Catalog.GetMetadata()!;
            Assert.Contains("<original/>", metadata.ToString());
            metadata.SetMetadata(metadata.ToString().Replace("<original/>", "<edited/>"));
            result = Save(document);
        }

        using var reopened = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Contains("<edited/>", reopened.Catalog.GetMetadata()!.ToString());
    }
}
