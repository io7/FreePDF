#!/usr/bin/env python3
# Original. MIT; see LICENSE.
"""Keeps the C# blocks of docs/guide equal to the tested examples in src/samples/FreePDF.Samples.

An example marks its code with "// begin-snippet: <name>" and "// end-snippet". A guide page puts
"<!-- snippet: <name> -->" on the line before a ```csharp block. Without options the script fails when a
block differs from its snippet, when a page names an unknown snippet, or when a snippet is on no page.
--write rewrites the blocks from the examples.
"""
import argparse
import pathlib
import re
import sys
import textwrap

ROOT = pathlib.Path(__file__).resolve().parent.parent
SAMPLES = ROOT / "src" / "samples" / "FreePDF.Samples"
GUIDE = ROOT / "docs" / "guide"
BEGIN = re.compile(r"^\s*// begin-snippet: ([a-z0-9-]+)\s*$")
END = re.compile(r"^\s*// end-snippet\s*$")
MARK = re.compile(r"^<!-- snippet: ([a-z0-9-]+) -->\s*$")
FENCE_OPEN = "```csharp"
FENCE_CLOSE = "```"


def read_snippets():
    snippets = {}
    for path in sorted(SAMPLES.glob("*.cs")):
        name = None
        lines = []
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            begin = BEGIN.match(line)
            if begin:
                if name is not None:
                    sys.exit(f"{path}:{number}: snippet {name} is not closed")
                name, lines = begin.group(1), []
            elif END.match(line):
                if name is None:
                    sys.exit(f"{path}:{number}: end-snippet without begin-snippet")
                if name in snippets:
                    sys.exit(f"{path}:{number}: snippet {name} is defined twice")
                snippets[name] = textwrap.dedent("\n".join(lines)).strip("\n")
                name = None
            elif name is not None:
                lines.append(line)
        if name is not None:
            sys.exit(f"{path}: snippet {name} is not closed")
    return snippets


def sync_page(path, snippets, used):
    """Returns the page with its marked blocks replaced, and whether any block changed."""
    lines = path.read_text(encoding="utf-8").splitlines()
    out = []
    changed = False
    i = 0
    while i < len(lines):
        out.append(lines[i])
        mark = MARK.match(lines[i])
        i += 1
        if not mark:
            continue
        name = mark.group(1)
        if name not in snippets:
            sys.exit(f"{path}:{i}: unknown snippet {name}")
        if i >= len(lines) or lines[i] != FENCE_OPEN:
            sys.exit(f"{path}:{i + 1}: a {FENCE_OPEN} block must follow the snippet mark")
        end = i + 1
        while end < len(lines) and lines[end] != FENCE_CLOSE:
            end += 1
        if end == len(lines):
            sys.exit(f"{path}:{i + 1}: the code block is not closed")
        used.add(name)
        if "\n".join(lines[i + 1:end]) != snippets[name]:
            changed = True
        out.append(FENCE_OPEN)
        out.extend(snippets[name].splitlines())
        out.append(FENCE_CLOSE)
        i = end + 1
    return "\n".join(out) + "\n", changed


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--write", action="store_true", help="rewrite the guide's blocks from the examples")
    args = parser.parse_args()

    snippets = read_snippets()
    used = set()
    stale = []
    for page in sorted(GUIDE.glob("*.md")):
        text, changed = sync_page(page, snippets, used)
        if not changed:
            continue
        if args.write:
            page.write_text(text, encoding="utf-8")
            print(f"updated {page.relative_to(ROOT)}")
        else:
            stale.append(page)

    unused = sorted(set(snippets) - used)
    for name in unused:
        print(f"snippet {name} is on no guide page", file=sys.stderr)
    for page in stale:
        print(f"{page.relative_to(ROOT)} differs from the examples; run python3 eng/sync_doc_snippets.py --write",
              file=sys.stderr)
    if unused or stale:
        return 1
    print(f"{len(snippets)} snippets; the guide matches the tested examples")
    return 0


if __name__ == "__main__":
    sys.exit(main())
