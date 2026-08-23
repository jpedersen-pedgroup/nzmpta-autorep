# Branding assets

`generate_brand_assets.py` derives every web/PWA brand asset from the two supplied
MPNZ logo SVGs so they can be regenerated consistently.

## Sources (`source/`)
- `Logo 5-03.svg` — MPNZ monogram + flourish (no tagline) → logos + icons
- `Logo 5-02.svg` — full lockup incl. "Milking & Pumping since 1932" → iOS launch screens

These are copies of the designer hand-off (MPNZ Brand Guidelines, Hustle HQ, May 2026).
Brand palette: Dark Blue `#003893`, Light Blue `#AFBCDB`.

## Generated outputs (`src/Autorep.Web/wwwroot/`)
| Asset | Use |
|---|---|
| `img/logo-mpnz.svg` | full-colour monogram — login card (light bg) |
| `img/logo-mpnz-white.svg` | reversed monogram — app header (dark bg) |
| `icons/icon.svg`, `icons/icon-maskable.svg` | PWA icon artwork (browser tab, Android) |
| `icons/icon-<size>.png` | PWA raster icons, 32–512 — Chrome turns these into the Windows shortcut, taskbar and alt-tab icons |
| | ↳ frames at `SMALL_MARK_MAX` (32px) and below carry the **M initial**; 48px and up carry the full wordmark |
| `icons/icon-maskable-<size>.png` | maskable rasters, 192 + 512 (Android adaptive icons) |
| `favicon.ico` | 16–256 multi-size icon for browser tabs and the Windows shell |
| `icons/apple-touch-icon.png` | iOS home-screen icon (180×180) |
| `icons/splash/apple-splash-*.png` | iOS PWA launch screens (tagline lockup on `#003893`) |

The `<link rel="apple-touch-startup-image">` tags are emitted to `apple-startup-links.html`
and live in `Pages/Shared/_BrandHead.cshtml`. Add a device row to `DEVICES` and re-run to
extend coverage.

## Run
```
python tools/branding/generate_brand_assets.py
```
Requires Python with PyMuPDF and Pillow (`pip install pymupdf pillow`).

Chrome will not rasterise an SVG manifest entry for the Windows shell, so the PNG sizes
listed in `manifest.webmanifest` are what stop an installed AutoRep showing Chrome's own
icon on the taskbar. Keep the manifest and `ICON_PNG_SIZES` in step.

The four-letter wordmark turns to mush below ~48px, so every frame at `SMALL_MARK_MAX`
(32px) and below drops to the M initial on the same blue tile. The crossover sits at 32
deliberately: Windows draws the taskbar button at 24px at 100% scaling and 32px at 150%,
so both land on the initial and the taskbar looks the same at either DPI.
