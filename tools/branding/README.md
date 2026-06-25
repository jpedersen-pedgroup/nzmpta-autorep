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
| `icons/icon.svg`, `icons/icon-maskable.svg` | PWA / favicon |
| `icons/apple-touch-icon.png` | iOS home-screen icon (180×180) |
| `icons/splash/apple-splash-*.png` | iOS PWA launch screens (tagline lockup on `#003893`) |

The `<link rel="apple-touch-startup-image">` tags are emitted to `apple-startup-links.html`
and live in `Pages/Shared/_BrandHead.cshtml`. Add a device row to `DEVICES` and re-run to
extend coverage.

## Run
```
python tools/branding/generate_brand_assets.py
```
Requires Python with PyMuPDF (`pip install pymupdf`).
