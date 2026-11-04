# Routes, Deep Linking & Navigation Specification — Lazy Bean Market Intelligence

This document defines the client-side routing model, hash-based URL structure, parameter serialization, aliases, redirects, and browser history synchronization extracted from `app/static/app.js` and `app/static/operator.js`.

---

## 1. Routing Model & Structure

The dashboard uses a hash-based Single Page Application (SPA) routing model with the pattern:
```
#/{slug}[/{subParam}][?snapshot={snapshotId}&section={sectionName}]
```

### 1.1 Route Mapping Table (`routeMap`)
Extracted from `app.js:4-16`:

| URL Slug / Pattern | Target View | Deep Link Parameter | Controlling Script | Aliases / Redirect Notes |
| :--- | :--- | :--- | :--- | :--- |
| `#/overview` | **Overview** | `?snapshot={id}` | `operator.js` | Main default landing view. |
| `#/price-comparisons` | **Price comparisons** | `?snapshot={id}` | `operations.js` | Full comparison matrix. |
| `#/weekly-actions` | **Weekly actions** | `?snapshot={id}` | `operator.js` | Defaults to "Suggested actions" tab. |
| `#/weekly-actions?section=offers` | **Weekly actions** | `section=offers` | `operator.js` | Deep links to "Competitor offers" tab. |
| `#/promotions` | **Weekly actions** | Redirect | `app.js:72` | Legacy alias. Auto-rewrites URL to `#/weekly-actions?section=offers` via `replaceState`. |
| `#/ads-campaigns` | **Ads & campaigns** | `?snapshot={id}` | `operator.js` | Observed competitor ads & platform coverage. |
| `#/ads` | **Ads & campaigns** | Alias | `app.js:11` | Shorthand alias for `ads-campaigns`. |
| `#/review-queue` | **Review queue** | `?snapshot={id}` | `operator.js` | Product matches & identity reviews. |
| `#/catalogue-evidence` | **Review queue** | Redirect | `app.js:72` | Legacy alias. Auto-rewrites URL to `#/review-queue` and surfaces warning banner. |
| `#/sources` | **Sources** | `?snapshot={id}` | `operator.js` | Store registry & config editor. |
| `#/history` | **History (Archive)**| None | `operator.js` / `operations.js` | Historical collection timeline & price log table. |
| `#/history?snapshot={id}` | **Historical Matrix**| `snapshot={id}` | `operations.js` | Historical Price Comparisons Matrix locked to snapshot. |
| `#/history/{id}` | **Historical Matrix**| Path segment | `app.js:46` | Path parameter fallback; parsed as snapshot ID. |
| `#/improve-recommendations`| **Improve recommendations**| None | `operator.js` | Internal data CSV/XLSX import. |

---

## 2. Route Parsing & Resolution Logic

Extracted from `parseCurrentHash()` in `app.js:30-49`:

```javascript
function parseCurrentHash() {
  if (typeof window === 'undefined' || !window.location) {
    return { view: activeView, slug: viewToSlug[activeView] || 'overview', snapshot: null };
  }
  const hash = (window.location.hash || '').replace(/^#/, '').trim();
  if (!hash) return { view: null, slug: null, snapshot: null };
  
  const [pathPart, queryPart] = hash.split('?');
  const segments = pathPart.split('/').filter(Boolean);
  const slug = (segments[0] || '').toLowerCase();
  const subParam = segments[1] || null;
  
  let snapshot = null, section = null;
  if (queryPart) {
    try {
      const u = new URL('http://local/?' + queryPart);
      snapshot = u.searchParams.get('snapshot') || null;
      section = u.searchParams.get('section') === 'offers' ? 'offers' : null;
    } catch (e) {}
  }
  if (!snapshot && subParam) snapshot = subParam;
  const view = routeMap[slug] || null;
  return { view, slug, snapshot, section };
}
```

---

## 3. History Synchronization & Browser Back/Forward

### 3.1 Route State Updates (`setRoute`)
Extracted from `app.js:51-68`:
```javascript
function setRoute(view, params = {}, options = {}) {
  activeView = view;
  if (typeof window !== 'undefined' && window.location) {
    const slug = viewToSlug[view] || 'overview';
    const qs = [];
    if (params.snapshot) qs.push('snapshot=' + encodeURIComponent(params.snapshot));
    if (params.section === 'offers') qs.push('section=offers');
    const qStr = qs.length ? '?' + qs.join('&') : '';
    const targetHash = `#/${slug}${qStr}`;
    if (window.location.hash !== targetHash) {
      if (options.replace && window.history && window.history.replaceState) {
        window.history.replaceState(null, '', targetHash);
      } else {
        window.location.hash = targetHash;
      }
    }
  }
}
```

---

### 3.2 Browser History Listeners
The application binds to both `hashchange` and `popstate` events:
```javascript
if (typeof window !== 'undefined' && window.addEventListener) {
  window.addEventListener('hashchange', () => perform(handleRoute));
  window.addEventListener('popstate', () => perform(handleRoute));
}
```
- **Forward / Backward Navigation**: Pressing the browser's Back button correctly invokes `handleRoute()`, updates `#runSelect`, synchronization state, and executes `render()`.
- **Replacing State on Aliases**: Routes like `#/promotions` and `#/catalogue-evidence` replace the history entry via `replaceState` so that Back button does not trigger an infinite redirect loop.
