// Original FreePDF implementation. See LICENSE.
using PdfSharp.UniversalAccessibility;

namespace FreePDF.Accessibility;

internal enum IndependentValidationStatus { MachineChecksPassed, Failed, Incomplete }

internal sealed record IndependentFinding(string Specification, string Clause, string TestNumber,
    int FailedChecks, string Description, IReadOnlyList<string> Locations);

internal sealed record IndependentValidation
{
    public string Validator => "veraPDF";
    public string AdapterVersion => "1.0.0";
    public string RequestedProfile { get; init; } = "ua1";
    public string? ProfileName { get; init; }
    public string? ValidatorVersion { get; init; }
    public IReadOnlyDictionary<string, string> Components { get; init; } = new Dictionary<string, string>();
    public string DocumentSha256 { get; init; } = "";
    public string? ReportSha256 { get; init; }
    public IndependentValidationStatus Status { get; init; } = IndependentValidationStatus.Incomplete;
    public bool HumanReviewRequired => true;
    public string Scope => "PDF/UA-1 machine validation only; no WCAG or ADA certification.";
    public int? ExitCode { get; init; }
    public int? PassedRules { get; init; }
    public int? FailedRules { get; init; }
    public int? PassedChecks { get; init; }
    public int? FailedChecks { get; init; }
    public IReadOnlyList<IndependentFinding> Findings { get; init; } = Array.Empty<IndependentFinding>();
    public string? IncompleteReason { get; init; }

    internal static PdfAccessibilityScanStatus Combine(PdfAccessibilityReport native, IndependentValidation? independent)
    {
        if (native.Status == PdfAccessibilityScanStatus.Incomplete || independent?.Status == IndependentValidationStatus.Incomplete)
            return PdfAccessibilityScanStatus.Incomplete;
        if (native.Status == PdfAccessibilityScanStatus.Failed || independent?.Status == IndependentValidationStatus.Failed)
            return PdfAccessibilityScanStatus.Failed;
        return PdfAccessibilityScanStatus.NeedsReview;
    }
}
