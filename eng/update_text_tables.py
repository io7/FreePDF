#!/usr/bin/env python3
"""Regenerates the glyph tables of FreePDF's text extraction.

Writes Pdf.Content.Text/GlyphTables.g.cs and src/packages/Adobe-Glyph-Tables.txt from the Adobe Glyph List,
the ITC Zapf Dingbats Glyph List and three Core14 AFM files (as distributed with Apache PDFBox 3.0.3; only the
data is used). Every download is pinned by SHA-256. WinAnsiEncoding and MacRomanEncoding come from Python's
cp1252 and mac_roman codecs with the ISO 32000-1 Annex D differences noted below.

Usage: python3 eng/update_text_tables.py
"""
import hashlib
import re
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Content.Text/GlyphTables.g.cs"
NOTICE = ROOT / "src/packages/Adobe-Glyph-Tables.txt"
AGL = "https://raw.githubusercontent.com/adobe-type-tools/agl-aglfn/4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6"
AFM = "https://raw.githubusercontent.com/apache/pdfbox/3.0.3/pdfbox/src/main/resources/org/apache/pdfbox/resources/afm"
SOURCES = {
    "glyphlist.txt": (f"{AGL}/glyphlist.txt", "a3b2f61ced9f3644cc0d4ecde5c59df34ca286c689d9484a43a710a81c466789"),
    "zapfdingbats.txt": (f"{AGL}/zapfdingbats.txt", "f6394e3cb8a447e84a1dad75d4baaf2aa7f45dc104faf369f4720e1a774ef2dc"),
    "Helvetica.afm": (f"{AFM}/Helvetica.afm", "5654761eab868ccb3080122accf13b4fb79edf37af09e1c57a881c35c334ee03"),
    "Symbol.afm": (f"{AFM}/Symbol.afm", "13eeef80def2f9a8d3a31a980731376823e8b48340ff3891ffc1cc0299c0b4fe"),
    "ZapfDingbats.afm": (f"{AFM}/ZapfDingbats.afm", "d3e7b9f6366ab0df06acbb217c8586c5962ff2efbd4e1b5566c7eaf5b745dd41"),
    "MustRead.html": (f"{AFM}/MustRead.html", "b226bfc00e1b8b8a80c7b3cfbbc322d13b4b0401f94cdeafdd93b2210ad802eb"),
}


def fetch(name):
    url, sha256 = SOURCES[name]
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != sha256:
        raise SystemExit(f"Hash mismatch: {url}")
    return data.decode("latin-1")


def entries(glyph_list):
    """The name;HHHH[ HHHH] lines of a glyph list, in file order."""
    return [line.strip() for line in glyph_list.splitlines() if line.strip() and not line.startswith("#")]


def list_notice(glyph_list):
    """The copyright and licence block between the first two '# ---' lines."""
    block, inside = [], False
    for line in glyph_list.splitlines():
        if line.startswith("# ---"):
            if inside:
                break
            inside = True
            continue
        if inside:
            block.append(line[2:] if line.startswith("# ") else line.lstrip("#"))
    return "\n".join(block).strip()


def afm_encoding(afm):
    """Glyph name for each code 0-255 of the font's built-in encoding ('' where undefined)."""
    names = [""] * 256
    for line in afm.splitlines():
        match = re.match(r"^C (\d+) ; WX \d+ ; N ([^ ;]+) ;", line)
        if match and int(match.group(1)) < 256:
            names[int(match.group(1))] = match.group(2)
    return names


def afm_notices(afm):
    return [m.group(1) for m in (re.match(r"^(?:Comment |Notice )(Copyright .+?)\s*$", l) for l in afm.splitlines()) if m]


def single_byte(codec, undefined, overrides):
    """256 characters; codes below 32 and codes the codec rejects become `undefined`."""
    chars = []
    for code in range(256):
        if code in overrides:
            chars.append(overrides[code])
            continue
        try:
            char = bytes([code]).decode(codec)
        except UnicodeDecodeError:
            char = undefined
        chars.append(undefined if code < 32 else char)
    return chars


def cs_names(names):
    rows = [", ".join(f'"{n}"' for n in names[i:i + 16]) for i in range(0, 256, 16)]
    return "{\n            " + ",\n            ".join(rows) + "\n        }"


def cs_chars(chars):
    rows = ['"' + "".join(f"\\u{ord(c):04X}" for c in chars[i:i + 16]) + '"' for i in range(0, 256, 16)]
    return "\n            " + "\n            + ".join(rows)


def main():
    agl, zapf = fetch("glyphlist.txt"), fetch("zapfdingbats.txt")
    helvetica, symbol, dingbats = fetch("Helvetica.afm"), fetch("Symbol.afm"), fetch("ZapfDingbats.afm")
    must_read = re.sub(r"\s+", " ", fetch("MustRead.html"))
    paragraph = re.search(r"(This file and the 14 PostScript\(R\) AFM files .*? support the use of the AFM files\.)", must_read)
    if not paragraph:
        raise SystemExit("MustRead paragraph not found.")
    notices = []
    for afm in (helvetica, symbol, dingbats):
        notices += [n for n in afm_notices(afm) if n not in notices]
    # ISO 32000-1 Annex D: in WinAnsiEncoding, all unused codes above 40 octal map to the bullet; the PDF
    # MacRomanEncoding has the currency sign at 333 octal (Mac OS Roman later put the euro there) and no Apple logo.
    # The duplicate codes draw the space and hyphen glyphs (WinAnsi 240 and 255 octal, MacRoman 312 octal), so
    # their glyph names give U+0020 and U+002D, not the no-break space and soft hyphen of the code pages.
    win_ansi = single_byte("cp1252", "•", {0x7F: "•", 0xA0: " ", 0xAD: "-"})
    mac_roman = single_byte("mac_roman", "�", {0x7F: "�", 0xCA: " ", 0xDB: "¤", 0xF0: "�"})
    for code in range(32):
        win_ansi[code] = "�"

    notice_text = (
        "The glyph tables in FreePDF's text extraction (Pdf.Content.Text/GlyphTables.g.cs) are generated by\n"
        "eng/update_text_tables.py from these sources.\n\n"
        "Adobe Glyph List and ITC Zapf Dingbats Glyph List, "
        "https://github.com/adobe-type-tools/agl-aglfn at commit 4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6:\n\n"
        + list_notice(agl) + "\n\n"
        "Built-in encodings of Helvetica (StandardEncoding), Symbol and ZapfDingbats, from Adobe's Core14 AFM files\n"
        "as distributed with Apache PDFBox 3.0.3. Modified by FreePDF: reduced to the glyph name of each character\n"
        "code; widths, kerning, bounding boxes and the other metrics are left out.\n\n"
        + "\n".join(notices) + "\n\nFrom the MustRead.html file that accompanies the AFM files:\n" + paragraph.group(1) + "\n")
    NOTICE.write_text(notice_text, encoding="utf-8", newline="\n")

    header = "\n".join("// " + line if line else "//" for line in notice_text.splitlines())
    agl_lines = "\n".join(entries(agl))
    zapf_lines = "\n".join(entries(zapf))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(f"""// <auto-generated>
// Generated by eng/update_text_tables.py; do not edit. Regenerate with that script.
//
{header}
// </auto-generated>

namespace PdfSharp.Pdf.Content.Text
{{
    /// <summary>
    /// Glyph lists and single-byte encodings for text extraction (ISO 32000-1 9.10.2 and Annex D).
    /// </summary>
    static class GlyphTables
    {{
        /// <summary>The Adobe Glyph List: one "name;HHHH[ HHHH]" line per glyph name.</summary>
        internal const string AdobeGlyphList = @"{agl_lines}";

        /// <summary>The ITC Zapf Dingbats Glyph List, in the same format.</summary>
        internal const string ZapfDingbatsGlyphList = @"{zapf_lines}";

        /// <summary>Glyph names of StandardEncoding by code ("" where undefined), from Helvetica's built-in encoding.</summary>
        internal static readonly string[] StandardEncoding = {cs_names(afm_encoding(helvetica))};

        /// <summary>Glyph names of the Symbol font's built-in encoding by code.</summary>
        internal static readonly string[] SymbolEncoding = {cs_names(afm_encoding(symbol))};

        /// <summary>Glyph names of the ZapfDingbats font's built-in encoding by code.</summary>
        internal static readonly string[] ZapfDingbatsEncoding = {cs_names(afm_encoding(dingbats))};

        /// <summary>WinAnsiEncoding: the character of each code (U+FFFD below 32).</summary>
        internal const string WinAnsiEncoding ={cs_chars(win_ansi)};

        /// <summary>MacRomanEncoding: the character of each code (U+FFFD where undefined).</summary>
        internal const string MacRomanEncoding ={cs_chars(mac_roman)};
    }}
}}
""", encoding="utf-8", newline="\n")
    print(f"Wrote {OUTPUT.relative_to(ROOT)} and {NOTICE.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
