// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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

        // The header is always written in this form: SetMetadata reads its begin attribute, and headers with text
        // before them, an XML declaration or a mis-encoded byte order mark made the save throw.
        const string Header = "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>";

        static readonly Regex TrailerPattern = new(@"\A<\?xpacket\s+end\s*=\s*([""'])([rw])\1\s*\?>", RegexOptions.CultureInvariant);

        /// <summary>
        /// The PDF/A part the packet identifies the file with, or 0: pdfaid:part as an element or an attribute of a
        /// top-level rdf:Description. A pdfaid:part nested deeper belongs to something else, such as the XMP of a placed
        /// file that InDesign or Illustrator keeps in xmpMM:Pantry.
        /// </summary>
        internal static int ClaimedPdfAPart(string packet)
        {
            if (!packet.Contains(PdfAId) || Load(packet, out _) is not { } document || FindRdf(document) is not { } rdf)
                return 0;
            foreach (var description in TopDescriptions(rdf))
            {
                var values = new[] { description.GetAttribute("part", PdfAId) }.Concat(description.ChildNodes.OfType<XmlElement>()
                    .Where(element => element.LocalName == "part" && element.NamespaceURI == PdfAId)
                    .Select(element => element.InnerText));
                foreach (var value in values)
                {
                    if (Int32.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var part) && part > 0)
                        return part;
                }
            }
            return 0;
        }

        /// <summary>
        /// Returns the updated packet, or null if the packet is not XML with an rdf:RDF element or cannot be updated.
        /// A text property is set when its document information entry has a value, and emptied when the entry is
        /// present but empty; a missing entry leaves the property as it is, and so does an empty date.
        /// </summary>
        internal static string? Update(string packet, DocumentMetadataInfo info, PdfAFormat? pdfa, Func<string, bool> infoHasEntry)
        {
            try
            {
                if (Load(packet, out var trailer) is not { } document || FindRdf(document) is not { } rdf)
                    return null;

                var updater = new Updater(document, rdf);
                if (pdfa is { } format)
                {
                    updater.SetSimple(PdfAId, "pdfaid", "part", format.Part.ToString(CultureInfo.InvariantCulture));
                    if (format.ConformanceLevel != ' ')
                        updater.SetSimple(PdfAId, "pdfaid", "conformance", format.ConformanceLevel.ToString());
                }
                if (Wanted(info.Title, PdfDocumentInformation.Keys.Title) is { } title)
                    updater.SetAlternative(Dc, "dc", "title", title);
                if (Wanted(info.Author, PdfDocumentInformation.Keys.Author) is { } author)
                    updater.SetSequence(Dc, "dc", "creator", author);
                if (Wanted(info.Subject, PdfDocumentInformation.Keys.Subject) is { } subject)
                    updater.SetAlternative(Dc, "dc", "description", subject);
                if (Wanted(info.Keywords, PdfDocumentInformation.Keys.Keywords) is { } keywords)
                    updater.SetSimple(Pdf, "pdf", "Keywords", keywords);
                if (Wanted(info.Producer, PdfDocumentInformation.Keys.Producer) is { } producer)
                    updater.SetSimple(Pdf, "pdf", "Producer", producer);
                if (Wanted(info.Creator, PdfDocumentInformation.Keys.Creator) is { } creator)
                    updater.SetSimple(Xmp, "xmp", "CreatorTool", creator);
                var metadataDate = String.IsNullOrEmpty(info.ModificationDate) ? info.CreationDate : info.ModificationDate;
                foreach (var (name, value) in new[] { ("CreateDate", info.CreationDate), ("ModifyDate", info.ModificationDate), ("MetadataDate", metadataDate) })
                {
                    if (!String.IsNullOrEmpty(value))
                        updater.SetSimple(Xmp, "xmp", name, value);
                }

                // Line breaks in values are written as character references, which a parser keeps; a parser turns a
                // literal CR LF into LF, and the value would no longer agree with the document information. Line breaks
                // between elements are written as LF, so that only values carry references.
                NormalizeWhitespaceNodes(document);
                var output = new StringWriter(CultureInfo.InvariantCulture);
                var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.Entitize };
                using (var writer = XmlWriter.Create(output, settings))
                    document.Save(writer);
                return Header + output + trailer;
            }
            catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException)
            {
                return null;
            }

            string? Wanted(string value, string key)
            {
                if (!String.IsNullOrEmpty(value))
                    return ValidXmlCharacters(value);
                return infoHasEntry(key) ? "" : null;
            }
        }

        /// <summary>
        /// The text without the characters XML 1.0 does not allow, such as most control characters and unpaired
        /// surrogates.
        /// </summary>
        internal static string ValidXmlCharacters(string value)
        {
            StringBuilder? result = null;
            for (int index = 0; index < value.Length; index++)
            {
                char ch = value[index];
                bool pair = Char.IsHighSurrogate(ch) && index + 1 < value.Length && Char.IsLowSurrogate(value[index + 1]);
                bool valid = pair || XmlConvert.IsXmlChar(ch);
                if (!valid && result == null)
                    result = new StringBuilder(value, 0, index, value.Length);
                if (valid)
                {
                    result?.Append(ch);
                    if (pair)
                        result?.Append(value[index + 1]);
                }
                if (pair)
                    index++;
            }
            return result?.ToString() ?? value;
        }

        // The XML between the packet's header and trailer, parsed with DTDs refused; null if that is not XML. The
        // trailer is kept when it is a valid one.
        static XmlDocument? Load(string packet, out string trailer)
        {
            int bodyStart = 0;
            int begin = packet.IndexOf("<?xpacket", StringComparison.Ordinal);
            if (begin >= 0 && packet.IndexOf("?>", begin, StringComparison.Ordinal) is var close and >= 0
                && packet.IndexOf("begin", begin, close - begin, StringComparison.Ordinal) >= 0)
            {
                bodyStart = close + 2;
            }
            trailer = "<?xpacket end=\"w\"?>";
            int trailerStart = packet.LastIndexOf("<?xpacket end", StringComparison.Ordinal);
            if (trailerStart >= bodyStart)
            {
                if (TrailerPattern.Match(packet.Substring(trailerStart)) is { Success: true } match)
                    trailer = $"<?xpacket end=\"{match.Groups[2].Value}\"?>";
            }
            else
            {
                trailerStart = packet.Length;
            }
            var body = packet.Substring(bodyStart, trailerStart - bodyStart).TrimStart();
            if (body.StartsWith("<?xml", StringComparison.Ordinal) && body.IndexOf("?>", StringComparison.Ordinal) is var declarationEnd and >= 0)
                body = body.Substring(declarationEnd + 2);

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

        static XmlElement? FindRdf(XmlDocument document)
            => document.GetElementsByTagName("RDF", Rdf).Cast<XmlElement>().FirstOrDefault();

        static IEnumerable<XmlElement> TopDescriptions(XmlElement rdf)
            => rdf.ChildNodes.OfType<XmlElement>().Where(element => element.LocalName == "Description" && element.NamespaceURI == Rdf);

        static void NormalizeWhitespaceNodes(XmlNode node)
        {
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is XmlWhitespace or XmlSignificantWhitespace)
                    child.Value = child.Value!.Replace("\r\n", "\n").Replace('\r', '\n');
                else if (child.HasChildNodes)
                    NormalizeWhitespaceNodes(child);
            }
        }

        sealed class Updater(XmlDocument document, XmlElement rdf)
        {
            readonly string _about = TopDescriptions(rdf).FirstOrDefault()?.GetAttribute("about", Rdf) ?? "";
            readonly Dictionary<string, XmlElement> _added = new();

            // Removes the property everywhere it occurs, as an element or as an attribute; returns the first element.
            XmlElement? TakeFirst(string ns, string name)
            {
                XmlElement? first = null;
                foreach (var description in TopDescriptions(rdf).ToList())
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
                var property = Property(ns, prefix, name);
                property.RemoveAll();
                property.InnerText = value;
            }

            // A language alternative: the x-default entry gets the value; other languages are kept.
            public void SetAlternative(string ns, string prefix, string name, string value)
            {
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
                var property = Property(ns, prefix, name);
                property.RemoveAll();
                var sequence = property.AppendChild(document.CreateElement("rdf", "Seq", Rdf))!;
                sequence.AppendChild(document.CreateElement("rdf", "li", Rdf))!.InnerText = value;
            }
        }
    }
}
