# Design System Specification — Lazy Bean Market Intelligence

This document specifies the exact design tokens, color palette, typography hierarchy, spacing scale, component geometry, elevation, and visual rules extracted directly from `app/static/style.css` and rendered DOM states.

---

## 1. Color Palette & Semantic Tokens

### 1.1 Core CSS Variables (`:root`)
Defined in `app/static/style.css:1`:

| Token | CSS Variable | Hex / Value | Usage |
| :--- | :--- | :--- | :--- |
| **Canvas Background** | `--canvas` | `#f5f6f8` | Global application background behind all panels and cards. |
| **Surface** | `--surface` | `#ffffff` | Background of cards, panels, modals, sidebar, header, and inputs. |
| **Primary Ink** | `--ink` | `#161b22` | Primary body text, headings, strong text, and table values. |
| **Muted Ink** | `--muted` | `#626b76` | Secondary labels, timestamps, metadata, captions, table headers. |
| **Line / Divider** | `--line` | `#dfe3e8` | Borders on panels, tables, dialogs, inputs, dividers, toolbar borders. |
| **Action Primary** | `--action` | `#164f40` | Deep forest green brand color. Primary buttons, selected tab, links, progress. |
| **Warning Ink** | `--warn` | `#79520b` | Warning badge text and caution highlights. |
| **Base Space** | `--space` | `24px` | Standard spacing unit for grid gutters and section breaks. |

---

### 1.2 Action & Brand Palette (Forest Green & Accents)
Extracted from buttons, navigation, progress, and focus rules:

| Color Role | Hex Value | Source Selector | Description |
| :--- | :--- | :--- | :--- |
| **Primary Action** | `#164f40` | `.primary`, `.weekly-tabs button[aria-selected="true"]` | Main button fill, active tab background, progress bar origin. |
| **Primary Hover** | `#103d31` | `.primary:hover` | Darkened green on button hover. |
| **Focus Ring (Global)** | `#388a75` | `:focus-visible` | Global outline ring on interactive elements (`outline: 3px solid #388a75; outline-offset: 3px`). |
| **Focus Ring (Layout)** | `#164f40` | `.price-comparison-layout :focus-visible`, `.operator-view :focus-visible` | Layout-scoped outline (`outline: 2px solid var(--action); outline-offset: 3px`). |
| **Active Nav Background**| `#eaf1ee` | `nav button[aria-current="page"]` | Light sage green background for currently active sidebar nav item. |
| **Active Nav Ink** | `#164f40` | `nav button[aria-current="page"]` | Deep green text with font weight `650`. |
| **Status Dot Active** | `#387c60` | `.dot` | Green circular indicator in the sidebar footer (`7px × 7px`). |
| **Brand Mark Background**| `#202723` | `.mark` | Dark charcoal-green tile holding the Lazy Bean mark (`42px × 42px`). |
| **Brand Mark Icon Fill** | `#fdec0a` | `LOGO.svg` | Vivid yellow coffee bean icon fill. |
| **Progress Gradient End**| `#2d866c` | `.progress-fill` | Linear gradient stop (`linear-gradient(90deg, #164f40, #2d866c)`). |
| **Matrix Own Column** | `#f4f9f6` | `.matrix-own` | Light pale mint green background for Lazy Bean's reference product column. |
| **Matrix Hover Row** | `#f5f9f7` | `.price-comparison-layout tbody tr:hover td` | Subtle green-tinted hover highlight on matrix rows. |
| **Matrix First Cell** | `#fafcfb` | `.price-comparison-layout td:first-child` | Pale tinted background for sticky product name cells. |

---

### 1.3 Semantic Status Palette (Good, Warning, Bad, Neutral)

| Semantic Role | Text Hex | Background Hex | Border Hex | Used In |
| :--- | :--- | :--- | :--- | :--- |
| **Good / Success** | `#236044` | `#e7f3ec` | `#9bc9b1` | `.badge.good`, `.btn-xs.approve` (`#1b5238` text), Progress banner (`#9bc9b1` border). |
| **Good Hover** | `#1b5238` | `#d4eadc` | `#9bc9b1` | `.btn-xs.approve:hover` |
| **Warning / Notice** | `#79520b` | `#fff3d9` | `#c3a04e` | `.badge.warn`, `#notice` (`#694700` text), `.callout` (`#c3a04e` left border, `#fffaf0` bg). |
| **Operator Caution** | `#70551b` | `#fff8e8` | `#c3a04e` | `.operator-warning` (3px solid left border). |
| **Bad / Danger** | `#962f32` | `#fbe9e9` | `#e6b3b3` | `.badge.bad`, `.btn-xs.reject` (`#962f32` text, `#e6b3b3` border). |
| **Error Message** | `#9c3030` | Transparent | None | `#loginError` (text under login inputs). |
| **Critical Progress Error**| `#b91c1c` | Transparent | None | `cleanProgressError()` in progress cards. |
| **Neutral Badge** | `#505963` | `#eef0f3` | None | `.badge` default state (grey background). |

---

### 1.4 Neutrals, Grays & Borders

| Token Name | Hex Value | Used In |
| :--- | :--- | :--- |
| **Border Line** | `#dfe3e8` | Panels, header bottom border, sidebar right border, horizontal dividers. |
| **Secondary Line** | `#edf0f2` | Table row separator (`border-bottom: 1px solid #edf0f2`). |
| **Input Border** | `#cbd1d7` | Form inputs, select dropdowns, textareas (`border: 1px solid #cbd1d7`). |
| **Button Border** | `#c8cdd3` | Standard button default border (`border: 1px solid #c8cdd3`). |
| **Ghost Border** | `#d5dbe0` | `.btn-xs.ghost` border (`border-color: #d5dbe0`). |
| **Ghost Text** | `#4f5861` | `.btn-xs.ghost` text color. |
| **Ghost Hover Background**| `#f0f3f2` | Standard button hover background. |
| **Nav Idle Ink** | `#59616a` | Unselected sidebar navigation button text color. |
| **Side Note Text** | `#4b5751` | Sidebar footer explanatory text. |
| **Table Header Background**| `#f9fafb` | Standard `th` background (`#f8fafc` in matrix table). |
| **Progress Track Gray** | `#e6ecef` | Background track of the collection progress bar. |
| **Progress Card Gray** | `#f8faf9` | Background of individual source progress cards. |
| **Operator Detail Tile** | `#f6f8f7` | Background of attribute definitions in `.operator-detail dl div`. |
| **Preformatted Gray** | `#f5f6f8` | Background of `<pre>` payload containers. |

---

## 2. Typography Specification

### 2.1 Font Family & General Rules
- **Primary Font Stack**: `Segoe UI, Arial, sans-serif` (`style.css:1`).
- **Base Font Size**: `15px`.
- **Base Line Height**: `1.5` (`22.5px`).
- **Antialiasing**: Standard browser default font rendering.

---

### 2.2 Typographic Hierarchy Table

| Element / Class | Size | Weight | Line Height | Letter Spacing | Color | Margin / Transform |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Eyebrow** (`.eyebrow`) | `10px` | `700` (Bold) | `1.4` | `1.5px` | `var(--muted)` (`#626b76`) | `margin: 0 0 8px`; Uppercase |
| **Page Title** (`h1`) | `30px` | `700` (Bold) | `1.2` | `-0.7px` | `var(--ink)` (`#161b22`) | `margin: 0` (Mobile: `26px`) |
| **Subtitle** (`#subtitle`)| `14px` | `400` (Regular)| `1.4` | `normal` | `var(--muted)` (`#626b76`) | `margin-top: 9px` |
| **Section Title** (`h2`)| `18px` | `700` (Bold) | `1.3` | `normal` | `var(--ink)` (`#161b22`) | `margin: 0 0 10px` |
| **Operator H2** (`.operator-view h2`)| `19px` | `700` (Bold) | `1.3` | `normal` | `var(--ink)` (`#161b22`) | `margin: 0` |
| **Card / Row Heading** (`h3`)| `15px` | `700` (Bold) | `1.4` | `normal` | `var(--ink)` (`#161b22`) | `margin: 0 0 8px` |
| **Body Paragraph** (`p`)| `15px` | `400` (Regular)| `1.5` | `normal` | `var(--ink)` (`#161b22`) | `margin: 8px 0 16px` |
| **Operator Paragraph** (`.operator-view p`)| `15px` | `400` (Regular)| `1.55` | `normal` | `var(--ink)` (`#161b22`) | `margin: 5px 0` |
| **KPI Strong Number** (`.stat strong`)| `30px` | `700` (Bold) | `1.3` | `normal` | `var(--ink)` (`#161b22`) | `margin: 8px 0`; `tabular-nums` |
| **Operator KPI Number** (`.operator-summary strong`)| `27px` | `700` (Bold) | `1.1` | `normal` | `var(--action)` (`#164f40`) | `margin: 0` |
| **KPI Top Label** (`.stat span`)| `12px` | `400` (Regular)| `1.4` | `normal` | `var(--muted)` (`#626b76`) | Uppercase tracking |
| **Table Header** (`th`)| `10px` | `650` (Semibold)| `1.3` | `0.7px` | `var(--muted)` (`#626b76`) | Uppercase (`text-transform: uppercase`) |
| **Matrix Header** (`.matrix-table th`)| `9.5px`| `700` (Bold) | `1.25` | `0.3px` | `var(--muted)` (`#626b76`) | Uppercase (`text-transform: uppercase`) |
| **Table Body Cell** (`td`)| `13px` | `400` (Regular)| `1.4` | `normal` | `var(--ink)` (`#161b22`) | Padding `15px 20px` |
| **Matrix Cell Body** (`.matrix-table td`)| `12px` | `400` (Regular)| `1.3` | `normal` | `var(--ink)` (`#161b22`) | Padding `8px 6px` |
| **Matrix Price** (`.matrix-price`)| `12px` | `700` (Bold) | `1.2` | `normal` | `var(--ink)` (`#161b22`) | `tabular-nums`; inline flex gap 3px |
| **Matrix Product Link** (`.matrix-title`)| `11px` | `400` (Regular)| `1.5` | `normal` | `var(--ink)` (`#161b22`) | `margin: 6px 0`; underline on hover |
| **Status Badge** (`.badge`)| `11px` | `600` (Semibold)| `1.5` | `normal` | Contextual | Padding `3px 7px` |
| **Matrix Badge** (`.matrix-table .badge`)| `9px` | `600` (Semibold)| `1.4` | `normal` | Contextual | Padding `2px 4px` |
| **Form Labels** (`label`)| `13px` | `600` (Semibold)| `1.4` | `normal` | `var(--ink)` (`#161b22`) | `display: grid; gap: 6px` |
| **Toolbar Text** (`.toolbar`)| `13px` | `400` (Regular)| `1.4` | `normal` | `var(--ink)` (`#161b22`) | Vertical border-block `1px solid var(--line)` |
| **Header Breadcrumb** (`header`)| `13px` | `400` (Regular)| `1.4` | `normal` | `var(--muted)` (`#626b76`) | Flex row, `52px` height |
| **Code / Monospace** (`code`, `pre`)| `12px` | `400` (Regular)| `1.4` | `normal` | `var(--ink)` (`#161b22`) | Background `#f5f6f8` |
| **Footer Text** (`footer`)| `11px` | `400` (Regular)| `1.4` | `normal` | `var(--muted)` (`#626b76`) | Legal disclaimer padding `0 36px 26px` |

---

## 3. Spacing, Dimensions & Grid Rules

### 3.1 Base Spacing Scale
- `2px` / `3px`: Micro gaps between badge elements and matrix price tags.
- `4px` / `5px`: Navigation button gaps, badge paddings, tag margins.
- `6px` / `8px`: Form label internal gaps, matrix cell padding, tab spacing.
- `10px` / `12px`: Brand header gap, stat card gaps (laptop), filter gutters.
- `14px` / `16px`: Stat card gutters (desktop), main padding (laptop), panel padding.
- `18px` / `20px`: Panel padding, action row padding, main content padding (desktop).
- `22px` / `24px`: Section gap, panel bottom margin, operator view gap.
- `28px`: Dialog internal padding.
- `40px`: Empty state top/bottom padding.

---

### 3.2 Fixed Dimensions
- **Sidebar Width**: `205px` (fixed on Desktop); `180px` (Laptop max-width 1050px); `100%` (Mobile max-width 720px).
- **Header Height**: `52px` (Desktop); `45px` (Mobile max-width 720px).
- **Brand Mark Tile**: `42px × 42px` (border radius `11px`).
- **Brand Mark Icon**: `28px × 28px` (object-fit: contain).
- **Status Dot**: `7px × 7px` (border radius `50%`).
- **Primary Buttons Height**: Minimum `42px` (padding `9px 15px`).
- **Extra Small Buttons (`.btn-xs`)**: Minimum `24px` (padding `2px 7px`, font-size `11px`, radius `4px`).
- **Matrix Copy Button (`.matrix-copy`)**: Minimum `28px` (padding `4px 7px`, font-size `10px`).
- **Progress Track Height**: `10px` (border-radius `999px`).
- **Dialog Max Width**: `560px` (max-width `calc(100% - 30px)`, max-height `90vh`).
- **Matrix Sticky First Column**: Width `18%`, min-width `160px`, max-width `230px`.
- **Matrix Lazy Bean Column**: Width `9.5%`, min-width `85px`.
- **Matrix Competitor Cell**: Width `11%`, min-width `102px`.
- **Matrix Median Column**: Width `7.5%`, min-width `72px` (numeric right-aligned).
- **Matrix Gap Column**: Width `6.5%`, min-width `65px` (numeric right-aligned).

---

## 4. Borders, Radii & Shadows

### 4.1 Border Radii
- `3px`: Matrix price badge border radius.
- `4px`: Badges, extra-small buttons (`.btn-xs`), sidebar toggle button.
- `5px`: Select, input, textarea, notice box (`#notice`), `.matrix-copy` button.
- `6px`: Standard buttons (`button`), progress cards (`.progress-card`), operator attribute tiles (`dl div`).
- `7px`: KPI stat cards (`.stat`), weekly tabs buttons (`.weekly-tabs button`).
- `8px`: Panels (`.panel`), progress banner (`.progress-banner`), operator summary buttons, decision rows (`.decision-row`).
- `10px`: Modals (`dialog`).
- `11px`: Brand icon tile (`.mark`).
- `999px`: Progress track and fill pill capsules.

---

### 4.2 Elevation & Shadows
- **Brand Mark Shadow**: `box-shadow: 0 2px 6px rgba(0, 0, 0, 0.12)`.
- **Matrix Sticky Header / Cell Shadow**: `box-shadow: 2px 0 5px rgba(0, 0, 0, 0.04)`.
- **Live Progress Banner Shadow**: `box-shadow: 0 2px 8px rgba(16, 61, 49, 0.06)`.
- **Modal Backdrop Color**: `background: #101c2190` (dark translucent overlay).

---

## 5. Focus & Interactive States

### 5.1 Focus Rings
- **Global Accessible Focus Ring**:
  ```css
  button:focus-visible, a:focus-visible, input:focus-visible, select:focus-visible, textarea:focus-visible {
    outline: 3px solid #388a75;
    outline-offset: 3px;
  }
  ```
- **Scoped Layout Focus Ring**:
  ```css
  .price-comparison-layout :is(button, a, input, select):focus-visible,
  .operator-view :is(button, a, input, select, summary):focus-visible {
    outline: 2px solid var(--action);
    outline-offset: 3px;
  }
  ```

### 5.2 Disabled States
- Disabled buttons reduce opacity to `0.5` and set cursor to `wait`:
  ```css
  button:disabled {
    opacity: 0.5;
    cursor: wait;
  }
  ```
