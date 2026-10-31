// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Collections;
using PdfSharp.Internal;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.Internal;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf.Advanced
{
    /// <summary>
    /// Represents an array of PDF content streams of a page.
    /// </summary>
    public sealed class PdfContents : PdfArray
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PdfContents"/> class.
        /// </summary>
        /// <param name="document">The document.</param>
        public PdfContents(PdfDocument document)
            : base(document, false)
        { }

        internal PdfContents(PdfArray array)
            : base(array)
        {
            int count = Elements.Count;
            for (int idx = 0; idx < count; idx++)
            {
                // Convert the references from PdfDictionary to PdfContent
                var item = Elements[idx];
                var iref = item as PdfReference;
                // if (iref != null && iref.Value is PdfDictionary)
                if (iref is { Value: PdfDictionary t1 } t2)
                {
                    // The following line is correct!
                    // ReSharper disable once ObjectCreationAsStatement because assignment is done in constructor.
                    if (iref.Value is not PdfContent)
                        new PdfContent((PdfDictionary)iref.Value);
                }
                else
                    throw new InvalidOperationException("Unexpected item in a content stream array.");
            }
        }

        /// <summary>
        /// Appends a new content stream and returns it.
        /// </summary>
        public PdfContent AppendContent()
        {
            Debug.Assert(Owner != null);

            SetModified();
            PdfContent content = new PdfContent(Owner);
            Debug.Assert(content.Reference != null);
            Elements.Add(content.Reference);
            return content;
        }

        /// <summary>
        /// Prepends a new content stream and returns it.
        /// </summary>
        public PdfContent PrependContent()
        {
            Debug.Assert(Owner != null);

            SetModified();
            PdfContent content = new PdfContent(Owner);
            Debug.Assert(content.Reference != null);
            Elements.Insert(0, content.Reference);
            return content;
        }

        /// <summary>
        /// Creates a single content stream with the bytes from the array of the content streams.
        /// This operation does not modify any of the content streams in this array.
        /// </summary>
        public PdfContent CreateSingleContent()
        {
            byte[] bytes = [];
            foreach (PdfItem iref in Elements)
            {
                PdfDictionary cont = (PdfDictionary)((PdfReference)iref).Value;
                var bytes1 = bytes;
                var bytes2 = cont.Stream!.UnfilteredValue;
                bytes = new byte[bytes1.Length + bytes2.Length + 1];
                bytes1.CopyTo(bytes, 0);
                bytes[bytes1.Length] = (byte)'\n';
                bytes2.CopyTo(bytes, bytes1.Length + 1);
            }
            var content = new PdfContent(Owner);
            content.Stream = new PdfDictionary.PdfStream(bytes, content);
            return content;
        }

        /// <summary>
        /// Replaces the current content of the page with the specified content sequence.
        /// </summary>
        public PdfContent ReplaceContent(CSequence seq)
        {
            if (seq == null)
                throw new ArgumentNullException(nameof(seq));

            var bytes = PdfEncoders.RawEncoding.GetBytes(seq.ToContent());
            return ReplaceContent(bytes);
        }

        /// <summary>
        /// Replaces the current content of the page with the specified bytes.
        /// </summary>
        public PdfContent ReplaceContent(byte[] contentBytes)
        {
            Debug.Assert(Owner != null);

            var content = new PdfContent(Owner);
            content.CreateStream(contentBytes);

            Elements.Clear();
            Elements.Add(content.RequiredReference);

            return content;
        }

        void SetModified()
        {
            if (_modified)
                return;
            _modified = true;
            if (Elements.Count == 0)
                return;

            // Keep the existing streams unchanged: they may be shared by pages,
            // filtered, or end in a comment without a terminating newline.
            // Separate wrapper streams isolate the original graphics state.
            var begin = new PdfContent(Owner);
            begin.CreateStream(new byte[] { (byte)'q', (byte)'\n' });
            var end = new PdfContent(Owner);
            end.CreateStream(new byte[] { (byte)'\n', (byte)'Q', (byte)'\n' });
            Elements.Insert(0, begin.RequiredReference);
            Elements.Add(end.RequiredReference);
        }

        bool _modified;

        internal override void WriteObject(PdfWriter writer)
        {
            // Save two bytes in PDF stream...
            if (Elements.Count == 1)
            {
                var xxx = Elements[0];
                Elements[0].WriteObject(writer);
            }
            else
                base.WriteObject(writer);
        }

        /// <summary>
        /// Gets the enumerator.
        /// </summary>
        public new IEnumerator<PdfContent> GetEnumerator()
        {
            return new PdfPageContentEnumerator(this);
        }

        class PdfPageContentEnumerator : IEnumerator<PdfContent>
        {
            internal PdfPageContentEnumerator(PdfContents list)
            {
                _contents = list;
                _index = -1;
            }

            public bool MoveNext()
            {
                if (_index < _contents.Elements.Count - 1)
                {
                    _index++;
                    _currentElement = (PdfContent)((PdfReference)_contents.Elements[_index]).Value;
                    return true;
                }
                _index = _contents.Elements.Count;
                return false;
            }

            public void Reset()
            {
                _currentElement = null;
                _index = -1;
            }

            object IEnumerator.Current => Current;

            public PdfContent Current
            {
                get
                {
                    if (_index == -1 || _index >= _contents.Elements.Count)
                        throw new InvalidOperationException(PsMsgs.ListEnumCurrentOutOfRange);
                    return _currentElement ?? throw new InvalidOperationException("Current called before MoveNext.");
                }
            }

            public void Dispose()
            {
                // Nothing to do.
            }

            PdfContent? _currentElement;
            int _index;
            readonly PdfContents _contents;
        }
    }
}
