# FreePDF documentation

Start with the [guide](guide/index.md). The published site, with the API reference, is
https://io7.github.io/FreePDF/.

## For users

- [Guide](guide/index.md): making and changing PDF files with the drawing API.
- [Text extraction](text-extraction.md)
- [Accessibility scanner](accessibility-preflight.md)
- [Migrating from PDFsharp 6.2.4](migrating-to-7.md)
- [Support policy](support-policy.md)
- [Release notes](releases/)

## Building the site

`docfx.json` builds the site from the guide, the topic pages above and the XML comments of the packages:

```
dotnet tool restore
rm -rf docs/_site docs/api docs/obj
dotnet docfx metadata docs/docfx.json
dotnet docfx build docs/docfx.json --warningsAsErrors
```

The site is in `docs/_site`. docfx caches its work in `docs/obj` and does not repeat a warning on the next
build, so delete the three folders first. The guide's code blocks come from the tests in
`src/samples/FreePDF.Samples`: change the test, then run `python3 eng/sync_doc_snippets.py --write`.

## For maintainers

The other files are the release procedure (`releasing.md`), validation records, research, and notes
inherited from PDFsharp (`PDFsharp/`, `MigraDoc/`, `upstream/`, `coding/`, `change-log/`), some of them in
German. `docs-public.csproj` is an empty project that shows these files in Visual Studio.
