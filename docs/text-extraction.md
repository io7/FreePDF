# Text extraction

`PdfTextExtractor` reads the text that a page's content draws.

```csharp
using PdfSharp.Pdf.Content.Text;

using var document = PdfReader.Open("invoice.pdf", PdfDocumentOpenMode.Import);
string text = PdfTextExtractor.GetText(document.Pages[0]);              // reading order
string all = PdfTextExtractor.GetText(document);                        // pages end with "\f"
foreach (var run in PdfTextExtractor.GetTextRuns(document.Pages[0]))    // one run per Tj, TJ, ' or "
    Console.WriteLine($"{run.Text} at {run.X}, {run.Y} ({run.FontSize} pt {run.FontName})");
```

Coordinates are in the page's default user space: points, origin at the bottom left, before `/Rotate`.
`PdfLineExtractor.GetLineSegments(page)` gives the straight segments of painted paths, for example table rules.

## What is decoded

The Unicode text of a character comes from, in this order (ISO 32000-1 9.10.2):

1. the font's ToUnicode CMap;
2. for simple fonts, the encoding: StandardEncoding, WinAnsiEncoding or MacRomanEncoding, the built-in
   encodings of Symbol and ZapfDingbats, and `/Differences`, with glyph names mapped by the Adobe Glyph List.

A character with neither is U+FFFD. Composite fonts read their codes with `/Identity-H`, `/Identity-V` or an
embedded CMap stream; their widths come from `/W` and `/DW`. An embedded CMap stream, ToUnicode or encoding,
inherits the CMap stream its `/UseCMap` names. The font can also come from a graphics state parameter
dictionary (`gs` with `/Font`). A standard font without `/Widths` takes the width of the glyph its encoding
selects.

## Order and spacing

Reading order puts lines from the top down and runs from left to right; baselines closer than half the font
size are one line. Content order keeps the order of the content stream and starts a line where the baseline
moves. A gap wider than 0.15 of the font size between runs, or a `TJ` adjustment of more than 150/1000 em, is
written as a space.

## Limits

`PdfTextExtractionOptions` bounds one page: `MaxDecodedBytes` (64 MiB), `MaxFormDepth` (32) and `MaxResults`
(1,000,000 runs or segments). Reaching one throws `PdfReaderException`. `MaxDecodedBytes` counts each stream's
data and decoded bytes every time it is read, the extracted text at two bytes per character, and the tables
built for fonts: 16 KiB per font, 8 KiB per CMap stream, 32 bytes per `/W` entry. So a page can use about
4,000 fonts. A CMap with more than 100 code space ranges of three or four bytes is refused the same way; one-
and two-byte ranges have no limit. A `/UseCMap` chain is followed for 8 CMaps.

A form that draws itself is read once; a syntax error, such as a number that is not finite, ends that content
stream and keeps the text before it. A stream whose filters fail on its data draws nothing. A stream with a
filter FreePDF does not implement (RunLengthDecode, CCITTFaxDecode, JBIG2Decode, JPXDecode, the TIFF
predictor) throws `NotImplementedException`, so text is never lost without notice.

## Not yet supported

- Predefined CJK CMaps other than Identity (also as a `/UseCMap` base), and Unicode for CIDs without ToUnicode.
- Widths of Helvetica and Times glyphs outside WinAnsiEncoding (for example `fi`) when the font has no
  `/Widths`; Courier glyphs are all 600/1000 em.
- Unicode from embedded font programs (a symbolic TrueType font or an embedded Type1 font without ToUnicode
  or a known encoding is read with StandardEncoding).
- MacExpertEncoding, vertical writing positions, `/ActualText`.
- Text in annotations and form fields: flatten the form first.
- Reading order for rotated text and columns.
- A font that draws several characters with one glyph under Identity-H can only map that glyph to one
  character in its ToUnicode CMap; no reader can tell those characters apart.
