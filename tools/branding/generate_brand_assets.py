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
    icons/splash/apple-splash-*.png   iOS PWA launch screens: reversed tagline lockup on #003893

It also prints the <link rel="apple-touch-startup-image"> tags to paste into _BrandHead.cshtml.

Brand palette (MPNZ Brand Guidelines, Hustle HQ May 2026):
    Dark Blue #003893  |  Light Blue #AFBCDB

Requires PyMuPDF (`fitz`) for SVG->PNG rasterisation. Re-run after changing the
source SVGs to regenerate every derived asset.
"""
import os
import re
import shutil

import fitz  # PyMuPDF

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
    # PWA icon -- rounded square, brand-blue field, white monogram at 82% width.
    icon = (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" rx="104" fill="{DARK}"/>'
        f"{icon_group(vb, paths, 512, 0.82)}</svg>\n"
    )
    with open(os.path.join(ICON_DIR, "icon.svg"), "w", encoding="utf-8") as f:
        f.write(icon)
    print("  wrote icons/icon.svg")

    # Maskable -- full-bleed field, monogram inside the central 60% safe zone.
    maskable = (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" fill="{DARK}"/>'
        f"{icon_group(vb, paths, 512, 0.60)}</svg>\n"
    )
    with open(os.path.join(ICON_DIR, "icon-maskable.svg"), "w", encoding="utf-8") as f:
        f.write(maskable)
    print("  wrote icons/icon-maskable.svg")

    # apple-touch-icon -- square PNG (no rounding; iOS applies its own mask).
    square = (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        f'<rect width="512" height="512" fill="{DARK}"/>'
        f"{icon_group(vb, paths, 512, 0.78)}</svg>"
    )
    rasterise(square, 512, 512, os.path.join(ICON_DIR, "apple-touch-icon.png"), out_px=(180, 180))
    print("  wrote icons/apple-touch-icon.png")


def rasterise(svg, vb_w, vb_h, dst, out_px=None):
    """SVG string -> PNG via fitz (svg -> pdf -> pixmap)."""
    doc = fitz.open(stream=svg.encode("utf-8"), filetype="svg")
    pdf = fitz.open("pdf", doc.convert_to_pdf())
    page = pdf[0]
    out_w, out_h = out_px if out_px else (vb_w, vb_h)
    mat = fitz.Matrix(out_w / vb_w, out_h / vb_h)
    pix = page.get_pixmap(matrix=mat, alpha=False)
    pix.save(dst)


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
