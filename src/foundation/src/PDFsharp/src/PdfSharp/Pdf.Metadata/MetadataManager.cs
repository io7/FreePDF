// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PdfSharp.Events;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Internal;
using Microsoft.Extensions.Logging;
using PdfSharp.Logging;

// v7.0.0 Review

namespace PdfSharp.Pdf.Metadata
{
    /// <summary>
    /// The MetadataManager provides functionality for easier handling of PDF metadata.
    /// </summary>
    public class MetadataManager : ManagerBase
    {
        /// <summary>
        /// Initializes a new instance of this class.
        /// </summary>
        MetadataManager(PdfDocument document) : base(document)
        { }

        /// <summary>
        /// Gets the PDF metadata object from the catalog, or null,
        /// if no such object exists.
        /// </summary>
        public PdfMetadata? GetMetadata()
        {
            return Document.Catalog.GetMetadata();
        }

        /// <summary>
        /// Gets or sets the strategy how PDFsharp treads document metadata.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">DocumentMetadataStrategy was set more than once.</exception>
        public DocumentMetadataStrategy Strategy
        {
            get => _strategy ?? DocumentMetadataStrategy.NoMetadata;
            set
            {
                if (_strategy == value)
                    return;

                if (_strategy != null)
                {
                    // Update of strategy set by PdfAManager is allowed.
                    if (!(_strategy == DocumentMetadataStrategy.AutoGenerate && value == DocumentMetadataStrategy.UserGenerated))
                        throw new InvalidOperationException("DocumentMetadataStrategy can only be set once.");
                }
                _strategy = value;
            }
        }
        DocumentMetadataStrategy? _strategy;

        /// <summary>
        /// Adjusts the document metadata strategy for PDF/A generation if a PDF/A format is set.
        /// </summary>
        internal void AdjustStrategyForPdfA() => AdjustStrategyForStandard("PDF/A");

        /// <summary>
        /// Makes sure XMP metadata is written for a standard that requires it (PDF/A, PDF/UA).
        /// </summary>
        internal void AdjustStrategyForStandard(string standard)
        {
            _strategy = _strategy switch
            {
                DocumentMetadataStrategy.KeepExisting => Fail(),
                DocumentMetadataStrategy.NoMetadata => Fail(),
                DocumentMetadataStrategy.AutoGenerate => DocumentMetadataStrategy.AutoGenerate,
                DocumentMetadataStrategy.UserGenerated => DocumentMetadataStrategy.UserGenerated,
                null or DocumentMetadataStrategy.NoMetadata => _strategy = DocumentMetadataStrategy.AutoGenerate,
                _ => throw new ArgumentOutOfRangeException(nameof(_strategy))
            };
            return;

            //[DoesNotReturn]
            DocumentMetadataStrategy Fail()
            {
                throw new InvalidOperationException(
                    $"With a document metadata strategy of '{_strategy}' the document cannot be a {standard} document.");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="xml"></param>
        public void SetMetadata(byte[] xml)
        {
            Document.Catalog.GetOrCreateMetadata().SetMetadata(xml);
        }

        /// <summary>
        /// Gets the metadata info PDFsharp has collected for the current document.
        /// This includes PdfDocumentInformation and the document IDs.
        /// </summary>
        public DocumentMetadataInfo GetMetadataInfo()
        {
            var md = new DocumentMetadataInfo(Document);
            return md;
        }

        internal void PrepareForSave()
        {
            var catalog = Document.Catalog;

            // If HasMetadata is true, but XML stream is empty, then remove the inconsistent Metadata.
            // We always remove empty Metadata here, regardless of the strategy.
            //if (catalog.HasMetadata && !(catalog.GetMetadata(false)!.Stream?.Value?.Length > 0))
            if (!(catalog.GetMetadata()?.Stream?.Value.Length > 0))
                catalog.Elements.Remove(PdfCatalog.Keys.Metadata);

            if (_strategy is DocumentMetadataStrategy.KeepExisting)
                return;

            // An incremental update leaves existing metadata alone; changing it could break earlier signatures.
            bool canUpdate = Document.IncrementalUpdate == null;
            if (_strategy is null)
            {
                // A file whose XMP claims PDF/A must keep XMP and document information in agreement, or a save that
                // changes the information (Modify mode sets /ModDate) breaks its conformance (upstream #166).
                if (canUpdate && catalog.GetMetadata() is { Stream.Value.Length: > 0 } existing)
                    UpdateClaimedPdfA(existing);
                return;
            }

            if (_strategy == DocumentMetadataStrategy.NoMetadata)
            {
                // Remove Metadata.
                catalog.Elements.Remove(PdfCatalog.Keys.Metadata);
                return;
            }

            var metadata = catalog.GetOrCreateMetadata();
            if (_strategy == DocumentMetadataStrategy.AutoGenerate)
            {
                // Create default Metadata if it is currently empty.
                if (!(catalog.GetMetadata()?.Stream?.Value.Length > 0))  // TODO PdfDictionary.StreamLength would be helpful.
                {
                    var xml = metadata.CreateDefaultMetadata();
                    metadata.SetMetadata(xml);
                }
                else if (canUpdate && Document.PdfAManager is { IsPdfADocument: true } pdfaManager)
                {
                    // SetPdfA on a file with its own XMP: the packet is kept, but it must identify the file as PDF/A
                    // and agree with the document information (upstream #356). A packet that is not XML is replaced.
                    var updated = ReadPacket(metadata) is { } packet
                        ? XmpPacketUpdater.Update(packet, new DocumentMetadataInfo(Document), pdfaManager.Format, HasInfoEntry)
                        : null;
                    if (updated == null)
                        PdfSharpLogHost.Logger.LogWarning("The document's XMP metadata cannot be read; it is replaced by generated metadata.");
                    metadata.SetMetadata(updated ?? metadata.CreateDefaultMetadata());
                }
                else if (canUpdate)
                {
                    // AutoGenerate set by the caller, or by PDF/UA, on an opened file whose XMP claims PDF/A.
                    UpdateClaimedPdfA(metadata);
                }
                return;
            }

            if (_strategy == DocumentMetadataStrategy.UserGenerated)
            {
                var metadataInfo = new DocumentMetadataInfo(Document);
                var metadataArgs = new DocumentMetadataEventArgs(Document)
                {
                    Metadata = metadata,
                    Info = metadataInfo
                };
                Document.Events.OnCreateDocumentMetadata(Document, metadataArgs);
            }
        }

        // A file whose XMP claims PDF/A must keep XMP and document information in agreement, or a save that changes
        // the information (Modify mode sets /ModDate) breaks its conformance (upstream #166). The PDF/A
        // identification stays as it is; a packet that cannot be updated is kept.
        void UpdateClaimedPdfA(PdfMetadata metadata)
        {
            if (ReadPacket(metadata) is { } packet && XmpPacketUpdater.ClaimedPdfAPart(packet) > 0
                && XmpPacketUpdater.Update(packet, new DocumentMetadataInfo(Document), null, HasInfoEntry) is { } updated)
            {
                metadata.SetMetadata(updated);
            }
        }

        bool HasInfoEntry(string key) => Document.Info.Elements.ContainsKey(key);

        // The packet as text: UTF-8, or UTF-16 with a byte order mark, as XMP in PDF allows; null if it is empty or
        // its stream cannot be decoded (a damaged or unknown filter), so that such a file still saves.
        internal static string? ReadPacket(PdfMetadata metadata)
        {
            byte[]? bytes;
            try
            {
                bytes = metadata.Stream?.UnfilteredValue;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
            if (bytes == null || bytes.Length == 0)
                return null;
            if (bytes.Length >= 2 && (bytes[0] == 0xFE && bytes[1] == 0xFF || bytes[0] == 0xFF && bytes[1] == 0xFE))
                return (bytes[0] == 0xFE ? System.Text.Encoding.BigEndianUnicode : System.Text.Encoding.Unicode).GetString(bytes, 2, bytes.Length - 2);
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return System.Text.Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }

        /// <summary>
        /// Converts a DateTimeOffset in an XMP metadata compatible string.
        /// </summary>
        internal static string ToXmpDateString(DateTimeOffset? dateTimeOffset)
        {
            var result = dateTimeOffset == null
                ? ""
                : Invariant($"{dateTimeOffset.Value:yyyy-MM-ddTHH:mm:ssK}");
            return result;
        }

        /// <summary>
        /// Gets or creates the MetadataManager for the specified document.
        /// </summary>
        public static MetadataManager ForDocument(PdfDocument document)
            => document.MetadataManager ??= new(document);
    }
}
