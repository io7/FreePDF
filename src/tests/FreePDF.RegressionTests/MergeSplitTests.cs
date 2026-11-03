// Merging and splitting with AppendPages and ExtractPages: form fields, bookmarks and links come along and point to the
// pages of the new document. Original fixture; MIT, see LICENSE.
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Forms;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public class MergeSplitTests
{
    const string Page3Marker = "% page three drawing";

    /// <summary>
    /// Three pages. Page 1: text field "name", a radio widget of "size", links to page 3 (explicit), page 2 (GoTo
    /// action) and page 3 (name in the catalog /Dests). Page 2: check box "agree", the other radio widget, a web link.
    /// Page 3: large content. Bookmarks: Chapter 1 (page 1) with Section 1.1 (page 2), Chapter 3 (named, page 3).
    /// </summary>
    static byte[] Fixture()
    {
        var drawing = new StringBuilder(Page3Marker + "\n");
        for (int line = 0; line < 200; line++)
            drawing.Append("0 0 m 400 400 l S\n");
        const string on = "q 0 g 2 2 10 10 re f Q";
        const string off = "q Q";
        return TestPdf.Create(new[] {
            /* 1 */ "<< /Type /Catalog /Pages 2 0 R /AcroForm 6 0 R /Outlines 9 0 R /Names << /Dests 12 0 R >> /Dests 20 0 R >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>",
            /* 3 */ "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Contents 13 0 R /Annots [7 0 R 14 0 R 16 0 R 18 0 R 25 0 R] >>",
            /* 4 */ "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [8 0 R 19 0 R 21 0 R] >>",
            /* 5 */ "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Contents 17 0 R >>",
            /* 6 */ "<< /Fields [7 0 R 8 0 R 22 0 R] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 15 0 R >> >> /XFA [(template) 23 0 R] >>",
            /* 7 */ "<< /FT /Tx /T (name) /V (Ada) /DA (/Helv 12 Tf 0 g) /Type /Annot /Subtype /Widget /Rect [20 350 220 370] /P 3 0 R /F 4 >>",
            /* 8 */ "<< /FT /Btn /T (agree) /V /Off /AS /Off /Type /Annot /Subtype /Widget /Rect [20 320 34 334] /P 4 0 R /F 4 /AP << /N << /Yes 26 0 R /Off 27 0 R >> >> >>",
            /* 9 */ "<< /Type /Outlines /First 10 0 R /Last 11 0 R /Count 3 >>",
            /* 10 */ "<< /Title (Chapter 1) /Parent 9 0 R /Next 11 0 R /First 24 0 R /Last 24 0 R /Count 1 /Dest [3 0 R /XYZ 0 400 0] >>",
            /* 11 */ "<< /Title (Chapter 3) /Parent 9 0 R /Prev 10 0 R /Dest (chap3) >>",
            /* 12 */ "<< /Names [(chap3) [5 0 R /Fit]] >>",
            /* 13 */ "<< /Length 3 >>\nstream\nq Q\nendstream",
            /* 14 */ "<< /Type /Annot /Subtype /Link /Rect [20 100 120 120] /Border [0 0 0] /Dest [5 0 R /XYZ 0 400 0] /P 3 0 R >>",
            /* 15 */ "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            /* 16 */ "<< /Type /Annot /Subtype /Link /Rect [20 60 120 80] /Border [0 0 0] /A << /S /GoTo /D [4 0 R /Fit] >> >>",
            /* 17 */ $"<< /Length {drawing.Length} >>\nstream\n{drawing}\nendstream",
            /* 18 */ "<< /Type /Annot /Subtype /Link /Rect [20 20 120 40] /Border [0 0 0] /Dest /chap3name >>",
            /* 19 */ "<< /Parent 22 0 R /Type /Annot /Subtype /Widget /Rect [40 290 54 304] /P 4 0 R /F 4 /AS /Off /AP << /N << /Large 26 0 R /Off 27 0 R >> >> >>",
            /* 20 */ "<< /chap3name [5 0 R /FitH 300] >>",
            /* 21 */ "<< /Type /Annot /Subtype /Link /Rect [20 200 120 220] /Border [0 0 0] /A << /S /URI /URI (https://example.com/) >> >>",
            /* 22 */ "<< /FT /Btn /Ff 49152 /T (size) /V /Off /Kids [25 0 R 19 0 R] >>",
            /* 23 */ "<< /Length 10 >>\nstream\n<template/>\nendstream",
            /* 24 */ "<< /Title (Section 1.1) /Parent 10 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>",
            /* 25 */ "<< /Parent 22 0 R /Type /Annot /Subtype /Widget /Rect [20 290 34 304] /P 3 0 R /F 4 /AS /Off /AP << /N << /Small 26 0 R /Off 27 0 R >> >> >>",
            /* 26 */ $"<< /Type /XObject /Subtype /Form /BBox [0 0 14 14] /Length {on.Length} >>\nstream\n{on}\nendstream",
            /* 27 */ $"<< /Type /XObject /Subtype /Form /BBox [0 0 14 14] /Length {off.Length} >>\nstream\n{off}\nendstream",
        });
    }

    static PdfDocument OpenForImport() => PdfReader.Open(new MemoryStream(Fixture()), PdfDocumentOpenMode.Import);

    static PdfDocument SaveAndReopen(PdfDocument document)
    {
        var stream = new MemoryStream();
        document.Save(stream, false);
        return PdfReader.Open(new MemoryStream(stream.ToArray()), PdfDocumentOpenMode.Modify);
    }

    static PdfFormFields Fields(PdfDocument document) => document.Catalog.GetAcroForm()!.Fields;

    static List<string> FieldNames(PdfDocument document)
        => Fields(document).Elements.Select(item => ((PdfDictionary)((PdfReference)item).Value).Elements.GetString("/T")).ToList();

    static List<PdfDictionary> Annotations(PdfDocument document, int pageIndex, string subtype)
    {
        var annots = document.Pages[pageIndex].Elements.GetArray("/Annots");
        if (annots == null)
            return [];
        return annots.Elements.Select(item => (item as PdfReference)?.Value as PdfDictionary ?? (PdfDictionary)item)
            .Where(annot => annot.Elements.GetName("/Subtype") == subtype).ToList();
    }

    /// <summary>
    /// The page index a destination array leads to, or -1 if it leads to no page of the document.
    /// </summary>
    static int TargetPage(PdfDocument document, PdfArray destination)
    {
        var reference = destination.Elements[0] as PdfReference;
        for (int index = 0; index < document.PageCount; index++)
        {
            if (ReferenceEquals(document.Pages[index].Reference, reference))
                return index;
        }
        return -1;
    }

    static PdfArray LinkDestination(PdfDictionary link)
        => link.Elements.GetArray("/Dest") ?? link.Elements.GetDictionary("/A")!.Elements.GetArray("/D")!;

    static int OutlineTarget(PdfDocument document, PdfOutline outline)
    {
        var destination = outline.Elements.GetArray("/Dest");
        if (destination == null)
            return -1;
        return TargetPage(document, destination);
    }

    [Fact]
    public void Merged_form_fields_are_a_form_that_can_be_filled()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();
        merged.AppendPages(source);

        using var reopened = SaveAndReopen(merged);

        Assert.Equal(new[] { "name", "agree", "size" }, FieldNames(reopened));
        Assert.NotNull(reopened.Catalog.GetAcroForm()!.Elements.GetDictionary("/DR")!.Elements.GetDictionary("/Font")!.Elements["/Helv"]);
        ((PdfFormTextField)Fields(reopened)["name"]!).Text = "Grace";
        Assert.Equal("Grace", ((PdfFormTextField)Fields(reopened)["name"]!).Text);
        var nameWidget = Annotations(reopened, 0, "/Widget")[0];
        Assert.Same(reopened.Pages[0].Reference, nameWidget.Elements["/P"]);
    }

    [Fact]
    public void Merging_the_same_form_twice_renames_the_second_copy()
    {
        using var first = OpenForImport();
        using var second = OpenForImport();
        using var merged = new PdfDocument();

        var firstResult = merged.AppendPages(first);
        var secondResult = merged.AppendPages(second);
        ((PdfFormTextField)Fields(merged)["name_2"]!).Text = "Second";
        using var reopened = SaveAndReopen(merged);

        Assert.Empty(firstResult.RenamedFields);
        Assert.Equal("name_2", secondResult.RenamedFields["name"]);
        Assert.Equal(3, secondResult.RenamedFields.Count);
        Assert.Equal(new[] { "name", "agree", "size", "name_2", "agree_2", "size_2" }, FieldNames(reopened));
        Assert.Equal("Ada", ((PdfFormTextField)Fields(reopened)["name"]!).Text);
        Assert.Equal("Second", ((PdfFormTextField)Fields(reopened)["name_2"]!).Text);
    }

    [Fact]
    public void Merged_forms_can_be_flattened()
    {
        using var first = OpenForImport();
        using var second = OpenForImport();
        using var merged = new PdfDocument();
        merged.AppendPages(first);
        merged.AppendPages(second);

        merged.Flatten();
        using var reopened = SaveAndReopen(merged);

        Assert.Null(reopened.Catalog.GetAcroForm());
        Assert.Empty(Annotations(reopened, 3, "/Widget"));
    }

    [Fact]
    public void Xfa_is_not_carried_into_the_merged_form()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();
        merged.AppendPages(source);

        Assert.Null(merged.Catalog.GetAcroForm()!.Elements["/XFA"]);
    }

    [Fact]
    public void Links_lead_to_the_pages_of_the_merged_document()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();
        merged.AddPage();

        var result = merged.AppendPages(source);
        using var reopened = SaveAndReopen(merged);

        Assert.Equal(new[] { 1, 2, 3 }, result.Pages.Select(page => merged.Pages.Cast<PdfPage>().ToList().IndexOf(page)));
        var links = Annotations(reopened, 1, "/Link");
        Assert.Equal(new[] { 3, 2, 3 }, links.Select(link => TargetPage(reopened, LinkDestination(link))));
        // The named destination is written as the explicit destination it named.
        Assert.Equal("/FitH", links[2].Elements.GetArray("/Dest")!.Elements.GetName(1));
        Assert.Equal("https://example.com/", Assert.Single(Annotations(reopened, 2, "/Link")).Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
    }

    [Fact]
    public void Links_to_pages_that_were_left_out_are_removed()
    {
        using var source = OpenForImport();

        using var part = source.ExtractPages(0, 1);
        using var reopened = SaveAndReopen(part);

        Assert.Empty(Annotations(reopened, 0, "/Link"));
        Assert.Equal(2, Annotations(reopened, 0, "/Widget").Count);
    }

    [Fact]
    public void An_extracted_page_carries_no_hidden_copies_of_the_other_pages()
    {
        byte[] bytes;
        using (var source = OpenForImport())
        using (var part = source.ExtractPages(0, 1))
        {
            var stream = new MemoryStream();
            part.Save(stream, false);
            bytes = stream.ToArray();
        }
        using var reopened = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

        var pageObjects = reopened.Internals.GetAllObjects().OfType<PdfDictionary>().Count(dict => dict.Elements.GetName("/Type") == "/Page");
        Assert.Equal(1, pageObjects);
        Assert.DoesNotContain(reopened.Internals.GetAllObjects().OfType<PdfDictionary>(),
            dict => dict.Stream != null && TestPdf.Latin1.GetString(dict.Stream.UnfilteredValue).Contains(Page3Marker));
    }

    [Fact]
    public void Fonts_are_shared_between_page_content_annotations_and_repeated_merges()
    {
        // Font 5 is used by the page content and the note's appearance, font 6 only by the appearance.
        const string content = "BT /F1 12 Tf (Text) Tj ET";
        const string appearance = "BT /F1 10 Tf (A) Tj /F2 10 Tf (B) Tj ET";
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R /Annots [7 0 R] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
            "<< /Type /Annot /Subtype /FreeText /Rect [20 20 120 40] /DA (/F1 10 Tf) /AP << /N 8 0 R >> >>",
            $"<< /Type /XObject /Subtype /Form /BBox [0 0 100 20] /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> /Length {appearance.Length} >>\nstream\n{appearance}\nendstream",
        });
        using var first = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(first);
        merged.AppendPages(first);
        using var reopened = SaveAndReopen(merged);

        var fonts = reopened.Internals.GetAllObjects().OfType<PdfDictionary>().Count(dict => dict.Elements.GetName("/Type") == "/Font");
        Assert.Equal(2, fonts);
        Assert.Single(Annotations(reopened, 0, "/FreeText"));
        Assert.Single(Annotations(reopened, 1, "/FreeText"));
    }

    [Fact]
    public void The_tab_order_transparency_group_and_user_unit_of_a_page_are_kept()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Tabs /S /UserUnit 2 /Group << /S /Transparency /CS /DeviceRGB /I true >> >>",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(source);
        using var reopened = SaveAndReopen(merged);

        var page = reopened.Pages[0];
        Assert.Equal("/S", page.Elements.GetName("/Tabs"));
        Assert.Equal(2, page.Elements.GetInteger("/UserUnit"));
        var group = page.Elements.GetDictionary("/Group")!;
        Assert.Equal("/DeviceRGB", group.Elements.GetName("/CS"));
        Assert.True(group.Elements.GetBoolean("/I"));
    }

    static byte[] SingleFieldForm(string formAppearance, string baseFont, string fieldName) => TestPdf.Create(new[] {
        $"<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA ({formAppearance}) /DR << /Font << /F1 5 0 R >> >> >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [4 0 R] >>",
        $"<< /FT /Tx /T ({fieldName}) /Type /Annot /Subtype /Widget /Rect [20 350 220 370] /P 3 0 R /F 4 >>",
        $"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} /Encoding /WinAnsiEncoding >>",
    });

    static (string Content, string BaseFont) FilledAppearance(PdfDocument document, string fieldName, int pageIndex)
    {
        ((PdfFormTextField)Fields(document)[fieldName]!).Text = "Hello";
        var widget = Annotations(document, pageIndex, "/Widget")[0];
        var normal = widget.Elements.GetDictionary("/AP")!.Elements.GetDictionary("/N")!;
        var fonts = normal.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var font = fonts.Elements.GetDictionary(fonts.Elements.Keys.Single())!;
        return (TestPdf.Latin1.GetString(normal.Stream!.UnfilteredValue), font.Elements.GetName("/BaseFont"));
    }

    [Fact]
    public void Fields_keep_the_font_size_and_colour_of_their_own_form()
    {
        // Both forms call their font /F1 and give the size and colour only at form level.
        using var first = PdfReader.Open(new MemoryStream(SingleFieldForm("/F1 10 Tf 0 g", "Courier", "a")), PdfDocumentOpenMode.Import);
        using var second = PdfReader.Open(new MemoryStream(SingleFieldForm("/F1 18 Tf 0 0 1 rg", "Helvetica", "b")), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(first);
        merged.AppendPages(second);
        using var reopened = SaveAndReopen(merged);

        var (secondContent, secondFont) = FilledAppearance(reopened, "b", 1);
        Assert.Equal("/Helvetica", secondFont);
        Assert.Contains("18 Tf", secondContent);
        Assert.Contains("0 0 1 rg", secondContent);
        var (firstContent, firstFont) = FilledAppearance(reopened, "a", 0);
        Assert.Equal("/Courier", firstFont);
        Assert.Contains("10 Tf", firstContent);
    }

    [Fact]
    public void The_same_form_appended_twice_shares_its_default_resources()
    {
        using var first = OpenForImport();
        using var second = OpenForImport();
        using var merged = new PdfDocument();

        merged.AppendPages(first);
        merged.AppendPages(first);
        merged.AppendPages(second);

        var form = merged.Catalog.GetAcroForm()!;
        Assert.Equal(new[] { "/Helv" }, form.Elements.GetDictionary("/DR")!.Elements.GetDictionary("/Font")!.Elements.Keys);
        Assert.Equal(9, form.Fields.Count);
    }

    [Fact]
    public void Top_level_fields_of_one_name_stay_one_field_and_are_renamed_together()
    {
        // Two widgets, each its own top-level field "x": by ISO 32000-1 12.7.3.2 one field.
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [4 0 R 5 0 R] >>",
            "<< /FT /Tx /T (x) /Type /Annot /Subtype /Widget /Rect [20 350 220 370] /P 3 0 R >>",
            "<< /FT /Tx /T (x) /Type /Annot /Subtype /Widget /Rect [20 300 220 320] /P 3 0 R >>",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        var first = merged.AppendPages(source);
        var second = merged.AppendPages(source);

        Assert.Empty(first.RenamedFields);
        Assert.Equal("x_2", Assert.Single(second.RenamedFields).Value);
        Assert.Equal(new[] { "x", "x", "x_2", "x_2" }, FieldNames(merged));
    }

    [Fact]
    public void A_bookmark_colour_outside_0_to_1_is_clamped()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>",
            "<< /Title (Red) /Parent 4 0 R /Dest [3 0 R /Fit] /C [255 0 -1] >>",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(source);
        using var reopened = SaveAndReopen(merged);

        var red = Assert.Single(reopened.Outlines).TextColor;
        Assert.Equal((255, 0, 0), (red.R, red.G, red.B));
    }

    [Fact]
    public void A_bookmark_that_leads_elsewhere_is_kept()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /Outlines 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] >>",
            "<< /Type /Outlines /First 5 0 R /Last 5 0 R /Count 1 >>",
            "<< /Title (Website) /Parent 4 0 R /A << /S /URI /URI (https://example.com/) >> >>",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

        using var part = source.ExtractPages(0, 1);
        using var reopened = SaveAndReopen(part);

        var website = Assert.Single(reopened.Outlines);
        Assert.Equal("https://example.com/", website.Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
    }

    [Fact]
    public void A_button_action_leads_to_the_copied_page_or_is_removed()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] >>",
            "<< /FT /Btn /Ff 65536 /T (next) /Type /Annot /Subtype /Widget /Rect [20 20 80 40] /P 3 0 R /A << /S /GoTo /D [4 0 R /Fit] >> >>",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

        using var whole = source.ExtractPages(0, 2);
        using var firstPage = source.ExtractPages(0, 1);
        using var wholeReopened = SaveAndReopen(whole);
        using var firstReopened = SaveAndReopen(firstPage);

        var destination = Annotations(wholeReopened, 0, "/Widget")[0].Elements.GetDictionary("/A")!.Elements.GetArray("/D")!;
        Assert.Equal(1, TargetPage(wholeReopened, destination));
        Assert.Null(Annotations(firstReopened, 0, "/Widget")[0].Elements["/A"]);
    }

    [Fact]
    public void A_widget_without_a_form_on_two_pages_is_copied_to_both()
    {
        // Typing the second page's widgets after the first page's other annotations threw before.
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [5 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Rect [10 10 50 50] /FT /Tx /T (x) >>",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        merged.AppendPages(source);
        using var reopened = SaveAndReopen(merged);

        Assert.Single(Annotations(reopened, 0, "/Widget"));
        Assert.Single(Annotations(reopened, 1, "/Widget"));
    }

    [Fact]
    public void Damaged_annotation_entries_do_not_stop_the_copy()
    {
        // A /Subtype that is a number (found in the veraPDF corpus) and a /Type that is a string.
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 400 400] /Annots [4 0 R] >>",
            "<< /Type (Annot) /Subtype 7 /Rect [20 20 120 40] /AP << /N 5 0 R >> >>",
            "<< /Subtype 3 /BBox [0 0 100 20] /Length 3 >>\nstream\nq Q\nendstream",
        });
        using var source = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

        using var part = source.ExtractPages(0, 1);
        using var reopened = SaveAndReopen(part);

        Assert.Equal(1, reopened.PageCount);
    }

    [Fact]
    public void A_field_with_widgets_on_several_pages_keeps_the_widgets_of_the_extracted_pages()
    {
        using var source = OpenForImport();

        using var part = source.ExtractPages(1, 1);
        using var reopened = SaveAndReopen(part);

        Assert.Equal(new[] { "agree", "size" }, FieldNames(reopened));
        var size = Fields(reopened)["size"]!;
        var kid = (PdfDictionary)((PdfReference)Assert.Single(size.Elements.GetArray("/Kids")!.Elements)).Value;
        Assert.Same(reopened.Pages[0].Reference, kid.Elements["/P"]);
        Assert.Equal("https://example.com/", Assert.Single(Annotations(reopened, 0, "/Link")).Elements.GetDictionary("/A")!.Elements.GetString("/URI"));
    }

    [Fact]
    public void Bookmarks_lead_to_the_pages_of_the_merged_document()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();
        merged.AddPage();

        merged.AppendPages(source);
        using var reopened = SaveAndReopen(merged);

        Assert.Equal(new[] { "Chapter 1", "Chapter 3" }, reopened.Outlines.Select(o => o.Title));
        Assert.Equal(1, OutlineTarget(reopened, reopened.Outlines[0]));
        Assert.Equal(3, OutlineTarget(reopened, reopened.Outlines[1]));
        var section = Assert.Single(reopened.Outlines[0].Outlines);
        Assert.Equal("Section 1.1", section.Title);
        Assert.Equal(2, OutlineTarget(reopened, section));
    }

    [Fact]
    public void Bookmarks_to_pages_that_were_left_out_are_dropped()
    {
        using var source = OpenForImport();

        using var firstPage = source.ExtractPages(0, 1);
        using var lastPages = source.ExtractPages(1, 2);
        using var firstReopened = SaveAndReopen(firstPage);
        using var lastReopened = SaveAndReopen(lastPages);

        var chapter1 = Assert.Single(firstReopened.Outlines);
        Assert.Equal("Chapter 1", chapter1.Title);
        Assert.False(chapter1.HasChildren);

        // Chapter 1 stays as the heading of Section 1.1, without a destination of its own.
        Assert.Equal(new[] { "Chapter 1", "Chapter 3" }, lastReopened.Outlines.Select(o => o.Title));
        Assert.Equal(-1, OutlineTarget(lastReopened, lastReopened.Outlines[0]));
        Assert.Equal(0, OutlineTarget(lastReopened, Assert.Single(lastReopened.Outlines[0].Outlines)));
        Assert.Equal(1, OutlineTarget(lastReopened, lastReopened.Outlines[1]));
    }

    [Fact]
    public void A_bookmark_title_puts_each_source_under_its_own_bookmark()
    {
        using var first = OpenForImport();
        using var second = OpenForImport();
        using var merged = new PdfDocument();

        merged.AppendPages(first, new PdfPageImportOptions { BookmarkTitle = "First file" });
        merged.AppendPages(second, new PdfPageImportOptions { BookmarkTitle = "Second file", KeepBookmarks = false });
        using var reopened = SaveAndReopen(merged);

        Assert.Equal(new[] { "First file", "Second file" }, reopened.Outlines.Select(o => o.Title));
        Assert.Equal(0, OutlineTarget(reopened, reopened.Outlines[0]));
        Assert.Equal(3, OutlineTarget(reopened, reopened.Outlines[1]));
        Assert.Equal(new[] { "Chapter 1", "Chapter 3" }, reopened.Outlines[0].Outlines.Select(o => o.Title));
        Assert.False(reopened.Outlines[1].HasChildren);
    }

    [Fact]
    public void Without_bookmarks_the_outline_is_left_alone()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();

        merged.AppendPages(source, new PdfPageImportOptions { KeepBookmarks = false });

        Assert.Equal(0, merged.Outlines.Count);
    }

    [Fact]
    public void Part_of_a_document_can_be_appended()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();

        var result = merged.AppendPages(source, 1, 2);

        Assert.Equal(2, merged.PageCount);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(new[] { "agree", "size" }, FieldNames(merged));
    }

    [Fact]
    public void Appending_a_document_without_pages_adds_nothing()
    {
        var bytes = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [] /Count 0 >>",
        });
        using var empty = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        using var merged = new PdfDocument();

        var result = merged.AppendPages(empty);

        Assert.Empty(result.Pages);
        Assert.Equal(0, merged.PageCount);
    }

    [Fact]
    public void A_source_not_opened_for_import_is_refused_before_anything_changes()
    {
        using var source = PdfReader.Open(new MemoryStream(Fixture()), PdfDocumentOpenMode.Modify);
        using var merged = new PdfDocument();

        Assert.Throws<InvalidOperationException>(() => merged.AppendPages(source));

        Assert.Equal(0, merged.PageCount);
        Assert.Null(merged.Catalog.GetAcroForm());
    }

    [Fact]
    public void Bad_ranges_and_the_document_itself_are_refused()
    {
        using var source = OpenForImport();
        using var merged = new PdfDocument();

        Assert.Throws<ArgumentOutOfRangeException>(() => merged.AppendPages(source, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => merged.AppendPages(source, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.ExtractPages(0, 0));
        Assert.Throws<ArgumentException>(() => source.AppendPages(source));
        Assert.Equal(0, merged.PageCount);
    }

    [Fact]
    public void A_pdf_ua_document_refuses_appended_pages_before_anything_changes()
    {
        using var source = OpenForImport();
        using var tagged = new PdfDocument();
        UAManager.ForDocument(tagged);

        Assert.Throws<InvalidOperationException>(() => tagged.AppendPages(source));

        Assert.Equal(0, tagged.PageCount);
    }
}
