# LazyBean Competitor Intelligence - Deployment Guide

This document describes how to deploy, configure, and maintain the LazyBean Competitor Intelligence platform across **MonsterASP.NET** (Backend API & PostgreSQL on `*.runasp.net`), **Netlify** (Frontend & Reverse Proxy), and **GitHub Actions** (Scheduled Automations & Ads Sync).

---

## 1. Architecture Overview

```
                          ┌──────────────────────────┐
                          │   Client Browser         │
                          └────────────┬─────────────┘
                                       │ (same-origin cookies)
                                       ▼
                       ┌────────────────────────────────┐
                       │        Netlify CDN             │
                       │  - Astro / React Frontend UI   │
                       │  - /api/* JWS-Signed Proxy     │
                       │  - signed = "NETLIFY_PROXY_..."│
                       └───────────────┬────────────────┘
                                       │ HTTPS Proxy
                                       ▼
                    ┌──────────────────────────────────────┐
                    │      MonsterASP.NET (IIS/Windows)    │
                    │  - .NET 10 Web API (In-Process)      │
                    │  - https://lazy-bean.runasp.net      │
                    │  - Verifies X-Nf-Sign JWS (HS256)    │
                    │  - Checks iss="netlify" & exp        │
                    │  - Self Keep-Alive Ping (every 2m)   │
                    │  - Memory Working Set < 180 MB       │
                    └───────────────┬──────────────────────┘
                                    │
                                    ▼
                    ┌──────────────────────────────────────┐
                    │    MonsterASP PostgreSQL Database    │
                    │  - Auto-migrations on boot           │
                    │  - Deduplicated PriceObservations    │
                    │  - Automated Panel Backups           │
                    └──────────────────────────────────────┘
                                    ▲
                                    │ Internal Token Auth
                    ┌───────────────┴──────────────────────┐
                    │          GitHub Actions              │
                    │  - daily-run.yml (05:00 UTC)         │
                    │  - google-ads.yml (Weekly)           │
                    │  - meta-ads.yml (Self-hosted runner) │
                    └──────────────────────────────────────┘
```

---

## 2. MonsterASP.NET Backend Configuration

MonsterASP.NET provides Windows/IIS hosting on `*.runasp.net` domains with a 256 MB RAM limit.

> [!IMPORTANT]
> **HTTPS Prerequisite**: Free plan sites are provisioned on `*.runasp.net`. You **MUST** request HTTPS enablement for `https://lazy-bean.runasp.net` from MonsterASP support before configuring Netlify. Netlify must strictly proxy to HTTPS; never configure Netlify to proxy to `http://`.

### 2.1 Publish Application
Run the dotnet publish command in Release mode:
```bash
dotnet publish backend/src/LazyBean.Api/LazyBean.Api.csproj -c Release -o ./publish
```
The resulting `./publish` directory contains `LazyBean.Api.dll`, `web.config` (with in-process hosting and workstation GC configured), `appsettings.Production.json` (no secrets), and the `seed/` definitions.

Upload all files from `./publish` to the `site/wwwroot` directory on MonsterASP via FTP or the MonsterASP Web File Manager.

### 2.2 Environment Variables & App Settings
Configure these settings in the MonsterASP Control Panel (**Websites -> Application Settings** or set them as environment variables). You can enter them using either colon notation (`Section:Key`) or double-underscore notation (`Section__Key`):

| Setting Key (`:`) | Setting Key (`__`) | Description | Example / Production Value |
|---|---|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | PostgreSQL Connection String | `Host=pg.monsterasp.net;Port=5432;Database=lazybean_prod;Username=lazybean_user;Password=YourSecurePassword!` |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | Optional Redis cache string. If left blank, app defaults to in-memory HybridCache L1 safely. | `redis.monsterasp.net:6379` *(or leave blank)* |
| `Admin:Username` | `Admin__Username` | Bootstrap Admin Login | `admin` |
| `Admin:Password` | `Admin__Password` | Bootstrap Admin Password | `StrongRandomPassword123!` |
| `Internal:Token` | `Internal__Token` | Secret token for GitHub Actions and runner scripts | `random-64-character-token-secret` |
| `Security:NetlifyProxySecret` | `Security__NetlifyProxySecret` | Shared secret with Netlify reverse proxy (used to sign & verify JWS tokens) | `random-32-character-secret` |
| `Security:UseHttpsRedirection` | `Security__UseHttpsRedirection` | Keep `false` behind reverse proxy | `false` |
| `App:PublicBaseUrl` | `App__PublicBaseUrl` | Backend URL used for self keep-alive pings and internal references | `https://lazy-bean.runasp.net` |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | Hosting environment | `Production` |
| `DOTNET_gcServer` | `DOTNET_gcServer` | Forces Workstation GC to keep memory under 200MB | `0` |
| `Meta:AccessToken` | `Meta__AccessToken` | Optional Meta Graph API token for status checks | *(Optional)* |

### 2.3 Automatic Migrations & Database Initialization
- Database migrations run **automatically and idempotently** at application startup via `db.Database.MigrateAsync()`.
- The 6 baseline sources (5 competitors + Lazy Bean reference) are seeded from `seed/competitors.json` via `DbInitializer`.
- Admin user is seeded automatically from `Admin:Username` and `Admin:Password`.
- No manual SQL or database schema alteration is required.
- The `/health` endpoint executes `SELECT 1` on PostgreSQL to verify database connectivity.

---

## 3. MonsterASP PostgreSQL Database

### 3.1 Backups
MonsterASP provides managed database backups in the control panel:
1. Navigate to **Databases -> PostgreSQL**.
2. Select your database.
3. Click **Backup / Restore** to trigger a manual backup or enable scheduled daily backups.
4. Backups can be downloaded as `.sql` or `.dump` files.

### 3.2 Storage Optimization & Projection
- `PriceObservation` records are written **only when price, compare-at, or stock changes**. Redundant unchanged price rows are skipped, while `Variant.LastObservedAt` and `Variant.LastSeenRunId` are updated on every run via set-based `ExecuteUpdateAsync`.
- Raw HTTP response bodies are saved only for failures or changes in gzip format and purged after 14 days.
- Product descriptions are excluded from storage.
- Storage requirement: ~12 MB for 16,000+ variants across 5 competitors. Projected 365-day database size is **~45–65 MB**, well within the 1 GB MonsterASP database limit.

---

## 4. Netlify Frontend & Signed Reverse Proxy

The frontend is an Astro 5 + React 19 single-page application located in `/web`. Netlify serves static assets from the edge while proxying `/api/*` to the backend so authentication cookies (`lazybean.auth`) remain first-party.

### 4.1 Deployment Setup
1. In Netlify, create a new site from your GitHub repository.
2. Build Settings:
   - **Base directory:** `web` (documented in `web/netlify.toml`)
   - **Build command:** `npm run build`
   - **Publish directory:** `dist`
3. Environment Variables (**Site configuration -> Environment variables**):
   - `NETLIFY_PROXY_SECRET` = matching `Security:NetlifyProxySecret` on MonsterASP.

### 4.2 Signed Proxy Configuration (`web/netlify.toml`)
A single `netlify.toml` resides in `web/` configuring the signed proxy rule:
```toml
# Netlify Configuration for Lazy Bean Market Intelligence
# In Netlify Site Settings, configure Base Directory: "web"
# Build Command: "npm run build"
# Publish Directory: "dist"

[build]
  publish = "dist"
  command = "npm run build"

# Proxy /api/* requests to MonsterASP backend with JWS signed request header
# Signed with the NETLIFY_PROXY_SECRET environment variable configured in Netlify
[[redirects]]
  from = "/api/*"
  to = "https://lazy-bean.runasp.net/api/:splat"
  status = 200
  force = true
  signed = "NETLIFY_PROXY_SECRET"

# Fallback for Astro SPA / client-side routing
[[redirects]]
  from = "/*"
  to = "/index.html"
  status = 200
```

### 4.3 JWS Verification on Backend
Netlify signs each proxied request by appending an `x-nf-sign` JWS (HS256) header.
Backend middleware `NetlifyForwardedHeadersMiddleware`:
1. Validates the HMAC-SHA256 signature using `Security:NetlifyProxySecret`.
2. Validates that `iss == "netlify"`.
3. Validates that the token expiration `exp` has not lapsed.
4. **Only upon successful verification** does it trust `X-Forwarded-For` and `X-Forwarded-Proto`. If missing or invalid, forwarded headers are stripped to prevent IP spoofing.

---

## 5. GitHub Actions Workflows & Runners

### 5.1 Required Repository Secrets
Add these secrets in GitHub (**Settings -> Secrets and variables -> Actions**):

| Secret Name | Value | Description |
|---|---|---|
| `API_BASE_URL` | `https://lazy-bean.runasp.net` | Backend URL on MonsterASP |
| `INTERNAL_TOKEN` | Secret matching `Internal:Token` | Authorization for internal automation endpoints |

### 5.2 Workflows Overview
1. **Daily Collection Run (`.github/workflows/daily-run.yml`)**:
   - **Schedule:** `0 5 * * *` (05:00 UTC daily / 07:00 Egypt standard time).
   - Calls `POST /api/internal/runs` with header `X-Internal-Token: ${{ secrets.INTERNAL_TOKEN }}`.
   - Triggers collection and matching across all competitors.
   - Self keep-alive pings `/health` every 2 minutes to keep the IIS application pool awake during execution.
2. **Google Ads Weekly Sync (`.github/workflows/google-ads.yml`)**:
   - **Schedule:** `0 6 * * 0` (06:00 UTC every Sunday).
   - Runs on `[self-hosted, lazybean-collector]`.
   - Queries Google Ads Transparency Center and posts results to `POST /api/internal/ads/google`.
3. **Meta Ads Weekly Sync (`.github/workflows/meta-ads.yml`)**:
   - **Schedule:** `0 6 * * 0` (06:00 UTC every Sunday).
   - Runs on `[self-hosted, lazybean-collector]` (Windows residential runner).
   - Collects ads using `meta-ads-collector` (country EG, active + inactive, max 50 ads/page).
   - Ingests campaigns and linked products via `POST /api/internal/ads/meta`.

---

## 6. First-Deploy Checklist

Follow these steps in order when setting up a fresh production environment:

- [ ] **Step 1: Request HTTPS for the runasp.net site from MonsterASP support (CRITICAL)**
  - MonsterASP free tier sites default to `http://<site>.runasp.net`.
  - Open a ticket with MonsterASP support to enable SSL/HTTPS for `https://lazy-bean.runasp.net`.
  - Confirm `https://lazy-bean.runasp.net` loads via HTTPS before configuring the Netlify proxy. **Never point Netlify at http://**.
- [ ] **Step 2: Create MonsterASP PostgreSQL Database**
  - In MonsterASP control panel, create a new PostgreSQL database.
  - Note down the connection string: `Host`, `Port`, `Database`, `Username`, `Password`.
- [ ] **Step 3: Configure MonsterASP Environment Variables**
  - Set `ConnectionStrings:Default` with the PostgreSQL credentials.
  - Set `Admin:Username` and `Admin:Password` to your chosen credentials.
  - Set `Internal:Token` to a secure 64-character random string.
  - Set `Security:NetlifyProxySecret` to a secure 32-character random string.
  - Set `Security:UseHttpsRedirection` to `false`.
  - Set `App:PublicBaseUrl` to `https://lazy-bean.runasp.net`.
- [ ] **Step 4: Deploy Backend Publish Artifact**
  - Run `dotnet publish backend/src/LazyBean.Api/LazyBean.Api.csproj -c Release -o publish`.
  - Upload contents of `publish/` to MonsterASP `site/wwwroot/`.
- [ ] **Step 5: Verify Database Auto-Migration & Backend Health**
  - Navigate to `https://lazy-bean.runasp.net/health`.
  - Verify HTTP 200 OK (`Healthy`).
  - On first boot, EF Core creates all tables and seeds the 6 sources and admin user.
- [ ] **Step 6: Setup Netlify Frontend**
  - Connect GitHub repository in Netlify with base directory `web`.
  - Set environment variable `NETLIFY_PROXY_SECRET` matching the secret in Step 3.
  - Trigger deployment. Ensure static build finishes and `dist` is published.
  - Verify `https://<your-site>.netlify.app` loads the Unlock operations screen when unauthenticated.
- [ ] **Step 7: Configure GitHub Actions Secrets**
  - In GitHub repository settings, add `API_BASE_URL` (`https://lazy-bean.runasp.net`) and `INTERNAL_TOKEN`.
  - Register the self-hosted Windows runner with labels `self-hosted, lazybean-collector`.
- [ ] **Step 8: Run Smoke Test Protocol**
  - Execute the 5 verification tests below.

---

## 7. Production Smoke Test Protocol

Run these verification tests immediately after deployment:

### Test 1: Health Check
Verify the backend API and PostgreSQL connectivity directly:
```bash
curl -i https://lazy-bean.runasp.net/health
```
**Expected Response:**
```http
HTTP/1.1 200 OK
Content-Type: text/plain

Healthy
```

---

### Test 2: Admin Authentication & Cookie Policy
Attempt login through Netlify proxy with configured admin credentials:
```bash
curl -i -X POST https://your-site.netlify.app/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"YourAdminPassword!"}' \
  -c cookies.txt
```
**Expected Response:**
- HTTP 200 OK with user details JSON: `{"username":"admin","role":"Admin"}`
- `Set-Cookie` header containing `lazybean.auth` with attributes:
  - `HttpOnly`
  - `SameSite=Lax`
  - `Secure`
  - `Path=/`

Verify protected route rejection without cookie:
```bash
curl -i https://your-site.netlify.app/api/runs
```
**Expected Response:** HTTP 401 Unauthorized (returns status code, does not redirect to login page).

Verify protected route success with cookie:
```bash
curl -i https://your-site.netlify.app/api/runs -b cookies.txt
```
**Expected Response:** HTTP 200 OK (returns list of runs).

---

### Test 3: Collection Run Trigger & Execution
Trigger a collection run via the internal endpoint using the shared token:
```bash
curl -i -X POST https://lazy-bean.runasp.net/api/internal/runs \
  -H "X-Internal-Token: YOUR_INTERNAL_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"trigger":"SmokeTest"}'
```
**Expected Response:**
- HTTP 202 Accepted with run details:
```json
{
  "runId": "uuid-here",
  "status": "Running",
  "startedAt": "2026-11-04T..."
}
```

Poll run progress via the authenticated frontend endpoint:
```bash
curl -s https://your-site.netlify.app/api/runs -b cookies.txt
```
**Expected Outcome:**
- Status progresses from `Running` to `Completed`.
- Competitors processed: `cafelax`, `fengany`, `coffee-and-cream`, `strong-coffee`, `coffee-zone`, `lazy-bean`.
- Products and variants collected (~16,000+ variants).
- Automated matching completes: ~350+ matches auto-accepted, review queue populated.

---

### Test 4: Price Matrix & Price Position Verification
Query the latest price comparison matrix:
```bash
curl -s https://your-site.netlify.app/api/price-matrix/latest -b cookies.txt
```
**Expected Response:**
- HTTP 200 OK.
- Contains summary metrics (`totalProducts`, `matchedProducts`, `cheaperCount`, `moreExpensiveCount`, `parityCount`).
- `items` array contains matched products with:
  - `productTitle`
  - `referencePrice`
  - `competitorPrices` map
  - `position` (`Cheaper`, `MoreExpensive`, or `Parity`)
  - `priceDifference` and `percentageDifference`

---

### Test 5: Ads Ingestion, Campaigns & Product Linking
Test Meta Ads ingestion via internal endpoint:
```bash
curl -i -X POST https://lazy-bean.runasp.net/api/internal/ads/meta \
  -H "X-Internal-Token: YOUR_INTERNAL_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "pageId": "100456846237420",
    "status": "Success",
    "ads": [
      {
        "id": "smoke-test-ad-1",
        "title": "Smoke Test Campaign",
        "body": "Specialty Ethiopian roasted beans for espresso and filter.",
        "link": "https://www.cafelax.com/products/ethiopia-sidama",
        "snapshotUrl": "https://facebook.com/ads/archive/render_ad/?id=smoke-test-ad-1",
        "status": "Active"
      }
    ]
  }'
```
**Expected Response:** HTTP 200 OK `{"status":"Success","adCount":1}`.

Verify campaign grouping and product linking:
```bash
curl -s https://your-site.netlify.app/api/ads/campaigns -b cookies.txt
```
**Expected Response:**
- HTTP 200 OK.
- Grouped campaigns returned with ad counts and active statuses.

Verify linked ads on the price matrix:
```bash
curl -s https://your-site.netlify.app/api/ads -b cookies.txt
```
**Expected Response:**
- Ads return with `linkedProductId`, `linkedVariantId`, `handle`, and `pricePosition` badges matching the advertised product.
