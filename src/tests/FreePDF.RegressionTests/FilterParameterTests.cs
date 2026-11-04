// Stream filters and their parameters in the forms ISO 32000-1 7.3.8 allows: a null entry in a /DecodeParms array
// means no parameters, a filter name may be an indirect object, and a crypt filter's data was decrypted when the file
// was read. Each threw when the stream was decoded. Original fixtures; MIT, see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class FilterParameterTests
{
    // "Hello" in ASCIIHex.
    const string Hello = "48656C6C6F>";

    static PdfDocument WithStream(string streamEntries, params string[] more)
    {
        var file = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Data 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            $"<< /Length {Hello.Length} {streamEntries} >>\nstream\n{Hello}\nendstream",
        }.Concat(more).ToArray());
        return PdfReader.Open(new MemoryStream(file), PdfDocumentOpenMode.Import);
    }

    static string Decoded(PdfDocument document)
        => TestPdf.Latin1.GetString(document.Catalog.Elements.GetDictionary("/Data")!.Stream!.UnfilteredValue);

    [Theory]
    [InlineData("/Filter [/ASCIIHexDecode] /DecodeParms [null]")]
    [InlineData("/Filter [/ASCIIHexDecode] /DecodeParms null")]
    [InlineData("/Filter /ASCIIHexDecode /DecodeParms [<< >>]")]
    [InlineData("/Filter [/ASCIIHexDecode] /DecodeParms << >>")]
    [InlineData("/Filter [/Crypt /ASCIIHexDecode] /DecodeParms [<< /Type /CryptFilterDecodeParms /Name /Identity >> null]")]
    [InlineData("/Filter /Crypt")]
    // As FreePDF 7.6.0 and earlier wrote a crypt filter in front of another filter.
    [InlineData("/Filter [/Crypt /ASCIIHexDecode] /DecodeParms << /Type /CryptFilterDecodeParms /Name /Identity >>")]
    public void Filters_and_parameters_in_every_allowed_form_are_decoded(string streamEntries)
    {
        using var document = WithStream(streamEntries);
        var expected = streamEntries == "/Filter /Crypt" ? Hello : "Hello";
        Assert.Equal(expected, Decoded(document));
    }

    [Fact]
    public void A_filter_name_held_as_an_indirect_object_is_read()
    {
        using var document = WithStream("/Filter 5 0 R /DecodeParms 6 0 R", "/ASCIIHexDecode", "null");
        Assert.Equal("Hello", Decoded(document));
    }

    static byte[] EncryptedWithoutEncryptingMetadata(bool withMetadata, bool aes)
    {
        using var document = new PdfDocument();
        document.AddPage();
        if (withMetadata)
        {
            document.Catalog.GetOrCreateMetadata().SetMetadata("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><metadata-check/></x:xmpmeta><?xpacket end=\"w\"?>");
        }
        if (aes)
            document.SecurityHandler.SetEncryptionToV5(encryptMetadata: false);
        else
            document.SecurityHandler.SetEncryptionToV4UsingRC4(encryptMetadata: false);
        document.SecuritySettings.UserPassword = "user";
        document.SecuritySettings.OwnerPassword = "owner";
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // With EncryptMetadata false, FreePDF marks the document's metadata with the Identity crypt filter; reading it
    // back threw "Filter not implemented: Crypt".
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unencrypted_document_metadata_in_an_encrypted_file_can_be_read(bool aes)
    {
        var file = EncryptedWithoutEncryptingMetadata(withMetadata: true, aes);

        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        var metadata = reopened.Catalog.Elements.GetDictionary("/Metadata")!;
        Assert.Contains("/Crypt", metadata.Elements["/Filter"]!.ToString());
        Assert.Contains("<metadata-check/>", TestPdf.Latin1.GetString(metadata.Stream!.UnfilteredValue));
        Assert.Contains("<metadata-check/>", TestPdf.Latin1.GetString(file));
    }

    // Review of #47: compressed document metadata got /Filter [/Crypt /FlateDecode] with a lone /DecodeParms
    // dictionary, which ISO 32000-1 Table 5 does not allow and FreePDF could not decode. /DecodeParms is now an array
    // with one entry per filter.
    // Second round: direct parameter dictionaries threw "A direct object can only be added once", and an indirect
    // filter name went into the array as a reference, which pyHanko does not take.
    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "")]
    [InlineData(true, "direct-dictionary")]
    [InlineData(true, "direct-array")]
    [InlineData(false, "indirect-name")]
    public void Compressed_document_metadata_gets_aligned_crypt_filter_parameters(bool aes, string shape)
    {
        const string xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">" +
            "<compressed-check/></x:xmpmeta><?xpacket end=\"w\"?>";
        byte[] file;
        using (var document = new PdfDocument())
        {
            document.AddPage();
            var metadata = new PdfDictionary(document);
            metadata.Elements.SetName("/Type", "/Metadata");
            metadata.Elements.SetName("/Subtype", "/XML");
            if (shape == "indirect-name")
                metadata.Elements["/Filter"] = new PdfNameObject(document, "/FlateDecode").Reference;
            else
                metadata.Elements.SetName("/Filter", "/FlateDecode");
            var predictor = new PdfDictionary(document);
            predictor.Elements.SetInteger("/Predictor", 1);
            if (shape == "direct-dictionary")
                metadata.Elements["/DecodeParms"] = predictor;
            else if (shape == "direct-array")
            {
                var parameterArray = new PdfArray(document);
                metadata.Elements["/DecodeParms"] = parameterArray;
                parameterArray.Elements.Add(predictor);
            }
            metadata.CreateStream(PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(TestPdf.Latin1.GetBytes(xmp), PdfFlateEncodeMode.Default));
            document.Internals.AddObject(metadata);
            document.Catalog.Elements.SetObject("/Metadata", metadata);
            if (aes)
                document.SecurityHandler.SetEncryptionToV5(encryptMetadata: false);
            else
                document.SecurityHandler.SetEncryptionToV4UsingRC4(encryptMetadata: false);
            document.SecuritySettings.UserPassword = "user";
            document.SecuritySettings.OwnerPassword = "owner";
            using var output = new MemoryStream();
            document.Save(output);
            file = output.ToArray();
        }

        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        var written = reopened.Catalog.Elements.GetDictionary("/Metadata")!;
        Assert.Equal("[/Crypt /FlateDecode]", written.Elements["/Filter"]!.ToString());
        var parameters = written.Elements.GetArray("/DecodeParms")!;
        Assert.Equal(2, parameters.Elements.Count);
        Assert.Equal("/Identity", parameters.Elements.GetDictionary(0)!.Elements.GetName("/Name"));
        if (shape.StartsWith("direct", StringComparison.Ordinal))
            Assert.Equal(1, parameters.Elements.GetDictionary(1)!.Elements.GetInteger("/Predictor"));
        Assert.Contains("<compressed-check/>", TestPdf.Latin1.GetString(written.Stream!.UnfilteredValue));
    }

    [Fact]
    public void Setting_the_crypt_filter_twice_and_resetting_it_keeps_the_filters_aligned()
    {
        using var document = new PdfDocument();
        document.SecurityHandler.SetEncryptionToV5();
        var stream = new PdfDictionary(document);
        stream.Elements.SetName("/Filter", "/FlateDecode");
        var predictor = new PdfDictionary(document);
        predictor.Elements.SetInteger("/Predictor", 1);
        stream.Elements["/DecodeParms"] = predictor;
        stream.CreateStream([1, 2, 3]);

        document.SecurityHandler.SetIdentityCryptFilter(stream);
        document.SecurityHandler.SetIdentityCryptFilter(stream);
        Assert.Equal("[/Crypt /FlateDecode]", stream.Elements["/Filter"]!.ToString());
        var parameters = stream.Elements.GetArray("/DecodeParms")!;
        Assert.Equal(2, parameters.Elements.Count);
        Assert.Equal(1, parameters.Elements.GetDictionary(1)!.Elements.GetInteger("/Predictor"));

        document.SecurityHandler.ResetCryptFilter(stream);
        Assert.Equal("/FlateDecode", stream.Elements["/Filter"]!.ToString());
        Assert.Equal(1, stream.Elements.GetDictionary("/DecodeParms")!.Elements.GetInteger("/Predictor"));

        // The layout FreePDF 7.6.0 and earlier wrote: a lone crypt filter dictionary next to two filters.
        stream.Elements["/Filter"] = new PdfArray(document, new PdfName("/Crypt"), new PdfName("/FlateDecode"));
        var crypt = new PdfDictionary(document);
        crypt.Elements.SetName("/Type", "/CryptFilterDecodeParms");
        crypt.Elements.SetName("/Name", "/Identity");
        stream.Elements["/DecodeParms"] = crypt;
        document.SecurityHandler.ResetCryptFilter(stream);
        Assert.Equal("/FlateDecode", stream.Elements["/Filter"]!.ToString());
        Assert.False(stream.Elements.ContainsKey("/DecodeParms"));
    }

    // A document without metadata got a /Metadata dictionary that held only the crypt filter and no stream, which
    // ISO 32000-1 Table 28 does not allow.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_document_without_metadata_gets_none_when_metadata_is_not_encrypted(bool aes)
    {
        var file = EncryptedWithoutEncryptingMetadata(withMetadata: false, aes);

        using var reopened = PdfReader.Open(new MemoryStream(file), "user", PdfDocumentOpenMode.Import);
        Assert.False(reopened.Catalog.Elements.ContainsKey("/Metadata"));
    }
}
