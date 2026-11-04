# Application Shell & Global Layout — Lazy Bean Market Intelligence

This document specifies the global layout structure, navigation system, header, toolbar, notifications, live progress banners, footers, and modal dialogs extracted from `app/static/index.html`, `app/static/style.css`, `app/static/app.js`, and `app/static/operations.js`.

---

## 1. Global Layout Grid & Shell Architecture

The dashboard is structured as a two-column desktop layout with a fixed-position left sidebar and a flexible main workspace:

```
+---------------------------------------------------------------------------------------------------+
| Aside (Sidebar)     | Workspace Header (52px height)                                             |
| Width: 205px        | [☰] Operations / Competitor intelligence               Africa/Cairo        |
| Fixed left: 0       +-----------------------------------------------------------------------------+
|                     | Main Content Container (#content, padding: 16px 20px)                       |
| Brand Tile (42px)   |   Heading: [EYEBROW] Title & Subtitle      [Refresh sources (Primary)]     |
|                     |   Toolbar: Report date [Select]  [Badge]  [Reload]                          |
| Nav Items (1..5)    |   Notice Banner (#notice, yellow callout when active)                       |
|                     |   Live Progress Banner (#liveProgressBanner, active during collection)      |
| Details (Settings)  |   -----------------------------------------------------------------------   |
|   Nav Items (6..8)  |   View Section (#view) -> Screen-specific content injection                 |
|                     |   -----------------------------------------------------------------------   |
| Side Note & Lock    | Workspace Footer: Legal / Intelligence Evidence Disclaimer                  |
+---------------------------------------------------------------------------------------------------+
```

---

## 2. Sidebar Navigation (`<aside>`)

### 2.1 Container Geometry & Transitions
- **Position**: `fixed; inset: 0 auto 0 0;` (pinned to viewport left).
- **Width**: `205px` (desktop); shrinks to `180px` below `1050px` width; transforms to a relative horizontal strip below `720px`.
- **Background**: `white` (`#ffffff`).
- **Border**: `border-right: 1px solid var(--line)` (`#dfe3e8`).
- **Padding**: `20px 12px`.
- **Z-Index**: `20`.
- **Collapse Animation**: `transition: transform 0.2s ease;`.
- **Collapsed State**:
  ```css
  body.sidebar-collapsed aside {
    transform: translateX(-100%);
  }
  body.sidebar-collapsed .workspace {
    margin-left: 0;
    width: 100%;
  }
  ```

---

### 2.2 Brand Identity Block (`.brand`)
- **Container**: `display: flex; align-items: center; gap: 10px; padding: 0 6px 18px; font-size: 17px; font-weight: 700;`.
- **Mark Tile (`.mark`)**:
  - Size: `42px × 42px`, `flex-shrink: 0`.
  - Background: `#202723` (dark charcoal-green).
  - Border Radius: `11px`.
  - Box Shadow: `0 2px 6px rgba(0, 0, 0, 0.12)`.
  - Layout: `display: grid; place-items: center;`.
- **Mark Image**: `<img src="/assets/logo.png?v=3" width="26" height="26">` (`style="width: 26px; height: 26px; object-fit: contain; display: block;"`).
- **Brand Typography**:
  - Main text: `Lazy Bean` (`font-size: 17px; font-weight: 700; color: var(--ink)`).
  - Small subtext: `<small>MARKET INTELLIGENCE</small>` (`display: block; font-size: 9px; letter-spacing: 1.3px; margin-top: 2px; color: var(--muted);`).

---

### 2.3 Navigation Items (`<nav id="nav">`)
The navigation is rendered by `renderNav()` in `app.js:117` and updated dynamically:

```html
<nav aria-label="Main navigation" id="nav">
  <!-- Primary Top Views (Index 0..4) -->
  <button data-view="Overview" data-route="#/overview" aria-current="page">Overview</button>
  <button data-view="Weekly actions" data-route="#/weekly-actions">Weekly actions</button>
  <button data-view="Price comparisons" data-route="#/price-comparisons">Price comparisons</button>
  <button data-view="Ads & campaigns" data-route="#/ads-campaigns">Ads & campaigns</button>
  <button data-view="Review queue" data-route="#/review-queue">Reviews needing attention (3)</button>
  
  <!-- Secondary Settings Views (Index 5..7) -->
  <details class="nav-secondary" open>
    <summary>Settings & history</summary>
    <button data-view="Sources" data-route="#/sources">Manage sources</button>
    <button data-view="History" data-route="#/history">Report history</button>
    <button data-view="Improve recommendations" data-route="#/improve-recommendations">Internal data import</button>
  </details>
</nav>
```

#### Navigation Item Styles:
- **Default State**:
  ```css
  nav button {
    text-align: left;
    border: 0;
    color: #59616a;
    font-weight: 500;
    padding: 8px 10px;
    background: none;
    font-size: 13px;
    cursor: pointer;
    border-radius: 6px;
    width: 100%;
  }
  ```
- **Active State (`aria-current="page"`)**:
  ```css
  nav button[aria-current="page"] {
    background: #eaf1ee;
    color: #164f40;
    font-weight: 650;
  }
  ```
- **Secondary Accordion (`.nav-secondary`)**:
  - `border-top: 1px solid var(--line); margin-top: 14px; padding-top: 12px;`.
  - `summary`: `padding: 10px 12px; color: var(--muted); font-size: 12px; font-weight: 650; cursor: pointer;`.
  - Automatically opens if `activeView` is `Sources`, `History`, or `Improve recommendations`.

---

### 2.4 Side Note & Dashboard Lock Container (`.side-note`)
Pinned to the bottom of the sidebar:
- **Container**: `margin-top: auto; padding: 20px 10px 0; border-top: 1px solid var(--line); font-size: 12px; color: #4b5751;`.
- **Status Dot (`.dot`)**: `display: inline-block; width: 7px; height: 7px; border-radius: 50%; background: #387c60; margin-right: 5px;`.
- **Label**: `Read-only intelligence`.
- **Body Note**: `<p>No automated pricing or ad changes.</p>` (`color: var(--muted); margin: 4px 0 8px;`).
- **Lock Button (`#logout`)**: `<button id="logout" class="link">Lock dashboard</button>`.
  - **Runtime behavior**: In `localPreview` mode (default local execution), this button is hidden (`$('logout').hidden = localPreview;`). In authenticated token mode, clicking it clears `token = ''` and opens the `#login` modal.

---

## 3. Workspace Layout (`.workspace`)

- **Offset**: `margin-left: 205px; width: calc(100% - 205px); min-width: 0; transition: margin-left 0.2s ease, width 0.2s ease;`.
- **Collapsed Offset**: `margin-left: 0; width: 100%;`.

---

### 3.1 Header (`<header>`)
- **Height**: `52px; padding: 0 20px; display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--line); background: white; color: var(--muted); font-size: 13px;`.
- **Left Section**:
  - **Sidebar Toggle Button (`#sidebarToggle`)**:
    ```html
    <button id="sidebarToggle" type="button" class="btn-xs ghost"
      style="padding: 4px 8px; font-size: 13px; font-weight: 700; cursor: pointer; border-radius: 4px"
      title="Toggle Fullscreen Width">☰</button>
    ```
    - Clicking toggles class `sidebar-collapsed` on `document.body`.
  - **Breadcrumb Text**: `Operations / Competitor intelligence`.
- **Right Section**:
  - `<span class="timezone">Africa/Cairo</span>` (hidden on mobile below 720px).

---

### 3.2 Main Heading (`.heading`)
- **Container**: `display: flex; align-items: center; justify-content: space-between; gap: 20px;`.
- **Left Block**:
  - Eyebrow: `<p class="eyebrow">WEEKLY MARKET REVIEW</p>`.
  - Title: `<h1 id="title">Overview</h1>` (`font-size: 30px; letter-spacing: -0.7px;`).
  - Subtitle: `<p id="subtitle">Evidence first. Decisions with context.</p>` (`color: var(--muted); font-size: 14px; margin-top: 9px;`).
- **Right Block**:
  - Primary Action Button: `<button id="refresh" class="primary">Refresh sources</button>`.
  - When collection is running: text changes to `"Update in progress"`, button is disabled.

---

### 3.3 Toolbar (`.toolbar`)
- **Container**: `margin: 25px 0; display: flex; align-items: center; gap: 15px; border-block: 1px solid var(--line); padding: 14px 0; font-size: 13px;`.
- **Report Date Selector**:
  ```html
  <label>Report date
    <select id="runSelect">
      <option value="ea0e412df4a846cf9fb1e51833d3d868">2 Nov 2026, 19:30 · Updated</option>
    </select>
  </label>
  ```
- **Status Indicator**: `<span id="runStatus" aria-live="polite"></span>` (displays `.badge` matching the snapshot's state: `Updated`, `Incomplete`, `Update failed`, etc.).
- **Reload Action**: `<button id="reload" class="link">Reload</button>`.

---

### 3.4 Notification Area (`#notice`)
- **Container**: `<div id="notice" role="status" hidden></div>`.
- **CSS**: `padding: 12px 16px; background: #fff3d9; color: #694700; margin-bottom: 18px; border-radius: 5px; font-size: 13px;`.
- **Behavior**: Revealed whenever `notice(text)` is invoked (e.g. "Match approved", "Source configuration saved", "Product link copied"). Hidden when empty.

---

### 3.5 Live Progress Banner (`#liveProgressBanner`)
Rendered dynamically by `renderLiveProgressBanner()` in `operations.js:142` when a run is `queued`, `running`, `cancelling`, or `draft`:

```html
<div class="progress-banner">
  <div class="progress-header">
    <div>
      <strong style="font-size: 15px; color: var(--action)">Checking Coffee & Cream, CAFELAX…</strong>
      <span class="muted" style="margin-left: 8px">2 / 6 source checks finished · 450 saved observations</span>
    </div>
    <div style="display: flex; gap: 8px; align-items: center">
      <span style="font-size: 16px; font-weight: 700; color: var(--action)">48%</span>
      <button type="button" class="btn-xs ghost" data-cancel-run="ea0e412df4...">Stop update</button>
    </div>
  </div>
  
  <div class="progress-track" role="progressbar" aria-valuenow="48" aria-valuemin="0" aria-valuemax="100">
    <div class="progress-fill" style="width: 48%"></div>
  </div>
  
  <div class="progress-grid">
    <div class="progress-card">
      <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px">
        <strong>Lazy Bean</strong>
        <span class="badge good">Updated</span>
      </div>
      <div class="muted" style="font-size: 11px">1,251 products · Batch 10 / 10</div>
    </div>
    <!-- Cards for the remaining 5 competitor sources -->
  </div>
</div>
```

---

### 3.6 Footer (`<footer>`)
- **Container**: `font-size: 11px; color: var(--muted); padding: 0 36px 26px;`.
- **Text**: `Observed evidence supports review. Ad presence does not establish demand or campaign success.`.

---

## 4. Modal Dialogs (`<dialog>`)

### 4.1 Global Dialog Styling
```css
dialog {
  border: 1px solid var(--line);
  border-radius: 10px;
  padding: 28px;
  width: 560px;
  max-width: calc(100% - 30px);
  max-height: 90vh;
  color: var(--ink);
  background: white;
}
dialog::backdrop {
  background: #101c2190;
}
dialog input:not([type="checkbox"]) {
  width: 100%;
}
dialog .actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 24px;
}
```

---

### 4.2 Modal Inventory

#### A. Login Dialog (`#login`)
- **Heading**: Logo mark + `LAZY BEAN OPERATIONS` eyebrow + `Unlock your workspace` (`h2`).
- **Description**: `Enter the API token from the local .env file. It stays in this tab’s memory.`.
- **Fields**: Access token password input (`#token`).
- **Error**: `<p id="loginError" role="alert"></p>` (`color: #9c3030`).
- **Submit Button**: `<button class="primary">Open dashboard</button>`.

#### B. Collection Dialog (`#runDialog`)
- **Heading**: `Collect a fresh snapshot` (`h2`).
- **Description**: `Update the selected stores. Saved reports remain available, and a store may be incomplete if it cannot be checked.`.
- **Preflight Callout**: `<div id="refreshChecks" class="callout" aria-live="polite" hidden></div>`.
- **Free Credit Confirmation Area**: `#freeCreditConfirmation` (input `#freeCreditsLeft`, checkbox `#freeCreditsConfirmed`, button `#confirmFreeCredits`).
- **Fields**:
  - Source selector (`#sourceSelect`): "All six sources" or individual store.
  - Pilot checkbox (`#pilot`): "Include stores waiting for validation" (default checked).
  - Draft checkbox (`#draft`): "Prepare a report without starting the update" (default unchecked).
- **Footer Callout**: `Weekly scheduling is off. A manual run never overwrites a published report.`.
- **Actions**: `<button type="button" data-close="runDialog">Cancel</button>` + `<button class="primary" id="startCollection">Start collection</button>`.

#### C. Detail & Evidence Modal (`#detailDialog`)
A multi-purpose dialog hosting dynamic inspection templates:
- **Product Evidence Details**: Product name, store, price, reference price, availability, attributes list (`Brand`, `Size / weight`, `Pack count`, `Capsule system`, `Model`), and "Open stored payload" link.
- **Price Gap Math Explanation**: Mathematical calculation breakdown (`(Lazy Bean − Market) ÷ Market × 100 = Gap%`), observed competitor price list.
- **Pilot Source Validation Sample**: 10-row product sample table with checkboxes, robots confirmation checkbox, verification notes input, and "Approve verified source sample" button.
- **Manual Product Linker**: Competitor catalog product search and selection dropdown to manually link and approve an exact match.
- **Advertiser Account Approval**: Advertiser ID, Display name, Public library URL, Ownership evidence URL, and verification notes.
