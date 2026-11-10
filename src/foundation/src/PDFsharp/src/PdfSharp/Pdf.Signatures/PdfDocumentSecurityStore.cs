// FreePDF - MIT, see the LICENSE file in the solution root.

using PdfSharp.Pdf.Advanced;

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// The document security store (DSS, ISO 32000-2 12.8.4.3; ETSI EN 319 142-1 5.4.2): validation data for the
    /// document's signatures — certificates, OCSP responses and CRLs — kept in the document so the signatures can be
    /// validated after their certificates expire or are revoked. With it, PAdES B-T signatures become B-LT; a document
    /// time-stamp after it makes them B-LTA.
    /// </summary>
    /// <remarks>
    /// Open a signed document with <see cref="IO.PdfDocumentOpenMode.Append"/>, add the data and save: the update leaves
    /// the signed bytes as they are. Adding validation data is permitted at every certification level.
    /// </remarks>
    public static class PdfDocumentSecurityStore
    {
        /// <summary>
        /// Adds validation data to the document's DSS, creating the DSS if the document has none. Each item is
        /// DER-encoded: an X.509 certificate, an OCSP response (RFC 6960 OCSPResponse) or a CRL (RFC 5280
        /// CertificateList). Items the DSS already holds are not added again.
        /// </summary>
        /// <returns>The number of items added.</returns>
        public static int Add(PdfDocument document, IEnumerable<byte[]>? certificates = null, IEnumerable<byte[]>? ocspResponses = null,
            IEnumerable<byte[]>? crls = null)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            var lists = new[] { ("/Certs", Check(certificates, "certificates")), ("/OCSPs", Check(ocspResponses, "ocspResponses")), ("/CRLs", Check(crls, "crls")) };
            if (lists.All(list => list.Item2.Count == 0))
                return 0;

            var dss = document.Catalog.Elements.GetDictionary("/DSS");
            if (dss == null)
            {
                dss = new PdfDictionary(document);
                document.Internals.AddObject(dss);
                document.Catalog.Elements["/DSS"] = dss.Reference;
            }

            int added = 0;
            foreach (var (key, items) in lists)
            {
                if (items.Count == 0)
                    continue;
                var array = dss.Elements.GetArray(key);
                if (array == null)
                {
                    array = new PdfArray(document);
                    dss.Elements[key] = array;
                }
                var present = new List<byte[]>();
                for (int index = 0; index < array.Elements.Count; index++)
                {
                    if (array.Elements.GetDictionary(index)?.Stream?.UnfilteredValue is { } data)
                        present.Add(data);
                }
                foreach (var item in items)
                {
                    if (present.Any(data => data.AsSpan().SequenceEqual(item)))
                        continue;
                    var stream = new PdfDictionary(document);
                    stream.CreateStream(item);
                    document.Internals.AddObject(stream);
                    array.Elements.Add(stream.Reference!);
                    present.Add(item);
                    added++;
                }
            }
            return added;
        }

        // Each item must be a DER SEQUENCE: certificates, OCSP responses and CRLs all are.
        static List<byte[]> Check(IEnumerable<byte[]>? items, string name)
        {
            var list = items?.ToList() ?? new List<byte[]>();
            foreach (var item in list)
            {
                if (item == null || item.Length < 2 || item[0] != 0x30)
                    throw new ArgumentException("Each item must be DER-encoded (an ASN.1 SEQUENCE).", name);
            }
            return list;
        }
    }
}
