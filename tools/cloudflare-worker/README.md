# [DEPRECATED] LazyBean Meta Ads Cloudflare Worker

> [!WARNING]
> **DEPRECATED & NOT IN USE**:
> Meta conclusively blocks all datacenter egress IPs (GitHub Actions, MonsterASP, and Cloudflare Worker edge nodes) with challenge triggers (`challengeTriggered: true`) and rate limits (`1675004`).
> Meta collection does **not** run via Cloudflare Workers. Instead, it runs optionally via a self-hosted runner on an office PC (`tools/meta-ads/` + `.github/workflows/meta-ads.yml`) using a standard residential/office Egyptian IP.
>
> This directory is kept for historical diagnostic reference only. It is **not** included in the production build or deployment packages.

## Deployment Options

### Option A: Cloudflare Dashboard (Recommended, 30 seconds)
1. Open [Cloudflare Workers Dashboard](https://dash.cloudflare.com/) and navigate to `lazybean-meta`.
2. Click **Quick Edit** or **Edit code**.
3. Replace the contents of `worker.js` with `tools/cloudflare-worker/worker.js`.
4. Click **Deploy**.

### Option B: Wrangler CLI
```bash
cd tools/cloudflare-worker
npx wrangler deploy
```

## Endpoints

- `GET /health`: Healthcheck.
- `GET /diagnose?pageId=100456846237420`: Runs a diagnostic test directly against the public Meta Ad Library for CAFELAX (Page ID: `100456846237420`).
- `POST /api/ads/collect`: Collects and normalizes ads for a given `pageId` in Egypt (`EG`).
