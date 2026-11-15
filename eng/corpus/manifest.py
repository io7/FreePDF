"""Original synthetic scenario specification; no downloaded/customer documents.

The same JSONL drives every producer. Scenario identities, parameters and content
are deterministic. PDF bytes may still differ because producer metadata varies.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

VERSION = 1
PRODUCERS = ("freepdf", "reportlab", "chromium")
AXES = {
    "paper": [[595, 842], [612, 792], [420, 595]],
    "landscape": [False, True],
    "fontSize": [9, 11, 13, 15],
    "margin": [18, 30, 42],
    "pages": [1, 2, 3],
    "rows": [2, 4, 6, 8],
    "layout": ["paragraphs", "list", "table"],
    "illustration": ["rectangle", "ellipse", "image"],
    "style": ["regular", "bold", "italic"],
    "color": ["#142536", "#653215", "#254d2b", "#4c285e"],
    "languageText": ["Example text", "Café ÆØÅ", "Ελληνικά"],
    "compress": [False, True],
}
SPACE = math.prod(len(values) for values in AXES.values())
assert math.gcd(7919, SPACE) == 1


def scenario(index, seed=20261116):
    if not 0 <= index < SPACE:
        raise ValueError(f"index must be in [0, {SPACE})")
    n = (index * 7919 + seed) % SPACE
    case = {"schemaVersion": VERSION, "id": f"case-{index:06d}", "index": index, "seed": seed}
    for key, values in AXES.items():
        case[key] = values[n % len(values)]
        n //= len(values)
    case["width"], case["height"] = case["paper"][::-1] if case["landscape"] else case["paper"]
    case["lines"] = [
        [f"C{index:05d}P{page + 1}"] +
        [f"R{row:02d} {case['languageText']}" for row in range(case["rows"])]
        for page in range(case["pages"])
    ]
    encoded = json.dumps(case, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode()
    case["scenarioSha256"] = hashlib.sha256(encoded).hexdigest()
    return case


def write_manifest(path, count, seed, shard=0, shards=1):
    if not 1 <= count <= SPACE or not 0 <= shard < shards:
        raise ValueError("invalid count or shard")
    with Path(path).open("x", encoding="utf-8") as output:
        for index in range(shard, count, shards):
            output.write(json.dumps(scenario(index, seed), ensure_ascii=False, sort_keys=True) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--count", type=int, default=14000, help="scenarios; three producers yield 42,000 PDFs")
    parser.add_argument("--seed", type=int, default=20261116)
    parser.add_argument("--shard", type=int, default=0)
    parser.add_argument("--shards", type=int, default=1)
    args = parser.parse_args()
    write_manifest(args.output, args.count, args.seed, args.shard, args.shards)
