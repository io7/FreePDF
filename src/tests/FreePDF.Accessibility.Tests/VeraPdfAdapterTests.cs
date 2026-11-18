// Original FreePDF synthetic reports and negative controls. See LICENSE.
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace FreePDF.Accessibility.Tests;

public sealed class VeraPdfAdapterTests
{
    static readonly string Input = Path.GetFullPath("synthetic.pdf");
    const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    internal static XDocument Report(bool passed = true)
    {
        int failed = passed ? 0 : 1;
        return new XDocument(new XElement("report",
            new XElement("buildInformation", new XElement("releaseDetails", new XAttribute("id", "core"), new XAttribute("version", "1.30.3")),
                new XElement("releaseDetails", new XAttribute("id", "validation-model"), new XAttribute("version", "1.30.3"))),
            new XElement("jobs", new XElement("job",
                new XElement("item", new XAttribute("size", 123), new XElement("name", Input)),
                new XElement("validationReport", new XAttribute("jobEndStatus", "normal"), new XAttribute("profileName", "PDF/UA-1 validation profile"),
                    new XAttribute("isCompliant", passed ? "true" : "false"),
                    new XElement("details", new XAttribute("passedRules", 106 - failed), new XAttribute("failedRules", failed),
                        new XAttribute("passedChecks", 200), new XAttribute("failedChecks", failed),
                        passed ? null : new XElement("rule", new XAttribute("specification", "ISO 14289-1:2014"), new XAttribute("clause", "7.3"),
                            new XAttribute("testNumber", "1"), new XAttribute("status", "failed"), new XAttribute("failedChecks", 1),
                            new XElement("description", "Synthetic figure has no description."),
                            new XElement("check", new XAttribute("status", "failed"), new XElement("context", "root/figure[0]"))))))),
            new XElement("batchSummary", new XAttribute("totalJobs", 1), new XAttribute("failedToParse", 0), new XAttribute("encrypted", 0),
                new XAttribute("outOfMemory", 0), new XAttribute("veraExceptions", 0),
                new XElement("validationReports", new XAttribute("compliant", passed ? 1 : 0), new XAttribute("nonCompliant", failed), new XAttribute("failedJobs", 0), "1"))));
    }

    static IndependentValidation Parse(XDocument report, int exit = 0) =>
        VeraPdfAdapter.Parse(Encoding.UTF8.GetBytes(report.ToString()), exit, Input, 123, Hash);

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public void ExplicitVerdictsRetainIdentityAndHumanReview(bool passed, int exit)
    {
        var report = Parse(Report(passed), exit);
        Assert.Equal(passed ? IndependentValidationStatus.MachineChecksPassed : IndependentValidationStatus.Failed, report.Status);
        Assert.Equal("1.30.3", report.ValidatorVersion);
        Assert.Equal("PDF/UA-1 validation profile", report.ProfileName);
        Assert.Equal(Hash, report.DocumentSha256);
        Assert.Equal(64, report.ReportSha256!.Length);
        Assert.True(report.HumanReviewRequired);
        Assert.Equal(passed ? 0 : 1, report.Findings.Count);
    }

    [Theory]
    [InlineData("missing-profile")]
    [InlineData("wrong-profile")]
    [InlineData("missing-version")]
    [InlineData("missing-model-version")]
    [InlineData("missing-summary")]
    [InlineData("second-job")]
    [InlineData("wrong-file")]
    [InlineData("wrong-size")]
    [InlineData("parse-failure")]
    [InlineData("encrypted")]
    [InlineData("oom")]
    [InlineData("exception")]
    [InlineData("job-failed")]
    [InlineData("terminated")]
    [InlineData("zero-rules")]
    [InlineData("zero-checks")]
    [InlineData("contradictory-summary")]
    [InlineData("contradictory-failures")]
    [InlineData("invalid-boolean")]
    [InlineData("negative-count")]
    [InlineData("duplicate-report")]
    [InlineData("duplicate-component")]
    public void InvalidOrIncompleteReportsNeverPass(string defect)
    {
        var xml = Report();
        var validation = xml.Descendants("validationReport").Single();
        var details = xml.Descendants("details").Single();
        var summary = xml.Descendants("batchSummary").Single();
        var totals = xml.Descendants("validationReports").Single();
        switch (defect)
        {
            case "missing-profile": validation.Attribute("profileName")!.Remove(); break;
            case "wrong-profile": validation.SetAttributeValue("profileName", "PDF/A-1b validation profile"); break;
            case "missing-version": xml.Descendants("releaseDetails").First().Attribute("version")!.Remove(); break;
            case "missing-model-version": xml.Descendants("releaseDetails").Last().Remove(); break;
            case "missing-summary": summary.Remove(); break;
            case "second-job": xml.Descendants("jobs").Single().Add(new XElement(xml.Descendants("job").Single())); break;
            case "wrong-file": xml.Descendants("name").Single().Value = Path.GetFullPath("other.pdf"); break;
            case "wrong-size": xml.Descendants("item").Single().SetAttributeValue("size", 1); break;
            case "parse-failure": summary.SetAttributeValue("failedToParse", 1); break;
            case "encrypted": summary.SetAttributeValue("encrypted", 1); break;
            case "oom": summary.SetAttributeValue("outOfMemory", 1); break;
            case "exception": summary.SetAttributeValue("veraExceptions", 1); break;
            case "job-failed": totals.SetAttributeValue("failedJobs", 1); break;
            case "terminated": validation.SetAttributeValue("jobEndStatus", "maxFailures"); break;
            case "zero-rules": details.SetAttributeValue("passedRules", 0); break;
            case "zero-checks": details.SetAttributeValue("passedChecks", 0); break;
            case "contradictory-summary": totals.SetAttributeValue("nonCompliant", 1); break;
            case "contradictory-failures": details.SetAttributeValue("failedChecks", 1); break;
            case "invalid-boolean": validation.SetAttributeValue("isCompliant", "yes"); break;
            case "negative-count": details.SetAttributeValue("passedChecks", -1); break;
            case "duplicate-report": validation.Parent!.Add(new XElement(validation)); break;
            case "duplicate-component": xml.Descendants("buildInformation").Single().Add(new XElement(xml.Descendants("releaseDetails").First())); break;
        }
        Assert.Equal(IndependentValidationStatus.Incomplete, Parse(xml).Status);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 17)]
    [InlineData(false, 0)]
    [InlineData(false, 17)]
    public void ExitCodeMustAgreeWithReport(bool passed, int exit) =>
        Assert.Equal(IndependentValidationStatus.Incomplete, Parse(Report(passed), exit).Status);

    [Fact]
    public void MissingFailedRuleIsIncomplete()
    {
        var xml = Report(false);
        xml.Descendants("rule").Single().Remove();
        Assert.Equal(IndependentValidationStatus.Incomplete, Parse(xml, 1).Status);
    }

    [Fact]
    public void FailureHiddenInPassedRuleIsIncomplete()
    {
        var xml = Report();
        var rule = new XElement(Report(false).Descendants("rule").Single());
        rule.SetAttributeValue("status", "passed");
        xml.Descendants("details").Single().Add(rule);
        Assert.Equal(IndependentValidationStatus.Incomplete, Parse(xml).Status);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-status")]
    [InlineData("empty-context")]
    [InlineData("too-many")]
    public void FailedOccurrencesMustAgreeWithRule(string defect)
    {
        var xml = Report(false);
        var check = xml.Descendants("check").Single();
        switch (defect)
        {
            case "missing": check.Remove(); break;
            case "wrong-status": check.SetAttributeValue("status", "passed"); break;
            case "empty-context": check.Element("context")!.Value = ""; break;
            case "too-many": check.Parent!.Add(new XElement(check)); break;
        }
        Assert.Equal(IndependentValidationStatus.Incomplete, Parse(xml, 1).Status);
    }

    [Theory]
    [InlineData("not XML")]
    [InlineData("<report>")]
    [InlineData("<!DOCTYPE report [<!ENTITY x SYSTEM 'file:///unread'>]><report>&x;</report>")]
    public void MalformedOrEntityReportsAreIncomplete(string xml) =>
        Assert.Equal(IndependentValidationStatus.Incomplete, VeraPdfAdapter.Parse(Encoding.UTF8.GetBytes(xml), 0, Input, 123, Hash).Status);

    [Fact]
    public async Task MissingExecutableIsIncomplete()
    {
        var result = await VeraPdfAdapter.ValidateAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), Input, Hash, "ua1", TimeSpan.FromSeconds(5));
        Assert.Equal(IndependentValidationStatus.Incomplete, result.Status);
        Assert.NotNull(result.IncompleteReason);
    }

    [Fact]
    public async Task UnsupportedProfileDoesNotStartAProcess()
    {
        var result = await VeraPdfAdapter.ValidateAsync("must-not-run", Input, Hash, "ua2", TimeSpan.FromSeconds(5));
        Assert.Equal(IndependentValidationStatus.Incomplete, result.Status);
        Assert.Contains("Unsupported profile", result.IncompleteReason!);
        Assert.Equal("ua2", result.RequestedProfile);
    }

    [Theory]
    [InlineData("unchanged", "MachineChecksPassed", null)]
    [InlineData("failing", "Failed", null)]
    [InlineData("change-input", "Incomplete", "Input bytes changed during independent validation.")]
    [InlineData("failing-change-input", "Incomplete", "Input bytes changed during independent validation.")]
    [InlineData("bad-report", "Incomplete", "Validator report was malformed")]
    [InlineData("stderr-flood", "Incomplete", "Validator output exceeded its size limit.")]
    [InlineData("failing-unreadable", "Incomplete", "Validator could not run or its input could not be read.")]
    public async Task AdapterBindsTheVerdictToUnchangedSnapshotBytes(string content, string expected, string? reason)
    {
        // File permissions cannot remove read access on Windows or for a privileged process.
        if (content == "failing-unreadable" && (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)) return;
        var status = Enum.Parse<IndependentValidationStatus>(expected);
        var input = Path.GetTempFileName();
        try
        {
            File.WriteAllText(input, content);
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(input)));
            string executable = Path.Combine(AppContext.BaseDirectory, "FreePDF.Accessibility.Tests" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var result = await VeraPdfAdapter.ValidateAsync(executable, input, hash, "ua1", TimeSpan.FromSeconds(15));
            Assert.Equal(status, result.Status);
            Assert.Equal(hash, result.DocumentSha256);
            if (reason == null) Assert.Null(result.IncompleteReason);
            else Assert.StartsWith(reason, result.IncompleteReason);
            // An incomplete result carries no verdict or findings that could contradict it.
            if (status == IndependentValidationStatus.Incomplete)
            {
                Assert.Empty(result.Findings);
                Assert.Null(result.PassedRules);
                Assert.Null(result.FailedRules);
                Assert.Null(result.PassedChecks);
                Assert.Null(result.FailedChecks);
            }
        }
        finally { File.Delete(input); }
    }
}
