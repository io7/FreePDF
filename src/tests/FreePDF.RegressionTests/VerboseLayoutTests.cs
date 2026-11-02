// The verbose writer layout (PdfDocumentOptions.Layout = Verbose, the Debug-build default) writes a statistics
// comment into the file. It wrote the user and owner passwords of an encrypted document in plain text, and it failed
// for documents whose /EmbeddedFiles name tree was read from a file. Original fixtures; MIT, see LICENSE.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Attachments;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class VerboseLayoutTests
{
    [Fact]
    public void Verbose_layout_does_not_write_the_passwords_of_an_encrypted_document()
    {
        using var document = new PdfDocument();
        document.Options.Layout = PdfWriterLayout.Verbose;
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
            graphics.DrawRectangle(XBrushes.Red, 10, 10, 50, 50);
        document.SecurityHandler.SetEncryptionToV5();
        document.SecuritySettings.UserPassword = "secret-user-4711";
        document.SecuritySettings.OwnerPassword = "secret-owner-0815";

        using var saved = new MemoryStream();
        document.Save(saved);

        var text = Encoding.Latin1.GetString(saved.ToArray());
        Assert.DoesNotContain("secret-user-4711", text);
        Assert.DoesNotContain("secret-owner-0815", text);
        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), "secret-user-4711", PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
    }

    [Fact]
    public void The_embedded_file_count_of_a_read_document_is_available()
    {
        using var document = PdfReader.Open(new MemoryStream(PreservationFixtures.Semantic()), PdfDocumentOpenMode.Modify);

        Assert.Equal(1, EmbeddedFilesManager.ForDocument(document).FileCount);
    }

    [Fact]
    public void Verbose_layout_saves_a_read_document_with_embedded_files()
    {
        using var document = PdfReader.Open(new MemoryStream(PreservationFixtures.Semantic()), PdfDocumentOpenMode.Modify);
        document.Options.Layout = PdfWriterLayout.Verbose;

        using var saved = new MemoryStream();
        document.Save(saved);

        using var reopened = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Import);
        Assert.Equal(1, reopened.PageCount);
    }
}
