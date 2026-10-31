"""Create an original geometric test font. Requires fonttools==4.60.1. MIT."""
from pathlib import Path
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

font = FontBuilder(1000, isTTF=True)
font.setupGlyphOrder([".notdef", "space", "box"])
font.setupCharacterMap({32: "space", **{i: "box" for i in range(33, 127)}})
glyphs = {}
for name in [".notdef", "space", "box"]:
    pen = TTGlyphPen(None)
    if name != "space":
        pen.moveTo((50, 0)); pen.lineTo((550, 0)); pen.lineTo((550, 700)); pen.lineTo((50, 700)); pen.closePath()
    glyphs[name] = pen.glyph()
font.setupGlyf(glyphs)
font.setupHorizontalMetrics({name: (600, 50) for name in glyphs})
font.setupHorizontalHeader(ascent=800, descent=-200)
font.setupNameTable({"familyName": "FreePDF Synthetic", "styleName": "Regular", "uniqueFontIdentifier": "FreePDFSynthetic-1", "fullName": "FreePDF Synthetic Regular", "psName": "FreePDFSynthetic-Regular", "version": "Version 1.0"})
font.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
font.setupPost()
font.font["head"].created = font.font["head"].modified = 3800000000
font.font.recalcTimestamp = False
font.save(Path(__file__).with_name("synthetic.ttf"))
