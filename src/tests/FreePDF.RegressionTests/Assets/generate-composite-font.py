"""Create an original nested-composite fixture using fonttools. MIT."""
from pathlib import Path
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

names = [".notdef", "space", "left", "right", "pair", "nested", "accent"]
glyphs = {}
for name in names:
    pen = TTGlyphPen(glyphs)
    if name == "pair":
        pen.addComponent("left", (1, 0, 0, 1, 0, 0))
        pen.addComponent("right", (1, 0, 0, 1, 300, 0))
    elif name == "nested":
        pen.addComponent("pair", (0.5, 0, 0, 0.5, 0, 0))
        # Defined below, then this glyph is rebuilt after all simple outlines exist.
    elif name != "space":
        pen.moveTo((30, 0)); pen.lineTo((180, 0)); pen.lineTo((180, 500)); pen.lineTo((30, 500)); pen.closePath()
    glyphs[name] = pen.glyph()
pen = TTGlyphPen(glyphs)
pen.addComponent("pair", (0.5, 0, 0, 0.5, 0, 0))
pen.addComponent("accent", (1, 0, 0, 1, 400, 200))
glyphs["nested"] = pen.glyph()
font = FontBuilder(1000, isTTF=True)
font.setupGlyphOrder(names)
font.setupCharacterMap({32:"space", 0xe000:"nested"})
font.setupGlyf(glyphs)
font.setupHorizontalMetrics({name:(700,30) for name in names})
font.setupHorizontalHeader(ascent=800, descent=-200)
font.setupNameTable({"familyName":"FreePDF Composite", "styleName":"Regular", "uniqueFontIdentifier":"FreePDFComposite-1", "fullName":"FreePDF Composite Regular", "psName":"FreePDFComposite-Regular", "version":"Version 1.0"})
font.setupOS2(sTypoAscender=800,sTypoDescender=-200,usWinAscent=800,usWinDescent=200)
font.setupPost()
font.font["head"].created = font.font["head"].modified = 3800000000
font.font.recalcTimestamp = False
font.save(Path(__file__).with_name("composite.ttf"))
