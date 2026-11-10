// MigraDoc tab handling found in the upstream PDFsharp triage of 2026-11-05 (#404, #405). Original fixtures with the
// synthetic box font (6 pt per character at 10 pt). MIT, see LICENSE.
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class MigraDocTabTests
{
    // Upstream #404: default tab stops were compared as absolute positions with the formatting area's relative width,
    // so with a 2.5 cm left margin the default stops after 13.5 cm were missing and a tab there broke the line.
    // "a" ends at 13.21 cm; the next default stop is 13.75 cm.
    [Fact]
    public void Default_tab_stops_reach_the_right_edge_of_the_body()
    {
        var document = NewDocument();
        var paragraph = document.Sections[0]!.AddParagraph();
        paragraph.Format.LeftIndent = Unit.FromCentimeter(13);
        paragraph.AddText("a");
        paragraph.AddTab();
        paragraph.AddText("b");

        var texts = RenderTexts(document);

        var a = texts.Single(t => t.Text == "a");
        var b = texts.Single(t => t.Text == "b");
        Assert.Equal(a.Y, b.Y, 2);
        Assert.Equal(Unit.FromCentimeter(2.5 + 13.75).Point, b.X, 1);
    }

    // Upstream #405: the blank right after a tab is ignored; probing stopped at it and the tab forced a line break.
    [Fact]
    public void A_blank_after_a_tab_does_not_break_the_line()
    {
        var document = NewDocument();
        var paragraph = document.Sections[0]!.AddParagraph();
        paragraph.AddText("a");
        paragraph.AddTab();
        paragraph.AddText(" b");

        var texts = RenderTexts(document);

        var a = texts.Single(t => t.Text == "a");
        var b = texts.Single(t => t.Text.Trim() == "b");
        Assert.Equal(a.Y, b.Y, 2);
        Assert.True(b.X >= Unit.FromCentimeter(2.5 + 1.25).Point - 0.1, $"b at {b.X}");
    }

    static Document NewDocument()
    {
        TestFonts.Initialize();
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        document.Styles[StyleNames.Normal]!.Font.Size = 10;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromCentimeter(21);
        section.PageSetup.PageHeight = Unit.FromCentimeter(29.7);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.5);
        return document;
    }

    /// <summary>
    /// The shown strings of page 1 with their text-space start position, following Tm and Td.
    /// </summary>
    static List<(string Text, double X, double Y)> RenderTexts(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output);
        var page = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import).Pages[0];

        var result = new List<(string, double, double)>();
        double lineX = 0, lineY = 0;
        foreach (var op in Flatten(ContentReader.ReadContent(page)))
        {
            switch (op.Name)
            {
                case "Tm":
                    lineX = Number(op.Operands[4]);
                    lineY = Number(op.Operands[5]);
                    break;
                case "Td":
                    lineX += Number(op.Operands[0]);
                    lineY += Number(op.Operands[1]);
                    break;
                case "Tj":
                    result.Add((((CString)op.Operands[0]).Value, lineX, lineY));
                    break;
            }
        }
        return result;
    }

    static double Number(CObject operand) => operand switch
    {
        CReal real => real.Value,
        CInteger integer => integer.Value,
        _ => throw new InvalidOperationException($"{operand} is not a number."),
    };

    static IEnumerable<COperator> Flatten(CSequence sequence)
    {
        foreach (var item in sequence)
        {
            if (item is COperator op)
                yield return op;
            else if (item is CSequence inner)
            {
                foreach (var nested in Flatten(inner))
                    yield return nested;
            }
        }
    }
}
