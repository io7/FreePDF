// Small hostile files whose structure is nested tens of thousands of levels deep. Reading each recursed once per level and
// overflowed the stack, which ends the process (no catch stops it), in every open mode and so also in
// PdfSignatureVerifier: direct arrays nested inside each other, a chain of streams whose /Length is the next stream, and a
// form field tree. Each now opens, or fails with an ordinary exception. Found in a Codex review of 7.10. Original
// fixtures; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class HostileNestingTests
{
    const int Depth = 20_000;

    static readonly string[] Start =
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    };

    // A page with an entry that is an array nested 50,000 levels deep.
    static byte[] NestedArrays()
    {
        const int levels = 50_000;
        var nested = new StringBuilder(levels * 2);
        nested.Append('[', levels).Append(']', levels);
        return TestPdf.Create(Start.Concat(new[] { $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Hostile {nested} >>" }).ToArray());
    }

    // Objects 4 to Depth + 3 are streams; each one's /Length is the next one, and the last has a number.
    static byte[] StreamLengthChain()
    {
        var objects = Start.Concat(new[] { "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>" }).ToList();
        for (int index = 0; index < Depth; index++)
        {
            int number = index + 4;
            var length = index < Depth - 1 ? $"{number + 1} 0 R" : "1";
            objects.Add($"<< /Length {length} >>\nstream\nx\nendstream");
        }
        return TestPdf.Create(objects.ToArray());
    }

    // An interactive form whose field tree is depth levels deep: field n has the single kid n + 1; the last is a widget.
    static byte[] DeepFields(int depth = Depth)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{depth + 4} 0 R] >>",
            "<< >>",
        };
        for (int index = 0; index < depth; index++)
        {
            int number = index + 5;
            var parent = index == 0 ? "" : $" /Parent {number - 1} 0 R";
            objects.Add(index < depth - 1
                ? $"<< /T (f){parent} /Kids [{number + 1} 0 R] >>"
                : $"<< /Type /Annot /Subtype /Widget /FT /Tx /Rect [10 10 100 30] /P 3 0 R{parent} >>");
        }
        return TestPdf.Create(objects.ToArray());
    }

    public static TheoryData<string> Files => new() { nameof(NestedArrays), nameof(StreamLengthChain), nameof(DeepFields) };

    static byte[] File(string name) => name switch
    {
        nameof(NestedArrays) => NestedArrays(),
        nameof(StreamLengthChain) => StreamLengthChain(),
        _ => DeepFields(),
    };

    // Opening either works or throws an ordinary exception; saving what opened works too.
    static void OpenAndSave(byte[] bytes, PdfDocumentOpenMode mode)
    {
        PdfDocument document;
        try
        {
            document = PdfReader.Open(new MemoryStream(bytes), mode);
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or ObjectNotAvailableException)
        {
            return;
        }
        using (document)
        {
            if (mode != PdfDocumentOpenMode.Import)
                document.Save(new MemoryStream());
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Opens_in_Import_mode(string file) => OpenAndSave(File(file), PdfDocumentOpenMode.Import);

    [Theory]
    [MemberData(nameof(Files))]
    public void Opens_and_saves_in_Modify_mode(string file) => OpenAndSave(File(file), PdfDocumentOpenMode.Modify);

    [Theory]
    [MemberData(nameof(Files))]
    public void Opens_and_saves_in_Append_mode(string file) => OpenAndSave(File(file), PdfDocumentOpenMode.Append);

    [Fact]
    public void A_stream_length_chain_is_read_from_endstream()
    {
        using var document = PdfReader.Open(new MemoryStream(StreamLengthChain()), PdfDocumentOpenMode.Import);
        Assert.Single(document.Pages);
    }

    // A field tree whose fields with kids reach 100 levels below the top opens; one level more is refused. Dropping the
    // deeper fields instead would hide them from signature checks.
    [Fact]
    public void A_field_tree_is_read_to_100_levels_and_refused_beyond()
    {
        using (var document = PdfReader.Open(new MemoryStream(DeepFields(101)), PdfDocumentOpenMode.Modify))
            document.Save(new MemoryStream());

        Assert.Throws<PdfReaderException>(() => PdfReader.Open(new MemoryStream(DeepFields(102)), PdfDocumentOpenMode.Import));
    }

    // A text field whose /Parent is itself: inherited values, flags and the full name followed /Parent without end (a stack
    // overflow, or a loop that never stopped).
    [Fact]
    public void A_field_that_is_its_own_parent_can_be_read_and_flattened()
    {
        var bytes = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (/Helv 0 Tf 0 g) >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /Parent 4 0 R /Rect [10 10 150 30] /P 3 0 R >>",
        });

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        var field = Assert.Single(document.Catalog.AcroForm!.Fields.Cast<PdfSharp.Pdf.Forms.PdfFormField>());
        Assert.Equal("name", field.FullName);
        Assert.Null(field.Value);
        document.Flatten();
        document.Save(new MemoryStream());
    }

    // A cross-reference section whose count is a run of 100,000 'R' tokens: an old fallback in the integer reader read an
    // object ID after each 'R', recursing once per token.
    [Fact]
    public void A_run_of_R_tokens_in_the_cross_reference_section_does_not_recurse()
    {
        var head = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n"
                   + "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n";
        var xref = head.Length;
        var bytes = Encoding.ASCII.GetBytes(head + "xref\n0 " + string.Concat(Enumerable.Repeat("R ", 100_000))
                                            + "\ntrailer\n<< /Root 1 0 R /Size 4 >>\nstartxref\n" + xref + "\n%%EOF\n");

        foreach (var mode in new[] { PdfDocumentOpenMode.Import, PdfDocumentOpenMode.Modify, PdfDocumentOpenMode.Append })
            OpenAndSave(bytes, mode);
    }

    // Clearing the value of a field that is its own parent, and asking a typeless one for its type, followed /Parent
    // without end.
    [Fact]
    public void A_field_that_is_its_own_parent_can_be_cleared_and_typed()
    {
        var bytes = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (x) /Parent 4 0 R /Rect [10 10 150 30] /P 3 0 R >>",
            "<< /T (node) /Parent 5 0 R /Kids [] >>",
        });

        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
        var fields = document.Catalog.AcroForm!.Fields.Cast<PdfSharp.Pdf.Forms.PdfFormField>().ToList();
        var text = fields.Single(f => f.Name == "name");
        text.Value = null;
        Assert.Null(text.Value);
        var node = fields.Single(f => f.Name == "node");
        _ = Record.Exception(() => node.FieldType);
    }

    // A metadata stream whose /Filter is an array that holds itself: decoding recursed without end.
    [Fact]
    public void A_filter_array_that_holds_itself_is_not_applied()
    {
        var bytes = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            "<< /Type /Metadata /Subtype /XML /Filter 5 0 R /Length 3 >>\nstream\nabc\nendstream",
            "[5 0 R]",
        });

        foreach (var mode in new[] { PdfDocumentOpenMode.Import, PdfDocumentOpenMode.Modify, PdfDocumentOpenMode.Append })
            OpenAndSave(bytes, mode);
    }

    // XMP metadata whose pdfaid:part holds 50,000 nested elements: the XML DOM read and wrote it recursively.
    [Fact]
    public void Deeply_nested_XMP_is_not_read()
    {
        const int levels = 50_000;
        var nested = new StringBuilder();
        for (int level = 0; level < levels; level++)
            nested.Append("<x>");
        nested.Append('1');
        for (int level = 0; level < levels; level++)
            nested.Append("</x>");
        var packet = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
                     + "<rdf:Description rdf:about=\"\" xmlns:pdfaid=\"http://www.aiim.org/pdfa/ns/id/\"><pdfaid:part>" + nested
                     + "</pdfaid:part></rdf:Description></rdf:RDF></x:xmpmeta>";
        var bytes = TestPdf.Create(new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /Metadata 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            $"<< /Type /Metadata /Subtype /XML /Length {packet.Length} >>\nstream\n{packet}\nendstream",
        });

        foreach (var mode in new[] { PdfDocumentOpenMode.Import, PdfDocumentOpenMode.Modify, PdfDocumentOpenMode.Append })
            OpenAndSave(bytes, mode);
    }

    // A content stream of 50,000 '[': the content parser recursed once per level.
    [Fact]
    public void Deeply_nested_content_arrays_are_refused()
    {
        var content = Encoding.ASCII.GetBytes(new string('[', 50_000));

        Assert.ThrowsAny<Exception>(() => PdfSharp.Pdf.Content.ContentReader.ReadContent(content));
    }

#if NET8_0_OR_GREATER
    // An earlier revision whose field tree is too deep to read, and an update that empties the form: the verifier took the
    // earlier revision for a %%EOF that is no revision and returned an empty list, though a signature could be in it.
    [Fact]
    public async Task The_signature_verifier_refuses_a_file_whose_earlier_revision_exceeds_the_limits()
    {
        var first = DeepFields(102);
        var text = Encoding.Latin1.GetString(first);
        var startXref = text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9).Trim().Split('\n', '\r')[0];
        var update = new StringBuilder("\n");
        int offset = first.Length + update.Length;
        update.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] >> >>\nendobj\n");
        int xref = first.Length + update.Length;
        update.Append($"xref\n1 1\n{offset:D10} 00000 n\r\ntrailer\n<< /Size {102 + 5} /Root 1 0 R /Prev {startXref} >>\nstartxref\n{xref}\n%%EOF\n");
        var file = first.Concat(Encoding.Latin1.GetBytes(update.ToString())).ToArray();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => PdfSharp.Pdf.Signatures.PdfSignatureVerifier.VerifyAsync(file));
        Assert.Contains("earlier revision", refused.Message);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task The_signature_verifier_does_not_crash(string file)
    {
        try
        {
            Assert.Empty(await PdfSharp.Pdf.Signatures.PdfSignatureVerifier.VerifyAsync(File(file)));
        }
        catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or ObjectNotAvailableException)
        {
        }
    }
#endif
}
