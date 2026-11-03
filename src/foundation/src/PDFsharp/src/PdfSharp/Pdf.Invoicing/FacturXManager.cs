// FreePDF - MIT licence. See the LICENSE file in the solution root.

using System.Xml;
using PdfSharp.Events;
using PdfSharp.Pdf.Attachments;
using PdfSharp.Pdf.Internal;
using PdfSharp.Pdf.Metadata;
using PdfSharp.Pdf.PdfA;

namespace PdfSharp.Pdf.Invoicing
{
    /// <summary>
    /// Makes a document a Factur-X / ZUGFeRD hybrid invoice: a PDF/A-3 file with the invoice XML (UN/CEFACT Cross
    /// Industry Invoice) embedded, linked from the catalog's associated files, and declared in the XMP metadata with
    /// the Factur-X PDF/A extension schema.
    /// FreePDF checks that the XML is a well-formed Cross Industry Invoice. It does not check the invoice content
    /// against EN 16931 or a profile's business rules; validate the XML with the tools of your e-invoicing provider.
    /// </summary>
    public sealed class FacturXManager : ManagerBase
    {
        /// <summary>The XMP namespace of the Factur-X extension schema (Factur-X 1.0, ZUGFeRD 2.x).</summary>
        public const string XmpNamespace = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";

        const string CrossIndustryInvoiceNamespace = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";

        FacturXManager(PdfDocument document) : base(document) { }

        /// <summary>
        /// Gets or creates the FacturXManager for the specified document.
        /// </summary>
        public static FacturXManager ForDocument(PdfDocument document)
            => document.FacturXManager ??= new(document);

        /// <summary>
        /// Gets the profile of the embedded invoice, or null if no invoice is set.
        /// </summary>
        public FacturXProfile? Profile { get; private set; }

        /// <summary>
        /// Gets the name under which the invoice XML is embedded: factur-x.xml, or xrechnung.xml for XRechnung.
        /// </summary>
        public string FileName => Profile == FacturXProfile.XRechnung ? "xrechnung.xml" : "factur-x.xml";

        /// <summary>
        /// Embeds the invoice XML and declares the document a Factur-X invoice of the specified profile.
        /// Call it before adding pages unless a PDF/A-3 format is already set: without one, the document becomes
        /// PDF/A-3B, and FreePDF sets PDF/A formats only before content exists.
        /// </summary>
        /// <param name="xml">The UN/CEFACT Cross Industry Invoice XML, UTF-8 encoded.</param>
        /// <param name="profile">The profile the XML conforms to.</param>
        /// <param name="relationship">The associated-file relationship (see <see cref="PdfAFRelationship"/>).
        /// By default Data for MINIMUM and BASIC WL and Alternative for the other profiles; national rules may
        /// ask for Source or Data instead.</param>
        public void SetInvoice(byte[] xml, FacturXProfile profile, string? relationship = null)
        {
            if (xml == null!)
                throw new ArgumentNullException(nameof(xml));
            if (Profile != null)
                throw new InvalidOperationException("The document already has a Factur-X invoice.");
            EnsureCrossIndustryInvoice(xml);
            EnsurePdfA3();

            Profile = profile;
            relationship ??= profile is FacturXProfile.Minimum or FacturXProfile.BasicWL
                ? PdfAFRelationship.Data
                : PdfAFRelationship.Alternative;
            var now = DateTimeOffset.Now;
            EmbeddedFilesManager.ForDocument(Document).AddFile(new EmbeddedFileInfo
            {
                NamesKey = FileName,
                FileName = FileName,
                FileType = "text/xml",
                Description = profile == FacturXProfile.XRechnung ? "XRechnung invoice" : "Factur-X invoice",
                Data = xml,
                AFRelationship = relationship,
                CreationTime = now,
                ModificationTime = now,
            });

            // The XMP metadata is created at save; add the Factur-X declaration to it then.
            MetadataManager.ForDocument(Document).Strategy = DocumentMetadataStrategy.UserGenerated;
            Document.Events.CreateDocumentMetadata += AddFacturXMetadata;
        }

        static void EnsureCrossIndustryInvoice(byte[] xml)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            try
            {
                using var reader = XmlReader.Create(new MemoryStream(xml), settings);
                reader.MoveToContent();
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "CrossIndustryInvoice" || reader.NamespaceURI != CrossIndustryInvoiceNamespace)
                    throw new ArgumentException($"The invoice XML must be a UN/CEFACT Cross Industry Invoice ({{{CrossIndustryInvoiceNamespace}}}CrossIndustryInvoice).", nameof(xml));
                while (reader.Read()) { }
            }
            catch (XmlException ex)
            {
                throw new ArgumentException("The invoice XML is not well-formed: " + ex.Message, nameof(xml), ex);
            }
        }

        void EnsurePdfA3()
        {
            var pdfA = PdfAManager.ForDocument(Document);
            if (pdfA.IsPdfADocument)
            {
                if (pdfA.Format.Part != 3)
                    throw new InvalidOperationException($"Factur-X requires PDF/A-3, but the document is {pdfA.Format.FullName}.");
                return;
            }
            if (Document.PageCount > 0)
                throw new InvalidOperationException("Factur-X requires PDF/A-3. Call SetInvoice, or set a PDF/A-3 format with PdfAManager, before adding pages.");
            pdfA.SetFormat(PdfAFormats.PdfA_3b);
        }

        void AddFacturXMetadata(object sender, DocumentMetadataEventArgs e)
        {
            var xml = e.Metadata.CreateDefaultMetadata();
            var end = xml.IndexOf("</rdf:RDF>", StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException("The default XMP metadata has no rdf:RDF element.");

            // XMP allows a property only once per resource. If the default metadata already declares extension
            // schemas (PDF/UA does), the Factur-X schema joins that list instead of adding a second one.
            var schemas = xml.IndexOf("<pdfaExtension:schemas>", StringComparison.Ordinal);
            var bagEnd = schemas < 0 ? -1 : xml.IndexOf("</rdf:Bag>", schemas, StringComparison.Ordinal);
            if (bagEnd >= 0 && bagEnd < end)
                xml = xml[..bagEnd] + SchemaEntry + xml[bagEnd..end] + FacturXProperties() + xml[end..];
            else
                xml = xml[..end] + ExtensionSchemas + FacturXProperties() + xml[end..];
            e.Metadata.SetMetadata(xml);
        }

        string ConformanceLevel => Profile switch
        {
            FacturXProfile.Minimum => "MINIMUM",
            FacturXProfile.BasicWL => "BASIC WL",
            FacturXProfile.Basic => "BASIC",
            FacturXProfile.EN16931 => "EN 16931",
            FacturXProfile.Extended => "EXTENDED",
            FacturXProfile.XRechnung => "XRECHNUNG",
            _ => throw new InvalidOperationException("No Factur-X profile is set."),
        };

        // The PDF/A extension schema entry declaring the fx properties (PDF/A only allows declared custom XMP
        // properties).
        const string SchemaEntry = $"""
                      <rdf:li rdf:parseType="Resource">
                        <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>
                        <pdfaSchema:namespaceURI>{XmpNamespace}</pdfaSchema:namespaceURI>
                        <pdfaSchema:prefix>fx</pdfaSchema:prefix>
                        <pdfaSchema:property>
                          <rdf:Seq>
                            <rdf:li rdf:parseType="Resource">
                              <pdfaProperty:name>DocumentFileName</pdfaProperty:name>
                              <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                              <pdfaProperty:category>external</pdfaProperty:category>
                              <pdfaProperty:description>The name of the embedded XML document</pdfaProperty:description>
                            </rdf:li>
                            <rdf:li rdf:parseType="Resource">
                              <pdfaProperty:name>DocumentType</pdfaProperty:name>
                              <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                              <pdfaProperty:category>external</pdfaProperty:category>
                              <pdfaProperty:description>The type of the hybrid document in capital letters, e.g. INVOICE or ORDER</pdfaProperty:description>
                            </rdf:li>
                            <rdf:li rdf:parseType="Resource">
                              <pdfaProperty:name>Version</pdfaProperty:name>
                              <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                              <pdfaProperty:category>external</pdfaProperty:category>
                              <pdfaProperty:description>The actual version of the standard applying to the embedded XML document</pdfaProperty:description>
                            </rdf:li>
                            <rdf:li rdf:parseType="Resource">
                              <pdfaProperty:name>ConformanceLevel</pdfaProperty:name>
                              <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                              <pdfaProperty:category>external</pdfaProperty:category>
                              <pdfaProperty:description>The conformance level of the embedded XML document</pdfaProperty:description>
                            </rdf:li>
                          </rdf:Seq>
                        </pdfaSchema:property>
                      </rdf:li>

            """;

        const string ExtensionSchemas = $"""
                <rdf:Description rdf:about="" xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/" xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#" xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
                  <pdfaExtension:schemas>
                    <rdf:Bag>
            {SchemaEntry}        </rdf:Bag>
                  </pdfaExtension:schemas>
                </rdf:Description>

            """;

        string FacturXProperties() => $"""
                <rdf:Description rdf:about="" xmlns:fx="{XmpNamespace}">
                  <fx:DocumentType>INVOICE</fx:DocumentType>
                  <fx:DocumentFileName>{FileName}</fx:DocumentFileName>
                  <fx:Version>1.0</fx:Version>
                  <fx:ConformanceLevel>{ConformanceLevel}</fx:ConformanceLevel>
                </rdf:Description>

            """;
    }
}
