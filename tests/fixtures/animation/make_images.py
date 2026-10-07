"""Makes the animated images of the slice 4b animation fixture.

Each image has eight frames of 250 ms, each a solid color with its frame
number, from 0 to 7, drawn in white, and repeats for ever. The same frames
are written as a GIF, an animated WebP, and an animated PNG, so a frame
index read from a recording names the same color in each.

Run from this directory with Pillow installed:

    python make_images.py

See docs/architecture/page-recreation.md, "Animation fixture".
"""

from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 96
FRAME_MILLISECONDS = 250

# Frame index, color name, and RGB, as the fixture page lists them.
FRAMES = (
    (0, "red", (204, 0, 0)),
    (1, "orange", (204, 102, 0)),
    (2, "olive", (128, 128, 0)),
    (3, "green", (0, 128, 0)),
    (4, "teal", (0, 128, 128)),
    (5, "blue", (0, 0, 204)),
    (6, "purple", (102, 0, 153)),
    (7, "black", (0, 0, 0)),
)

# Each digit as five rows of three cells, drawn as squares, so no font is
# needed and the frames are the same wherever the script runs.
DIGITS = {
    0: ("111", "101", "101", "101", "111"),
    1: ("010", "110", "010", "010", "111"),
    2: ("111", "001", "111", "100", "111"),
    3: ("111", "001", "111", "001", "111"),
    4: ("101", "101", "111", "001", "001"),
    5: ("111", "100", "111", "001", "111"),
    6: ("111", "100", "111", "101", "111"),
    7: ("111", "001", "001", "001", "001"),
}
CELL = 12


def frame_image(index: int, color: tuple[int, int, int]) -> Image.Image:
    image = Image.new("RGB", (SIZE, SIZE), color)
    draw = ImageDraw.Draw(image)
    left = (SIZE - 3 * CELL) // 2
    top = (SIZE - 5 * CELL) // 2
    for row, cells in enumerate(DIGITS[index]):
        for column, cell in enumerate(cells):
            if cell == "1":
                x = left + column * CELL
                y = top + row * CELL
                draw.rectangle((x, y, x + CELL - 1, y + CELL - 1), fill=(255, 255, 255))
    return image


def main() -> None:
    directory = Path(__file__).resolve().parent
    frames = [frame_image(index, color) for index, _, color in FRAMES]
    durations = [FRAME_MILLISECONDS] * len(frames)
    common = {"save_all": True, "append_images": frames[1:], "duration": durations, "loop": 0}
    frames[0].save(directory / "frames.gif", format="GIF", disposal=1, optimize=False, **common)
    frames[0].save(directory / "frames.webp", format="WEBP", lossless=True, **common)
    frames[0].save(directory / "frames.png", format="PNG", **common)


if __name__ == "__main__":
    main()
