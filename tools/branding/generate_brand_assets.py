#!/usr/bin/env python3
"""
Generate the MPNZ-branded web/PWA assets from the supplied brand logo SVGs.

Inputs  (committed under tools/branding/source/):
    Logo 5-03.svg  -- MPNZ monogram + flourish (no tagline)
    Logo 5-02.svg  -- full lockup: monogram + flourish + rule + "Milking & Pumping since 1932"

Outputs (under src/Autorep.Web/wwwroot/):
    img/logo-mpnz.svg          full-colour monogram  (#003893 + #AFBCDB)  -- light backgrounds (login card)
    img/logo-mpnz-white.svg    reversed monogram     (#FFFFFF + #AFBCDB)  -- dark backgrounds (app header)
    icons/icon.svg             PWA icon: white monogram on #003893 rounded square
    icons/icon-maskable.svg    PWA maskable icon: white monogram in the central safe zone
    icons/apple-touch-icon.png 180x180 home-screen icon for iOS (square; iOS masks corners)
    icons/icon-<size>.png      PWA raster icons -- what Chrome turns into the Windows shortcut,
                               taskbar and alt-tab icons (it cannot use the SVG for those).
                               32px and below carry the M initial instead of the full wordmark
    icons/icon-maskable-<size>.png  maskable raster icons (Android adaptive icons)
    favicon.ico                multi-size icon for browser tabs and the Windows shell
    icons/splash/apple-splash-*.png   iOS PWA launch screens: reversed tagline lockup on #003893

It also prints the <link rel="apple-touch-startup-image"> tags to paste into _BrandHead.cshtml.

Brand palette (MPNZ Brand Guidelines, Hustle HQ May 2026):
    Dark Blue #003893  |  Light Blue #AFBCDB

Requires PyMuPDF (`fitz`) for SVG->PNG rasterisation and Pillow for packing favicon.ico.
Re-run after changing the source SVGs to regenerate every derived asset.
"""
import io
import os
import re
import shutil

import fitz  # PyMuPDF
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE_DIR = os.path.join(HERE, "source")
WEB = os.path.abspath(os.path.join(HERE, "..", "..", "src", "Autorep.Web"))
WWW = os.path.join(WEB, "wwwroot")
IMG_DIR = os.path.join(WWW, "img")
ICON_DIR = os.path.join(WWW, "icons")
SPLASH_DIR = os.path.join(ICON_DIR, "splash")

# Fallback location of the originals (the designer's hand-off folder) used only to
# bootstrap tools/branding/source/ the first time this script runs.
ORIGINALS = r"C:\Users\JoshPedersen\OneDrive - Pedersen Group\Clients\NZMPTA"

DARK = "#003893"
LIGHT = "#AFBCDB"
WHITE = "#FFFFFF"

MONOGRAM = "Logo 5-03.svg"
LOCKUP = "Logo 5-02.svg"

# Raster sizes listed in manifest.webmanifest. Chrome derives the Windows shortcut, taskbar and
# alt-tab icons from these -- it will not rasterise an SVG manifest entry for the Windows shell,
# so a manifest carrying only SVGs leaves the installed app showing Chrome's own icon. 192 and 512
# are the pair Chrome requires for installability; the smaller sizes are the ones the shell asks
# for, supplied directly so Windows never has to downscale 512 into a 32px taskbar button.
ICON_PNG_SIZES = (32, 48, 64, 96, 128, 192, 256, 512)
MASKABLE_PNG_SIZES = (192, 512)

# Packed into favicon.ico: 16/32 for browser tabs, 24 for the app window title bar, 48-256 for
# Explorer and the taskbar at higher DPI scaling.
FAVICON_SIZES = (16, 24, 32, 48, 64, 128, 256)

# Path order in Logo 5-03.svg: three flourish strokes, then the M, P, N and Z letterforms.
# write_icons() checks the count before relying on this index.
INITIAL_PATH = 3

# At and below this size the four-letter wordmark collapses into an illegible smear, so those
# frames carry the M initial instead. 32 is the right crossover because Windows draws the taskbar
# button at 24px at 100% scaling and 32px at 150% -- both land on the initial, so the taskbar reads
# the same at either DPI, while the 48px and larger frames the desktop and Explorer use keep the
# full wordmark.
SMALL_MARK_MAX = 32


def ensure_sources():
    os.makedirs(SOURCE_DIR, exist_ok=True)
    for name in (MONOGRAM, LOCKUP):
        dst = os.path.join(SOURCE_DIR, name)
        if not os.path.exists(dst):
            src = os.path.join(ORIGINALS, name)
            if not os.path.exists(src):
                raise SystemExit(
                    f"Missing source SVG '{name}'. Place it in {SOURCE_DIR} "
                    f"(or make {ORIGINALS} available) and re-run."
                )
            shutil.copyfile(src, dst)
            print(f"  bootstrapped source: {dst}")


def parse_svg(name):
    """Return (viewBox, [(cls, d), ...]) for a source logo SVG."""
    text = open(os.path.join(SOURCE_DIR, name), encoding="utf-8").read()
    vb = re.search(r'viewBox="([^"]+)"', text).group(1)
    paths = re.findall(r'<path class="(cls-[12])" d="([^"]+)"', text)
    if not paths:
        raise SystemExit(f"No class-tagged paths found in {name}")
    return vb, paths


def render_paths(paths, c1, c2):
    """Emit <path> elements with inline fills (cls-1 -> c1, cls-2 -> c2)."""
    out = []
    for cls, d in paths:
        out.append(f'<path fill="{c1 if cls == "cls-1" else c2}" d="{d}"/>')
    return "".join(out)


def write_logo_svgs(vb, paths):
    os.makedirs(IMG_DIR, exist_ok=True)
    variants = {
        "logo-mpnz.svg": (DARK, LIGHT),     # full colour, light backgrounds
        "logo-mpnz-white.svg": (WHITE, LIGHT),  # reversed, dark backgrounds
    }
    for fname, (c1, c2) in variants.items():
        svg = (
            f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" '
            f'role="img" aria-label="MPNZ">'
            f"{render_paths(paths, c1, c2)}</svg>\n"
        )
        with open(os.path.join(IMG_DIR, fname), "w", encoding="utf-8") as f:
            f.write(svg)
        print(f"  wrote {os.path.relpath(os.path.join(IMG_DIR, fname), WEB)}")


def write_lockup_svg(vb, paths):
    """Full-colour lockup (monogram + flourish + rule + tagline) for the login card."""
    os.makedirs(IMG_DIR, exist_ok=True)
    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" '
        f'role="img" aria-label="MPNZ — Milking &amp; Pumping since 1932">'
        f"{render_paths(paths, DARK, LIGHT)}</svg>\n"
    )
    with open(os.path.join(IMG_DIR, "logo-mpnz-lockup.svg"), "w", encoding="utf-8") as f:
        f.write(svg)
    print("  wrote wwwroot/img/logo-mpnz-lockup.svg")


def _vb_dims(vb):
    _, _, w, h = (float(x) for x in vb.replace(",", " ").split())
    return w, h


def icon_group(vb, paths, size, frac, c1=WHITE, c2=LIGHT):
    """White monogram scaled to `frac` of `size`, centred, as an SVG <g>."""
    w, h = _vb_dims(vb)
    target_w = size * frac
    scale = target_w / w
    tx = (size - target_w) / 2
    ty = (size - h * scale) / 2
    return (
        f'<g transform="translate({tx:.2f},{ty:.2f}) scale({scale:.5f})">'
        f"{render_paths(paths, c1, c2)}</g>"
    )


def write_icons(vb, paths):
    os.makedirs(ICON_DIR, exist_ok=True)
    if len(paths) != 7:
        raise SystemExit(
            f"{MONOGRAM}: expected 7 paths (3 flourish + M, P, N, Z), found {len(paths)}. "
            "Re-check INITIAL_PATH before regenerating the small icon frames."
        )
    # The small frames drop to the M on its own -- see SMALL_MARK_MAX. 0.56 rather than the
    # wordmark's 0.82 because a single letterform is measured on its own ink, not the logo's
    # full-width box, so the same number would run it into the corners.
    initial = reduced_icon_svg(vb, [paths[INITIAL_PATH]], 0.56)

    # PWA icon -- rounded square, brand-blue field, white monogram at 82% width.
    icon = (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" rx="104" fill="{DARK}"/>'
        f"{icon_group(vb, paths, 512, 0.82)}</svg>\n"
    )
    with open(os.path.join(ICON_DIR, "icon.svg"), "w", encoding="utf-8") as f:
        f.write(icon)
    print("  wrote icons/icon.svg")

    # ...and the same artwork as bitmaps, which is what the Windows shell actually consumes.
    # Chrome builds an installed PWA's taskbar, title-bar and alt-tab icons from the manifest's
    # PNGs; given SVG-only entries it has no bitmap to use and falls back to the Chrome icon.
    # Rendered with alpha so the rounded corners stay transparent rather than boxing the icon in.
    for size in ICON_PNG_SIZES:
        art = initial if size <= SMALL_MARK_MAX else icon
        rasterise(art, 512, 512, os.path.join(ICON_DIR, f"icon-{size}.png"),
                  out_px=(size, size), alpha=True)
    print(f"  wrote icons/icon-<size>.png ({', '.join(str(s) for s in ICON_PNG_SIZES)}"
          f"; {SMALL_MARK_MAX}px and below carry the M initial)")

    # Maskable -- full-bleed field, monogram inside the central 60% safe zone.
    maskable = (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" fill="{DARK}"/>'
        f"{icon_group(vb, paths, 512, 0.60)}</svg>\n"
    )
    with open(os.path.join(ICON_DIR, "icon-maskable.svg"), "w", encoding="utf-8") as f:
        f.write(maskable)
    print("  wrote icons/icon-maskable.svg")

    # Full-bleed, so no alpha to preserve -- Android crops it to the launcher's own shape.
    for size in MASKABLE_PNG_SIZES:
        rasterise(maskable, 512, 512, os.path.join(ICON_DIR, f"icon-maskable-{size}.png"),
                  out_px=(size, size))
    print(f"  wrote icons/icon-maskable-<size>.png ({', '.join(str(s) for s in MASKABLE_PNG_SIZES)})")

    # apple-touch-icon -- square PNG (no rounding; iOS applies its own mask).
    square = (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" fill="{DARK}"/>'
        f"{icon_group(vb, paths, 512, 0.78)}</svg>"
    )
    rasterise(square, 512, 512, os.path.join(ICON_DIR, "apple-touch-icon.png"), out_px=(180, 180))
    print("  wrote icons/apple-touch-icon.png")

    # favicon.ico -- browser tabs and the Windows shell both index into this single file, so pack
    # every size either asks for. Each frame is rendered from the vector rather than downsampled
    # from one large raster, which is what keeps the 16 and 24px frames legible.
    order = sorted(FAVICON_SIZES, reverse=True)
    frames = [pil_frame(initial if size <= SMALL_MARK_MAX else icon, size) for size in order]
    frames[0].save(
        os.path.join(WWW, "favicon.ico"),
        format="ICO",
        sizes=[(size, size) for size in order],
        append_images=frames[1:],
    )
    print(f"  wrote favicon.ico ({', '.join(str(s) for s in FAVICON_SIZES)})")


def pixmap(svg, vb_w, vb_h, out_px=None, alpha=False):
    """SVG string -> fitz pixmap (svg -> pdf -> pixmap) at out_px, defaulting to the viewBox."""
    doc = fitz.open(stream=svg.encode("utf-8"), filetype="svg")
    pdf = fitz.open("pdf", doc.convert_to_pdf())
    page = pdf[0]
    out_w, out_h = out_px if out_px else (vb_w, vb_h)
    mat = fitz.Matrix(out_w / vb_w, out_h / vb_h)
    return page.get_pixmap(matrix=mat, alpha=alpha)


def rasterise(svg, vb_w, vb_h, dst, out_px=None, alpha=False):
    """SVG string -> PNG on disk. alpha=True keeps the rounded corners transparent, which the
    app icon needs: an opaque corner reads as a white or black box on the Windows taskbar."""
    pixmap(svg, vb_w, vb_h, out_px, alpha).save(dst)


def pil_open(pix):
    """fitz pixmap -> Pillow RGBA image."""
    return Image.open(io.BytesIO(pix.tobytes("png"))).convert("RGBA")


def pil_frame(svg, size):
    """512-viewBox SVG string -> Pillow RGBA image, rendered from the vector at `size`."""
    return pil_open(pixmap(svg, 512, 512, (size, size), alpha=True))


def subset_bbox(vb, subset):
    """Tight bounding box of `subset`, in viewBox units.

    Measured off a high-resolution probe render rather than parsed out of the path data: the `d`
    attributes are full of curves whose control points sit outside the ink, so reading their
    numbers would centre the mark wrong.
    """
    probe_w = 1024
    vb_w, vb_h = _vb_dims(vb)
    probe_h = probe_w * vb_h / vb_w
    svg = (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}">'
        f"{render_paths(subset, WHITE, LIGHT)}</svg>"
    )
    bbox = pil_open(pixmap(svg, vb_w, vb_h, (probe_w, probe_h), alpha=True)).getbbox()
    if bbox is None:
        raise SystemExit("subset_bbox: the requested paths rendered blank.")
    x0, y0, x1, y1 = bbox
    sx, sy = vb_w / probe_w, vb_h / probe_h
    return x0 * sx, y0 * sy, x1 * sx, y1 * sy


def reduced_icon_svg(vb, subset, frac):
    """A 512 icon tile carrying only `subset`, centred on its own ink at `frac` of the tile."""
    x0, y0, x1, y1 = subset_bbox(vb, subset)
    mark_w, mark_h = x1 - x0, y1 - y0
    scale = 512 * frac / max(mark_w, mark_h)
    tx = (512 - mark_w * scale) / 2 - x0 * scale
    ty = (512 - mark_h * scale) / 2 - y0 * scale
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" rx="104" fill="{DARK}"/>'
        f'<g transform="translate({tx:.3f},{ty:.3f}) scale({scale:.5f})">'
        f"{render_paths(subset, WHITE, LIGHT)}</g></svg>"
    )


# (px_w, px_h, pt_w, pt_h, dpr) -- portrait orientation; modern iPhone + iPad classes.
DEVICES = [
    (750, 1334, 375, 667, 2),    # iPhone SE / 6-8
    (1170, 2532, 390, 844, 3),   # iPhone 12-14 / 12-13 Pro
    (1179, 2556, 393, 852, 3),   # iPhone 14 Pro / 15 / 15 Pro / 16
    (1290, 2796, 430, 932, 3),   # iPhone 14 Pro Max / 15 Plus / 15 Pro Max
    (1536, 2048, 768, 1024, 2),  # iPad mini / 9.7"
    (1668, 2388, 834, 1194, 2),  # iPad Pro 11" / Air
    (2048, 2732, 1024, 1366, 2), # iPad Pro 12.9"
]


def splash_svg(w, h, vb, paths):
    """Reversed tagline lockup centred on a #003893 field sized w x h px."""
    vw, vh = _vb_dims(vb)
    base = min(w, h)
    logo_w = base * 0.62
    scale = logo_w / vw
    tx = (w - logo_w) / 2
    ty = (h - vh * scale) / 2
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}">'
        f'<rect width="{w}" height="{h}" fill="{DARK}"/>'
        f'<g transform="translate({tx:.2f},{ty:.2f}) scale({scale:.5f})">'
        f"{render_paths(paths, WHITE, LIGHT)}</g></svg>"
    )


def write_splash(vb, paths):
    os.makedirs(SPLASH_DIR, exist_ok=True)
    links = []
    for px_w, px_h, pt_w, pt_h, dpr in DEVICES:
        for orient in ("portrait", "landscape"):
            w, h = (px_w, px_h) if orient == "portrait" else (px_h, px_w)
            fname = f"apple-splash-{w}-{h}.png"
            rasterise(splash_svg(w, h, vb, paths), w, h, os.path.join(SPLASH_DIR, fname))
            media = (
                f"screen and (device-width: {pt_w}px) and (device-height: {pt_h}px) "
                f"and (-webkit-device-pixel-ratio: {dpr}) and (orientation: {orient})"
            )
            links.append(
                f'<link rel="apple-touch-startup-image" '
                f'media="{media}" href="~/icons/splash/{fname}" />'
            )
    print(f"  wrote {len(DEVICES) * 2} splash PNGs to icons/splash/")
    out = os.path.join(HERE, "apple-startup-links.html")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(links) + "\n")
    print(f"  wrote startup-image <link> tags to {os.path.relpath(out, HERE)}")
    return links


def main():
    ensure_sources()
    mvb, mpaths = parse_svg(MONOGRAM)
    lvb, lpaths = parse_svg(LOCKUP)
    print("Generating brand assets...")
    write_logo_svgs(mvb, mpaths)
    write_lockup_svg(lvb, lpaths)
    write_icons(mvb, mpaths)
    write_splash(lvb, lpaths)
    print("Done.")


if __name__ == "__main__":
    main()
