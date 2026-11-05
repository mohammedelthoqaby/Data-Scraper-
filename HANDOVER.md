# LazyBean - System Handover & Operational Guide

This document describes the deployment architecture, configuration secrets, scheduled automation, and operational maintenance for LazyBean.

The system is designed for **zero developer maintenance**:
- No self-hosted runners or personal servers.
- No Contabo VPS or persistent cloud compute costs.
- No credit card required (runs entirely on free tiers).
- Completely hands-off daily operation after handover.

---

## 1. Architecture Overview

```
                                  +------------------------------------+
                                  |         GitHub Actions (Free)       |
                                  |  - Daily trigger at 05:00 UTC      |
                                  |  - Runs LazyBean.CollectorCli for  |
                                  |    Strong Coffee (WooCommerce)     |
                                  +-----------------+------------------+
                                                    | (X-Internal-Token)
                                                    v
+--------------------------+              +------------------------------------+
|   Cloudflare Worker      |              |        MonsterASP (Backend API)    |
|   lazybean-meta          | <----------  |  - Serves ASP.NET Core & React UI  |
|  - Queries public Meta   |  (Optional   |  - Runs Shopify, Woo & LB Scraping |
|    Ad Library GraphQL    |   Worker     |  - Executes Matching, AI & Alerts  |
|  - Free tier (100k/day)  |   Secret)    |  - SQLite / PostgreSQL Persistence |
+--------------------------+              +-----------------+------------------+
                                                            |
                                                            v
                                                  +--------------------+
                                                  | PostgreSQL (Neon)  |
                                                  +--------------------+
```

1. **Backend & Frontend (MonsterASP)**:
   - ASP.NET Core 9 / 10 API hosting the built Astro / React SPA from `wwwroot`.
   - Collects from server-friendly sources: Lazy Bean (Shopify), Cafelax, Coffee & Cream, Coffee Zone, Fengany.
   - Orchestrates automated catalog classification, deterministic matching, optional Gemini AI review triage, and manager action alerts.
2. **External WooCommerce Collector (GitHub Actions)**:
   - Strong Coffee (`strongcoffeeegypt.com`) blocks MonsterASP server IPs (HTTP 403) but succeeds from standard cloud IPs.
   - A dedicated .NET console tool (`LazyBean.CollectorCli`) runs in GitHub Actions (`.github/workflows/daily-run.yml` and `github-collect.yml`), reuses the exact same `WooStoreApiAdapter` and domain rules, and streams product batches back to the backend.
3. **Ads Intelligence**:
   - **Google Ads**: Direct in-app sync with transparency SearchSuggestions & SearchCreatives.
   - **Meta Ads**: Seeded page IDs in PendingApproval state. Can run via Cloudflare Worker (`tools/cloudflare-worker/worker.js`) or local utility.

---

## 2. Secrets & Configuration Inventory

No secret values are stored in the git repository. All secrets are managed in their respective provider consoles:

### A. GitHub Repository Secrets
Location: **GitHub Repository > Settings > Secrets and variables > Actions**

| Secret Name | Purpose | Example / Format |
|---|---|---|
| `API_BASE_URL` | Base public URL of MonsterASP application | `https://lazy-bean.runasp.net` (no trailing slash) |
| `INTERNAL_TOKEN` | Shared secret token authorizing `/api/internal/*` requests | High-entropy string (e.g. 32-64 chars) |

### B. MonsterASP Environment Variables
Location: **MonsterASP Control Panel > Website Settings > Environment Variables**

| Variable Name | Purpose | Notes |
|---|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string | Point to Neon Postgres database |
| `Internal__Token` | Shared internal API token | **Must match `INTERNAL_TOKEN` in GitHub Secrets** |
| `Security__JwtSecret` | Cryptographic secret for signing auth tokens | 32+ random characters |
| `Admin__Username` | Initial administrator login username | e.g. `admin` |
| `Admin__Password` | Initial administrator login password | Strong password |
| `Gemini__ApiKey` | (Optional) Gemini API Key fallback | Better to configure via UI with encryption |

### C. In-App Encrypted Secrets (Settings UI)
Location: **LazyBean Web App > Settings > Google AI Studio (Gemini)**

| Setting | Purpose | Notes |
|---|---|---|
| `Gemini API Key` | Powers AI candidate review triage and attention ranking | Encrypted with ASP.NET Data Protection into DB |

### D. Cloudflare Worker Secrets
Location: **Cloudflare Dashboard > Workers & Pages > `lazybean-meta` > Settings > Variables**

| Secret Name | Purpose | Notes |
|---|---|---|
| `WORKER_SECRET` | Header secret `X-Worker-Secret` for worker endpoints | Prevents unauthorized external use |

---

## 3. Scheduled Automations

### Daily Run (05:00 UTC / 07:00 Egypt Time)
- File: `.github/workflows/daily-run.yml`
- Workflow:
  1. Calls `POST /api/internal/runs` on MonsterASP with `X-Internal-Token`.
  2. Receives the `runId`.
  3. Launches `LazyBean.CollectorCli` for `strong-coffee` on GitHub runner.
  4. Posts product batches to `POST /api/internal/runs/{runId}/competitors/strong-coffee/products`.
  5. Completes competitor run with `POST .../complete`.
  6. MonsterASP automatically executes classification -> matching -> AI triage (if configured) -> alerts.

---

## 4. Manual Operations & Troubleshooting

### Triggering a Run Manually
- **From UI**: Click **Start Collection Run** on the Collection Runs page.
- **From GitHub Actions**:
  - Open **Actions** tab in GitHub repo.
  - Select **Daily Collection Run** > Click **Run workflow**.

### Re-running Strong Coffee Only
- Open GitHub Actions > Select **GitHub Actions Collector** (`github-collect.yml`).
- Enter the `run_id` (copied from Collection Runs page) > Click **Run workflow**.

### Adding or Approving Advertiser Identities
1. Go to **Manage Sources > Advertiser Identities**.
2. For Meta: Review seeded identities (`cafelax`, `coffee-and-cream`, `coffee-zone`, `fengany`, `strong-coffee`) and click **Approve**.
3. For Google: Click **🔍 Discover Google Advertisers** next to any competitor, review discovered candidates, and approve.

---

## 5. Deployment / Publishing Instructions

To produce a new release package:
```bash
# 1. Build frontend
cd web && npm run build && cd ..

# 2. Publish backend win-x86 for MonsterASP
dotnet publish backend/src/LazyBean.Api/LazyBean.Api.csproj -c Release -r win-x86 --self-contained false -o publish

# 3. Ensure appsettings.Development.json is removed
rm -f publish/appsettings.Development.json

# 4. Zip the contents of publish/ into lazybean-api.zip (files at root of zip)
```
Upload `lazybean-api.zip` directly to MonsterASP root directory and extract.
