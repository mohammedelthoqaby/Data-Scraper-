# Migration Acceptance Criteria — Lazy Bean Market Intelligence

This document provides an objective, measurable checklist to certify that the future technology-stack implementation matches the existing dashboard's visual identity, component geometry, interactive flows, and data fidelity.

---

## 1. Global Shell & Layout Acceptance Criteria

- [ ] **Sidebar Geometry**: Fixed width of exactly `205px` on desktop (>=1051px), `180px` on laptop (721px–1050px), and transforms to a relative horizontal strip on mobile (<=720px).
- [ ] **Sidebar Collapse**: Clicking `#sidebarToggle` (`☰`) applies smooth `0.2s ease` transition, translating sidebar to `-100%` and adjusting workspace `margin-left: 0; width: 100%`.
- [ ] **Brand Identity Tile**: Tile is exactly `42px × 42px` with background `#202723`, border-radius `11px`, and shadow `0 2px 6px rgba(0,0,0,0.12)`. Inner logo is `26px × 26px` object-contain.
- [ ] **Brand Typography**: "Lazy Bean" title (`17px`, bold) with `<small>MARKET INTELLIGENCE</small>` (`9px`, letter-spacing `1.3px`, color `#626b76`).
- [ ] **Navigation Structure**: 5 primary buttons (Overview, Weekly actions, Price comparisons, Ads & campaigns, Review queue) + secondary `<details>` accordion ("Settings & history") holding Sources, History, and Improve recommendations.
- [ ] **Active Nav Styling**: Active page button has background `#eaf1ee`, text color `#164f40`, and font-weight `650`.
- [ ] **Header Dimensions & Info**: Header height is exactly `52px` (desktop) / `45px` (mobile). Contains breadcrumb `"Operations / Competitor intelligence"` and timezone badge `<span class="timezone">Africa/Cairo</span>` (hidden on mobile).
- [ ] **Toolbar Geometry**: Bordered top and bottom with `1px solid #dfe3e8`, padding `14px 0`, margin `25px 0`. Contains report date select, status badge, and "Reload" link button.
- [ ] **Footer Content**: Displays legal/intelligence evidence disclaimer: `"Observed evidence supports review. Ad presence does not establish demand or campaign success."`.

---

## 2. Design System & Visual Tokens Fidelity

- [ ] **Color Tokens Match**:
  - Canvas: `#f5f6f8`
  - Surfaces: `#ffffff`
  - Primary Ink: `#161b22`
  - Muted Ink: `#626b76`
  - Divider Lines: `#dfe3e8`
  - Primary Action Green: `#164f40` (hover `#103d31`)
  - Warning Text: `#79520b` (bg `#fff3d9`, border `#c3a04e`)
  - Bad / Error Text: `#962f32` (bg `#fbe9e9`, border `#e6b3b3`)
  - Success / Good Text: `#236044` (bg `#e7f3ec`, border `#9bc9b1`)
- [ ] **Typography Match**:
  - Primary font stack is `Segoe UI, Arial, sans-serif`.
  - Base body font size is `15px` with line-height `1.5`.
  - H1 is `30px` (bold, letter-spacing `-0.7px`) on desktop, `26px` on mobile.
  - H2 is `18px` (standard) / `19px` (operator view).
  - H3 is `15px` (bold, line-height `1.4`).
  - Eyebrow is `10px`, bold, uppercase, letter-spacing `1.5px`, color `#626b76`.
  - Tabular numerals (`tabular-nums`) enforced on all prices, percentages, counters, and metric cards.
- [ ] **Focus Rings**: Accessible focus indicators (`outline: 3px solid #388a75; outline-offset: 3px` globally; `outline: 2px solid #164f40` in layout containers).

---

## 3. Screen-by-Screen Functional Acceptance

### 3.1 Overview Screen
- [ ] Displays Operator Intro with last update date in `Africa/Cairo` timezone.
- [ ] Surfaces warning banner if collection failed or sources are incomplete.
- [ ] Renders 3-column operator summary KPI buttons (Suggested actions, Supported comparisons, Product matches to review) with correct counts and click-to-navigate behavior.
- [ ] Lists top 3 "Changes at competitors" (`.decision-row`) or empty state.
- [ ] Lists top 3 "What needs attention?" action items with `[View actions]` buttons.
- [ ] Renders Integration Readiness panel, Meta Ads API panel, and Bright Data routes table.

### 3.2 Price Comparisons Matrix Screen
- [ ] Renders 4 KPI cards (`.stats-4`): Lazy Bean Catalog, Matched Products, Competitor Products, Brands & Categories.
- [ ] Panel header contains functional `[⬇ Export Matrix CSV]` link.
- [ ] Implements 4 functional filter controls: Search (100ms debounced), Category dropdown, Brand dropdown, and Show Products toggle (`All` vs `Matched Only`).
- [ ] Top pager displays current range (`Showing 1–50 of N products`), functional `‹ Previous`, `Page X / Y`, `Next ›`, and toggle between `50` and `100` per page.
- [ ] Multi-column matrix table maintains proportional widths (Sticky: 18%, Lazy Bean: 9.5%, 5 Competitors: 11% each, Median: 7.5%, Gap: 6.5%).
- [ ] Sticky first column remains pinned during horizontal scroll with `box-shadow: 2px 0 5px rgba(0,0,0,0.04)`.
- [ ] Lazy Bean column has distinctive `#f4f9f6` background.
- [ ] Competitor cells display price, percentage difference badge (+X% / -X%), strikethrough reference price, external link with `↗`, observed date, stock badge, status badge, `[Copy link]` button, and `[View details]` button.
- [ ] Empty state renders when filters match 0 products.

### 3.3 Weekly Actions Screen
- [ ] Two-tab container (`.weekly-tabs`) with `Suggested actions` and `Competitor offers`.
- [ ] Supports full keyboard navigation across tabs (ArrowLeft, ArrowRight, Home, End).
- [ ] Tab 1 lists suggested decision rows with rule tags, price gap, store count, stock gate, and `[Why this suggestion?]` button.
- [ ] Clicking `[Why this suggestion?]` opens math modal with formula breakdown `(Own − Median) ÷ Median × 100 = Gap%`.
- [ ] `[Show more actions]` button expands list by 5 items.
- [ ] Tab 2 lists competitor offer cards with competitor filter, offer type filter, count label, and `[Show more offers]` expansion (+10).
- [ ] Offers display advertised reference price and discount percentage badge.

### 3.4 Ads & Campaigns Screen
- [ ] Renders observed competitor ads list (up to 50 cards) with competitor name, platform badge (Meta/Google), headline/body copy, reported status, and source link.
- [ ] Renders manual ad evidence form with JSON validation.
- [ ] Renders "Refresh paid-ad evidence" form (`#adsRefreshForm`).
- [ ] Renders Current advertiser review table and Provider samples awaiting approval.
- [ ] Renders Meta Ads Library API token form (`data/META_ACCESS_TOKEN.env.txt`) and re-check button.

### 3.5 Review Queue Screen
- [ ] Displays redirect warning banner if accessed via `#/catalogue-evidence`.
- [ ] Section 1 ("Are these the same product?") lists pending match reviews with side-by-side attribute diffs (`Brand`, `Net weight`, `Capsule count`, `Capsule system`, `Model`).
- [ ] Review form requires verification notes (`minlength="10"`), and provides `[Confirm same product]` and `[Not the same]` buttons.
- [ ] Submitting match updates review queue and immediately updates navigation badge counter.
- [ ] Section 2 lists ambiguous offers needing terms check.
- [ ] Section 3 lists unapproved advertiser identities with candidate profiles and ownership links.

### 3.6 Sources Screen
- [ ] Lists all 6 sources (Lazy Bean + 5 competitors) with adapter version, validation status badge, monitoring status text, and toggle button (`Enable / Disable monitoring`).
- [ ] Clicking `[Review sample]` opens modal with 10 real products from snapshot, verification checkboxes, robots confirmation, and notes field.
- [ ] Form `#configForm` allows updating store format, entry URLs, and change notes.
- [ ] Form `#sourceForm` adds pending competitor domain with automatic slug generation.
- [ ] Form `#brightdataPilotForm` triggers bounded test collection.

### 3.7 History Screen
- [ ] State 1: Displays 4 KPI cards, Quick Jump select, Archived Snapshots table (with `[View Price Comparisons ↗]`), and Complete Historical Price Log table.
- [ ] Historical Price Log supports search, store filter, brand filter, snapshot filter, and pagination (200 / page or Show All).
- [ ] State 2 (Historical Matrix): When snapshot is selected, renders full price matrix with top bar containing `[‹ Back to All Snapshots]`, snapshot date, state badge, snapshot switcher dropdown, and CSV export.

### 3.8 Improve Recommendations Screen
- [ ] File input accepts `.csv, .xlsx`.
- [ ] Submitting form triggers preview validation.
- [ ] If valid: surfaces `[Commit validated import]` button; clicking commits data with confirmation notice.

---

## 4. Modal Dialogs & Interaction Acceptance

- [ ] **Dialog Styling**: Native `<dialog>` elements styled with border `1px solid #dfe3e8`, radius `10px`, padding `28px`, width `560px`, and dark overlay backdrop `#101c2190`.
- [ ] **Close Behavior**: Escape key or clicking buttons with `data-close="{dialogId}"` closes dialog without side effects.
- [ ] **One-Click URL Copying**: Clicking `[data-copy-product-url]` copies URL to clipboard, changes button text to `"Copied"`, shows notice `"Product link copied."`, and reverts button text after `1,800ms`.
- [ ] **Preflight Checks**: Opening `#runDialog` triggers preflight verification; disables start button if run is active or quota unverified.
- [ ] **Live Progress Banner**: In-flight collections render animated progress bar, percentage, source-by-source cards, and a working `[Stop update]` button.
- [ ] **Polling Loop**: Active runs poll `GET /api/runs/{id}` every 3,000ms until terminal state (`completed`, `partial`, `failed`, `cancelled`).

---

## 5. Responsive Behavior Acceptance

- [ ] **1440px / 1366px (Desktop)**: Full two-column layout with 205px sidebar, 4-column matrix filters, 4-column KPI grid.
- [ ] **1050px (Laptop)**: Sidebar narrows to 180px; matrix filters and KPIs reflow to 2×2 grid.
- [ ] **900px (Tablet)**: Matrix table enforces `min-width: 1000px` with horizontal scroll while sticky first column stays pinned.
- [ ] **720px (Mobile)**:
  - Sidebar moves to top block; side-note is hidden.
  - Navigation converts to a horizontally scrolling swipeable strip (`display: flex; overflow-x: auto; gap: 3px; min-height: 44px;`).
  - Header timezone label is hidden.
  - Heading aligns vertically with H1 reduced to `26px`.
  - Operator summary KPIs stack to 1-column.
  - Decision rows and offer rows collapse to vertical flex-column with full-width buttons.
  - Offer prices align left (`text-align: left`).
- [ ] **520px / 390px (Smartphone)**: Matrix filters collapse to single column; KPI card number font-size reduces to `26px`.
