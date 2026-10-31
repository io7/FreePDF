// Package-only consumer: deliberately has no source project references. MIT.
using System.Reflection;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.RtfRendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;

GlobalFontSettings.FontResolver = new SmokeFontResolver();
var document = new Document();
(document.Styles[StyleNames.Normal] ?? throw new InvalidDataException("Default document style missing.")).Font.Name = "Package Font";
document.AddSection().AddParagraph("FreePDF installed-package check");
var renderer = new PdfDocumentRenderer { Document = document };
renderer.RenderDocument();
using var pdf = new MemoryStream();
renderer.PdfDocument.Save(pdf); pdf.Position=0;
using var reopened = PdfReader.Open(pdf,PdfDocumentOpenMode.Modify);
if (reopened.PageCount != 1) throw new InvalidDataException("Package PDF round trip failed.");
using var rtf = new MemoryStream();
new RtfDocumentRenderer().Render(document,rtf,false,"");
if (!Encoding.ASCII.GetString(rtf.ToArray()).StartsWith("{\\rtf",StringComparison.Ordinal))
    throw new InvalidDataException("Package RTF render failed.");
var expectedVersion = typeof(SmokeFontResolver).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(a => a.Key == "ExpectedFreePDFVersion").Value!;
foreach (var name in new[]{"PdfSharp","PdfSharp.Shared","PdfSharp.System","PdfSharp.Imaging","PdfSharp.OpenType","PdfSharp.BarCodes","PdfSharp.Charting","PdfSharp.Cryptography","MigraDoc.DocumentObjectModel","MigraDoc.Rendering","MigraDoc.RtfRendering"})
{
    var assembly = Assembly.Load(name);
    var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
    if (version.Split('+')[0] != expectedVersion) throw new InvalidDataException("Unexpected assembly: " + name + " " + version);
}
Console.WriteLine("PASS: installed FreePDF PDF/RTF output, reopen, and all 11 packaged assemblies; runtime " + Environment.Version);
if (args.Length == 2) ReleaseWorkflows.Run(args[0], args[1], expectedVersion);
else if (args.Length != 0) throw new ArgumentException("Usage: PackageSmoke [output-directory synthetic-certificate.pfx]");

sealed class SmokeFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName,bool bold,bool italic) => new("package",bold,italic);
    public byte[] GetFont(string faceName)
    {
        using var stream = typeof(SmokeFontResolver).Assembly.GetManifestResourceStream("PackageFont.ttf")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
}
