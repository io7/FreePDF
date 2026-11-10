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
    /// and when, whether the signer's chain ends at a trusted root, revocation, later changes, and the PAdES level
    /// (ISO 32000-2 12.8, ETSI EN 319 142-1, RFC 5652, RFC 3161, RFC 6960, RFC 5280). It reports facts and a technical
    /// status, not the legal effect of a signature; EU trusted lists and qualified status are not checked.
    /// </summary>
    public static class PdfSignatureVerifier
    {
        /// <summary>
        /// Checks every signature and document time-stamp of the file, in the order their fields are found. A file that
        /// cannot be opened throws; a signature that cannot be checked is reported as Indeterminate.
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
        /// Checks every signature and document time-stamp of the file, in the order their fields are found. A file that
        /// cannot be opened throws; a signature that cannot be checked is reported as Indeterminate.
        /// </summary>
        public static async Task<IReadOnlyList<PdfSignatureVerification>> VerifyAsync(byte[] file, PdfSignatureVerificationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            options ??= new PdfSignatureVerificationOptions();
            using var document = PdfReader.Open(new MemoryStream(file, false), options.Password, PdfDocumentOpenMode.Import);
            var context = new Context(file, document, options, cancellationToken);

            var results = new List<PdfSignatureVerification>();
            foreach (var signature in Signatures(document))
            {
                var result = new PdfSignatureVerification
                {
                    FieldName = signature.Name,
                    SubFilter = NameOf(signature.Value, "/SubFilter").TrimStart('/'),
                    Revisions = context.RevisionEnds.Count,
                };
                try
                {
                    await CheckAsync(context, signature, result).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    result._reasons.Add($"The signature could not be checked: {ex.Message}");
                    result.Status = PdfSignatureStatus.Indeterminate;
                    result.PadesLevel = PdfPadesLevel.None;
                }
                results.Add(result);
            }

            // B-LTA: a trusted document time-stamp in a later revision covers a DSS that holds the validation data.
            foreach (var result in results.Where(r => r.PadesLevel == PdfPadesLevel.BaselineLT))
            {
                foreach (var stamp in results.Where(s => s.Kind == PdfSignatureKind.DocumentTimeStamp && s.Intact && s.Trusted && s.End > result.End))
                {
                    if (CoveredDssHolds(context, stamp.End, result))
                    {
                        result.PadesLevel = PdfPadesLevel.BaselineLTA;
                        break;
                    }
                }
            }
            return results;
        }

        sealed class Context
        {
            internal Context(byte[] file, PdfDocument document, PdfSignatureVerificationOptions options, CancellationToken cancellationToken)
            {
                File = file;
                Document = document;
                Options = options;
                ValidationTime = options.ValidationTime ?? DateTimeOffset.UtcNow;
                TrustedRoots = options.TrustedRoots?.ToList();
                RevisionEnds = RevisionEnds(file);
                Dss = ReadDss(document);
                Extra = options.Online?.ExtraCertificates?.ToList() ?? [];
                Fetcher = options.Revocation == PdfRevocationCheck.Online
                    ? new PdfDocumentSecurityStore.Fetcher(options.Online ?? new PdfValidationDataOptions(), cancellationToken)
                    : null;
            }

            internal byte[] File { get; }
            internal PdfDocument Document { get; }
            internal PdfSignatureVerificationOptions Options { get; }
            internal DateTimeOffset ValidationTime { get; }
            internal List<X509Certificate2>? TrustedRoots { get; }
            internal List<int> RevisionEnds { get; }
            internal Dss Dss { get; }
            internal List<X509Certificate2> Extra { get; }
            internal PdfDocumentSecurityStore.Fetcher? Fetcher { get; }
        }

        static async Task CheckAsync(Context context, FoundSignature signature, PdfSignatureVerification result)
        {
            var value = signature.Value;
            result.Kind = NameOf(value, "/Type") == "/DocTimeStamp" ? PdfSignatureKind.DocumentTimeStamp
                : signature.CertificationLevel != null ? PdfSignatureKind.Certification
                : PdfSignatureKind.Approval;
            result.CertificationLevel = signature.CertificationLevel;
            try
            {
                result.ClaimedSigningTime = value.Elements.GetDateTime("/M");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result._reasons.Add("The /M signing time is not a PDF date.");
            }
            // Anything that keeps the status from Valid without making it Invalid.
            bool doubtful = false;

            // The byte range: from 0, one gap that is exactly this signature's /Contents.
            if (!ByteRange(context.File, value, out var covered, out var contents, out var end, out var cleanPadding, out var problem))
            {
                result._reasons.Add(problem);
                result.Status = PdfSignatureStatus.Invalid;
                return;
            }
            result.End = end;
            if (!cleanPadding)
            {
                result._reasons.Add("The /Contents holds data after the signature, which the signature does not cover.");
                doubtful = true;
            }
            result.CoversWholeFile = end >= TrimmedLength(context.File);
            int revision = context.RevisionEnds.FindIndex(e => e >= end);
            result.Revision = revision < 0 ? context.RevisionEnds.Count : revision + 1;
            if (!context.RevisionEnds.Contains(end) && !result.CoversWholeFile)
            {
                result._reasons.Add("The byte range does not end at the end of a revision.");
                doubtful = true;
            }

            // Integrity and the signer; a time-stamp's time is only a candidate until its authority is checked.
            SignerInfo? signerInfo = null;
            SignedCms? cms = null;
            (DateTimeOffset Time, X509Certificate2 Authority, string HashOid)? stamp = null;
            bool documentTimeStamp = result.Kind == PdfSignatureKind.DocumentTimeStamp || result.SubFilter == "ETSI.RFC3161";
            if (documentTimeStamp)
            {
                stamp = CheckDocumentTimeStamp(contents, covered, result);
            }
            else if (result.SubFilter is "adbe.pkcs7.detached" or "ETSI.CAdES.detached" or "adbe.pkcs7.sha1")
            {
                (cms, signerInfo, stamp) = CheckCms(contents, covered, result);
            }
            else
            {
                result._reasons.Add($"The /SubFilter {result.SubFilter} is not supported.");
                result.Status = PdfSignatureStatus.Indeterminate;
                return;
            }
            if (!result.Intact)
            {
                result.Status = PdfSignatureStatus.Invalid;
                return;
            }
            var signer = result.Signer!;
            // A signature time-stamp that is there but cannot be verified is a claim without proof.
            if (stamp == null && signerInfo != null
                && signerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>().Any(a => a.Oid.Value == "1.2.840.113549.1.9.16.2.14"))
            {
                doubtful = true;
            }

            // Digests that no longer resist collisions.
            var digest = documentTimeStamp ? stamp?.HashOid : signerInfo?.DigestAlgorithm.Value;
            if (result.SubFilter == "adbe.pkcs7.sha1" || digest is "1.3.14.3.2.26" or "1.2.840.113549.2.5")
            {
                result._reasons.Add("The signature uses a weak digest (SHA-1 or MD5).");
                doubtful = true;
            }

            var available = new List<X509Certificate2>(context.Extra);
            available.AddRange(context.Dss.Certificates);
            if (cms != null)
                available.AddRange(cms.Certificates.Cast<X509Certificate2>());
            if (context.TrustedRoots != null)
                available.AddRange(context.TrustedRoots);

            // What the certificate may be used for.
            bool usable = documentTimeStamp ? IsTimeStampAuthority(signer, result) : IsSigningCertificate(signer, result);

            // The signature time-stamp counts only when its authority is a trusted time-stamp authority at the token's time.
            List<X509Certificate2>? stampChain = null;
            if (!documentTimeStamp && stamp is { } candidate)
            {
                var authority = BuildChain(candidate.Authority, available, context.TrustedRoots, candidate.Time);
                if (authority.Trusted && authority.ValidNow && IsTimeStampAuthority(candidate.Authority, result))
                {
                    result.TimeStampTime = candidate.Time;
                    result.TimeStampAuthority = candidate.Authority;
                    stampChain = authority.Chain;
                }
                else
                {
                    result._reasons.Add("The signature time-stamp is not used: its authority is not a trusted time-stamp authority" +
                        (authority.Problem != null ? $" ({authority.Problem})." : "."));
                    doubtful = true;
                }
            }
            else if (documentTimeStamp && stamp is { } token)
            {
                result.TimeStampTime = token.Time;
                result.TimeStampAuthority = token.Authority;
            }

            // The chain at the validation time; for a signature, else at its trusted time-stamp, which proves it existed then.
            var (chain, trusted, chainProblem, validNow) = BuildChain(signer, available, context.TrustedRoots, context.ValidationTime);
            if (!validNow && !documentTimeStamp && result.TimeStampTime is { } stampTime)
            {
                var atStamp = BuildChain(signer, available, context.TrustedRoots, stampTime);
                if (atStamp.ValidNow)
                {
                    (chain, trusted, chainProblem, validNow) = atStamp;
                    result._reasons.Add($"The certificate is not valid at the validation time; it was at the time-stamp ({stampTime:u}).");
                }
            }
            if (!validNow)
                result._reasons.Add($"The certificate is not valid at {(result.TimeStampTime ?? context.ValidationTime):u}.");
            result.Chain = chain;
            result.Trusted = trusted && validNow && usable;
            if (chainProblem != null)
                result._reasons.Add(chainProblem);

            // Revocation below the roots: the signer's chain at the best proven time.
            var bestTime = result.TimeStampTime ?? context.ValidationTime;
            var fromDss = PdfRevocationStatus.NotChecked;
            if (context.Options.Revocation != PdfRevocationCheck.None)
            {
                fromDss = ChainRevocation(chain, context.Dss, bestTime, context.ValidationTime, null);
                result.Revocation = context.Fetcher != null && fromDss == PdfRevocationStatus.Unknown
                    ? await OnlineRevocationAsync(context, chain, bestTime, result).ConfigureAwait(false)
                    : ChainRevocation(chain, context.Dss, bestTime, context.ValidationTime, result);
            }

            // Later revisions: what the signature's permissions forbid, or what changes the signed content.
            if (!result.CoversWholeFile)
            {
                var (forbidden, unexpected, laterProblem) = LaterChanges(context, end);
                result.ForbiddenChange = forbidden;
                result.UnexpectedChange = unexpected;
                if (forbidden != null)
                    result._reasons.Add($"A later update makes a change the signatures forbid: {forbidden}.");
                if (unexpected != null)
                    result._reasons.Add($"A later update changes more than form fields, signatures, annotations and validation data: {unexpected}.");
                if (laterProblem != null)
                {
                    result._reasons.Add(laterProblem);
                    doubtful = true;
                }
            }

            result.Status = result.ForbiddenChange != null || result.Revocation == PdfRevocationStatus.Revoked
                ? PdfSignatureStatus.Invalid
                : result.Trusted && result.Revocation != PdfRevocationStatus.Unknown && !doubtful && result.UnexpectedChange == null
                    ? PdfSignatureStatus.Valid
                    : PdfSignatureStatus.Indeterminate;

            // The PAdES level of a valid signature. B-LT needs the DSS alone to hold the chains and their revocation data;
            // B-LTA is decided once the document time-stamps are known.
            if (result.Status != PdfSignatureStatus.Valid || result.SubFilter != "ETSI.CAdES.detached" || signerInfo == null
                || !SigningCertificateMatches(signerInfo, signer, result))
            {
                return;
            }
            result.PadesLevel = PdfPadesLevel.BaselineB;
            if (result.TimeStampTime is not { } signedAt || stampChain == null)
                return;
            result.PadesLevel = PdfPadesLevel.BaselineT;
            result.LongTermChains = [(chain, bestTime), (stampChain, signedAt)];
            if (context.Dss.Present && result.LongTermChains.All(c => DssCovers(context.Dss, c.Chain, c.Time, context.ValidationTime)))
                result.PadesLevel = PdfPadesLevel.BaselineLT;
        }

        // The DSS holds every certificate of the chain and revocation data that shows it not revoked at the time.
        static bool DssCovers(Dss dss, List<X509Certificate2> chain, DateTimeOffset time, DateTimeOffset validationTime)
            => chain.All(dss.Holds) && ChainRevocation(chain, dss, time, validationTime, null) == PdfRevocationStatus.Good;

        // The DSS of the revision a document time-stamp covers holds the validation data of the signature.
        static bool CoveredDssHolds(Context context, int stampEnd, PdfSignatureVerification result)
        {
            try
            {
                using var revision = PdfReader.Open(new MemoryStream(context.File, 0, stampEnd, false), context.Options.Password, PdfDocumentOpenMode.Import);
                var dss = ReadDss(revision);
                return dss.Present && result.LongTermChains.All(c => DssCovers(dss, c.Chain, c.Time, context.ValidationTime));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return false;
            }
        }

        // ===== Byte range =====

        static bool ByteRange(byte[] file, PdfDictionary value, out byte[] covered, out byte[] contents, out int end, out bool cleanPadding,
            out string problem)
        {
            covered = [];
            contents = [];
            end = 0;
            cleanPadding = true;
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
            if (a != 0 || b < 2 || c <= b + 1 || c + d > file.Length || (c - b) % 2 != 0)
            {
                problem = "The /ByteRange does not start at 0, has no even gap, or runs past the end of the file.";
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
            // /Contents is padded with zeros after the DER value; anything else there is not signed.
            try
            {
                var length = new AsnReader(hex, AsnEncodingRules.BER).ReadEncodedValue().Length;
                contents = hex.AsSpan(0, length).ToArray();
                cleanPadding = hex.Skip(length).All(b => b == 0);
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

        static (SignedCms? Cms, SignerInfo? SignerInfo, (DateTimeOffset, X509Certificate2, string)? Stamp) CheckCms(byte[] contents,
            byte[] covered, PdfSignatureVerification result)
        {
            SignedCms cms;
            try
            {
                if (result.SubFilter == "adbe.pkcs7.sha1")
                {
                    // The CMS signs the SHA-1 digest of the covered bytes as its encapsulated content.
                    cms = new SignedCms();
                    cms.Decode(contents);
                    if (!cms.ContentInfo.Content.AsSpan().SequenceEqual(Hash(HashAlgorithmName.SHA1, covered)))
                    {
                        result._reasons.Add("The signed SHA-1 digest does not match the covered bytes.");
                        return (cms, null, null);
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
                return (null, null, null);
            }
            if (cms.SignerInfos.Count != 1)
            {
                result._reasons.Add($"The CMS has {cms.SignerInfos.Count} signers; PDF signatures have one.");
                return (cms, null, null);
            }
            var signerInfo = cms.SignerInfos[0];
            result.Signer = signerInfo.Certificate;
            if (result.Signer == null)
            {
                result._reasons.Add("The signer's certificate is not in the CMS.");
                return (cms, signerInfo, null);
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
                return (cms, signerInfo, null);
            }
            return (cms, signerInfo, SignatureTimeStamp(signerInfo, result));
        }

        // The signature time-stamp (RFC 3161 token over the signature value), when there is one and it verifies.
        static (DateTimeOffset, X509Certificate2, string)? SignatureTimeStamp(SignerInfo signerInfo, PdfSignatureVerification result)
        {
            var attribute = signerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>()
                .FirstOrDefault(a => a.Oid.Value == "1.2.840.113549.1.9.16.2.14");
            if (attribute == null || attribute.Values.Count == 0)
                return null;
#if NET8_0_OR_GREATER
            try
            {
                if (!Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _))
                {
                    result._reasons.Add("The signature time-stamp is not an RFC 3161 token.");
                    return null;
                }
                if (!token.VerifySignatureForSignerInfo(signerInfo, out var authority) || authority == null)
                {
                    result._reasons.Add("The signature time-stamp does not verify against the signature.");
                    return null;
                }
                return (token.TokenInfo.Timestamp, authority, token.TokenInfo.HashAlgorithmId.Value ?? "");
            }
            catch (CryptographicException ex)
            {
                result._reasons.Add($"The signature time-stamp cannot be read: {ex.Message}");
                return null;
            }
#else
            result._reasons.Add("Time-stamps are checked on .NET 8 or later only.");
            return null;
#endif
        }

        static (DateTimeOffset, X509Certificate2, string)? CheckDocumentTimeStamp(byte[] contents, byte[] covered, PdfSignatureVerification result)
        {
#if NET8_0_OR_GREATER
            try
            {
                if (!Rfc3161TimestampToken.TryDecode(contents, out var token, out _))
                {
                    result._reasons.Add("The document time-stamp is not an RFC 3161 token.");
                    return null;
                }
                if (!token.VerifySignatureForData(covered, out var authority) || authority == null)
                {
                    result._reasons.Add("The document time-stamp does not match the covered bytes.");
                    return null;
                }
                result.Intact = true;
                result.Signer = authority;
                return (token.TokenInfo.Timestamp, authority, token.TokenInfo.HashAlgorithmId.Value ?? "");
            }
            catch (CryptographicException ex)
            {
                result._reasons.Add($"The document time-stamp cannot be read: {ex.Message}");
                return null;
            }
#else
            result._reasons.Add("Document time-stamps are checked on .NET 8 or later only.");
            return null;
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
                if (attribute.Values.Count == 0)
                    break;
                try
                {
                    var certId = new AsnReader(attribute.Values[0].RawData, AsnEncodingRules.DER).ReadSequence().ReadSequence().ReadSequence();
                    HashAlgorithmName? algorithm = HashAlgorithmName.SHA1;
                    if (v2)
                    {
                        algorithm = HashAlgorithmName.SHA256;
                        if (certId.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                            algorithm = HashFromOid(certId.ReadSequence().ReadObjectIdentifier());
                    }
                    if (algorithm is not { } hash)
                    {
                        result._reasons.Add("The signing-certificate attribute uses an unknown hash.");
                        return false;
                    }
                    if (Hash(hash, signer.RawData).AsSpan().SequenceEqual(certId.ReadOctetString()))
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

        // ===== Certificates =====

        const string TimeStampingUsage = "1.3.6.1.5.5.7.3.8";
        const string OcspSigningUsage = "1.3.6.1.5.5.7.3.9";

        // A certificate for signing documents: digital signature or non-repudiation when key usage is given, and not one
        // whose extended key usages are only for TLS servers, code, time-stamps or OCSP.
        static bool IsSigningCertificate(X509Certificate2 certificate, PdfSignatureVerification result)
        {
            if (certificate.Extensions["2.5.29.15"] is X509KeyUsageExtension usage
                && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
            {
                result._reasons.Add("The signer's certificate is not for digital signatures (key usage).");
                return false;
            }
            if (certificate.Extensions["2.5.29.37"] is X509EnhancedKeyUsageExtension extended)
            {
                var purposes = extended.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value).ToList();
                string[] otherPurposes = ["1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.3", TimeStampingUsage, OcspSigningUsage];
                if (purposes.Count > 0 && purposes.All(p => otherPurposes.Contains(p)))
                {
                    result._reasons.Add("The signer's certificate is for other purposes (extended key usage).");
                    return false;
                }
            }
            return true;
        }

        // RFC 3161 2.3: a time-stamp authority's certificate has the timeStamping extended key usage.
        static bool IsTimeStampAuthority(X509Certificate2 certificate, PdfSignatureVerification result)
        {
            if (certificate.Extensions["2.5.29.37"] is X509EnhancedKeyUsageExtension extended
                && extended.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == TimeStampingUsage))
            {
                return true;
            }
            result._reasons.Add($"'{certificate.Subject}' is not a time-stamp authority's certificate (no timeStamping extended key usage).");
            return false;
        }

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
            var flags = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, status) => all | status.Status);
            bool timeValid = (flags & X509ChainStatusFlags.NotTimeValid) == 0;
#if !NET5_0_OR_GREATER
            // Without custom root trust, the root must be one of the given ones.
            var root = elements.LastOrDefault();
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

        // The issuer's signature on a certificate: its tbsCertificate, signatureAlgorithm and signatureValue.
        static bool IsSignedBy(X509Certificate2 certificate, X509Certificate2 issuer)
        {
            try
            {
                var outer = new AsnReader(certificate.RawData, AsnEncodingRules.DER).ReadSequence();
                var tbs = outer.ReadEncodedValue().ToArray();
                var algorithm = outer.ReadSequence().ReadObjectIdentifier();
                var signature = outer.ReadBitString(out _);
                return certificate.IssuerName.RawData.AsSpan().SequenceEqual(issuer.SubjectName.RawData)
                       && VerifySignature(issuer, algorithm, tbs, signature);
            }
            catch (AsnContentException)
            {
                return false;
            }
        }

        // ===== Revocation =====

        // The revocation status of a chain below its root, from the DSS; the problems go to the result when given.
        static PdfRevocationStatus ChainRevocation(List<X509Certificate2> chain, Dss dss, DateTimeOffset bestTime, DateTimeOffset validationTime,
            PdfSignatureVerification? result)
        {
            var status = PdfRevocationStatus.Good;
            for (int index = 0; index + 1 < chain.Count; index++)
            {
                var found = Revocation(chain[index], chain[index + 1], dss.OcspResponses, dss.Crls, bestTime, validationTime);
                if (found.Problem != null)
                    result?._reasons.Add(found.Problem);
                if (found.Status == PdfRevocationStatus.Revoked)
                    return PdfRevocationStatus.Revoked;
                if (found.Status == PdfRevocationStatus.Unknown)
                    status = PdfRevocationStatus.Unknown;
            }
            return status;
        }

        // The DSS first, then the responders and CRLs the certificates name.
        static async Task<PdfRevocationStatus> OnlineRevocationAsync(Context context, List<X509Certificate2> chain, DateTimeOffset bestTime,
            PdfSignatureVerification result)
        {
            var fetcher = context.Fetcher!;
            var status = PdfRevocationStatus.Good;
            for (int index = 0; index + 1 < chain.Count; index++)
            {
                var certificate = chain[index];
                var issuer = chain[index + 1];
                var found = Revocation(certificate, issuer, context.Dss.OcspResponses, context.Dss.Crls, bestTime, context.ValidationTime);
                if (found.Status == PdfRevocationStatus.Unknown)
                {
                    var ocsp = await fetcher.OcspAsync(certificate, issuer).ConfigureAwait(false);
                    var crls = ocsp == null ? await fetcher.CrlsAsync(certificate).ConfigureAwait(false) : [];
                    found = Revocation(certificate, issuer, ocsp == null ? [] : [ocsp.Value.Response], crls, bestTime, context.ValidationTime);
                    result._reasons.AddRange(fetcher.Problems);
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

        // The status of one certificate from verified OCSP responses, then CRLs. Data counts when it was issued at or
        // after the best signature time (it shows the certificate was not revoked then), or when its validity covers the
        // validation time.
        static (PdfRevocationStatus Status, string? Problem) Revocation(X509Certificate2 certificate, X509Certificate2 issuer,
            List<byte[]> ocspResponses, List<byte[]> crls, DateTimeOffset bestTime, DateTimeOffset validationTime)
        {
            string? problem = null;
            bool Fresh(DateTimeOffset thisUpdate, DateTimeOffset? nextUpdate)
                => thisUpdate >= bestTime.AddSeconds(-1)
                   || (nextUpdate is { } next && thisUpdate <= validationTime && validationTime <= next);

            foreach (var response in ocspResponses)
            {
                if (Ocsp.Check(response, certificate, issuer) is not { } answer)
                    continue;
                if (answer.RevocationTime is { } revoked)
                {
                    return revoked <= bestTime
                        ? (PdfRevocationStatus.Revoked, $"'{certificate.Subject}' was revoked at {revoked:u} (OCSP).")
                        : (PdfRevocationStatus.Good, $"'{certificate.Subject}' was revoked at {revoked:u}, after the signature (OCSP).");
                }
                if (Fresh(answer.ThisUpdate, answer.NextUpdate))
                    return (PdfRevocationStatus.Good, null);
                problem = $"The OCSP response for '{certificate.Subject}' is older than the signature and no longer current.";
            }
            foreach (var crl in crls)
            {
                if (Crl.Check(crl, certificate, issuer) is not { } answer)
                    continue;
                if (answer.RevocationTime is { } revoked)
                {
                    return revoked <= bestTime
                        ? (PdfRevocationStatus.Revoked, $"'{certificate.Subject}' was revoked at {revoked:u} (CRL).")
                        : (PdfRevocationStatus.Good, $"'{certificate.Subject}' was revoked at {revoked:u}, after the signature (CRL).");
                }
                if (Fresh(answer.ThisUpdate, answer.NextUpdate))
                    return (PdfRevocationStatus.Good, null);
                problem = $"The CRL for '{certificate.Subject}' is older than the signature and no longer current.";
            }
            return (PdfRevocationStatus.Unknown, problem ?? $"No revocation data for '{certificate.Subject}'.");
        }

        // ===== Later changes =====

        // The first change after the signed revision that its permissions forbid; else, when only the signed fields are
        // protected (an approval signature), the first change beyond P=3; or a problem that kept the check from running.
        static (string? Forbidden, string? Unexpected, string? Problem) LaterChanges(Context context, int end)
        {
            var document = context.Document;
            var revisionBytes = context.File.AsSpan(0, end).ToArray();
            PdfDocument revision;
            try
            {
                revision = PdfReader.Open(new MemoryStream(revisionBytes, false), context.Options.Password, PdfDocumentOpenMode.Import);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return (null, null, $"The signed revision cannot be read on its own: {ex.Message}");
            }
            using (revision)
            {
                var permissions = PdfModificationPermissions.Read(revision);
                if (permissions == null)
                    return (null, null, "The signed revision has no signed signature field.");

                // A later trailer may point at another catalog or encryption dictionary, which no changed object shows.
                string? replaced = null;
                if (revision.Catalog.ObjectID != document.Catalog.ObjectID)
                    replaced = "replacing the document catalog (trailer /Root)";
                else if ((revision.Trailer.Elements["/Encrypt"] as PdfReference)?.ObjectID != (document.Trailer.Elements["/Encrypt"] as PdfReference)?.ObjectID)
                    replaced = "replacing the encryption dictionary (trailer /Encrypt)";

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

                var rules = permissions.LimitsChanges ? permissions : permissions.AtMost(3);
                if (replaced != null)
                    return permissions.LimitsChanges ? (replaced, null, null) : (null, replaced, null);
                try
                {
                    rules.Check(document, revisionBytes, written, context.Options.Password);
                    return (null, null, null);
                }
                catch (InvalidOperationException ex) when (ex.Data[PdfModificationPermissions.RefusedChangeKey] is string change)
                {
                    // A signed field itself is always protected; otherwise an approval signature permits the change.
                    bool forbidden = permissions.LimitsChanges || ex.Data[PdfModificationPermissions.ProtectedKey] is true;
                    return forbidden ? (change, null, null) : (null, change, null);
                }
            }
        }

        // ===== Document =====

        sealed record FoundSignature(string Name, PdfDictionary Value, int? CertificationLevel);

        // The signature fields with a value, with fully qualified names, and the certification level of the one the
        // catalog's /Perms /DocMDP names or whose /Reference has the DocMDP transform.
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
                var partial = Resolve(field.Elements["/T"]) is PdfString t ? t.Value : "";
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

        static string NameOf(PdfDictionary dictionary, string key) => Resolve(dictionary.Elements[key]) is PdfName name ? name.Value : "";

        static bool HasDocMdpReference(PdfDictionary value)
            => Resolve(value.Elements["/Reference"]) is PdfArray references
               && references.Elements.Any(r => Resolve(r) is PdfDictionary reference && NameOf(reference, "/TransformMethod") == "/DocMDP");

        static int DocMdpLevel(PdfDictionary value)
        {
            if (Resolve(value.Elements["/Reference"]) is PdfArray references)
            {
                foreach (var item in references.Elements)
                {
                    if (Resolve(item) is PdfDictionary reference && NameOf(reference, "/TransformMethod") == "/DocMDP")
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
            internal List<X509Certificate2> Certificates { get; } = [];
            internal List<byte[]> OcspResponses { get; } = [];
            internal List<byte[]> Crls { get; } = [];

            internal bool Holds(X509Certificate2 certificate)
                => Certificates.Any(c => c.RawData.AsSpan().SequenceEqual(certificate.RawData));
        }

        static Dss ReadDss(PdfDocument document)
        {
            if (Resolve(document.Catalog.Elements["/DSS"]) is not PdfDictionary dictionary)
                return new Dss();
            var dss = new Dss { Present = true };
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

        // A signature of a certificate, a CRL or an OCSP response: RSA with PKCS #1 v1.5 or ECDSA, with SHA-1 to SHA-512.
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
            catch (Exception ex) when (ex is CryptographicException or AsnContentException)
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

        static DateTimeOffset ReadTime(AsnReader reader)
            => reader.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? reader.ReadUtcTime() : reader.ReadGeneralizedTime();

        static bool IsTime(Asn1Tag tag) => tag.HasSameClassAndValue(Asn1Tag.UtcTime) || tag.HasSameClassAndValue(Asn1Tag.GeneralizedTime);

        static bool IsCa(X509Certificate2 certificate)
            => certificate.Extensions["2.5.29.19"] is X509BasicConstraintsExtension { CertificateAuthority: true };

        // ===== OCSP (RFC 6960) =====

        static class Ocsp
        {
            // The answer of a verified response about the certificate, or null if the response is not about it, not
            // successful, says unknown, or is not signed by the issuer or a responder the issuer authorized.
            internal static (DateTimeOffset? RevocationTime, DateTimeOffset ThisUpdate, DateTimeOffset? NextUpdate)? Check(byte[] der,
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
                    (DateTimeOffset?, DateTimeOffset, DateTimeOffset?)? answer = null;
                    while (responses.HasData && answer == null)
                    {
                        var single = responses.ReadSequence();
                        if (!IsAbout(single.ReadSequence(), certificate, issuer))
                            continue;
                        var tag = single.PeekTag();
                        if (tag.TagClass != TagClass.ContextSpecific || tag.TagValue is not (0 or 1))
                            return null;   // unknown
                        DateTimeOffset? revocationTime = null;
                        if (tag.TagValue == 1)
                            revocationTime = single.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, true)).ReadGeneralizedTime();
                        else
                            single.ReadNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                        var thisUpdate = single.ReadGeneralizedTime();
                        DateTimeOffset? nextUpdate = null;
                        if (single.HasData && single.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                            nextUpdate = single.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadGeneralizedTime();
                        answer = (revocationTime, thisUpdate, nextUpdate);
                    }
                    if (answer == null || Responder(responderId, issuer, certificates, producedAt) is not { } responder
                        || !VerifySignature(responder, algorithm, tbsData, signature))
                    {
                        return null;
                    }
                    return answer;
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

            // The responder (RFC 6960 4.2.2.2): the issuer itself, or a certificate the issuer signed for OCSP signing
            // (id-kp-OCSPSigning), valid when the response was produced, with id-pkix-ocsp-nocheck so that its own
            // revocation need not be checked.
            static X509Certificate2? Responder(byte[] responderId, X509Certificate2 issuer, List<X509Certificate2> certificates,
                DateTimeOffset producedAt)
            {
                if (Names(responderId, issuer))
                    return issuer;
                foreach (var candidate in certificates)
                {
                    if (!Names(responderId, candidate))
                        continue;
                    bool authorized = candidate.Extensions["2.5.29.37"] is X509EnhancedKeyUsageExtension usage
                                      && usage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == OcspSigningUsage);
                    bool noCheck = candidate.Extensions["1.3.6.1.5.5.7.48.1.5"] != null;
                    bool keyUsage = candidate.Extensions["2.5.29.15"] is not X509KeyUsageExtension ku
                                    || (ku.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) != 0;
                    bool valid = candidate.NotBefore.ToUniversalTime() <= producedAt.UtcDateTime && producedAt.UtcDateTime <= candidate.NotAfter.ToUniversalTime();
                    if (authorized && noCheck && keyUsage && valid && IsSignedBy(candidate, issuer))
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
        }

        // ===== CRL (RFC 5280 5.1) =====

        static class Crl
        {
            // CRL extensions this reader understands; a CRL with another critical extension is not used (RFC 5280 5.2).
            static readonly string[] KnownExtensions =
                ["2.5.29.20", "2.5.29.35", "2.5.29.28", "2.5.29.27", "2.5.29.46", "1.3.6.1.5.5.7.1.1"];

            // From a complete CRL of the certificate's issuer, signed by it, whose scope includes the certificate: when it
            // was issued, its next update, and when the certificate was revoked, if it is listed; null if the CRL cannot
            // be used for it.
            internal static (DateTimeOffset ThisUpdate, DateTimeOffset? NextUpdate, DateTimeOffset? RevocationTime)? Check(byte[] der,
                X509Certificate2 certificate, X509Certificate2 issuer)
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
                    // The issuer must be allowed to sign CRLs, and must have signed this one.
                    if (issuer.Extensions["2.5.29.15"] is X509KeyUsageExtension usage && (usage.KeyUsages & X509KeyUsageFlags.CrlSign) == 0)
                        return null;
                    if (!VerifySignature(issuer, algorithm, tbsData, signature))
                        return null;

                    var thisUpdate = ReadTime(tbs);
                    DateTimeOffset? nextUpdate = tbs.HasData && IsTime(tbs.PeekTag()) ? ReadTime(tbs) : null;
                    DateTimeOffset? revocationTime = null;
                    var serial = new BigInteger(certificate.GetSerialNumber());
                    if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
                    {
                        var revoked = tbs.ReadSequence();
                        while (revoked.HasData)
                        {
                            var entry = revoked.ReadSequence();
                            if (entry.ReadInteger() != serial)
                                continue;
                            var time = ReadTime(entry);
                            // removeFromCRL (8) in a delta CRL undoes a hold; it is not a revocation.
                            if (!(entry.HasData && ReasonCode(entry.ReadSequence()) == 8))
                                revocationTime = time;
                        }
                    }
                    if (tbs.HasData && tbs.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0))
                        && !ScopeIncludes(tbs.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)).ReadSequence(), certificate))
                    {
                        return null;
                    }
                    return (thisUpdate, nextUpdate, revocationTime);
                }
                catch (Exception ex) when (ex is AsnContentException or CryptographicException)
                {
                    return null;
                }
            }

            static int? ReasonCode(AsnReader extensions)
            {
                while (extensions.HasData)
                {
                    var extension = extensions.ReadSequence();
                    if (extension.ReadObjectIdentifier() != "2.5.29.21")
                        continue;
                    if (extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean))
                        extension.ReadBoolean();
                    var value = new AsnReader(extension.ReadOctetString(), AsnEncodingRules.DER).ReadEnumeratedBytes().Span;
                    return value.Length == 1 ? value[0] : null;
                }
                return null;
            }

            // A complete CRL whose issuing distribution point, if any, covers the certificate: not a delta CRL, no
            // unknown critical extension, no reasons subset, not indirect, the right kind of certificate, and a
            // distribution point the certificate names.
            static bool ScopeIncludes(AsnReader extensions, X509Certificate2 certificate)
            {
                while (extensions.HasData)
                {
                    var extension = extensions.ReadSequence();
                    var oid = extension.ReadObjectIdentifier();
                    bool critical = extension.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && extension.ReadBoolean();
                    var value = extension.ReadOctetString();
                    if (oid == "2.5.29.27")
                        return false;   // deltaCRLIndicator
                    if (critical && !KnownExtensions.Contains(oid))
                        return false;
                    if (oid != "2.5.29.28")
                        continue;
                    var point = new AsnReader(value, AsnEncodingRules.DER).ReadSequence();
                    var names = new List<byte[]>();
                    while (point.HasData)
                    {
                        var tag = point.PeekTag();
                        if (tag.TagClass != TagClass.ContextSpecific)
                            return false;
                        switch (tag.TagValue)
                        {
                            case 0:   // distributionPoint, with fullName [0] GeneralNames
                                var dp = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                                if (dp.HasData && dp.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                                {
                                    var full = dp.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                                    while (full.HasData)
                                        names.Add(full.ReadEncodedValue().ToArray());
                                }
                                break;
                            case 1:   // onlyContainsUserCerts
                                if (point.ReadBoolean(new Asn1Tag(TagClass.ContextSpecific, 1)) && IsCa(certificate))
                                    return false;
                                break;
                            case 2:   // onlyContainsCACerts
                                if (point.ReadBoolean(new Asn1Tag(TagClass.ContextSpecific, 2)) && !IsCa(certificate))
                                    return false;
                                break;
                            default:  // onlySomeReasons, indirectCRL, onlyContainsAttributeCerts
                                return false;
                        }
                    }
                    if (names.Count > 0 && !CertificateNames(certificate).Any(n => names.Any(m => m.AsSpan().SequenceEqual(n))))
                        return false;
                }
                return true;
            }

            // The full names (GeneralName encodings) of the certificate's CRL distribution points.
            static List<byte[]> CertificateNames(X509Certificate2 certificate)
            {
                var result = new List<byte[]>();
                if (certificate.Extensions["2.5.29.31"] is not { } extension)
                    return result;
                var points = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
                while (points.HasData)
                {
                    var point = points.ReadSequence();
                    if (!point.HasData || !point.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        continue;
                    var dp = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                    if (!dp.HasData || !dp.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                        continue;
                    var full = dp.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true));
                    while (full.HasData)
                        result.Add(full.ReadEncodedValue().ToArray());
                }
                return result;
            }
        }
    }
}
