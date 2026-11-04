# Screen Inventory Specification — Lazy Bean Market Intelligence

This document provides complete, framework-agnostic specifications for all views, screens, sub-views, and modal states in the Lazy Bean Competitor Intelligence Dashboard.

---

## 1. Screen Architecture & Routing Map

| View Name | Internal Slug | Route / Deep Link | Controlling Script | Primary Content Pattern |
| :--- | :--- | :--- | :--- | :--- |
| **Overview** | `overview` | `#/overview?snapshot={id}` | `operator.js` + `operations.js` | Operator summary KPIs, decision rows, coverage tables, integration cards. |
| **Price Comparisons** | `price-comparisons` | `#/price-comparisons?snapshot={id}` | `operations.js` | 4 KPI cards, 4 filters, sticky multi-column matrix table, pagination. |
| **Weekly Actions (Suggested)** | `weekly-actions` | `#/weekly-actions?snapshot={id}` | `operator.js` | 2-tab layout, suggested decision rows, math explanation modal. |
| **Weekly Actions (Offers)** | `weekly-actions` (or `promotions`) | `#/weekly-actions?snapshot={id}&section=offers` | `operator.js` | Filterable competitor offer cards, advertised discount tags. |
| **Ads & Campaigns** | `ads-campaigns` (or `ads`) | `#/ads-campaigns?snapshot={id}` | `operator.js` + `operations.js` | Observed ad cards, platform coverage table, Meta API settings, ad refresh. |
| **Review Queue** | `review-queue` (or `catalogue-evidence`) | `#/review-queue?snapshot={id}` | `operator.js` + `operations.js` | Match decision rows, attribute diffs, advertiser approvals, page extractions. |
| **Sources** | `sources` | `#/sources?snapshot={id}` | `operator.js` + `operations.js` | Monitored domains, sample validator, config editor, add store form. |
| **History (Archive Log)** | `history` | `#/history` | `operator.js` or `operations.js` | Snapshot timeline, historical price records table, jump-to-date dropdown. |
| **Historical Price Matrix** | `history` (with snapshot) | `#/history?snapshot={id}` | `operations.js` | Full price matrix with snapshot switch bar and "Back to Snapshots" control. |
| **Improve Recommendations** | `improve-recommendations`| `#/improve-recommendations` | `operator.js` | Internal CSV / XLSX upload, preview validation, commit flow. |

---

## 2. Detailed Screen Specifications

### Screen A: Overview

```
+----------------------------------------------------------------------------------------------------+
| Report updated 2 Nov 2026, 19:30.                                                                   |
| [!] Warning: Some information is incomplete or may be older. Check the source date before acting.   |
|                                                                                                    |
| +--------------------------------+ +--------------------------------+ +--------------------------+ |
| | 3                              | | 121                            | | 3                        | |
| | Suggested actions              | | Supported price comparisons    | | Product matches to review| |
| +--------------------------------+ +--------------------------------+ +--------------------------+ |
|                                                                                                    |
| Changes at competitors                                                                             |
| +------------------------------------------------------------------------------------------------+ |
| | Lavazza Crema e Gusto 250g Ground Coffee                                                       | |
| | CAFELAX · Price changed in the saved report                                                     | |
| +------------------------------------------------------------------------------------------------+ |
|                                                                                                    |
| What needs attention?                                                                              |
| +--------------------------------------------------------------------+ +-------------------------+ |
| | Illy Decaffeinato Whole Beans 250g                                 | | [View actions]          | |
| | Review this price · Observed competitor prices support a closer look | +-------------------------+ |
| +--------------------------------------------------------------------+                             |
|                                                                                                    |
| [Integration Readiness Panels: Storage, Apify, Cash Ceiling, Provider Quotas, Meta API, BrightData] |
+----------------------------------------------------------------------------------------------------+
```

- **Header Text**:
  - Title: `Overview`
  - Subtitle: `See what needs attention this week.`
- **KPI Metrics (Operator Summary)**:
  - Card 1: `rec.length` -> **Suggested actions** (clicking navigates to `Weekly actions`).
  - Card 2: `comparisons.length` -> **Supported price comparisons** (clicking navigates to `Price comparisons`).
  - Card 3: `matches.length` -> **Product matches to review** (clicking navigates to `Review queue`).
- **Section 1: Changes at competitors (`h2`)**:
  - Displays top 3 changes from `report.changes`: title, competitor name, and change kind (`Price changed in the saved report` or `Newly observed in the checked pages`).
  - Empty state: `No supported changes recorded. Comparable evidence is needed before describing a change.`.
- **Section 2: What needs attention? (`h2`)**:
  - Displays top 3 action items: title, action label (`Review this price` / `Check stock before advertising`), and reason. Action button: `[View actions]`.
- **Appended Operations Panels**:
  - **Integration Readiness Panel**: Storage status, Apify token status, zero-dollar external cash ceiling notice, provider quota table (Apify, Firecrawl, Bright Data headroom and run caps).
  - **Official Meta Ads Library API Panel**: Credential configuration, token state badge, last check date, and token update form.
  - **Bright Data Account Checks & Source Routes Table**: Monitored source routing mapping.

---

### Screen B: Price Comparisons (Complete Matrix)

```
+----------------------------------------------------------------------------------------------------+
| [4 KPI Cards: LAZY BEAN CATALOG (1,251) | MATCHED PRODUCTS (121) | COMPETITOR PRODUCTS | BRANDS]    |
|                                                                                                    |
| Lazy Bean vs. Competitors — Complete Price Comparison Matrix                [⬇ Export Matrix CSV]   |
| +------------------------------------------------------------------------------------------------+ |
| | Search Product or Variant | Category (All) | Brand (All) | Show Products (All / Matched Only)  | |
| +------------------------------------------------------------------------------------------------+ |
| Showing 1–50 of 1,251 products                            [‹ Previous] Page 1 / 26 [Next ›] [100]  |
| +------------------------------------------------------------------------------------------------+ |
| | Product (Sticky)   | Lazy Bean (Own)  | Coffee & Cream | CAFELAX   | Fengany   | Median | Gap  | |
| |--------------------+------------------+----------------+-----------+-----------+--------+------| |
| | Lavazza Oro 250g   | 420.00 EGP       | 450.00 EGP     | 410.00 EGP| 430.00 EGP| 430.00 | -2.3%| |
| | Beans · LAVAZZA    | [Available]      | [+7.1%]        | [-2.4%]   | [+2.4%]   | EGP    | [good| |
| | [Copy] [Details]   | Obs: 2 Nov 19:30 | Matched [Copy] | Matched   | Matched   | 3 store|      | |
+----------------------------------------------------------------------------------------------------+
```

- **Header Text**:
  - Title: `Price comparisons`
  - Subtitle: `Compare saved prices and check the source before acting.`
- **Top Metric Cards (`.stats-4`)**:
  1. `LAZY BEAN CATALOG`: Count of total own products (`1,251`).
  2. `MATCHED PRODUCTS`: Count of verified equivalent products (`121`).
  3. `COMPETITOR PRODUCTS`: Sum of observed items across all 5 competitor stores.
  4. `BRANDS & CATEGORIES`: Count of unique brands and product categories.
- **Filters Bar (`.filters`)**:
  - Search Product: `#matrixSearch` (debounced 100ms).
  - Category: `#matrixCategory` (populated with distinct category names).
  - Brand: `#matrixBrand` (populated with distinct brands).
  - Show Products: `#matrixFilter` (`All Lazy Bean Products` vs. `Matched Products Only`).
- **Pagination Bar (`#matrixPagerTop`)**:
  - Showing counter: `Showing 1–50 of 1,251 products`.
  - Paging controls: `‹ Previous`, `Page 1 / 26`, `Next ›`.
  - Page size toggle: `Show 100 per page` / `Show 50 per page`.
- **Matrix Table Columns**:
  1. `Lazy Bean Product` (18% width, sticky left): Title link with `↗`, meta tags (Category, Brand, Size, Seller), `[Copy link]` button, `[View details]` button.
  2. `Lazy Bean Price` (9.5% width, background `#f4f9f6`): Price in EGP, strikethrough reference price, availability badge, observation date.
  3..7. `Competitor Columns` (11% width each, 5 stores): Price in EGP, price difference badge (`+X%` / `-X%`), strikethrough reference price, competitor title link, observation date, availability badge, match status badge (`Matched`, `Manually confirmed`, `Suggested`), `[Copy link]`, `[View details]`.
  8. `Market median` (7.5% width, numeric right-aligned): Median price strong, store count text.
  9. `Price gap` (6.5% width, numeric right-aligned): Color-coded gap percentage badge (`.badge.good` for negative/lower price, `.badge.bad` for higher price).

---

### Screen C: Weekly Actions

The Weekly Actions view features a top tab switcher between **Suggested actions** and **Competitor offers**:

#### Tab 1: Suggested Actions (`#actionsTab`)
- **Header Text**:
  - Title: `Weekly actions`
  - Subtitle: `Review suggested decisions and offers found at competitors.`
- **Section Heading**: `Suggested actions` + `<p class="muted">Review the evidence before making a decision. Advertising tests do not predict sales.</p>`.
- **Decision Rows (`.decision-row`)**:
  - Product title (`h3`).
  - Action label strong: `Review this price`, `Check stock before advertising`, or `Consider a small advertising test`.
  - Reasoning text.
  - Metadata: Store count compared, price gap percentage, stock condition rule.
  - Action button: `<button type="button" data-op-rec="{index}">Why this suggestion?</button>` (opens the Price Gap Math Explanation modal).
- **Expansion Action**: `<button type="button" id="actionMore">Show more actions</button>` (reveals 5 more).

#### Tab 2: Competitor Offers (`#offersTab`)
- **Header Text**: Same shell heading with active tab `Competitor offers`.
- **Section Heading**: `Competitor offers` + `<p class="muted">Offers found in the checked pages. Reference prices are the stores’ advertised comparison prices, not proven earlier prices.</p>`.
- **Filters**:
  - Competitor select (`#offerSource`).
  - Offer type select (`#offerType`): e.g. `clearance`, `bundle`, `discount`.
- **Counter**: `<p id="offerCount" class="muted">Showing X of Y offers found in the checked pages.</p>`.
- **Offer Rows (`.offer-row`)**:
  - Left column: Store name, scope, product title, offer type and terms, availability, observation date, matched own product, `[View source details]` link button, `[Open source]` link.
  - Right column (`.offer-prices`): Recorded price strong, advertised reference price, advertised discount percentage tag.
- **Expansion Action**: `<button type="button" id="offerMore">Show more offers</button>` (reveals 10 more).

---

### Screen D: Ads & Campaigns

- **Header Text**:
  - Title: `Ads & campaigns`
  - Subtitle: `See the competitor ads observed in this report.`
- **Section Heading**: `Observed competitor ads` + `<p class="muted">Ad presence does not establish sales, spending or success. Some platforms or accounts may not have been checked.</p>`.
- **Observed Ad Cards (`.decision-row`)**:
  - Competitor name and Platform (`Meta` or `Google`).
  - Ad copy headline or body text.
  - Metadata: Observation date, reported active status (`active` / `not active` / `not confirmed`).
  - External link: `[View ad source]`.
- **Additional Panels (Operations)**:
  - **Refresh Paid-Ad Evidence Form**: Select competitor, select platform, `[Collect up to 5 ads per identity]` button.
  - **Current Advertiser Review Table**: Competitor/platform, identity status, public library evidence links, `[Review {id}]` button.
  - **Provider Samples Table**: Real provider evidence samples awaiting identity approval.
  - **Meta Graph API Access Token Form**: Password input to save and verify token in `data/META_ACCESS_TOKEN.env.txt`.

---

### Screen E: Review Queue

- **Header Text**:
  - Title: `Reviews needing attention` (badge count in nav)
  - Subtitle: `Confirm product matches and advertising accounts.`
- **Redirect Warning**: Shown if user navigated via `#catalogue-evidence`: `<div class="operator-warning">Catalogue evidence is now available through Price comparisons and product reviews below.</div>`.
- **Section 1: Are these the same product? (`h2`)**:
  - Subtext: `These are current reviews, independent of the selected report date. Compare the product, weight, pack count and capsule system before confirming.`.
  - Item Cards (`.decision-row`):
    - Left column: Lazy Bean product title, Competitor store & title, match basis explanation, attribute diff string (`Our details: ... | Competitor details: ...`), `[View source details]` button.
    - Right form (`form[data-match]`): Notes input (`What did you verify?`, minlength 10), `[Confirm same product]` (`value="approve"`), `[Not the same]` (`value="reject"`).
  - Expansion: `[Show more product reviews]` (+10).
- **Section 2: Offers needing a check (`h2`)**:
  - Ambiguous offer listings requiring manual condition verification. Action link: `[Check the offer terms]`.
- **Section 3: Confirm advertising accounts (`h2`)**:
  - Unapproved advertiser identity candidates: Platform profile link, business website link, `[Review account]` button.
- **Section 4: Extraction issues (Operations)**:
  - Table of failed, blocked, or quarantined page URLs with error reasons.

---

### Screen F: Sources (Manage Sources)

- **Header Text**:
  - Title: `Manage sources`
  - Subtitle: `Manage and validate the stores you monitor.`
- **Store Cards (`.decision-row`)**:
  - Store name (`h3`), store URL link (`[Visit store]`), monitoring status text (`Monitoring enabled` / `Monitoring disabled`).
  - Validation note: `Sample confirmed` or `Sample needs checking before monitoring can be enabled.`.
  - Actions: `<button type="button" data-sample="{id}">Review sample</button>` + `<button type="button" data-toggle="{id}">Enable/Disable monitoring</button>`.
- **Accordion 1: Update store collection settings (`<details>`)**:
  - Form `#configForm`: Store selector, Store format adapter (`Commerce JSON-LD`, `WooCommerce`, `Lazy Bean`), Pages to check textarea (one entry URL per line), Reason for change input (`minlength="10"`), `[Save settings for review]` button.
- **Accordion 2: Add a store for review (`<details>`)**:
  - Form `#sourceForm`: Store name input, website origin URL, entry points textarea, `[Add store]` button. Automatically generates kebab-case slug ID from store name.
- **Bright Data Staged Pilot Form**:
  - Form `#brightdataPilotForm`: Test one bounded source (up to 3 pages / 5 observations) without full collection.

---

### Screen G: History & Historical Archive

The History view supports two distinct states depending on route parameters:

#### State 1: Timeline & Price Archive Log (`#/history`)
- **4 Top KPI Cards (`.stats-4`)**:
  1. `ARCHIVED SNAPSHOTS`: Total runs recorded.
  2. `TOTAL PRICE RECORDS`: Total historical price observations.
  3. `LAZY BEAN PRICE LOGS`: Count of historical Lazy Bean records.
  4. `COMPETITOR PRICE LOGS`: Count of historical competitor records.
- **Quick Jump Bar**: Dropdown to immediately open price comparisons for any historical date.
- **Archived Snapshots Table**:
  - Columns: Collected / Finished date, Snapshot ID, State, Lazy Bean Items, Competitor Items, Total Prices, Actions (`[View Price Comparisons ↗]`, `[Open Overview]`, `[⬇ Matrix CSV]`).
- **Complete Historical Price Log Table**:
  - Filters: Search Product/SKU, Store select, Brand select, Snapshot date select.
  - Columns: Observed Date & Time, Snapshot ID, Store name, Product & Specifications, Recorded Price (EGP), Previous Price / Change badge, Availability badge.
  - Pagination: 200 items per page with `Show All` toggle. Action: `[⬇ Export Full Price History CSV]`.

#### State 2: Historical Price Comparisons Matrix (`#/history?snapshot={id}`)
- Displays the complete multi-column Price Comparison Matrix locked to the specified historical snapshot.
- Top Bar: `[‹ Back to All Snapshots]` button, `Archived Historical Snapshot` warning badge, snapshot date, state badge, snapshot switcher dropdown, and snapshot CSV export link.

---

### Screen H: Improve Recommendations (Data Import)

- **Header Text**:
  - Title: `Internal data import`
  - Subtitle: `Preview optional internal business data.`
- **File Upload Form (`#importForm`)**:
  - Subtext: `Optional: preview a CSV or spreadsheet before adding internal data. Use explicit canonical product_id, period_start, period_end. Optional fields: stock_quantity, unit_cost, selling_price, units_sold, returns, revenue, ad_spend. Money is in EGP.`.
  - File input: `<input type="file" name="file" accept=".csv,.xlsx" required>`.
  - Submit Action: `<button class="primary">Preview file</button>`.
- **Validation Preview Box (`#importPreview`)**:
  - Renders validation status: "File ready for review" vs. errors.
  - If valid: Renders `<button type="button" data-commit="{import_id}">Commit validated import</button>`.
