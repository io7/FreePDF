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

    // An interactive form whose field tree is Depth levels deep: field n has the single kid n + 1; the last is a widget.
    static byte[] DeepFields()
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [{Depth + 4} 0 R] >>",
            "<< >>",
        };
        for (int index = 0; index < Depth; index++)
        {
            int number = index + 5;
            var parent = index == 0 ? "" : $" /Parent {number - 1} 0 R";
            objects.Add(index < Depth - 1
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

    [Fact]
    public void A_deep_field_tree_is_cut_at_the_deepest_level_kept()
    {
        using var document = PdfReader.Open(new MemoryStream(DeepFields()), PdfDocumentOpenMode.Modify);
        var field = Assert.Single(document.Catalog.AcroForm!.Fields.Cast<PdfSharp.Pdf.Forms.PdfFormField>());
        int depth = 0;
        while (field.HasKids)
        {
            field = Assert.Single(field.Kids.Cast<PdfSharp.Pdf.Forms.PdfFormField>());
            depth++;
        }
        Assert.Equal(100, depth);
        document.Save(new MemoryStream());
    }

#if NET8_0_OR_GREATER
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
