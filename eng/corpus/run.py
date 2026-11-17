"""Strict multi-producer CI corpus. Any missing input, rejection or regression fails.

Build CorpusGenerator and eng/research/CorpusCheck in Release before running.
Each PDF is round-tripped in its own bounded child process using CorpusCheck.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import platform
import shutil
import signal
import subprocess
import sys
import time
import unicodedata

from manifest import PRODUCERS, write_manifest
from producers import ROOT, FONT

GENERATOR = ROOT / "eng/corpus/CorpusGenerator/bin/Release/net10.0/CorpusGenerator.dll"
CHECKER = ROOT / "eng/research/CorpusCheck/bin/Release/net10.0/CorpusCheck.dll"


def command(args, timeout=30):
    env = dict(os.environ, DOTNET_GCHeapHardLimit="0x20000000", DOTNET_CLI_TELEMETRY_OPTOUT="1")
    process = subprocess.Popen([str(a) for a in args], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               start_new_session=True, env=env)
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.communicate()
        raise RuntimeError(f"{Path(str(args[0])).name}: timeout after {timeout}s")
    if process.returncode:
        detail = (stdout + stderr).decode("utf-8", "replace")[-4000:]
        raise RuntimeError(f"{Path(str(args[0])).name}: exit {process.returncode}: {detail}")
    return stdout.decode("utf-8", "replace")


def rendered(case, every):
    """Render about one scenario in `every`, chosen by scenario hash.

    Every Nth index would pin the low digits of manifest.py's permutation and
    never render some page geometries (for example A4 landscape in the full tier).
    """
    return int(case["scenarioSha256"][:16], 16) % every == 0


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def normalized(text):
    return " ".join(unicodedata.normalize("NFC", text).split())


def assert_semantics(case, text):
    pages = text.split("\f")
    if pages and not pages[-1].strip():
        pages.pop()
    if len(pages) != case["pages"]:
        raise ValueError(f"Extracted {len(pages)} pages, expected {case['pages']}")
    for number, expected in enumerate(case["lines"]):
        actual = normalized(pages[number])
        for line in expected:
            if normalized(line) not in actual:
                raise ValueError(f"Missing semantic content on page {number + 1}: {line}")


def render_diff(original, saved, directory, pages, timeout):
    from PIL import Image, ImageChops
    maximum = 0.0
    for page in range(1, pages + 1):
        images = []
        for label, source in (("before", original), ("after", saved)):
            prefix = directory / f"{label}-{page}"
            command(["pdftoppm", "-f", page, "-l", page, "-singlefile", "-r", "60", "-gray", "-png", source, prefix], timeout)
            images.append(prefix.with_suffix(".png"))
        with Image.open(images[0]) as before, Image.open(images[1]) as after:
            if before.size != after.size:
                raise ValueError(f"Rendered dimensions changed on page {page}")
            difference = ImageChops.difference(before.convert("L"), after.convert("L"))
            histogram = difference.histogram()
            fraction = sum(histogram[17:]) / (before.width * before.height)
            maximum = max(maximum, fraction)
            if fraction > .001:
                difference.save(directory / f"difference-{page}.png")
                raise ValueError(f"Page {page}: {fraction:.6f} of pixels changed by more than 16/255 (limit .001)")
        for path in images:
            path.unlink()
    return maximum


def check_one(case, producer, output, timeout, render_every, keep_pdfs):
    source = output / "sources" / producer / (case["id"] + ".pdf")
    work = output / "work" / producer / case["id"]
    work.mkdir(parents=True)
    saved = work / "roundtrip.pdf"
    result = {"case": case["id"], "producer": producer, "scenarioSha256": case["scenarioSha256"], "outcome": "failed", "renderedPages": 0}
    start = time.monotonic()
    try:
        result["inputSha256"] = digest(source)
        result["bytes"] = source.stat().st_size
        command(["qpdf", "--check", source], timeout)
        result["roundtrip"] = json.loads(command(["dotnet", CHECKER, "check-a11y", source, saved], timeout).strip())
        result["outputSha256"] = digest(saved)
        if result["roundtrip"]["pages"] != case["pages"]:
            raise ValueError("FreePDF page count differs from the scenario")
        if producer == "freepdf" and result["roundtrip"]["before"] != "NeedsReview":
            raise ValueError("FreePDF's tagged fixture did not pass the implemented preflight checks: " + json.dumps(result["roundtrip"]))
        command(["qpdf", "--check", saved], timeout)
        original_text = command(["pdftotext", "-enc", "UTF-8", source, "-"], timeout)
        saved_text = command(["pdftotext", "-enc", "UTF-8", saved, "-"], timeout)
        assert_semantics(case, original_text)
        assert_semantics(case, saved_text)
        if normalized(original_text) != normalized(saved_text):
            raise ValueError("Extracted text changed on round trip")
        result["semanticSha256"] = hashlib.sha256(json.dumps(case["lines"], ensure_ascii=False).encode()).hexdigest()
        if rendered(case, render_every):
            result["maxPixelDifference"] = render_diff(source, saved, work, case["pages"], timeout)
            result["renderedPages"] = case["pages"]
        result["outcome"] = "ok"
    except Exception as error:
        result["error"] = str(error)
        failure = output / "failures" / producer / case["id"]
        failure.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(work), str(failure))
        if source.exists():
            shutil.copy2(source, failure / "original.pdf")
        (failure / "scenario.json").write_text(json.dumps(case, ensure_ascii=False, indent=2))
        (failure / "result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        shutil.rmtree(work)
    finally:
        if not keep_pdfs and source.exists():
            source.unlink()
    result["seconds"] = round(time.monotonic() - start, 3)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="new output directory; never overwrites a previous run")
    parser.add_argument("--count", type=int, default=60, help="scenario count before sharding; PDF count is three times this")
    parser.add_argument("--seed", type=int, default=20261116)
    parser.add_argument("--shard", type=int, default=0)
    parser.add_argument("--shards", type=int, default=1)
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--generation-workers", type=int, default=1, choices=range(1, 5), help="independent Chromium processes; useful for an unsharded full run")
    parser.add_argument("--timeout", type=int, default=30)
    parser.add_argument("--render-every", type=int, default=1, help="render all pages of about one scenario in N (chosen by scenario hash), across all producers")
    parser.add_argument("--keep-pdfs", action="store_true")
    args = parser.parse_args()
    if min(args.workers, args.timeout, args.render_every) < 1:
        parser.error("workers, timeout and render-every must be positive")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    summary = {"status": "failed", "scenarioCount": 0, "expectedPdfs": 0, "checkedPdfs": 0, "passedPdfs": 0,
               "failedPdfs": 0, "renderedPdfs": 0, "renderedPages": 0, "seed": args.seed,
               "shard": args.shard, "shards": args.shards, "renderEvery": args.render_every}
    try:
        versions = {"python": sys.version, "platform": platform.platform(),
                    "producerPackages": {p: importlib.metadata.version(p) for p in ("reportlab", "playwright", "pillow")},
                    "qpdf": command(["qpdf", "--version"]).strip(), "dotnet": command(["dotnet", "--version"]).strip(),
                    "poppler": subprocess.run(["pdftotext", "-v"], capture_output=True, text=True, check=True).stderr.strip(),
                    "fontSha256": digest(FONT), "generatorSha256": digest(GENERATOR), "checkerSha256": digest(CHECKER),
                    "assemblySha256": {f"{tool}/{path.name}": digest(path) for tool, directory in (("generator", GENERATOR.parent), ("checker", CHECKER.parent)) for path in sorted(directory.glob("*.dll"))},
                    "recipeSha256": {name: digest(ROOT / "eng/corpus" / name) for name in ("manifest.py", "producers.py", "run.py", "requirements.txt")},
                    "gitCommit": command(["git", "-C", ROOT, "rev-parse", "HEAD"]).strip(),
                    "workingTreeDirty": bool(command(["git", "-C", ROOT, "status", "--porcelain"]).strip())}
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            browser = p.chromium.launch()
            versions["chromium"] = browser.version
            browser.close()
        (output / "tools.json").write_text(json.dumps(versions, indent=2))
        manifest = output / "manifest.jsonl"
        write_manifest(manifest, args.count, args.seed, args.shard, args.shards)
        cases = [json.loads(line) for line in manifest.read_text().splitlines()]
        if not cases:
            raise ValueError("Shard contains no scenarios")
        summary.update(scenarioCount=len(cases), expectedPdfs=len(cases) * len(PRODUCERS), manifestSha256=digest(manifest))
        generation_timeout = min(1800, max(120, len(cases) * 2))
        jobs = [["dotnet", GENERATOR, manifest, output / "sources/freepdf"],
                [sys.executable, ROOT / "eng/corpus/producers.py", "reportlab", manifest, output / "sources/reportlab"]]
        for worker in range(min(args.generation_workers, len(cases))):
            part = output / f"chromium-{worker}.jsonl"
            part.write_text("".join(json.dumps(case, ensure_ascii=False) + "\n" for case in cases[worker::args.generation_workers]))
            jobs.append([sys.executable, ROOT / "eng/corpus/producers.py", "chromium", part, output / "sources/chromium"])
        with ThreadPoolExecutor(max_workers=len(jobs)) as pool:
            for future in as_completed([pool.submit(command, job, generation_timeout) for job in jobs]):
                future.result()
        # Exact inventory is part of the gate; a generator cannot silently skip a file.
        expected = {f"{producer}/{case['id']}.pdf" for producer in PRODUCERS for case in cases}
        actual = {str(path.relative_to(output / "sources")) for path in (output / "sources").rglob("*.pdf")}
        if actual != expected:
            raise ValueError("Generated PDF inventory differs from manifest")
        print(f"Generated {len(actual)} PDFs; checking with {args.workers} workers", flush=True)
        with (output / "results.jsonl").open("w") as log, ThreadPoolExecutor(max_workers=args.workers) as pool:
            futures = [pool.submit(check_one, case, producer, output, args.timeout, args.render_every, args.keep_pdfs)
                       for case in cases for producer in PRODUCERS]
            for future in as_completed(futures):
                result = future.result()
                log.write(json.dumps(result, ensure_ascii=False) + "\n")
                log.flush()
                summary["checkedPdfs"] += 1
                summary["passedPdfs" if result["outcome"] == "ok" else "failedPdfs"] += 1
                summary["renderedPdfs"] += bool(result["renderedPages"])
                summary["renderedPages"] += result["renderedPages"]
                if summary["checkedPdfs"] % 300 == 0:
                    print(f"Checked {summary['checkedPdfs']}/{len(actual)}; failed {summary['failedPdfs']}", flush=True)
        summary["status"] = "passed" if summary["failedPdfs"] == 0 and summary["checkedPdfs"] == summary["expectedPdfs"] else "failed"
    except Exception as error:
        summary["error"] = str(error)
    (output / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary), flush=True)
    return 0 if summary["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
