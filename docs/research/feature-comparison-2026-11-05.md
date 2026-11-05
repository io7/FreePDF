# FreePDF 7.7.0 compared with other PDF libraries

Checked 2026-11-05. The FreePDF column comes from its README, CHANGELOG, docs and source
(this repository at 7.7.0). Every other column comes from the vendor's
public documentation, feature pages, API reference, changelogs or GitHub READMEs. PDFsharp 6.5
is described from its public docs and changelogs only, never from its source.

**Cells:** `yes`, `partial`, `no` or `?` (could not verify), then a short note and a source key
in brackets. The keys are listed under [Sources](#sources).
- **add-on:** a separately licensed module from the same vendor.
- **pkg:** a separate package under the same licence.
- **Not listed:** the vendor's feature pages do not mention it. That means "no" with ordinary
  confidence, not proof.

**Columns:**
- **FreePDF**: io7.FreePDF 7.7.0.
- **iText**: iText Core 9.x, Java and .NET, plus add-ons.
- **QuestPDF**: 2026.9.1.
- **PdfPig**: 0.1.16.
- **PDFsharp 6.5**: PDFsharp/MigraDoc 6.5.0 and the 7.0 previews.
- **Syncfusion**: Essential PDF / .NET PDF Library.
- **Aspose**: Aspose.PDF for .NET.
- **pdf-lib**: 1.17.1.
- **qpdf/pikepdf**: qpdf 12.4 and pikepdf 10.
- **pyHanko**: 0.38.1.

## Creation

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| C1 Low-level drawing (paths, text, images) | yes: XGraphics, gradients, transparency [fp:readme] | yes: PdfCanvas [it:canvas] | partial: layout DSL, SVG/canvas element, no raw page API [qp:FO] | partial: text, lines, rectangles, images [pp:PB] | yes: XGraphics [ps:PF] | yes: PdfGraphics [sf:shapes] | yes: DOM plus raw operators [as:operators] | yes: drawText/Image/SvgPath [pl:RM] | partial: basic pikepdf.canvas [qk:pCv] | partial: "very primitive" [ph:hU] |
| C2 TrueType embedding + subsetting | yes: subsets; finds installed fonts since 7.2 [fp:migr-fonts] | yes [it:fonts] | yes: subsetting since 2024.3 [qp:R24] | partial: embeds TTF; subsetting not documented [pp:DB] | yes [ps:FE] | yes [sf:text] | yes [as:formatting] | yes: via fontkit; subsetting "does not work for all fonts" [pl:RM] | no: Standard 14 and existing fonts only [qk:pCv] | yes: fontTools, for stamps [ph:hFo] |
| C3 OpenType CFF fonts | no: CFF .otf and .ttc collections skipped [fp:migr-fonts] | yes: CFF subsetting [it:cff] | ? | no: "Standard 14 and TrueType only" [pp:RM] | partial: CFF embedded whole, not subset [ps:FE] | yes: .otf accepted (CFF not named) [sf:text] | yes: .otf (CFF not named) [as:addtext] | ? | no [qk:pCv] | partial: CID-keyed OTF [ph:hU] |
| C4 Complex scripts, right-to-left | no: shaping "not promised" [fp:support] | partial: add-on pdfCalligraph, commercial only [it:calligraph] | yes: RTL, shaping, bidi [qp:GH] | no: builder text ASCII only [pp:RM] | ? not documented | yes: RTL plus ComplexScript flag [sf:text] | ?: not in guide; forum shows bidi bugs [as:rtl] | ? not documented | no | partial: HarfBuzz, stamp text only [ph:hFo] |
| C5 Image formats | partial: JPEG, PNG, BMP; GIF/TIFF only in GDI/WPF builds; no SVG/WebP [fp:src-img] [ps:IMG] | yes: + GIF, TIFF, JP2, JBIG2; SVG/WebP modules [it:imgtype] | partial: JPEG, PNG, BMP, WebP, SVG [qp:IMG] | partial: JPEG, PNG, CCITT [pp:PB] | partial: as FreePDF [ps:IMG] | yes: + GIF, TIFF, EMF, SVG; no WebP [sf:img2pdf] | yes: + GIF, TIFF, SVG, EMF, HEIC; no WebP [as:img2pdf] | partial: PNG, JPEG [pl:JS] | partial: via Pillow [qk:pCv] | partial: Pillow bitmaps [ph:hU] |
| C6 Barcodes, QR | partial: Code 39, 2 of 5, DataMatrix, OMR; no QR or Code 128; MigraDoc has no barcode renderer [fp:src-bc] | yes: QR, PDF417, Code 128, EAN [it:barcodes] | partial: third-party integration [qp:FO] | no [pp:RM] | ? not in feature list [ps:MF] | yes: 1D/2D, QR [sf:barcode] | no: separate Aspose.BarCode [as:total] | no [pl:RM] | no [qk:qO] | yes: QR stamps [ph:hAPI] |
| C7 Charts | yes: PdfSharp.Charting, MigraDoc charts [fp:migr] | no [it:suite] | partial: ScottPlot etc. integration [qp:FO] | no [pp:RM] | yes [ps:MF] | no [sf:overview] | no: basic shapes only [as:graphs] | no [pl:RM] | no [qk:qO] | no [ph:hRW] |

## Layout

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| L1 Flow layout (paragraphs, tables, headers/footers, page breaks) | yes: MigraDoc; also RTF output [fp:readme] | yes [it:events] | yes [qp:FO] | no: absolute positions [pp:DC] | yes [ps:MF] | yes: flow layout, PdfGrid [sf:flow] | yes [as:table] | no [pl:RM] | no [qk:qO] | no [ph:hRW] |
| L2 HTML/CSS to PDF | no | yes: add-on pdfHTML (AGPL) [it:pdfhtml] | no [qp:FO] | no [pp:RM] | no: points to third-party HtmlRenderer [ps:FAQ] | yes: pkg, bundles Blink [sf:html] | yes: built-in engine [as:html] | no [pl:RM] | no [qk:qO] | no [ph:hRW] |
| L3 Automatic hyphenation | no: none in source [fp:src-grep] | yes [it:hyph] | partial: soft hyphens only [qp:TS] | no [pp:RM] | ? [ps:MF] | no [sf:text] | partial: XML attribute only [as:xml] | no | no | no |
| L4 Page-number fields, TOC | yes: Page, NumPages, PageRef, Section fields; TOC built by hand [fp:src-fields] | partial: by hand with events [it:events] | yes: page numbers, sections [qp:FO] | no [pp:RM] | partial: page fields [ps:MF] | partial: page fields; TOC by hand [sf:pages] | yes: TOC generation [as:manipulate] | no | no | no |

## Reading and extraction

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| R1 Parse existing PDFs, repair damaged ones | partial: object, xref and hybrid streams, tolerant parser; repairs only misnumbered xref entries, no full rebuild [fp:src-parser] [fp:roadmap] | yes: rebuilds xref [it:reader] | partial: loads via qpdf for merging only [qp:DO] | yes: brute-force xref rebuild [pp:RM] | yes: more tolerant in 7.0 [ps:PH] | yes: OpenAndRepair, basic [sf:open] | yes: repair [as:features] | partial: lenient; encrypted files throw [pl:SRC] | yes: recovery [qk:qD] | yes: strict=False [ph:hFAQ] |
| R2 Plain text extraction | no: no API [fp:src-grep] | yes [it:textx] | no [qp:FO] | yes: core purpose [pp:RM] | partial: add-on PsX Text Extractor [ps:OFF] | yes [sf:textx] | yes [as:textx] | no [pl:RM] | no [qk:pCS] | no [ph:idx] |
| R3 Text with positions, layout analysis | no | partial: glyph positions, no layout analysis [it:textx] | no | yes: letters, words, Docstrum, reading order [pp:DLA] | partial: add-on PsX Text Extractor [ps:OFF] | yes: lines, words, characters with bounds [sf:textx] | yes [as:searchtext] | no | no | no |
| R4 Image extraction | partial: only by walking the object model, no helper [fp:readme] | yes [it:imgx] | no | yes [pp:IM] | partial: add-on PsX [ps:OFF] | yes: Imaging pkg on .NET Core [sf:imgx] | yes [as:imgx] | no | yes: pikepdf PdfImage [qk:pIm] | no |
| R5 Table extraction | no | no: pdf2Data moved to Apryse [it:pdf2data] | no | no: points to Tabula Sharp [pp:WK] | no [ps:OFF] | yes: pkg Smart Table Extractor [sf:table] | yes: TableAbsorber [as:tablex] | no | no | no |
| R6 OCR | no | yes: add-on pdfOCR (AGPL; Tesseract/ONNX) [it:ocr] | no | no | no [ps:OFF] | yes: pkg, Tesseract [sf:ocr] | partial: hOCR hook for external OCR [as:create] | no | no | no |

## Editing

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| E1 Merge, split, import pages | yes: AppendPages/ExtractPages keep fields, bookmarks, links [fp:readme] | yes [it:pdfdoc] | yes [qp:DO] | yes [pp:RM] | yes [ps:PF] | yes [sf:merge] | yes [as:merge] | yes [pl:JS] | yes [qk:qC] | no [ph:idx] |
| E2 Stamp, watermark, overlay | yes: draw on imported pages, XPdfForm [fp:support] | yes [it:manip] | yes: overlay/underlay [qp:DO] | partial [pp:PB] | yes [ps:PF] | yes [sf:watermark] | yes [as:stamping] | yes: embedPage [pl:RM] | yes [qk:qC] | partial [ph:hSt] |
| E3 Page labels | no: dictionary key only; merging drops them [fp:src-catalog] [fp:support] | yes [it:page] | no [qp:DO] | ? | ? | yes [sf:pages] | yes [as:numbering] | no [pl:SRC] | yes: qpdf sets; pikepdf reads [qk:qC] | ? |
| E4 Optional content (layers) | no: dictionary key only; merging drops it [fp:src-catalog] [fp:support] | yes: PdfLayer [it:layer] | no [qp:DO] | ? | ? | yes [sf:layers] | partial: lock/flatten; creating not documented [as:layers] | no [pl:RM] | no [qk:pPg] | ? |
| E5 Embedded attachments | yes: EmbeddedFilesManager add/read, /AF [fp:src-attach] | yes [it:pdfdoc] | partial: add only [qp:DO] | yes [pp:RM] | partial [ps:PF] | yes [sf:attach] | yes [as:attach] | partial: add only [pl:RM] | yes [qk:qC] | yes [ph:hAPI] |
| E6 Annotations | partial: typed classes for ~25 types; no per-type appearance generation [fp:src-annot] | yes [it:page] | partial: hyperlinks only [qp:HL] | partial: read; AddLink [pp:RM] | partial [ps:PF] | yes [sf:annot] | yes [as:annot] | no [pl:SRC] | partial: low level [qk:pAt] | partial: signature widgets [ph:hF] |
| E7 Bookmarks / outlines | yes [fp:migr-merge] | yes [it:pdfdoc] | partial: from headings [qp:ACC] | yes [pp:RM] | yes [ps:PH] | yes [sf:bookmarks] | yes [as:bookmarks] | no [pl:SRC] | yes: pikepdf [qk:pOl] | ? |
| E8 Redaction that removes content | no: redact-annotation dictionary only [fp:src-redact] | yes: add-on pdfSweep (AGPL) [it:sweep] | no | no | no [ps:FAQ] | yes: Imaging pkg on .NET Core [sf:redact] | yes [as:extraannot] | no | no [qk:pCS] | no |
| E9 Content editing, find/replace | partial: low-level content-stream parser/writer, no text replace [fp:src-content] | partial: no replace API [it:replace] | no | no [pp:RM] | no [ps:FAQ] | partial: find; replace by redact and redraw [sf:text] | yes: regex find/replace [as:replace] | no [pl:RM] | partial: TokenFilter [qk:pCS] | no |
| E10 Incremental (append) save | yes: Append mode keeps signatures, also encrypted files [fp:readme] | yes [it:stamping] | no [qp:DO] | yes: PdfIncrementalUpdateBuilder [pp:RM] | ? not stated [ps:SIG] | yes: default for loaded docs [sf:document] | yes [as:save] | no: full rewrite [pl:SRC] | no: pikepdf merges updates [qk:pAPI] | yes: default [ph:hRW] |
| E11 Object streams on write | no: full save writes a classic xref table; Append keeps an xref stream when the file has one [fp:src-writer] [fp:migr-signed] | yes: full compression mode [it:writer] | ? | ? [pp:WT] | ? [ps:PH] | partial: xref streams default; object streams unclear [sf:xref] | yes: CompressObjects [as:compressobjects] | yes: default [pl:SRC] | yes [qk:qC] | ? |
| E12 Linearization | no: none in source [fp:src-grep] | no [it:writer] | yes: Linearize() via qpdf [qp:DO] | no [pp:RM] | ? | yes [sf:document] | yes [as:optimize] | no [pl:RM] | yes [qk:qC] | ? |
| E13 Optimisation (image downsampling, dedup) | partial: Flate, font subsets, shared resources within one merge; no dedup or image recompression [fp:migr-merge] | partial: smart mode; add-on pdfOptimizer (commercial) [it:optimizer] | partial: compression, image DPI [qp:DS] | partial: in-memory dedup writer [pp:WT] | partial [ps:FE] | yes [sf:compress] | yes [as:optimize] | no [pl:RM] | partial: image optimisation, unused resources [qk:qC] | no [ph:idx] |

## Forms

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| F1 Fill AcroForm fields | yes: typed fields, draws appearances; WinAnsi text only [fp:migr-forms] | yes [it:core] | no [qp:FO] | no: read-only [pp:FRM] | partial: "limited"; PsX Forms add-on [ps:FAQ] [ps:OFF] | yes [sf:forms] | yes [as:fill] | yes [pl:FORM] | yes: pikepdf.form [qk:pF] | ? |
| F2 Create new fields | no: field constructors are internal; signing adds signature fields only [fp:src-forms] | yes [it:forms] | no | no [pp:FRM] | partial: add-on PsX Forms [ps:OFF] | yes [sf:forms] | yes [as:createform] | yes [pl:FORM] | no [qk:pF] | partial: empty signature fields [ph:hF] |
| F3 Flatten | yes: since 7.2 [fp:migr-forms] | yes [it:core] | no | no | partial: add-on PsX Forms [ps:OFF] | yes [sf:forms] | yes [as:manipulate] | yes [pl:FORM] | yes: needs appearances [qk:pF] | ? |
| F4 XFA | no: not read; merging drops it [fp:support] | partial: fill/extract; flattening needs add-on pdfXFA (commercial) [it:xfa] | no | no [pp:FRM] | ? | yes: create, fill, flatten [sf:xfa] | partial: fill, convert to AcroForm [as:xfa] | no: getForm deletes XFA [pl:FORM] | no [qk:pF] | ? |
| F5 FDF / XFDF | no | partial: XFDF [it:xfdf] | no | no | ? | partial: FDF [sf:forms] | yes: FDF, XFDF, XML, JSON [as:importexport] | no [pl:FORM] | ? | ? |

## Signatures

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| S1 Sign: CMS / PAdES B-B | partial: detached CMS, SHA-256/384/512; SubFilter adbe.pkcs7.detached, not ETSI.CAdES.detached, so not PAdES baseline [fp:support] [fp:src-sig] | yes: PdfPadesSigner [it:pades] | no: "not available… nor planned" [qp:I146] | no [pp:RM] | partial: CMS; PAdES level not stated [ps:SIG] | yes: CMS, CAdES [sf:sign] | yes: claims PAdES [as:sign] | no [pl:I637] | no [qk:pSec] | yes: B-B to B-LTA [ph:hL] |
| S2 Sign an existing field; several signatures | yes: FieldName (7.6), Append mode [fp:readme] | yes [it:signerprops] | no [qp:I146] | no | partial: unique field names in 6.5 [ps:SIG] [ps:PH] | yes [sf:sign] | yes [as:addsig] | no [pl:I637] | no | yes [ph:hS] |
| S3 RFC 3161 timestamps | partial: signature timestamp from a TSA URI on .NET 8+ only; no document timestamps; release checks don't cover it [fp:src-signer] [fp:support] | yes: signature and document [it:signer] | no | no | yes: signature timestamp; not on .NET Framework [ps:SIG] | yes: both [sf:sign] | partial: signature timestamp only [as:sign] | no | no | yes: both [ph:hS] [ph:hL] |
| S4 LTV (DSS/VRI, B-LT/B-LTA) | no: DSS marked "nyi" in source [fp:src-catalog] | yes [it:pades] | no | no | ? not mentioned [ps:SIG] | yes: EnableLtv, DSS, LTA [sf:sign] | partial: UseLtv flag only [as:useltv] | no | no | yes [ph:hS] |
| S5 Verify signatures | no: no API; CI checks with pdfsig and pyHanko [fp:support] | yes: chain, revocation, EU trusted lists [it:validation] | no | no | ? not mentioned [ps:SIG] | yes [sf:sign] | yes [as:sign] | no | no | yes: AdES, LTV, diff analysis [ph:hV] |
| S6 Certification (DocMDP), FieldMDP | partial: enforces DocMDP/FieldMDP on Append saves and records FieldMDP for locked fields; cannot create a certification signature [fp:support] [fp:src-sigopts] | yes [it:signerprops] | no | no | ? | yes: DocMDP; FieldMDP not mentioned [sf:sign] | yes: DocMDPSignature [as:sign] | no | no | yes [ph:hL] [ph:hD] |
| S7 External / HSM / cloud / deferred signing | partial: pluggable async IDigitalSigner; no two-step deferred API [fp:src-isigner] | yes: signDeferred, external container [it:signer] | no | no | yes: IDigitalSigner [ps:SIG] | yes: external signer, deferred [sf:sign] | yes [as:smartcard] | no | no | yes: PKCS#11, CSC [ph:hSg] |

## Encryption

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| X1 Encrypt: RC4, AES-128, AES-256 | yes [fp:support] | yes: also AES-GCM, PDF MAC [it:sec90] | yes [qp:DO] | no [pp:RM] | yes [ps:ENC] | yes: also AES-GCM [sf:security] | yes [as:encrypt] | no [pl:RM] | yes [qk:qE] | yes [ph:hAPI] |
| X2 Open encrypted PDFs | yes: also in Append mode [fp:migr-signed] | yes [it:readerprops] | yes [qp:DO] | yes [pp:RM] | yes [ps:ENC] | yes [sf:security] | yes [as:decrypt] | no: throws [pl:RM] | yes [qk:qC] | yes [ph:hE] |
| X3 Public-key (certificate) encryption | no: standard handler only [fp:support] | yes [it:writer] | no [qp:DO] | ? | ? [ps:ENC] | no [sf:security] | yes [as:certenc] | no | no [qk:qE] | yes [ph:hE] |

## Standards

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| P1 Create PDF/A | partial: 1b, 2b, 3b checked with veraPDF; 1a to 4f can be declared but aren't validated [fp:support] [fp:src-pdfa] | yes: parts 1 to 4, all levels [it:core] | yes: 2a/b/u, 3a/b/u [qp:GH] | partial: declares 1A to 3B, no tagging [pp:PA] | partial: "very early state" [ps:PDFA] | yes: parts 1 to 4 [sf:conformance] | yes: parts 1 to 4 [as:pdfa] | no | partial: pikepdf declares level B [qk:pA] | no [ph:idx] |
| P2 Convert existing PDF to PDF/A | no: "not promised" [fp:support] | no [it:pdfua] | no | no | no [ps:PDFA] | yes: Imaging pkg [sf:conformance] | yes [as:pdfa] | no | no [qk:pA] | no |
| P3 Validate PDF/A | no: veraPDF in CI [fp:support] | partial: its own output only [it:pdfua] | no [qp:ACC] | no | no [ps:PDFA] | no [sf:conformance] | yes: PDF/A and PDF/UA [as:pdfa] | no | partial: allow-list check [qk:pA] | no |
| P4 Tagged PDF/UA | yes: PDF/UA-1 from MigraDoc, checked with veraPDF; not for XGraphics; no UA-2 [fp:readme] [fp:support] | yes: UA-1 and UA-2 [it:92] | yes: UA-1 [qp:ACC] | no [pp:DB] | partial: UAManager [ps:UA] | yes: also UA-2 [sf:tagged] | yes: UA-1 [as:tagged] | no [pl:SRC] | no | no |
| P5 Factur-X / ZUGFeRD | yes: FacturXManager, PDF/A-3b checked with veraPDF [fp:readme] | partial: samples only [it:zugferd] | yes [qp:ZUG] | partial: by hand [pp:DB] | partial: add-on PsX Factur-X [ps:OFF] | yes: incl. XRechnung profiles [sf:zugferd] | partial: attaches XML only [as:zugferd] | no | no | no |
| P6 PDF 2.0 | partial: AES-256 files get version 2.0; no 2.0 conformance claim [fp:changelog] | yes [it:core] | ? | ? | partial [ps:PH] | yes [sf:overview] | yes [as:keyfeatures] | ? | partial [qk:qE] | partial [ph:hL] |

## Rendering

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| V1 Render pages to images | partial: Windows GDI/WPF builds preview their own MigraDoc output; cannot rasterise existing PDFs [fp:src-mdwin] | no: pdfRender gone [it:render] | partial: its own documents only [qp:OUT] | no [pp:FAQ] | no: "cannot render PDF files" [ps:FAQ] | yes: pkg, native PDFium [sf:render] | yes: libgdiplus on Linux [as:toimage] | no | no [qk:pI] | no |
| V2 Printing | partial: MigraDocPrintDocument for its own documents, Windows only [fp:src-print] | no [it:events] | no [qp:OUT] | no | partial [ps:PF] | partial: Windows viewer [sf:document] | partial: Windows only [as:drawing] | no | no [qk:pI] | no |

## Conversion

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| K1 Office to PDF | no | no: pdfOffice gone [it:office] | no | no | no [ps:OFF] | yes: pkg DocIO/XlsIO/Presentation [sf:conv] | no: needs Aspose.Words/Cells/Slides [as:pptx] | no | no [qk:qO] | no |
| K2 PDF to Word, HTML, other | no: only MigraDoc's own model to RTF [fp:readme] | no [it:pdfhtml] | no: own documents to images/SVG [qp:OUT] | partial: text to ALTO/PageXML/hOCR [pp:DLA] | no [ps:MF] | yes: pkg [sf:pdf2word] | yes [as:pdf2word] | no | no [qk:qJ] | no |
| K3 Image, SVG, XPS to PDF | partial: draw images yourself; no SVG/XPS [fp:src-img] | partial: images, SVG module [it:core] | partial: images/SVG inside layouts [qp:FO] | no | partial: images; add-on PsX Markdown2PDF [ps:IMG] [ps:OFF] | yes [sf:conv] | yes [as:other2pdf] | partial [pl:JS] | no: recommends img2pdf [qk:pIm] | no |

## Platform

| Feature | FreePDF | iText | QuestPDF | PdfPig | PDFsharp 6.5 | Syncfusion | Aspose | pdf-lib | qpdf/pikepdf | pyHanko |
|---|---|---|---|---|---|---|---|---|---|---|
| Z1 Cross-platform, no native dependencies | yes: managed; netstandard2.0 and .NET 8–10, Linux and Windows CI; GDI/WPF builds are Windows-only [fp:readme] [fp:support] | yes: also Native AOT [it:92] | partial: bundles native Skia and qpdf; no mobile/WASM [qp:REL] | yes [pp:NG] | yes [ps:SPEC] | partial: SkiaSharp/PDFium/Blink natives for some features [sf:nuget] | partial: libgdiplus, or the Drawing variant [as:install] | yes: JavaScript [pl:RM] | yes: native C++, wheels bundle it [qk:pGH] | yes: Python [ph:hCh] |

## Licences

| Tool | Licence | Free use | Commercial terms |
|---|---|---|---|
| **FreePDF** | MIT [fp:readme] | Unrestricted | None |
| **iText Core + pdfHTML, pdfSweep, pdfOCR** | AGPL-3.0 or commercial [it:buy] [it:agpl] | AGPL: you must publish your whole application's source, also for network/SaaS use, and keep iText's producer line | Quote only: annual subscription by PDF volume, or OEM. pdfCalligraph, pdfOptimizer and pdfXFA are commercial only [it:suite] |
| **QuestPDF** | QuestPDF Community License v3.0 (since 6 Jul 2026), "source-available commercial", not MIT, not OSI [qp:LIC] | Under USD 1M annual gross revenue, plus non-profits, academia, open source and transitive-only use. Public-sector bodies and listed companies are excluded whatever their revenue | Professional from $1,999, Enterprise from $4,999, perpetual with an annual updates term [qp:PR] |
| **PdfPig** | Apache-2.0 [pp:NG] | Unrestricted | None |
| **PDFsharp/MigraDoc 6.5 and 7.0** | AGPL-3.0-or-later or commercial. The licence must be chosen in code, otherwise it throws [ps:LIC] [ps:LL] | AGPL terms (closed-source use prohibited) | €750 one-off excl. VAT: unlimited developers and servers, 6.5.x and 7.0.x updates [ps:PRI]. PsX add-ons (Forms, Factur-X, Text Extractor, Markdown2PDF) are separate, price on request [ps:OFF] |
| **Syncfusion** | Proprietary [sf:price] | Community License: under $1M revenue, at most 5 developers and 10 employees, at most $3M outside capital; government excluded [sf:community] | Document SDK $1,199 per developer per year, subscription [sf:price] |
| **Aspose.PDF for .NET** | Proprietary [as:price] | Evaluation: watermark on every page, first 4 pages only [as:licensing] | From $1,199 (1 developer, 1 location) to $59,950 (Site SDK), perpetual with 1 year of updates; metered from $1,999/month [as:price] |
| **pdf-lib** | MIT [pl:RM] | Unrestricted | None. Last release v1.17.1 on 2021-11-06; looks dormant [pl:NPM] [pl:MT] |
| **qpdf / pikepdf** | Apache-2.0 / MPL-2.0 [qk:qL] [qk:pGH] | Unrestricted | None |
| **pyHanko** | MIT; parts derived from PyPDF2 are BSD-3 [ph:hLic] | Unrestricted | None |

### Other free .NET options

- **Aspose.PDF FOSS for .NET** (MIT, NuGet `Aspose.PDF.FOSS` 26.11.0).
  - Claims: text and table extraction, rendering, AcroForm create/fill, PKCS#7 signing with
    DocMDP and RFC 3161 timestamps, signature verification, PDF/A 1a–4f and PDF/UA-1 export,
    HTML import.
  - Maturity: published as monthly source drops (7 commits, about 2,000 downloads). The NuGet
    description says "some features are incomplete and the public API may change".
  - Sources: [as:foss] [as:fossproduct]. These are vendor claims; nobody has tested them yet.
- **HtmlRenderer.PdfSharp 1.6.1** (BSD-3-Clause, July 2026; HTML 4.01 and CSS 2, fully managed).
  - Dependencies: PDFsharp ≥ 6.2.4 and System.Drawing.Common.
  - Because it depends on the `PDFsharp` package ID, FreePDF users cannot use it as published.
  - Sources: [x:htmlrenderer] [x:htmlrenderer-gh].
- **PDFtoImage** (MIT): renders PDFs with PDFium and SkiaSharp, native binaries per platform
  [x:pdftoimage].

## Sources

### FreePDF (paths in this repository; github.com/io7/FreePDF)

| Key | Source |
|---|---|
| fp:readme | `README.md` |
| fp:changelog | `CHANGELOG.md` (7.5.0: AES-256 files and `/Version 2.0`) |
| fp:support | `docs/support-policy.md` |
| fp:migr | `docs/migrating-to-7.md` (package contents) |
| fp:migr-fonts | `docs/migrating-to-7.md#fonts` |
| fp:migr-forms | `docs/migrating-to-7.md#filling-forms` |
| fp:migr-merge | `docs/migrating-to-7.md#merging-and-splitting` |
| fp:migr-signed | `docs/migrating-to-7.md#signed-documents` |
| fp:roadmap | `docs/research/7.0-roadmap.md` |
| fp:src-sig | `src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Signatures/DigitalSignatureHandler.cs` line 393 (`/adbe.pkcs7.detached`) |
| fp:src-sigopts | `…/Pdf.Signatures/DigitalSignatureOptions.cs` (no certification option) |
| fp:src-signer | `…/PdfSharp.Cryptography/Pdf.Signatures/PdfSharpDefaultSigner.cs` (RFC 3161 behind `#if NET8_0_OR_GREATER`) |
| fp:src-isigner | `src/foundation/src/shared/src/PdfSharp.System/Pdf.Signatures/IDigitalSigner.cs` |
| fp:src-catalog | `…/PdfSharp/Pdf.Advanced/PdfCatalog.cs` (`/PageLabels`, `/OCProperties` keys only; "DSS - nyi") |
| fp:src-redact | `…/PdfSharp/Pdf.Annotations/PdfRedactAnnotation.cs` (keys only) |
| fp:src-annot | `…/PdfSharp/Pdf.Annotations/` (only base `/AP` and the widget/signature appearance handler) |
| fp:src-forms | `…/PdfSharp/Pdf.Forms/Design.cs` (field constructors `internal`) |
| fp:src-bc | `…/PDFsharp/src/PdfSharp.BarCodes` (Code3of9Standard, Code2of5Interleaved, CodeDataMatrix ECC200, CodeOmr); no barcode renderer in `MigraDoc.Rendering/Rendering` |
| fp:src-img | Image importers: `PngImageImporter`, `JpegImageImporter`, `BmpImageImporter`, `BitmapImageImporter` (GDI/WPF) |
| fp:src-parser | `…/PdfSharp/Pdf.IO/Parser.cs` (`RepairedCrossReferences`: misnumbered entries only) |
| fp:src-writer | `…/PdfSharp/Pdf/PdfDocument.cs` `WriteFile` (classic xref table) |
| fp:src-attach | `…/PdfSharp/Pdf.Attachments/EmbeddedFilesManager.cs` |
| fp:src-content | `…/PdfSharp/Pdf.Content/` (CParser, ContentReader, ContentWriter) |
| fp:src-pdfa | `…/PdfSharp/Pdf.PdfA/PdfAFormats.cs` |
| fp:src-fields | `…/MigraDoc.DocumentObjectModel/DocumentObjectModel.Fields/` |
| fp:src-print | `…/MigraDoc.Rendering/Rendering.Printing/MigraDocPrintDocument.cs` |
| fp:src-mdwin | `…/MigraDoc.Rendering/Rendering.Windows/DocumentPreview.xaml` |
| fp:src-grep | Searched the whole `src/` tree: no text-extraction, linearization or hyphenation code |

### iText

| Key | URL |
|---|---|
| it:core | https://itextpdf.com/products/itext-core |
| it:suite | https://itextpdf.com/products/itext-suite |
| it:canvas | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/canvas/PdfCanvas.html |
| it:fonts | https://kb.itextpdf.com/itext/chapter-6-using-fonts-in-pdfhtml |
| it:cff | https://api.itextpdf.com/iText/java/8.0.3/com/itextpdf/io/font/CFFFontSubset.html |
| it:calligraph | https://itextpdf.com/products/pdfcalligraph |
| it:imgtype | https://api.itextpdf.com/iText/java/9.0.0/com/itextpdf/io/image/ImageType.html |
| it:barcodes | https://api.itextpdf.com/iText/java/9.0.0/com/itextpdf/barcodes/package-summary.html |
| it:events | https://kb.itextpdf.com/itext/chapter-7-handling-events-setting-viewer-preferenc |
| it:pdfhtml | https://itextpdf.com/products/convert-html-css-to-pdf-pdfhtml |
| it:hyph | https://api.itextpdf.com/iText/java/9.0.0/com/itextpdf/layout/hyphenation/package-summary.html |
| it:reader | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/PdfReader.html |
| it:textx | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/canvas/parser/listener/package-summary.html |
| it:imgx | https://kb.itextpdf.com/itext/supported-pdf-image-formats |
| it:pdf2data | https://itextpdf.com/en/products/itext-7/extract-data-from-pdf-pdf2data (redirects to Apryse) |
| it:ocr | https://itextpdf.com/products/pdf-ocr-text-recognition |
| it:pdfdoc | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/PdfDocument.html |
| it:manip | https://kb.itextpdf.com/itext/chapter-5-manipulating-an-existing-pdf-document |
| it:page | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/PdfPage.html |
| it:layer | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/layer/PdfLayer.html |
| it:sweep | https://itextpdf.com/products/pdf-redaction-pdfsweep |
| it:replace | https://itextpdf.com/en/resources/examples/itext-7/replacing-pdf-objects |
| it:stamping | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/StampingProperties.html |
| it:writer | https://api.itextpdf.com/iText/java/9.0.0/com/itextpdf/kernel/pdf/WriterProperties.html |
| it:optimizer | https://itextpdf.com/products/compress-pdf-pdfoptimizer |
| it:forms | https://kb.itextpdf.com/itext/itext-7-jump-start-tutorial-chapter-4 |
| it:xfa | https://itextpdf.com/products/flatten-pdf-pdfxfa |
| it:xfdf | https://api.itextpdf.com/iText/java/latest/com/itextpdf/forms/xfdf/package-summary.html |
| it:pades | https://api.itextpdf.com/iText/java/9.0.0/com/itextpdf/signatures/PdfPadesSigner.html |
| it:signerprops | https://api.itextpdf.com/iText/java/latest/com/itextpdf/signatures/SignerProperties.html |
| it:signer | https://api.itextpdf.com/iText/java/latest/com/itextpdf/signatures/PdfSigner.html |
| it:validation | https://kb.itextpdf.com/itext/signature-module-refactoring-built-in-signature-va |
| it:sec90 | https://itextpdf.com/blog/technical-notes/itext-suite-90-new-security-standards-signature-validation |
| it:readerprops | https://api.itextpdf.com/iText/java/latest/com/itextpdf/kernel/pdf/ReaderProperties.html |
| it:pdfua | https://kb.itextpdf.com/itext/chapter-7-creating-pdf-ua-and-pdf-a-documents |
| it:92 | https://itextpdf.com/blog/itext-suite-92-net-maui-aot-pdfua-validation |
| it:zugferd | https://itextpdf.com/blog/technical-notes/creating-zugferd-itext |
| it:render | https://kb.itextpdf.com/itext/release-pdfrender-1-0-0 |
| it:office | https://itextpdf.com/products/convert-ms-office-to-pdf-pdfoffice (redirects to Apryse) |
| it:buy | https://itextpdf.com/how-buy |
| it:agpl | https://itextpdf.com/how-buy/AGPLv3-license |

### QuestPDF

| Key | URL |
|---|---|
| qp:DO | https://www.questpdf.com/concepts/document-operations.html |
| qp:FO | https://www.questpdf.com/features-overview.html |
| qp:GH | https://github.com/QuestPDF/QuestPDF |
| qp:REL | https://github.com/QuestPDF/QuestPDF/releases |
| qp:R24 | https://github.com/QuestPDF/QuestPDF/releases/tag/2024.3.0 |
| qp:TS | https://www.questpdf.com/api-reference/text/text-style.html |
| qp:IMG | https://www.questpdf.com/api-reference/image/basics.html |
| qp:ACC | https://www.questpdf.com/concepts/accessibility.html |
| qp:DS | https://www.questpdf.com/concepts/document-settings.html |
| qp:OUT | https://www.questpdf.com/concepts/generating-output.html |
| qp:ZUG | https://www.questpdf.com/examples/zugferd.html |
| qp:HL | https://www.questpdf.com/api-reference/hyperlink.html |
| qp:I146 | https://github.com/QuestPDF/QuestPDF/issues/146 |
| qp:LIC | https://www.questpdf.com/license/community.html |
| qp:PR | https://www.questpdf.com/pricing.html |

### PdfPig

| Key | URL |
|---|---|
| pp:RM | https://github.com/UglyToad/PdfPig |
| pp:WK | https://github.com/UglyToad/PdfPig/wiki |
| pp:DC | https://github.com/UglyToad/PdfPig/wiki/Document-Creation |
| pp:DLA | https://github.com/UglyToad/PdfPig/wiki/Document-Layout-Analysis |
| pp:IM | https://github.com/UglyToad/PdfPig/wiki/Images |
| pp:FRM | https://github.com/UglyToad/PdfPig/wiki/Forms-(AcroForms) |
| pp:FAQ | https://github.com/UglyToad/PdfPig/wiki/FAQ |
| pp:PB | https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfPageBuilder.cs |
| pp:DB | https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfDocumentBuilder.cs |
| pp:PA | https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfAStandard.cs |
| pp:WT | https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfWriterType.cs |
| pp:NG | https://www.nuget.org/packages/PdfPig |

### PDFsharp/MigraDoc 6.5 (public docs only)

| Key | URL |
|---|---|
| ps:PH | https://docs.pdfsharp.net/PDFsharp/History.html (also https://docs.pdfsharp.net/General/History.html) |
| ps:PF | https://docs.pdfsharp.net/PDFsharp/Overview/Features.html |
| ps:MF | https://docs.pdfsharp.net/MigraDoc/Overview/Features.html |
| ps:FAQ | https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html |
| ps:IMG | https://docs.pdfsharp.net/PDFsharp/Topics/Bitmap-Images/Formats.html |
| ps:FE | https://docs.pdfsharp.net/PDFsharp/Topics/Fonts/Font-Embedding.html |
| ps:ENC | https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Encryption.html |
| ps:SIG | https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Signatures.html |
| ps:PDFA | https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Archiving.html |
| ps:UA | https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Accessibility.html |
| ps:SPEC | https://docs.pdfsharp.net/PDFsharp/Overview/Specifications.html |
| ps:LIC | https://docs.pdfsharp.net/General/License/License.html |
| ps:LL | https://docs.pdfsharp.net/General/License/License-loading.html |
| ps:PRI | https://pdfsharp.com/Pricing |
| ps:OFF | https://pdfsharp.com/Offers |

### Syncfusion (help.syncfusion.com timed out for the researcher; rows were checked against its docs source repo, github.com/syncfusion-content/document-processing-docs, which builds these pages)

| Key | URL |
|---|---|
| sf:overview | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/overview |
| sf:shapes | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-shapes |
| sf:text | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-text |
| sf:img2pdf | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/converting-images-to-pdf |
| sf:barcode | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-barcode |
| sf:flow | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-flow-layout |
| sf:html | https://help.syncfusion.com/document-processing/pdf/conversions/html-to-pdf/overview |
| sf:pages | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-pages |
| sf:open | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/open-pdf-file |
| sf:textx | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-text-extraction |
| sf:imgx | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-image-extraction |
| sf:table | https://help.syncfusion.com/document-processing/data-extraction/net/working-with-table-extraction |
| sf:ocr | https://help.syncfusion.com/document-processing/data-extraction/net/ocr-overview |
| sf:merge | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/merge-documents |
| sf:watermark | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-watermarks |
| sf:layers | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-layers |
| sf:attach | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-attachments |
| sf:annot | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-annotations |
| sf:bookmarks | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-bookmarks |
| sf:redact | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-redaction |
| sf:document | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-document |
| sf:xref | https://help.syncfusion.com/cr/wpf/Syncfusion.Pdf.PdfCrossReferenceType.html |
| sf:compress | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-compression |
| sf:forms | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-forms |
| sf:xfa | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-xfa |
| sf:sign | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-digitalsignature |
| sf:security | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-security |
| sf:conformance | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-pdf-conformance |
| sf:tagged | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-tagged-pdf |
| sf:zugferd | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-zugferd-invoice |
| sf:render | https://help.syncfusion.com/document-processing/pdf/conversions/pdf-to-image/overview |
| sf:conv | https://help.syncfusion.com/document-processing/pdf/conversions/overview |
| sf:pdf2word | https://help.syncfusion.com/document-processing/data-extraction/net/conversions/pdf-to-word |
| sf:nuget | https://help.syncfusion.com/document-processing/pdf/pdf-library/net/nuget-packages-required |
| sf:community | https://www.syncfusion.com/products/communitylicense |
| sf:price | https://www.syncfusion.com/sales/products/document-sdk |

### Aspose.PDF for .NET

| Key | URL |
|---|---|
| as:operators | https://docs.aspose.com/pdf/net/working-with-operators/ |
| as:formatting | https://docs.aspose.com/pdf/net/formatting-pdf-document/ |
| as:addtext | https://docs.aspose.com/pdf/net/add-text-to-a-pdf-file/ |
| as:rtl | https://forum.aspose.com/t/implementing-arabic-text-support-in-pdfs-rtl-and-bi-directional-font-handling/291527 |
| as:img2pdf | https://docs.aspose.com/pdf/net/convert-images-format-to-pdf/ |
| as:total | https://docs.aspose.com/pdf/net/integration-with-aspose-total-components/ |
| as:graphs | https://docs.aspose.com/pdf/net/working-with-graphs/ |
| as:table | https://docs.aspose.com/pdf/net/add-table-in-existing-pdf-document/ |
| as:html | https://docs.aspose.com/pdf/net/convert-html-to-pdf/ |
| as:xml | https://docs.aspose.com/pdf/net/create-a-hello-world-pdf-document-through-xml-and-xslt/ |
| as:manipulate | https://docs.aspose.com/pdf/net/manipulate-pdf-document/ |
| as:features | https://docs.aspose.com/pdf/net/features/ |
| as:textx | https://docs.aspose.com/pdf/net/extract-text-from-pdf/ |
| as:searchtext | https://docs.aspose.com/pdf/net/seach-and-get-text-from-pdf/ |
| as:imgx | https://docs.aspose.com/pdf/net/extract-images-from-pdf-file/ |
| as:tablex | https://docs.aspose.com/pdf/net/extract-table-from-existing-pdf-document/ |
| as:create | https://docs.aspose.com/pdf/net/create-pdf-document/ |
| as:merge | https://docs.aspose.com/pdf/net/merge-pdf/ |
| as:stamping | https://docs.aspose.com/pdf/net/stamping/ |
| as:numbering | https://reference.aspose.com/pdf/net/aspose.pdf/numberingstyle |
| as:layers | https://docs.aspose.com/pdf/net/working-with-layers/ |
| as:attach | https://docs.aspose.com/pdf/net/attachments/ |
| as:annot | https://docs.aspose.com/pdf/net/annotations/ |
| as:bookmarks | https://docs.aspose.com/pdf/net/bookmarks/ |
| as:extraannot | https://docs.aspose.com/pdf/net/extra-annotations/ |
| as:replace | https://docs.aspose.com/pdf/net/replace-text-an-a-pdf/ |
| as:save | https://reference.aspose.com/pdf/net/aspose.pdf/document/save |
| as:compressobjects | https://reference.aspose.com/pdf/net/aspose.pdf.optimization/optimizationoptions/compressobjects/ |
| as:optimize | https://docs.aspose.com/pdf/net/optimize-pdf-document/ |
| as:fill | https://docs.aspose.com/pdf/net/fill-form/ |
| as:createform | https://docs.aspose.com/pdf/net/create-form/ |
| as:xfa | https://docs.aspose.com/pdf/net/xfa-forms/ |
| as:importexport | https://docs.aspose.com/pdf/net/import-and-export-data/ |
| as:sign | https://docs.aspose.com/pdf/net/digitally-sign-pdf-file/ |
| as:addsig | https://docs.aspose.com/pdf/net/add-signature-in-pdf-file/ |
| as:useltv | https://reference.aspose.com/pdf/es/net/aspose.pdf.forms/signature/useltv |
| as:smartcard | https://docs.aspose.com/pdf/net/sign-pdf-document-from-smart-card/ |
| as:encrypt | https://docs.aspose.com/pdf/net/encrypt-pdf/ |
| as:decrypt | https://docs.aspose.com/pdf/net/decrypt-pdf/ |
| as:certenc | https://reference2.aspose.com/pdf/net/aspose.pdf.security/certificateencryptionoptions |
| as:pdfa | https://docs.aspose.com/pdf/net/convert-pdf-to-pdfa/ |
| as:tagged | https://docs.aspose.com/pdf/net/create-tagged-pdf-documents/ |
| as:zugferd | https://docs.aspose.com/pdf/net/attach-zugferd/ |
| as:keyfeatures | https://docs.aspose.com/pdf/net/key-features/ |
| as:toimage | https://docs.aspose.com/pdf/net/convert-pdf-to-images-format/ |
| as:drawing | https://docs.aspose.com/pdf/net/drawing/ |
| as:pptx | https://docs.aspose.com/pdf/net/convert-pdf-to-powerpoint/ |
| as:pdf2word | https://docs.aspose.com/pdf/net/convert-pdf-to-word/ |
| as:other2pdf | https://docs.aspose.com/pdf/net/convert-other-files-to-pdf/ |
| as:install | https://docs.aspose.com/pdf/net/installation/ |
| as:licensing | https://docs.aspose.com/pdf/net/licensing/ |
| as:price | https://purchase.aspose.com/pricing/pdf/net/ |
| as:foss | https://github.com/aspose-pdf-foss/Aspose.PDF-FOSS-for-.NET |
| as:fossproduct | https://products.aspose.org/pdf/net/ |

### pdf-lib

| Key | URL |
|---|---|
| pl:RM | https://github.com/Hopding/pdf-lib |
| pl:JS | https://pdf-lib.js.org/ |
| pl:FORM | https://pdf-lib.js.org/docs/api/classes/pdfform |
| pl:SRC | https://github.com/Hopding/pdf-lib/blob/master/src/api/PDFDocument.ts |
| pl:I637 | https://github.com/Hopding/pdf-lib/issues/637 |
| pl:NPM | https://registry.npmjs.org/pdf-lib |
| pl:MT | https://github.com/Hopding/pdf-lib/blob/master/docs/MAINTAINERSHIP.md |

### qpdf / pikepdf

| Key | URL |
|---|---|
| qk:qO | https://qpdf.readthedocs.io/en/stable/overview.html |
| qk:qC | https://qpdf.readthedocs.io/en/stable/cli.html |
| qk:qE | https://qpdf.readthedocs.io/en/stable/encryption.html |
| qk:qD | https://qpdf.readthedocs.io/en/stable/design.html |
| qk:qJ | https://qpdf.readthedocs.io/en/stable/json.html |
| qk:qL | https://qpdf.readthedocs.io/en/stable/license.html |
| qk:pI | https://pikepdf.readthedocs.io/en/latest/ |
| qk:pCv | https://pikepdf.readthedocs.io/en/latest/api/canvas.html |
| qk:pAPI | https://pikepdf.readthedocs.io/en/latest/api/main.html |
| qk:pCS | https://pikepdf.readthedocs.io/en/latest/topics/content_streams.html |
| qk:pIm | https://pikepdf.readthedocs.io/en/latest/topics/images.html |
| qk:pPg | https://pikepdf.readthedocs.io/en/latest/topics/pages.html |
| qk:pAt | https://pikepdf.readthedocs.io/en/latest/topics/attachments.html |
| qk:pOl | https://pikepdf.readthedocs.io/en/latest/topics/outlines.html |
| qk:pF | https://pikepdf.readthedocs.io/en/latest/topics/interactive_forms.html |
| qk:pSec | https://pikepdf.readthedocs.io/en/latest/topics/security.html |
| qk:pA | https://pikepdf.readthedocs.io/en/latest/topics/pdfa.html |
| qk:pGH | https://github.com/pikepdf/pikepdf |

### pyHanko (docs moved from pyhanko.readthedocs.io to docs.pyhanko.eu)

| Key | URL |
|---|---|
| ph:idx | https://docs.pyhanko.eu/en/latest/ |
| ph:hS | https://docs.pyhanko.eu/en/latest/cli-guide/signing.html |
| ph:hE | https://docs.pyhanko.eu/en/latest/cli-guide/encryption.html |
| ph:hSt | https://docs.pyhanko.eu/en/latest/cli-guide/stamping.html |
| ph:hL | https://docs.pyhanko.eu/en/latest/lib-guide/signing.html |
| ph:hF | https://docs.pyhanko.eu/en/latest/lib-guide/sig-fields.html |
| ph:hV | https://docs.pyhanko.eu/en/latest/lib-guide/validation/index.html |
| ph:hD | https://docs.pyhanko.eu/en/latest/lib-guide/validation/diff-analysis.html |
| ph:hRW | https://docs.pyhanko.eu/en/latest/lib-guide/reading-writing.html |
| ph:hU | https://docs.pyhanko.eu/en/latest/lib-guide/pdf-utils.html |
| ph:hFo | https://docs.pyhanko.eu/en/latest/api-docs/pyhanko.pdf_utils.font.html |
| ph:hAPI | https://docs.pyhanko.eu/en/latest/api-docs/pyhanko.pdf_utils.html |
| ph:hSg | https://docs.pyhanko.eu/en/latest/api-docs/pyhanko.sign.signers.html |
| ph:hFAQ | https://docs.pyhanko.eu/en/latest/faq.html |
| ph:hCh | https://docs.pyhanko.eu/en/latest/changelog.html |
| ph:hLic | https://docs.pyhanko.eu/en/latest/license.html |

### Other

| Key | URL |
|---|---|
| x:htmlrenderer | https://www.nuget.org/packages/HtmlRenderer.PdfSharp/1.6.1 |
| x:htmlrenderer-gh | https://github.com/ArthurHub/HTML-Renderer |
| x:pdftoimage | https://github.com/sungaila/PDFtoImage |
| x:nuget | https://azuresearch-usnc.nuget.org/query?q=pdf&take=60 (download counts, fetched 2026-11-05) |

## Not verified

- **QuestPDF:** CFF font support (C3), object-stream output (E11) and PDF 2.0 (P6) are not
  documented. The README claims "advanced text shaping" but doesn't name an engine or list
  scripts.
- **PDFsharp 6.5:** the docs don't cover complex scripts, hyphenation, page labels, layers,
  incremental save, object streams, linearization, XFA, FDF, LTV, validation, DocMDP or
  public-key encryption. Those cells are `?`, and the upstream source was deliberately not
  read.
- **Syncfusion:** whether object streams are written (E11), and whether the Smart Data
  Extractor (tables, PDF to Word) is covered by the Document SDK licence.
- **Aspose:** right-to-left text quality (C4). Page labels, LTV, incremental save and
  certificate encryption appear only in the API reference, not the guides.
- **Aspose.PDF FOSS:** every feature is as claimed in its README. None was tested.
- **FreePDF:** the RFC 3161 timestamp path exists in code but its own release checks don't
  cover it. Whether GDI/WPF builds decode GIF/TIFF is taken from the inherited PDFsharp docs,
  not tested.
