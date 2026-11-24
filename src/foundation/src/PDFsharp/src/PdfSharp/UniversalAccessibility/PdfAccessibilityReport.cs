// Original FreePDF implementation. See LICENSE and PROVENANCE.md.
using System.Collections.Generic;
using System.Linq;

namespace PdfSharp.UniversalAccessibility
{
    /// <summary>The outcome of an individual accessibility check.</summary>
    public enum PdfAccessibilityCheckStatus
    {
        /// <summary>No applicable occurrence was found.</summary>
        NotApplicable,
        /// <summary>The implemented check passed for inspected occurrences.</summary>
        Passed,
        /// <summary>Human judgment is required.</summary>
        NeedsReview,
        /// <summary>The check could not be completed.</summary>
        NotChecked,
        /// <summary>At least one inspected occurrence failed.</summary>
        Failed
    }

    /// <summary>A preflight never certifies accessibility or legal compliance.</summary>
    public enum PdfAccessibilityScanStatus
    {
        /// <summary>No implemented check failed; human review and further validation remain.</summary>
        NeedsReview,
        /// <summary>At least one implemented check failed.</summary>
        Failed,
        /// <summary>Some implemented checks could not finish; failures may also be present.</summary>
        Incomplete
    }

    /// <summary>Resource limits for a local, read-only accessibility preflight.</summary>
    public sealed class PdfAccessibilityScanOptions
    {
        /// <summary>Optional password. It is never included in the report.</summary>
        public string? Password { get; set; }
        /// <summary>Maximum input size. Defaults to 64 MiB.</summary>
        public int MaxFileBytes { get; set; } = 64 * 1024 * 1024;
        /// <summary>Maximum pages to inspect.</summary>
        public int MaxPages { get; set; } = 2000;
        /// <summary>Maximum structure-tree, role-map and number-tree nodes to visit. Page content is bounded by <see cref="MaxDecodedBytes"/>.</summary>
        public int MaxNodes { get; set; } = 100000;
        /// <summary>Maximum structure-tree, page-tree, Form XObject and marked-content nesting depth.</summary>
        public int MaxDepth { get; set; } = 64;
        /// <summary>Maximum findings, including review tasks.</summary>
        public int MaxFindings { get; set; } = 10000;
        /// <summary>Maximum decoded bytes inspected across metadata, page content, invoked forms and inspected Unicode maps.</summary>
        public int MaxDecodedBytes { get; set; } = 32 * 1024 * 1024;
        /// <summary>
        /// Maximum time for one scan, or null for no limit. When it is reached, the scan ends as incomplete and keeps
        /// the failures found so far. The limit also applies while the PDF is opened, as the cancellation token does.
        /// </summary>
        public TimeSpan? MaxDuration { get; set; } = TimeSpan.FromMinutes(2);
    }

    /// <summary>A versioned check, its scope and its outcome.</summary>
    public sealed class PdfAccessibilityCheck
    {
        internal PdfAccessibilityCheck(string id, string title, string guidance, string reference)
        { Id = id; Title = title; Guidance = guidance; Reference = reference; }
        /// <summary>Stable identifier for CI policies and suppressions.</summary>
        public string Id { get; }
        /// <summary>The precise check performed, not a claim of complete standard coverage.</summary>
        public string Title { get; }
        /// <summary>Suggested remediation or review.</summary>
        public string Guidance { get; }
        /// <summary>Public specification or technique reference.</summary>
        public string Reference { get; }
        /// <summary>Aggregated outcome; a failing occurrence takes precedence.</summary>
        public PdfAccessibilityCheckStatus Status { get; internal set; }
        /// <summary>Number of inspected occurrences.</summary>
        public int Occurrences { get; internal set; }
    }

    /// <summary>An actionable finding. Page numbers are one-based.</summary>
    public sealed class PdfAccessibilityFinding
    {
        internal PdfAccessibilityFinding(string ruleId, PdfAccessibilityCheckStatus status, string message, int? page, string location)
        { RuleId = ruleId; Status = status; Message = message; Page = page; Location = location; }
        /// <summary>The associated check identifier.</summary>
        public string RuleId { get; }
        /// <summary>Outcome at this location.</summary>
        public PdfAccessibilityCheckStatus Status { get; }
        /// <summary>What needs attention.</summary>
        public string Message { get; }
        /// <summary>One-based page, if known.</summary>
        public int? Page { get; }
        /// <summary>PDF dictionary or structure path.</summary>
        public string Location { get; }
    }

    /// <summary>A structure-order entry for human review, not an extracted screen-reader transcript.</summary>
    public sealed class PdfAccessibilityStructureEntry
    {
        internal PdfAccessibilityStructureEntry(string role, int? page, string path, string alternativeText)
        { Role = role; Page = page; Path = path; AlternativeText = alternativeText; }
        /// <summary>Resolved standard role.</summary>
        public string Role { get; }
        /// <summary>One-based page, if known.</summary>
        public int? Page { get; }
        /// <summary>Position in the structure tree.</summary>
        public string Path { get; }
        /// <summary>Author-supplied Alt or ActualText. Treat as untrusted document text.</summary>
        public string AlternativeText { get; }
    }

    /// <summary>Evidence from the original bytes. No repair or re-save is performed.</summary>
    public sealed class PdfAccessibilityReport
    {
        /// <summary>Version of this bounded preflight rule set.</summary>
        public string RulesetVersion => "1.3.0";
        /// <summary>The intended standard; only the listed checks are implemented.</summary>
        public string Profile => "PDF/UA-1 preflight";
        /// <summary>Always partial: this API is not a complete PDF/UA validator or ADA certification.</summary>
        public string Coverage => "Partial";
        /// <summary>SHA-256 of the input bytes; empty when the input limit prevented reading them.</summary>
        public string DocumentSha256 { get; internal set; } = "";
        /// <summary>Number of pages, when readable.</summary>
        public int PageCount { get; internal set; }
        /// <summary>Whether the implemented checks finished without a read, resource or unsupported-content interruption.</summary>
        public bool ScanCompleted { get; internal set; }
        /// <summary>Overall outcome. There is intentionally no compliant/pass outcome.</summary>
        public PdfAccessibilityScanStatus Status { get; internal set; }
        /// <summary>Whether at least one implemented check failed, also when <see cref="Status"/> is <see cref="PdfAccessibilityScanStatus.Incomplete"/>.</summary>
        public bool HasFailures => Checks.Any(c => c.Status == PdfAccessibilityCheckStatus.Failed);
        /// <summary>Every implemented check, including checks not applicable to this file.</summary>
        public IReadOnlyList<PdfAccessibilityCheck> Checks { get; internal set; } = Array.Empty<PdfAccessibilityCheck>();
        /// <summary>Failures, review tasks and incomplete checks.</summary>
        public IReadOnlyList<PdfAccessibilityFinding> Findings { get; internal set; } = Array.Empty<PdfAccessibilityFinding>();
        /// <summary>Structure traversal order to help a reviewer inspect author intent.</summary>
        public IReadOnlyList<PdfAccessibilityStructureEntry> StructureOrder { get; internal set; } = Array.Empty<PdfAccessibilityStructureEntry>();
        /// <summary>Known coverage boundaries, independent of whether this scan completed.</summary>
        public IReadOnlyList<string> Limitations { get; } = Array.AsReadOnly(new[]
        {
            "This preflight does not establish PDF/UA, WCAG, Section 508 or ADA compliance.",
            "Human review is required for reading order, text alternatives, headings, table meaning, contrast, language and assistive-technology usability.",
            "The font check covers visible text in page content and invoked forms. The Unicode check examines used character codes in supported font encodings. The glyph check reads embedded TrueType, Type 1 and CFF programs and Type 3 procedures, not yet OpenType programs in FontFile3. Glyph shapes, annotation appearances, form-field semantics and optional content require further validation.",
            "The scanner resolves named marked-content properties. Complex inline property dictionaries and external reference XObjects require independent inspection.",
            "PDF/UA-2 is not supported. Use an independent validator and assistive-technology testing before making a conformance claim.",
            "Limits bound this preflight's traversal; they are not a process sandbox. Isolate untrusted PDFs with external memory and time limits."
        });
    }
}
