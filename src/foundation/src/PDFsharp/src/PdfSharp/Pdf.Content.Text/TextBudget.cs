// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// One page's budget of bytes: every filter stage's output counts, as in the reader's own limit, and so does the
    /// extracted text at two bytes per character, because one code can map to a long ToUnicode string.
    /// </summary>
    sealed class TextBudget
    {
        long _left;

        internal TextBudget(long limit)
        {
            _left = limit;
        }

        /// <summary>
        /// The decoded bytes of a stream. A stream whose filters fail on its data is empty: it draws nothing, and the
        /// page's other streams are still read.
        /// </summary>
        internal byte[] Decode(PdfDictionary stream)
        {
            var bytes = stream.Stream?.Value ?? Array.Empty<byte>();
            long spent = bytes.Length;
            var filter = TextItems.Resolve(stream.Elements["/Filter"]);
            if (filter != null && filter is not PdfNull && filter is not PdfNullObject)
            {
                var limit = new DecodeLimit(_left, _left);
                try
                {
                    bytes = Filtering.Decode(bytes, filter, stream.Elements["/DecodeParms"], limit);
                }
                catch (PdfReaderException ex) when (Parser.IsLimitExceeded(ex))
                {
                    throw Exceeded();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    bytes = Array.Empty<byte>();
                }
                spent = Math.Max(limit.Spent, bytes.Length);
            }
            Spend(spent);
            return bytes;
        }

        internal void Spend(long bytes)
        {
            if (bytes > _left)
                throw Exceeded();
            _left -= bytes;
        }

        static PdfReaderException Exceeded() => Parser.LimitExceeded("Text extraction would decode more than MaxDecodedBytes.");
    }
}
