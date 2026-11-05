# FreePDF 7.7.0: gaps, ranked by need

Checked 2026-11-05. Evidence and source keys are in `feature-comparison-2026-11-05.md` in this folder.

**How the ranking works.** Gaps are ranked by how much a typical .NET developer choosing a
*free* PDF library would need the feature. Typical uses: generating invoices and reports,
merging, filling and signing forms, archiving, and reading PDFs that come in. The signals:
- NuGet download totals for libraries that do only that job [x:nuget].
- The 91 upstream PDFsharp issue titles in `docs/research/pdfsharp-issues-2026-10-30.json`.
- What empira chose to sell as separate PsX add-ons [ps:OFF], which marks proven paid demand.

Effort and differentiation are shown separately and do not affect the order. A suggested
sequence that weighs all three is at the end.

**Effort** is a rough size for FreePDF's maintainers with the current codebase:
- **small:** days to two weeks.
- **medium:** weeks.
- **large:** months, or an ongoing commitment.

## Ranked gaps

### 1. Text extraction (plain text, then words with positions)
- **Need:** very high. PdfPig, which mostly extracts text, has 42.4M downloads [x:nuget].
  - Uses: search indexing, data capture, and unit tests that check generated PDFs.
  - Upstream asks for it ("how to extract data from this kind of pdf?").
  - empira sells it as the PsX Text Extractor add-on [ps:OFF].
  - Today FreePDF has only a low-level content-stream parser [fp:src-content].
- **Who has it:**
  - iText: yes, positions but no layout analysis [it:textx].
  - PdfPig: yes, words and reading order [pp:DLA].
  - Syncfusion [sf:textx] and Aspose [as:textx]: yes.
  - PDFsharp: paid add-on only.
  - QuestPDF, pdf-lib, pikepdf: no.
- **Effort:**
  - medium for plain text and glyph positions. It needs ToUnicode CMaps, simple-font
    encodings and Differences, CID fonts and Type 3, on top of the existing CParser and
    font code.
  - large for reading order, columns and table-aware layout.
- **Concerns:** none on licence; it can be pure managed code. Differentiation is moderate,
  because PdfPig (Apache-2.0) already covers it for free and works alongside FreePDF. The
  value is one library and the same object model for read, edit and write. It is also the
  foundation for redaction (#15) and find-and-replace.

### 2. HTML/CSS to PDF
- **Need:** very high, the largest download signal of any single job.
  - DinkToPdf 32.7M, IronPdf 21.9M, iText pdfHTML 26.8M (two package IDs),
    Select.HtmlToPdf 9.8M, WkHtmlToPdf wrappers 7.4M, HtmlRenderer.PdfSharp 7.4M,
    NReco 7.0M [x:nuget].
  - PDFsharp's own FAQ points users to HtmlRenderer [ps:FAQ].
- **Who has it:**
  - iText: AGPL add-on pdfHTML [it:pdfhtml].
  - Syncfusion: bundles Blink [sf:html].
  - Aspose: own engine [as:html].
  - Nobody else in the matrix.
- **Effort:**
  - small to medium: publish a FreePDF build of **HtmlRenderer.PdfSharp** (BSD-3-Clause,
    HTML 4.01 and CSS 2, fully managed) [x:htmlrenderer]. As published it depends on the
    `PDFsharp` ≥ 6.2.4 package ID, so a FreePDF app can't use it without pulling in a second
    implementation with the same assembly names.
  - large for modern CSS (flexbox, grid).
- **Concerns:** a browser-grade engine needs Chromium-size native dependencies. Keep it out
  of core: at most a separate package, or documented integration. HtmlRenderer also depends
  on System.Drawing.Common (Windows-only on modern .NET), so Linux support must be checked
  before promising anything.

### 3. Creating fillable form fields
- **Need:** high. MigraDoc and XGraphics users want to produce fillable forms (applications,
  contracts), not only fill other people's.
  - FreePDF fills and flattens but cannot create a text box or check box: the field
    constructors are internal [fp:src-forms].
  - empira sells this as PsX Forms [ps:OFF].
- **Who has it:** iText [it:forms], Syncfusion [sf:forms], Aspose [as:createform], pdf-lib
  [pl:FORM]. Not QuestPDF, PdfPig, pikepdf or upstream core.
- **Effort:** medium. The form model, default appearances and appearance drawing for filling
  already exist. Needed:
  - public constructors and a `PdfForm.Add…` API
  - widget placement on a page
  - on/off appearances for check boxes and radio buttons
  - a MigraDoc element later
- **Concerns:** none. This builds directly on FreePDF's strongest recent work (fill, flatten,
  merge forms, sign fields). With it, FreePDF would be the only MIT .NET library with the
  full form cycle.

### 4. Rendering pages to images (thumbnails, previews)
- **Need:** high. PDFtoImage 10.3M, PDFium native packages about 12M each,
  Syncfusion.Pdf.Imaging 10.8M [x:nuget]. FreePDF can't rasterise an existing PDF.
- **Who has it:**
  - Syncfusion: PDFium [sf:render].
  - Aspose [as:toimage].
  - QuestPDF: own documents only [qp:OUT].
  - iText: has dropped it [it:render].
  - Nobody else.
- **Effort:**
  - large to very large for a managed renderer: font rasterising, shadings, blend modes,
    and JBIG2/JPX/CCITT codecs.
  - small for an integration sample or adapter with PDFtoImage (MIT, PDFium plus SkiaSharp)
    [x:pdftoimage].
- **Concerns:** native PDFium binaries for each platform. Recommend documented integration,
  not a core feature. Even QuestPDF and Syncfusion use native engines.

### 5. QR codes and common 1D barcodes
- **Need:** high for invoices and labels.
  - EPC/GiroCode payment QR codes, Swiss QR-bill, shipping labels, Code 128 and EAN-13.
  - FreePDF has Code 39, 2 of 5, DataMatrix and OMR only, and MigraDoc has no barcode
    renderer [fp:src-bc].
  - ZXing.Net alone has 55.6M downloads [x:nuget].
  - The usual workaround is a PNG from another library, which loses vector sharpness.
- **Who has it:** iText [it:barcodes], Syncfusion [sf:barcode], pyHanko (QR) [ph:hAPI].
  QuestPDF and Aspose use third-party or separate products.
- **Effort:** small to medium: a QR encoder, Code 128, EAN-13/UPC, and a MigraDoc
  `Barcode` renderer.
- **Concerns:** to port rather than write fresh, take MIT code (e.g. QRCoder) or Apache-2.0
  code (ZXing.Net) and keep the attribution. No dependency needed. Best need-to-effort ratio
  in this list.

### 6. Opening damaged PDFs (full cross-reference rebuild)
- **Need:** high in practice. "Cannot open", "unexpected token" and xref errors are the
  largest group in the upstream issue list. FreePDF fixes misnumbered entries but doesn't
  rebuild a missing or broken xref by scanning for objects [fp:src-parser].
- **Who has it:** iText [it:reader], PdfPig (brute force) [pp:RM], qpdf [qk:qD],
  Syncfusion (basic) [sf:open], Aspose [as:features].
- **Effort:** medium. Scan for `n g obj` and trailers, rebuild the table, then fuzz with the
  existing mutation corpus.
- **Concerns:** Append mode must keep refusing repaired files, as it does today
  [fp:migr-signed]. A repaired file can only be saved as a new file.

### 7. Text beyond WinAnsi in forms, and more font formats
- **Need:** medium to high in Central and Eastern Europe, Greece and Asia.
  - Filled and flattened form text is limited to WinAnsi (CP1252), so Polish, Czech,
    Cyrillic, Greek and CJK values are left to the viewer, or refused under PDF/A
    [fp:migr-forms].
  - CFF-flavoured `.otf` fonts and `.ttc` collections are skipped [fp:migr-fonts], and
    Noto CJK and many system fonts come in exactly those formats.
  - Upstream asks for IVS and Japanese.
- **Who has it:** iText (CFF) [it:cff], Syncfusion [sf:text], Aspose [as:addtext],
  pyHanko (CID OTF) [ph:hU].
- **Effort:** medium for each part.
  - Forms: write a Type 0/Identity-H font into `/DR` from the font resolver, and build
    appearances with it.
  - CFF: embed as FontFile3. Subsetting CFF is the hard part; embedding the whole font first
    is acceptable.
  - TTC: extract one face, which is small.
- **Concerns:** none on licence.

### 8. PAdES signatures, document timestamps and LTV
- **Need:** medium overall. High for EU eIDAS workflows and long-term archiving, where
  validators expect PAdES baseline profiles.
- **Today in FreePDF:**
  - It signs with `adbe.pkcs7.detached` [fp:src-sig], not `ETSI.CAdES.detached` with a
    signing-certificate-v2 attribute, so it isn't PAdES B-B.
  - RFC 3161 signature timestamps exist in code for .NET 8+, but its release checks don't
    cover them [fp:src-signer] [fp:support].
  - There are no document timestamps.
  - DSS is marked "nyi" [fp:src-catalog].
- **Who has it:** iText (B-B to B-LTA) [it:pades], pyHanko [ph:hS], Syncfusion [sf:sign],
  Aspose (partial, a UseLtv flag) [as:useltv]. No other free .NET library.
- **Effort:**
  - small to medium for B-B and B-T: a SubFilter option, signed attributes, document
    timestamps, and tests for the existing TSA path.
  - medium for B-LT and B-LTA: fetch OCSP/CRL, write DSS and VRI in an incremental update,
    then an archive timestamp.
- **Concerns:**
  - .NET has no OCSP client, so either write a small one on System.Formats.Asn1 or depend
    on BouncyCastle (MIT).
  - Strategically the strongest differentiator: FreePDF's Append mode and DocMDP/FieldMDP
    enforcement already do the hard part. With PAdES and LTV it would, as far as public docs
    show, be the only MIT .NET library that can do B-LTA. Aspose.PDF FOSS claims signing and
    timestamps, but not LTV.

### 9. Smaller output files (object streams, deduplication after merges, image recompression)
- **Need:** medium.
  - A full save always writes a classic xref table with no object streams [fp:src-writer].
  - Merging many files keeps one copy of resources per source.
  - Upstream asks for "Optimising merged PDFs to reduce file size through duplicated
    resources".
- **Who has it:**
  - Object streams: iText [it:writer], Aspose [as:compressobjects], pdf-lib (default)
    [pl:SRC], qpdf [qk:qC].
  - Full optimisation: Syncfusion [sf:compress], Aspose [as:optimize].
- **Effort:**
  - small to medium for object and xref streams on write (watch encryption and PDF/A-1,
    which forbids them).
  - medium for content-hash deduplication of fonts, images and streams across merged sources.
  - medium to large for image downsampling.
- **Concerns:** image recompression needs JPEG encoding beyond what FreePDF ships. Keep that
  optional or out of core.

### 10. Signature verification
- **Need:** medium. Apps that receive signed PDFs (approval flows, inbound contracts) need
  "is this still valid, and who signed it?". FreePDF can preserve signatures and enforce
  their permissions, but can't report on them [fp:support].
- **Who has it:** iText [it:validation], pyHanko [ph:hV], Syncfusion [sf:sign],
  Aspose [as:sign].
- **Effort:**
  - medium for integrity and byte-range coverage, a CMS check (`SignedCms.CheckSignature`),
    chain building with X509Chain, and an "unauthorised changes after signing" report. Much
    of the last part already exists in the DocMDP/FieldMDP logic.
  - large for full AdES validation with trusted lists and LTV.
- **Concerns:** be careful about claims. Report the facts, and don't present a "legally
  valid" verdict.

### 11. SVG images (and GIF/TIFF in the portable build)
- **Need:** medium. Logos and icons increasingly arrive as SVG, and scanned documents as
  multi-page TIFF. The portable build reads JPEG, PNG and BMP only [fp:src-img].
- **Who has it:**
  - SVG: iText (svg module) [it:imgtype], QuestPDF [qp:IMG], Syncfusion [sf:img2pdf],
    Aspose [as:img2pdf].
  - TIFF/GIF: iText, Syncfusion, Aspose.
- **Effort:**
  - medium to large for an SVG subset (paths, shapes, gradients, transforms; text later)
    drawn through XGraphics.
  - medium for TIFF (CCITT G4 can be passed straight through) and GIF.
- **Concerns:** none if it's pure managed code. A full SVG renderer such as Svg.Skia means
  native Skia.

### 12. Complex scripts and right-to-left text (Arabic, Hebrew, Indic)
- **Need:** medium globally, low among current PDFsharp users.
  - QuestPDF advertises it as a headline feature [qp:GH].
  - iText charges extra for it (pdfCalligraph, commercial only) [it:calligraph], so a free
    implementation would stand out.
  - FreePDF doesn't promise shaping [fp:support].
- **Who has it:** QuestPDF, Syncfusion [sf:text], iText (paid add-on), pyHanko (stamps only)
  [ph:hFo].
- **Effort:** large: shaping, bidi and line breaking in MigraDoc and XGraphics.
- **Concerns:**
  - HarfBuzz via HarfBuzzSharp is MIT-compatible but native.
  - A managed bidi algorithm alone is medium effort and helps Hebrew; it isn't enough for
    Arabic or Indic.

### 13. Certification signatures (creating DocMDP)
- **Need:** medium-low. FreePDF enforces certification signatures made by others but can't
  certify a document itself: there's no certification level in `DigitalSignatureOptions`
  [fp:src-sigopts].
- **Who has it:** iText [it:signerprops], Syncfusion [sf:sign], Aspose [as:sign],
  pyHanko [ph:hL].
- **Effort:** small: `/Reference` with a DocMDP transform plus `/Perms`, and tests against
  pyHanko like the existing ones.
- **Concerns:** none. Cheap, and it completes the signing story.

### 14. Page labels and optional content (layers)
- **Need:** low to medium.
  - Page labels ("i, ii, 1, 2…") matter for books and reports.
  - Layers matter for CAD and print workflows.
  - FreePDF has neither API, and merging drops both [fp:support].
- **Who has it:**
  - iText [it:page] [it:layer] and Syncfusion [sf:pages] [sf:layers]: both.
  - Aspose: labels [as:numbering].
  - qpdf: labels [qk:qC].
- **Effort:** small for page labels, including carrying them through `AppendPages` and
  `ExtractPages`. Small to medium for an OCG API and carrying layers through merges.
- **Concerns:** none.

### 15. Redaction that really removes content
- **Need:** medium (GDPR requests, legal discovery). There's only a redact-annotation
  dictionary today [fp:src-redact].
- **Who has it:** iText (AGPL add-on pdfSweep) [it:sweep], Syncfusion [sf:redact],
  Aspose [as:extraannot].
- **Effort:** large. It depends on #1 (glyph positions) and needs:
  - rewriting text, image and path content
  - clipping images
  - removing annotations, form values and metadata under the area
- **Concerns:** this is security-sensitive, because a half-working redaction leaks data. Ship
  it only with an adversarial test suite.

### 16. Wider PDF/A: more validated levels, conversion, validation
- **Need:** medium-low.
- **Today in FreePDF:**
  - It validates PDF/A-1b, 2b and 3b.
  - It lets callers declare 1a to 4f without checking them [fp:support] [fp:src-pdfa].
  - It can't convert an existing PDF to PDF/A or check one.
- **Who has it:**
  - Conversion: Syncfusion [sf:conformance], Aspose [as:pdfa].
  - Validation: Aspose; pikepdf, partly [qk:pA].
  - Neither: iText [it:pdfua].
- **Effort:**
  - small to medium to validate 2u/3u output, 2a/3a combined with the tagged MigraDoc
    renderer, and PDF/A-4. Or refuse levels that aren't validated.
  - large for conversion (colour spaces, fonts, transparency).
  - large for validation (veraPDF is the reference, in Java).
- **Concerns:** declaring a level that isn't checked risks non-conforming archives. At least
  document it.

### 17. Accessibility beyond MigraDoc's PDF/UA-1
- **Need:** medium-low but rising: the European Accessibility Act has applied since June 2025.
- **Today in FreePDF:**
  - Only MigraDoc output is tagged and validated.
  - XGraphics documents aren't tagged [fp:support].
  - There's no PDF/UA-2.
  - Merging drops the structure tree [fp:migr-merge].
- **Who has it:** iText (UA-1 and UA-2) [it:92], Syncfusion (UA-2) [sf:tagged], Aspose (UA-1)
  [as:tagged], QuestPDF (UA-1) [qp:ACC].
- **Effort:** medium for a supported XGraphics tagging API (the inherited StructureBuilder is
  the starting point). Medium to large for UA-2 and for merging tagged documents.
- **Concerns:** none on licence.

### 18. MigraDoc layout conveniences: hyphenation, multi-column sections, automatic TOC
- **Need:** low to medium. Upstream asks for Word-style columns and a grid layout. There's no
  automatic hyphenation [fp:src-grep], and a TOC has to be built by hand from PageRef fields
  [fp:src-fields].
- **Who has it:**
  - Hyphenation: iText [it:hyph].
  - Grid/columns: QuestPDF.
  - Automatic TOC: Aspose [as:manipulate].
- **Effort:** medium for hyphenation (Liang's algorithm). Medium to large for multi-column
  sections in the MigraDoc formatter.
- **Concerns:** hyphenation pattern licences vary by language (MIT, LPPL, others). Ship only
  patterns with a clear MIT-compatible licence, or load them from user files.

### 19. FDF/XFDF form data import and export
- **Need:** low.
- **Who has it:** Aspose (FDF, XFDF, JSON) [as:importexport], iText (XFDF) [it:xfdf],
  Syncfusion (FDF) [sf:forms].
- **Effort:** small, given the field model. JSON import/export is the more useful variant
  for .NET apps.
- **Concerns:** none.

### 20. Public-key (certificate) encryption
- **Need:** low.
- **Who has it:** iText [it:writer], Aspose [as:certenc], pyHanko [ph:hE].
- **Effort:** medium: a PubSec handler with CMS enveloped data, for both reading and writing.
- **Concerns:** Append mode currently supports only the standard handler [fp:support].

### 21. Linearization ("fast web view")
- **Need:** low. Browsers and pdf.js use HTTP range requests whether or not a file is
  linearized.
- **Who has it:** qpdf [qk:qC], QuestPDF (via qpdf) [qp:DO], Syncfusion [sf:document],
  Aspose [as:optimize]. Not iText.
- **Effort:** medium. Hint tables are fiddly, and incremental updates undo linearization.
- **Concerns:** none. Low value.

### 22. XFA forms
- **Need:** low and falling. XFA is deprecated in PDF 2.0, but government forms still use it
  (upstream has two XFA issues).
- **Who has it:** Syncfusion [sf:xfa], Aspose (partial) [as:xfa], iText (partial; paid
  flattening) [it:xfa].
- **Effort:**
  - large for real XFA.
  - small to remove the XFA package on request, so that the AcroForm half of a hybrid form
    can be filled reliably. pdf-lib does this silently [pl:FORM]; FreePDF should do it only
    when asked.
- **Concerns:** none.

## Out of scope (recommend integration, not implementation)

| Gap | Who has it | Why not in FreePDF |
|---|---|---|
| OCR | iText pdfOCR [it:ocr], Syncfusion [sf:ocr] | Tesseract or ONNX native runtimes. Better as a sample pipeline: OCR text, then an invisible text layer drawn with XGraphics |
| Office to PDF | Syncfusion [sf:conv] | A separate document-model product. iText has even dropped pdfOffice [it:office] |
| PDF to Word/HTML | Syncfusion [sf:pdf2word], Aspose [as:pdf2word] | Needs #1 plus heavy layout reconstruction; low value for a creation library |
| Table extraction | Syncfusion [sf:table], Aspose [as:tablex] | Builds on #1; iText dropped pdf2Data [it:pdf2data]. Revisit only after #1 |
| Printing arbitrary PDFs | none cross-platform | Needs a renderer (#4); MigraDoc printing on Windows already exists |

## Suggested sequence (need × effort × differentiation)

1. **QR and Code 128/EAN barcodes, plus the MigraDoc barcode renderer** (#5): small effort,
   common need, invoice-adjacent like Factur-X.
2. **Creating form fields** (#3) and **certification signatures** (#13): medium plus small.
   Together they complete a form and signing cycle no other MIT .NET library offers.
3. **PAdES B-B/B-T with document timestamps, then B-LT/B-LTA** (#8), then **verification**
   (#10): a unique free alternative to iText's AGPL signing stack.
4. **Text extraction with positions** (#1): the biggest raw need, and the foundation for
   redaction. Differentiation is lower because PdfPig exists.
5. **Damaged-file repair** (#6), **non-WinAnsi forms and CFF/TTC fonts** (#7), and **object
   streams and dedup** (#9): robustness and quality work that every user feels.
6. **Integration packages or samples instead of engines:** a FreePDF build of HtmlRenderer
   (#2) and PDFtoImage rendering (#4).

## Competitive notes that affect priorities

- **QuestPDF is no longer MIT.** Its Community License v3.0 (6 Jul 2026) is "source-available
  commercial": free only under USD 1M revenue, and public-sector bodies and listed companies
  are excluded [qp:LIC]. That makes FreePDF the main MIT code-first layout library for those
  users. QuestPDF still has no forms and no signatures [qp:I146].
- **PDFsharp 6.5 is AGPL or €750 commercial** [ps:PRI]. empira sells forms, Factur-X and text
  extraction as PsX add-ons [ps:OFF]. FreePDF already gives away forms and Factur-X; text
  extraction is the remaining PsX feature it lacks.
- **Aspose.PDF FOSS (MIT, monthly since June 2026)** claims a very broad feature set,
  including extraction, rendering, form creation, signing and verification [as:foss]. It is
  vendor-backed, but published as source drops with an unstable API. It's worth tracking,
  because it is the broadest other MIT .NET library in this space. Pdfxport.Net, another
  MIT continuation of PDFsharp, is the other one; see `docs/research/7.0-roadmap.md`.
- **iText has removed features:** rendering, Office conversion and table extraction now
  redirect to Apryse [it:render] [it:office] [it:pdf2data]. Complex scripts are commercial
  only even for AGPL users [it:calligraph].
