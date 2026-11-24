# PDF/UA-1 requirements inventory

`requirements.json` accounts for all 137 failure-condition entries in the
Matterhorn Protocol 1.1 tables. The CLI embeds the same file:

```sh
freepdf-a11y --requirements
freepdf-a11y --requirements --format json --output requirements.json
```

The inventory is version 1.2.0. It contains 31 checkpoints, 87 machine conditions,
48 human conditions and two entries that refer to other requirements. The source
introduction still says 136 conditions and 47 human conditions. Its version 1.1
tables add human condition 13-008. The inventory includes that entry.

## Reading an entry

Each entry retains its source condition, PDF/UA clause, assessment method and
source page. Notes and exceptions remain in the linked source. The checkpoint
provides an applicability procedure, a review procedure and informative WCAG
relationships. Each entry names the owner role and expected evidence.

`nativeCoverage` distinguishes partial native evidence, unsupported machine
checks, human review and references to other requirements. Native rule IDs point
to related checks; they do not claim complete coverage of the source condition.
The native scanner also has implementation checks, such as resource limits,
that are not standalone Matterhorn conditions.

`independentCoverage: Unmapped` means that we have not verified a condition-level
mapping to the external validator. The adapter reports veraPDF's profile verdict
separately. It never converts that verdict into 87 passed inventory entries.

Every entry starts with `documentOutcome: NotAssessed`. The inventory does not
inspect a document or decide that a feature is absent. Reviewers must record
applicability and decisions against the PDF hash. This release does not yet store
those decisions or provide a sign-off workflow.

## WCAG relationships

Mappings name WCAG 2.1 and 2.2 separately and link to dated W3C recommendations.
They are FreePDF's informative relationships, not assertions that a PDF/UA test
satisfies a WCAG criterion. Empty mappings mean that no direct relationship is
claimed. The inventory does not enumerate every WCAG success criterion or decide
which legal obligations apply to a document.

## Source and validation

Source: Copyright 2021 PDF Association, Matterhorn Protocol 1.1, CC-BY-4.0:
https://pdfa.org/download-area/publications/Matterhorn-Protocol-1-1.pdf

FreePDF adapted the condition text and added coverage metadata and procedures.
The source notes remain authoritative for those conditions. Attribution and the
license link are in `Matterhorn-NOTICE.txt`, also included in the tool package.
The source's cross-references are retained, including 28-016's reference to
20-001; its condition itself refers to PDF/UA clause 7.11.

Contract tests verify every ID against independently transcribed checkpoint
ranges, the human/machine counts, references, rule IDs and explicit unassessed
outcomes. Those tests establish inventory completeness, not validator accuracy.
