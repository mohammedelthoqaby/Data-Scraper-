# LazyBean Backend

.NET 10 monolithic backend, Clean Architecture in 4 layers.

```
backend/
├── src/
│   ├── LazyBean.Domain          # Entities, enums, repository/UoW interfaces, domain exceptions. No dependencies.
│   ├── LazyBean.Application     # CQRS (MediatR commands/queries), FluentValidation validators, DTOs,
│   │                             ICacheService/IReadDbContext/IPasswordHasher abstractions, validation
│   │                             pipeline behavior. Depends on Domain (+ EF Core, for IQueryable async LINQ
│   │                             operators on the read side - see "Read-side CQRS" below).
│   ├── LazyBean.Infrastructure  # EF Core (Npgsql), generic Repository<T> + UnitOfWork, HybridCache
│   │                             (+ optional Redis L2), password hashing, health checks. Depends on Application + Domain.
│   └── LazyBean.Api             # ASP.NET Core MVC controllers, Swagger, rate limiting, auth, forwarded
│                                 headers, correlation-id + exception-handling middleware, Serilog, DI root.
└── tests/
    ├── LazyBean.Application.Tests  # Pure unit tests (Moq where needed): command handler,
    │                                 validator, normalizer, validation gate, removal policy.
    ├── LazyBean.IntegrationTests   # Real docker-compose Postgres: unique-violation -> 409,
    │                                 fixture-backed adapter tests, a full collection run,
    │                                 and auth tests (WebApplicationFactory).
    └── fixtures/                   # Genuine captures of both platforms (Shopify from
                                     Coffee Zone Misr, WooCommerce from the real Strong
                                     Coffee site) - see fixtures/README.md.
```

Dependency direction: `Api -> Infrastructure -> Application -> Domain`. Domain knows nothing about
EF Core, Postgres, Redis or ASP.NET Core. **Application is not fully framework-agnostic**: it
references the `Microsoft.EntityFrameworkCore` package (not `Npgsql`, not `Design`) purely for the
`IQueryable<T>` async LINQ operators (`ToListAsync`, `SingleOrDefaultAsync`, ...) used against
`IReadDbContext` in query handlers. This is a deliberate, pragmatic tradeoff - the same one Jason
Taylor's widely-used Clean Architecture template makes - not an oversight: it buys real-DB-shaped
async queries in Application without pulling in Npgsql, connection strings, or anything else
Infrastructure-specific. Application still has no dependency on Postgres, Redis, or ASP.NET Core.

## Patterns demonstrated (via the `Competitor` slice)

- **CQRS**: `Application/Competitors/{Commands,Queries}` — one small class per use case.
- **Repository + Unit of Work**: `Domain/Interfaces/{IRepository,ICompetitorRepository,IUnitOfWork}`,
  implemented in `Infrastructure/Persistence`.
- **FluentValidation**: a validator per command, run automatically by `ValidationBehavior<,>`
  (a MediatR pipeline behavior) before the handler executes.
- **Hybrid caching**: `ICacheService` wraps `HybridCache` (in-process L1 + optional Redis L2),
  used by query handlers and invalidated by command handlers.
- **Rate limiting**: fixed-window limiter partitioned by the real client IP (100 req/min on
  `"api"`, 5 req/min on `"login"`). Rejections return `429` with a `Retry-After` header and a
  `retryAfterSeconds` body field. See "Forwarded headers" below for why the partition key is the
  *real* client IP even behind a reverse proxy.
- **Forwarded headers**: `UseForwardedHeaders` is the very first thing in the pipeline. It only
  trusts `X-Forwarded-For`/`X-Forwarded-Proto` from `ForwardedHeaders:TrustedNetworks` - by default
  loopback plus `172.28.5.0/24`, the **fixed subnet** `docker-compose.yml`'s `lazybean` network is
  pinned to (see its `ipam.config`), not a guess at Docker's whole private range. A request from
  outside those networks has its headers ignored, so a random client can't spoof its own IP to
  dodge the rate limiter. Once trusted, it rewrites `HttpContext.Connection.RemoteIpAddress`, which
  is what the rate limiter partitions on - no limiter code change needed, just correct ordering.
- **Authentication/authorization**: cookie-based (`CookieAuthenticationDefaults`), backed by a
  minimal `users` table (`Username`, `PasswordHash` via ASP.NET Core Identity's `PasswordHasher<T>`
  - PBKDF2, salted - without pulling in the rest of Identity). The cookie's `SecurePolicy` is
  `SameAsRequest` in Development (so plain-HTTP localhost testing works) and `Always` everywhere
  else - it's never sent over a non-HTTPS connection outside dev. Every controller requires
  authentication by default (a global `AuthorizeFilter`); `POST /api/auth/login` and
  `POST /api/auth/logout` opt out with `[AllowAnonymous]`. `/health` is a minimal-API endpoint, not
  an MVC action, so the filter never touches it. First boot seeds one admin user from
  `Admin:Username`/`Admin:Password` (user-secrets only) if the `users` table is empty.
- **HTTPS redirection**: off by default (`Security:UseHttpsRedirection`, `appsettings.json`) -
  correct when a reverse proxy (nginx in `docker-compose`) terminates TLS and talks plain HTTP to
  this container; turning it on there would make Kestrel redirect the proxy's own traffic. Set it
  to `true` only when Kestrel itself is internet-facing and terminating TLS.
- **Middleware pipeline** (`Program.cs`), in order:
  1. `UseForwardedHeaders` — must run before anything that reads the client IP or scheme.
  2. `CorrelationIdMiddleware` — reuses an inbound `X-Correlation-Id` or mints one, echoes it on
     the response, and pushes it into Serilog's `LogContext` so every log line for the request
     (including exceptions) carries it.
  3. `ExceptionHandlingMiddleware` — catches everything downstream and shapes
     `application/problem+json` responses: `404` for `NotFoundException`, `400` for
     `ValidationException`, `401` for `InvalidCredentialsException`, **`409` for a
     `DbUpdateException` whose inner exception is a Postgres unique-violation (`SqlState 23505`)**,
     `500` otherwise.
  4. `UseSwagger` / `UseSwaggerUI` — **Development only**; in any other environment these calls are
     skipped entirely, so `/swagger/*` 404s.
  5. `UseSerilogRequestLogging` — one structured log line per request.
  6. `UseHttpsRedirection` — only if `Security:UseHttpsRedirection=true` (off by default).
  7. `UseRateLimiter`
  8. `UseAuthentication`
  9. `UseAuthorization` — `[Authorize]`-by-default via the global filter.
  10. `MapControllers` / `MapHealthChecks("/health")`.
- **Swagger**: available at `/swagger` in Development only (verified: `200` in Development, `404`
  in Production — see "Verification evidence" below).
- **Health checks**: `GET /health` (anonymous, unrated) runs `PostgresHealthCheck`
  (`Database.CanConnectAsync`) always, plus `RedisHealthCheck` (round-trips a probe key through
  `IDistributedCache`) only when a Redis connection string is configured.
- **Read-side CQRS**: queries go through `IReadDbContext.Query<T>()` (returns a no-tracking
  `IQueryable<T>`, projected straight to a DTO in the handler); commands still go through
  `IRepository<T>` + `IUnitOfWork` so EF Core tracks the entity for the update. One interface,
  implemented by the same `LazyBeanDbContext` on the infrastructure side - no second DbContext.
- **Cache invalidation via tags**: `ICacheService.GetOrCreateAsync` takes an optional `tags`
  collection (HybridCache's native tagging); the Competitor queries tag their entries
  `"competitors"`, and the three command handlers invalidate with a single
  `cache.RemoveByTagAsync("competitors")` instead of tracking and removing each key by hand.

## Running locally

```bash
cd backend
docker compose up -d   # Postgres on localhost:15432, Redis on localhost:16379
                        # (remapped from 5432/6379 - see note below)
dotnet ef database update --project src/LazyBean.Infrastructure --startup-project src/LazyBean.Api
cd src/LazyBean.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=15432;Database=lazybean;Username=lazybean;Password=changeme"
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:16379"
dotnet user-secrets set "Admin:Username" "admin"
dotnet user-secrets set "Admin:Password" "<pick-a-strong-password>"
dotnet run
```

`Admin:Username`/`Admin:Password` only matter on first boot (empty `users` table); after the
admin user is seeded once they're unused - log in at `POST /api/auth/login` and call
`POST /api/auth/logout` to clear the cookie.

> **Windows note**: on this machine, Hyper-V/WSL reserves TCP ports in the 5311–6513 range
> (`netsh interface ipv4 show excludedportrange protocol=tcp`), which blocks the default
> 5432/6379. `docker-compose.yml` here maps Postgres to host port **15432** and Redis to
> **16379** to dodge that range. Adjust if your machine doesn't have the same exclusion.

**Secrets**: `appsettings.json` only ever holds empty placeholders for
`ConnectionStrings:Default` / `ConnectionStrings:Redis` — real values go into
[.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (Development)
or environment variables / a real secret store (other environments), never into the committed
file. See `appsettings.Example.json` for the shape. Leave `ConnectionStrings:Redis` empty (or
unset) to run `HybridCache` **memory-only** — `Infrastructure/DependencyInjection.cs` only
registers the Redis-backed `IDistributedCache` when that connection string is non-empty; with no
`IDistributedCache` registered, `HybridCache` automatically falls back to its in-process L1 only.
The API logs which mode it started in (`Cache mode: ...`).

## CQRS dispatcher

[MediatR](https://github.com/LuckyPennySoftware/MediatR) **12.5.0**, licensed **Apache-2.0**.
Pinned deliberately: MediatR 13+ (the latest is 14.2.0 as of writing) relicensed to the
**Reciprocal Public License 1.5 (RPL-1.5)** — a strong reciprocal/copyleft license under Lucky
Penny Software's dual-licensing model (open-source your changes under RPL, or buy a commercial
license). For a closed-source product like LazyBean, 12.5.0 is the last version that's free and
permissive (Apache-2.0) with no such obligation. If you ever bump the MediatR version, check
`LICENSE.md` in the installed NuGet package first.

## Verification evidence

Commands and the exact responses they produced, captured while `docker compose up -d` and the
API were running (Development unless noted). Earlier items (429/Swagger/cache/409/Redis-optional)
were re-verified after the auth + forwarded-headers changes; new items below cover this round.

**Forwarded headers rewrite the rate limiter's partition key** — exhausted the 5/min `login`
policy for one spoofed client IP, then showed a different spoofed IP gets a fresh bucket in the
same real connection (both requests physically came from loopback, which is in
`ForwardedHeaders:TrustedNetworks`, so the headers are honored):
```
# X-Forwarded-For: 203.0.113.10, six requests in a row:
401 401 401 401 401 429

# Same IP again -> still blocked:
HTTP/1.1 429 Too Many Requests
Retry-After: 60

# X-Forwarded-For: 203.0.113.99 (different IP, same loopback connection) -> fresh bucket:
HTTP/1.1 401 Unauthorized
{"status":401,"title":"Invalid username or password.",...}
```
Untrusted callers (outside `ForwardedHeaders:TrustedNetworks`) have their `X-Forwarded-For`
ignored entirely by `UseForwardedHeaders`, so they can't spoof a fresh bucket for themselves by
sending a random header - only trusted proxies get to say what the real client IP is.

**Authentication** — full flow against the real DB and seeded admin:
```
POST /api/auth/login {"username":"admin","password":"<correct>"}
  -> 200, Set-Cookie: lazybean.auth=...; httponly; samesite=lax, body {"id":...,"username":"admin"}
GET  /api/auth/me (with cookie)            -> 200 {"id":...,"username":"admin"}
GET  /api/competitors (with cookie)        -> 200 [...]
GET  /api/competitors (no cookie)          -> 401
POST /api/auth/login {"...","password":"wrong"}
  -> 401 {"status":401,"title":"Invalid username or password.",...}
```

**Health check**:
```
GET /health (no auth header sent) -> 200 "Healthy"
```

**1. Rate limiting (429 + Retry-After)** — unchanged mechanism, now also proven on `login` above;
originally verified pre-auth with 105 rapid `GET /api/competitors` calls:
```
200 × 100, then 429 × 5
```
```
HTTP/1.1 429 Too Many Requests
Retry-After: 60
X-Correlation-Id: 94a7debb619d424aab2e2b9c909be88d

{"status":429,"title":"Too many requests. Please slow down.","retryAfterSeconds":60}
```

**2. Swagger dev-only**:
```
Development: GET /swagger/v1/swagger.json -> 200, GET /swagger/index.html -> 200
Production:  GET /swagger/v1/swagger.json -> 404, GET /swagger/index.html -> 404
```

**3. Cache invalidation** (now via `RemoveByTagAsync("competitors")`, re-verified after the
`IReadDbContext` refactor) — `GET` (1 item) → `POST` (create) → `GET` immediately:
```
GET  -> [ {coffee-zone-misr} ]
POST -> {id, key: "strong-coffee", ...}  (201 Created)
GET  -> [ {coffee-zone-misr}, {strong-coffee} ]   <- new item visible with no delay
```

**5. Unique index + 409 mapping** — proven as an automated test
(`tests/LazyBean.IntegrationTests/UniqueKeyConflictMappingTests.cs`) rather than a one-off curl,
because reliably forcing the true INSERT-vs-INSERT race over HTTP (the validator's
pre-check normally catches duplicates first, returning `400`) isn't deterministic. The test
inserts the same `Key` via two `DbContext` instances directly, asserts the real
`Npgsql.PostgresException.SqlState == "23505"`, then feeds that exact exception through
`ExceptionHandlingMiddleware` and asserts a `409` response. `dotnet test
tests/LazyBean.IntegrationTests` — 1 passed.

**8. Redis optional** — confirmed by the startup log line in both modes:
```
Development (Redis configured via user-secrets): Cache mode: hybrid (in-process L1 + Redis L2)
Production   (no Redis connection string):        Cache mode: memory-only (no Redis connection string configured)
```

## Running tests

```bash
dotnet test tests/LazyBean.Application.Tests   # pure unit tests, no external dependencies
dotnet test tests/LazyBean.IntegrationTests    # needs `docker compose up -d` (real Postgres)
```

**Tests never touch the dev database** (`lazybean`). `TestDatabaseFixture`
(`tests/LazyBean.IntegrationTests/TestDatabaseFixture.cs`) creates a dedicated `lazybean_test`
database on the same docker-compose Postgres server, migrates it, and drops it again once the
whole test run finishes - an `[ICollectionFixture<TestDatabaseFixture>]` shared across every
`[Collection("Database")]`-tagged test class via `db.ConnectionString`. Verified: the dev
database's `competitors` table had the same row count before and after a full test run.

`LazyBean.IntegrationTests` also disables test-collection parallelization (`xunit.runner.json`):
several tests share that one `lazybean_test` database and `CollectionRunIntegrationTests`
processes *every* enabled `Competitor` row in it, so running tests concurrently caused genuine
cross-test interference (one test's leftover competitor rows got swept into another's run).
That test truncates *within the dedicated test database only* at the start, for a deterministic
"exactly one competitor" result against rows other tests in the same collection left behind -
never against `lazybean`.

Current counts: 54 unit tests (`LazyBean.Application.Tests`) + 16 tests (`LazyBean.IntegrationTests`:
6 fixture-backed adapter tests - Shopify + the real Strong Coffee Woo capture, 1 full-run
integration test, 1 unique-key/409 test, 6 auth tests, all against `lazybean_test`).

## Live evidence: a real run against Coffee Zone Misr

One real `Competitor` (`coffee-zone-misr`, Shopify, `https://coffeezonemisr.com`), one real
`POST /api/runs`, polled via `GET /api/runs/{id}` to completion - no other competitor enabled:

```
POST /api/runs                     -> {"id":"1782fae3-d58b-4650-991e-312781c27e3b"}
GET  /api/runs/{id} (after ~14s)   -> {
  "status": "Completed",
  "startedAt":  "2026-11-03T16:45:39.26Z",
  "finishedAt": "2026-11-03T16:45:53.68Z",
  "competitors": [{
    "competitorKey": "coffee-zone-misr",
    "status": "Completed", "coverage": "Complete",
    "pages": 1, "products": 191, "errors": 0
  }]
}
```

- **Duration**: ~14.4 seconds (one started→finished run, `MinDelayMs` honored between requests).
- **Products**: 191 (one page - the real catalog fits under `limit=250`).
- **Variants**: 191 (one variant per product on this store).
- **Price observations**: 191 (every variant passed the validation gate - all EGP, all positive,
  no prior-run baseline to compare against on a first run).
- **Change events**: 191 `ProductAdded` (first run - nothing existed to compare against).

Three sample normalized rows (`SELECT ... FROM products JOIN variants JOIN price_observations`),
showing real titles with weight extracted:

| Title | NormalizedTitle | WeightGrams | Price (EGP) |
| --- | --- | --- | --- |
| Borbone 100% Arabica Whole Beans Coffee 1kg | borbone 100% arabica whole beans coffee 1kg | 1000 | 1099.00 |
| Lavazza Crema e Gusto Ground Coffee 250g | lavazza crema e gusto ground coffee 250g | 250 | 360.00 |
| illy Intenso Bold Roast Ground Espresso Coffee 250g | illy intenso bold roast ground espresso coffee 250g | 250 | 519.00 |

## Strong Coffee investigation (resolved: our bug, not a block)

The earlier Woo fixture was built from the documented Store API schema because
`strongcoffee.com` (and `www.strongcoffee.com/2014`) 404'd / had no `wc` namespace. The user
independently got `200` with 452 products from `https://strongcoffee-eg.com/wp-json/wc/store/v1/products`
using a plain Python script - a different domain entirely. Re-tested here, **with no User-Agent
change** (never impersonated a browser):

| Request | Status | `X-WP-Total` | `cf-mitigated` |
| --- | --- | --- | --- |
| `GET https://strongcoffee-eg.com/wp-json/wc/store/v1/products?per_page=5&page=1`, UA `LazyBean-Collector/1.0` | 200 | 452 | absent |
| Same URL, no custom UA (`User-Agent:` blank) | 200 | 452 | absent |

Both identical: `Server: cloudflare`, `CF-RAY: a54ce4160938aead-MRS`,
`Content-Type: application/json; charset=UTF-8`, no Cloudflare challenge in either response.
First bytes of body (both cases): `[{"id":48170,"name":"Black Mirror Dot Coffee Weighing
Platform &#8211; ميزان بلاك ميرور",...`. `robots.txt` has no rule touching `/wp-json/` at all
(only `/wp-admin/`, `/wp-content/uploads/...`, `?add-to-cart=`).

**Verdict: a bug on our side** - the stored `Competitor.BaseUrl` was the wrong domain. The site
does not block identified bots; it answers our own collector UA exactly like no UA at all. Fixed
by using `strongcoffee-eg.com` for the `strong-coffee` competitor; no workaround, no UA change.

## Live evidence: all 5 competitors, two runs

Five real `Competitor` rows, each domain verified reachable and genuinely the right store
before use (two candidate domains for Coffee & Cream turned out to be a 404 and an unrelated
parked domain - confirmed with the user rather than guessed):

| Competitor | Platform | BaseUrl |
| --- | --- | --- |
| Coffee Zone Misr | Shopify | `coffeezonemisr.com` |
| Fengany | Shopify | `fengany.com` |
| CAFELAX | Shopify | `cafelax.com` |
| Coffee & Cream | Shopify | `coffeeandcream.app` |
| Strong Coffee | WooCommerce | `strongcoffee-eg.com` |

**Run #1** (`POST /api/runs`, started `17:05:19Z`, finished `17:18:41Z` - **13m22s**):

| Competitor | Status | Coverage | Pages | Products | Variants | Errors | Retries | Weight extracted | UnitCount extracted |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| coffee-zone-misr | Completed | Complete | 1 | 191 | 191 | 0 | 0 | 50.8% | 0.0% |
| fengany | Completed | Complete | 15 | 3,541 | 3,541 | 0 | 0 | 39.0% | 0.0% |
| cafelax | Completed | Complete | 34 | 8,372 | 8,796 | 0 | 0 | 55.7% | 0.0% |
| coffee-and-cream | Completed | Complete | 8 | 1,914 | 1,923 | 0 | 0 | 33.0% | 0.0% |
| strong-coffee | Completed | Complete | 5 | 452 | 468 | 0 | 0 | 26.5% | 7.5% |

"Weight extracted" = % of that competitor's **products** with `WeightGrams` set; "UnitCount
extracted" = % of **variants** with `UnitCount` set (only meaningful where titles actually say
"N Capsules" etc. - most of these catalogs are equipment/accessories, not just bagged coffee, so
low/zero UnitCount is expected, not a bug). Strong Coffee's 452 matches the user's manual Python
test exactly.

**Why CAFELAX took most of the 13m22s** (answering the follow-up directly): **zero retries** were
logged for *any* competitor in either run (`grep -c "retrying in" <log>` = 0 both times), which
rules out 429/503 throttling. CAFELAX is not WooCommerce, so there's no per-variation fetch loop
either (that pattern only exists in `WooStoreApiAdapter`, for `type=variable` products).
The actual cause: **CAFELAX's catalog is genuinely ~44x Coffee Zone Misr's** (8,372 vs. 191
products, 34 pages vs. 1) combined with `CollectionRunExecutor` processing each product
sequentially - per product, it's roughly 7 sequential round trips (SELECT/INSERT product, SELECT/
INSERT variant, SELECT last observation, INSERT observation, INSERT change event) before moving
to the next one. At this catalog size that per-product overhead dominates over the 3-second
inter-page HTTP delay. This is a real, known characteristic, not a bug - batching those writes
would be the next optimization if CAFELAX-sized catalogs become routine.

**Run #2** (immediately after, started `17:23:28Z`, finished `17:35:05Z` - **11m37s**, same 5
competitors, same product/page counts throughout, 0 errors, 0 retries): **1 `ChangeEvent`**
(`StockChanged`, CAFELAX's "Raflos Organic Matcha Latte With Coconut Oil Based Milk 110g" went
`False` → `True`) - the catalogs genuinely didn't move price-wise in the ~17 minutes between runs,
as expected.

**Test isolation re-confirmed**: the dev database (`lazybean`) had exactly 0 competitors before
this round's tests ran and exactly 0 after - `dotnet test tests/LazyBean.IntegrationTests`
creates/migrates/drops `lazybean_test` each time (see "Running tests" below); nothing in the test
suite touches `lazybean`. The 5 competitors above were created through the running API for this
live-evidence run, not by any test.

## Domain model

From the [workflow design doc](../LazyBean%20CI%20v2%20—%20Workflow%20Design.md), two migrations:
`AddAuthAndDomainModel` (initial schema) and `AddCollectionPipelineSchema` (politeness fields on
`Competitor`, `Category`/`WeightGrams` renames, the unique `(VariantId, RunId)` constraint, and
`FetchedResource`):

| Entity | Table | Notes |
| --- | --- | --- |
| `Competitor` | `competitors` | `MinDelayMs` (default 3000) / `MaxConcurrency` drive the collector's politeness policy. |
| `Product` | `products` | Unique on `(CompetitorId, ExternalId)`; `WeightGrams`/`Category` filled by the normalizer. |
| `Variant` | `variants` | Unique on `(ProductId, ExternalId)`; `WeightGrams`/`UnitCount` extracted from its title. |
| `PriceObservation` | `price_observations` | Append-only (no UPDATE of a past price); unique on `(VariantId, RunId)` so a retried fetch within one run can't double-insert. |
| `CollectionRun` | `collection_runs` | One row per scraping run; only one `Queued`/`Running` at a time. |
| `CompetitorRun` | `competitor_runs` | Unique on `(RunId, CompetitorId)`; `Degraded` status means "don't trust this run's absence of products as real removals." |
| `Offer` | `offers` | `DetailsJson` as `jsonb`. Schema only - nothing populates it yet (no offer/promo adapter). |
| `ChangeEvent` | `change_events` | `PriceChanged`/`StockChanged`/`ProductAdded`/`ProductRemoved`/`DiscountStarted`/`DiscountEnded`, generated by `CollectionRunExecutor`. |
| `ProductMatch` | `product_matches` | Links a competitor's variant to LazyBean's own; unique on the pair. Schema only - no matching logic yet. |
| `ReviewItem` | `review_items` | `PayloadJson` as `jsonb`; where a suspicious price observation lands instead of becoming a `PriceObservation` (see "Validation gate" below). |
| `FetchedResource` | `fetched_resources` | One row per HTTP request the collector made; `StoragePath` points at the gzip'd raw body on disk. |
| `User` | `users` | Cookie login (see "Authentication/authorization" above). |

## Collection pipeline

Runs inside the API host - no separate Worker process. `ICompetitorAdapter` is the only contract
`Application` knows about (`Application/Collection/Contracts`); everything that touches HTTP,
Postgres, or the filesystem lives in `Infrastructure/Collection`.

- **Adapters** (`Infrastructure/Collection/Adapters`), one per `Competitor.Platform`, not per
  site - a new Shopify or WooCommerce store needs zero new code, just a `Competitor` row:
  - `ShopifyAdapter`: `GET /products.json?limit=250&page=N` until an empty page. `price` /
    `compare_at_price` are decimal strings; `grams` is ignored (observed to always be `0` on the
    real store - see the fixture) in favor of extracting weight from the title.
  - `WooStoreApiAdapter`: `GET /wp-json/wc/store/v1/products?per_page=100&page=N`, stopping at
    the `X-WP-TotalPages` response header. Prices are minor-unit integer strings divided by
    `10^currency_minor_unit`. For `type=variable`, each entry in `variations` is fetched
    individually (`GET /products/{variationId}`) for its own price - the list endpoint doesn't
    carry per-variation prices.
  - `ICompetitorAdapterResolver` picks the adapter by `Competitor.Platform` - no per-site classes.
- **HTTP policy** (`CollectorHttpClient`, singleton): fixed `User-Agent: LazyBean-Collector/1.0`;
  requests to the same competitor are serialized and spaced by `Competitor.MinDelayMs` (default
  3000ms); on `429`/`503` it honors `Retry-After` (falling back to exponential backoff) for up to
  3 retries, then throws `CollectionFetchException`, which the orchestrator catches and marks that
  `CompetitorRun` `Failed`. `robots.txt` is fetched and cached per host (`RobotsTxtCache`); a
  disallowed path is skipped rather than fetched.
- **Evidence** (`GzipEvidenceStore`): every successful response body is gzip'd to
  `EvidenceStorage:Path` (default `data/evidence/`, meant to be a volume in production) and
  recorded as a `FetchedResource` row with its SHA-256 content hash.
- **Normalizer** (`Application/Collection/Normalization`, pure, no I/O):
  - `TitleNormalizer.Decode` — `WebUtility.HtmlDecode`.
  - `TitleNormalizer.NormalizeForMatching` — HTML-decode, Arabic-Indic digits → ASCII, Arabic
    letter variants collapsed (أ/إ/آ→ا, ى→ي, ة→ه, tashkeel stripped), lowercased, whitespace
    collapsed - the key used to compare titles across competitors.
  - `TitleNormalizer.ExtractWeightGrams` / `ExtractUnitCount` — pulls `"250g"`, `"1kg"`,
    `"250 جرام"`, `"10 Capsules"`, `"١٠ كبسولات"` (Arabic-Indic digits too) out of a title.
  - `PriceLogic.IsDiscounted` — `compareAtPrice > price`, strictly (equal or lower doesn't count).
- **Validation gate** (`PriceObservationGate`, pure): a non-positive price, a non-EGP currency, or
  a >60% change versus the variant's last observation sends the row to `ReviewItem` instead of
  `PriceObservation`.
- **Run lifecycle**: `POST /api/runs` creates a `Queued` `CollectionRun` (refusing with `409` if
  one is already `Queued`/`Running`) and enqueues its id onto an in-process channel
  (`IRunQueue`). `CollectionRunBackgroundService` drains that channel one run at a time - that's
  what actually guarantees "only one run at a time" at execution; the `409` check guarantees it at
  creation time. Within a run, enabled competitors are fetched **in parallel**, each one's own
  pages fetched **sequentially** through its adapter. `DailyCollectionJob` (Quartz, cron
  `0 0 7 * * ?` in `Africa/Cairo`) queues a `Scheduled` run the same way, skipping quietly if one's
  already in flight.
- **Removal rule**: a competitor's missing products are marked `Removed` only if its `CompetitorRun`
  reaches the end of its adapter's pagination without error (`Coverage = Complete`) *and* this
  run's product count hasn't dropped more than 30% versus the average of its last 3 completed
  runs (`ProductRemovalPolicy`, pure). Otherwise the run is marked `Degraded` and nothing is
  removed - a broken adapter page doesn't get to delete a catalog.
- **Change events**: for every variant, the new observation is compared against its immediately
  previous one to emit `PriceChanged`/`StockChanged`/`DiscountStarted`/`DiscountEnded`; a brand
  new product/variant emits `ProductAdded`; a product that stops appearing (and passes the
  removal rule above) emits `ProductRemoved`. `CollectionRunExecutor.ExecuteAsync` ends by calling
  `cache.RemoveByTagAsync` for the `"runs"` and `"products"` tags.
- **Concurrency note**: competitors run in parallel via `Task.WhenAll`, so a single `DbContext`
  can't be shared across them (EF Core forbids concurrent use of one instance). The pipeline uses
  `IDbContextFactory<LazyBeanDbContext>` throughout (`CollectionRunExecutor`, `GzipEvidenceStore`)
  instead of an injected scoped `DbContext` - a real bug this surfaced and fixed during the
  full-run integration test (see `tests/LazyBean.IntegrationTests/Collection/CollectionRunIntegrationTests.cs`).
- **`GET /api/runs`** / **`GET /api/runs/{id}`**: the list is summary-only (status/timestamps); the
  detail view joins in per-competitor status, coverage, pages, products, and errors.

## Adding a new feature slice

1. Add the entity to `Domain/Entities`, its repository interface to `Domain/Interfaces`, and expose
   it on `IUnitOfWork`.
2. Add an EF Core configuration under `Infrastructure/Persistence/Configurations`. Writes go
   through a repository under `Infrastructure/Persistence/Repositories` wired into `UnitOfWork`;
   reads go through `IReadDbContext.Query<T>()` (no repository needed for a read-only query).
3. Add commands/queries + validators under `Application/<Feature>`. Tag cached query results and
   invalidate by that tag from the command handlers (see `ICacheService`/`RemoveByTagAsync`), not
   by tracking individual keys.
4. Add an MVC controller under `Api/Controllers` that sends them through `ISender`. It requires
   authentication by default - add `[AllowAnonymous]` only if it genuinely shouldn't.
5. `dotnet ef migrations add <Name> --project src/LazyBean.Infrastructure --startup-project src/LazyBean.Api`.
