# LazyBean - System Handover & Operational Guide

This document describes the deployment architecture, configuration secrets, scheduled automation, and operational maintenance for LazyBean.

The system is designed for **zero developer maintenance**:
- No personal servers. The only self-hosted runner is the optional office PC used for Meta Ads (section 4); nothing else depends on it.
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
|  Optional Office PC      |              |        MonsterASP (Backend API)    |
|  (Self-Hosted Runner)    | -----------> |  - Serves ASP.NET Core & React UI  |
|  - meta-ads.yml (Daily)  |  (Internal   |  - Runs Shopify, Woo & LB Scraping |
|  - Egyptian Office IP    |    Token)    |  - Executes Matching, AI & Alerts  |
|  - Zero impact when off  |              |  - SQLite / PostgreSQL Persistence |
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
   - Strong Coffee (`strongcoffee-eg.com`) blocks MonsterASP server IPs (HTTP 403) but succeeds from standard cloud IPs.
   - A dedicated .NET console tool (`LazyBean.CollectorCli`) runs in GitHub Actions (`.github/workflows/daily-run.yml` and `github-collect.yml`), reuses the exact same `WooStoreApiAdapter` and domain rules, and streams product batches back to the backend.
3. **Ads Intelligence**:
   - **Google Ads**: Direct in-app backend sync with transparency `SearchSuggestions` & `SearchCreatives`. Weekly internal trigger + manual button.
   - **Meta Ads**: Public Meta Ad Library blocks datacenter IPs (MonsterASP, GitHub, Cloudflare). Collection runs optionally via a self-hosted GitHub runner on an office PC (`meta-ads.yml` + `tools/meta-ads/sync.py`) using Egyptian residential/office IP. If the PC is off, nothing breaks: the UI displays "Runner offline - last sync <date>" with no error alerts. Each competitor card also provides a direct "View on Meta Ad Library ↗" button opening the official Egypt library page.

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
| `GitHub__DispatchToken` | Fine-grained PAT for Actions dispatch | **Actions: Read and write** on this repo (see guide below) |
| `GitHub__Repo` | Target GitHub repository | `mohammedelthoqaby/Data-Scraper-` |
| `GitHub__Ref` | Git branch to trigger workflow on | `main` |

---

### Step-by-Step GitHub Fine-Grained Personal Access Token (PAT) Setup

To allow MonsterASP to automatically dispatch `github-collect.yml` whenever a manual or scheduled collection run starts:

1. Log into your GitHub account and go to **Settings** (top right profile icon > Settings).
2. In the left sidebar, click **Developer settings** (at the bottom).
3. Under **Personal access tokens**, click **Fine-grained tokens** > **Generate new token**.
4. Configure the token fields:
   - **Token name**: `LazyBean-MonsterASP-Dispatch`
   - **Expiration**: Select `90 days` or `1 year` (set a calendar reminder to renew before expiration).
   - **Resource owner**: `mohammedelthoqaby`
   - **Repository access**: Select **Only select repositories**, then choose `mohammedelthoqaby/Data-Scraper-`.
   - **Permissions > Repository permissions**:
     - Find **Actions**: change access from *No access* to **Read and write**.
     - All other permissions should remain *No access*.
5. Click **Generate token** and copy the generated token (`github_pat_...`).
6. Go to **MonsterASP Control Panel > Website Settings > Environment Variables**:
   - Add `GitHub__DispatchToken` with the copied token value.
   - Add `GitHub__Repo` with value `mohammedelthoqaby/Data-Scraper-`.
   - Add `GitHub__Ref` with value `main`.
7. **Security Guarantee**: The backend never logs this token in console logs, exception traces, or HTTP responses. If dispatch fails, the backend only surfaces the sanitized HTTP status code (e.g. HTTP 401 / 403) to the UI.

---

### C. In-App Encrypted Secrets (Settings UI)
Location: **LazyBean Web App > Settings > Google AI Studio (Gemini)**

| Setting | Purpose | Notes |
|---|---|---|
| `Gemini API Key` | Powers AI candidate review triage and attention ranking | Encrypted with ASP.NET Data Protection into DB |

---

## 3. Scheduled Automations & Automated Dispatch

### Daily Run (05:00 UTC / 07:00 Egypt Time)
- File: `.github/workflows/daily-run.yml`
- Workflow:
  1. Calls `POST /api/internal/runs` on MonsterASP with `X-Internal-Token`.
  2. Receives the `runId`.
  3. The server immediately initiates collection:
     - Scrapes server-hosted competitors (`cafelax`, `coffee-and-cream`, `coffee-zone`, `fengany`).
     - Automatically calls GitHub REST API `workflow_dispatch` for `github-collect.yml` for any competitor configured with `collectionHost = "github"` (`strong-coffee`).
  4. GitHub Actions runner executes `LazyBean.CollectorCli` on `ubuntu-latest`:
     - Posts product batches to `POST /api/internal/runs/{runId}/competitors/strong-coffee/products`.
     - Completes competitor run with `POST .../complete`.
  5. The server waits up to 30 minutes with a live countdown: `Waiting for GitHub collector (mm:ss / 30:00)`.
  6. MonsterASP automatically executes the post-processing pipeline: classification -> matching -> AI triage (if configured) -> alerts.

### Manual Collection Runs (Dashboard)
- When clicking **Start Collection Run** in the web dashboard, the exact same automated dispatch occurs:
  - The backend dispatches `github-collect.yml` automatically via GitHub API.
  - The dashboard displays a direct link **View GitHub Actions ↗** and a **↻ Re-dispatch** button on the Strong Coffee row.
  - The false "Scraper appears stalled" warning is suppressed while waiting for external collectors.

### Weekly Google Ads Sync
- Executes inside the backend automatically every 7 days after the daily collection run, or on-demand via the **Sync Google Ads** button in the Ads UI.
- Direct connection to Google Ads Transparency SearchSuggestions and SearchCreatives.

---

## 4. Optional Meta Ads Office PC Runner (Setup & Maintenance)

Because Meta blocks all datacenter IPs (MonsterASP, GitHub cloud runners, Cloudflare), Meta collection can run optionally from any company office PC via GitHub's free self-hosted runner mechanism:

### Quick Setup Steps (One-time, 5 minutes)
1. In your GitHub repository, navigate to **Settings > Actions > Runners > New self-hosted runner**.
2. Select your OS (Windows or Linux).
3. Download the runner package and run `config.cmd` (or `config.sh`).
4. When prompted for labels, add: `lazybean-collector`.
5. Run the runner:
   - On Windows: Run `run.cmd`, or install as a background service:
     ```powershell
     .\svc.bat install
     .\svc.bat start
     ```
   - On Linux/macOS: Run `./run.sh` or `./svc.sh install && ./svc.sh start`.

### How It Operates
- Workflow: `.github/workflows/meta-ads.yml` runs daily at 09:00 UTC on the `[self-hosted, lazybean-collector]` runner, or manually via **Run workflow** (`workflow_dispatch`). There is no GitHub-hosted fallback for Meta: a Meta run that cannot reach the office runner simply does not happen.
- Posts to `API_BASE_URL` (production) with `INTERNAL_TOKEN`. Each page's result (status, ad count, error) is printed per page and in the run summary in the Actions log.
- Runs Python (`tools/meta-ads/sync.py`), scrapes approved Meta Page IDs through the Egyptian office IP, and posts ads to `POST /api/internal/ads/meta`.
- **Runs only when the PC is on**: When the PC is off, GitHub simply queues the job until the PC boots up, or the run can be triggered manually.
- **Nothing breaks when off**: The LazyBean dashboard displays `"Runner offline - last sync <date>"`. No errors, red alerts, or broken states appear anywhere.
- **Direct Library Access**: The Ads page provides a direct **"View on Meta Ad Library ↗"** button for each competitor to view live active campaigns at any time without running any scraper.

---

## 5. Manual Operations & Troubleshooting

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
2. For Meta: identities seeded from `seed/competitors.json` (`cafelax`, `coffee-and-cream`, `coffee-zone`, `fengany`, `strong-coffee`) are approved automatically on startup. There is no manual approval step for them. To stop collecting a page, reject its identity in **Manage Sources > Advertiser Identities**; a rejection is kept across restarts and is not re-approved on the next startup.
3. For Google: Click **🔍 Discover Google Advertisers** next to any competitor, review discovered candidates, and approve.

---

## 6. Deployment / Publishing Instructions

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
