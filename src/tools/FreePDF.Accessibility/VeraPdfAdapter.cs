// Original FreePDF implementation of the published veraPDF CLI report contract.
// veraPDF remains an optional, separately installed executable. See PROVENANCE.md.
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace FreePDF.Accessibility;

internal static class VeraPdfAdapter
{
    internal static async Task<IndependentValidation> ValidateAsync(string executable, string input,
        string documentSha256, string profile, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var result = new IndependentValidation { RequestedProfile = profile, DocumentSha256 = documentSha256 };
        if (profile != "ua1") return result with { IncompleteReason = "Unsupported profile. This adapter supports only explicit PDF/UA-1 (ua1)." };
        try
        {
            var output = await ValidatorProcess.RunAsync(executable,
                new[] { "--format", "xml", "--flavour", "ua1", "--maxfailures", "-1", "--maxfailuresdisplayed", "100", input },
                timeout, cancellationToken).ConfigureAwait(false);
            result = Parse(output.Report, output.ExitCode, input, new FileInfo(input).Length, documentSha256);
            // Detect a validator that changed the snapshot, or a concurrent writer.
            using var stream = File.OpenRead(input);
            if (!String.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), documentSha256, StringComparison.Ordinal))
                return Unverified(result, "Input bytes changed during independent validation.");
            return result;
        }
        // InvalidDataException is not an IOException. ValidatorProcess throws it when the validator's output exceeds its limit.
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or TimeoutException or ArgumentException)
        {
            // Do not include process stderr: it may contain passwords or document text.
            return Unverified(result, ex switch
            {
                TimeoutException => "Validator exceeded its time limit.",
                InvalidDataException => "Validator output exceeded its size limit.",
                _ => "Validator could not run or its input could not be read. Check the executable path and local installation."
            });
        }
    }

    // A verdict that cannot be bound to the snapshot bytes describes unknown input. Keep the validator's identity
    // for diagnostics, but drop its counts and findings so they cannot sit beside an incomplete status.
    static IndependentValidation Unverified(IndependentValidation result, string reason) => result with
    {
        Status = IndependentValidationStatus.Incomplete, IncompleteReason = reason,
        PassedRules = null, FailedRules = null, PassedChecks = null, FailedChecks = null,
        Findings = Array.Empty<IndependentFinding>()
    };

    internal static IndependentValidation Parse(byte[] xml, int exitCode, string input, long inputLength, string hash)
    {
        var result = new IndependentValidation
        {
            DocumentSha256 = hash, ExitCode = exitCode,
            ReportSha256 = Convert.ToHexStringLower(SHA256.HashData(xml))
        };
        try
        {
            if (xml.Length > ValidatorProcess.MaximumReportBytes) throw new InvalidDataException();
            using var stream = new MemoryStream(xml, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = ValidatorProcess.MaximumReportBytes
            });
            var root = XDocument.Load(reader).Root ?? throw new InvalidDataException();
            if (root.Name != "report") throw new InvalidDataException();
            var releases = One(root, "buildInformation").Elements("releaseDetails").ToArray();
            var components = releases.ToDictionary(e => Required(e, "id"), e => Required(e, "version"));
            if (!components.TryGetValue("core", out var core) || !components.ContainsKey("validation-model") || core.Length > 100)
                throw new InvalidDataException();
            result = result with { ValidatorVersion = core, Components = components };
            var job = One(One(root, "jobs"), "job");
            var item = One(job, "item");
            var expectedName = Path.GetFullPath(input);
            if (!String.Equals(Path.GetFullPath(One(item, "name").Value), expectedName,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                Number(item, "size") != inputLength) throw new InvalidDataException();
            var validation = One(job, "validationReport");
            var profileName = Required(validation, "profileName");
            result = result with { ProfileName = profileName };
            if (profileName != "PDF/UA-1 validation profile" || Required(validation, "jobEndStatus") != "normal")
                throw new InvalidDataException();
            bool compliant = Required(validation, "isCompliant") switch
            { "true" => true, "false" => false, _ => throw new InvalidDataException() };
            var details = One(validation, "details");
            int passedRules = Number(details, "passedRules"), failedRules = Number(details, "failedRules");
            int passedChecks = Number(details, "passedChecks"), failedChecks = Number(details, "failedChecks");
            if (passedRules + (long)failedRules == 0 || passedChecks + (long)failedChecks == 0 ||
                compliant != (failedRules == 0 && failedChecks == 0) || (failedRules == 0) != (failedChecks == 0))
                throw new InvalidDataException();
            var summary = One(root, "batchSummary");
            if (Number(summary, "totalJobs") != 1 || new[] { "failedToParse", "encrypted", "outOfMemory", "veraExceptions" }.Any(n => Number(summary, n) != 0))
                throw new InvalidDataException();
            var totals = One(summary, "validationReports");
            if (Number(totals, "failedJobs") != 0 || totals.Value.Trim() != "1" ||
                Number(totals, "compliant") != (compliant ? 1 : 0) || Number(totals, "nonCompliant") != (compliant ? 0 : 1))
                throw new InvalidDataException();
            // veraPDF 1.30.3 returns 0 for a pass and 1 for nonconformance.
            if (exitCode != (compliant ? 0 : 1)) throw new InvalidDataException();
            var rules = details.Elements("rule").ToArray();
            if (rules.Any(r => Required(r, "status") is not ("passed" or "failed"))) throw new InvalidDataException();
            if (rules.Any(r => Required(r, "status") == "passed" &&
                    (r.Attribute("failedChecks") != null && Number(r, "failedChecks") != 0 ||
                     r.Elements("check").Any(c => Required(c, "status") != "passed")))) throw new InvalidDataException();
            var failures = rules.Where(r => Required(r, "status") == "failed").ToArray();
            if (failures.Length != failedRules || failures.Sum(r => (long)Number(r, "failedChecks")) != failedChecks)
                throw new InvalidDataException();
            if (failures.Any(r => !r.Elements("check").Any() ||
                    r.Elements("check").Count() > Number(r, "failedChecks") ||
                    r.Elements("check").Any(c => Required(c, "status") != "failed" || String.IsNullOrWhiteSpace(One(c, "context").Value))))
                throw new InvalidDataException();
            var findings = failures.Select(r => new IndependentFinding(
                Required(r, "specification"), Required(r, "clause"), Required(r, "testNumber"), Number(r, "failedChecks"),
                One(r, "description").Value,
                r.Elements("check").Select(c => One(c, "context").Value).ToArray())).ToArray();
            if (findings.Any(f => f.FailedChecks < 1) || findings.Select(f => (f.Specification, f.Clause, f.TestNumber)).Distinct().Count() != findings.Length)
                throw new InvalidDataException();
            return result with
            {
                Status = compliant ? IndependentValidationStatus.MachineChecksPassed : IndependentValidationStatus.Failed,
                PassedRules = passedRules, FailedRules = failedRules, PassedChecks = passedChecks, FailedChecks = failedChecks,
                Findings = findings
            };
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return result with { IncompleteReason = "Validator report was malformed, inconsistent, incomplete, or used an unsupported profile/report format." };
        }
    }

    static XElement One(XElement parent, string name) => parent.Elements(name).Single();
    static string Required(XElement element, string name) =>
        element.Attribute(name)?.Value is { Length: > 0 } value ? value : throw new InvalidDataException();
    static int Number(XElement element, string name) =>
        int.TryParse(Required(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidDataException();
}
