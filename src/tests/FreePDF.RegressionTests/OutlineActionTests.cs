// Outline items that lead somewhere through an action (/A). ISO 32000-1 Table 153 permits /Dest only if no /A is
// present, but saving wrote a /Dest next to the /A of every item whose GoTo action it could resolve. Original
// fixtures; MIT, see LICENSE.
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class OutlineActionTests
{
    // Page 3 0 R is 240 wide, page 4 0 R is 300 wide; the outline item is object 6, more objects follow from 7.
    static byte[] Fixture(string item, string catalogEntries = "", params string[] more)
        => TestPdf.Create(new[] {
            $"<< /Type /Catalog /Pages 2 0 R /Outlines 5 0 R {catalogEntries} >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] >>",
            "<< /Type /Outlines /First 6 0 R /Last 6 0 R /Count 1 >>",
            item }.Concat(more).ToArray());

    static byte[] Save(byte[] bytes, PdfDocumentOpenMode mode, Action<PdfDocument>? change = null)
    {
        using var saved = new MemoryStream();
        using (var document = PdfReader.Open(new MemoryStream(bytes), mode))
        {
            change?.Invoke(document);
            document.Save(saved);
        }
        return saved.ToArray();
    }

    static PdfOutline OnlyItem(byte[] bytes, out PdfDocument document)
    {
        document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        return Assert.Single(document.Outlines);
    }

    [Theory]
    [InlineData("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>", "", null)]
    [InlineData("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D (second) >> >>", "/Dests << /second [4 0 R /Fit] >>", null)]
    [InlineData("<< /Title (Two) /Parent 5 0 R /A 7 0 R >>", "", "<< /S /GoTo /D [4 0 R /XYZ 0 200 0] >>")]
    public void A_GoTo_action_is_kept_and_gets_no_Dest(string item, string catalogEntries, string? action)
    {
        var more = action == null ? Array.Empty<string>() : new[] { action };
        var saved = Save(Fixture(item, catalogEntries, more), PdfDocumentOpenMode.Modify);

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.Dest));
            Assert.Equal("/GoTo", outline.Elements.GetDictionary(PdfOutline.Keys.A)!.Elements.GetName("/S"));
            Assert.Equal(300, outline.DestinationPage!.Width.Point);
        }
    }

    [Fact]
    public void Changing_the_page_of_a_GoTo_item_replaces_the_action_with_a_Dest()
    {
        var bytes = Fixture("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>");

        var saved = Save(bytes, PdfDocumentOpenMode.Modify, document => document.Outlines[0].DestinationPage = document.Pages[0]);

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.A));
            Assert.Equal(240, outline.DestinationPage!.Width.Point);
        }
    }

    [Fact]
    public void Changing_the_view_of_a_GoTo_item_replaces_the_action_with_a_Dest()
    {
        var bytes = Fixture("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D [4 0 R /XYZ 0 200 0] >> >>");

        var saved = Save(bytes, PdfDocumentOpenMode.Modify, document => document.Outlines[0].Top = 100);

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.A));
            Assert.Equal(300, outline.DestinationPage!.Width.Point);
            Assert.Equal(100, outline.Top);
        }
    }

    [Fact]
    public void Giving_a_URI_item_a_page_replaces_the_action_with_a_Dest()
    {
        var bytes = Fixture("<< /Title (Web) /Parent 5 0 R /A << /S /URI /URI (https://example.com/) >> >>");

        var saved = Save(bytes, PdfDocumentOpenMode.Modify, document => document.Outlines[0].DestinationPage = document.Pages[1]);

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.A));
            Assert.Equal(300, outline.DestinationPage!.Width.Point);
        }
    }

    [Fact]
    public void A_URI_action_is_kept()
    {
        var saved = Save(Fixture("<< /Title (Web) /Parent 5 0 R /A << /S /URI /URI (https://example.com/) >> >>"), PdfDocumentOpenMode.Modify);

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.Dest));
            Assert.Equal("/URI", outline.Elements.GetDictionary(PdfOutline.Keys.A)!.Elements.GetName("/S"));
        }
    }

    [Fact]
    public void A_Dest_item_keeps_its_Dest()
    {
        var saved = Save(Fixture("<< /Title (Two) /Parent 5 0 R /Dest [4 0 R /Fit] >>"), PdfDocumentOpenMode.Modify);

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.A));
            Assert.Equal(300, outline.DestinationPage!.Width.Point);
        }
    }

    [Fact]
    public void A_new_item_gets_a_Dest()
    {
        var saved = Save(Fixture("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>"), PdfDocumentOpenMode.Modify,
            document => document.Outlines.Add("One", document.Pages[0]));

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        var added = reopened.Outlines.Single(o => o.Title == "One");
        Assert.True(added.Elements.ContainsKey(PdfOutline.Keys.Dest));
        Assert.False(added.Elements.ContainsKey(PdfOutline.Keys.A));
        Assert.Equal(240, added.DestinationPage!.Width.Point);
    }

    [Fact]
    public void A_renamed_GoTo_item_is_written_with_its_action_and_no_Dest_in_Append_mode()
    {
        var original = Fixture("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>");

        var saved = Save(original, PdfDocumentOpenMode.Append, document => document.Outlines[0].Title = "Renamed");

        var update = TestPdf.Latin1.GetString(saved, original.Length, saved.Length - original.Length);
        var item = Regex.Match(update, @"(?ms)^6 0 obj\b(.*?)\bendobj").Groups[1].Value;
        Assert.Contains("/GoTo", item);
        Assert.DoesNotContain("/Dest", item);
        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
            Assert.Equal("Renamed", outline.Title);
    }

    [Theory]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.Append)]
    public void Removing_the_action_of_a_GoTo_item_writes_a_Dest(PdfDocumentOpenMode mode)
    {
        var bytes = Fixture("<< /Title (Two) /Parent 5 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>");

        var saved = Save(bytes, mode, document => document.Outlines[0].Elements.Remove(PdfOutline.Keys.A));

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.A));
            Assert.True(outline.Elements.ContainsKey(PdfOutline.Keys.Dest));
            Assert.Equal(300, outline.DestinationPage!.Width.Point);
        }
    }

    static PdfDictionary UriAction(PdfDocument document)
    {
        var action = new PdfDictionary(document);
        action.Elements.SetName("/S", "/URI");
        action.Elements.SetString("/URI", "https://example.com/");
        return action;
    }

    [Fact]
    public void An_action_set_on_a_new_item_is_kept_without_a_Dest()
    {
        var saved = Save(Fixture("<< /Title (Two) /Parent 5 0 R /Dest [4 0 R /Fit] >>"), PdfDocumentOpenMode.Modify, document => {
            var added = document.Outlines.Add("Web", document.Pages[0]);
            added.Elements[PdfOutline.Keys.A] = UriAction(document);
        });

        using var reopened = PdfReader.Open(new MemoryStream(saved), PdfDocumentOpenMode.Import);
        var web = reopened.Outlines.Single(o => o.Title == "Web");
        Assert.False(web.Elements.ContainsKey(PdfOutline.Keys.Dest));
        Assert.Equal("/URI", web.Elements.GetDictionary(PdfOutline.Keys.A)!.Elements.GetName("/S"));
    }

    [Fact]
    public void An_action_set_on_a_Dest_item_replaces_the_Dest()
    {
        var saved = Save(Fixture("<< /Title (Two) /Parent 5 0 R /Dest [4 0 R /Fit] >>"), PdfDocumentOpenMode.Modify,
            document => document.Outlines[0].Elements[PdfOutline.Keys.A] = UriAction(document));

        var outline = OnlyItem(saved, out var reopened);
        using (reopened)
        {
            Assert.False(outline.Elements.ContainsKey(PdfOutline.Keys.Dest));
            Assert.Equal("/URI", outline.Elements.GetDictionary(PdfOutline.Keys.A)!.Elements.GetName("/S"));
        }
    }
}
