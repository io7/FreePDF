// Original synthetic page and Form XObject accessibility checks. MIT.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;
using Xunit;

namespace FreePDF.RegressionTests;

public partial class AccessibilityScannerTests
{
    [Fact]
    public void Reused_decorative_form_inherits_the_callers_marking()
    {
        var bytes = Fixture(d =>
        {
            AccessibilityFormFixture.AddForm(d, d.Pages[0].Elements.GetDictionary("/Resources")!, "/Form0", "0 0 10 10 re f");
            d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC /Form0 Do /Form0 Do EMC"));
        });
        Assert.Equal(PdfAccessibilityScanStatus.NeedsReview, Scan(bytes).Status);
    }

    [Fact]
    public void Page_and_nested_form_own_separate_mcids_and_parent_tree_entries()
    {
        var report = Scan(Fixture(d => AccessibilityFormFixture.AddTaggedForm(d)));
        Assert.True(report.Status == PdfAccessibilityScanStatus.NeedsReview, String.Join("\n", report.Findings.Select(f => f.Message)));
        Assert.Equal(PdfAccessibilityCheckStatus.Passed, Outcome(report, "MCID"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Whole_form_object_reference_uses_its_singular_parent_tree_entry(bool wrongParent)
    {
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            var root = d.Catalog.Elements.GetDictionary("/StructTreeRoot")!;
            var figure = Child(Child(root));
            var form = AccessibilityFormFixture.AddForm(d, page.Elements.GetDictionary("/Resources")!, "/Whole", "1 1 8 8 re f");
            var objr = new PdfDictionary(d);
            objr.Elements.SetName("/Type", "/OBJR");
            objr.Elements["/Pg"] = page.Reference!;
            objr.Elements["/Obj"] = form.Reference!;
            var kids = new PdfArray(d);
            kids.Elements.Add(figure.Elements["/K"]!);
            kids.Elements.Add(objr);
            figure.Elements["/K"] = kids;
            var key = root.Elements.GetInteger("/ParentTreeNextKey");
            form.Elements.SetInteger("/StructParent", key);
            root.Elements.SetInteger("/ParentTreeNextKey", key + 1);
            var nums = root.Elements.GetDictionary("/ParentTree")!.Elements.GetArray("/Nums")!;
            nums.Elements.Add(new PdfInteger(key));
            nums.Elements.Add(wrongParent ? root.Reference! : figure.Reference!);
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Whole Do"));
        }));
        Assert.Equal(wrongParent ? PdfAccessibilityScanStatus.Failed : PdfAccessibilityScanStatus.NeedsReview, report.Status);
        if (wrongParent) Fails(report, "MCID");
    }

    [Theory]
    [InlineData("wrong-parent-key")]
    [InlineData("repeat")]
    [InlineData("missing-sequence")]
    [InlineData("wrong-page")]
    [InlineData("invalid-page")]
    public void Incorrect_form_structure_references_fail(string change)
    {
        var report = Scan(Fixture(d =>
        {
            var inner = AccessibilityFormFixture.AddTaggedForm(d);
            if (change == "wrong-parent-key") inner.Elements.SetInteger("/StructParents", d.Pages[0].Elements.GetInteger("/StructParents"));
            if (change == "repeat") d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Outer Do"));
            if (change == "missing-sequence") inner.Stream.Value = Encoding.ASCII.GetBytes("/Artifact BMC 1 1 8 8 re f EMC");
            if (change is "wrong-page" or "invalid-page")
            {
                var figure = Child(Child(d.Catalog.Elements.GetDictionary("/StructTreeRoot")!));
                var kids = figure.Elements.GetArray("/K")!;
                var mcr = kids.Elements.GetDictionary(kids.Elements.Count - 1)!;
                mcr.Elements["/Pg"] = change == "wrong-page" ? d.AddPage().Reference! : new PdfInteger(0);
            }
        }));
        Fails(report, "MCID");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_mcid_must_be_a_non_negative_integer(bool negative)
    {
        var report = Scan(Fixture(d =>
        {
            var form = AccessibilityFormFixture.AddTaggedForm(d);
            var mark = form.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Properties")!.Elements.GetDictionary("/Mark")!;
            if (negative) mark.Elements.SetInteger("/MCID", -1);
            else mark.Elements.SetString("/MCID", "0");
        }));
        Fails(report, "MCID");
        Assert.Contains(report.Findings, f => f.Location.EndsWith("/Inner/Resources/Properties/Mark"));
    }

    [Fact]
    public void Unresolved_named_property_does_not_hide_later_untagged_paint()
    {
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(
            Encoding.ASCII.GetBytes("/Span /Missing BDC 0 0 1 1 re f EMC 2 2 1 1 re f"))));
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Fails(report, "CONTENT");
    }

    [Fact]
    public void Unresolved_marking_leaves_the_painting_check_unassessed()
    {
        var report = Scan(Fixture(d => d.Pages[0].Contents.AppendContent().CreateStream(
            Encoding.ASCII.GetBytes("/Span /Missing BDC 0 0 1 1 re f EMC"))));
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "CONTENT"));
        Assert.Equal(PdfAccessibilityCheckStatus.NotChecked, Outcome(report, "MCID"));
        Assert.False(report.HasFailures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_a_form_without_resources_can_use_the_pages_named_properties(bool localResources)
    {
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            var resources = page.Elements.GetDictionary("/Resources")!;
            var properties = new PdfDictionary(d);
            properties.Elements["/Layout"] = new PdfDictionary(d);
            resources.Elements["/Properties"] = properties;
            var outer = AccessibilityFormFixture.AddForm(d, resources, "/Outer", "/Inner Do");
            var inner = AccessibilityFormFixture.AddForm(d, outer.Elements.GetDictionary("/Resources")!, "/Inner",
                "/Artifact /Layout BDC 0 0 10 10 re f EMC");
            if (!localResources) inner.Elements.Remove("/Resources");
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Outer Do"));
        }));
        Assert.Equal(localResources ? PdfAccessibilityScanStatus.Incomplete : PdfAccessibilityScanStatus.NeedsReview, report.Status);
        if (localResources)
            Assert.Contains(report.Findings, f => f.Location.EndsWith("/Outer/Resources/XObject/Inner/Resources/Properties/Layout"));
    }

    [Theory]
    [InlineData("/Span /Attrs BDC 0 0 1 1 re f EMC", true)]
    [InlineData("/Artifact BMC /Span /Attrs BDC 0 0 1 1 re f EMC EMC", false)]
    [InlineData("/Span /Attrs DP 0 0 1 1 re f", true)]
    public void A_named_property_without_an_mcid_does_not_tag_paint(string content, bool failed)
    {
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            var properties = new PdfDictionary(d);
            var attributes = new PdfDictionary(d);
            attributes.Elements.SetString("/Lang", "en");
            properties.Elements["/Attrs"] = attributes;
            page.Elements.GetDictionary("/Resources")!.Elements["/Properties"] = properties;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content));
        }));
        Assert.Equal(failed ? PdfAccessibilityScanStatus.Failed : PdfAccessibilityScanStatus.NeedsReview, report.Status);
        if (failed) Fails(report, "CONTENT");
    }

    [Theory]
    [InlineData("BT 0 Tr ET /F Do", true)]
    [InlineData("BT 3 Tr ET /F Do", false)]
    [InlineData("BT 0 Tr ET /Invisible Do BT (still visible) Tj ET", true)]
    public void Form_text_uses_the_callers_graphics_state_and_restores_it_on_return(string content, bool failed)
    {
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            var resources = page.Elements.GetDictionary("/Resources")!;
            var font = new PdfDictionary(d);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Review"] = font;
            resources.Elements["/Font"] = fonts;
            AccessibilityFormFixture.AddForm(d, resources, "/F", "BT (inside) Tj ET");
            AccessibilityFormFixture.AddForm(d, resources, "/Invisible", "BT 3 Tr (invisible) Tj ET");
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Review 10 Tf ET " + content + " EMC"));
        }));
        Assert.Equal(failed ? PdfAccessibilityScanStatus.Failed : PdfAccessibilityScanStatus.NeedsReview, report.Status);
        if (failed) Fails(report, "FONT");
    }

    [Fact]
    public void A_form_cannot_close_the_callers_marked_content()
    {
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            AccessibilityFormFixture.AddForm(d, page.Elements.GetDictionary("/Resources")!, "/F", "EMC");
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC /F Do EMC"));
        }));
        Fails(report, "CONTENT");
        Assert.Contains(report.Findings, f => f.Page == 1 && f.Location == "/Resources/XObject/F");
    }

    [Fact]
    public void A_recursive_form_is_incomplete_and_later_page_paint_is_still_checked()
    {
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            var form = AccessibilityFormFixture.AddForm(d, page.Elements.GetDictionary("/Resources")!, "/F", "/Self Do");
            var objects = new PdfDictionary(d);
            objects.Elements["/Self"] = form.Reference!;
            form.Elements.GetDictionary("/Resources")!.Elements["/XObject"] = objects;
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/F Do 1 1 2 2 re f"));
        }));
        Assert.Equal(PdfAccessibilityScanStatus.Incomplete, report.Status);
        Fails(report, "CONTENT");
        Assert.Contains(report.Findings, f => f.Status == PdfAccessibilityCheckStatus.NotChecked && f.Location.EndsWith("/Self"));
    }

    [Fact]
    public void A_long_form_name_is_shortened_in_every_finding_inside_the_form()
    {
        var name = "/" + new string('A', 4096);
        var report = Scan(Fixture(d =>
        {
            var page = d.Pages[0];
            AccessibilityFormFixture.AddForm(d, page.Elements.GetDictionary("/Resources")!, name,
                String.Concat(Enumerable.Repeat("/Missing Do\n", 50)));
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC " + name + " Do EMC"));
        }));
        var unresolved = report.Findings.Where(f => f.Location.EndsWith("/Resources/XObject/Missing")).ToList();
        Assert.Equal(50, unresolved.Count);
        Assert.All(unresolved, f => Assert.Equal("/Resources/XObject" + name.Substring(0, 128) + ".../Resources/XObject/Missing", f.Location));
    }

    [Fact]
    public void Deeply_nested_form_locations_keep_their_start_and_end_within_the_limit()
    {
        var report = Scan(Fixture(d =>
        {
            var resources = d.Pages[0].Elements.GetDictionary("/Resources")!;
            for (var i = 0; i < 12; i++)
            {
                var form = AccessibilityFormFixture.AddForm(d, resources, "/" + new string('B', 120) + i, i < 11 ? "/" + new string('B', 120) + (i + 1) + " Do" : "/Missing Do");
                resources = form.Elements.GetDictionary("/Resources")!;
            }
            d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC /" + new string('B', 120) + "0 Do EMC"));
        }));
        var finding = Assert.Single(report.Findings, f => f.Location.EndsWith("/Resources/XObject/Missing"));
        Assert.True(finding.Location.Length <= 1024, finding.Location.Length.ToString());
        Assert.StartsWith("/Resources/XObject/" + new string('B', 120) + "0/Resources", finding.Location);
        Assert.Contains("/...", finding.Location);
        Assert.Contains("B11/Resources/XObject/Missing", finding.Location);
    }

    [Fact]
    public void A_chain_of_type0_fonts_fails_once_and_is_not_followed()
    {
        var report = Scan(Fixture(d =>
        {
            PdfDictionary descendant = new PdfDictionary(d);
            descendant.Elements.SetName("/Type", "/Font");
            descendant.Elements.SetName("/Subtype", "/CIDFontType2");
            d.Internals.AddObject(descendant);
            for (var i = 0; i < 3; i++)
            {
                var type0 = new PdfDictionary(d);
                type0.Elements.SetName("/Type", "/Font");
                type0.Elements.SetName("/Subtype", "/Type0");
                var kids = new PdfArray(d);
                kids.Elements.Add(descendant.Reference!);
                type0.Elements["/DescendantFonts"] = kids;
                d.Internals.AddObject(type0);
                descendant = type0;
            }
            var resources = d.Pages[0].Elements.GetDictionary("/Resources")!;
            var fonts = new PdfDictionary(d);
            fonts.Elements["/Chain"] = descendant.Reference!;
            resources.Elements["/Font"] = fonts;
            d.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC BT /Chain 10 Tf (x) Tj ET EMC"));
        }));
        var font = Assert.Single(report.Findings, f => f.RuleId == "FPDF-A11Y-FONT");
        Assert.Equal(PdfAccessibilityCheckStatus.Failed, font.Status);
        Assert.Equal("/Resources/Font/Chain", font.Location);
    }

    [Fact]
    public void Form_invocations_share_the_document_depth_and_decoded_byte_limits()
    {
        var bytes = Fixture(d =>
        {
            var page = d.Pages[0];
            var resources = page.Elements.GetDictionary("/Resources")!;
            for (var i = 0; i < 8; i++)
            {
                var form = AccessibilityFormFixture.AddForm(d, resources, "/F", i < 7 ? "/F Do" : new string(' ', 10000));
                resources = form.Elements.GetDictionary("/Resources")!;
            }
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Artifact BMC /F Do EMC"));
        });
        Assert.Equal(PdfAccessibilityScanStatus.NeedsReview, Scan(bytes).Status);
        Assert.Contains(Scan(bytes, new PdfAccessibilityScanOptions { MaxDepth = 6 }).Findings,
            f => f.RuleId == "FPDF-A11Y-READ" && f.Message.Contains("Form XObject nesting"));
        Assert.Contains(Scan(bytes, new PdfAccessibilityScanOptions { MaxDecodedBytes = 8000 }).Findings,
            f => f.RuleId == "FPDF-A11Y-READ" && f.Message.Contains("MaxDecodedBytes"));
    }
}
