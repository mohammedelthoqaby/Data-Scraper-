# LazyBean Meta Ads Cloudflare Worker

This worker provides serverless Meta Ad Library extraction for LazyBean without requiring self-hosted runners or paid proxies.

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
