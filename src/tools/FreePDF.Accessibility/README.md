# FreePDF accessibility preflight

An offline .NET 10 command-line tool that checks existing PDFs without changing
them. Reports include detected structural failures, check coverage, document hashes
and required human review. Coverage is partial PDF/UA-1; this tool does not certify
ADA, WCAG, Section 508 or complete PDF/UA compliance.

```sh
freepdf-a11y document.pdf
freepdf-a11y document.pdf --format html --output audit.html
freepdf-a11y document.pdf --format sarif --output audit.sarif
freepdf-a11y document.pdf --format json --policy automated
```

The first command prints a plain-English summary: problems found, human review
required, or scan incomplete. It lists up to 20 findings with suggested next steps.
The HTML report opens in a browser and contains every finding, check coverage,
structure order and limitations. JSON and SARIF are for automation.

The tool is implemented in this repository but has not been published to NuGet.
With the .NET 10 SDK, run it from a source checkout:

```sh
dotnet run --project src/tools/FreePDF.Accessibility -c Release -- document.pdf
```

Output files must not already exist. Use `--password-env NAME` to supply a password
through an environment variable. Reports can contain text from the input PDF.

Exit codes: `0` explicit automated policy satisfied (review still required), `1`
failed checks, `2` human review required, `3` incomplete scan, `64` usage or I/O
error. The default policy returns `2` even when all implemented checks pass.
That means a person still needs to check the document; it is not a tool crash.

See [the full scanner guide](https://github.com/io7/FreePDF/blob/main/docs/accessibility-preflight.md)
for the rule list, limitations and native .NET API.
