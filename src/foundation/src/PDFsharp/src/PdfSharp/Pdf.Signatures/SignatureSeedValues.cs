// FreePDF - signature field seed values.
// See the LICENSE file in the solution root for more information.

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Checks a signature field's seed value dictionary (/SV, ISO 32000-1 12.7.4.5 Table 234, ISO 32000-2 Table 237)
    /// before the field is signed. Required constraints that FreePDF meets pass; required constraints it cannot meet
    /// or cannot check refuse the signature, as Acrobat does; optional seed values are recommendations and ignored.
    /// </summary>
    static class SignatureSeedValues
    {
        // Bits of /Ff: a set bit makes the entry a required constraint.
        const int FilterBit = 1;
        const int SubFilterBit = 2;
        const int ReasonsBit = 4;
        const int AddRevInfoBit = 6;
        const int DigestMethodBit = 7;
        const int LockDocumentBit = 8;
        const int AppearanceFilterBit = 9;

        public static void Check(PdfDictionary seedValues, DigitalSignatureOptions options)
        {
            var name = options.FieldName;
            var flags = seedValues.Elements.GetInteger("/Ff");
            bool Required(int bit) => (flags & (1 << (bit - 1))) != 0;

            var missing = new List<string>();
            if (Required(FilterBit) && seedValues.Elements.GetName("/Filter") is { Length: > 1 } filter && filter != "/Adobe.PPKLite")
                missing.Add($"the signature handler {filter}");
            if (Required(SubFilterBit) && seedValues.Elements.GetArray("/SubFilter") is { } subFilters)
            {
                var formats = Names(subFilters);
                if (formats.Count > 0 && !formats.Contains("/adbe.pkcs7.detached"))
                    missing.Add($"the signature format {string.Join(" or ", formats)}");
            }
            // A required /LegalAttestation only limits the attestations a signer may make; FreePDF makes none.
            if (Required(AddRevInfoBit) && seedValues.Elements.GetBoolean("/AddRevInfo"))
                missing.Add("embedded revocation information (AddRevInfo)");
            if (Required(DigestMethodBit) && seedValues.Elements.GetArray("/DigestMethod") is { Elements.Count: > 0 })
                missing.Add("a particular digest method (DigestMethod), which the signer does not report");
            if (Required(LockDocumentBit) && seedValues.Elements.GetName("/LockDocument") == "/true")
                missing.Add("locking the document (LockDocument)");
            if (Required(AppearanceFilterBit) && seedValues.Elements.ContainsKey("/AppearanceFilter"))
                missing.Add("a named signature appearance (AppearanceFilter)");
            if (seedValues.Elements.GetDictionary("/Cert") is { } certificate && certificate.Elements.GetInteger("/Ff") != 0)
                missing.Add("constraints on the signing certificate (Cert), which the signer does not report");
            if (seedValues.Elements.GetDictionary("/MDP") is { } mdp && mdp.Elements.GetInteger("/P") > 0)
                missing.Add("a certification signature (MDP)");
            if (seedValues.Elements.GetDictionary("/TimeStamp") is { } timeStamp && timeStamp.Elements.GetInteger("/Ff") == 1)
                missing.Add("a timestamp (TimeStamp), which the signer does not report");
            if (missing.Count > 0)
            {
                throw new NotSupportedException($"The signature field '{name}' requires {string.Join(", ", missing)}; " +
                    "FreePDF cannot sign it.");
            }

            if (!Required(ReasonsBit))
                return;
            var reason = options.Reason ?? "";
            if (ReasonMustBeOmitted(seedValues))
            {
                if (reason.Length > 0)
                    throw new ArgumentException($"The signature field '{name}' does not allow a reason.", nameof(options));
                return;
            }
            var allowed = Strings(seedValues.Elements.GetArray("/Reasons")!);
            if (!allowed.Contains(reason))
            {
                throw new ArgumentException($"The signature field '{name}' requires one of these reasons: " +
                    string.Join(", ", allowed.Select(value => $"'{value}'")) + ".", nameof(options));
            }
        }

        /// <summary>
        /// Tells whether the signature must have no /Reason: the reasons are required and none are listed, or only a
        /// single period (ISO 32000-2 Table 237).
        /// </summary>
        public static bool ReasonMustBeOmitted(PdfDictionary seedValues)
        {
            if ((seedValues.Elements.GetInteger("/Ff") & (1 << (ReasonsBit - 1))) == 0)
                return false;
            var reasons = seedValues.Elements.GetArray("/Reasons");
            if (reasons == null || reasons.Elements.Count == 0)
                return true;
            return reasons.Elements.Count == 1 && reasons.Elements.GetString(0) == ".";
        }

        static List<string> Names(PdfArray array)
            => Enumerable.Range(0, array.Elements.Count).Select(index => array.Elements.GetName(index)).Where(value => value.Length > 1).ToList();

        static List<string> Strings(PdfArray array)
            => Enumerable.Range(0, array.Elements.Count).Select(index => array.Elements.GetString(index)).ToList();
    }
}
