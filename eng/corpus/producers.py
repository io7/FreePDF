"""Generate original scenarios through independent, test-only PDF producers."""
import base64
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FONT = ROOT / "lab/Assets/DejaVuSans.ttf"
IMAGE = ROOT / "lab/Assets/colors.png"


def reportlab(cases, output):
    from reportlab.pdfgen.canvas import Canvas
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.lib.colors import HexColor
    pdfmetrics.registerFont(TTFont("Corpus", str(FONT)))
    output.mkdir(parents=True, exist_ok=True)
    for c in cases:
        path = output / (c["id"] + ".pdf")
        with path.open("xb") as target:
            pdf = Canvas(target, pagesize=(c["width"], c["height"]), pageCompression=int(c["compress"]), invariant=1)
            pdf.setTitle(c["id"])
            for lines in c["lines"]:
                m, size, height = c["margin"], c["fontSize"], c["height"]
                pdf.setFont("Corpus", size)
                pdf.setFillColor(HexColor(c["color"]))
                for row, line in enumerate(lines):
                    x = m + (12 if row and c["layout"] == "list" else 0)
                    y = height - m - size - row * size * 1.8
                    pdf.saveState()
                    if c["style"] == "italic":
                        pdf.transform(1, 0, .16, 1, -y * .16, 0)
                    if c["style"] == "bold":
                        pdf.setStrokeColor(HexColor(c["color"]))
                        pdf.setLineWidth(.22)
                        text = pdf.beginText(x, y)
                        text.setTextRenderMode(2)
                        text.textOut(line)
                        pdf.drawText(text)
                    else:
                        pdf.drawString(x, y, line)
                    pdf.restoreState()
                    if row and c["layout"] == "table":
                        pdf.line(m, y - 3, c["width"] - m, y - 3)
                y = m + 6
                if c["illustration"] == "image":
                    pdf.drawImage(str(IMAGE), m, y, width=60, height=30, mask="auto")
                elif c["illustration"] == "ellipse":
                    pdf.ellipse(m, y, m + 60, y + 30, fill=1, stroke=0)
                else:
                    pdf.rect(m, y, 60, 30, fill=1, stroke=0)
                pdf.showPage()
            pdf.save()


def chromium(cases, output):
    from playwright.sync_api import sync_playwright
    output.mkdir(parents=True, exist_ok=True)
    font = base64.b64encode(FONT.read_bytes()).decode()
    image = base64.b64encode(IMAGE.read_bytes()).decode()
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page()
        page.route("**/*", lambda route: route.abort())
        page.set_default_timeout(30000)
        for c in cases:
            body = []
            for lines in c["lines"]:
                heading = f'<h1>{html.escape(lines[0])}</h1>'
                rows = [html.escape(line) for line in lines[1:]]
                if c["layout"] == "table":
                    content = '<table aria-label="Synthetic data"><tbody>' + ''.join(f'<tr><td>{line}</td></tr>' for line in rows) + '</tbody></table>'
                elif c["layout"] == "list":
                    content = '<ul>' + ''.join(f'<li>{line}</li>' for line in rows) + '</ul>'
                else:
                    content = ''.join(f'<p>{line}</p>' for line in rows)
                if c["illustration"] == "image":
                    figure = f'<img alt="Synthetic image" src="data:image/png;base64,{image}">'
                else:
                    radius = 'border-radius:50%;' if c["illustration"] == "ellipse" else ''
                    figure = f'<div role="img" aria-label="Synthetic shape" style="background:{c["color"]};{radius}"></div>'
                body.append(f'<section>{heading}{content}<figure>{figure}</figure></section>')
            style = 'font-weight:bold;' if c["style"] == "bold" else 'font-style:italic;' if c["style"] == "italic" else ''
            css = f'''@font-face{{font-family:Corpus;src:url(data:font/ttf;base64,{font})}}
@page{{size:{c['width']}pt {c['height']}pt;margin:0}}
*{{box-sizing:border-box}}html,body{{margin:0;padding:0}}body{{font: {c['fontSize']}pt Corpus;color:{c['color']};{style}}}
section{{position:relative;width:{c['width']}pt;height:{c['height']}pt;padding:{c['margin']}pt;break-after:page;overflow:hidden}}
section:last-child{{break-after:auto}}h1,p,ul,li,table,td{{margin:0;padding:0;font:inherit;line-height:1.8}}
ul{{padding-left:12pt;list-style:none}}table{{border-collapse:collapse;width:100%}}td{{border-bottom:.5pt solid #777}}
figure{{position:absolute;left:{c['margin']}pt;bottom:{c['margin'] + 6}pt;margin:0}}figure>*{{width:60pt;height:30pt}}'''
            page.set_content(f'<!doctype html><html lang="en"><meta charset="utf-8"><title>{c["id"]}</title><style>{css}</style><body>{"".join(body)}</body></html>', wait_until="load")
            page.evaluate("document.fonts.ready")
            data = page.pdf(prefer_css_page_size=True, print_background=True, tagged=True, outline=True)
            with (output / (c["id"] + ".pdf")).open("xb") as target:
                target.write(data)
        browser.close()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("producer", choices=["reportlab", "chromium"])
    parser.add_argument("manifest", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    cases = [json.loads(line) for line in args.manifest.read_text().splitlines()]
    globals()[args.producer](cases, args.output)
