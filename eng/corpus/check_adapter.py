"""Exercise the optional adapter against pinned veraPDF and original fixture PDFs."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

from run import GENERATOR, ROOT, command


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("verapdf", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--tool", type=Path, help="Installed freepdf-a11y executable; otherwise use the source-built DLL")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    cli = [str(args.tool)] if args.tool else ["dotnet", str(ROOT / "src/tools/FreePDF.Accessibility/bin/Release/net10.0/FreePDF.Accessibility.dll")]
    command(["dotnet", GENERATOR, "--fixtures", args.output])
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

    for name, expected, verdict in (("valid", 2, "MachineChecksPassed"), ("missing-alt", 1, "Failed"),
                                    ("missing-language", 1, "Failed"), ("untagged", 1, "Failed"), ("malformed", 3, "Incomplete")):
        text, sha = scan(name, expected, "--format", "json")
        report = json.loads(text)
        independent = report["independentValidation"]
        if report["schemaVersion"] != "2.0" or independent["status"] != verdict or independent["validatorVersion"] != "1.30.3":
            raise RuntimeError(f"Incorrect independent verdict or validator version for {name}")
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
               "additionalControls": ["explicit automated policy", "missing validator", "unsupported profile", "text", "html", "sarif", "inventory"],
               "scope": "Original controls only; not complete conformance coverage"}
    (args.output / "adapter-summary.json").write_text(json.dumps(summary, indent=2))
    print("PASS: optional adapter, five fixture outcomes, seven additional controls, original-byte preservation and separate reports.")


if __name__ == "__main__":
    main()
