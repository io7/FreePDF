// Examples for docs/guide/security.md. MIT; see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.Samples;

public sealed class SecuritySamples : SampleTest
{
    [Fact]
    public void Metadata()
    {
        // begin-snippet: metadata
        using var document = new PdfDocument();
        document.Info.Title = "Invoice 2026-118";
        document.Info.Author = "Example Ltd";
        document.Info.Subject = "Invoice";
        document.Info.Keywords = "invoice, 2026";
        document.AddPage();
        document.Save("metadata.pdf");
        // end-snippet

        using var saved = PdfReader.Open("metadata.pdf", PdfDocumentOpenMode.Import);
        Assert.Equal("Invoice 2026-118", saved.Info.Title);
        Assert.Equal("Example Ltd", saved.Info.Author);
        Assert.Matches(@"^FreePDF \d+\.\d+\.\d+", saved.Info.Producer);   // name and version
    }

    [Fact]
    public void Passwords()
    {
        // begin-snippet: passwords
        using var document = new PdfDocument();
        document.AddPage();
        document.SecurityHandler.SetEncryptionToV5();            // AES-256
        document.SecuritySettings.UserPassword = "open-me";      // needed to open the file
        document.SecuritySettings.OwnerPassword = "change-me";   // needed to change it
        document.SecuritySettings.PermitPrint = false;
        document.SecuritySettings.PermitModifyDocument = false;
        document.Save("protected.pdf");
        // end-snippet

        Assert.Throws<PdfReaderException>(() => PdfReader.Open("protected.pdf", PdfDocumentOpenMode.Import));
        using var opened = PdfReader.Open("protected.pdf", "open-me", PdfDocumentOpenMode.Import);
        Assert.Equal(5, opened.SecurityHandler.Elements.GetInteger("/V"));
        Assert.False(opened.SecuritySettings.HasFullPermission);
        Assert.False(opened.SecuritySettings.PermitPrint);
    }
}
