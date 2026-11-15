"""Compare original positive/negative fixtures with the pinned veraPDF UA-1 oracle.

Agreement on these fixtures does not imply complete scanner coverage.
"""
import argparse
import json
from pathlib import Path
import subprocess

from run import GENERATOR, command, digest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("verapdf", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    command(["dotnet", GENERATOR, "--fixtures", args.output])
    results = []
    for name, expected in (("valid", True), ("missing-alt", False), ("missing-language", False), ("untagged", False)):
        path = args.output / (name + ".pdf")
        # veraPDF's verdict is in JSON; a nonconforming document is an expected negative control.
        process = subprocess.run([str(args.verapdf), "--format", "json", "--flavour", "ua1", str(path)], capture_output=True, text=True, timeout=60)
        (args.output / (name + "-verapdf.json")).write_text(process.stdout)
        report = json.loads(process.stdout)
        jobs = report["report"]["jobs"]
        if len(jobs) != 1 or len(jobs[0].get("validationResult", [])) != 1:
            raise RuntimeError("veraPDF did not return exactly one validation result")
        compliant = jobs[0]["validationResult"][0]["compliant"]
        if compliant is not expected:
            raise RuntimeError(f"veraPDF verdict mismatch for {name}: expected {expected}, got {compliant}")
        results.append({"fixture": name, "sha256": digest(path), "expectedMachineConformance": expected, "verapdfMachineConformance": compliant})
    (args.output / "oracle-summary.json").write_text(json.dumps({"verapdf": command([args.verapdf, "--version"]).strip(), "results": results}, indent=2))
    print("veraPDF agreed with all four fixture expectations (machine checks only).")


if __name__ == "__main__":
    main()
