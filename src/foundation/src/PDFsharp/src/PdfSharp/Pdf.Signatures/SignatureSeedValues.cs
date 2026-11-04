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
        const int LegalAttestationBit = 5;
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
            if (Required(LegalAttestationBit) && seedValues.Elements.GetArray("/LegalAttestation") is { Elements.Count: > 0 })
                missing.Add("a legal attestation");
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

            if (Required(ReasonsBit) && seedValues.Elements.GetArray("/Reasons") is { Elements.Count: > 0 } reasons)
            {
                // A single empty string means that no reason may be given.
                var allowed = Strings(reasons);
                if (!allowed.Contains(options.Reason))
                {
                    var choices = allowed.All(reason => reason.Length == 0)
                        ? "no reason"
                        : "one of these reasons: " + string.Join(", ", allowed.Select(reason => $"'{reason}'"));
                    throw new ArgumentException($"The signature field '{name}' requires {choices}.", nameof(options));
                }
            }
        }

        static List<string> Names(PdfArray array)
            => Enumerable.Range(0, array.Elements.Count).Select(index => array.Elements.GetName(index)).Where(value => value.Length > 1).ToList();

        static List<string> Strings(PdfArray array)
            => Enumerable.Range(0, array.Elements.Count).Select(index => array.Elements.GetString(index)).ToList();
    }
}
