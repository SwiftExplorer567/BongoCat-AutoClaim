"""Render the app icon and branding images from assets/icon.svg.

Usage: python tools/generate_assets.py
Writes assets/app.ico, assets/logo.png, assets/steam_guide_icon.png,
assets/banner.png (README header) and assets/social-preview.png (GitHub social preview).
Requires: pip install resvg-py pillow
"""
import io
import os

import resvg_py
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
SVG = os.path.join(ASSETS, "icon.svg")


def render(size):
    png = resvg_py.svg_to_bytes(svg_path=SVG, width=size, height=size)
    return Image.open(io.BytesIO(bytes(png))).convert("RGBA")


def main():
    big = render(1024)

    big.resize((512, 512), Image.LANCZOS).save(os.path.join(ASSETS, "logo.png"))

    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    frames = [render(n * 4).resize((n, n), Image.LANCZOS) for n in sizes]
    frames[-1].save(os.path.join(ASSETS, "app.ico"), sizes=[(n, n) for n in sizes],
                    append_images=frames[:-1])

    # Steam guide branding image: square, the icon on a matching night backdrop
    steam = Image.new("RGBA", (512, 512), (15, 21, 48, 255))
    steam.alpha_composite(big.resize((448, 448), Image.LANCZOS), (32, 32))
    steam.convert("RGB").save(os.path.join(ASSETS, "steam_guide_icon.png"))

    # README banner and GitHub social preview (both embed logo.png, rendered above)
    for name, width in (("banner", 2560), ("social-preview", 1280)):
        png = resvg_py.svg_to_bytes(svg_path=os.path.join(ASSETS, name + ".svg"), width=width,
                                    resources_dir=ASSETS)
        Image.open(io.BytesIO(bytes(png))).save(os.path.join(ASSETS, name + ".png"))


if __name__ == "__main__":
    main()
