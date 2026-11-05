// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Globalization;
using System.Xml;
using PdfSharp.Pdf.PdfA;

namespace PdfSharp.Pdf.Metadata
{
    /// <summary>
    /// Updates an existing XMP packet in place: it sets the properties that mirror the document information dictionary
    /// and, when asked, the PDF/A identification, and keeps everything else the packet holds, such as extension schemas.
    /// PDF/A requires XMP and document information to agree, and a PDF/A file must identify itself in its XMP.
    /// </summary>
    static class XmpPacketUpdater
    {
        const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        const string Dc = "http://purl.org/dc/elements/1.1/";
        const string Pdf = "http://ns.adobe.com/pdf/1.3/";
        const string Xmp = "http://ns.adobe.com/xap/1.0/";
        const string PdfAId = "http://www.aiim.org/pdfa/ns/id/";
        const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

        /// <summary>
        /// The PDF/A part the packet identifies the file with (pdfaid:part, as an element or an attribute), or 0.
        /// </summary>
        internal static int ClaimedPdfAPart(string packet)
        {
            if (!packet.Contains(PdfAId) || Load(packet, out _, out _) is not { } document)
                return 0;
            var values = document.GetElementsByTagName("part", PdfAId).Cast<XmlElement>().Select(element => element.InnerText)
                .Concat(document.GetElementsByTagName("Description", Rdf).Cast<XmlElement>()
                    .Select(description => description.GetAttribute("part", PdfAId)));
            foreach (var value in values)
            {
                if (Int32.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var part) && part > 0)
                    return part;
            }
            return 0;
        }

        // Splits the packet into its xpacket header and trailer and parses the XML between them (DTDs refused);
        // null if that is not XML.
        static XmlDocument? Load(string packet, out string header, out string trailer)
        {
            int headerEnd = 0;
            header = "<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>";
            int begin = packet.IndexOf("<?xpacket", StringComparison.Ordinal);
            if (begin >= 0 && packet.IndexOf("begin=", begin, StringComparison.Ordinal) is var attribute and >= 0
                && packet.IndexOf("?>", begin, StringComparison.Ordinal) is var close and >= 0 && attribute < close)
            {
                headerEnd = close + 2;
                header = packet.Substring(0, headerEnd);
            }
            trailer = "<?xpacket end=\"w\"?>";
            int trailerStart = packet.LastIndexOf("<?xpacket end", StringComparison.Ordinal);
            if (trailerStart >= headerEnd)
                trailer = packet.Substring(trailerStart);
            else
                trailerStart = packet.Length;
            var body = packet.Substring(headerEnd, trailerStart - headerEnd);

            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = XmlReader.Create(new StringReader(body), settings);
                document.Load(reader);
                return document;
            }
            catch (XmlException)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the updated packet, or null if the packet is not XML with an rdf:RDF element. Properties whose
        /// document information value is empty are left as they are.
        /// </summary>
        internal static string? Update(string packet, DocumentMetadataInfo info, PdfAFormat? pdfa)
        {
            if (Load(packet, out var header, out var trailer) is not { } document)
                return null;
            if (document.GetElementsByTagName("RDF", Rdf).Cast<XmlElement>().FirstOrDefault() is not { } rdf)
                return null;

            var updater = new Updater(document, rdf);
            if (pdfa is { } format)
            {
                updater.SetSimple(PdfAId, "pdfaid", "part", format.Part.ToString(CultureInfo.InvariantCulture));
                if (format.ConformanceLevel != ' ')
                    updater.SetSimple(PdfAId, "pdfaid", "conformance", format.ConformanceLevel.ToString());
            }
            updater.SetAlternative(Dc, "dc", "title", info.Title);
            updater.SetSequence(Dc, "dc", "creator", info.Author);
            updater.SetAlternative(Dc, "dc", "description", info.Subject);
            updater.SetSimple(Pdf, "pdf", "Keywords", info.Keywords);
            updater.SetSimple(Pdf, "pdf", "Producer", info.Producer);
            updater.SetSimple(Xmp, "xmp", "CreatorTool", info.Creator);
            updater.SetSimple(Xmp, "xmp", "CreateDate", info.CreationDate);
            updater.SetSimple(Xmp, "xmp", "ModifyDate", info.ModificationDate);
            updater.SetSimple(Xmp, "xmp", "MetadataDate",
                String.IsNullOrEmpty(info.ModificationDate) ? info.CreationDate : info.ModificationDate);

            var output = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
                document.Save(writer);
            return header + output + trailer;
        }

        sealed class Updater(XmlDocument document, XmlElement rdf)
        {
            readonly string _about = rdf.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(element => element.LocalName == "Description" && element.NamespaceURI == Rdf)
                ?.GetAttribute("about", Rdf) ?? "";
            readonly Dictionary<string, XmlElement> _added = new();

            IEnumerable<XmlElement> Descriptions => rdf.ChildNodes.OfType<XmlElement>()
                .Where(element => element.LocalName == "Description" && element.NamespaceURI == Rdf);

            // Removes the property everywhere it occurs, as an element or as an attribute; returns the first element.
            XmlElement? TakeFirst(string ns, string name)
            {
                XmlElement? first = null;
                foreach (var description in Descriptions.ToList())
                {
                    if (description.GetAttributeNode(name, ns) is { } attribute)
                        description.Attributes.Remove(attribute);
                    foreach (var element in description.ChildNodes.OfType<XmlElement>()
                                 .Where(element => element.LocalName == name && element.NamespaceURI == ns).ToList())
                    {
                        if (first == null)
                            first = element;
                        else
                            description.RemoveChild(element);
                    }
                }
                return first;
            }

            XmlElement Property(string ns, string prefix, string name)
            {
                if (TakeFirst(ns, name) is { } existing)
                    return existing;
                if (!_added.TryGetValue(ns, out var description))
                {
                    description = document.CreateElement("rdf", "Description", Rdf);
                    description.SetAttribute("about", Rdf, _about);
                    rdf.AppendChild(description);
                    _added[ns] = description;
                }
                var property = document.CreateElement(prefix, name, ns);
                description.AppendChild(property);
                return property;
            }

            public void SetSimple(string ns, string prefix, string name, string value)
            {
                if (String.IsNullOrEmpty(value))
                    return;
                var property = Property(ns, prefix, name);
                property.RemoveAll();
                property.InnerText = value;
            }

            // A language alternative: the x-default entry gets the value; other languages are kept.
            public void SetAlternative(string ns, string prefix, string name, string value)
            {
                if (String.IsNullOrEmpty(value))
                    return;
                var property = Property(ns, prefix, name);
                var alternative = property.ChildNodes.OfType<XmlElement>().FirstOrDefault(element => element.LocalName == "Alt" && element.NamespaceURI == Rdf);
                if (alternative == null)
                {
                    property.RemoveAll();
                    alternative = (XmlElement)property.AppendChild(document.CreateElement("rdf", "Alt", Rdf))!;
                }
                var item = alternative.ChildNodes.OfType<XmlElement>().FirstOrDefault(element =>
                    element.LocalName == "li" && element.NamespaceURI == Rdf && element.GetAttribute("lang", XmlNamespace) == "x-default");
                if (item == null)
                {
                    item = document.CreateElement("rdf", "li", Rdf);
                    var language = document.CreateAttribute("xml", "lang", XmlNamespace);
                    language.Value = "x-default";
                    item.Attributes.Append(language);
                    alternative.PrependChild(item);
                }
                item.InnerText = value;
            }

            // An ordered list with the value as its only entry, as PDF/A expects for dc:creator.
            public void SetSequence(string ns, string prefix, string name, string value)
            {
                if (String.IsNullOrEmpty(value))
                    return;
                var property = Property(ns, prefix, name);
                property.RemoveAll();
                var sequence = property.AppendChild(document.CreateElement("rdf", "Seq", Rdf))!;
                sequence.AppendChild(document.CreateElement("rdf", "li", Rdf))!.InnerText = value;
            }
        }
    }
}
