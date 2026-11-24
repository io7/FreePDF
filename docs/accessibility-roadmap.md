# Accessibility coverage roadmap

The product target is a PDF checker that explains every applicable requirement,
finds defects it can detect automatically, and records the human review needed
for the remaining requirements. The current CLI is a partial PDF/UA-1 preflight.
It does not establish ADA compliance.

## Release order

| Release | Scope | State |
| --- | --- | --- |
| 7.11.0 | Native preflight API, offline CLI, coordinate precision fix and 84,000-file corpus configuration. Package and test the CLI with the six library packages. | Released 2026-11-16. The full 84,000-file run was not part of the release gates. |
| [7.12.0](releases/7.12.0.md) | Account for all Matterhorn 1.1 conditions and add optional veraPDF validation. | Released 2026-11-17. |
| [7.13.0](releases/7.13.0.md) | Inspect local Form XObjects, including nested forms, and resolve named marked-content properties. | Released 2026-11-18. |
| [7.14.0](releases/7.14.0.md) | Bound the scan's memory and time on hostile files and add the `MaxDuration` limit. | Released 2026-11-18. |
| [7.14.1](releases/7.14.1.md) | Stop the reading of the PDF at `MaxDuration` and on cancellation. | Released 2026-11-19. |
| [7.16.0](releases/7.16.0.md) | Check that shown character codes map to Unicode (`FPDF-A11Y-UNICODE`). | Prepared for stable publication. |
| Following releases | Extend native inspection to glyphs, form fields and annotations. Add source-document validation and a human-review record tied to the PDF hash. Treat PDF/UA-2 as a separate profile. | Planned in the sequence below. |

The 7.12.0 acceptance gate is a traceable requirement list plus positive and
negative adapter controls. A missing validator, timeout, malformed report or
unsupported profile must remain an incomplete result. A machine-validation pass
must retain the human-review requirements. No release in this plan promises
automatic legal certification.

PDF/UA defines PDF accessibility requirements. WCAG describes accessibility
outcomes, including meaning and usability. ADA obligations also depend on the
organization and how it provides the document. For example, the US Department
of Justice's Title II web rule specifies WCAG 2.1 Level AA for covered state and
local government content, subject to the rule's scope and exceptions. A validator
result alone does not settle those obligations.

Sources: [DOJ rule fact sheet](https://www.ada.gov/resources/2024-03-08-web-rule/),
[W3C evaluation-tool limits](https://www.w3.org/WAI/test-evaluate/tools/selecting/),
[veraPDF validation scope](https://docs.verapdf.org/validation/).

## Delivery sequence

These are proposed milestones, not implemented capabilities or release dates.

| Milestone | Work | Acceptance evidence |
| --- | --- | --- |
| 1. Account for every requirement | Map PDF/UA-1 and Matterhorn checkpoints to versioned rule IDs, WCAG criteria, applicability, automated checks and human tasks. Publish unsupported requirements explicitly. Treat PDF/UA-2 as a separate profile. | No requirement silently omitted; every check has an owner, reference, expected outcome and test or review procedure. |
| 2. Complete automated PDF/UA-1 coverage | Add Form XObject and named-property inspection, Unicode/glyph checks, complete structure/parent-tree rules, headings, table-header associations, annotations, field names/states, optional content and security permissions. Use pinned veraPDF as an independent validator during development; an optional CLI adapter can provide its separate results sooner. | Positive and deliberately broken fixtures for each rule, independent validator comparison, documented differences and bounded behavior on malformed input. A validator pass must retain its exact profile and version. |
| 3. Make accessible authoring routine | Validate source document semantics before generation. Require useful author-supplied descriptions, real headings, table headers and accessible form metadata. Support reviewed fixes in an output copy with a change log. | Generated feature fixtures satisfy the target profile and retain content and accessibility information through save/import operations. Repairs never imply that missing meaning can be guessed safely. |
| 4. Guide human review | Present text/structure order, image descriptions, table relationships, language, contrast and keyboard/form tasks in a review report. Test representative documents with screen readers and people who use assistive technology. | Reviewer identity, decision, rationale and evidence for each applicable human task. Tie the review to the PDF hash and invalidate it when the PDF changes. |
| 5. Produce an auditable result | Combine native checks, independent validation and review evidence. Record PDF hash, standard/profile, rule and tool versions, applicable scope, failures and outstanding review. Add CI policy and batch scanning around that model. | A technical conformance report only when all applicable requirements have supporting evidence. Partial scans, unsupported features and unfinished reviews cannot become a clean result. |

Start with PDF/UA-1 and a WCAG mapping that names the exact version. Support WCAG
2.2 AA as an additional target while retaining the WCAG 2.1 requirements needed
by a customer's policy; version differences must be explicit. PDF/UA-2 support
needs its own requirements, fixtures and independent validation, not a renamed
PDF/UA-1 result.

The [Matterhorn Protocol](https://pdfa.org/resource/the-matterhorn-protocol/)
distinguishes machine and human checkpoints. It is the starting inventory for
PDF/UA-1, not a substitute for the standards or the WCAG review. Counts of broad
rules in the current scanner are not a percentage of standards coverage.

## What the 84,000-file corpus proves

The synthetic corpus contains 28,000 distinct parameter combinations rendered by
FreePDF, ReportLab and Chromium. It tests parsing, generation, text preservation,
sampled rendering and changes in the implemented accessibility findings.
It does not contain 84,000 independently annotated accessibility verdicts.

Keep that scale test and build a separate, smaller conformance suite with a
known expected result for each requirement. Include complex and malformed
structures, forms, annotations, nested content, multilingual text and cases
that require human judgment. Measure false negatives and false positives against
those labels. More files improve regression coverage; independently established
expected results establish whether the accessibility checks are right.

The [requirements inventory](accessibility/requirements.json) accounts for every
Matterhorn 1.1 table entry. The [inventory guide](accessibility/README.md) explains
the source's count discrepancy, partial native coverage and informative WCAG
mappings. The optional [veraPDF adapter](accessibility-verapdf.md) reports a
separate machine verdict. Its result does not mark individual inventory rows as
passed or complete human review.

Native rule `FPDF-A11Y-UNICODE` now checks used character codes against ToUnicode
maps, named encodings and `/Differences` glyph names, and fails forbidden mapped
values. The next font work is validation that the referenced glyph exists
(Matterhorn 31-030), then encodings that need the font program or the CJK
character collections. Form-field and annotation rules follow.
