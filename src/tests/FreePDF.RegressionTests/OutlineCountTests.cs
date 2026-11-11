// The /Count of outline items (ISO 32000-1 12.3.3, Table 152 and 153): required on every item with children, the
// number of descendants visible while it is open and negative while it is closed; on the root, the number of visible
// items. FreePDF counted only descendants added already open, so an open bookmark with closed children had no /Count
// and showed closed, closed bookmarks had none either, and Opened read false for every item of an opened file.
// Original fixtures; MIT, see LICENSE.
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class OutlineCountTests
{
    // The root is object 4; "Open" (5) has the child "Child" (6), "Closed" (7) has the child "Inner" (8).
    static byte[] Fixture(string openCount = "/Count 1", string closedCount = "/Count -1", string rootCount = "/Count 3")
        => TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>",
            $"<< /Type /Outlines /First 5 0 R /Last 7 0 R {rootCount} >>",
            $"<< /Title (Open) /Parent 4 0 R /Next 7 0 R /First 6 0 R /Last 6 0 R {openCount} /Dest [3 0 R /Fit] >>",
            "<< /Title (Child) /Parent 5 0 R /Dest [3 0 R /Fit] >>",
            $"<< /Title (Closed) /Parent 4 0 R /Prev 5 0 R /First 8 0 R /Last 8 0 R {closedCount} /Dest [3 0 R /Fit] >>",
            "<< /Title (Inner) /Parent 7 0 R /Dest [3 0 R /Fit] >>" });

    static byte[] Save(PdfDocument document)
    {
        using var saved = new MemoryStream();
        document.Save(saved);
        return saved.ToArray();
    }

    static byte[] Save(byte[] bytes, PdfDocumentOpenMode mode, Action<PdfDocument>? change = null)
    {
        using var document = PdfReader.Open(new MemoryStream(bytes), mode);
        change?.Invoke(document);
        return Save(document);
    }

    static PdfDocument Reopen(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    static int? Count(PdfDictionary item) => item.Elements.TryGetInteger(PdfOutline.Keys.Count, out var count) ? count : null;

    static PdfOutline Item(PdfOutlineCollection outlines, string title) => outlines.Single(o => o.Title == title);

    [Fact]
    public void New_items_with_children_get_a_count_and_the_root_counts_the_visible_items()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var a = document.Outlines.Add("A", page, true);
        var a1 = a.Outlines.Add("A1", page, true);
        a1.Outlines.Add("A1a", page);
        a.Outlines.Add("A2", page);
        var b = document.Outlines.Add("B", page, false);
        var b1 = b.Outlines.Add("B1", page, true);
        b1.Outlines.Add("B1a", page);
        b1.Outlines.Add("B1b", page);
        b.Outlines.Add("B2", page);

        using var reopened = Reopen(Save(document));

        var outlines = reopened.Outlines;
        Assert.Equal(5, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));   // A, A1, A1a, A2, B
        var readA = Item(outlines, "A");
        Assert.Equal(3, Count(readA));                         // A1, A1a, A2
        Assert.Equal(1, Count(Item(readA.Outlines, "A1")));
        Assert.Null(Count(Item(readA.Outlines, "A2")));        // no children, no /Count
        var readB = Item(outlines, "B");
        Assert.Equal(-4, Count(readB));                        // B1, B1a, B1b, B2 if B were open
        Assert.Equal(2, Count(Item(readB.Outlines, "B1")));
        Assert.True(readA.Opened);
        Assert.False(readB.Opened);
        Assert.True(Item(readB.Outlines, "B1").Opened);
    }

    [Fact]
    public void An_opened_file_keeps_its_open_and_closed_items()
    {
        using (var opened = Reopen(Fixture()))
        {
            Assert.True(Item(opened.Outlines, "Open").Opened);
            Assert.False(Item(opened.Outlines, "Closed").Opened);
        }

        using var reopened = Reopen(Save(Fixture(), PdfDocumentOpenMode.Modify));

        Assert.Equal(1, Count(Item(reopened.Outlines, "Open")));
        Assert.Equal(-1, Count(Item(reopened.Outlines, "Closed")));
        Assert.Equal(3, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));
    }

    [Fact]
    public void An_unchanged_item_keeps_the_count_it_was_read_with()
    {
        // Signature checks compare revisions by their written form: rewriting an unchanged item, even to fix it, would
        // hide a later change to it or report one that never happened.
        using var reopened = Reopen(Save(Fixture(closedCount: "", rootCount: "/Count 99"), PdfDocumentOpenMode.Modify));

        Assert.Null(Count(Item(reopened.Outlines, "Closed")));
        Assert.Equal(99, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));
    }

    [Fact]
    public void A_changed_item_and_the_items_above_it_get_their_counts()
    {
        var saved = Save(Fixture(closedCount: "", rootCount: "/Count 99"), PdfDocumentOpenMode.Modify,
            document => Item(document.Outlines, "Closed").Outlines.Add("Added", document.Pages[0]));

        using var reopened = Reopen(saved);
        Assert.Equal(-2, Count(Item(reopened.Outlines, "Closed")));
        Assert.Equal(3, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));   // Open, Child, Closed
        Assert.Equal(1, Count(Item(reopened.Outlines, "Open")));
    }

    [Fact]
    public void Opening_an_item_writes_its_count()
    {
        var saved = Save(Fixture(), PdfDocumentOpenMode.Modify, document => Item(document.Outlines, "Closed").Opened = true);

        using var reopened = Reopen(saved);
        Assert.Equal(1, Count(Item(reopened.Outlines, "Closed")));
        Assert.Equal(4, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));
    }

    [Fact]
    public void Removing_the_last_child_drops_the_count()
    {
        var saved = Save(Fixture(), PdfDocumentOpenMode.Modify, document =>
        {
            var open = Item(document.Outlines, "Open");
            open.Outlines.Remove(open.Outlines[0]);
        });

        using var reopened = Reopen(saved);
        Assert.Null(Count(Item(reopened.Outlines, "Open")));
        Assert.Equal(2, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));
    }

    [Fact]
    public void A_deep_open_chain_is_counted_at_every_level()
    {
        // One pass from the bottom up: a long chain of open items costs no more than its length.
        using var document = new PdfDocument();
        var page = document.AddPage();
        var outlines = document.Outlines;
        const int depth = 2000;
        for (int level = 0; level < depth; level++)
            outlines = outlines.Add($"Level {level}", page, true).Outlines;

        using var reopened = Reopen(Save(document));

        var item = Assert.Single(reopened.Outlines);
        Assert.Equal(depth, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));
        for (int level = 0; level < depth - 1; level++)
        {
            Assert.Equal(depth - 1 - level, Count(item));
            item = Assert.Single(item.Outlines);
        }
        Assert.Null(Count(item));
    }

    [Fact]
    public void An_unchanged_document_in_Append_mode_is_saved_as_it_was()
    {
        // The missing /Count values would be written in Modify mode; an Append-mode save writes nothing.
        var original = Fixture(closedCount: "", rootCount: "");

        var saved = Save(original, PdfDocumentOpenMode.Append);

        Assert.Equal(original, saved);
    }

    [Fact]
    public void A_child_added_in_Append_mode_updates_the_counts()
    {
        var original = Fixture();

        var saved = Save(original, PdfDocumentOpenMode.Append,
            document => Item(document.Outlines, "Open").Outlines.Add("Added", document.Pages[0]));

        var update = TestPdf.Latin1.GetString(saved, original.Length, saved.Length - original.Length);
        Assert.Matches(@"(?s)\b5 0 obj\b.*?/Count 2\b.*?endobj", update);
        Assert.Matches(@"(?s)\b4 0 obj\b.*?/Count 4\b.*?endobj", update);
        Assert.DoesNotMatch(@"(?s)\b7 0 obj\b", update);   // the closed item did not change
        using var reopened = Reopen(saved);
        Assert.Equal(2, Item(reopened.Outlines, "Open").Outlines.Count);
    }

    [Fact]
    public void Merged_bookmarks_keep_open_and_closed()
    {
        using var source = Reopen(Fixture());
        using var merged = new PdfDocument();
        merged.AppendPages(source);

        using var reopened = Reopen(Save(merged));

        Assert.Equal(1, Count(Item(reopened.Outlines, "Open")));
        Assert.Equal(-1, Count(Item(reopened.Outlines, "Closed")));
    }

    [Fact]
    public void MigraDoc_headings_are_open_bookmarks()
    {
        TestFonts.Initialize();
        var document = new Document();
        document.Styles[StyleNames.Normal]!.Font.Name = "FreePDF Synthetic";
        var section = document.AddSection();
        section.AddParagraph("Chapter").Format.OutlineLevel = OutlineLevel.Level1;
        section.AddParagraph("Section one").Format.OutlineLevel = OutlineLevel.Level2;
        section.AddParagraph("Section two").Format.OutlineLevel = OutlineLevel.Level2;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var reopened = Reopen(Save(renderer.PdfDocument));

        var chapter = Item(reopened.Outlines, "Chapter");
        Assert.Equal(2, Count(chapter));
        Assert.True(chapter.Opened);
        Assert.Equal(3, Count(reopened.Internals.Catalog.Elements.GetDictionary("/Outlines")!));
    }
}
