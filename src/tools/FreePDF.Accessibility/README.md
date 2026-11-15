# FreePDF accessibility preflight

An offline .NET 10 command-line tool that checks existing PDFs without changing
them. Reports include detected structural failures, check coverage, document hashes
and required human review. Coverage is partial PDF/UA-1; this tool does not certify
ADA, WCAG, Section 508 or complete PDF/UA compliance.

```sh
freepdf-a11y document.pdf --format html --output audit.html
freepdf-a11y document.pdf --format sarif --output audit.sarif
freepdf-a11y document.pdf --format json --policy automated
```

Output files must not already exist. Use `--password-env NAME` to supply a password
through an environment variable. Reports can contain text from the input PDF.

Exit codes: `0` explicit automated policy satisfied (review still required), `1`
failed checks, `2` human review required, `3` incomplete scan, `64` usage or I/O
error. The default policy returns `2` even when all implemented checks pass.

See [the full scanner guide](https://github.com/io7/FreePDF/blob/main/docs/accessibility-preflight.md)
for the rule list, limitations and native .NET API.
