"""Regenerate the original three-letter test fonts. Requires fontTools."""

from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

ROOT = Path(__file__).resolve().parent
PIXELS = {
    "A": ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "K": ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
}


def build(path, family, inset):
    order = [".notdef", "space", *PIXELS]
    glyphs = {}
    for name in order:
        pen = TTGlyphPen(None)
        for row, cells in enumerate(PIXELS.get(name, [])):
            for col, cell in enumerate(cells):
                if cell != "1":
                    continue
                left = 70 + 100 * col + inset
                bottom = 100 * (6 - row) + inset
                size = 100 - 2 * inset
                pen.moveTo((left, bottom))
                pen.lineTo((left, bottom + size))
                pen.lineTo((left + size, bottom + size))
                pen.lineTo((left + size, bottom))
                pen.closePath()
        glyphs[name] = pen.glyph()
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({32: "space", **{ord(char): char for char in PIXELS}})
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics(
        {name: (650, glyphs[name].xMin if glyphs[name].numberOfContours else 0) for name in order}
    )
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({
        "familyName": family,
        "styleName": "Regular",
        "uniqueFontIdentifier": family + " Regular",
        "fullName": family + " Regular",
        "psName": family.replace(" ", "") + "-Regular",
    })
    fb.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    fb.font["head"].created = fb.font["head"].modified = 3660681600
    fb.font.recalcTimestamp = False
    path.parent.mkdir(parents=True, exist_ok=True)
    fb.save(path)


if __name__ == "__main__":
    build(ROOT / "res/font/apk_block.ttf", "APK Fixture Block", 0)
    build(ROOT / "assets/fonts/apk_unused.ttf", "APK Fixture Dots", 15)
