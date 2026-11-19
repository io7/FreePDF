// Original synthetic fixtures for the text extraction tests. MIT; see LICENSE.
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content.Text;
using PdfSharp.Pdf.IO;

namespace FreePDF.RegressionTests;

static class TextPdf
{
    /// <summary>
    /// A one-page A4 PDF whose content is <paramref name="content"/> (each char one byte, 0-255) and whose
    /// /Resources/Font holds the given fonts.
    /// </summary>
    internal static byte[] Page(string content, params (string Name, Func<PdfDocument, PdfDictionary> Font)[] fonts)
        => Save(Document(content, fonts));

    internal static PdfDocument Document(string content, params (string Name, Func<PdfDocument, PdfDictionary> Font)[] fonts)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        var dictionary = new PdfDictionary(document);
        foreach (var (name, font) in fonts)
            dictionary.Elements[name] = Indirect(document, font(document));
        page.Resources.Elements["/Font"] = dictionary;
        page.Contents.AppendContent().CreateStream(Bytes(content));
        return document;
    }

    internal static PdfReference Indirect(PdfDocument document, PdfDictionary dictionary)
    {
        if (dictionary.Reference == null)
            document.Internals.AddObject(dictionary);
        return dictionary.Reference!;
    }

    internal static PdfDictionary Font(PdfDocument document, string subtype, string baseFont, PdfItem? encoding = null)
    {
        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", subtype);
        font.Elements.SetName("/BaseFont", baseFont);
        if (encoding != null)
            font.Elements["/Encoding"] = encoding;
        return font;
    }

    internal static PdfReference Stream(PdfDocument document, string data)
    {
        var stream = new PdfDictionary(document);
        stream.CreateStream(Bytes(data));
        return Indirect(document, stream);
    }

    internal static PdfArray Items(PdfDocument document, params PdfItem[] items)
    {
        var array = new PdfArray(document);
        foreach (var item in items)
            array.Elements.Add(item);
        return array;
    }

    internal static PdfArray Numbers(PdfDocument document, params double[] values)
        => Items(document, values.Select(v => (PdfItem)new PdfReal(v)).ToArray());

    internal static byte[] Bytes(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = checked((byte)text[i]);
        return bytes;
    }

    internal static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output);
        document.Dispose();
        return output.ToArray();
    }

    internal static string Text(byte[] pdf, PdfTextOrder order = PdfTextOrder.Reading, int page = 0)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return PdfTextExtractor.GetText(document.Pages[page], order);
    }

    internal static IReadOnlyList<PdfTextRun> Runs(byte[] pdf, int page = 0)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        return PdfTextExtractor.GetTextRuns(document.Pages[page]);
    }

    /// <summary>A Type1 standard font as /F1, with an optional /Encoding.</summary>
    internal static (string, Func<PdfDocument, PdfDictionary>) Standard(string baseFont, Func<PdfDocument, PdfItem>? encoding = null)
        => ("/F1", d => Font(d, "/Type1", baseFont, encoding?.Invoke(d)));

    /// <summary>A Type0 font with Identity-H (unless given), a CIDFontType2 descendant and the given ToUnicode body.</summary>
    internal static (string, Func<PdfDocument, PdfDictionary>) Composite(string toUnicode, Func<PdfDocument, PdfItem>? encoding = null,
        Action<PdfDocument, PdfDictionary>? descendant = null)
        => ("/F1", d =>
        {
            var font = Font(d, "/Type0", "/ABCDEF+Synthetic", encoding?.Invoke(d) ?? new PdfName("/Identity-H"));
            var cidFont = Font(d, "/CIDFontType2", "/ABCDEF+Synthetic");
            descendant?.Invoke(d, cidFont);
            font.Elements["/DescendantFonts"] = Items(d, Indirect(d, cidFont));
            if (toUnicode.Length > 0)
                font.Elements["/ToUnicode"] = Stream(d, CMap(toUnicode));
            return font;
        });

    /// <summary>A ToUnicode CMap with a two-byte code space around the given mapping blocks.</summary>
    internal static string CMap(string body, string codeSpace = "1 begincodespacerange <0000> <FFFF> endcodespacerange")
        => "/CIDInit /ProcSet findresource begin 12 dict begin begincmap\n"
           + "/CMapName /Test-UCS def /CMapType 2 def\n" + codeSpace + "\n" + body
           + "\nendcmap CMapName currentdict /CMap defineresource pop end end";
}
