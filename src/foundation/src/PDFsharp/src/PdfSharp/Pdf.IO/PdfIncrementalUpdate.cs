// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Internal;

namespace PdfSharp.Pdf.IO
{
    /// <summary>
    /// Saves a document opened with <see cref="PdfDocumentOpenMode.Append"/> as an incremental update
    /// (ISO 32000-1, 7.5.6): the original bytes unchanged, then the new and changed objects, a cross-reference
    /// section for them, and a trailer that points back to the newest original one.
    /// </summary>
    sealed class PdfIncrementalUpdate
    {
        internal PdfIncrementalUpdate(byte[] original, long startXRef, bool crossReferenceStream, int size, int headerVersion)
        {
            _original = original;
            _startXRef = startXRef;
            _crossReferenceStream = crossReferenceStream;
            _size = size;
            _headerVersion = headerVersion;
        }

        /// <summary>
        /// Records how each object of the file that is reachable from the trailer would be written now. Saving
        /// writes the objects whose output differs, so changes made by the open-time preparation itself are not
        /// written unless something else changes the object.
        /// </summary>
        internal void RecordOriginalObjects(PdfDocument document, HashSet<PdfObjectID> fileObjects)
        {
            foreach (var iref in document.IrefTable.TransitiveClosure(document.Trailer))
            {
                if (iref.Value is { } value && fileObjects.Contains(iref.ObjectID))
                    _hashes[iref.ObjectID] = TryHash(document, value);
            }
        }

        /// <summary>
        /// Records whether the document information was changed since the file was opened. Call before
        /// PrepareForSave, which puts the producer into it.
        /// </summary>
        internal void RecordDocumentInformationChange(PdfDocument document)
        {
            _documentInformationChanged = false;
            if (document.Trailer.Elements[PdfTrailer.Keys.Info] is not PdfReference { Value: { } info } reference)
                return;
            if (_hashes.TryGetValue(reference.ObjectID, out var before))
            {
                var now = TryHash(document, info);
                _documentInformationChanged = now != null && (before == null || !Same(before, now));
            }
            else if (info is PdfDictionary dictionary)
            {
                // Created since the file was opened. Reading document.Info creates one with only the default producer.
                _documentInformationChanged = dictionary.Elements.KeyNames.Any(key => key.Value != PdfDocumentInformation.Keys.Producer)
                    || dictionary.Elements.GetString(PdfDocumentInformation.Keys.Producer) != document.DefaultProducer;
            }
        }

        /// <summary>
        /// Writes the original bytes and the update. Call after PrepareForSave.
        /// </summary>
        internal void Write(PdfDocument document, PdfWriter writer)
        {
            // The header cannot change in an update. The catalog's /Version raises the version instead.
            int version = document.Version;
            if (version > _headerVersion)
            {
                var name = Invariant($"/{version / 10}.{version % 10}");
                if (String.CompareOrdinal(document.Catalog.Elements.GetName(PdfCatalog.Keys.Version), name) < 0)
                    document.Catalog.Elements.SetName(PdfCatalog.Keys.Version, name);
            }

            var reachable = document.IrefTable.TransitiveClosure(document.Trailer);
            Array.Sort(reachable, PdfReference.Comparer);
            var written = new List<PdfReference>();
            foreach (var iref in reachable)
            {
                if (iref.Value is not { } value)
                    continue;
                if (!_hashes.TryGetValue(iref.ObjectID, out var before))
                {
                    written.Add(iref); // New since the file was opened.
                    continue;
                }
                var now = TryHash(document, value);
                // An object that cannot be written now is kept as it is in the file.
                if (now != null && (before == null || !Same(before, now)))
                    written.Add(iref);
            }

            var stream = writer.Stream;
            stream.Write(_original, 0, _original.Length);

            // When nothing changed but what PrepareForSave puts into the document information (the producer), the
            // file is saved as it was. Otherwise the update also gets a new modification date.
            var info = document.Trailer.Elements[PdfTrailer.Keys.Info] as PdfReference;
            if (!_documentInformationChanged && written.All(iref => info != null && iref.ObjectID == info.ObjectID))
                return;
            document.Info.ModificationDate = DateTimeOffset.Now;
            if (info != null && written.All(iref => iref.ObjectID != info.ObjectID))
            {
                written.Add(info);
                written.Sort(PdfReference.Comparer);
            }

            if (_original.Length > 0 && _original[_original.Length - 1] is not ((byte)'\n' or (byte)'\r'))
                writer.WriteRaw('\n');

            foreach (var iref in written)
            {
                iref.Position = writer.Position;
                iref.Value.WriteObject(writer);
            }

            int highest = Math.Max(_size - 1, document.IrefTable.MaxObjectNumber);
            foreach (var iref in written)
                highest = Math.Max(highest, iref.ObjectNumber);
            var entries = written.Select(iref => new Entry(iref.ObjectNumber, iref.GenerationNumber, iref.Position)).ToList();

            if (_crossReferenceStream)
                WriteCrossReferenceStream(document, writer, entries, highest + 1);
            else
                WriteCrossReferenceTable(document, writer, entries, highest);
        }

        void WriteCrossReferenceTable(PdfDocument document, PdfWriter writer, List<Entry> entries, int highest)
        {
            SizeType startXRef = writer.Position;
            var text = new StringBuilder("xref\n");
            foreach (var run in Runs(entries))
            {
                text.Append(Invariant($"{run[0].Number} {run.Count}\n"));
                foreach (var entry in run)
                    text.Append(Invariant($"{entry.Position:0000000000} {entry.Generation:00000} n \n"));
            }
            text.Append(Invariant($"trailer\n<< /Size {highest + 1} "));
            writer.WriteRaw(text.ToString());
            WriteTrailerEntries(document, writer);
            writer.WriteRaw(" >>\n");
            writer.WriteEof(document, startXRef);
        }

        void WriteCrossReferenceStream(PdfDocument document, PdfWriter writer, List<Entry> entries, int number)
        {
            // The stream has an entry for itself (ISO 32000-1, 7.5.8.3).
            SizeType position = writer.Position;
            entries.Add(new Entry(number, 0, position));
            int offsetWidth = position > UInt32.MaxValue ? 8 : 4;
            var data = new MemoryStream();
            var index = new StringBuilder();
            foreach (var run in Runs(entries))
            {
                if (index.Length > 0)
                    index.Append(' ');
                index.Append(Invariant($"{run[0].Number} {run.Count}"));
                foreach (var entry in run)
                {
                    data.WriteByte(1);
                    for (int shift = 8 * (offsetWidth - 1); shift >= 0; shift -= 8)
                        data.WriteByte((byte)((long)entry.Position >> shift));
                    data.WriteByte((byte)(entry.Generation >> 8));
                    data.WriteByte((byte)entry.Generation);
                }
            }
            var bytes = data.ToArray();
            writer.WriteRaw(Invariant($"{number} 0 obj\n<< /Type /XRef /Size {number + 1} /W [1 {offsetWidth} 2] /Index [{index}] "));
            WriteTrailerEntries(document, writer);
            writer.WriteRaw(Invariant($" /Length {bytes.Length} >>\nstream\n"));
            writer.Stream.Write(bytes, 0, bytes.Length);
            writer.WriteRaw("\nendstream\nendobj\n");
            writer.WriteEof(document, position);
        }

        // /Root, /Info, /ID and /Prev; the caller writes /Size.
        void WriteTrailerEntries(PdfDocument document, PdfWriter writer)
        {
            var trailer = document.Trailer;
            writer.WriteRaw(Invariant($"/Prev {_startXRef}"));
            WriteReference(writer, "/Root", trailer.Elements[PdfTrailer.Keys.Root]);
            WriteReference(writer, "/Info", trailer.Elements[PdfTrailer.Keys.Info]);

            // The first identifier stays; the second one is new for this revision (ISO 32000-1, 14.4).
            var second = NewIdentifier();
            var first = (trailer.Elements.GetArray(PdfTrailer.Keys.ID)?.Elements.Count == 2
                ? trailer.Elements.GetArray(PdfTrailer.Keys.ID)!.Elements[0] as PdfString
                : null) ?? NewIdentifier();
            writer.WriteRaw(" /ID [");
            first.WriteObject(writer);
            second.WriteObject(writer);
            writer.WriteRaw("]");
        }

        static void WriteReference(PdfWriter writer, string key, PdfItem? item)
        {
            if (item is PdfReference reference)
                writer.WriteRaw(Invariant($" {key} {reference.ObjectNumber} {reference.GenerationNumber} R"));
        }

        static PdfString NewIdentifier()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            return new PdfString(PdfEncoders.RawEncoding.GetString(bytes, 0, bytes.Length), PdfStringFlags.HexLiteral);
        }

        // Consecutive object numbers form one subsection.
        static IEnumerable<List<Entry>> Runs(List<Entry> entries)
        {
            List<Entry>? run = null;
            foreach (var entry in entries.OrderBy(entry => entry.Number))
            {
                if (run != null && entry.Number == run[run.Count - 1].Number + 1)
                {
                    run.Add(entry);
                    continue;
                }
                if (run != null)
                    yield return run;
                run = [entry];
            }
            if (run != null)
                yield return run;
        }

        // The object as WriteObject writes it, in compact layout so that layout comments cannot differ.
        static byte[]? TryHash(PdfDocument document, PdfObject value)
        {
            try
            {
                using var buffer = new MemoryStream();
                var writer = new PdfWriter(buffer, document, null) { Layout = PdfWriterLayout.Compact };
                value.WriteObject(writer);
                using var sha = SHA256.Create();
                return sha.ComputeHash(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        }

        static bool Same(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
                return false;
            for (int index = 0; index < first.Length; index++)
            {
                if (first[index] != second[index])
                    return false;
            }
            return true;
        }

        readonly record struct Entry(int Number, int Generation, SizeType Position);

        readonly byte[] _original;
        readonly long _startXRef;
        readonly bool _crossReferenceStream;
        readonly int _size;
        readonly int _headerVersion;
        readonly Dictionary<PdfObjectID, byte[]?> _hashes = new();
        bool _documentInformationChanged;
    }
}
