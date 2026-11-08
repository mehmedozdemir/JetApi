"""Renders the JetApi application icon (build/icon.svg) to the formats the packages need.

Usage: python build/make-icons.py   (needs Pillow)
Writes src/JetApi.Desktop/Assets/jetapi.ico (Windows), jetapi.icns (macOS) and jetapi.png (Linux, window icon).
Keep the drawing below in sync with icon.svg.
"""
from pathlib import Path

from PIL import Image, ImageDraw

ACCENT = (0x34, 0x51, 0xD1, 255)
WHITE = (255, 255, 255, 255)
# Icon.Logo: M4 13l6-2 9-7-3 10-6 2-2 6-2-6z on a 24x24 grid.
LOGO = [(4, 13), (10, 11), (19, 4), (16, 14), (10, 16), (8, 22), (6, 16), (4, 13)]
SUPERSAMPLE = 4
OUT = Path(__file__).resolve().parent.parent / "src" / "JetApi.Desktop" / "Assets"


def render(size: int) -> Image.Image:
    big = size * SUPERSAMPLE
    unit = big / 1024
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle([64 * unit, 64 * unit, 960 * unit, 960 * unit], radius=200 * unit, fill=ACCENT)

    # The glyph's stroked bounds (3..20, 3..23) are centred on the 1024 canvas.
    scale = 30 * unit
    points = [((167 + x * 30) * unit, (122 + y * 30) * unit) for x, y in LOGO]
    width = round(2 * scale)
    draw.line(points, fill=WHITE, width=width, joint="curve")
    radius = width / 2
    for x, y in points:  # round joins and caps
        draw.ellipse([x - radius, y - radius, x + radius, y + radius], fill=WHITE)
    return image.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    master = render(1024)
    render(256).save(OUT / "jetapi.png")
    master.save(OUT / "jetapi.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    master.save(OUT / "jetapi.icns")
    print(f"Icons written to {OUT}")


if __name__ == "__main__":
    main()
