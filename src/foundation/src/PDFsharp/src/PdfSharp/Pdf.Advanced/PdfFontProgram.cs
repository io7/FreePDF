// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Internal.OpenType;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.PdfA;

namespace PdfSharp.Pdf.Advanced
{
    /// <summary>
    /// Represents the base class of a PDF font.
    /// </summary>
    public sealed class PdfFontProgram : PdfDictionary
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PdfFontProgram"/> class.
        /// </summary>
        internal PdfFontProgram(PdfDocument document)
            : base(document)
        { }

        /// <summary>
        /// Initializes a new instance of this class using the elements of the specified dictionary.
        /// After this type transformation the specified dictionary is dead and cannot be used anymore.
        /// </summary>
        internal PdfFontProgram(PdfDictionary dict)
            : base(dict)
        { }

        internal void CreateFontFileAndAddToDescriptor(PdfFontDescriptor pdfFontDescriptor, CMapInfo cmapInfo, bool cidFont)
        {
            //var x = pdfFontDescriptor.Elements[PdfFontDescriptor.Keys.FontFile2];
            var x = pdfFontDescriptor.Elements.GetValue(PdfFontDescriptor.Keys.FontFile2); // #US373

            OpenTypeFontFace subSet = pdfFontDescriptor.Descriptor.FontFace.CreateFontSubset(cmapInfo.GlyphIndices, cidFont);
            byte[] fontData = subSet.OTFontSource.Bytes;

            Owner.Internals.AddObject(this);
            pdfFontDescriptor.Elements[PdfFontDescriptor.Keys.FontFile2] = RequiredReference;

            Elements["/Length1"] = new PdfInteger(fontData.Length);
            if (!Owner.Options.NoCompression)
            {
                fontData = Filtering.FlateDecode.Encode(fontData, Document.Options.FlateEncodeMode);
                Elements.SetName(PdfStream.Keys.Filter, "/FlateDecode");
            }
            Elements.SetInteger(PdfStream.Keys.Length, fontData.Length);
            CreateStream(fontData);

            // PDF/A-1 requires a CIDSet for an embedded CID font subset (ISO 19005-1 6.3.5); validators refused the file
            // without one. CID equals glyph index here. A subset holds exactly the glyphs of the closure that
            // CreateFontSubset completed; a complete font file holds all of its glyphs. An opened file counts with the
            // PDF/A part its XMP claims, so that text added to it keeps it conforming.
            int part = Owner.PdfAManager is { IsPdfADocument: true } pdfa ? pdfa.Part : Owner.ClaimedPdfAPart;
            if ((cidFont || pdfFontDescriptor.UsedByCidFont) && part == 1)
            {
                var face = pdfFontDescriptor.Descriptor.FontFace;
                var glyphs = subSet != face
                    ? cmapInfo.GlyphIndices.Keys
                    : Enumerable.Range(0, face.maxp.numGlyphs).Select(index => (ushort)index);
                AddCidSet(pdfFontDescriptor, glyphs);
            }
        }

        void AddCidSet(PdfFontDescriptor pdfFontDescriptor, IEnumerable<ushort> glyphs)
        {
            var indices = glyphs.ToArray();
            var bits = new byte[indices.Length == 0 ? 1 : indices.Max() / 8 + 1];
            foreach (var index in indices)
                bits[index >> 3] |= (byte)(0x80 >> (index & 7));

            var cidSet = new PdfDictionary(Owner);
            Owner.Internals.AddObject(cidSet);
            if (!Owner.Options.NoCompression)
            {
                bits = Filtering.FlateDecode.Encode(bits, Document.Options.FlateEncodeMode);
                cidSet.Elements.SetName(PdfStream.Keys.Filter, "/FlateDecode");
            }
            cidSet.Elements.SetInteger(PdfStream.Keys.Length, bits.Length);
            cidSet.CreateStream(bits);
            pdfFontDescriptor.Elements[PdfFontDescriptor.Keys.CIDSet] = cidSet.RequiredReference;
        }
    }
}
