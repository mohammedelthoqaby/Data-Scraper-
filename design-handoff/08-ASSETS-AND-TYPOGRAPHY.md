# Assets, Branding & Typography Specification — Lazy Bean Market Intelligence

This document details the brand marks, vector assets, icon geometry, font fallbacks, Cairo timezone formatting, and Egyptian Pound (EGP) currency rules extracted from the codebase.

---

## 1. Brand Identity Assets & SVG Geometry

### 1.1 The Lazy Bean Brand Tile
The Lazy Bean brand tile appears in the desktop sidebar (`<aside>`), mobile navigation header, and the unlock dialog (`#login`):

- **Tile Container (`.mark`)**:
  - CSS: `display: grid; place-items: center; width: 42px; height: 42px; background: #202723; color: white; border-radius: 11px; flex-shrink: 0; box-shadow: 0 2px 6px rgba(0, 0, 0, 0.12);`
  - Background Color: `#202723` (dark charcoal-green).
  - Border Radius: `11px`.
  - Box Shadow: `0 2px 6px rgba(0, 0, 0, 0.12)`.
- **Inner Image**:
  - Image element: `<img src="/assets/logo.png?v=3" alt="Lazy Bean logo" width="26" height="26">`
  - CSS: `width: 26px; height: 26px; object-fit: contain; display: block;` (or `28px × 28px` in `.mark img`).

---

### 1.2 Official Vector Asset (`LOGO.svg`)
Extracted from the repository root (`LOGO.svg`):

```xml
<svg id="Layer_1" data-name="Layer 1" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 792 612">
  <defs>
    <style>.cls-1{fill:#fdec0a;}</style>
  </defs>
  <!-- Coffee bean emblem path -->
  <path class="cls-1" d="M333,485.79A115.48,115.48,0,0,1,277.38,472c-33.76-18.4-56.55-53.67-62.54-96.76-5.52-39.68,3.17-83,24.46-122.12s53-69.88,89.37-86.75c39.46-18.33,81.46-18.29,115.21.11s56.55,53.66,62.53,96.76c5.52,39.68-3.17,83.05-24.46,122.12h0c-21.29,39.07-53,69.87-89.37,86.75C372.89,481.22,352.57,485.79,333,485.79Zm55-260.77c-29.11,0-64,24.52-84.87,62.81-13.71,25.14-19.65,53.35-16.31,77.39,1.64,11.79,7.12,33,25.37,43,31.21,17,79.8-9.38,106.09-57.63s22.15-103.39-9.06-120.4A43.92,43.92,0,0,0,387.92,225Z"/>
  <!-- Vertical machine/stem bar -->
  <rect class="cls-1" x="506.18" y="157.28" width="72.6" height="323.86"/>
</svg>
```

- **ViewBox**: `0 0 792 612`
- **Icon Primary Fill**: `#fdec0a` (Vivid Yellow).
- **Icon Elements**:
  1. A stylized organic coffee bean contour with an inner negative cut.
  2. A vertical bar representing precision machinery and packaging.

---

### 1.3 Favicon (`app/static/favicon.png`)
- **Dimensions**: `32 × 32` PNG (contains the `#202723` tile with `#fdec0a` logo mark).
- **HTML Link**: `<link rel="icon" type="image/png" href="/assets/favicon.png?v=3">`.

---

## 2. Typography Stacks & Rules

### 2.1 Font Family Hierarchy
- **System Declaration**:
  ```css
  :root {
    font-family: Segoe UI, Arial, sans-serif;
  }
  ```
- **Platform Fallbacks**:
  - **Windows**: `Segoe UI` (native Windows system typography).
  - **macOS / iOS**: `Arial` -> `-apple-system, BlinkMacSystemFont`.
  - **Linux / Android**: `Roboto, Arial, sans-serif`.

---

### 2.2 Tabular Numbers Rule
Prices, counters, matrix gaps, and percentages must align vertically without jitter. The dashboard explicitly enforces tabular numerals on all numeric table cells and metrics:

```css
td.numeric, .price-comparison-layout .matrix-price, .price-comparison-layout .stat > strong {
  font-variant-numeric: tabular-nums;
}
```

---

## 3. Localization & Formatting Standards

### 3.1 Cairo Timezone (`Africa/Cairo`)
All timestamps observed on competitor websites, snapshots, and logs are rendered in Egypt Standard Time (`Africa/Cairo`):

```javascript
const cairoDateFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: 'Africa/Cairo',
  dateStyle: 'medium',
  timeStyle: 'short'
});

const date = v => v ? cairoDateFormatter.format(new Date(v)) : 'Not observed';
```

- **Output Example**: `2 Nov 2026, 19:30` (using `en-GB` format: day first, abbreviated month, year, 24-hour time).
- **Header Label**: Pinned in the workspace header as `<span class="timezone">Africa/Cairo</span>`.

---

### 3.2 Egyptian Pound Currency Formatting (EGP)
Prices in the backend database are stored in **minor units** (piastres / cents, e.g. `42000` = `420.00 EGP`):

```javascript
const egpNumberFormatter = new Intl.NumberFormat('en-EG', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2
});

const money = v => v == null ? '—' : `${egpNumberFormatter.format(Number(v) / 100)} EGP`;
```

- **Output Example**: `420.00 EGP`
- **Non-EGP Fallback** (`operatorPrice` in `operator.js:7`):
  ```javascript
  const operatorPrice = (v, currency = 'EGP') =>
    v == null || !currency ? 'Not confirmed'
    : currency === 'EGP' ? money(v)
    : `${(Number(v) / 100).toLocaleString('en-EG', {minimumFractionDigits: 2, maximumFractionDigits: 2})} ${currency}`;
  ```

---

### 3.3 External Link Glyphs
All external links pointing to store websites, public ad library pages, and evidence payloads append the northeast arrow glyph (`↗`, `\u2197`):

```javascript
const link = (url, label = 'Source') =>
  `<a href="${esc(safeURL(url))}" target="_blank" rel="noopener noreferrer">${esc(label)} ↗</a>`;
```
- **Styling**: `a { color: var(--action); text-underline-offset: 3px; }`
- **Security**: Always enforces `rel="noopener noreferrer"` and `target="_blank"`.
