// FreePDF - MIT, see the LICENSE file in the solution root.

using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace PdfSharp.Pdf.Signatures
{
    /// <summary>
    /// Checks the signatures and document time-stamps of a PDF file: that the bytes they cover are intact, who signed
    /// and when, whether the signer's chain ends at a trusted root, revocation, later changes their permissions forbid,
    /// and the PAdES level (ISO 32000-2 12.8, ETSI EN 319 142-1, RFC 5652, RFC 3161, RFC 6960, RFC 5280). It reports
    /// facts and a technical status, not the legal effect of a signature; EU trusted lists and qualified status are not
    /// checked.
    /// </summary>
    public static class PdfSignatureVerifier
    {
        /// <summary>
        /// Checks every signature and document time-stamp of the file, in the order their fields are found.
        /// </summary>
        public static async Task<IReadOnlyList<PdfSignatureVerification>> VerifyAsync(Stream file, PdfSignatureVerificationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            return await VerifyAsync(buffer.ToArray(), options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks every signature and document time-stamp of the file, in the order their fields are found.
        /// </summary>
        public static async Task<IReadOnlyList<PdfSignatureVerification>> VerifyAsync(byte[] file, PdfSignatureVerificationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            options ??= new PdfSignatureVerificationOptions();
            var validationTime = options.ValidationTime ?? DateTimeOffset.UtcNow;
            var trustedRoots = options.TrustedRoots?.ToList();

            using var document = PdfReader.Open(new MemoryStream(file, false), options.Password, PdfDocumentOpenMode.Import);
            var revisionEnds = RevisionEnds(file);
            var dss = ReadDss(document);
            var extra = options.Online?.ExtraCertificates?.ToList() ?? [];
            var fetcher = options.Revocation == PdfRevocationCheck.Online
                ? new PdfDocumentSecurityStore.Fetcher(options.Online ?? new PdfValidationDataOptions(), cancellationToken)
                : null;

            var found = Signatures(document);
            var results = new List<(PdfSignatureVerification Result, int End)>();
            foreach (var signature in found)
            {
                var result = new PdfSignatureVerification
                {
                    FieldName = signature.Name,
                    SubFilter = signature.Value.Elements.GetName("/SubFilter").TrimStart('/'),
                    Revisions = revisionEnds.Count,
                };
                int end = await CheckAsync(file, document, signature, result, dss, extra, trustedRoots, validationTime, options,
                    fetcher, revisionEnds).ConfigureAwait(false);
                results.Add((result, end));
            }

            // B-LTA: a valid document time-stamp in a later revision covers the DSS that made the signature B-LT.
            foreach (var (result, end) in results)
            {
                if (result.PadesLevel != PdfPadesLevel.BaselineLT)
                    continue;
                bool stamped = results.Any(other => other.Result.Kind == PdfSignatureKind.DocumentTimeStamp && other.Result.Intact
                    && other.End > end && dss.Position is { } position && position < other.End);
                if (stamped)
                    result.PadesLevel = PdfPadesLevel.BaselineLTA;
            }
            return results.Select(r => r.Result).ToList();
        }

        static async Task<int> CheckAsync(byte[] file, PdfDocument document, FoundSignature signature, PdfSignatureVerification result,
            Dss dss, List<X509Certificate2> extra, List<X509Certificate2>? trustedRoots, DateTimeOffset validationTime,
            PdfSignatureVerificationOptions options, PdfDocumentSecurityStore.Fetcher? fetcher, List<int> revisionEnds)
        {
            var value = signature.Value;
            result.Kind = value.Elements.GetName("/Type") == "/DocTimeStamp" ? PdfSignatureKind.DocumentTimeStamp
                : signature.CertificationLevel != null ? PdfSignatureKind.Certification
                : PdfSignatureKind.Approval;
            result.CertificationLevel = signature.CertificationLevel;
            try
            {
                result.ClaimedSigningTime = value.Elements.GetDateTime("/M");
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
            {
                result._reasons.Add("The /M signing time is not a PDF date.");
            }

            // The byte range: from 0, one gap that is exactly this signature's /Contents, ending at a revision.
            if (!ByteRange(file, value, out var covered, out var contents, out var end, out var problem))
            {
                result._reasons.Add(problem);
                result.Status = PdfSignatureStatus.Invalid;
                return 0;
            }
            int trimmedLength = TrimmedLength(file);
            result.CoversWholeFile = end >= trimmedLength;
            int revision = revisionEnds.FindIndex(e => e >= end);
            result.Revision = revision < 0 ? revisionEnds.Count : revision + 1;
            if (!revisionEnds.Contains(end) && !result.CoversWholeFile)
                result._reasons.Add("The byte range does not end at the end of a revision.");

            // Integrity, signer, times.
            SignerInfo? signerInfo = null;
            SignedCms? cms = null;
            if (result.Kind == PdfSignatureKind.DocumentTimeStamp || result.SubFilter == "ETSI.RFC3161")
            {
                CheckDocumentTimeStamp(contents, covered, result);
            }
            else if (result.SubFilter is "adbe.pkcs7.detached" or "ETSI.CAdES.detached" or "adbe.pkcs7.sha1")
            {
                (cms, signerInfo) = CheckCms(contents, covered, result);
            }
            else
            {
                result._reasons.Add($"The /SubFilter {result.SubFilter} is not supported.");
                result.Status = PdfSignatureStatus.Indeterminate;
                return end;
            }
            if (!result.Intact)
            {
                result.Status = PdfSignatureStatus.Invalid;
                return end;
            }

            // The certificates to build chains from: the CMS, the DSS, the caller's.
            var available = new List<X509Certificate2>(extra);
            available.AddRange(dss.Certificates);
            if (cms != null)
                available.AddRange(cms.Certificates.Cast<X509Certificate2>());
            if (trustedRoots != null)
                available.AddRange(trustedRoots);

            // The best proven time of the signature: its time-stamp, or else now.
            var bestTime = result.TimeStampTime ?? validationTime;
            var signer = result.Signer!;
            var (chain, trusted, chainProblem, validNow) = BuildChain(signer, available, trustedRoots, validationTime);
            if (!validNow && result.TimeStampTime is { } stampTime)
            {
                // Expired (or not yet valid) now: valid at the time-stamp, which proves the signature existed then.
                var atStamp = BuildChain(signer, available, trustedRoots, stampTime);
                if (atStamp.ValidNow)
                {
                    (chain, trusted, chainProblem) = (atStamp.Chain, atStamp.Trusted, atStamp.Problem);
                    result._reasons.Add($"The certificate is not valid at the validation time; it was at the time-stamp ({stampTime:u}).");
                    validNow = true;
                }
            }
            result.Chain = chain;
            result.Trusted = trusted && validNow;
            if (chainProblem != null)
                result._reasons.Add(chainProblem);
            if (result.TimeStampAuthority is { } tsa && result.Kind != PdfSignatureKind.DocumentTimeStamp)
            {
                var tsaChain = BuildChain(tsa, available, trustedRoots, result.TimeStampTime ?? validationTime);
                if (!tsaChain.Trusted || !tsaChain.ValidNow)
                    result._reasons.Add($"The time-stamp authority's certificate is not trusted: {tsaChain.Problem ?? "not valid at the time-stamp"}.");
            }

            // Revocation of the chain below its root.
            if (options.Revocation != PdfRevocationCheck.None)
                result.Revocation = await CheckRevocationAsync(chain, dss, fetcher, bestTime, result).ConfigureAwait(false);
            else
                result.Revocation = PdfRevocationStatus.NotChecked;

            // Later revisions: what the signature's permissions and locks forbid.
            if (!result.CoversWholeFile)
                result.ForbiddenChange = ForbiddenChange(file, end, document, options.Password);
            if (result.ForbiddenChange != null)
                result._reasons.Add($"A later update makes a change the signatures forbid: {result.ForbiddenChange}.");

            // PAdES level, up to B-LT; B-LTA is decided once the document time-stamps are known.
            if (result.SubFilter == "ETSI.CAdES.detached" && signerInfo != null && SigningCertificateMatches(signerInfo, signer, result))
            {
                result.PadesLevel = PdfPadesLevel.BaselineB;
                if (result.TimeStampTime != null)
                {
                    result.PadesLevel = PdfPadesLevel.BaselineT;
                    if (dss.Present && result.Revocation == PdfRevocationStatus.Good && chain.All(c => dss.Holds(c)))
                        result.PadesLevel = PdfPadesLevel.BaselineLT;
                }
            }

            result.Status = result.ForbiddenChange != null || result.Revocation == PdfRevocationStatus.Revoked
                ? PdfSignatureStatus.Invalid
                : result.Trusted && result.Revocation != PdfRevocationStatus.Unknown
                    ? PdfSignatureStatus.Valid
                    : PdfSignatureStatus.Indeterminate;
            return end;
        }

        // ===== Byte range =====

        static bool ByteRange(byte[] file, PdfDictionary value, out byte[] covered, out byte[] contents, out int end, out string problem)
        {
            covered = [];
            contents = [];
            end = 0;
            problem = "";
            var array = Resolve(value.Elements["/ByteRange"]) as PdfArray;
            var numbers = array?.Elements.Select(item => Resolve(item) switch
            {
                PdfInteger integer => (long?)integer.Value,
                PdfLongInteger longInteger => longInteger.Value,
                _ => null,
            }).ToList();
            if (numbers is not { Count: 4 } || numbers.Any(n => n is null or < 0))
            {
                problem = "The /ByteRange is not four non-negative integers.";
                return false;
            }
            long a = numbers[0]!.Value, b = numbers[1]!.Value, c = numbers[2]!.Value, d = numbers[3]!.Value;
            if (a != 0 || b < 2 || c <= b + 1 || c + d > file.Length)
            {
                problem = "The /ByteRange does not start at 0, has no gap, or runs past the end of the file.";
                return false;
            }
            if (file[b] != '<' || file[c - 1] != '>')
            {
                problem = "The gap in the /ByteRange is not a hexadecimal string.";
                return false;
            }
            var hex = new byte[(c - b - 2) / 2];
            for (long i = 0; i < hex.Length; i++)
            {
                int high = HexValue(file[b + 1 + 2 * i]), low = HexValue(file[b + 2 + 2 * i]);
                if (high < 0 || low < 0)
                {
                    problem = "The gap in the /ByteRange is not a hexadecimal string.";
                    return false;
                }
                hex[i] = (byte)(high << 4 | low);
            }
            // The gap must be this signature's /Contents, not another string.
            if (Resolve(value.Elements["/Contents"]) is not PdfString parsed || !parsed.GetRawBytes().AsSpan().SequenceEqual(hex))
            {
                problem = "The gap in the /ByteRange is not the signature's /Contents.";
                return false;
            }
            covered = new byte[b + d];
            Array.Copy(file, 0, covered, 0, b);
            Array.Copy(file, c, covered, b, d);
            // /Contents is padded with zeros after the DER value.
            try
            {
                var length = new AsnReader(hex, AsnEncodingRules.BER).ReadEncodedValue().Length;
                contents = hex.AsSpan(0, length).ToArray();
            }
            catch (AsnContentException)
            {
                problem = "The /Contents is not a DER value.";
                return false;
            }
            end = (int)(c + d);
            return true;
        }

        static int HexValue(byte b) => b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - '0',
            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
            _ => -1,
        };

        // The offsets just after each %%EOF marker and its end of line: where the revisions end.
        static List<int> RevisionEnds(byte[] file)
        {
            var result = new List<int>();
            var marker = "%%EOF"u8;
            for (int index = file.AsSpan().IndexOf(marker); index >= 0;)
            {
                int end = index + marker.Length;
                if (end < file.Length && file[end] == '\r')
                    end++;
                if (end < file.Length && file[end] == '\n')
                    end++;
                result.Add(end);
                int next = file.AsSpan(index + marker.Length).IndexOf(marker);
                index = next < 0 ? -1 : index + marker.Length + next;
            }
            return result;
        }

        // The file's length without trailing white space.
        static int TrimmedLength(byte[] file)
        {
            int length = file.Length;
            while (length > 0 && file[length - 1] is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t' or 0)
                length--;
            return length;
        }

        // ===== CMS signatures =====

        static (SignedCms? Cms, SignerInfo? SignerInfo) CheckCms(byte[] contents, byte[] covered, PdfSignatureVerification result)
        {
            SignedCms cms;
            try
            {
                if (result.SubFilter == "adbe.pkcs7.sha1")
                {
                    // The CMS signs the SHA-1 digest of the covered bytes as its encapsulated content.
                    cms = new SignedCms();
                    cms.Decode(contents);
                    using var sha1 = SHA1.Create();
                    if (!cms.ContentInfo.Content.AsSpan().SequenceEqual(sha1.ComputeHash(covered)))
                    {
                        result._reasons.Add("The signed SHA-1 digest does not match the covered bytes.");
                        return (cms, null);
                    }
                }
                else
                {
                    cms = new SignedCms(new ContentInfo(covered), detached: true);
                    cms.Decode(contents);
                }
            }
            catch (CryptographicException ex)
            {
                result._reasons.Add($"The signature is not a CMS SignedData FreePDF can read: {ex.Message}");
                return (null, null);
            }
            if (cms.SignerInfos.Count != 1)
            {
                result._reasons.Add($"The CMS has {cms.SignerInfos.Count} signers; PDF signatures have one.");
                return (cms, null);
            }
            var signerInfo = cms.SignerInfos[0];
            result.Signer = signerInfo.Certificate;
            if (result.Signer == null)
            {
                result._reasons.Add("The signer's certificate is not in the CMS.");
                return (cms, signerInfo);
            }
            try
            {
                // The message digest over the covered bytes and the signature with the signer's key.
                signerInfo.CheckSignature(verifySignatureOnly: true);
                result.Intact = true;
            }
            catch (CryptographicException ex)
            {
                result._reasons.Add($"The signature does not verify over the covered bytes: {ex.Message}");
                return (cms, signerInfo);
            }
            CheckSignatureTimeStamp(signerInfo, result);
            return (cms, signerInfo);
        }

        // The signature time-stamp (RFC 3161 token over the signature value), when there is one.
        static void CheckSignatureTimeStamp(SignerInfo signerInfo, PdfSignatureVerification result)
        {
            var attribute = signerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>()
                .FirstOrDefault(a => a.Oid.Value == "1.2.840.113549.1.9.16.2.14");
            if (attribute == null || attribute.Values.Count == 0)
                return;
#if NET8_0_OR_GREATER
            if (!Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _))
            {
                result._reasons.Add("The signature time-stamp is not an RFC 3161 token.");
                return;
            }
            if (!token.VerifySignatureForSignerInfo(signerInfo, out var tsa) || tsa == null)
            {
                result._reasons.Add("The signature time-stamp does not verify against the signature.");
                return;
            }
            result.TimeStampTime = token.TokenInfo.Timestamp;
            result.TimeStampAuthority = tsa;
#else
            result._reasons.Add("Time-stamps are checked on .NET 8 or later only.");
#endif
        }

        static void CheckDocumentTimeStamp(byte[] contents, byte[] covered, PdfSignatureVerification result)
        {
#if NET8_0_OR_GREATER
            if (!Rfc3161TimestampToken.TryDecode(contents, out var token, out _))
            {
                result._reasons.Add("The document time-stamp is not an RFC 3161 token.");
                return;
            }
            if (!token.VerifySignatureForData(covered, out var tsa) || tsa == null)
            {
                result._reasons.Add("The document time-stamp does not match the covered bytes.");
                return;
            }
            result.Intact = true;
            result.Signer = tsa;
            result.TimeStampAuthority = tsa;
            result.TimeStampTime = token.TokenInfo.Timestamp;
#else
            result._reasons.Add("Document time-stamps are checked on .NET 8 or later only.");
#endif
        }

        // signing-certificate-v2 (RFC 5035) or signing-certificate (RFC 2634): the first ESSCertID must be the signer's.
        static bool SigningCertificateMatches(SignerInfo signerInfo, X509Certificate2 signer, PdfSignatureVerification result)
        {
            foreach (CryptographicAttributeObject attribute in signerInfo.SignedAttributes)
            {
                bool v2 = attribute.Oid.Value == "1.2.840.113549.1.9.16.2.47";
                if (!v2 && attribute.Oid.Value != "1.2.840.113549.1.9.16.2.12")
                    continue;
                try
                {
                    var certId = new AsnReader(attribute.Values[0].RawData, AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence();
                    var algorithm = HashAlgorithmName.SHA1;
                    if (v2)
                    {
                        algorithm = HashAlgorithmName.SHA256;
                        if (certId.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                            algorithm = HashFromOid(certId.ReadSequence().ReadObjectIdentifier()) ?? default;
                    }
                    if (algorithm == default)
                    {
                        result._reasons.Add("The signing-certificate attribute uses an unknown hash.");
                        return false;
                    }
                    if (Hash(algorithm, signer.RawData).AsSpan().SequenceEqual(certId.ReadOctetString()))
                        return true;
                    result._reasons.Add("The signing-certificate attribute names another certificate than the signer's.");
                    return false;
                }
                catch (AsnContentException)
                {
                    result._reasons.Add("The signing-certificate attribute is not DER.");
                    return false;
                }
            }
            result._reasons.Add("A PAdES signature needs the signing-certificate-v2 attribute; it has none.");
            return false;
        }

        // ===== Chains =====

        // The chain from the certificate up, whether its root is trusted, and whether it is valid at the time.
        static (List<X509Certificate2> Chain, bool Trusted, string? Problem, bool ValidNow) BuildChain(X509Certificate2 leaf,
            List<X509Certificate2> available, List<X509Certificate2>? trustedRoots, DateTimeOffset time)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = time.UtcDateTime;
#if NET5_0_OR_GREATER
            chain.ChainPolicy.DisableCertificateDownloads = true;
            if (trustedRoots != null)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                foreach (var trustedRoot in trustedRoots)
                    chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
            }
#else
            if (trustedRoots != null)
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
#endif
            foreach (var certificate in available)
                chain.ChainPolicy.ExtraStore.Add(certificate);
            bool built = chain.Build(leaf);
            var elements = chain.ChainElements.Cast<X509ChainElement>().Select(e => PdfDocumentSecurityStore.LoadCertificate(e.Certificate.RawData)).ToList();
            var root = elements.LastOrDefault();
            var flags = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, status) => all | status.Status);
            bool timeValid = (flags & X509ChainStatusFlags.NotTimeValid) == 0;
#if !NET5_0_OR_GREATER
            // Without custom root trust, the root must be one of the given ones.
            if (trustedRoots != null && root != null && !trustedRoots.Any(r => r.Thumbprint == root.Thumbprint))
                return (elements, false, "The chain does not end at one of the trusted roots.", timeValid);
            if (trustedRoots != null)
                flags &= ~X509ChainStatusFlags.UntrustedRoot;
#endif
            var other = flags & ~X509ChainStatusFlags.NotTimeValid;
            if (other == X509ChainStatusFlags.NoError && (built || !timeValid))
                return (elements, true, null, timeValid);
            var problems = string.Join("; ", chain.ChainStatus.Where(s => s.Status != X509ChainStatusFlags.NotTimeValid)
                .Select(s => s.StatusInformation.Trim()).Where(s => s.Length > 0).Distinct());
            return (elements, false, $"The certificate chain is not trusted: {(problems.Length > 0 ? problems : other.ToString())}.", timeValid);
        }

        // ===== Revocation =====

        static async Task<PdfRevocationStatus> CheckRevocationAsync(List<X509Certificate2> chain, Dss dss,
            PdfDocumentSecurityStore.Fetcher? fetcher, DateTimeOffset bestTime, PdfSignatureVerification result)
        {
            var status = PdfRevocationStatus.Good;
            for (int index = 0; index + 1 < chain.Count; index++)
            {
                var certificate = chain[index];
                var issuer = chain[index + 1];
                var found = Revocation(certificate, issuer, dss.OcspResponses, dss.Crls, bestTime);
                if (found.Status == PdfRevocationStatus.Unknown && fetcher != null)
                {
                    var ocsp = await fetcher.OcspAsync(certificate, issuer).ConfigureAwait(false);
                    var crls = ocsp == null ? await fetcher.CrlsAsync(certificate).ConfigureAwait(false) : [];
                    found = Revocation(certificate, issuer, ocsp == null ? [] : [ocsp.Value.Response], crls, bestTime);
                    foreach (var problem in fetcher.Problems)
                        result._reasons.Add(problem);
                    fetcher.Problems.Clear();
                }
                if (found.Problem != null)
                    result._reasons.Add(found.Problem);
                if (found.Status == PdfRevocationStatus.Revoked)
                    return PdfRevocationStatus.Revoked;
                if (found.Status == PdfRevocationStatus.Unknown)
                    status = PdfRevocationStatus.Unknown;
            }
            return status;
        }

        // The status of one certificate from OCSP responses, then CRLs. Data issued before the best signature time does
        // not show the status at signing (EN 319 102-1 freshness), so it is not used.
        static (PdfRevocationStatus Status, string? Problem) Revocation(X509Certificate2 certificate, X509Certificate2 issuer,
            List<byte[]> ocspResponses, List<byte[]> crls, DateTimeOffset bestTime)
        {
            string? problem = null;
            foreach (var response in ocspResponses)
            {
                var answer = Ocsp.Check(response, certificate, issuer);
                if (answer == null)
                    continue;
                if (answer.Value.Revoked && answer.Value.RevocationTime <= bestTime)
                    return (PdfRevocationStatus.Revoked, $"'{certificate.Subject}' was revoked at {answer.Value.RevocationTime:u} (OCSP).");
                if (answer.Value.Revoked)
                    return (PdfRevocationStatus.Good, $"'{certificate.Subject}' was revoked at {answer.Value.RevocationTime:u}, after the signature (OCSP).");
                if (answer.Value.ThisUpdate >= bestTime.AddSeconds(-1) || answer.Value.ProducedAt >= bestTime.AddSeconds(-1))
                    return (PdfRevocationStatus.Good, null);
                problem = $"The OCSP response for '{certificate.Subject}' is older than the signature.";
            }
            foreach (var crl in crls)
            {
                var answer = Crl.Check(crl, certificate, issuer);
                if (answer == null)
                    continue;
                if (answer.Value.RevocationTime is { } revoked && revoked <= bestTime)
                    return (PdfRevocationStatus.Revoked, $"'{certificate.Subject}' was revoked at {revoked:u} (CRL).");
                if (answer.Value.RevocationTime is { } later)
                    return (PdfRevocationStatus.Good, $"'{certificate.Subject}' was revoked at {later:u}, after the signature (CRL).");
                if (answer.Value.ThisUpdate >= bestTime.AddSeconds(-1))
                    return (PdfRevocationStatus.Good, null);
                problem = $"The CRL for '{certificate.Subject}' is older than the signature.";
            }
            return (PdfRevocationStatus.Unknown, problem ?? $"No revocation data for '{certificate.Subject}'.");
        }

        // ===== Later changes =====

        static string? ForbiddenChange(byte[] file, int end, PdfDocument document, string? password)
        {
            var revisionBytes = file.AsSpan(0, end).ToArray();
            using var revision = PdfReader.Open(new MemoryStream(revisionBytes, false), password, PdfDocumentOpenMode.Import);
            var permissions = PdfModificationPermissions.Read(revision);
            if (permissions == null)
                return null;
            var written = new List<PdfReference>();
            foreach (var iref in document.IrefTable.TransitiveClosure(document.Trailer))
            {
                if (iref.Value is not { } value)
                    continue;
                if (revision.Internals.GetObject(iref.ObjectID) is not { } old)
                {
                    written.Add(iref);
                    continue;
                }
                var before = PdfIncrementalUpdate.TryHash(revision, old);
                var after = PdfIncrementalUpdate.TryHash(document, value);
                if (before == null || after == null || !PdfIncrementalUpdate.Same(before, after))
                    written.Add(iref);
            }
            try
            {
                permissions.Check(document, revisionBytes, written, password);
                return null;
            }
            catch (InvalidOperationException ex) when (ex.Data[PdfModificationPermissions.RefusedChangeKey] is string change)
            {
                return change;
            }
        }

        // ===== Document =====

        sealed record FoundSignature(string Name, PdfDictionary Value, int? CertificationLevel);

        // The signature fields with a value, with fully qualified names, and the certification level of the one the
        // catalog's /Perms /DocMDP names.
        static List<FoundSignature> Signatures(PdfDocument document)
        {
            var result = new List<FoundSignature>();
            var docMdp = Resolve((Resolve(document.Catalog.Elements["/Perms"]) as PdfDictionary)?.Elements["/DocMDP"]) as PdfDictionary;
            var fields = (Resolve(document.Catalog.Elements["/AcroForm"]) as PdfDictionary)?.Elements["/Fields"];
            if (Resolve(fields) is not PdfArray roots)
                return result;
            var visited = new HashSet<PdfDictionary>();
            var pending = new Stack<(PdfItem Item, string Parent)>();
            for (int index = roots.Elements.Count - 1; index >= 0; index--)
                pending.Push((roots.Elements[index], ""));
            while (pending.Count > 0)
            {
                var (item, parent) = pending.Pop();
                if (Resolve(item) is not PdfDictionary field || !visited.Add(field))
                    continue;
                var partial = field.Elements.GetString("/T");
                var name = partial.Length == 0 ? parent : parent.Length == 0 ? partial : parent + "." + partial;
                if (Resolve(field.Elements["/V"]) is PdfDictionary value && value.Elements.ContainsKey("/ByteRange"))
                {
                    int? level = ReferenceEquals(value, docMdp) || HasDocMdpReference(value) ? DocMdpLevel(value) : null;
                    result.Add(new FoundSignature(name, value, level));
                }
                if (Resolve(field.Elements["/Kids"]) is PdfArray kids)
                {
                    for (int index = kids.Elements.Count - 1; index >= 0; index--)
                        pending.Push((kids.Elements[index], name));
                }
            }
            return result;
        }

        static bool HasDocMdpReference(PdfDictionary value)
            => Resolve(value.Elements["/Reference"]) is PdfArray references
               && references.Elements.Any(r => (Resolve(r) as PdfDictionary)?.Elements.GetName("/TransformMethod") == "/DocMDP");

        static int DocMdpLevel(PdfDictionary value)
        {
            if (Resolve(value.Elements["/Reference"]) is PdfArray references)
            {
                foreach (var item in references.Elements)
                {
                    if (Resolve(item) is PdfDictionary reference && reference.Elements.GetName("/TransformMethod") == "/DocMDP")
                    {
                        var parameters = Resolve(reference.Elements["/TransformParams"]) as PdfDictionary;
                        var p = Resolve(parameters?.Elements["/P"]) is PdfInteger integer ? integer.Value : 2;
                        return p is >= 1 and <= 3 ? p : 1;
                    }
                }
            }
            return 2;
        }

        sealed class Dss
        {
            internal bool Present { get; init; }
            internal long? Position { get; init; }
            internal List<X509Certificate2> Certificates { get; } = [];
            internal List<byte[]> OcspResponses { get; } = [];
            internal List<byte[]> Crls { get; } = [];

            internal bool Holds(X509Certificate2 certificate)
                => Certificates.Any(c => c.RawData.AsSpan().SequenceEqual(certificate.RawData));
        }

        static Dss ReadDss(PdfDocument document)
        {
            var item = document.Catalog.Elements["/DSS"];
            if (Resolve(item) is not PdfDictionary dictionary)
                return new Dss();
            var dss = new Dss { Present = true, Position = (item as PdfReference)?.Position };
            foreach (var data in Streams(dictionary, "/Certs"))
            {
                try
                {
                    dss.Certificates.Add(PdfDocumentSecurityStore.LoadCertificate(data));
                }
                catch (CryptographicException)
                {
                }
            }
            dss.OcspResponses.AddRange(Streams(dictionary, "/OCSPs"));
            dss.Crls.AddRange(Streams(dictionary, "/CRLs"));
            return dss;
        }

        static IEnumerable<byte[]> Streams(PdfDictionary dss, string key)
        {
            if (Resolve(dss.Elements[key]) is not PdfArray array)
                yield break;
            foreach (var item in array.Elements)
            {
                if (Resolve(item) is PdfDictionary { Stream: { } stream })
                    yield return stream.UnfilteredValue;
            }
        }

        static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

        // ===== Hashes and signatures =====

        internal static HashAlgorithmName? HashFromOid(string oid) => oid switch
        {
            "1.3.14.3.2.26" => HashAlgorithmName.SHA1,
            "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
            "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
            "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
            _ => null,
        };

        internal static byte[] Hash(HashAlgorithmName algorithm, byte[] data)
        {
            using var hash = IncrementalHash.CreateHash(algorithm);
            hash.AppendData(data);
            return hash.GetHashAndReset();
        }

        // A signature of a CRL or an OCSP response: RSA with PKCS #1 v1.5 or ECDSA, with SHA-1 to SHA-512.
        internal static bool VerifySignature(X509Certificate2 signer, string algorithmOid, byte[] data, byte[] signature)
        {
            var (hash, ecdsa) = algorithmOid switch
            {
                "1.2.840.113549.1.1.5" => (HashAlgorithmName.SHA1, false),
                "1.2.840.113549.1.1.11" => (HashAlgorithmName.SHA256, false),
                "1.2.840.113549.1.1.12" => (HashAlgorithmName.SHA384, false),
                "1.2.840.113549.1.1.13" => (HashAlgorithmName.SHA512, false),
                "1.2.840.10045.4.1" => (HashAlgorithmName.SHA1, true),
                "1.2.840.10045.4.3.2" => (HashAlgorithmName.SHA256, true),
                "1.2.840.10045.4.3.3" => (HashAlgorithmName.SHA384, true),
                "1.2.840.10045.4.3.4" => (HashAlgorithmName.SHA512, true),
                _ => (default(HashAlgorithmName), false),
            };
            if (hash == default)
                return false;
            try
            {
                if (!ecdsa)
                {
                    using var rsa = signer.GetRSAPublicKey();
                    return rsa != null && rsa.VerifyData(data, signature, hash, RSASignaturePadding.Pkcs1);
                }
                using var key = signer.GetECDsaPublicKey();
                if (key == null)
                    return false;
#if NET5_0_OR_GREATER
                return key.VerifyData(data, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
#else
                return key.VerifyData(data, DerToP1363(signature, (key.KeySize + 7) / 8), hash);
#endif
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

#if !NET5_0_OR_GREATER
        // An ECDSA signature as the DER SEQUENCE of r and s, as r and s of the key's size, concatenated.
        static byte[] DerToP1363(byte[] der, int size)
        {
            var sequence = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            var result = new byte[2 * size];
            for (int part = 0; part < 2; part++)
            {
                var value = sequence.ReadIntegerBytes().Span;
                while (value.Length > size && value[0] == 0)
                    value = value.Slice(1);
                if (value.Length > size)
                    throw new CryptographicException("The ECDSA signature does not fit the key.");
                value.CopyTo(result.AsSpan(part * size + size - value.Length));
            }
            return result;
        }
#endif

        internal static DateTimeOffset ReadTime(AsnReader reader)
            => reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? reader.ReadUtcTime() : reader.ReadGeneralizedTime();

        // ===== OCSP (RFC 6960) =====

        static class Ocsp
        {
            // The answer of a verified response about the certificate, or null if the response is not about it, not
            // successful, or not signed by the issuer or a responder the issuer delegated to.
            internal static (bool Revoked, DateTimeOffset RevocationTime, DateTimeOffset ThisUpdate, DateTimeOffset ProducedAt)? Check(byte[] der,
                X509Certificate2 certificate, X509Certificate2 issuer)
            {
                try
                {
                    var response = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
                    var status = response.ReadEnumeratedBytes().Span;
                    if (status.Length != 1 || status[0] != 0)
                        return null;
                    var responseBytes = response.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadSequence();
                    if (responseBytes.ReadObjectIdentifier() != "1.3.6.1.5.5.7.48.1.1")
                        return null;
                    var basic = new AsnReader(responseBytes.ReadOctetString(), AsnEncodingRules.DER).ReadSequence();
                    var tbsData = basic.ReadEncodedValue().ToArray();
                    var algorithm = basic.ReadSequence().ReadObjectIdentifier();
                    var signature = basic.ReadBitString(out _);
                    var certificates = new List<X509Certificate2>();
                    if (basic.HasData && basic.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                    {
                        var certs = basic.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadSequence();
                        while (certs.HasData)
                            certificates.Add(PdfDocumentSecurityStore.LoadCertificate(certs.ReadEncodedValue().ToArray()));
                    }

                    var data = new AsnReader(tbsData, AsnEncodingRules.DER).ReadSequence();
                    if (data.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        data.ReadEncodedValue();   // version
                    var responderId = data.ReadEncodedValue().ToArray();
                    var producedAt = data.ReadGeneralizedTime();
                    var responses = data.ReadSequence();
                    (bool, DateTimeOffset, DateTimeOffset)? answer = null;
                    while (responses.HasData && answer == null)
                    {
                        var single = responses.ReadSequence();
                        if (!IsAbout(single.ReadSequence(), certificate, issuer))
                            continue;
                        var tag = single.PeekTag();
                        if (tag.TagClass != TagClass.ContextSpecific || tag.TagValue == 2)
                            return null;   // unknown
                        bool revoked = tag.TagValue == 1;
                        var revocationTime = default(DateTimeOffset);
                        if (revoked)
                            revocationTime = single.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)).ReadGeneralizedTime();
                        else
                            single.ReadNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                        answer = (revoked, revocationTime, single.ReadGeneralizedTime());
                    }
                    if (answer == null || Responder(responderId, issuer, certificates) is not { } responder
                        || !VerifySignature(responder, algorithm, tbsData, signature))
                    {
                        return null;
                    }
                    return (answer.Value.Item1, answer.Value.Item2, answer.Value.Item3, producedAt);
                }
                catch (Exception ex) when (ex is AsnContentException or CryptographicException)
                {
                    return null;
                }
            }

            // CertID: the issuer's name and key hashes and the serial number (RFC 6960 4.1.1).
            static bool IsAbout(AsnReader certId, X509Certificate2 certificate, X509Certificate2 issuer)
            {
                var algorithm = HashFromOid(certId.ReadSequence().ReadObjectIdentifier());
                if (algorithm == null)
                    return false;
                var nameHash = certId.ReadOctetString();
                var keyHash = certId.ReadOctetString();
                var serial = certId.ReadInteger();
                return serial == new BigInteger(certificate.GetSerialNumber())
                    && Hash(algorithm.Value, issuer.SubjectName.RawData).AsSpan().SequenceEqual(nameHash)
                    && Hash(algorithm.Value, issuer.PublicKey.EncodedKeyValue.RawData).AsSpan().SequenceEqual(keyHash);
            }

            // The responder: the issuer itself, or a certificate the issuer issued for OCSP signing (RFC 6960 4.2.2.2).
            static X509Certificate2? Responder(byte[] responderId, X509Certificate2 issuer, List<X509Certificate2> certificates)
            {
                foreach (var candidate in new[] { issuer }.Concat(certificates))
                {
                    if (!Names(responderId, candidate))
                        continue;
                    if (candidate.RawData.AsSpan().SequenceEqual(issuer.RawData))
                        return candidate;
                    if (IsDelegatedResponder(candidate, issuer))
                        return candidate;
                }
                return null;
            }

            // ResponderID: byName [1] Name or byKey [2] KeyHash (SHA-1 of the key's BIT STRING value).
            static bool Names(byte[] responderId, X509Certificate2 candidate)
            {
                var reader = new AsnReader(responderId, AsnEncodingRules.DER);
                var tag = reader.PeekTag();
                if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
                {
                    var name = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)).ReadEncodedValue();
                    return name.Span.SequenceEqual(candidate.SubjectName.RawData);
                }
                if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
                {
                    var keyHash = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2, true)).ReadOctetString();
                    return Hash(HashAlgorithmName.SHA1, candidate.PublicKey.EncodedKeyValue.RawData).AsSpan().SequenceEqual(keyHash);
                }
                return false;
            }

            static bool IsDelegatedResponder(X509Certificate2 candidate, X509Certificate2 issuer)
            {
                if (candidate.Extensions["2.5.29.37"] is not X509EnhancedKeyUsageExtension usage
                    || !usage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.9"))
                {
                    return false;
                }
                if (!candidate.IssuerName.RawData.AsSpan().SequenceEqual(issuer.SubjectName.RawData))
                    return false;
                // Signed by the issuer: build the two-certificate chain and see that the issuer is next.
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
#if NET5_0_OR_GREATER
                chain.ChainPolicy.DisableCertificateDownloads = true;
#endif
                chain.ChainPolicy.ExtraStore.Add(issuer);
                chain.Build(candidate);
                return chain.ChainElements.Count > 1 && chain.ChainElements[1].Certificate.RawData.AsSpan().SequenceEqual(issuer.RawData);
            }
        }

        // ===== CRL (RFC 5280 5.1) =====

        static class Crl
        {
            // From a CRL of the certificate's issuer, signed by it: when it was issued and when the certificate was
            // revoked, if it is listed; null if the CRL is not the issuer's or does not verify.
            internal static (DateTimeOffset ThisUpdate, DateTimeOffset? RevocationTime)? Check(byte[] der, X509Certificate2 certificate,
                X509Certificate2 issuer)
            {
                try
                {
                    var list = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
                    var tbsData = list.ReadEncodedValue().ToArray();
                    var algorithm = list.ReadSequence().ReadObjectIdentifier();
                    var signature = list.ReadBitString(out _);

                    var tbs = new AsnReader(tbsData, AsnEncodingRules.DER).ReadSequence();
                    if (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
                        tbs.ReadInteger();                                   // version
                    tbs.ReadSequence();                                      // signature
                    var name = tbs.ReadEncodedValue();
                    if (!name.Span.SequenceEqual(certificate.IssuerName.RawData) || !name.Span.SequenceEqual(issuer.SubjectName.RawData))
                        return null;
                    if (!VerifySignature(issuer, algorithm, tbsData, signature))
                        return null;
                    var thisUpdate = ReadTime(tbs);
                    if (tbs.HasData && (tbs.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) || tbs.PeekTag().HasSameClassAndValue(Asn1Tag.GeneralizedTime)))
                        ReadTime(tbs);                                       // nextUpdate
                    var serial = new BigInteger(certificate.GetSerialNumber());
                    if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                    {
                        var revoked = tbs.ReadSequence();
                        while (revoked.HasData)
                        {
                            var entry = revoked.ReadSequence();
                            if (entry.ReadInteger() == serial)
                                return (thisUpdate, ReadTime(entry));
                        }
                    }
                    return (thisUpdate, null);
                }
                catch (Exception ex) when (ex is AsnContentException or CryptographicException)
                {
                    return null;
                }
            }
        }
    }
}
