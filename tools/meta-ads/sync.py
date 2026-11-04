#!/usr/bin/env python3
"""
Meta Ads Ingestion Runner.
Fetches approved Meta identities from the LazyBean API, runs collection per page ID,
and posts the results back to the backend ingestion endpoint.
Includes polite inter-page delays (20-40s) to prevent rate limiting.
Cross-platform: runs on Windows and Linux runners identically.
"""

import json
import os
import random
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

# Add script directory to sys.path so collect.py can be imported directly
sys.path.insert(0, str(Path(__file__).parent))
from collect import collect_page_ads


def get_required_env(key: str) -> str:
    val = os.environ.get(key, "").strip()
    if not val:
        print(f"Error: Required environment variable '{key}' is missing or empty.", file=sys.stderr)
        sys.exit(1)
    return val


def main():
    api_base = get_required_env("API_BASE_URL").rstrip("/")
    token = get_required_env("INTERNAL_TOKEN")
    min_delay = int(os.environ.get("DELAY_MIN_SECONDS", "20"))
    max_delay = int(os.environ.get("DELAY_MAX_SECONDS", "40"))
    page_limit = int(os.environ.get("MAX_ADS_PER_PAGE", "50"))

    print("=" * 65)
    print("LazyBean Meta Ads Collector Sync Pass")
    print(f"Target API Base: {api_base}")
    print(f"Inter-page Delay Window: {min_delay}s - {max_delay}s")
    print("=" * 65)

    # 1. Fetch approved Meta advertiser identities
    identities_url = f"{api_base}/api/internal/ads/meta/identities"
    req = urllib.request.Request(
        identities_url,
        headers={
            "X-Internal-Token": token,
            "Accept": "application/json",
            "User-Agent": "LazyBean-MetaAds-Collector/1.0"
        }
    )

    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            identities = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        print(f"Failed to fetch Meta identities: HTTP {e.code} - {e.read().decode('utf-8')}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Network error fetching Meta identities: {e}", file=sys.stderr)
        sys.exit(1)

    print(f"Found {len(identities)} approved Meta advertiser identities.")
    if not identities:
        print("No approved Meta identities to sync. Exiting.")
        return

    results_summary = []

    for idx, identity in enumerate(identities, 1):
        page_id = identity.get("pageId") or identity.get("externalId")
        comp_name = identity.get("competitorName") or identity.get("name")
        print(f"\n[{idx}/{len(identities)}] Processing {comp_name} (Page ID: {page_id})...")

        # Collect ads for this page
        collect_res = collect_page_ads(
            page_id=page_id,
            country="EG",
            status="ALL",
            max_results=page_limit
        )

        status = collect_res["status"]
        ads_count = collect_res["totalCollected"]
        error = collect_res.get("error")

        print(f"  -> Collection status: {status} | Ads returned: {ads_count}")
        if error:
            print(f"  -> Diagnostics: {error}")

        # Post result to backend
        ingest_payload = {
            "pageId": str(page_id),
            "status": status,
            "error": error,
            "ads": collect_res.get("ads", [])
        }

        post_url = f"{api_base}/api/internal/ads/meta"
        post_req = urllib.request.Request(
            post_url,
            data=json.dumps(ingest_payload).encode("utf-8"),
            headers={
                "X-Internal-Token": token,
                "Content-Type": "application/json",
                "Accept": "application/json",
                "User-Agent": "LazyBean-MetaAds-Collector/1.0"
            }
        )

        try:
            with urllib.request.urlopen(post_req, timeout=40) as post_resp:
                post_body = json.loads(post_resp.read().decode("utf-8"))
                print(f"  -> Ingestion result: {post_body}")
        except urllib.error.HTTPError as e:
            print(f"  -> Ingestion failed: HTTP {e.code} - {e.read().decode('utf-8')}", file=sys.stderr)
        except Exception as e:
            print(f"  -> Ingestion network error: {e}", file=sys.stderr)

        results_summary.append({
            "name": comp_name,
            "pageId": page_id,
            "status": status,
            "count": ads_count,
            "error": error
        })

        # Polite inter-page delay if there are more pages to process
        if idx < len(identities):
            delay = random.uniform(min_delay, max_delay)
            print(f"Waiting {delay:.1f}s before next page...")
            time.sleep(delay)

    print("\n" + "=" * 65)
    print("Meta Ads Sync Pass Finished")
    print("Summary:")
    for r in results_summary:
        print(f" - {r['name']} ({r['pageId']}): Status={r['status']}, Ads={r['count']}")
    print("=" * 65)


if __name__ == "__main__":
    main()
