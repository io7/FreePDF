# Contributing to FreePDF

FreePDF accepts contributions under the MIT licence. By submitting a contribution,
you confirm that you have the right to contribute it under those terms. Keep
existing copyright notices and any applicable third-party notices intact.

## Independently implemented fixes

1. Start with the MIT baseline or reviewed FreePDF code.
2. Describe the observed defect and expected behaviour in your own words.
   Factual release notes, specifications, and independently observed behaviour
   can inform the requirement.
3. Write a small reproduction and an appropriate regression test using material
   you have permission to contribute. Prefer synthetic documents and inputs.
4. Implement the correction from those requirements and the permitted source.
5. Explain the implementation and its evidence in the pull request.

For this contribution route, do not consult or supply later AGPL source patches,
implementation-derived pseudocode, or decompiled code to the implementer or a
coding agent. Renaming symbols, translating code, or asking AI to rewrite a
patch does not establish independent authorship. Reading code is not itself a
licence violation; these input restrictions are FreePDF's provenance policy.

Do not copy upstream tests, fixtures, screenshots, documentation prose, or
comments without checking their own licences. A public issue may contain both
factual bug information and separately protected material.

If you have already consulted an upstream implementation relevant to a change,
disclose that before submitting code. Maintainers will determine an appropriate
review path; do not describe it as independently implemented without review.

## Importing existing code

Imports require a documented MIT-compatible licence for the exact material.
Include the immutable source reference, licence evidence, required notices,
and any adaptation made. Changes from later MIT-licensed releases can be
considered after this review. Do not merge a newer upstream branch wholesale.

## AI-assisted contributions

The same requirements apply to AI-generated or AI-assisted changes. Record the
source material made available to the agent. Restrict browsing and repository
access to the approved source versions and behavioural requirements. Review
generated code and tests before submitting them.

## Pull requests

- Explain the defect or improvement and its effect on users.
- Record the source provenance and licensing route.
- Include relevant validation and its limits, especially for PDF layout,
  parsing, signatures, and platform-specific behaviour.
- Keep changes scoped and preserve public API compatibility unless a breaking
  change is explicitly proposed.

The initial CI compiles core libraries; it is not a complete rendering test
suite. Run the tests relevant to your change and document any missing assets
or unavailable platforms.

## Releases

The inherited packaging projects still name upstream packages. Do not publish
their output. A FreePDF package release requires distinct package IDs, correct
internal dependency references, licence notices, versioning, and release checks.
Repository creation does not constitute a NuGet release.
