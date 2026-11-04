# Interaction Specification — Lazy Bean Market Intelligence

This document provides exact, step-by-step interaction specifications extracted from `app.js`, `operations.js`, and `operator.js`, detailing triggers, initial states, user actions, UI transitions, API requests, error handling, and state consequences.

---

## 1. Global Shell & Navigation Interactions

### 1.1 Sidebar Collapse / Expand
- **Trigger**: User clicks `#sidebarToggle` button in the header (`☰`).
- **Initial State**: Sidebar visible at left (`margin-left: 205px` on workspace).
- **DOM Action**: `document.body.classList.toggle('sidebar-collapsed')`.
- **CSS Transition**:
  - `aside`: `transform: translateX(-100%);` (smooth `0.2s ease` transition).
  - `.workspace`: `margin-left: 0; width: 100%;` (smooth `0.2s ease` transition).
- **Secondary Click**: Removes `.sidebar-collapsed` class; restores sidebar to `205px` width.
- **Persistence**: Not persisted in localStorage (defaults to open on fresh session).

---

### 1.2 Route Navigation & Deep Linking
- **Trigger**: User clicks any navigation item with `data-view="{view}"` and `data-route="{hash}"`, or clicks a KPI summary card.
- **Handler**: `setRoute(view, params)` in `app.js:51`.
- **UI Response**:
  - Immediate view blanking: `$('view').innerHTML = empty('Loading ' + view, '')`.
  - Sets `activeView = view`.
  - URL hash updated: `#/overview`, `#/price-comparisons`, `#/weekly-actions`, etc.
  - Active button in `<nav>` gets `aria-current="page"`.
  - `<details class="nav-secondary">` auto-opens if navigating to `Sources`, `History`, or `Improve recommendations`.
  - Browser history state pushed (`history.pushState` or hash update).

---

### 1.3 Snapshot Selector (`#runSelect`)
- **Trigger**: User selects a different date in the `#runSelect` dropdown.
- **Handler**: `selectSnapshot(runId)` in `app.js:134`.
- **Request**: `GET /api/runs/{run_id}`.
- **UI Response**:
  - `currentRun` updated in memory.
  - `#runStatus` badge updated with snapshot's state.
  - URL query updated: `#/{slug}?snapshot={run_id}`.
  - Active screen re-rendered with the new snapshot's data.
- **Failure Fallback**: If selected run has state `failed` and contains `last_successful_run_id`, the system automatically loads the last successful run, updates the selector, and surfaces the warning: `"The latest update did not finish. You are viewing an earlier saved report; check its dates before acting."`.

---

### 1.4 Notification Callouts (`#notice`)
- **Trigger**: System functions call `notice(text)`.
- **UI Transition**:
  - Sets `$('notice').textContent = text;`.
  - Removes `hidden` attribute from `#notice`.
  - Displays yellow warning bar (`background: #fff3d9; color: #694700;`).
- **Clearing**: Automatically cleared at the beginning of any `perform(fn)` execution block.

---

## 2. Collection & Lifecycle Interactions

### 2.1 Refresh Sources Flow & Preflight Check
- **Trigger**: User clicks `#refresh` ("Refresh sources") in the page heading.
- **UI Response**:
  - `#freeCreditsConfirmed.checked` reset to `false`.
  - `#runDialog.showModal()` called (native HTML dialog).
  - Triggers asynchronous `checkFreshCollection()`.
- **Preflight Check (`checkFreshCollection`)**:
  - `#startCollection` button disabled immediately.
  - Fetches `GET /api/runs` to ensure no job is currently `running` or `queued`.
  - If a job is active: surfaces notice: `"An update is already collecting/queued... It will finish before another update starts."` and renders `[View current update]` button.
  - If queue is clear: fetches `GET /api/collection/preflight?pilot={bool}&source_id={id}`.
  - If `preflight.ready === true`: hides warning callout; enables `#startCollection`.
  - If `preflight.ready === false`: surfaces warning message; if `state === 'budget_unverified'`, exposes `#freeCreditConfirmation` container.

---

### 2.2 Bright Data Free Credit Confirmation
- **Trigger**: User enters actual free credit counter from Bright Data billing and clicks `#confirmFreeCredits`.
- **Validation**: Requires `#freeCreditsConfirmed` checked, counter integer between `0` and `5000`.
- **API Request**:
  - `POST /api/brightdata/entitlement`
  - Body: `{remaining: int, total: 5000, cycle_start: str, cycle_end: str, notes: str}`
- **UI Response**:
  - Disables confirm button during request.
  - On success: re-runs `checkFreshCollection()`; unblocks `#startCollection`.

---

### 2.3 Starting a Collection
- **Trigger**: User clicks `#startCollection` in `#runDialog`.
- **API Request**:
  - `POST /api/runs`
  - Body: `{source_ids: [id] | null, pilot: boolean, draft: boolean}`
- **UI Response**:
  - Button text changes to `"Starting…"`, disabled.
  - Adds new run to top of `runList`.
  - Closes `#runDialog`.
  - Updates `#runSelect` value and selects the new snapshot.
  - Surfaces notification: `"Update queued. The local server processes one collection at a time; this page will show the update currently in progress."` (or draft notice if draft was checked).

---

### 2.4 Active Run Polling & Dynamic Progress
- **Interval**: 3,000ms loop via `setInterval` in `app.js:211`.
- **Condition**: Executes only if `token || localPreview`, `currentRun` exists, and state is in `['queued', 'running', 'cancelling']`.
- **API Request**: `GET /api/runs/{currentRun.id}`.
- **UI Response (`renderLiveProgressBanner`)**:
  - Computes progress percentage: `computeRunProgress(run)`.
  - Updates progress bar fill (`.progress-fill` width).
  - Updates source progress cards (`.progress-card`) with accepted products and batch counts.
  - Shows current step label: `"Checking {Source1, Source2}…"` or `"Finalizing price comparisons…"`.
- **Completion Transition**:
  - When state transitions to `completed` or `partial`:
  - Re-fetches run list (`GET /api/runs`).
  - Clears live progress banner.
  - Re-renders current view with fresh price observations.
  - Displays green completion notice: `"Collection finished (100%). Displayed prices have been updated and previous snapshots are archived in History."`.

---

### 2.5 Cancelling an Active Run
- **Trigger**: User clicks `[data-cancel-run]` in the progress banner.
- **UI Response**: Button disabled, text changes to `"Stopping…"`.
- **API Request**: `POST /api/runs/{id}/cancel`.
- **Result**: State changes to `cancelling` or `cancelled`. Banner updates label to `"Stopping this update…"`.

---

## 3. Price Comparison Matrix Interactions

### 3.1 Live Filtering & Debounced Search
- **Search Input (`#matrixSearch`)**:
  - User types query (e.g. "Oro", "Decaf", "250g").
  - Debounced by `100ms` via `setTimeout`.
  - Filters across product title, brand, variant subline, and all competitor listing titles.
  - Resets pagination to `Page 1`.
- **Category Filter (`#matrixCategory`)**:
  - User selects category dropdown.
  - Filter applied immediately; resets to `Page 1`.
- **Brand Filter (`#matrixBrand`)**:
  - User selects brand dropdown.
  - Filter applied immediately; resets to `Page 1`.
- **Show Filter (`#matrixFilter`)**:
  - Options: `All Lazy Bean Products (N)` vs. `Matched Products Only (N)`.
  - Filters rows where `matched_count > 0`.
- **Empty Filter State**:
  - If no products match: clears pager, displays `.empty` state: `"No products match the current filter. Try adjusting your search query, category, or brand filter."`.

---

### 3.2 Pagination & Page Size Toggle
- **Page Size**: Defaults to `50` products per page.
- **Previous / Next (`#matrixPrev`, `#matrixNext`)**:
  - Decrements/increments `currentPage`.
  - Disabled at boundaries (`currentPage <= 1` or `currentPage >= totalPages`).
- **Page Size Toggle (`#matrixToggleAll`)**:
  - Toggles between `50 per page` and `100 per page`.
  - Label changes dynamically: `"Show 100 per page"` vs. `"Show 50 per page"`.

---

### 3.3 One-Click URL Copying
- **Trigger**: User clicks `[data-copy-product-url]` button on any product cell.
- **Action**: Copies sanitized HTTPS product URL to system clipboard via `navigator.clipboard.writeText(url)`.
- **UI Response**:
  - Button text changes from `"Copy link"` to `"Copied"`.
  - Notice bar displays `"Product link copied."`.
  - After `1,800ms`: button text safely reverts to `"Copy link"`.

---

### 3.4 Product Evidence Detail Modal
- **Trigger**: User clicks `[data-op-matrix]` on Lazy Bean or competitor cell.
- **UI Response**:
  - Extracts row attributes (`Brand`, `Net weight`, `Capsule count`, `Capsule system`, `Model`).
  - Renders definition list `<dl>` inside `#detailDialog`.
  - Renders advertised reference price, observed date, and verified availability.
  - Calls `$('detailDialog').showModal()`.

---

### 3.5 Manual Product Linker & Approval
- **Trigger**: User clicks `[data-matrix-link]` on an unlinked competitor cell.
- **UI Response**:
  - Opens `#detailDialog` hosting `#matrixLinkForm`.
  - Shows competitor catalog search input (`#matrixLinkSearch`) and select list of observed products.
  - User types to search competitor products; select list filters in real time.
- **Submission**:
  - User selects competitor product and submits form.
  - API Request: `POST /api/matches` with `{product_id, observation_id, approve: true, notes: 'Manually linked and approved from price comparison matrix'}`.
  - Closes dialog, re-renders matrix with new match badge, displays notice: `"Product linked and price comparison approved."`.

---

## 4. Weekly Actions & Offer Interactions

### 4.1 Tab Switching & Keyboard Accessibility
- **Tabs**: `Suggested actions` (`#actionsTab`) and `Competitor offers` (`#offersTab`).
- **Click Trigger**: Clicking tab switches active panel and updates URL hash:
  - Suggested actions -> `#/weekly-actions?snapshot={id}`
  - Competitor offers -> `#/weekly-actions?snapshot={id}&section=offers`
- **Keyboard Navigation**:
  - `ArrowLeft` / `ArrowRight`: Toggles between actions and offers.
  - `Home`: Focuses and selects actions tab.
  - `End`: Focuses and selects offers tab.
  - Sets `aria-selected="true"`, updates `tabindex="0"`, toggles `hidden` on panels.

---

### 4.2 "Why this suggestion?" Math Explanation Modal
- **Trigger**: User clicks `[data-op-rec="{index}"]` on any suggested action card.
- **UI Response**:
  - Computes exact mathematical delta: `(Lazy Bean − Median) ÷ Median × 100 = Gap%`.
  - Calculates difference in EGP (`money(Math.abs(own - median))`).
  - Identifies whether store count is 1 or multi-store median.
  - Renders breakdown in `#detailDialog`:
    ```
    Price gap: -12.5%
    (420.00 EGP − 480.00 EGP) ÷ 480.00 EGP × 100 = -12.5%.
    Lazy Bean is 60.00 EGP lower than the median competitor price.
    ```
  - Lists individual competitor prices, observation dates, and direct links.
  - Opens `#detailDialog.showModal()`.

---

### 4.3 Competitor Offer Filtering & Expansion
- **Filters**: Competitor dropdown (`#offerSource`) and Offer Type dropdown (`#offerType`).
- **Interaction**: Changing either dropdown immediately re-filters `operatorOffers`.
- **Counter**: Updates `#offerCount`: `"Showing X of Y offers found in the checked pages."`.
- **Expansion (`#offerMore`)**: Clicking increments limit by 10 (`dataset.limit = String(Number(limit) + 10)`).

---

## 5. Review Queue Interactions

### 5.1 Equivalence Match Decision (Approve / Reject)
- **Trigger**: User enters verification notes (`minlength="10"`) and clicks `[Confirm same product]` or `[Not the same]`.
- **Form**: `form[data-match="{id}"]`.
- **API Request**:
  - `POST /api/matches/{match_id}/decision`
  - Body: `{notes: string, decision: 'approve' | 'reject'}`
- **UI Response**:
  - Clears review cache (`reviewCache.clear()`).
  - Re-renders review queue; approved item is removed from queue.
  - Navigation badge count updates immediately (`Reviews needing attention (N)`).

---

### 5.2 Advertiser Account Ownership Approval
- **Trigger**: User clicks `[Review account]` on unapproved advertiser candidate.
- **UI Response**: Opens `#detailDialog` pre-filled with candidate advertiser ID, display name, library URL, and ownership URL.
- **User Action**: Enters verification notes and clicks `[Confirm account]`.
- **API Request**:
  - `POST /api/identities/{identity_id}/approve`
  - Body: `{advertiser_id, display_name, library_url, ownership_evidence_url, notes}`
- **UI Response**: Closes modal, re-renders Review Queue, shows notice: `"Identity approved for future snapshots. Use the ads-only refresh to collect."`.

---

## 6. Sources & Historical Archive Interactions

### 6.1 Enable / Disable Monitoring Toggle
- **Trigger**: User clicks `[data-toggle="{source_id}"]`.
- **API Request**:
  - `PATCH /api/sources/{source_id}`
  - Body: `{enabled: boolean}`
- **UI Response**: Reloads sources (`load()`), re-renders list with updated monitoring status text and toggled button label.

---

### 6.2 Pilot Validation Sample Review
- **Trigger**: User clicks `[data-sample="{source_id}"]`.
- **API Request**: `GET /api/sources/{source_id}/validation-sample?run_id={id}`.
- **UI Response**: Opens `#detailDialog` with 10 real products from the snapshot.
- **User Action**: Checks reviewed product checkboxes, checks robots/access checkbox, enters notes (`minlength="10"`), and clicks `[Approve verified source sample]`.
- **Submit Request**: `POST /api/sources/{source_id}/validate` with `{run_id, verified_observation_ids: [...], robots_checked: true, notes: '...'}`.
- **Result**: Closes dialog, reloads sources, updates validation badge to `Validated`.

---

### 6.3 Historical Matrix Navigation
- **Trigger**: In History view, user clicks `[data-history-matrix="{run_id}"]` or selects a date in `#historyQuickRunSelect`.
- **UI Response**:
  - Sets `historySelectedSnapshotId = run_id`.
  - Sets URL hash: `#/history?snapshot={run_id}`.
  - Calls `renderHistoricalPriceMatrix(run_id)`.
  - Renders full matrix with top historical switcher bar.
- **Return Action**: User clicks `[‹ Back to All Snapshots]` (`#historyMatrixBack`):
  - Clears `historySelectedSnapshotId = null`.
  - Sets URL hash: `#/history`.
  - Restores History archive timeline.
