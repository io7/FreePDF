"""Exercise the optional adapter against pinned veraPDF and original fixture PDFs."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

from run import GENERATOR, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("verapdf", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--tool", type=Path, help="Installed freepdf-a11y executable; otherwise use the source-built DLL")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    cli = [str(args.tool)] if args.tool else ["dotnet", str(ROOT / "src/tools/FreePDF.Accessibility/bin/Release/net10.0/FreePDF.Accessibility.dll")]
    subprocess.run(["dotnet", str(GENERATOR), "--fixtures", str(args.output)], check=True, timeout=30)
    (args.output / "malformed.pdf").write_text("not a PDF")
    results = []

    def scan(name, expected, *options, validator=None):
        pdf = args.output / (name + ".pdf")
        before = hashlib.sha256(pdf.read_bytes()).hexdigest()
        run = subprocess.run(cli + [str(pdf), "--verapdf", str(validator or args.verapdf), *options], capture_output=True, text=True, timeout=90)
        if run.returncode != expected:
            raise RuntimeError(f"{name}: expected exit {expected}, got {run.returncode}: {run.stderr} {run.stdout[:500]}")
        if hashlib.sha256(pdf.read_bytes()).hexdigest() != before:
            raise RuntimeError("The original PDF changed")
        return run.stdout, before

    for name, expected, status, verdict in (
        ("valid", 2, "NeedsReview", "MachineChecksPassed"),
        ("missing-alt", 1, "Failed", "Failed"),
        ("missing-language", 1, "Failed", "Failed"),
        ("untagged", 1, "Failed", "Failed"),
        ("incomplete-missing-language", 1, "Incomplete", "Failed"),
        ("nested-form", 2, "NeedsReview", "MachineChecksPassed"),
        # 1.30.3 accepts an outer form invoked twice when its inner form owns the MCID.
        ("reused-tagged-form", 1, "Failed", "MachineChecksPassed"),
        ("untagged-form-paint", 1, "Failed", "Failed"),
        ("unicode-mapped", 2, "NeedsReview", "MachineChecksPassed"),
        ("unicode-missing", 1, "Failed", "Failed"),
        ("unicode-custom-encoding", 3, "Incomplete", "MachineChecksPassed"),
        ("unicode-forbidden", 1, "Failed", "Failed"),
        ("unicode-glyph-name", 2, "NeedsReview", "MachineChecksPassed"),
        ("unicode-glyph-name-missing", 1, "Failed", "Failed"),
        ("glyph-notdef", 1, "Failed", "Failed"),
        ("glyph-missing", 1, "Failed", "Failed"),
        # 1.30.3 fails this file on glyph widths only: it does not apply the CIDToGIDMap stream to .notdef.
        ("glyph-map-notdef", 1, "Failed", "Failed"),
        ("malformed", 3, "Incomplete", "Incomplete"),
    ):
        text, sha = scan(name, expected, "--format", "json")
        report = json.loads(text)
        independent = report["independentValidation"]
        if report["schemaVersion"] != "2.0" or independent["status"] != verdict or independent["validatorVersion"] != "1.30.3":
            raise RuntimeError(f"Incorrect independent verdict or validator version for {name}")
        if report["native"]["status"] != status or report["status"] != status or report["hasFailures"] != (expected == 1):
            raise RuntimeError(f"Incorrect combined status or failure flag for {name}")
        if name.startswith("unicode-"):
            # The rule status, and the veraPDF 7.21.7 test that must report the same defect.
            expected_rule, vera_test = {
                "unicode-mapped": ("Passed", None),
                "unicode-missing": ("Failed", "1"),
                "unicode-custom-encoding": ("NotChecked", None),
                "unicode-forbidden": ("Failed", "2"),
                "unicode-glyph-name": ("Passed", None),
                "unicode-glyph-name-missing": ("Failed", "1"),
            }[name]
            rule = next(c for c in report["native"]["checks"] if c["id"] == "FPDF-A11Y-UNICODE")
            if rule["status"] != expected_rule:
                raise RuntimeError(f"{name}: Unicode rule returned {rule['status']}")
            if vera_test and not any(
                    f["clause"] == "7.21.7" and f["testNumber"] == vera_test for f in independent["findings"]):
                raise RuntimeError(f"{name}: veraPDF did not report 7.21.7-{vera_test}")
        if name.startswith("glyph-"):
            # Every glyph control fails the native rule. veraPDF reports .notdef as 7.21.8-1 and a glyph missing from
            # the program as 7.21.4.1-2; for a CIDToGIDMap entry of 0 it reports neither (a documented difference).
            vera_rule, vera_present = {
                "glyph-notdef": (("7.21.8", "1"), True),
                "glyph-missing": (("7.21.4.1", "2"), True),
                "glyph-map-notdef": (("7.21.8", "1"), False),
            }[name]
            rule = next(c for c in report["native"]["checks"] if c["id"] == "FPDF-A11Y-GLYPH")
            if rule["status"] != "Failed":
                raise RuntimeError(f"{name}: glyph rule returned {rule['status']}")
            found = any((f["clause"], f["testNumber"]) == vera_rule for f in independent["findings"])
            if found != vera_present:
                raise RuntimeError(f"{name}: veraPDF {'did not report' if vera_present else 'reported'} {vera_rule[0]}-{vera_rule[1]}")
        if {report["documentSha256"], report["native"]["documentSha256"], independent["documentSha256"]} != {sha}:
            raise RuntimeError("Native and independent results are not bound to the same bytes")
        if not report["humanReviewRequired"] or len(report["requirementsInventory"]["humanRequirementIds"]) != 48:
            raise RuntimeError("Human review tasks were lost")
        (args.output / (name + ".json")).write_text(text)
        results.append({"fixture": name, "sha256": sha, "exit": expected, "independentStatus": verdict})

    scan("valid", 0, "--policy", "automated", "--format", "json")
    text, _ = scan("valid", 3, "--format", "json", validator=args.output / "missing-validator")
    if json.loads(text)["independentValidation"]["status"] != "Incomplete":
        raise RuntimeError("Missing validator was accepted")
    text, _ = scan("missing-language", 1, "--policy", "automated", "--format", "json", validator=args.output / "missing-validator")
    report = json.loads(text)
    if report["status"] != "Incomplete" or not report["hasFailures"] or report["independentValidation"]["status"] != "Incomplete":
        raise RuntimeError("An unavailable validator hid a detected native failure")
    text, _ = scan("valid", 3, "--verapdf-profile", "ua2", "--format", "json")
    if json.loads(text)["independentValidation"]["status"] != "Incomplete":
        raise RuntimeError("Unsupported profile was accepted")
    for fmt in ("text", "html", "sarif"):
        text, _ = scan("missing-alt", 1, "--format", fmt)
        if "veraPDF" not in text:
            raise RuntimeError(f"Independent result missing from {fmt}")
        if fmt == "sarif" and len(json.loads(text)["runs"]) != 2:
            raise RuntimeError("SARIF did not separate the two validators")
        (args.output / ("combined." + fmt)).write_text(text)
    inventory = subprocess.run(cli + ["--requirements", "--format", "json"], capture_output=True, text=True, check=True, timeout=20)
    if len(json.loads(inventory.stdout)["requirements"]) != 137:
        raise RuntimeError("Installed requirements inventory is incomplete")
    summary = {"status": "passed", "validator": "veraPDF 1.30.3", "fixtures": results,
               "additionalControls": ["explicit automated policy", "missing validator", "native failure with missing validator",
                                      "unsupported profile", "text", "html", "sarif", "inventory"],
               "scope": "Original controls only; not complete conformance coverage"}
    (args.output / "adapter-summary.json").write_text(json.dumps(summary, indent=2))
    print(f"PASS: optional adapter, {len(results)} fixture outcomes, {len(summary['additionalControls'])} additional controls, original-byte preservation and separate reports.")


if __name__ == "__main__":
    main()
