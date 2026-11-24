// Original small oracle/CLI fixtures, independent of the large generated corpus.
using System.Text;
using FreePDF.Lab.Fixtures;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.UniversalAccessibility;

static class AccessibilityFixtures
{
    internal static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        using var buffer = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            pdf.Info.Title = "Synthetic accessibility fixture";
            var tags = UAManager.ForDocument(pdf).StructureBuilder;
            using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            {
                tags.BeginElement(PdfIllustrationElementTag.Figure, "<script>alert('untrusted PDF text')</script>", new XRect(40, 40, 100, 60));
                gfx.DrawRectangle(XBrushes.Red, 40, 40, 100, 60);
                tags.End();
            }
            pdf.Save(buffer);
        }
        var original = buffer.ToArray();
        WriteNew("valid.pdf", original);
        foreach (var mutation in new[] { "missing-alt", "missing-language", "untagged", "incomplete-missing-language", "nested-form", "reused-tagged-form", "untagged-form-paint" })
        {
            using var pdf = PdfReader.Open(new MemoryStream(original), PdfDocumentOpenMode.Modify);
            if (mutation == "missing-language") pdf.Catalog.Elements.Remove("/Lang");
            else if (mutation == "untagged") pdf.Catalog.Elements.Remove("/StructTreeRoot");
            else if (mutation == "incomplete-missing-language")
            {
                // A confirmed failure plus an inline property dictionary outside native coverage.
                pdf.Catalog.Elements.Remove("/Lang");
                pdf.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Span <</Lang (en) /ActualText (sample)>> BDC EMC"));
            }
            else if (mutation is "nested-form" or "reused-tagged-form" or "untagged-form-paint")
            {
                var inner = AccessibilityFormFixture.AddTaggedForm(pdf);
                if (mutation == "reused-tagged-form")
                    pdf.Pages[0].Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes("/Outer Do"));
                if (mutation == "untagged-form-paint")
                    inner.Stream!.Value = inner.Stream.Value.Concat(Encoding.ASCII.GetBytes(" 2 2 5 5 re f")).ToArray();
            }
            else
            {
                static PdfDictionary Child(PdfDictionary d) => d.Elements.GetDictionary("/K") ?? d.Elements.GetArray("/K")!.Elements.GetDictionary(0)!;
                Child(Child(pdf.Catalog.Elements.GetDictionary("/StructTreeRoot")!)).Elements.Remove("/Alt");
            }
            using var bytes = new MemoryStream(); pdf.Save(bytes); WriteNew(mutation + ".pdf", bytes.ToArray());
        }
        WriteUnicodeFixtures(directory);
        WriteGlyphFixtures(directory);
        File.WriteAllText(Path.Combine(directory, "README.txt"), "Synthetic machine-check fixtures, not certified accessible documents. The valid fixture deliberately includes HTML-like alternative text to test report escaping; its meaning requires human review.\n");
        void WriteNew(string name, byte[] data)
        {
            using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew);
            output.Write(data);
        }
    }

    // A tagged paragraph with the two letters AB in one font.
    static MemoryStream Original(PdfFontEncoding fontEncoding)
    {
        var original = new MemoryStream();
        using (var pdf = new PdfDocument())
        {
            pdf.Info.Title = "Synthetic mapped text";
            var tags = UAManager.ForDocument(pdf).StructureBuilder;
            using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            {
                tags.BeginElement(PdfBlockLevelElementTag.Paragraph);
                gfx.DrawString("AB", new XFont("Corpus Sans", 14, XFontStyleEx.Regular,
                    new XPdfFontOptions(fontEncoding)), XBrushes.Black, 40, 60);
                tags.End();
            }
            pdf.Save(original, closeStream: false);
        }
        return original;
    }

    static PdfDictionary OnlyFont(PdfDocument document)
    {
        var fonts = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        return fonts.Elements.GetDictionary(fonts.Elements.Keys.Single())!;
    }

    // Rewrites the ToUnicode entry of B, the second letter.
    static void EditEntryOfB(PdfDictionary font, Func<string, string?> edit)
    {
        var cmap = font.Elements.GetDictionary("/ToUnicode")!;
        var lines = Encoding.ASCII.GetString(cmap.Stream!.UnfilteredValue).Split('\n')
            .Select(line => line.TrimEnd().EndsWith("<0042>", StringComparison.Ordinal) ? edit(line.TrimEnd()) : line)
            .OfType<string>();
        cmap.Elements.Remove("/Filter");
        cmap.Stream.Value = Encoding.ASCII.GetBytes(string.Join("\n", lines));
    }

    static void WriteUnicodeFixtures(string directory)
    {
        using var original = Original(PdfFontEncoding.Unicode);
        foreach (var name in new[] { "unicode-mapped", "unicode-missing", "unicode-custom-encoding", "unicode-forbidden" })
        {
            original.Position = 0;
            using var document = PdfReader.Open(original, PdfDocumentOpenMode.Modify);
            var font = OnlyFont(document);
            if (name == "unicode-forbidden")
            {
                // B maps to U+FEFF, a value ISO 14289-1 7.21.7 forbids. A is unchanged.
                EditEntryOfB(font, line => line[..^"<0042>".Length] + "<FEFF>");
            }
            if (name == "unicode-missing")
            {
                // Keep A mapped, remove only B's mapping. Both glyphs still draw.
                var cmap = font.Elements.GetDictionary("/ToUnicode")!;
                var lines = Encoding.ASCII.GetString(cmap.Stream!.UnfilteredValue).Split('\n')
                    .Where(line => !line.TrimEnd().EndsWith("<0042>", StringComparison.Ordinal))
                    .Select(line => line.TrimEnd() == "2 beginbfrange" ? "1 beginbfrange" : line);
                cmap.Elements.Remove("/Filter");
                cmap.Stream.Value = Encoding.ASCII.GetBytes(string.Join("\n", lines));
            }
            if (name == "unicode-custom-encoding")
            {
                // Equivalent to Identity-H for the two used glyphs, expressed as an embedded encoding CMap.
                var child = font.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
                var encoding = new PdfDictionary(document);
                encoding.Elements.SetName("/Type", "/CMap");
                encoding.Elements.SetName("/CMapName", "/SyntheticIdentity");
                encoding.Elements["/CIDSystemInfo"] = child.Elements.GetDictionary("/CIDSystemInfo")!.Clone();
                encoding.CreateStream(Encoding.ASCII.GetBytes(
                    "/CIDInit /ProcSet findresource begin 12 dict begin begincmap " +
                    "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> def " +
                    "/CMapName /SyntheticIdentity def /CMapType 1 def " +
                    "1 begincodespacerange <0000> <FFFF> endcodespacerange " +
                    "1 begincidrange <0000> <FFFF> 0 endcidrange " +
                    "endcmap CMapName currentdict /CMap defineresource pop end end"));
                document.Internals.AddObject(encoding);
                font.Elements["/Encoding"] = encoding.Reference!;
            }
            using var file = new FileStream(Path.Combine(directory, name + ".pdf"), FileMode.CreateNew);
            document.Save(file);
        }

        using var simple = Original(PdfFontEncoding.WinAnsi);
        foreach (var (name, glyph) in new[] { ("unicode-glyph-name", "/B"), ("unicode-glyph-name-missing", "/g123") })
        {
            simple.Position = 0;
            using var document = PdfReader.Open(simple, PdfDocumentOpenMode.Modify);
            var font = OnlyFont(document);
            // B's code takes its glyph name from /Differences, and no ToUnicode map supplies it. A keeps WinAnsi.
            var encoding = new PdfDictionary(document);
            encoding.Elements.SetName("/Type", "/Encoding");
            encoding.Elements.SetName("/BaseEncoding", "/WinAnsiEncoding");
            encoding.Elements["/Differences"] = new PdfArray(document, new PdfInteger(66), new PdfName(glyph));
            font.Elements["/Encoding"] = encoding;
            font.Elements.Remove("/ToUnicode");
            using var file = new FileStream(Path.Combine(directory, name + ".pdf"), FileMode.CreateNew);
            document.Save(file);
        }
    }

    // The Identity-H paragraph, plus one artifact that shows a code which selects .notdef or a glyph the program lacks.
    // The descendant uses CIDToGIDMap Identity, so a code is a glyph index.
    static void WriteGlyphFixtures(string directory)
    {
        using var original = Original(PdfFontEncoding.Unicode);
        foreach (var (name, code) in new[] { ("glyph-notdef", "0000"), ("glyph-missing", "FFF0"), ("glyph-map-notdef", "0024") })
        {
            original.Position = 0;
            using var document = PdfReader.Open(original, PdfDocumentOpenMode.Modify);
            var fonts = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            var key = fonts.Elements.Keys.Single();
            if (name == "glyph-map-notdef")
            {
                // A CIDToGIDMap stream that maps every CID up to 0x2F, the one shown included, to glyph 0.
                var child = fonts.Elements.GetDictionary(key)!.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
                var map = new PdfDictionary(document);
                map.CreateStream(new byte[0x30 * 2]);
                document.Internals.AddObject(map);
                child.Elements["/CIDToGIDMap"] = map.Reference!;
            }
            document.Pages[0].Contents.AppendContent().CreateStream(
                Encoding.ASCII.GetBytes($"/Artifact BMC BT {key} 14 Tf 40 100 Td <{code}> Tj ET EMC"));
            using var file = new FileStream(Path.Combine(directory, name + ".pdf"), FileMode.CreateNew);
            document.Save(file);
        }

        // A second font with a synthetic Type 1 or CFF program, shown in an artifact: glyphs A and B (CIDs 1, 5 and 7).
        foreach (var (name, show) in new[] { ("glyph-type1-missing", "42"), ("glyph-type1c-notdef", "01"),
                     ("glyph-cff-cid-missing", "0003"), ("glyph-type1c-builtin-notdef", "80") })
        {
            original.Position = 0;
            using var document = PdfReader.Open(original, PdfDocumentOpenMode.Modify);
            var fonts = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            // CID 3 is not in the CID-keyed charset; B's code is renamed to a glyph the Type 1 program lacks; WinAnsi
            // names no glyph at 01, and the CFF built-in Standard Encoding names none at 80.
            var (key, bytes, subtype) = name switch
            {
                "glyph-cff-cid-missing" => ("/FontFile3", SyntheticCff.Cid(new[] { 1, 5, 7 }), "/CIDFontType0C"),
                "glyph-type1-missing" => ("/FontFile", SyntheticType1.Build(new[] { "A", "B" }), null),
                _ => ("/FontFile3", SyntheticCff.Named(new[] { "#34", "#35" }), "/Type1C"),
            };
            var program = new PdfDictionary(document);
            program.CreateStream(bytes);
            if (subtype != null) program.Elements.SetName("/Subtype", subtype);
            document.Internals.AddObject(program);
            var descriptor = new PdfDictionary(document);
            descriptor.Elements[key] = program.Reference!;
            descriptor.Elements.SetName("/Type", "/FontDescriptor");
            descriptor.Elements.SetName("/FontName", "/Synthetic");
            descriptor.Elements.SetInteger("/Flags", 32);
            descriptor.Elements["/FontBBox"] = new PdfArray(document, new PdfInteger(0), new PdfInteger(0), new PdfInteger(1000), new PdfInteger(1000));
            descriptor.Elements.SetInteger("/ItalicAngle", 0);
            descriptor.Elements.SetInteger("/Ascent", 800);
            descriptor.Elements.SetInteger("/Descent", -200);
            descriptor.Elements.SetInteger("/CapHeight", 700);
            descriptor.Elements.SetInteger("/StemV", 80);
            var font = new PdfDictionary(document);
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/BaseFont", "/Synthetic");
            if (name == "glyph-cff-cid-missing")
            {
                var info = new PdfDictionary(document);
                info.Elements.SetString("/Registry", "Adobe");
                info.Elements.SetString("/Ordering", "Identity");
                info.Elements.SetInteger("/Supplement", 0);
                var child = new PdfDictionary(document);
                child.Elements.SetName("/Type", "/Font");
                child.Elements.SetName("/Subtype", "/CIDFontType0");
                child.Elements.SetName("/BaseFont", "/Synthetic");
                child.Elements["/CIDSystemInfo"] = info;
                child.Elements["/FontDescriptor"] = descriptor;
                font.Elements.SetName("/Subtype", "/Type0");
                font.Elements.SetName("/Encoding", "/Identity-H");
                font.Elements["/DescendantFonts"] = new PdfArray(document, child);
            }
            else
            {
                font.Elements.SetName("/Subtype", "/Type1");
                font.Elements.SetInteger("/FirstChar", 0);
                font.Elements.SetInteger("/LastChar", 255);
                font.Elements["/Widths"] = new PdfArray(document, Enumerable.Repeat<PdfItem>(new PdfInteger(500), 256).ToArray());
                font.Elements["/FontDescriptor"] = descriptor;
                if (name == "glyph-type1-missing")
                {
                    var encoding = new PdfDictionary(document);
                    encoding.Elements.SetName("/BaseEncoding", "/WinAnsiEncoding");
                    encoding.Elements["/Differences"] = new PdfArray(document, new PdfInteger(66), new PdfName("/uniE000"));
                    font.Elements["/Encoding"] = encoding;
                }
                if (name == "glyph-type1c-notdef") font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            }
            fonts.Elements["/Synthetic"] = font;
            document.Pages[0].Contents.AppendContent().CreateStream(
                Encoding.ASCII.GetBytes($"/Artifact BMC BT /Synthetic 14 Tf 40 100 Td <{show}> Tj ET EMC"));
            using var file = new FileStream(Path.Combine(directory, name + ".pdf"), FileMode.CreateNew);
            document.Save(file);
        }
    }
}
