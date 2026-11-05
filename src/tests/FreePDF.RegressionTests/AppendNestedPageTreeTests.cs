// Adding, removing and moving pages in Append mode when the page tree is nested. The update changes the tree where
// it is: only the nodes between the root and the pages that come or go are written, and a page that lands under
// another node writes the values it inherited before. Original fixtures; MIT, see LICENSE.
using System.Globalization;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace FreePDF.RegressionTests;

public class AppendNestedPageTreeTests
{
    // Pages 5, 6 and 7 are in node 3, which gives them a 400 x 300 media box, a crop box and /Rotate 90. Page 8 is in
    // node 4, which gives it a 200 x 100 media box. The root gives nothing.
    static byte[] Nested() => TestPdf.Create(new[] {
        /* 1 */ "<< /Type /Catalog /Pages 2 0 R >>",
        /* 2 */ "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 4 >>",
        /* 3 */ "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R 7 0 R] /Count 3 /MediaBox [0 0 400 300] /CropBox [10 10 390 290] /Rotate 90 >>",
        /* 4 */ "<< /Type /Pages /Parent 2 0 R /Kids [8 0 R] /Count 1 /MediaBox [0 0 200 100] >>",
        /* 5 */ "<< /Type /Page /Parent 3 0 R >>",
        /* 6 */ "<< /Type /Page /Parent 3 0 R >>",
        /* 7 */ "<< /Type /Page /Parent 3 0 R >>",
        /* 8 */ "<< /Type /Page /Parent 4 0 R >>",
    });

    static byte[] Save(byte[] original, Action<PdfDocument> change)
    {
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
        change(document);
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    // The numbers of the objects the update writes, without the document information that every update gets.
    static int[] Written(byte[] original, byte[] result)
    {
        Assert.True(result.Length > original.Length);
        Assert.Equal(original, result.Take(original.Length).ToArray());
        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        var information = int.Parse(Regex.Match(update, @"/Info\s+(\d+)\s+0\s+R").Groups[1].Value, CultureInfo.InvariantCulture);
        return Regex.Matches(update, @"(?m)^(\d+) \d+ obj\b").Cast<Match>()
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Where(number => number != information).OrderBy(number => number).ToArray();
    }

    // The text of an object as the update writes it.
    static string WrittenObject(byte[] original, byte[] result, int number)
    {
        var update = TestPdf.Latin1.GetString(result, original.Length, result.Length - original.Length);
        var match = Regex.Match(update, $@"(?ms)^{number} 0 obj\b(.*?)\bendobj");
        Assert.True(match.Success, $"The update does not write object {number}.");
        return match.Groups[1].Value;
    }

    static PdfDocument Reopen(byte[] result) => PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);

    static int[] PageNumbers(PdfDocument document)
        => Enumerable.Range(0, document.PageCount).Select(index => document.Pages[index].Reference!.ObjectNumber).ToArray();

    // Every node's /Count is the number of pages below it, and every kid names its node as /Parent.
    static void AssertConsistentTree(PdfDocument document)
    {
        var root = (PdfDictionary)((PdfReference)document.Catalog.Elements["/Pages"]!).Value;
        Assert.Equal(document.PageCount, CountPages(root));
    }

    static int CountPages(PdfDictionary node)
    {
        if (node.Elements.GetName("/Type") == "/Page")
            return 1;
        var kids = node.Elements.GetArray("/Kids")!;
        Assert.NotEmpty(kids.Elements);
        int count = 0;
        foreach (var item in kids.Elements)
        {
            var kid = (PdfDictionary)((PdfReference)item).Value;
            Assert.Equal(node.Reference!.ObjectNumber, ((PdfReference)kid.Elements["/Parent"]!).ObjectNumber);
            count += CountPages(kid);
        }
        Assert.Equal(count, node.Elements.GetInteger("/Count"));
        return count;
    }

    static void AssertValuesOfNode3(PdfPage page)
    {
        Assert.Equal(400, page.MediaBox.Width, 3);
        Assert.Equal(380, page.CropBox.Width, 3);
        Assert.Equal(90, page.Rotate);
    }

    [Fact]
    public void A_page_inserted_inside_a_node_changes_that_node_and_the_nodes_above_it()
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.Insert(1));

        var written = Written(original, result);
        Assert.Equal(new[] { 2, 3, 9 }, written);
        using var reopened = Reopen(result);
        Assert.Equal(new[] { 5, 9, 6, 7, 8 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
        AssertValuesOfNode3(reopened.Pages[0]);
        AssertValuesOfNode3(reopened.Pages[2]);
    }

    [Fact]
    public void A_new_page_does_not_inherit_what_it_does_not_have()
    {
        var original = Nested();
        double width = 0, height = 0;
        var result = Save(original, document => {
            var page = document.Pages.Insert(1);
            width = page.MediaBox.Width;
            height = page.MediaBox.Height;
        });

        var page = WrittenObject(original, result, 9);
        Assert.Matches(@"/Rotate\s*0\b", page);
        Assert.Contains("/CropBox", page);
        using var reopened = Reopen(result);
        var newPage = reopened.Pages[1];
        Assert.Equal(0, newPage.Rotate);
        Assert.Equal(width, newPage.MediaBox.Width, 3);
        Assert.Equal(height, newPage.MediaBox.Height, 3);
        Assert.Equal(newPage.MediaBox, newPage.CropBox);
    }

    [Fact]
    public void A_crop_box_follows_a_page_size_set_after_the_insert()
    {
        var original = Nested();
        var result = Save(original, document => {
            var page = document.Pages.Insert(1);
            page.MediaBox = new PdfRectangle(new XRect(0, 0, 500, 700));
        });

        using var reopened = Reopen(result);
        Assert.Equal(new PdfRectangle(new XRect(0, 0, 500, 700)), reopened.Pages[1].CropBox);
    }

    [Theory]
    [InlineData(0, new[] { 9, 5, 6, 7, 8 })]
    [InlineData(4, new[] { 5, 6, 7, 8, 9 })]
    public void A_page_at_either_end_goes_into_the_root(int index, int[] order)
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.Insert(index));

        Assert.Equal(new[] { 2, 9 }, Written(original, result));
        using var reopened = Reopen(result);
        Assert.Equal(order, PageNumbers(reopened));
        AssertConsistentTree(reopened);
    }

    [Fact]
    public void Removing_a_page_changes_its_node_and_the_nodes_above_it()
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.RemoveAt(0));

        Assert.Equal(new[] { 2, 3 }, Written(original, result));
        using var reopened = Reopen(result);
        Assert.Equal(new[] { 6, 7, 8 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
        AssertValuesOfNode3(reopened.Pages[0]);
    }

    [Fact]
    public void A_node_left_without_pages_is_taken_out_of_the_tree()
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.Remove(document.Pages[3]));

        Assert.Equal(new[] { 2 }, Written(original, result));
        using var reopened = Reopen(result);
        Assert.Equal(new[] { 5, 6, 7 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
    }

    [Fact]
    public void A_page_moved_within_its_node_is_not_written()
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.MovePage(0, 1));

        Assert.Equal(new[] { 3 }, Written(original, result));
        using var reopened = Reopen(result);
        Assert.Equal(new[] { 6, 5, 7, 8 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
        AssertValuesOfNode3(reopened.Pages[1]);
    }

    [Theory]
    [InlineData(1, 0, new[] { 6, 5, 7, 8 })]
    [InlineData(0, 2, new[] { 6, 7, 5, 8 })]
    public void A_page_moved_to_either_end_of_its_node_stays_in_it(int from, int to, int[] order)
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.MovePage(from, to));

        Assert.Equal(new[] { 3 }, Written(original, result));
        using var reopened = Reopen(result);
        Assert.Equal(order, PageNumbers(reopened));
        AssertConsistentTree(reopened);
    }

    [Fact]
    public void Moving_a_page_and_moving_it_back_keeps_the_file()
    {
        var original = Nested();
        var result = Save(original, document => {
            document.Pages.MovePage(0, 3);
            document.Pages.MovePage(3, 0);
        });

        Assert.Equal(original, result);
    }

    [Fact]
    public void A_page_moved_to_another_node_writes_the_values_it_inherited()
    {
        var original = Nested();
        var result = Save(original, document => document.Pages.MovePage(1, 3));

        Assert.Equal(new[] { 2, 3, 6 }, Written(original, result));
        var page = WrittenObject(original, result, 6);
        Assert.Matches(@"/MediaBox\s*\[0 0 400 300\]", page);
        Assert.Matches(@"/Rotate\s*90\b", page);
        using var reopened = Reopen(result);
        Assert.Equal(new[] { 5, 7, 8, 6 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
        AssertValuesOfNode3(reopened.Pages[3]);
        Assert.Equal(200, reopened.Pages[2].MediaBox.Width, 3);
    }

    [Fact]
    public void A_node_emptied_by_removing_a_page_that_stays_referenced_is_not_written()
    {
        // The bookmark keeps page 8, which still names node 4 as its parent.
        var original = TestPdf.Create(new[] {
            /* 1 */ "<< /Type /Catalog /Pages 2 0 R /Outlines 9 0 R >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            /* 3 */ "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R] /Count 1 /MediaBox [0 0 400 300] >>",
            /* 4 */ "<< /Type /Pages /Parent 2 0 R /Kids [6 0 R] /Count 1 /MediaBox [0 0 200 100] >>",
            /* 5 */ "<< /Type /Page /Parent 3 0 R >>",
            /* 6 */ "<< /Type /Page /Parent 4 0 R >>",
            /* 7 */ "<< /Title (Second page) /Parent 9 0 R /Dest [6 0 R /Fit] >>",
            /* 8 */ "<< /Title (First page) /Parent 9 0 R /Dest [5 0 R /Fit] /Next 7 0 R >>",
            /* 9 */ "<< /Type /Outlines /First 8 0 R /Last 7 0 R /Count 2 >>",
        });

        var result = Save(original, document => document.Pages.RemoveAt(1));

        Assert.Equal(new[] { 2 }, Written(original, result));
        using var reopened = Reopen(result);
        Assert.Equal(new[] { 5 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
    }

    [Fact]
    public void An_indirect_rotation_in_the_tree_is_read_and_kept()
    {
        // Node 3 holds /Rotate as the indirect integer 8, which was ignored.
        var original = TestPdf.Create(new[] {
            /* 1 */ "<< /Type /Catalog /Pages 2 0 R >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 3 >>",
            /* 3 */ "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2 /MediaBox [0 0 400 300] /Rotate 8 0 R >>",
            /* 4 */ "<< /Type /Pages /Parent 2 0 R /Kids [7 0 R] /Count 1 /MediaBox [0 0 200 100] >>",
            /* 5 */ "<< /Type /Page /Parent 3 0 R >>",
            /* 6 */ "<< /Type /Page /Parent 3 0 R >>",
            /* 7 */ "<< /Type /Page /Parent 4 0 R >>",
            /* 8 */ "90",
        });
        using (var read = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Import))
            Assert.Equal(90, read.Pages[0].Rotate);

        var result = Save(original, document => {
            document.Pages.Insert(1);
            document.Pages.MovePage(0, 3);
        });

        using var reopened = Reopen(result);
        Assert.Equal(new[] { 9, 6, 7, 5 }, PageNumbers(reopened));
        Assert.Equal(0, reopened.Pages[0].Rotate);
        Assert.Equal(90, reopened.Pages[1].Rotate);
        Assert.Equal(90, reopened.Pages[3].Rotate);
    }

    [Fact]
    public void An_indirect_root_rotation_survives_a_Modify_save()
    {
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 400 300] /Rotate 4 0 R >>",
            "<< /Type /Page /Parent 2 0 R >>",
            "90",
        });
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
        using var output = new MemoryStream();
        document.Save(output);

        using var reopened = Reopen(output.ToArray());
        Assert.Equal(90, reopened.Pages[0].Rotate);
    }

    [Theory]
    [InlineData(PdfDocumentOpenMode.Import)]
    [InlineData(PdfDocumentOpenMode.Modify)]
    [InlineData(PdfDocumentOpenMode.Append)]
    public void A_page_with_its_own_indirect_boxes_opens_under_a_node_with_boxes(PdfDocumentOpenMode mode)
    {
        // Opening threw "PdfRectangle is not implemented as an array".
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 400 300] /CropBox [10 10 390 290] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox 4 0 R /CropBox 5 0 R >>",
            "[0 0 200 100]",
            "[5 5 195 95]",
        });

        using var document = PdfReader.Open(new MemoryStream(original), mode);
        Assert.Equal(200, document.Pages[0].MediaBox.Width, 3);
        Assert.Equal(190, document.Pages[0].CropBox.Width, 3);
    }

    [Fact]
    public void A_box_or_rotation_that_refers_to_null_is_inherited()
    {
        // ISO 32000-1 7.3.9: an entry whose value is null is the same as no entry.
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 400 300] /CropBox [10 10 390 290] /Rotate 90 >>",
            "<< /Type /Page /Parent 2 0 R /CropBox 4 0 R /Rotate 4 0 R >>",
            "null",
        });
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
        using var output = new MemoryStream();
        document.Save(output);

        using var reopened = Reopen(output.ToArray());
        AssertValuesOfNode3(reopened.Pages[0]);
    }

    [Fact]
    public void A_new_page_under_a_node_with_rotation_0_gets_no_rotation_entry()
    {
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /Rotate 0 /MediaBox [0 0 400 300] >>",
            "<< /Type /Page /Parent 2 0 R >>",
        });

        var result = Save(original, document => document.AddPage());

        Assert.DoesNotContain("/Rotate", WrittenObject(original, result, 4));
    }

    [Fact]
    public void A_removed_page_can_be_inserted_again_elsewhere()
    {
        var original = Nested();
        var result = Save(original, document => {
            var page = document.Pages[3];
            document.Pages.Remove(page);
            document.Pages.Insert(1, page);
        });

        using var reopened = Reopen(result);
        Assert.Equal(new[] { 5, 8, 6, 7 }, PageNumbers(reopened));
        AssertConsistentTree(reopened);
        // Page 8 keeps its own size under node 3, and takes no crop box or rotation from it.
        var page = reopened.Pages[1];
        Assert.Equal(200, page.MediaBox.Width, 3);
        Assert.Equal(0, page.Rotate);
        Assert.Equal(page.MediaBox, page.CropBox);
    }

    [Fact]
    public void Pages_of_another_document_can_be_merged_into_a_nested_tree()
    {
        var original = Nested();
        var source = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 200] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 250 210] >>",
        });

        var result = Save(original, document => {
            using var import = PdfReader.Open(new MemoryStream(source), PdfDocumentOpenMode.Import);
            document.AppendPages(import);
        });

        var written = Written(original, result);
        // Without bookmarks to merge, the catalog does not change: it got an empty outline.
        Assert.DoesNotContain(1, written);
        Assert.Contains(2, written);
        Assert.DoesNotContain(3, written);
        Assert.DoesNotContain(4, written);
        using var reopened = Reopen(result);
        Assert.Equal(6, reopened.PageCount);
        Assert.Equal(new[] { 5, 6, 7, 8 }, PageNumbers(reopened).Take(4));
        Assert.Equal(240, reopened.Pages[4].MediaBox.Width, 3);
        Assert.Equal(250, reopened.Pages[5].MediaBox.Width, 3);
        AssertConsistentTree(reopened);
    }

    [Fact]
    public void Several_changes_keep_the_tree_consistent()
    {
        var original = Nested();
        var result = Save(original, document => {
            document.Pages.Insert(2);
            document.Pages.Insert(3);
            document.Pages.RemoveAt(0);
            document.Pages.MovePage(4, 1);
            document.Pages.RemoveAt(document.PageCount - 1);
            document.AddPage();
        });

        using var reopened = Reopen(result);
        Assert.Equal(5, reopened.PageCount);
        AssertConsistentTree(reopened);
        using var again = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Append);
        Assert.Equal(PageNumbers(reopened), PageNumbers(again));
    }

    [Fact]
    public void A_new_page_in_a_flat_tree_does_not_inherit_from_the_root()
    {
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 400 300] /CropBox [10 10 390 290] /Rotate 90 >>",
            "<< /Type /Page /Parent 2 0 R >>",
        });

        var result = Save(original, document => document.AddPage());

        Assert.Equal(new[] { 2, 4 }, Written(original, result));
        using var reopened = Reopen(result);
        AssertValuesOfNode3(reopened.Pages[0]);
        Assert.Equal(0, reopened.Pages[1].Rotate);
        Assert.Equal(reopened.Pages[1].MediaBox, reopened.Pages[1].CropBox);
    }

    [Fact]
    public void A_new_page_does_not_inherit_from_the_root_in_Modify_mode()
    {
        // The new page came out rotated by the root's /Rotate.
        var original = TestPdf.Create(new[] {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 400 300] /CropBox [10 10 390 290] /Rotate 90 >>",
            "<< /Type /Page /Parent 2 0 R >>",
        });
        using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
        document.AddPage();
        using var output = new MemoryStream();
        document.Save(output);

        using var reopened = Reopen(output.ToArray());
        AssertValuesOfNode3(reopened.Pages[0]);
        Assert.Equal(0, reopened.Pages[1].Rotate);
        Assert.False(reopened.Pages[1].HasCropBox);
    }

    [Fact]
    public void A_removed_page_can_be_inserted_again_in_Modify_mode()
    {
        // Inserting threw "Object already in table." in every mode.
        using var document = PdfReader.Open(new MemoryStream(Nested()), PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        document.Pages.Remove(page);
        document.Pages.Insert(2, page);
        using var output = new MemoryStream();
        document.Save(output);

        using var reopened = Reopen(output.ToArray());
        Assert.Equal(4, reopened.PageCount);
        AssertValuesOfNode3(reopened.Pages[2]);
        Assert.Equal(200, reopened.Pages[3].MediaBox.Width, 3);
    }

    [Fact]
    public void A_certified_document_refuses_page_changes_in_a_nested_tree()
    {
        // Pages 5 and 8 are in node 3. Signature 6 certifies the document with /P 3.
        var original = TestPdf.Create(new[] {
            /* 1 */ "<< /Type /Catalog /Pages 2 0 R /AcroForm 4 0 R /Perms << /DocMDP 6 0 R >> >>",
            /* 2 */ "<< /Type /Pages /Kids [3 0 R] /Count 2 >>",
            /* 3 */ "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 8 0 R] /Count 2 /MediaBox [0 0 400 400] >>",
            /* 4 */ "<< /Fields [7 0 R] /SigFlags 3 >>",
            /* 5 */ "<< /Type /Page /Parent 3 0 R /Annots [7 0 R] >>",
            /* 6 */ "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> " +
                    "/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 3 /V /1.2 >> >>] >>",
            /* 7 */ "<< /FT /Sig /T (approval) /V 6 0 R /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 5 0 R /F 132 >>",
            /* 8 */ "<< /Type /Page /Parent 3 0 R >>",
        });

        foreach (var change in new Action<PdfDocument>[] {
                     document => document.Pages.Insert(1),
                     document => document.Pages.RemoveAt(1),
                     document => document.Pages.MovePage(1, 0),
                 })
        {
            using var document = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Append);
            change(document);
            using var output = new MemoryStream();
            var refused = Assert.Throws<InvalidOperationException>(() => document.Save(output));
            Assert.Contains("DocMDP", refused.Message);
            Assert.Equal(0, output.Length);
        }
    }
}
