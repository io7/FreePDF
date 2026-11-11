// Files for the tests of what reading a file leaves as it is. Original fixtures; MIT, see LICENSE.
using FreePDF.Lab.Fixtures;

namespace FreePDF.RegressionTests;

static class ReadFixtures
{
    // Every fixture by name, for a theory.
    public static IEnumerable<object[]> Names => All.Keys.Select(name => new object[] { name });

    // The fixtures with one revision and a cross-reference table, which a test can extend by hand with an update.
    public static readonly string[] Plain = ["simple page", "shared content", "shared content array", "nested pages", "semantic", "kinds"];

    public static byte[] Get(string name) => All[name]();

    static readonly Dictionary<string, Func<byte[]>> All = new()
    {
        ["simple page"] = () => ReaderFixtureData.Get("valid"),
        ["shared content"] = () => PreservationFixtures.SharedContent(false),
        ["shared content array"] = () => PreservationFixtures.SharedContent(true),
        ["nested pages"] = NestedPages,
        ["semantic"] = PreservationFixtures.Semantic,
        ["kinds"] = Kinds,
        ["two revisions"] = () => ReaderFixtureData.Get("incremental"),
        ["object streams"] = () => PreservationFixtures.Incremental(),
        ["hybrid"] = () => PdfFixtureData.Hybrid(false),
        ["hybrid with a newer object"] = () => PdfFixtureData.Hybrid(true),
    };

    static string Stream(string entries, string data) => $"<< {entries} /Length {data.Length} >>\nstream\n{data}\nendstream";

    // Two pages under a node below the root, which the reader keeps as a tree. The pages inherit their media box from
    // the root.
    static byte[] NestedPages() => TestPdf.Create(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 2 /MediaBox [0 0 240 200] >>",
        "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 >>",
        "<< /Type /Page /Parent 3 0 R /Contents 6 0 R >>",
        "<< /Type /Page /Parent 3 0 R /Rotate 90 /Contents 6 0 R >>",
        Stream("", "0 1 0 rg 10 10 20 20 re f"),
    ]);

    // One of each kind of object that the reader gives a class of its own or prepares when it opens a file: pages that
    // inherit from the page tree, text, stamp, link and free-text annotations, a text field, a check box and a field
    // with a widget of its own, bookmarks with children, optional content, page labels, viewer preferences, named
    // destinations, metadata and the document information.
    static byte[] Kinds()
    {
        const string xmp = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
                           + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta><?xpacket end=\"w\"?>";
        return TestPdf.Create(
        [
            /*  1 */ "<< /Type /Catalog /Pages 2 0 R /Outlines 10 0 R /PageMode /UseOutlines /PageLayout /SinglePage /Lang (en)"
                     + " /PageLabels << /Nums [0 << /S /D >>] >> /ViewerPreferences << /HideToolbar false >>"
                     + " /AcroForm << /Fields [8 0 R 9 0 R 22 0 R] /DA (/Helv 10 Tf 0 g) >>"
                     + " /Names << /Dests << /Names [(start) [3 0 R /Fit]] >> >>"
                     + " /OCProperties << /OCGs [14 0 R] /D << /ON [14 0 R] >> >> /Metadata 15 0 R >>",
            /*  2 */ "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 612 792] >>",
            /*  3 */ "<< /Type /Page /Parent 2 0 R /Contents 5 0 R /CropBox [0 0 600 780]"
                     + " /Resources << /Font << /F1 6 0 R >> /XObject << /Im 7 0 R >> /Properties << /L 14 0 R >> >>"
                     + " /Annots [16 0 R 17 0 R 18 0 R 19 0 R 8 0 R 9 0 R 23 0 R] >>",
            /*  4 */ "<< /Type /Page /Parent 2 0 R /Rotate 90 /Contents 20 0 R >>",
            /*  5 */ Stream("", "BT /F1 12 Tf 20 700 Td (Signed text) Tj ET /OC /L BDC /Im Do EMC"),
            /*  6 */ "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            /*  7 */ Stream("/Type /XObject /Subtype /Form /BBox [0 0 50 50]", "0 0 50 50 re f"),
            /*  8 */ "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (Ada) /DA (/Helv 10 Tf 0 g) /MaxLen 40 /Q 1 /F 4"
                     + " /Rect [100 600 300 620] /P 3 0 R >>",
            /*  9 */ "<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /Yes /AS /Yes /F 4 /Rect [100 560 112 572] /P 3 0 R"
                     + " /AP << /N << /Yes 7 0 R /Off 7 0 R >> >> >>",
            /* 10 */ "<< /Type /Outlines /First 11 0 R /Last 12 0 R /Count 3 >>",
            /* 11 */ "<< /Title (Chapter 1) /Parent 10 0 R /Next 12 0 R /First 13 0 R /Last 13 0 R /Count 1 /Dest [3 0 R /Fit]"
                     + " /C [1 0 0] /F 2 >>",
            /* 12 */ "<< /Title (Chapter 2) /Parent 10 0 R /Prev 11 0 R /Dest [4 0 R /XYZ 0 792 0] >>",
            /* 13 */ "<< /Title (Section 1.1) /Parent 11 0 R /Dest [3 0 R /FitH 500] >>",
            /* 14 */ "<< /Type /OCG /Name (Layer) >>",
            /* 15 */ Stream("/Type /Metadata /Subtype /XML", xmp),
            /* 16 */ "<< /Type /Annot /Subtype /Text /Rect [10 10 30 30] /Contents (note) /Name /Key /C [1 1 0] /Open false /F 4"
                     + " /T (Author) /M (D:20260101000000Z) /P 3 0 R >>",
            /* 17 */ "<< /Type /Annot /Subtype /Stamp /Rect [40 10 140 40] /Name /Approved /C [0 1 0] /F 4 /P 3 0 R >>",
            /* 18 */ "<< /Type /Annot /Subtype /Link /Rect [20 690 150 710] /Dest [4 0 R /Fit] /Border [0 0 1] /H /I /P 3 0 R >>",
            /* 19 */ "<< /Type /Annot /Subtype /FreeText /Rect [200 10 300 40] /Contents (free) /DA (/Helv 10 Tf 0 g) /Q 0 /P 3 0 R >>",
            /* 20 */ Stream("", "0 0 1 rg 0 0 100 100 re f"),
            /* 21 */ "<< /Title (Report) /Author (Ada) /Producer (Test) /CreationDate (D:20260101000000Z) >>",
            /* 22 */ "<< /FT /Tx /T (group) /V (Shared) /DA (/Helv 10 Tf 0 g) /Kids [23 0 R] >>",
            /* 23 */ "<< /Type /Annot /Subtype /Widget /Parent 22 0 R /F 4 /Rect [100 520 300 540] /P 3 0 R >>",
        ], _ => "/Info 21 0 R");
    }
}
