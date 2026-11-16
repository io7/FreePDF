// Original synthetic fixtures. No customer documents or external validator code.
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public class AccessibilityScannerTests
{
    static PdfDictionary Child(PdfDictionary d) => d.Elements.GetDictionary("/K") ?? d.Elements.GetArray("/K")!.Elements.GetDictionary(0)!;

    static byte[] Fixture(Action<PdfDocument>? change = null)
    {
        using var saved = new MemoryStream();
        using (var document = new PdfDocument())
        {
            document.Info.Title = "Synthetic accessibility fixture";
            var builder = UAManager.ForDocument(document).StructureBuilder;
            using (var graphics = XGraphics.FromPdfPage(document.AddPage()))
            {
                builder.BeginElement(PdfIllustrationElementTag.Figure, "A rectangle", new XRect(40, 40, 100, 60));
                graphics.DrawRectangle(XBrushes.Red, 40, 40, 100, 60);
                builder.End();
            }
            document.Save(saved);
        }
        if (change == null) return saved.ToArray();
        using var modified = PdfReader.Open(new MemoryStream(saved.ToArray()), PdfDocumentOpenMode.Modify);
        change(modified);
        using var output = new MemoryStream();
        modified.Save(output);
        return output.ToArray();
    }

    static PdfAccessibilityReport Scan(byte[] bytes, PdfAccessibilityScanOptions? options = null)
        => PdfAccessibilityScanner.Scan(new MemoryStream(bytes), options);

    static void Fails(PdfAccessibilityReport report, string rule)
        => Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-" + rule && f.Status == PdfAccessibilityCheckStatus.Failed);

    static PdfAccessibilityCheckStatus Outcome(PdfAccessibilityReport report, string rule)
        => report.Checks.Single(c => c.Id == "FPDF-A11Y-" + rule).Status;

    // The fixture page with a non-embedded standard font as /FReview and the given content appended.
    static byte[] WithReviewFont(string content) => Fixture(d =>
    {
        var resources = d.Pages[0].Elements.GetDictionary("/Resources")!;
        var font = new PdfDictionary(d);
        font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica");
        var fonts = resources.Elements.GetDictionary("/Font") ?? new PdfDictionary(d);
        fonts.Elements["/FReview"] = font; resources.Elements["/Font"] = fonts;
        d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content));
    });

    [Fact]
    public void Tagged_figure_passes_implemented_checks_but_still_requires_review()
    {
        var report = Scan(Fixture());
        Assert.True(report.ScanCompleted, String.Join("\n", report.Findings.Select(f => f.Message)));
        Assert.Equal(PdfAccessibilityScanStatus.NeedsReview, report.Status);
        Assert.Equal("Partial", report.Coverage);
        Assert.DoesNotContain(report.Checks, c => c.Status is PdfAccessibilityCheckStatus.Failed or PdfAccessibilityCheckStatus.NotChecked);
        Assert.Contains(report.StructureOrder, e => e.Role == "/Figure" && e.AlternativeText == "A rectangle" && e.Page == 1);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-REVIEW");
    }

    [Theory]
    [InlineData("/Lang", "LANG")]
    [InlineData("/MarkInfo", "MARKED")]
    [InlineData("/ViewerPreferences", "DISPLAYTITLE")]
    [InlineData("/StructTreeRoot", "TREE")]
    public void Missing_catalog_requirements_have_stable_rule_ids(string key, string rule)
        => Fails(Scan(Fixture(d => d.Catalog.Elements.Remove(key))), rule);

    [Fact]
    public void Missing_alt_text_is_located_in_the_structure_tree()
    {
        var report = Scan(Fixture(d => Child(Child(d.Catalog.Elements.GetDictionary("/StructTreeRoot")!)).Elements.Remove("/Alt")));
        Fails(report, "ALT");
        var finding = Assert.Single(report.Findings.Where(f => f.RuleId == "FPDF-A11Y-ALT"));
        Assert.Equal(1, finding.Page);
        Assert.StartsWith("/StructTreeRoot/K", finding.Location);
    }

    [Fact]
    public void ActualText_can_supply_the_figure_text_alternative()
    {
        var report = Scan(Fixture(d =>
        {
            var figure = Child(Child(d.Catalog.Elements.GetDictionary("/StructTreeRoot")!));
            figure.Elements.Remove("/Alt"); figure.Elements.SetString("/ActualText", "A rectangle");
        }));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, report.Checks.Single(c => c.Id == "FPDF-A11Y-ALT").Status);
    }

    [Theory]
    [InlineData("/Unknown", "ROLE")]
    [InlineData("/TD", "NESTING")]
    public void Unknown_roles_and_orphan_cells_fail(string role, string rule)
        => Fails(Scan(Fixture(d => Child(Child(d.Catalog.Elements.GetDictionary("/StructTreeRoot")!)).Elements.SetName("/S", role))), rule);

    [Fact]
    public void Cyclic_roles_and_structure_cycles_terminate_with_failures()
    {
        var bytes = Fixture(d =>
        {
            var root = d.Catalog.Elements.GetDictionary("/StructTreeRoot")!;
            var figure = Child(Child(root));
            figure.Elements.SetName("/S", "/Custom");
            var map = new PdfDictionary(d); map.Elements.SetName("/Custom", "/Custom"); root.Elements["/RoleMap"] = map;
            figure.Elements["/K"] = figure.Reference!;
        });
        var report = Scan(bytes);
        Fails(report, "ROLE"); Fails(report, "TREE");
    }

    [Fact]
    public void Invalid_parent_and_parent_tree_are_detected()
    {
        var report = Scan(Fixture(d =>
        {
            var root = d.Catalog.Elements.GetDictionary("/StructTreeRoot")!;
            Child(Child(root)).Elements["/P"] = root.Reference!;
            root.Elements.Remove("/ParentTree");
        }));
        Fails(report, "TREE"); Fails(report, "MCID");
    }

    [Fact]
    public void Painting_appended_outside_a_tag_is_detected()
    {
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("0 0 10 10 re f"))));
        Fails(report, "CONTENT");
    }

    [Fact]
    public void Duplicate_and_unassociated_content_mcids_are_detected()
    {
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/P <</MCID 0>> BDC EMC /P <</MCID 123>> BDC EMC"))));
        Fails(report, "MCID");
        Assert.Contains(report.Findings, f => f.Message.Contains("more than once"));
        Assert.Contains(report.Findings, f => f.Message.Contains("no structure-tree"));
    }

    [Fact]
    public void Complex_marked_content_is_incomplete_instead_of_a_false_pass()
    {
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/P <</MCID 123 /ActualText (example)>> BDC EMC"))));
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.Status == PdfAccessibilityCheckStatus.NotChecked && f.RuleId == "FPDF-A11Y-MCID");
    }

    [Fact]
    public void Stream_position_and_bytes_are_preserved_and_original_bytes_are_hashed()
    {
        var bytes = Fixture();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 }.Concat(bytes).ToArray());
        stream.Position = 3;
        var before = stream.ToArray();
        var report = PdfAccessibilityScanner.Scan(stream);
        Assert.Equal(3, stream.Position); Assert.Equal(before, stream.ToArray());
        using var hash = SHA256.Create();
        Assert.Equal(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(), report.DocumentSha256);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("nodes")]
    [InlineData("decoded")]
    [InlineData("findings")]
    public void Resource_exhaustion_cannot_be_reported_as_a_completed_scan(string limit)
    {
        var options = new PdfAccessibilityScanOptions();
        switch (limit)
        {
            case "file": options.MaxFileBytes = 20; break;
            case "nodes": options.MaxNodes = 1; break;
            case "decoded": options.MaxDecodedBytes = 1; break;
            case "findings": options.MaxFindings = 1; break;
        }
        var report = Scan(Fixture(d => { d.Catalog.Elements.Remove("/Lang"); d.Catalog.Elements.Remove("/MarkInfo"); }), options);
        Assert.False(report.ScanCompleted); Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-READ");
    }

    [Fact]
    public void Malformed_and_password_protected_input_cannot_pass()
    {
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, Scan(Encoding.ASCII.GetBytes("not a PDF")).Status);
        var encrypted = Fixture(d => d.SecuritySettings.UserPassword = "synthetic-test-password");
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, Scan(encrypted).Status);
        Assert.Equal(PdfAccessibilityScanStatus.NeedsReview, Scan(encrypted, new PdfAccessibilityScanOptions { Password = "synthetic-test-password" }).Status);
    }

    [Fact]
    public void Cancellation_propagates_without_closing_the_callers_stream()
    {
        using var stream = new MemoryStream(Fixture());
        Assert.Throws<OperationCanceledException>(() => PdfAccessibilityScanner.Scan(stream, cancellationToken: new CancellationToken(true)));
        Assert.Equal(0, stream.Position); Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Page_selected_fonts_require_embedded_programs(bool removeProgram)
    {
        TestFonts.Initialize();
        using var bytes = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            pdf.Info.Title = "Text fixture";
            var tags = UAManager.ForDocument(pdf).StructureBuilder;
            using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            {
                tags.BeginElement(PdfBlockLevelElementTag.Paragraph);
                gfx.DrawString("Synthetic text", new XFont("FreePDF Synthetic", 12), XBrushes.Black, 40, 40);
                tags.End();
            }
            pdf.Save(bytes);
        }
        using var modified = PdfReader.Open(new MemoryStream(bytes.ToArray()), PdfDocumentOpenMode.Modify);
        if (removeProgram)
        {
            var fonts = modified.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            foreach (var value in fonts.Elements.Values)
            {
                var font = (PdfDictionary)(value is PdfReference r ? r.Value : value)!;
                if (font.Elements.GetName("/Subtype") == "/Type0") font = font.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
                font.Elements.GetDictionary("/FontDescriptor")!.Elements.Remove("/FontFile2");
            }
        }
        using var output = new MemoryStream(); modified.Save(output);
        var report = Scan(output.ToArray());
        Assert.Equal(removeProgram ? PdfAccessibilityCheckStatus.Failed : PdfAccessibilityCheckStatus.Passed, report.Checks.Single(c => c.Id == "FPDF-A11Y-FONT").Status);
    }

    [Fact]
    public void Untagged_links_and_wrong_tab_order_are_reported()
    {
        var report = Scan(Fixture(d =>
        {
            d.Pages[0].AddWebLink(new PdfRectangle(new XPoint(0, 0), new XPoint(10, 10)), "https://example.invalid/synthetic");
            d.Pages[0].Elements.SetName("/Tabs", "/R");
        }));
        Fails(report, "ANNOT"); Fails(report, "TABS");
    }

    [Theory]
    [InlineData("<pdfuaid:part>1", "<pdfuaid:part>2", "UAID")]
    [InlineData("<rdf:RDF", "?rdf:RDF", "TITLE")]
    public void Unsupported_profiles_and_malformed_metadata_are_not_clean_scans(string from, string to, string rule)
    {
        var bytes = Fixture();
        var pattern = Encoding.ASCII.GetBytes(from);
        var replacement = Encoding.ASCII.GetBytes(to);
        Assert.Equal(pattern.Length, replacement.Length);
        int position = -1;
        for (int i = 0; i <= bytes.Length - pattern.Length; i++)
            if (bytes.Skip(i).Take(pattern.Length).SequenceEqual(pattern)) { position = i; break; }
        Assert.True(position >= 0, "Fixture metadata must be visible and uncompressed.");
        Buffer.BlockCopy(replacement, 0, bytes, position, replacement.Length);
        var report = Scan(bytes);
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-" + rule && f.Status == PdfAccessibilityCheckStatus.NotChecked);
    }

    [Fact]
    public void Form_xobject_content_is_explicitly_unassessed()
    {
        var bytes = Fixture(d =>
        {
            var form = new PdfDictionary(d);
            form.Elements.SetName("/Type", "/XObject"); form.Elements.SetName("/Subtype", "/Form");
            form.Elements.SetRectangle("/BBox", new PdfRectangle(new XPoint(0, 0), new XPoint(10, 10)));
            form.CreateStream(Encoding.ASCII.GetBytes("0 0 10 10 re f"));
            d.Internals.AddObject(form);
            var xobjects = new PdfDictionary(d); xobjects.Elements["/Form0"] = form.Reference!;
            d.Pages[0].Elements.GetDictionary("/Resources")!.Elements["/XObject"] = xobjects;
            d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC /Form0 Do EMC"));
        });
        var report = Scan(bytes);
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-CONTENT" && f.Status == PdfAccessibilityCheckStatus.NotChecked);
    }

    [Fact]
    public void A_non_boolean_suspects_flag_is_not_treated_as_false()
        => Fails(Scan(Fixture(d => d.Catalog.Elements.GetDictionary("/MarkInfo")!.Elements.SetString("/Suspects", "false"))), "SUSPECTS");

    [Fact]
    public void Matching_but_negative_parent_tree_keys_are_invalid()
    {
        var report = Scan(Fixture(d =>
        {
            d.Pages[0].Elements.SetInteger("/StructParents", -1);
            var tree = d.Catalog.Elements.GetDictionary("/StructTreeRoot")!.Elements.GetDictionary("/ParentTree")!;
            tree.Elements.GetArray("/Nums")!.Elements[0] = new PdfInteger(-1);
        }));
        Fails(report, "MCID");
    }

    static void RepairedByReader(byte[] damaged)
    {
        var report = Scan(damaged);
        Assert.False(report.ScanCompleted);
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "READ"));
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-READ" && f.Message.Contains("repaired"));
    }

    [Fact]
    public void Shifted_cross_reference_offsets_are_a_repair_not_a_completed_scan()
    {
        var bytes = Fixture();
        int header = Array.IndexOf(bytes, (byte)'\n') + 1;
        RepairedByReader(bytes.Take(header).Concat(Encoding.ASCII.GetBytes("   ")).Concat(bytes.Skip(header)).ToArray());
    }

    [Fact]
    public void A_missing_cross_reference_table_and_trailer_are_a_repair_not_a_completed_scan()
    {
        var bytes = Fixture();
        int table = Encoding.Latin1.GetString(bytes).LastIndexOf("\nxref", StringComparison.Ordinal);
        Assert.True(table > 0, "Fixture must end with a classic cross-reference table.");
        RepairedByReader(bytes.Take(table + 1).ToArray());
    }

    [Fact]
    public void A_corrected_stream_length_is_a_repair_not_a_completed_scan()
    {
        const string marker = "% synthetic length marker";
        var bytes = Fixture(d =>
        {
            d.Options.CompressContentStreams = false;
            d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(marker + "\n/Artifact BMC 0 0 10 10 re f EMC"));
        });
        var text = Encoding.Latin1.GetString(bytes);
        int digits = text.LastIndexOf("/Length ", text.IndexOf(marker, StringComparison.Ordinal), StringComparison.Ordinal) + "/Length ".Length;
        int count = 0;
        while (Char.IsDigit(text[digits + count])) count++;
        Assert.True(count >= 2, "The marked stream must have a direct two-digit or longer /Length.");
        // Same width, so the cross-reference offsets stay right; only the declared length is too short.
        var shorter = Encoding.ASCII.GetBytes("1" + new string('0', count - 1));
        Buffer.BlockCopy(shorter, 0, bytes, digits, count);
        RepairedByReader(bytes);
    }

    [Fact]
    public void A_valid_incremental_update_is_not_mistaken_for_a_repair()
    {
        using var document = PdfReader.Open(new MemoryStream(Fixture()), PdfDocumentOpenMode.Append);
        document.Info.Subject = "Synthetic update";
        using var updated = new MemoryStream();
        document.Save(updated);
        var report = Scan(updated.ToArray());
        Assert.True(report.ScanCompleted, String.Join("\n", report.Findings.Select(f => f.Message)));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "READ"));
    }

    [Theory]
    [InlineData("BT /FReview 12 Tf 14.4 TL ET")]
    [InlineData("/Artifact BMC BT 3 Tr /FReview 12 Tf 20 20 Td (Invisible text layer) Tj ET EMC")]
    public void Fonts_that_render_no_glyphs_need_no_embedded_program(string content)
        => Assert.NotEqual(PdfAccessibilityCheckStatus.Failed, Outcome(Scan(WithReviewFont(content)), "FONT"));

    [Theory]
    [InlineData("/Artifact BMC BT 3 Tr /FReview 12 Tf (hidden) Tj 0 Tr (shown) Tj ET EMC")]
    [InlineData("/Artifact BMC q 3 Tr Q BT /FReview 12 Tf 20 20 Td (shown) Tj ET EMC")]
    [InlineData("/Artifact BMC BT /FReview 12 Tf 20 20 Td [(sho) 20 (wn)] TJ ET EMC")]
    public void Visible_text_in_a_font_without_its_program_fails(string content)
        => Assert.Equal(PdfAccessibilityCheckStatus.Failed, Outcome(Scan(WithReviewFont(content)), "FONT"));

    [Fact]
    public void Dense_valid_page_content_completes_without_exhausting_the_structure_limit()
    {
        var operations = new StringBuilder("/Artifact BMC\n");
        for (int i = 0; i < 60000; i++) operations.Append("0 0 1 1 re f\n");
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(operations.Append("EMC").ToString()))));
        Assert.True(report.ScanCompleted, String.Join("\n", report.Findings.Select(f => f.Message)));
        Assert.Equal(PdfAccessibilityScanStatus.NeedsReview, report.Status);
    }

    [Fact]
    public void Checks_interrupted_by_a_limit_are_not_reported_as_passed()
    {
        var report = Scan(Fixture(), new PdfAccessibilityScanOptions { MaxNodes = 1 });
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "LANG"));
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "TREE"));
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "ROLE"));
        Assert.Contains(report.Findings, f => f.RuleId == "FPDF-A11Y-READ" && f.Message.Contains("MaxNodes"));
    }

    [Fact]
    public void An_incomplete_scan_still_reports_that_it_found_failures()
    {
        var bytes = Fixture(d =>
        {
            d.Catalog.Elements.Remove("/Lang");
            var form = new PdfDictionary(d);
            form.Elements.SetName("/Type", "/XObject"); form.Elements.SetName("/Subtype", "/Form");
            form.Elements.SetRectangle("/BBox", new PdfRectangle(new XPoint(0, 0), new XPoint(10, 10)));
            form.CreateStream(Encoding.ASCII.GetBytes("0 0 10 10 re f"));
            d.Internals.AddObject(form);
            var xobjects = new PdfDictionary(d); xobjects.Elements["/Form0"] = form.Reference!;
            d.Pages[0].Elements.GetDictionary("/Resources")!.Elements["/XObject"] = xobjects;
            d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC /Form0 Do EMC"));
        });
        var report = Scan(bytes);
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Assert.True(report.HasFailures);
        Assert.False(Scan(Fixture()).HasFailures);
    }
}
