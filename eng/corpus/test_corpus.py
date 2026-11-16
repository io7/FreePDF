"""Contract tests for corpus diversity, strict failures and the public CLI."""
from collections import Counter
from html.parser import HTMLParser
import hashlib
import itertools
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from manifest import AXES, scenario, write_manifest
from run import ROOT, GENERATOR, assert_semantics, check_one, rendered

CLI = ROOT / "src/tools/FreePDF.Accessibility/bin/Release/net10.0/FreePDF.Accessibility.dll"


class ManifestTests(unittest.TestCase):
    def test_full_suite_has_28000_distinct_parameter_sets_and_84000_producer_inputs(self):
        cases = [scenario(i) for i in range(28000)]
        parameters = {json.dumps({key: case[key] for key in AXES}, sort_keys=True) for case in cases}
        self.assertEqual(28000, len(parameters))
        self.assertEqual(84000, len({(case["id"], producer) for case in cases for producer in ("freepdf", "reportlab", "chromium")}))
        for axis, values in AXES.items():
            self.assertEqual({json.dumps(v) for v in values}, {json.dumps(c[axis]) for c in cases})

    def test_sharding_is_disjoint_complete_and_preserves_the_same_scenarios(self):
        with tempfile.TemporaryDirectory() as folder:
            gathered = []
            for shard in range(24):
                path = Path(folder) / str(shard)
                write_manifest(path, 28000, 20261116, shard, 24)
                gathered.extend(json.loads(line) for line in path.read_text().splitlines())
            self.assertEqual(28000, len(gathered))
            self.assertEqual(28000, len({c["id"] for c in gathered}))
            for c in gathered:
                self.assertEqual(scenario(c["index"]), c)

    def test_render_samples_cover_every_page_geometry_and_axis_value(self):
        # Tiers from .github/workflows/accessibility-corpus.yml: (scenario count, render every Nth).
        for tier, count, every in (("nightly", 600, 10), ("full", 28000, 100)):
            with self.subTest(tier=tier):
                sample = [c for c in (scenario(i) for i in range(count)) if rendered(c, every)]
                self.assertGreater(len(sample), count // every // 2)
                self.assertLess(len(sample), count // every * 2)
                geometries = {(tuple(c["paper"]), c["landscape"]) for c in sample}
                self.assertEqual(len(AXES["paper"]) * len(AXES["landscape"]), len(geometries))
                for axis, values in AXES.items():
                    self.assertEqual({json.dumps(v) for v in values}, {json.dumps(c[axis]) for c in sample}, axis)
                if tier == "full":
                    for a, b in itertools.combinations(AXES, 2):
                        pairs = {(json.dumps(c[a]), json.dumps(c[b])) for c in sample}
                        self.assertEqual(len(AXES[a]) * len(AXES[b]), len(pairs), f"{a} x {b}")
        smoke = [scenario(i) for i in range(60)]
        self.assertTrue(all(rendered(c, 1) for c in smoke))

    def test_seed_changes_parameters_and_does_not_depend_on_producer(self):
        self.assertNotEqual(scenario(0, 1), scenario(0, 2))
        self.assertEqual(scenario(42, 17), scenario(42, 17))

    def test_semantics_reject_lost_text_and_lost_pages(self):
        case = scenario(0)
        text = "\f".join("\n".join(page) for page in case["lines"]) + "\f"
        assert_semantics(case, text)
        with self.assertRaises(ValueError):
            assert_semantics(case, text.replace(case["lines"][0][0], ""))
        with self.assertRaises(ValueError):
            assert_semantics(case, "")

    def test_parser_rejection_is_a_failure_and_keeps_a_reproducer(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            case = scenario(0)
            source = root / "sources/freepdf" / (case["id"] + ".pdf")
            source.parent.mkdir(parents=True)
            source.write_bytes(b"not a pdf")
            with patch("run.command", side_effect=RuntimeError("qpdf rejected input")):
                result = check_one(case, "freepdf", root, 5, 1, False)
            self.assertEqual("failed", result["outcome"])
            self.assertTrue((root / "failures/freepdf" / case["id"] / "original.pdf").exists())


class CliTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.folder = tempfile.TemporaryDirectory()
        cls.root = Path(cls.folder.name)
        subprocess.run(["dotnet", str(GENERATOR), "--fixtures", str(cls.root)], check=True, capture_output=True)

    @classmethod
    def tearDownClass(cls):
        cls.folder.cleanup()

    def scan(self, name="valid.pdf", *args):
        return subprocess.run(["dotnet", str(CLI), str(self.root / name), *args], capture_output=True, text=True, timeout=20)

    def test_default_requires_review_and_automated_policy_is_explicit(self):
        result = self.scan()
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertIn("Human review required", result.stdout)
        self.assertIn("partial PDF/UA-1", result.stdout)
        self.assertIn("Next step:", result.stdout)
        result = self.scan("valid.pdf", "--format", "json")
        report = json.loads(result.stdout)
        self.assertEqual("NeedsReview", report["status"])
        self.assertEqual("Partial", report["coverage"])
        self.assertEqual(hashlib.sha256((self.root / "valid.pdf").read_bytes()).hexdigest(), report["documentSha256"])
        self.assertEqual(0, self.scan("valid.pdf", "--policy", "automated").returncode)

    def test_negative_fixtures_fail_with_the_expected_rules(self):
        for name, rule in (("missing-alt", "ALT"), ("missing-language", "LANG"), ("untagged", "TREE")):
            with self.subTest(name=name):
                result = self.scan(name + ".pdf", "--policy", "automated", "--format", "json")
                self.assertEqual(1, result.returncode, result.stderr)
                self.assertIn("FPDF-A11Y-" + rule, {f["ruleId"] for f in json.loads(result.stdout)["findings"]})

    def test_detected_failures_win_over_an_incomplete_scan(self):
        result = self.scan("incomplete-missing-language.pdf", "--policy", "automated", "--format", "json")
        self.assertEqual(1, result.returncode, result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual("Incomplete", report["status"])
        self.assertTrue(report["hasFailures"])
        self.assertIn("FPDF-A11Y-LANG", {f["ruleId"] for f in report["findings"] if f["status"] == "Failed"})
        text = self.scan("incomplete-missing-language.pdf")
        self.assertEqual(1, text.returncode)
        self.assertIn("Problems found", text.stdout)
        self.assertIn("could not be checked", text.stdout)
        sarif = json.loads(self.scan("incomplete-missing-language.pdf", "--format", "sarif").stdout)
        self.assertTrue(sarif["runs"][0]["properties"]["hasFailures"])

    def test_unreadable_files_are_incomplete_and_cannot_satisfy_automated_policy(self):
        (self.root / "malformed.pdf").write_bytes(b"not a PDF")
        result = self.scan("malformed.pdf", "--policy", "automated", "--format", "json")
        self.assertEqual(3, result.returncode)
        self.assertFalse(json.loads(result.stdout)["scanCompleted"])

    def test_text_reports_problems_and_incomplete_scans_without_claiming_success(self):
        result = self.scan("missing-alt.pdf")
        self.assertEqual(1, result.returncode)
        self.assertIn("Problems found", result.stdout)
        self.assertIn("FAIL FPDF-A11Y-ALT:", result.stdout)
        self.assertIn("How to fix:", result.stdout)
        (self.root / "broken.pdf").write_bytes(b"not a PDF")
        result = self.scan("broken.pdf", "--policy", "automated")
        self.assertEqual(3, result.returncode)
        self.assertIn("Scan incomplete", result.stdout)
        self.assertNotIn("No failures found", result.stdout)

    @unittest.skipIf(os.name == "nt", "Windows does not allow control characters in file names")
    def test_text_and_output_confirmation_escape_terminal_controls(self):
        name = "untrusted-\x1b[31m\n\u202e.pdf"
        (self.root / name).write_bytes((self.root / "valid.pdf").read_bytes())
        result = self.scan(name)
        self.assertEqual(2, result.returncode)
        self.assertNotIn("\x1b", result.stdout)
        self.assertNotIn("\u202e", result.stdout)
        self.assertIn(r"untrusted-\u001b[31m\u000a\u202e.pdf", result.stdout)
        output = self.root / "report-\x1b[31m.html"
        result = self.scan(name, "--format", "html", "--output", str(output))
        self.assertEqual(2, result.returncode)
        self.assertEqual("", result.stdout)
        self.assertTrue(output.is_file())
        self.assertIn("Report written to", result.stderr)
        self.assertNotIn("\x1b", result.stderr)

    def test_html_escapes_document_text_and_contains_manual_review(self):
        result = self.scan("valid.pdf", "--format", "html")
        self.assertEqual(2, result.returncode)
        tags = []
        class Parser(HTMLParser):
            def handle_starttag(self, tag, attributes):
                tags.append(tag)
        Parser().feed(result.stdout)
        self.assertNotIn("script", tags)
        self.assertIn("&lt;script&gt;", result.stdout)
        self.assertIn("Human accessibility review", result.stdout)
        self.assertIn('lang="en"', result.stdout)

    def test_sarif_has_rule_metadata_hash_and_explicit_coverage(self):
        result = self.scan("missing-alt.pdf", "--format", "sarif")
        self.assertEqual(1, result.returncode)
        sarif = json.loads(result.stdout)
        self.assertEqual("2.1.0", sarif["version"])
        run = sarif["runs"][0]
        self.assertEqual("Partial", run["properties"]["coverage"])
        self.assertTrue(any(r["ruleId"] == "FPDF-A11Y-ALT" and r["level"] == "error" for r in run["results"]))
        self.assertEqual(64, len(run["artifacts"][0]["hashes"]["sha-256"]))

    def test_output_never_overwrites_input_or_existing_reports(self):
        original = (self.root / "valid.pdf").read_bytes()
        self.assertEqual(64, self.scan("valid.pdf", "--output", str(self.root / "valid.pdf")).returncode)
        self.assertEqual(original, (self.root / "valid.pdf").read_bytes())
        path = self.root / "existing.json"
        path.write_text("keep")
        self.assertEqual(64, self.scan("valid.pdf", "--output", str(path)).returncode)
        self.assertEqual("keep", path.read_text())


if __name__ == "__main__":
    unittest.main()
