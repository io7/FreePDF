# Reproducible producer corpus

The full suite is **28,000 distinct scenario parameter sets × three producers =
84,000 PDFs**. FreePDF, ReportLab and Chromium generate the same expected content.
No customer documents, scraped PDFs or proprietary test corpus are used.
The repository contains recipes and contracts, not thousands of PDF binaries.

Scenarios vary paper size/orientation, page count, margins, font size/style,
Unicode text, rows, paragraphs/lists/tables, raster images/vector illustrations,
colors and compression. The affine permutation over a mixed-radix parameter space
is deterministic and collision-free within one seed. Tests prove uniqueness and
that 24 shards partition the full 28,000 scenarios without omissions or overlap.
The original 14,000 scenarios keep their IDs and parameters; the expansion adds
14,000 different parameter sets using the same seed and permutation.
These combinations exercise known features; they are not 84,000 independent PDF
implementations or a replacement for hostile-input fuzzing and real-world fixtures.

## Gates

Each expected PDF must exist, pass qpdf syntax checks, open/save/reopen successfully
in a separate FreePDF process, preserve page count and extracted text, and contain
every expected content line on its intended page. The same semantic expectations
apply to all three producers. Cross-producer byte or pixel identity is not expected:
each engine can lay out the content differently.

Every input is scanned before and after its FreePDF round trip. An increased count
of failed accessibility findings or a newly incomplete scan fails the gate.
FreePDF's tagged synthetic documents must pass the implemented native checks before
the round trip. ReportLab's untagged output and Chromium's different tagging are
measured as baselines, not incorrectly treated as fully accessible documents.
This catches some accessibility regressions; equality of rule counts is not proof
that all accessibility semantics were preserved.

Selected scenarios additionally compare **every page** before and after the round
trip with independent Poppler renders at 60 dpi. At most 0.1% of pixels may differ
by more than 16/255 gray levels. Failures keep before/after/difference images.
Render sampling and actual counts are recorded explicitly in each summary.

The CI runner is strict: rejected PDFs, missing tools, crashes, timeouts, absent
files, semantic loss, render differences and accessibility regressions fail. It
reuses `eng/research/CorpusCheck`'s isolated worker via its new `check-a11y` command.
The older `run`/`fuzz` research modes keep their existing exploratory behavior and
are **not** substitutes for this CI gate.

## CI tiers

| Trigger | Scenarios | PDFs | Shards | Rendered scenarios |
| --- | ---: | ---: | ---: | ---: |
| Pull request / main / release | 60 | 180 | 1 | All 60 |
| Nightly, Monday–Saturday | 600 | 1,800 | 4 | About 1 in 10: 66 |
| Weekly Sunday / manual full | 28,000 | 84,000 | 24 | About 1 in 100: 289 |

All three producers are rendered for selected scenarios. Every PDF receives the
non-render checks. `--render-every N` selects a scenario when its scenario hash
is divisible by N. Selecting every Nth index instead fixed the low digits of the
scenario permutation, so nightly and full runs never rendered A5 landscape, A4
landscape or Letter portrait pages. With the hash, both tiers render all six page
geometries, and the full tier's 867 rendered PDFs cover every pair of parameter
values; a contract test checks this. `--render-every 1` renders the full
collection when more compute is available. CI limits concurrency to four shards and each shard to 60 minutes.
Doubling the full suite from 12 to 24 shards keeps the per-shard PDF count about
the same. Total weekly runner work roughly doubles; pull-request and nightly
budgets are unchanged.
Workflows become active only after they reach the appropriate GitHub branch;
creating this configuration is not evidence that a hosted run has completed.

## Local run (Linux, .NET 10, Python 3.12+)

Install qpdf and Poppler (`pdftotext`, `pdftoppm`) using your package manager, then:

```sh
python3 -m venv /tmp/freepdf-corpus-venv
/tmp/freepdf-corpus-venv/bin/pip install -r eng/corpus/requirements.txt
/tmp/freepdf-corpus-venv/bin/playwright install chromium
dotnet build eng/corpus/CorpusGenerator/CorpusGenerator.csproj -c Release
dotnet build eng/research/CorpusCheck/CorpusCheck.csproj -c Release
dotnet build src/tools/FreePDF.Accessibility/FreePDF.Accessibility.csproj -c Release
python3 -m unittest discover -s eng/corpus -p 'test_*.py' -v
/tmp/freepdf-corpus-venv/bin/python eng/corpus/run.py artifacts/corpus-smoke --count 60
```

Use a new output directory for each run. Full run:

```sh
/tmp/freepdf-corpus-venv/bin/python eng/corpus/run.py artifacts/corpus-full \
  --count 28000 --workers 4 --generation-workers 4 --render-every 100
```

Or divide it with `--shards 24 --shard 0` through `--shard 23`, each with its own
output directory. The default fixed seed is `20261116`; record a different seed
when expanding coverage. Pin a seed while comparing revisions.

Artifacts include scenario JSONL/hash, tool versions, embedded font hash, generator
and checker assembly hashes, input/output PDF hashes, per-file outcomes and summary
counts. Producer packages and Chromium are pinned; OS qpdf/Poppler versions are
recorded because runner packages can change. PDF timestamps/document IDs may vary:
the reproducibility contract is the scenario/expected behavior, not byte identity.

Successful PDFs and render images are deleted after checking unless `--keep-pdfs`
is selected. Failures retain their scenario, input, round trip and available render
evidence. GitHub uploads reports and failures for three days, not successful PDF
collections. A failure can be rerun by putting its `scenario.json` on one JSONL
line and passing it to `CorpusGenerator` or `producers.py`; use the recorded
dependency versions for the closest reproduction.

The DejaVu font and bitmap come from existing lab assets; the font's license is
in `lab/Assets/DejaVuSans.LICENSE.txt`. External producers and validators are
test tools only and are not linked into FreePDF packages. The earlier mutation
campaign remains documented in `docs/research/corpus-roundtrip-2026-11.md`.

The [2026-11-16 verification record](verification-2026-11-16.json) records the
42,000-file local run, report hashes, sampled-render counts, final 180-file recheck,
other completed checks and verification limits. It is historical evidence, not a
claim that hosted CI or a published package has been verified.

The [84,000-file expansion verification](verification-2026-11-16-84k.json)
records uniqueness/sharding contracts for all 28,000 scenarios and a local run of
shard 0: 3,501 PDFs passed, including 1,749 from the added range. Render comparisons
passed for 141 PDFs / 285 pages. It also records the packaged CLI checks. The full
84,000-file execution is configured but has not yet been run.
