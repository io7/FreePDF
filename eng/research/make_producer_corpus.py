"""Build a corpus of PDFs from original source documents through every PDF producer installed on this host.

Each producer has its own writer quirks (object streams, xref streams, linearisation, tagged structure, fonts, forms,
encryption). The sources are written here, so the corpus carries no third-party content.
"""
import os
import shutil
import subprocess
import sys
from pathlib import Path

out = Path(sys.argv[1]).resolve()
src = out / "_sources"
src.mkdir(parents=True, exist_ok=True)
log = []


def run(name, cmd, cwd=None, timeout=180):
    try:
        r = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=timeout)
        ok = r.returncode == 0
        log.append(f"{'ok  ' if ok else 'FAIL'} {name}: {' '.join(map(str, cmd))[:160]}" + ("" if ok else f" -> {r.stderr[-300:]}"))
        return ok
    except Exception as e:  # noqa: BLE001 - record and continue with the next producer
        log.append(f"FAIL {name}: {e}")
        return False


# --- original sources ---------------------------------------------------------------------------------------------
subprocess.run(["magick", "-size", "240x160", "gradient:steelblue-white", "-fill", "black", "-pointsize", "24",
                "-annotate", "+20+90", "FreePDF corpus", str(src / "image.png")], check=True)
subprocess.run(["magick", str(src / "image.png"), "-quality", "85", str(src / "image.jpg")], check=True)
rows = "\n".join(f"<tr><td>{i}</td><td>Linje {i} med æøå ÆØÅ</td><td>{i * 12.5:.2f}</td></tr>" for i in range(1, 61))
html = f"""<!doctype html><html lang="nb"><head><meta charset="utf-8"><title>Korpus for FreePDF</title>
<style>body{{font-family:sans-serif}} table{{border-collapse:collapse}} td{{border:1px solid #888;padding:2px 6px}}</style></head>
<body><h1>Korpusdokument</h1><p>Første avsnitt med <a href="https://example.org/">en lenke</a> og ÆØÅ æøå.</p>
<h2>Bilder</h2><img src="image.png" alt="Gradient"><img src="image.jpg" alt="JPEG">
<h2>Tabell</h2><table>{rows}</table><h2 id="slutt">Slutt</h2><p><a href="#slutt">Intern lenke</a></p></body></html>"""
(src / "doc.html").write_text(html, encoding="utf-8")
(src / "doc.md").write_text("# Korpusdokument\n\nFørste avsnitt med *kursiv* og **fet** tekst, ÆØÅ.\n\n"
                            "| Nr | Tekst |\n|---|---|\n" + "\n".join(f"| {i} | Linje {i} |" for i in range(1, 40)) +
                            "\n\n![Bilde](image.png)\n", encoding="utf-8")
tex = r"""\documentclass{article}
\usepackage[T1]{fontenc}\usepackage[utf8]{inputenc}\usepackage{graphicx}\usepackage{hyperref}\usepackage{longtable}
\begin{document}\tableofcontents\section{Innledning}Tekst med æøå og en \href{https://example.org}{lenke}.
\section{Matematikk}$$\int_0^1 x^2\,dx = \frac{1}{3}$$\section{Bilde}\includegraphics[width=5cm]{image.png}
\section{Tabell}\begin{longtable}{|r|l|}\hline Nr & Tekst\\\hline
""" + "\n".join(f"{i} & Linje {i}\\\\" for i in range(1, 90)) + r"""
\hline\end{longtable}\end{document}"""
(src / "doc.tex").write_text(tex, encoding="utf-8")
xetex = tex.replace(r"\usepackage[T1]{fontenc}\usepackage[utf8]{inputenc}", r"\usepackage{fontspec}\setmainfont{DejaVu Serif}")
(src / "docx.tex").write_text(xetex, encoding="utf-8")
(src / "drawing.svg").write_text("""<svg xmlns="http://www.w3.org/2000/svg" width="400" height="300">
<defs><linearGradient id="g"><stop offset="0" stop-color="#A11F63"/><stop offset="1" stop-color="#fff" stop-opacity="0.3"/></linearGradient></defs>
<rect x="10" y="10" width="380" height="280" rx="20" fill="url(#g)"/><circle cx="200" cy="150" r="80" fill="none" stroke="#222" stroke-width="6" stroke-dasharray="12 6"/>
<text x="50" y="160" font-family="DejaVu Sans" font-size="32">Inkscape ÆØÅ</text></svg>""", encoding="utf-8")

# --- producers ------------------------------------------------------------------------------------------------------
pdflatex = out / "pdflatex"; pdflatex.mkdir(exist_ok=True)
for _ in range(2):  # twice for the table of contents
    run("pdflatex", ["pdflatex", "-interaction=nonstopmode", "-output-directory", str(pdflatex), str(src / "doc.tex")], cwd=src)
xelatex = out / "xelatex"; xelatex.mkdir(exist_ok=True)
for _ in range(2):
    run("xelatex", ["xelatex", "-interaction=nonstopmode", "-output-directory", str(xelatex), str(src / "docx.tex")], cwd=src)
for d in (pdflatex, xelatex):
    for f in d.iterdir():
        if f.suffix != ".pdf":
            f.unlink()

chrome = out / "chrome"; chrome.mkdir(exist_ok=True)
run("chrome", ["google-chrome", "--headless=new", "--no-sandbox", "--disable-gpu", "--no-pdf-header-footer",
               f"--print-to-pdf={chrome / 'chrome.pdf'}", (src / "doc.html").as_uri()])
run("chrome-tagged", ["google-chrome", "--headless=new", "--no-sandbox", "--disable-gpu", "--generate-pdf-document-outline",
                      f"--print-to-pdf={chrome / 'chrome-outline.pdf'}", (src / "doc.html").as_uri()])

lo = out / "libreoffice"; lo.mkdir(exist_ok=True)
run("pandoc-docx", ["pandoc", str(src / "doc.md"), "-o", str(src / "doc.docx")], cwd=src)
profile = f"-env:UserInstallation=file://{src / 'lo-profile'}"
run("lo-docx", ["soffice", profile, "--headless", "--convert-to", "pdf", "--outdir", str(lo), str(src / "doc.docx")], timeout=240)
run("lo-pdfa", ["soffice", profile, "--headless", "--convert-to",
                'pdf:writer_pdf_Export:{"SelectPdfVersion":{"type":"long","value":"2"},"UseTaggedPDF":{"type":"boolean","value":"true"}}',
                "--outdir", str(lo / "pdfa"), str(src / "doc.docx")], timeout=240)
run("lo-html", ["soffice", profile, "--headless", "--convert-to", "pdf:writer_web_pdf_Export", "--outdir", str(lo / "html"), str(src / "doc.html")], timeout=240)

base = [p for p in (pdflatex / "doc.pdf", chrome / "chrome.pdf", lo / "doc.pdf") if p.exists()]
gs = out / "ghostscript"; gs.mkdir(exist_ok=True)
for p in base:
    stem = p.parent.name + "-" + p.stem
    run("gs-default", ["gs", "-q", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pdfwrite", f"-sOutputFile={gs / (stem + '.pdf')}", str(p)])
    run("gs-14", ["gs", "-q", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pdfwrite", "-dCompatibilityLevel=1.4", f"-sOutputFile={gs / (stem + '-14.pdf')}", str(p)])
    run("gs-ebook", ["gs", "-q", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pdfwrite", "-dPDFSETTINGS=/ebook", f"-sOutputFile={gs / (stem + '-ebook.pdf')}", str(p)])
    run("gs-pdfa2", ["gs", "-q", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pdfwrite", "-dPDFA=2", "-dPDFACompatibilityPolicy=1", "-sColorConversionStrategy=RGB",
                     f"-sOutputFile={gs / (stem + '-pdfa2.pdf')}", str(p)])
    run("gs-encrypt", ["gs", "-q", "-dBATCH", "-dNOPAUSE", "-sDEVICE=pdfwrite", "-sOwnerPassword=owner", "-dEncryptionR=3", "-dKeyLength=128",
                       "-dPermissions=-3904", f"-sOutputFile={gs / (stem + '-owner-only.pdf')}", str(p)])

mu = out / "mupdf"; mu.mkdir(exist_ok=True)
for p in base:
    stem = p.parent.name + "-" + p.stem
    run("mutool-clean", ["mutool", "clean", "-gggg", "-z", str(p), str(mu / (stem + "-clean.pdf"))])
    run("mutool-ascii", ["mutool", "clean", "-d", "-a", str(p), str(mu / (stem + "-decompressed-ascii.pdf"))])
    run("mutool-aes256", ["mutool", "clean", "-E", "aes-256", "-O", "owner", str(p), str(mu / (stem + "-aes256-owner-only.pdf"))])
    run("mutool-aes128-user", ["mutool", "clean", "-E", "aes-128", "-U", "user", "-O", "owner", str(p), str(mu / (stem + "-aes128-user.pdf"))])
    run("mutool-rc4", ["mutool", "clean", "-E", "rc4-128", "-O", "owner", str(p), str(mu / (stem + "-rc4-owner-only.pdf"))])

cairo = out / "cairo"; cairo.mkdir(exist_ok=True)
for p in base:
    run("pdftocairo", ["pdftocairo", "-pdf", str(p), str(cairo / (p.parent.name + "-" + p.stem + ".pdf"))])
run("inkscape", ["inkscape", str(src / "drawing.svg"), "--export-type=pdf", f"--export-filename={cairo / 'inkscape.pdf'}"])

poppler = out / "poppler"; poppler.mkdir(exist_ok=True)
if len(base) >= 2:
    run("pdfunite", ["pdfunite", *map(str, base), str(poppler / "united.pdf")])
if base:
    run("pdfseparate", ["pdfseparate", "-f", "1", "-l", "2", str(base[0]), str(poppler / "page-%d.pdf")])

shutil.rmtree(src / "lo-profile", ignore_errors=True)
(out / "producer-log.txt").write_text("\n".join(log) + "\n", encoding="utf-8")
print("\n".join(log))
print(sum(1 for _ in out.rglob("*.pdf") if "_sources" not in str(_)), "PDFs")
