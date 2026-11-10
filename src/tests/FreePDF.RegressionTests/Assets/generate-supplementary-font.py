"""Create an original test font with glyphs beyond U+FFFF and many distinct glyphs. Requires fonttools==4.60.1. MIT.

Code points: A (U+0041), MATHEMATICAL BOLD CAPITAL A (U+1D400), GRINNING FACE (U+1F600), and 150 Latin Extended
letters U+0100..U+0195, each with its own glyph (a box whose width differs), so a ToUnicode map gets more than 100
entries.
"""
from pathlib import Path
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

extended = list(range(0x100, 0x100 + 150))
names = [".notdef", "space", "A", "mathA", "grin"] + [f"g{cp:04X}" for cp in extended]
cmap = {32: "space", 0x41: "A", 0x1D400: "mathA", 0x1F600: "grin", **{cp: f"g{cp:04X}" for cp in extended}}

font = FontBuilder(1000, isTTF=True)
font.setupGlyphOrder(names)
font.setupCharacterMap(cmap)
glyphs = {}
widths = {}
for index, name in enumerate(names):
    pen = TTGlyphPen(None)
    width = 400 + (index % 7) * 50
    if name != "space":
        pen.moveTo((50, 0)); pen.lineTo((width - 50, 0)); pen.lineTo((width - 50, 700)); pen.lineTo((50, 700)); pen.closePath()
    glyphs[name] = pen.glyph()
    widths[name] = (width, 50)
font.setupGlyf(glyphs)
font.setupHorizontalMetrics(widths)
font.setupHorizontalHeader(ascent=800, descent=-200)
font.setupNameTable({"familyName": "FreePDF Supplementary", "styleName": "Regular", "uniqueFontIdentifier": "FreePDFSupplementary-1",
                     "fullName": "FreePDF Supplementary Regular", "psName": "FreePDFSupplementary-Regular", "version": "Version 1.0"})
font.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
font.setupPost()
font.font["head"].created = font.font["head"].modified = 3800000000
font.font.recalcTimestamp = False
font.save(Path(__file__).with_name("supplementary.ttf"))
