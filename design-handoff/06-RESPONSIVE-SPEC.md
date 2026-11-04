# Responsive Design Specification — Lazy Bean Market Intelligence

This document specifies the responsive adaptation rules, CSS media queries, layout reflows, and mobile transformation behaviors extracted from `app/static/style.css`.

---

## 1. Breakpoint Architecture & Hierarchy

The dashboard utilizes five deliberate responsive breakpoints:

```
[390px - 520px]       [521px - 720px]       [721px - 900px]       [901px - 1050px]      [1051px - 1440px+]
Small Mobile          Mobile / Phablet      Tablet                Laptop                Desktop
(1-col filters,       (Top nav strip,       (Matrix table         (Sidebar 180px,       (Fixed sidebar 205px,
26px KPI strong)      vert decision rows)   min-width 1000px)     2x2 filters/KPIs)     4-col filters/KPIs)
```

---

## 2. Component Breakpoint Comparison Table

| Component | Desktop (1440px / 1366px) | Laptop (1050px) | Tablet (900px) | Mobile (720px & 390px) |
| :--- | :--- | :--- | :--- | :--- |
| **Sidebar (`aside`)** | Fixed `205px` left, full height | Fixed `180px` left | Fixed `180px` left | **Relative top container**, width `100%`, border-bottom `1px solid var(--line)`. |
| **Brand Identity** | `padding: 0 6px 18px;` | `padding: 0 6px 18px;` | `padding: 0 6px 18px;` | `padding: 0 0 14px;` |
| **Navigation (`nav`)** | Vertical grid, gap `5px`, full width | Vertical grid, gap `5px` | Vertical grid, gap `5px` | **Horizontal scrolling strip**, `overflow-x: auto`, `display: flex`, gap `3px`, height `44px`. |
| **Secondary Nav (`.nav-secondary`)** | Accordion below nav items | Accordion below nav items | Accordion below nav items | Horizontal item in scrolling nav strip; expands inline with `gap: 4px`. |
| **Side Note / Lock** | Visible at bottom of sidebar | Visible at bottom | Visible at bottom | **Hidden** (`display: none`). |
| **Workspace Offset** | `margin-left: 205px; width: calc(100% - 205px);` | `margin-left: 180px; width: calc(100% - 180px);` | `margin-left: 180px;` | `margin: 0; width: 100%;` |
| **Header** | Height `52px`, padding `0 20px` | Height `52px` | Height `52px` | Height `45px`, font-size `11px`, timezone hidden. |
| **Timezone Indicator**| Visible (`Africa/Cairo`) | Visible | Visible | **Hidden** (`display: none`). |
| **Page Heading (`h1`)**| `30px`, inline with Refresh button | `30px` | `30px` | `26px`, flex-start alignment, wraps to vertical. |
| **Refresh Button** | `padding: 9px 15px; font-size: 15px;` | Normal | Normal | `padding: 8px; font-size: 12px;` |
| **Overview KPIs** | 3-column grid (`.operator-summary`) | 3-column grid | 3-column grid | **1-column vertical stack**. |
| **Matrix 4-KPIs** | 4-column grid (`repeat(4, 1fr)`) | **2×2 grid** (`repeat(2, 1fr)`) | 2×2 grid | **1-column stack** (`repeat(1, 1fr)` below 520px). |
| **Matrix Filters** | 4-column grid (`1.35fr 1fr 1fr 1.1fr`) | **2×2 grid** (`repeat(2, 1fr)`) | 2×2 grid | **1-column vertical stack** below 520px. |
| **Matrix Table** | Full width fluid table | Fluid with sticky column | **Enforced `min-width: 1000px`** with horizontal scroll | Scrollable container with sticky first column intact. |
| **Decision Rows** | Horizontal flex row (space-between) | Horizontal flex row | Horizontal flex row | **Vertical flex column**, full-width buttons. |
| **Offer Rows** | Product left, prices right-aligned | Product left, prices right | Product left, prices right | **Vertical flex column**, prices left-aligned (`text-align: left`). |
| **Detail Dialog** | Width `560px`, centered modal | Width `560px` | Width `560px` | Width `calc(100% - 30px)`, `dl` collapses to 1-col. |
| **Toolbar** | Single line flex row | Single line flex row | Wraps as needed | Wraps with gap `7px`, select max-width `230px`. |

---

## 3. Media Query Rules & Implementation Details

### 3.1 Laptop & Medium Screen Breakpoint (`max-width: 1100px`)
Extracted from `app/static/style.css:36`:
```css
@media (max-width: 1100px) {
  .price-comparison-layout .stats-4 {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .price-comparison-layout .stat {
    min-height: 138px;
  }
  .price-comparison-layout .filters {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}
```
- **Rationale**: Prevents filter labels and KPI number values from colliding when the dashboard canvas narrows.

---

### 3.2 Tablet & Narrow Desktop Breakpoint (`max-width: 1050px`)
Extracted from `app/static/style.css:1`:
```css
@media (max-width: 1050px) {
  aside {
    width: 180px;
  }
  .workspace {
    margin-left: 180px;
    width: calc(100% - 180px);
  }
  main {
    padding: 14px 16px;
  }
  .split {
    grid-template-columns: 1fr;
  }
  .stats {
    gap: 10px;
  }
  .stat {
    padding: 12px;
  }
}
```

---

### 3.3 Matrix Scroll Enforcer (`max-width: 900px`)
Extracted from `app/static/style.css:37`:
```css
@media (max-width: 900px) {
  .price-comparison-layout .matrix-table table {
    min-width: 1000px;
  }
}
```
- **Behavior**: Ensures that table columns (7 product columns + 2 summary columns) never become narrower than their minimum legible widths. The user scrolls horizontally, while the first column remains pinned to the left edge with a subtle shadow overlay (`box-shadow: 2px 0 5px rgba(0, 0, 0, 0.04)`).

---

### 3.4 Major Mobile Breakpoint (`max-width: 720px`)
Extracted from `app/static/style.css:1`, `style.css:70`, and `style.css:79-87`:

#### A. Sidebar to Top Bar Transformation
```css
@media (max-width: 720px) {
  aside {
    position: relative;
    width: 100%;
    padding: 16px;
    border-right: 0;
    border-bottom: 1px solid var(--line);
  }
  .brand {
    padding: 0 0 14px;
  }
  nav {
    display: flex;
    overflow-x: auto;
    gap: 3px;
    align-items: center;
  }
  nav button {
    white-space: nowrap;
    min-height: 44px;
    padding: 8px 12px;
    flex: 0 0 auto;
    width: auto;
  }
  .side-note {
    display: none;
  }
  .workspace {
    margin: 0;
    width: 100%;
  }
}
```

#### B. Secondary Nav Strip Accordion
```css
@media (max-width: 720px) {
  .nav-secondary {
    display: flex;
    border: 0;
    margin: 0;
    padding: 0;
    flex: 0 0 auto;
  }
  .nav-secondary summary {
    white-space: nowrap;
    min-height: 44px;
    display: flex;
    align-items: center;
  }
  .nav-secondary[open] {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .nav-secondary[open]::details-content {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .nav-secondary button {
    width: auto;
    white-space: nowrap;
  }
}
```

#### C. Content Reflows & Touch Optimization
```css
@media (max-width: 720px) {
  header {
    padding: 0 20px;
    height: 45px;
    font-size: 11px;
  }
  .timezone {
    display: none;
  }
  main {
    padding: 24px 16px;
  }
  .heading {
    align-items: flex-start;
    flex-direction: column;
    gap: 10px;
  }
  h1 {
    font-size: 26px;
  }
  .heading > button {
    padding: 8px;
    font-size: 12px;
  }
  .stats {
    grid-template-columns: 1fr;
  }
  .stat {
    display: grid;
    grid-template-columns: 1fr auto;
    align-items: center;
  }
  .stat strong {
    grid-column: 2;
    grid-row: 1/3;
    margin: 0;
  }
  .stat small {
    grid-column: 1;
  }
  .toolbar {
    flex-wrap: wrap;
    gap: 7px;
  }
  .toolbar select {
    max-width: 230px;
  }
  .panel-heading {
    padding: 16px;
  }
  .form-grid {
    grid-template-columns: 1fr;
  }
  .operator-summary {
    grid-template-columns: 1fr;
  }
  .decision-row, .offer-row {
    flex-direction: column;
    gap: 12px;
  }
  .offer-prices {
    text-align: left;
    min-width: 0;
  }
  .operator-detail dl {
    grid-template-columns: 1fr;
  }
  footer {
    padding: 0 16px 20px;
  }
}
```

---

### 3.5 Small Mobile Breakpoint (`max-width: 520px`)
Extracted from `app/static/style.css:38`:
```css
@media (max-width: 520px) {
  .price-comparison-layout .filters {
    grid-template-columns: 1fr;
  }
  .price-comparison-layout .stats-4 {
    gap: 10px;
  }
  .price-comparison-layout .stat {
    padding: 14px;
    min-height: 145px;
  }
  .price-comparison-layout .stat > strong {
    font-size: 26px;
  }
}
```
- **Rationale**: Complete single-column stack on smartphones (e.g. iPhone 14/15/16 at 390px width). Every filter input becomes full width (`width: 100%`).
