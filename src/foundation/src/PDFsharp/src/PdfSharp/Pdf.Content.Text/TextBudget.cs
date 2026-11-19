// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf.Content.Text
{
    /// <summary>
    /// One page's budget of bytes. A stream costs its own data and every filter stage's output, as in the reader's
    /// own limit, each time it is read. The extracted text costs two bytes per character, because one code can map
    /// to a long ToUnicode string, and each font and CMap a fixed size for its tables, because many fonts can share
    /// one large /W array or CMap stream.
    /// </summary>
    sealed class TextBudget
    {
        /// <summary>A loaded font: its 256-entry code tables or a code space table.</summary>
        internal const int FontBytes = 16 * 1024;
        /// <summary>A parsed CMap stream: its code space tables.</summary>
        internal const int CMapBytes = 8 * 1024;
        /// <summary>One entry of a /W array.</summary>
        internal const int WidthEntryBytes = 32;

        long _left;

        internal TextBudget(long limit)
        {
            _left = limit;
        }

        /// <summary>
        /// The decoded bytes of a stream. A stream whose filters fail on its data is empty: it draws nothing, and the
        /// page's other streams are still read. A filter FreePDF does not implement throws NotImplementedException.
        /// </summary>
        internal byte[] Decode(PdfDictionary stream)
        {
            var data = stream.Stream?.Value ?? Array.Empty<byte>();
            var bytes = data;
            long spent = data.Length;
            var filter = TextItems.Resolve(stream.Elements["/Filter"]);
            if (filter != null && filter is not PdfNull && filter is not PdfNullObject)
            {
                var limit = new DecodeLimit(_left, _left);
                try
                {
                    bytes = Filtering.Decode(data, filter, stream.Elements["/DecodeParms"], limit);
                }
                catch (PdfReaderException ex) when (Parser.IsLimitExceeded(ex))
                {
                    throw Exceeded();
                }
                catch (Exception ex) when (ex is not (OutOfMemoryException or NotImplementedException or NotSupportedException))
                {
                    bytes = Array.Empty<byte>();
                }
                spent = Math.Max(spent, Math.Max(limit.Spent, bytes.Length));
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
