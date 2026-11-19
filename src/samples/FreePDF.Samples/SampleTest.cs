// Base class of the guide's examples: each test runs in its own empty folder, with fresh font settings, and reads
// back the PDF its example wrote. MIT; see LICENSE.
using System.Text;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;
using Xunit;

// The examples change process-wide state (the current folder, GlobalFontSettings), so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FreePDF.Samples;

public abstract class SampleTest : IDisposable
{
    readonly string _previousFolder = Environment.CurrentDirectory;

    protected SampleTest()
    {
        Folder = Path.Combine(Path.GetTempPath(), "freepdf-samples", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Environment.CurrentDirectory = Folder;   // the examples save to relative file names
        GlobalFontSettings.ResetFontManagement();
    }

    /// <summary>The folder the example runs in.</summary>
    protected string Folder { get; }

    public void Dispose()
    {
        Environment.CurrentDirectory = _previousFolder;
        GlobalFontSettings.ResetFontManagement();
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
            // A viewer or virus scanner may still hold a file; the temp folder is cleaned up later.
        }
    }

    protected static string Text(string path, int page = 0)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return PdfTextExtractor.GetText(document.Pages[page]);
    }

    protected static IReadOnlyList<PdfTextRun> Runs(string path, int page = 0)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return PdfTextExtractor.GetTextRuns(document.Pages[page]);
    }

    protected static IReadOnlyList<PdfLineSegment> Lines(string path, int page = 0)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return PdfLineExtractor.GetLineSegments(document.Pages[page]);
    }

    /// <summary>Width and height of a page in points. A new page is A4 or Letter by the machine's region.</summary>
    protected static (double Width, double Height) MediaSize(string path, int page = 0)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return (document.Pages[page].Width.Point, document.Pages[page].Height.Point);
    }

    /// <summary>The base font names on the first page, without the subset prefix (ABCDEF+).</summary>
    protected static List<string> EmbeddedFonts(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var fonts = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var names = new List<string>();
        foreach (var key in fonts.Elements.Keys)
        {
            var baseFont = fonts.Elements.GetDictionary(key)!.Elements.GetName("/BaseFont");
            names.Add(baseFont.Substring(baseFont.IndexOf('+') + 1));
        }
        return names;
    }

    /// <summary>The page's content streams, decoded and joined.</summary>
    protected static string Content(string path, int page = 0)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var contents = document.Pages[page].Contents;
        var parts = new List<string>();
        for (int i = 0; i < contents.Elements.Count; i++)
            parts.Add(Encoding.ASCII.GetString(contents.Elements.GetDictionary(i)!.Stream!.UnfilteredValue));
        return string.Join("\n", parts);
    }

    /// <summary>
    /// The index of the page that a link annotation or a bookmark goes to: the first element of its /Dest array, or of
    /// the /D array of its GoTo action.
    /// </summary>
    protected static int DestinationPageIndex(PdfDocument document, PdfDictionary item)
    {
        var destination = item.Elements.GetArray("/Dest") ?? item.Elements.GetDictionary("/A")?.Elements.GetArray("/D");
        var target = Assert.IsType<PdfReference>(destination?.Elements[0]);
        for (int i = 0; i < document.PageCount; i++)
        {
            if (document.Pages[i].Reference?.ObjectID == target.ObjectID)
                return i;
        }
        return -1;
    }

    protected static bool Near(double expected, double actual, double tolerance = 0.5)
        => Math.Abs(expected - actual) <= tolerance;

    protected static void AssertNear(double expected, double actual, double tolerance = 0.5)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    protected static string Words(string text)
        => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
