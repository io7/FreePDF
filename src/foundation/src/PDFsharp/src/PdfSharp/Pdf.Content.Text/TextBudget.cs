// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// Decodes streams within one budget of bytes. Every filter stage's output counts, as in the reader's own limit.
    /// </summary>
    sealed class TextBudget
    {
        long _left;

        internal TextBudget(long limit)
        {
            _left = limit;
        }

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
                spent = Math.Max(limit.Spent, bytes.Length);
            }
            if (spent > _left)
                throw Exceeded();
            _left -= spent;
            return bytes;
        }

        static PdfReaderException Exceeded() => Parser.LimitExceeded("Text extraction would decode more than MaxDecodedBytes.");
    }
}
