# LazyBean Competitor Intelligence - Deployment Guide

This document describes how to deploy, configure, and maintain the LazyBean Competitor Intelligence platform across **MonsterASP.NET** (Backend API & PostgreSQL), **Netlify** (Frontend & Reverse Proxy), and **GitHub Actions** (Scheduled Automations).

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
                       │  - /api/* Proxy (force=200)    │
                       │  - Signs with x-nf-sign header │
                       └───────────────┬────────────────┘
                                       │ HTTPS Proxy
                                       ▼
                    ┌──────────────────────────────────────┐
                    │      MonsterASP.NET (IIS/Windows)    │
                    │  - .NET 10 Web API (In-Process)      │
                    │  - Verified x-nf-sign headers        │
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
                    └──────────────────────────────────────┘
```

---

## 2. MonsterASP.NET Backend Deployment

MonsterASP.NET provides free Windows/IIS hosting with 256 MB RAM.

### 2.1 Publish Application
Run the following dotnet publish command:
```bash
dotnet publish backend/src/LazyBean.Api/LazyBean.Api.csproj -c Release -o ./publish
```
Upload the contents of `./publish` to the `site/wwwroot` directory on MonsterASP via FTP or the Web File Manager. The included `web.config` will automatically run the app in-process under IIS.

### 2.2 Environment Variables & App Settings
Configure these settings in the MonsterASP Control Panel (**Websites -> Application Settings** / `appsettings.json`):

| Setting Key | Description | Example Value |
|---|---|---|
| `ConnectionStrings:Default` | PostgreSQL Connection String | `Host=pg.monsterasp.net;Port=5432;Database=...;Username=...;Password=...` |
| `Admin:Username` | Bootstrap Admin Login | `admin` |
| `Admin:Password` | Bootstrap Admin Password | `Strong-Secret-Password!` |
| `Internal:Token` | Secret token for GitHub Actions | `random-64-character-token` |
| `Security:NetlifyProxySecret` | Shared secret with Netlify | `random-32-character-secret` |
| `Security:UseHttpsRedirection` | Keep false behind reverse proxy | `false` |
| `SelfKeepAlive:Url` | Local ping URL to prevent IIS sleep | `http://localhost/health` |

### 2.3 Automatic Migrations & Database Initialization
- Database migrations run **automatically and idempotently** at application startup via `db.Database.MigrateAsync()`.
- Reference competitor (`LazyBean`) and admin user are seeded automatically.
- No manual SQL or database schema manipulation is required.
- The `/health` endpoint executes `SELECT 1` on PostgreSQL to verify connectivity.

---

## 3. MonsterASP PostgreSQL Database

### 3.1 Backups
MonsterASP provides managed database backups in the control panel:
1. Navigate to **Databases -> PostgreSQL**.
2. Select your database.
3. Click **Backup / Restore** to trigger a manual backup or enable scheduled daily backups.
4. Backups can be downloaded as `.sql` or `.dump` files.

### 3.2 Storage Optimization & Projection
- `PriceObservation` records are written **only when price, compare-at, or stock changes**. Redundant unchanged price rows are skipped, while `Variant.LastObservedAt` and `Variant.LastSeenRunId` are updated on every run.
- Raw HTTP response bodies are saved only for failures or changes in gzip format and purged after 14 days.
- Product descriptions are excluded from storage.
- Storage requirement: ~12 MB for 14,000+ variants across 5 competitors. Projected 365-day database size is **~45–65 MB**, well within the 1 GB MonsterASP database limit.

---

## 4. Netlify Frontend & Reverse Proxy

The frontend is an Astro 5 + React 19 application located in `/web`. Netlify serves static assets from the edge while proxying `/api/*` to MonsterASP so authentication cookies (`lazybean.auth`) remain first-party.

### 4.1 Deployment Setup
1. In Netlify, create a new site from your GitHub repository.
2. Build Settings:
   - **Base directory:** `web`
   - **Build command:** `npm run build`
   - **Publish directory:** `dist`
3. Environment Variables (**Site configuration -> Environment variables**):
   - `NETLIFY_PROXY_SECRET` = matching `Security:NetlifyProxySecret` on MonsterASP.

### 4.2 Proxy Configuration (`netlify.toml`)
Netlify proxies `/api/*` to MonsterASP with `force = true` and `status = 200`:
```toml
[[redirects]]
  from = "/api/*"
  to = "https://your-monsterasp-domain.monsterasp.net/api/:splat"
  status = 200
  force = true
  headers = { x-nf-sign = ":NETLIFY_PROXY_SECRET" }
```
MonsterASP's `NetlifyForwardedHeadersMiddleware` verifies `x-nf-sign`. If valid, it trusts `X-Forwarded-For` for client rate limiting; if missing or invalid, it falls back to the connection IP to prevent IP spoofing.

---

## 5. GitHub Actions Workflows

Two automated workflows run via GitHub Actions:

### 5.1 Required Repository Secrets
Add these secrets in GitHub (**Settings -> Secrets and variables -> Actions**):

| Secret Name | Value |
|---|---|
| `API_BASE_URL` | Your MonsterASP backend URL (e.g. `https://lazybean-api.monsterasp.net`) |
| `INTERNAL_TOKEN` | Token matching `Internal:Token` on MonsterASP |

### 5.2 Daily Collection Run (`.github/workflows/daily-run.yml`)
- **Schedule:** `0 5 * * *` (05:00 UTC daily / 07:00 Egypt standard time).
- Calls `POST /api/internal/runs` with header `Internal:Token: ${{ secrets.INTERNAL_TOKEN }}`.
- Initiates scraping and matching across all 5 competitors in the background.
- Self keep-alive pings `/health` every 2 minutes to keep the IIS application pool awake during execution.

### 5.3 Google Ads Weekly Sync (`.github/workflows/google-ads.yml`)
- **Schedule:** `0 6 * * 0` (06:00 UTC every Sunday).
- Runs in GitHub's Ubuntu environment (no Python needed on MonsterASP).
- Fetches approved Google advertiser identities from `GET /api/internal/ads/google/identities`.
- Queries Google Ads Transparency Center using direct connections (`PROXY=direct`).
- Ingests results via `POST /api/internal/ads/google`. If rate-limited or blocked, sets competitor status to `Blocked` with a 24-hour backoff.

---

## 6. Security & Resource Constraints

1. **Memory Budget:** Peak working set is strictly maintained under **180 MB** (well below the 256 MB free tier cap) through:
   - Per-page ChangeTracker clearing (`db.ChangeTracker.Clear()`)
   - Category-grouped product matching chunks
   - Streaming pagination without large memory buffering
2. **Rate Limiting:**
   - Public API: 100 requests / minute per client IP
   - Login: 5 requests / minute
   - Internal API: 60 requests / minute authenticated by constant-time token comparison
3. **Cookie Security:**
   - `lazybean.auth` is `HttpOnly`, `SameSite = Lax`, and `Secure = CookieSecurePolicy.Always` in production.
