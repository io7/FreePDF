"""Create original test fonts for the system font lookup. Requires fonttools==4.60.1. MIT.

Each font has one geometric glyph for ASCII and differs only in its names and style and licence bits:
- FreePDF Probe Regular and Bold (Bold sits in a subfolder, to check the recursive search);
- a stand-in named Liberation Sans, with its own full name, to check the substitutes for Helvetica and Arial;
- FreePDF Restricted, whose OS/2 fsType forbids embedding;
- FreePDF Outline CFF, an .otf with CFF outlines, which FreePDF cannot embed.
"""
from pathlib import Path
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.pens.ttGlyphPen import TTGlyphPen

HERE = Path(__file__).parent
GLYPHS = [".notdef", "space", "box"]


def draw(pen):
    pen.moveTo((50, 0)); pen.lineTo((550, 0)); pen.lineTo((550, 700)); pen.lineTo((50, 700)); pen.closePath()


def build(path, family, style, ps_name, weight, fs_selection, mac_style, fs_type=0, cff=False, full_name=None):
    font = FontBuilder(1000, isTTF=not cff)
    font.setupGlyphOrder(GLYPHS)
    font.setupCharacterMap({32: "space", **{i: "box" for i in range(33, 127)}})
    if cff:
        charstrings = {}
        for name in GLYPHS:
            pen = T2CharStringPen(600, None)
            if name != "space":
                draw(pen)
            charstrings[name] = pen.getCharString()
        font.setupCFF(ps_name, {"FullName": f"{family} {style}"}, charstrings, {})
    else:
        glyphs = {}
        for name in GLYPHS:
            pen = TTGlyphPen(None)
            if name != "space":
                draw(pen)
            glyphs[name] = pen.glyph()
        font.setupGlyf(glyphs)
    font.setupHorizontalMetrics({name: (600, 50) for name in GLYPHS})
    font.setupHorizontalHeader(ascent=800, descent=-200)
    font.setupNameTable({"familyName": family, "styleName": style, "uniqueFontIdentifier": f"{ps_name}-1",
                         "fullName": full_name or f"{family} {style}", "psName": ps_name, "version": "Version 1.0"})
    font.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200,
                  usWeightClass=weight, fsSelection=fs_selection, fsType=fs_type)
    font.setupPost()
    font.font["head"].macStyle = mac_style
    font.font["head"].created = font.font["head"].modified = 3800000000
    font.font.recalcTimestamp = False
    path.parent.mkdir(parents=True, exist_ok=True)
    font.save(path)


REGULAR, BOLD = 0x40, 0x20
build(HERE / "probe-regular.ttf", "FreePDF Probe", "Regular", "FreePDFProbe-Regular", 400, REGULAR, 0)
build(HERE / "nested" / "probe-bold.ttf", "FreePDF Probe", "Bold", "FreePDFProbe-Bold", 700, BOLD, 1)
build(HERE / "liberation-sans-stand-in.ttf", "Liberation Sans", "Regular", "FreePDFStandIn-LiberationSans", 400, REGULAR, 0,
      full_name="FreePDF Stand-in for Liberation Sans")
build(HERE / "restricted.ttf", "FreePDF Restricted", "Regular", "FreePDFRestricted-Regular", 400, REGULAR, 0, fs_type=0x0002)
build(HERE / "nested" / "outline-cff.otf", "FreePDF Outline CFF", "Regular", "FreePDFOutlineCFF-Regular", 400, REGULAR, 0, cff=True)
