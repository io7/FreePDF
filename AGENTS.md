# FreePDF development

Read `CONTRIBUTING.md` and `PROVENANCE.md` before changing code.

- Work from the recorded MIT baseline and reviewed FreePDF commits.
- Do not read, fetch, diff, decompile, or copy later AGPL implementation material
  while independently implementing a fix. Use factual behavioural requirements,
  specifications, and original reproducers.
- Do not fetch upstream's default branch or all tags. Fetch only an explicitly
  identified source version after its licence provenance has been reviewed.
- Record source inputs and the provenance of imported code, tests, and assets.
- Preserve original copyright and licence notices.
- Keep `PdfSharp` and `MigraDoc` API names compatible unless changing them is
  explicitly in scope.
- Do not publish the inherited upstream NuGet package identities.
- Never use customer documents or confidential data in public issues or tests.

The initial CI compiles core libraries. It does not establish rendering
correctness or validate every supported platform. Run relevant regression
checks for implementation changes and report their actual scope.
