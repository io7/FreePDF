# Independent validation with veraPDF

FreePDF 7.12 adds an optional veraPDF adapter to `freepdf-a11y`. Install veraPDF
separately, then provide the path to its executable or Windows `.bat` launcher:

```sh
freepdf-a11y document.pdf --verapdf /opt/verapdf/verapdf
freepdf-a11y document.pdf --verapdf /opt/verapdf/verapdf --format html --output audit.html
freepdf-a11y document.pdf --verapdf /opt/verapdf/verapdf --format json --output audit.json
```

The adapter was developed against veraPDF 1.30.3. CI pins its installer hash in
`eng/Install-VeraPdf.ps1`. FreePDF does not bundle or download the validator.
veraPDF has its own license and installation requirements.

## Outcomes and policy

FreePDF's native preflight and the independent validator inspect the same private
snapshot. Reports retain both results, the document SHA-256, the requested and
reported profile, and veraPDF component versions. The original is never passed
to the external process. FreePDF checks the snapshot hash again after validation.

| Combined exit | Meaning |
| --- | --- |
| 0 | Explicit `--policy automated`; both requested machine checks completed without a detected failure. Human review remains required. |
| 1 | At least one validator found a failure, including during an incomplete scan. |
| 2 | Default review policy; no detected machine failure, but human review remains required. |
| 3 | At least one result was incomplete and neither validator found a failure. |
| 64 | Invalid command options or input/report I/O failure. |

An independent pass cannot clear native failures or incomplete native checks.
When either result is incomplete, the combined report status stays `Incomplete`.
The combined `hasFailures` field in JSON and SARIF records a detected failure
from either validator.
A missing executable, timeout, truncated/malformed XML, inconsistent counts,
wrong input, unexpected process exit or unsupported profile produces an incomplete
independent result. So does validator output above its limit (8 MiB of report or
64 KiB of error output). If the input bytes change while veraPDF runs, the result
is incomplete and keeps no rule counts or findings, because they describe other
bytes. A PDF/UA machine pass does not certify WCAG or ADA compliance.

Only explicit PDF/UA-1 is supported: `--verapdf-profile ua1` is the default.
Other profile values, including `ua2` and automatic detection, remain incomplete.
The native scanner retains its declared coverage limits.

## Formats and compatibility

Without `--verapdf`, JSON retains the 7.11 native report shape. With the adapter,
JSON has `schemaVersion: "2.0"`, a combined `status`, and separate `native` and
`independentValidation` objects. `requirementsInventory` records the inventory
version, hash and outstanding human-task IDs. Applicability remains unassessed.

SARIF has one run for each validator. Its top-level `properties.status` is the
combined outcome. Incomplete runs have `executionSuccessful: false`; a machine
pass still has a human-review result. HTML and text label the combined, native
and independent outcomes. Report content is escaped for its output format.

Reports contain a SHA-256 of the raw validator output for diagnostics. The adapter
does not retain that raw output or expose process stderr. Failed-rule locations
come from the validator; it may limit displayed occurrences to 100 per rule.
The full failed-check counts are retained.

## Limits and local operation

The default external timeout is 60 seconds. Set `--verapdf-timeout SECONDS` to a
value from 1 to 600. Output is limited to 8 MiB and stderr to 64 KiB. Timeouts and
output limits terminate the process tree, including the launcher's JVM.
Snapshot size follows the native scanner's 64 MiB default file limit.

These controls are not an OS sandbox or a JVM memory limit. Select a trusted
local validator installation. Use external process isolation for hostile PDFs.
On Unix, the temporary snapshot directory is accessible only to its owner.
On Windows, it inherits the current user's temporary-directory permissions.
Cleanup removes the snapshot after the run; an OS cleanup failure can leave it
in the user's temporary directory.

The Windows batch launcher rejects paths containing quotes, percent signs,
exclamation marks or control characters. This prevents shell expansion. Install
veraPDF and select a temporary directory without those characters.
Passwords from `--password-env` are used only by the native scanner. They are
not passed on the external process command line. A validator that cannot inspect
an encrypted input returns incomplete.

## Validation

The adapter suite covers valid and failing reports, wrong profiles, missing
metadata, conflicting counts, extra jobs, DTD/entity input, resource limits,
cancellation, argument handling and snapshot changes. Original PDFs exercise
the installed tool with pinned veraPDF. These controls do not establish coverage
of every PDF/UA condition.

The `reused-tagged-form` fixture draws an outer form twice. Its inner form owns
a marked-content ID. FreePDF reports `FPDF-A11Y-MCID` because the same tagged
form is painted twice. veraPDF 1.30.3 reports a machine pass on this fixture.
The combined report retains both results and exits with code 1.

The public CLI contract is documented at:
https://docs.verapdf.org/cli/validation/
https://docs.verapdf.org/cli/help/

The complete Matterhorn inventory and its limits are described in
`docs/accessibility/README.md`.
