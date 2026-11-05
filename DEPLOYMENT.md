# LazyBean Competitor Intelligence - Deployment Guide

This document describes how to deploy, configure, and maintain the LazyBean Competitor Intelligence platform on **MonsterASP.NET** (`https://lazy-bean.runasp.net`), hosting both the **.NET 10 Web API** and the **Astro / React SPA Frontend** from the same origin, alongside **MonsterASP PostgreSQL** and **GitHub Actions** automations.

---

## 1. Single-Origin Architecture Overview

> [!NOTE]
> **Why Single Origin?**
> Domains on `netlify.app` face widespread DNS and connection timeouts across Egyptian ISPs (WE, Vodafone, Orange, Etisalat). By embedding the prebuilt static frontend (`web/dist/`) directly inside the backend's `wwwroot/`, MonsterASP serves the entire system from a single origin (`https://lazy-bean.runasp.net`). This completely eliminates Egyptian ISP connectivity failures, removes third-party proxy latency, and enhances security with `SameSite=Strict` first-party cookies.

```
                          ┌──────────────────────────┐
                          │   Client Browser (Egypt) │
                          └────────────┬─────────────┘
                                       │ HTTPS (Single Origin)
                                       │ SameSite=Strict Cookie: lazybean.auth
                                       ▼
                    ┌──────────────────────────────────────┐
                    │      MonsterASP.NET (IIS/Windows)    │
                    │  - Site: https://lazy-bean.runasp.net│
                    │  - .NET 10 Web API (In-Process)      │
                    │  - Static File Provider (wwwroot)    │
                    │    * /_astro/*: 1-yr immutable cache │
                    │    * index.html: no-cache revalidate │
                    │    * SPA Fallback: MapFallbackToFile │
                    │  - Self Keep-Alive Ping (every 2m)   │
                    │  - Working Set RAM < 180 MB          │
                    └───────────────┬──────────────────────┘
                                    │
                                    ▼ (Transaction Pooling)
                    ┌──────────────────────────────────────┐
                    │    MonsterASP PostgreSQL Database    │
                    │  - Auto-migrations on boot           │
                    │  - PgBouncer transaction pooling     │
                    │  - Data Protection Key Ring in DB    │
                    │  - AI Settings & Alerts Persistence  │
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

## 2. Publish Package (`lazybean-api.zip`)

### 2.1 One-Command Publish
Building the publish package is fully automated via MSBuild. Running `dotnet publish` compiles the backend and automatically builds `web/` (`npm run build`) and copies the output into `publish/wwwroot/`:

```powershell
dotnet publish backend/src/LazyBean.Api/LazyBean.Api.csproj -c Release -r win-x86 --self-contained false -o publish
```

### 2.2 Zip Package Structure
The package `lazybean-api.zip` is created with all files at the **ROOT** of the archive (no top-level wrapper folder):

```
lazybean-api.zip
├── wwwroot/
│   ├── _astro/             (hashed immutable JS/CSS bundles)
│   ├── assets/             (SVGs, icons, logos)
│   ├── favicon.ico
│   ├── favicon.svg
│   └── index.html          (SPA root entry point)
├── seed/
│   └── competitors.json    (bootstrap competitor definitions)
├── Collection/
│   └── Classification/     (coffee classification rules)
├── Matching/
│   └── generic_tokens.json (matching token filters)
├── web.config              (IIS in-process AspNetCoreModuleV2 handler)
├── LazyBean.Api.dll        (.NET 10 Web API assembly)
├── LazyBean.Api.exe
├── LazyBean.Application.dll
├── LazyBean.Domain.dll
├── LazyBean.Infrastructure.dll
├── appsettings.json
└── appsettings.Production.json
```

> [!IMPORTANT]
> `appsettings.Development.json` and any secret credentials are strictly excluded from the published zip. Real credentials must **only** be supplied via MonsterASP Environment Variables.

---

## 3. MonsterASP Environment Variables

In the MonsterASP Control Panel, navigate to **Websites > [lazy-bean.runasp.net] > Scripting > Environment variables** (or **Application Settings**).

Configure the following variables using the double-underscore (`__`) notation:

| Environment Variable Name | Required | Description / Example Value |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | **Yes** | Set to `Production`. |
| `DOTNET_gcServer` | **Yes** | Set to `0` (enforces Workstation GC to stay under 200 MB RAM limit). |
| `ConnectionStrings__Default` | **Yes** | PostgreSQL connection string provided by MonsterASP (e.g., `Host=pg.monsterasp.net;Port=5432;Database=lazybean_db;Username=...;Password=...`). Supports PgBouncer in transaction mode. |
| `ConnectionStrings__Redis` | Optional | Redis connection string if cache addon is active; otherwise leave empty to use in-process L1 cache. |
| `Admin__Username` | **Yes** | Initial administrative username (e.g., `admin`). |
| `Admin__Password` | **Yes** | Initial administrative password (e.g., `StrongSecurePassword123!`). |
| `Internal__Token` | **Yes** | Shared secret token for GitHub Actions and runner webhooks (64 characters recommended). |
| `App__PublicBaseUrl` | **Yes** | `https://lazy-bean.runasp.net` (used for keep-alive pings and internal URLs). |
| `Security__UseHttpsRedirection` | **Yes** | `false` (TLS is terminated by MonsterASP's IIS reverse proxy front). |

> [!NOTE]
> AI Studio API keys (Gemini API) and thresholds are managed at runtime via **Settings > AI** in the UI. Keys are encrypted with ASP.NET Core Data Protection and stored in the database (`DataProtectionKeys` table), surviving app restarts.

---

## 4. First-Deploy Checklist

Follow these steps when deploying to MonsterASP:

- [ ] **Step 1: Request HTTPS for `lazy-bean.runasp.net`**
  - MonsterASP free tier sites provision with HTTP by default.
  - Submit a ticket or enable SSL in the control panel so `https://lazy-bean.runasp.net` is active.
- [ ] **Step 2: Create PostgreSQL Database on MonsterASP**
  - Navigate to **Databases > PostgreSQL** and create the database.
  - Note down the host, port, database name, username, and password.
- [ ] **Step 3: Add Environment Variables in MonsterASP**
  - In **Scripting > Environment variables**, add all variables from the table in Section 3.
- [ ] **Step 4: Upload `lazybean-api.zip`**
  - In MonsterASP **Web File Manager**, navigate to `site/wwwroot/`.
  - Delete any default placeholder files (e.g. `hostingstart.html`).
  - Upload `lazybean-api.zip` and use MonsterASP's "Extract" feature to unzip the files directly into `site/wwwroot/`.
  - Verify that `web.config` and `LazyBean.Api.dll` reside directly in `site/wwwroot/`, and `wwwroot/index.html` exists under `site/wwwroot/wwwroot/index.html`.
- [ ] **Step 5: Verify Health & Automatic Database Migrations**
  - Open `https://lazy-bean.runasp.net/health`.
  - Should return HTTP 200 `Healthy`.
  - EF Core automatically applies all migrations on initial startup and seeds the baseline competitors and admin user.
- [ ] **Step 6: Configure GitHub Actions Secrets**
  - In your GitHub repository (**Settings > Secrets and variables > Actions**), set:
    - `API_BASE_URL` = `https://lazy-bean.runasp.net`
    - `INTERNAL_TOKEN` = matching value from `Internal__Token`.
- [ ] **Step 7: Verify Single-Origin SPA & Authentication**
  - Visit `https://lazy-bean.runasp.net/`.
  - Unlock screen appears.
  - Log in with `Admin__Username` and `Admin__Password`.
  - Overview, Price Matrix, and Action Center load with zero CORS or network issues.

---

## 5. Automated Jobs & GitHub Actions

### 5.1 Daily Collection (`.github/workflows/daily-run.yml`)
- **Schedule**: `0 5 * * *` (05:00 UTC / 07:00 Egypt time).
- **IIS 411 Fix**: Sends `POST /api/internal/runs` with `-H "Content-Type: application/json" -d "{}"` to satisfy IIS Content-Length requirements and avoid HTTP 411 errors.
- **Verification Endpoint**: Checks run status via `GET /api/internal/runs/latest`.

### 5.2 Google Ads Sync (`.github/workflows/google-ads.yml`)
- **Schedule**: Every Sunday at 06:00 UTC.
- Runs transparency scrapers and ingests via `POST /api/internal/ads/google`.

### 5.3 Meta Ads Sync (`.github/workflows/meta-ads.yml`)
- **Schedule**: Every Sunday at 06:00 UTC on a self-hosted runner (`lazybean-collector`).
- Ingests verified ad creatives and links via `POST /api/internal/ads/meta`.

---

## 6. Smoke Test Protocol

Run these checks against `https://lazy-bean.runasp.net`:

### Test 1: Health & Database Connectivity
```bash
curl -i https://lazy-bean.runasp.net/health
```
**Expected Response:** HTTP 200 OK `Healthy`.

### Test 2: Static Asset Headers
```bash
curl -i https://lazy-bean.runasp.net/index.html
```
**Expected Response:**
- `Cache-Control: no-cache, no-store, must-revalidate`
- `Content-Type: text/html`

### Test 3: Admin Login & SameSite=Strict Cookie
```bash
curl -i -X POST https://lazy-bean.runasp.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"YourPassword"}' \
  -c cookies.txt
```
**Expected Response:**
- HTTP 200 OK with authenticated user JSON.
- `Set-Cookie: lazybean.auth=...; path=/; samesite=strict; httponly; secure`

### Test 4: Latest Run Status via Internal API
```bash
curl -i https://lazy-bean.runasp.net/api/internal/runs/latest \
  -H "X-Internal-Token: YOUR_INTERNAL_TOKEN"
```
**Expected Response:** HTTP 200 OK with latest collection run summary JSON.
