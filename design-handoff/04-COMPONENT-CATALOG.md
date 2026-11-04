# Component Catalog — Lazy Bean Market Intelligence

This document defines the complete reusable component catalog, variants, dimensions, states, and HTML structures extracted from the Lazy Bean Competitor Intelligence Dashboard.

---

## 1. Buttons

### 1.1 Base Button (`button`)
- **CSS Selector**: `button`
- **Default Properties**:
  - `background: white; border: 1px solid #c8cdd3; border-radius: 6px;`
  - `min-height: 42px; padding: 9px 15px; color: var(--ink); font-weight: 600; cursor: pointer;`
- **Hover State**: `background: #f0f3f2;`
- **Disabled State**: `opacity: 0.5; cursor: wait;`
- **Focus State**: `outline: 3px solid #388a75; outline-offset: 3px;`

---

### 1.2 Button Variants

| Variant | Class / Selector | Visual Properties | Hover / Active | Usage |
| :--- | :--- | :--- | :--- | :--- |
| **Primary** | `.primary` | `background: #164f40; border-color: #164f40; color: white; font-weight: 600;` | `background: #103d31;` | Main calls to action: "Refresh sources", "Start collection", "Open dashboard". |
| **Link** | `.link` | `border: 0; background: transparent; color: #164f40; padding: 4px 8px; font-weight: 600;` | Text underline | Inline actions, modal triggers: "Reload", "Lock dashboard", "Inspect evidence". |
| **Ghost Extra-Small** | `.btn-xs.ghost` | `min-height: 24px; padding: 2px 7px; font-size: 11px; border-radius: 4px; background: #f8f9fa; border: 1px solid #d5dbe0; color: #4f5861;` | `background: #eef2f5;` | Secondary row actions, sidebar toggle, CSV export pills, table pagination. |
| **Approve Extra-Small** | `.btn-xs.approve`| `min-height: 24px; padding: 2px 7px; font-size: 11px; border-radius: 4px; background: #e7f3ec; border: 1px solid #9bc9b1; color: #1b5238; font-weight: 600;` | `background: #d4eadc;` | Affirmative actions: "View Price Comparisons ↗", Matrix match approval. |
| **Reject Extra-Small** | `.btn-xs.reject` | `min-height: 24px; padding: 2px 7px; font-size: 11px; border-radius: 4px; background: #fbe9e9; border: 1px solid #e6b3b3; color: #962f32; font-weight: 600;` | `background: #f5dada;` | Negative actions: Matrix match rejection. |
| **Matrix Copy Link** | `.matrix-copy` | `min-height: 28px; padding: 4px 7px; font-size: 10px; border-radius: 5px; white-space: nowrap; background: #f8f9fa; border: 1px solid #d5dbe0; color: #4f5861;` | `background: #eef2f5;` | One-click clipboard copy button on matrix cells. Toggles to "Copied" for 1.8s. |

---

## 2. Status Badges (`.badge`)

### 2.1 Base Badge Structure
- **Selector**: `.badge`
- **Default Properties**:
  - `display: inline-block; font-size: 11px; line-height: 1.5; border-radius: 4px; padding: 3px 7px; background: #eef0f3; color: #505963; font-weight: 600; white-space: normal;`
- **Matrix Variation**: `.matrix-table .badge` (`font-size: 9px; padding: 2px 4px; line-height: 1.4; border-radius: 3px;`).

---

### 2.2 Semantic Badge Variants & State Mapping

```html
<span class="badge good">Updated</span>
<span class="badge warn">Incomplete</span>
<span class="badge bad">Update failed</span>
<span class="badge">Draft</span>
```

| State String | Variant Class | Displayed Label | Meaning |
| :--- | :--- | :--- | :--- |
| `completed` | `.badge.good` | **Updated** | Source crawl or run successfully finalized. |
| `accepted` | `.badge.good` | **Available for review** | Extracted observation accepted within scope. |
| `approved` | `.badge.good` | **Confirmed** | Equivalence match confirmed by operator. |
| `approved_manual` | `.badge.good` | **Confirmed** | Manually confirmed product equivalence. |
| `exact_auto` | `.badge.good` | **Matched** | Automated deterministic match passed. |
| `validated` | `.badge.good` | **Validated** | Domain pilot sample validated by operator. |
| `in_stock` | `.badge.good` | **Available** | Verified in stock on competitor website. |
| `partial` | `.badge.warn` | **Incomplete** | Some sources failed; retained partial data. |
| `needs_review` | `.badge.warn` | **Needs a data check** | Extraction ambiguity or uncertain terms. |
| `needs_identity_review`| `.badge.warn` | **Account needs confirmation** | Ad account unverified or pending candidate review. |
| `suggested` | `.badge.warn` | **Needs review** | Candidate match requires operator check. |
| `pending_validation` | `.badge.warn` | **Needs validation** | Store unvalidated; collection blocked. |
| `manual_evidence` | `.badge.warn` | **Manually provided** | Ad record entered via manual JSON form. |
| `cancelled` | `.badge.warn` | **Stopped** | Operator stopped the update. |
| `cancelling` | `.badge.warn` | **Stopping** | In-flight provider cancellation. |
| `failed` | `.badge.bad` | **Update failed** | Collection or parser crashed. |
| `blocked` | `.badge.bad` | **Collection paused** | Robots or network access prevented collection. |
| `quarantined` | `.badge.bad` | **Needs a data check** | Contradicting pack size/price anomaly. |
| `rejected` | `.badge.bad` | **Not confirmed** | Match rejected by operator. |
| `out_of_stock` | `.badge.bad` | **Unavailable** | Verified out of stock. |
| `budget_exhausted` | `.badge.warn` | **Allowed usage used or reserved** | Usage hit cycle cap. |
| `budget_unverified` | `.badge.warn` | **Allowance needs confirmation** | Free allowance requires counter check. |
| `running` | `.badge` | **Updating** | Background collection active. |
| `queued` | `.badge` | **Waiting** | Queued behind active task. |
| `draft` | `.badge` | **Draft** | Snapshot held without collection. |

---

## 3. KPI & Summary Cards

### 3.1 Operator Summary Cards (`.operator-summary button`)
Used in the Overview screen to summarize action counts and provide immediate navigation:

```html
<div class="operator-summary">
  <button data-view="Weekly actions">
    <strong>3</strong>
    <span>Suggested actions</span>
  </button>
  <button data-view="Price comparisons">
    <strong>121</strong>
    <span>Supported price comparisons</span>
  </button>
  <button data-view="Review queue">
    <strong>3</strong>
    <span>Product matches to review</span>
  </button>
</div>
```

- **Container**: `display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 14px;`.
- **Card Element**: `text-align: left; background: #fff; border: 1px solid var(--line); border-radius: 8px; padding: 18px 20px; display: grid; gap: 7px; min-height: 104px; cursor: pointer;`.
- **Number (`strong`)**: `font-size: 27px; line-height: 1.1; color: var(--action);`.
- **Label (`span`)**: `font-size: 13px; color: var(--ink);`.

---

### 3.2 4-Column Stat Grid (`.stats-4`)
Used in Price Comparisons and History Archive screens:

```html
<div class="stats-4">
  <div class="stat">
    <span>LAZY BEAN CATALOG</span>
    <strong>1,251</strong>
    <small>All accepted Lazy Bean products</small>
  </div>
  <!-- 3 additional stat cards -->
</div>
```

- **Container**: `display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; margin: 0 0 20px; align-items: stretch;`.
- **Card (`.stat`)**: `background: white; border: 1px solid var(--line); border-radius: 7px; padding: 18px 20px; min-height: 154px; display: flex; flex-direction: column; align-items: flex-start;`.
- **Top Label (`span`)**: `font-size: 11px; line-height: 1.5; letter-spacing: 0.04em; color: var(--muted);`.
- **Value (`strong`)**: `font-size: 30px; line-height: 1.2; margin: 12px 0 8px; font-variant-numeric: tabular-nums;`.
- **Caption (`small`)**: `font-size: 12px; line-height: 1.55; color: var(--muted); overflow-wrap: anywhere;`.

---

## 4. Decision & Offer Rows

### 4.1 Decision Row (`.decision-row`)
The primary scannable building block of operator screens (Suggested actions, Reviews, Sources, History, Ads):

```html
<article class="decision-row">
  <div>
    <h3>Lavazza Crema e Gusto 250g Ground Coffee</h3>
    <strong>Review this price</strong>
    <p>Observed competitor prices support a closer look.</p>
    <p class="muted">3 stores compared · -12.5% price gap. Confirm the product and conditions before acting.</p>
  </div>
  <button type="button" data-op-rec="0">Why this suggestion?</button>
</article>
```

- **Container**: `background: #fff; border: 1px solid var(--line); border-radius: 8px; padding: 18px 20px; display: flex; justify-content: space-between; align-items: flex-start; gap: 20px; min-width: 0; margin-bottom: 14px;`.
- **Title (`h3`)**: `font-size: 15px; line-height: 1.4; margin: 0 0 6px; font-weight: 700;`.
- **Action Label (`strong`)**: `color: var(--action); font-size: 13px; font-weight: 700;`.
- **Action Button**: `flex-shrink: 0; min-height: 42px;`.

---

### 4.2 Competitor Offer Row (`.offer-row`)
Used in Weekly Actions (Competitor offers tab):

```html
<article class="offer-row">
  <div>
    <small>CAFELAX · Monitored pages</small>
    <h3>Illy Espresso Decaf 250g Ground Coffee</h3>
    <p><strong>Clearance discount</strong> · 20% off while supplies last</p>
    <p>Availability: Available · Observed 2 Nov 2026, 19:30 · First observed: not confirmed</p>
    <p>Our match: Illy Decaffeinato 250g Ground · 480.00 EGP</p>
    <button type="button" class="link" data-op-offer="obs_123">View source details</button>
    <a href="https://..." target="_blank" rel="noopener noreferrer">Open source ↗</a>
  </div>
  <div class="offer-prices">
    <strong>410.00 EGP</strong>
    <span>Advertised reference 510.00 EGP</span>
    <span>20% advertised discount</span>
  </div>
</article>
```

- **Left Block**: Metadata, store badge, offer title, condition terms, availability, matching reference product, details modal trigger, external store link.
- **Right Block (`.offer-prices`)**: `min-width: 155px; display: grid; gap: 6px; text-align: right; font-size: 12px; color: var(--muted);`.
- **Recorded Price (`strong`)**: `font-size: 16px; color: var(--ink); font-weight: 700; font-variant-numeric: tabular-nums;`.

---

## 5. Panels & Containers

### 5.1 Panel Structure (`.panel`)
```html
<div class="panel">
  <div class="panel-heading">
    <h2>Lazy Bean vs. Competitors — Complete Price Comparison Matrix</h2>
    <div style="display:flex;gap:8px;align-items:center">
      <a class="btn-xs ghost" href="/api/runs/.../export-matrix.csv">⬇ Export Matrix CSV</a>
    </div>
  </div>
  <div class="panel-body">
    <!-- Panel Content or Controls -->
  </div>
</div>
```

- **Container**: `background: white; border: 1px solid var(--line); border-radius: 8px; margin-bottom: 22px; overflow: hidden;`.
- **Heading (`.panel-heading`)**: `padding: 20px 22px; border-bottom: 1px solid var(--line); display: flex; justify-content: space-between; align-items: center; gap: 12px;`.
- **Heading Text (`h2`)**: `margin: 0; font-size: 18px; line-height: 1.3;`.
- **Body (`.panel-body`)**: `padding: 22px;`.

---

## 6. Matrix Table & Cell Elements

### 6.1 Matrix Table Container (`.matrix-table`)
- **Container**: `.table-scroll.matrix-table` (`width: 100%; overflow-x: auto; -webkit-overflow-scrolling: touch;`).
- **Table**: `table-layout: fixed; width: 100%; min-width: 0; border-collapse: separate; border-spacing: 0;`.
- **Column Group Definition**:
  ```html
  <colgroup>
    <col style="width: 18%">
    <col style="width: 10%">
    <col style="width: 11.2%"><!-- Coffee & Cream -->
    <col style="width: 11.2%"><!-- CAFELAX -->
    <col style="width: 11.2%"><!-- Fengany -->
    <col style="width: 11.2%"><!-- Coffee Zone Misr -->
    <col style="width: 11.2%"><!-- Strong Coffee -->
    <col style="width: 9%">  <!-- Median -->
    <col style="width: 7%">  <!-- Gap -->
  </colgroup>
  ```

---

### 6.2 Sticky First Column (`th:first-child`, `td:first-child`)
- **Properties**:
  - `position: sticky; left: 0; z-index: 2;` (Header has `z-index: 3`).
  - `background: #fafcfb; box-shadow: 2px 0 5px rgba(0, 0, 0, 0.04);`.
  - `min-width: 160px; max-width: 230px; width: 18%;`.

---

### 6.3 Competitor Matrix Cell (`.matrix-cell`)
```html
<td class="matrix-cell">
  <div class="matrix-price">
    <span>450.00 EGP</span>
    <span class="badge bad" title="Price difference vs. this competitor">+7.1%</span>
  </div>
  <small class="muted"><s>480.00 EGP</s></small>
  <a class="matrix-title" href="https://..." target="_blank" rel="noopener noreferrer" title="Lavazza Oro Beans 250g">
    Lavazza Oro Beans 250g ↗
  </a>
  <small class="muted">2 Nov 2026, 19:30</small>
  <small class="muted">Available</small>
  <div class="matrix-actions">
    <span class="badge good" title="Automated equivalence match">Matched</span>
    <button type="button" class="btn-xs ghost matrix-copy" data-copy-product-url="https://...">Copy link</button>
    <button type="button" class="btn-xs ghost" data-op-matrix="prod_1" data-op-source="coffee-and-cream">View details</button>
  </div>
</td>
```

---

## 7. Tabs Component (`.weekly-tabs`)

```html
<div class="weekly-tabs" role="tablist" aria-label="Weekly actions sections">
  <button type="button" id="actionsTab" role="tab" aria-controls="suggested-actions" aria-selected="true" tabindex="0">
    Suggested actions
  </button>
  <button type="button" id="offersTab" role="tab" aria-controls="competitor-offers" aria-selected="false" tabindex="-1">
    Competitor offers
  </button>
</div>
```

- **Container**: `display: flex; gap: 8px; align-items: center; border-bottom: 1px solid var(--line); padding-bottom: 12px; flex-wrap: wrap; margin-bottom: 20px;`.
- **Inactive Tab**: `min-height: 42px; padding: 9px 16px; border: 1px solid var(--line); border-radius: 7px; background: white; color: var(--ink); font-weight: 600; cursor: pointer;`.
- **Active Tab (`aria-selected="true"`)**: `background: #164f40; border-color: #164f40; color: white;`.
- **Keyboard Navigation**: Left/Right Arrow toggles tab; Home/End jumps to first/last; Enter/Space activates.

---

## 8. Live Progress Banner Component

```html
<div class="progress-banner">
  <div class="progress-header">
    <div>
      <strong style="font-size: 15px; color: var(--action)">Checking Coffee & Cream, CAFELAX…</strong>
      <span class="muted" style="margin-left: 8px">2 / 6 source checks finished · 450 saved observations</span>
    </div>
    <div style="display: flex; gap: 8px; align-items: center">
      <span style="font-size: 16px; font-weight: 700; color: var(--action)">48%</span>
      <button type="button" class="btn-xs ghost" data-cancel-run="run_123">Stop update</button>
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
    <!-- 5 additional source progress cards -->
  </div>
</div>
```

- **Banner Box**: `background: white; border: 1px solid #9bc9b1; border-left: 4px solid var(--action); border-radius: 8px; padding: 16px 20px; margin-bottom: 20px; box-shadow: 0 2px 8px rgba(16, 61, 49, 0.06);`.
- **Track**: `width: 100%; height: 10px; background: #e6ecef; border-radius: 999px; overflow: hidden; margin: 8px 0 12px;`.
- **Fill**: `height: 100%; background: linear-gradient(90deg, #164f40, #2d866c); border-radius: 999px; transition: width 0.35s ease;`.
- **Source Grid**: `display: grid; grid-template-columns: repeat(auto-fit, minmax(175px, 1fr)); gap: 10px; margin-top: 10px;`.
- **Source Card**: `background: #f8faf9; border: 1px solid var(--line); border-radius: 6px; padding: 10px 12px; font-size: 12px;`.

---

## 9. Form Inputs & Filter Controls

- **Standard Inputs, Selects, Textareas**:
  ```css
  select, input, textarea {
    border: 1px solid #cbd1d7;
    border-radius: 5px;
    padding: 10px 11px;
    background: white;
    max-width: 100%;
    color: var(--ink);
    font: inherit;
  }
  ```
- **Checkbox Container (`.check`)**:
  ```css
  .check {
    display: flex;
    align-items: center;
    gap: 10px;
    font-weight: 400;
    cursor: pointer;
  }
  .check input {
    width: 18px;
    height: 18px;
  }
  ```
- **Filter Row (`.filters`)**:
  ```css
  .filters {
    display: flex;
    gap: 12px;
    padding: 16px 22px;
    flex-wrap: wrap;
  }
  .price-comparison-layout .filters {
    display: grid;
    grid-template-columns: 1.35fr 1fr 1fr 1.1fr;
    gap: 16px;
    padding: 20px;
  }
  ```
- **Form Grid (`.form-grid`)**:
  ```css
  .form-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 15px;
  }
  ```

---

## 10. Callouts, Warnings & Notices

- **Notice Banner (`#notice`)**: `padding: 12px 16px; background: #fff3d9; color: #694700; margin-bottom: 18px; border-radius: 5px; font-size: 13px;`.
- **Preflight Callout (`.callout`)**: `border-left: 3px solid #c3a04e; background: #fffaf0; padding: 14px 18px; font-size: 13px; margin-bottom: 22px;`.
- **Operator Warning (`.operator-warning`)**: `background: #fff8e8; color: #70551b; border-left: 3px solid #c3a04e; padding: 12px 14px; margin-bottom: 14px; border-radius: 4px; font-size: 13px;`.

---

## 11. Empty States (`.empty`)

```html
<div class="empty">
  <h3>No products match the current filter</h3>
  <p>Try adjusting your search query, category, or brand filter.</p>
</div>
```

- **Properties**: `padding: 40px 26px; text-align: center; color: var(--muted);`.
- **Title (`h3`)**: `color: var(--ink); font-size: 17px; margin: 0 0 8px; font-weight: 700;`.
- **Body (`p`)**: `max-width: 520px; margin: 10px auto; font-size: 14px; line-height: 1.5;`.
