# Fixtures

- `shopify_coffeezonemisr_page1.json` - **genuine live capture**, one real request to
  `https://coffeezonemisr.com/products.json?limit=30&page=1` on 2026-11-03, User-Agent
  `LazyBean-Collector/1.0`. Used to build `ShopifyAdapter` and its tests offline.
- `shopify_empty_page.json` - the trivial `{"products":[]}` Shopify returns past the last
  page; not a capture (nothing to capture - it's just an empty array).

- `woo_strongcoffee_page1.json` - **genuine live capture**, `GET
  https://strongcoffee-eg.com/wp-json/wc/store/v1/products?per_page=20&page=1` on
  2026-11-03, User-Agent `LazyBean-Collector/1.0` (verified identical with no custom UA
  too - see backend/README.md's "Strong Coffee investigation" section). Real response:
  `200`, `X-WP-Total: 452`, `X-WP-TotalPages: 91`. Contains: minor-unit prices
  (`"price":"275000"`, `currency_minor_unit: 2`, `currency_code: "EGP"`), a discounted
  item (`regular_price` "300000" > `price` "275000"), HTML entities in names (`&#8211;`),
  Arabic-only names (e.g. "قمع الترشيح الحجري اليابانى"), weight/capsule patterns ("1kg",
  "200g", "10 Capsules"), and one `type: "variable"` product (id 47644, 3 color variations).
  Earlier note (now corrected): an earlier attempt at `strongcoffee.com` 404'd because that
  was the wrong domain entirely - the real site is `strongcoffee-eg.com`, which answers this
  endpoint fine with no special handling and no bot-blocking (see backend/README.md).
- `woo_strongcoffee_variation_47645/46/47.json` - genuine captures of
  `GET /wp-json/wc/store/v1/products/{id}` for product 47644's three variations (same
  capture session). Each one's own `attributes` field is empty on this site - the per-
  variation attribute value (e.g. "color: black") only appears in the *listing*'s
  `variations[].attributes` (page1 fixture above) and in this detail's `variation` string
  field, which `WooStoreApiAdapter` uses as a fallback.
- `woo_empty_page.json` - the trivial `[]` the Store API returns past the last page.

Both platforms are exercised by `tests/LazyBean.IntegrationTests/Collection/*AdapterTests.cs`
via a fixture-backed `HttpMessageHandler` - no network access at test time.
