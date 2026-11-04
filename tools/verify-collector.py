#!/usr/bin/env python3
"""
Single-Command Runner Host Verification Script.
Tests:
1. Meta Ads collector against CAFELAX (Page ID: 100456846237420) to ensure the runner IP is not blocked.
2. Google Ads Transparency connectivity.
3. Backend API authentication and identity endpoints (if API_BASE_URL and INTERNAL_TOKEN are set).
"""

import os
import sys
import json
import urllib.request
import urllib.error
from pathlib import Path

# Add tools/meta-ads to path
sys.path.insert(0, str(Path(__file__).parent / "meta-ads"))
try:
    from collect import collect_page_ads
except ImportError as e:
    print(f"[!] Warning: Could not import collect_page_ads ({e}). Ensure meta-ads-collector is installed.")
    collect_page_ads = None


def verify_meta(cafelax_page_id: str = "100456846237420") -> bool:
    print(f"\n[1/3] Verifying Meta Ads collection for CAFELAX ({cafelax_page_id})...")
    if collect_page_ads is None:
        print("  FAILED: meta-ads-collector is not installed in the current environment.")
        return False

    res = collect_page_ads(cafelax_page_id, country="EG", status="ALL", max_results=10)
    status = res.get("status")
    total = res.get("totalCollected", 0)
    print(f"  Status: {status}")
    print(f"  Ads Retrieved: {total}")

    if status == "Success" and total > 0:
        print("  -> Meta Ads verification PASSED! Runner IP has clean access to Meta Ad Library.")
        return True
    elif status == "Blocked":
        print("  -> Meta Ads verification BLOCKED! IP or subnet is rate-limited by Meta.")
        return False
    else:
        print(f"  -> Meta Ads verification FAILED: {res.get('error')}")
        return False


def verify_google() -> bool:
    print("\n[2/3] Verifying Google Ads Transparency connectivity...")
    test_url = "https://adstransparency.google.com/anonymized/lookup?authuser=0&hl=en&region=EG&adv_id=AR0123456789"
    req = urllib.request.Request(
        test_url,
        headers={
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Accept": "application/json, text/plain, */*"
        }
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = resp.read().decode("utf-8")
            print(f"  HTTP Response: {resp.status} (Length: {len(data)})")
            print("  -> Google Ads Transparency lookup PASSED.")
            return True
    except urllib.error.HTTPError as e:
        print(f"  HTTP Response: {e.code} ({e.reason})")
        if e.code in (403, 429):
            print("  -> Google Ads Transparency blocked/rate-limited direct scraper.")
            return False
        return True
    except Exception as e:
        print(f"  Connection error: {e}")
        return False


def verify_api() -> bool:
    api_base = os.environ.get("API_BASE_URL", "").rstrip("/")
    token = os.environ.get("INTERNAL_TOKEN", "").strip()

    print("\n[3/3] Verifying Backend API Internal Endpoints...")
    if not api_base or not token:
        print("  SKIPPED: API_BASE_URL and INTERNAL_TOKEN env vars are not set.")
        print("  (Run with API_BASE_URL=http://localhost:5188 INTERNAL_TOKEN=... to test backend ingestion).")
        return True

    url = f"{api_base}/api/internal/ads/meta/identities"
    req = urllib.request.Request(
        url,
        headers={"X-Internal-Token": token, "Accept": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            identities = json.loads(resp.read().decode("utf-8"))
            print(f"  HTTP Response: {resp.status}")
            print(f"  Found {len(identities)} approved Meta advertiser identities.")
            print("  -> Backend API verification PASSED.")
            return True
    except Exception as e:
        print(f"  Backend API check failed: {e}")
        return False


def main():
    print("=" * 65)
    print("LazyBean Ads Collector Host Verification Tool")
    print("=" * 65)

    meta_ok = verify_meta()
    google_ok = verify_google()
    api_ok = verify_api()

    print("\n" + "=" * 65)
    print("Verification Summary:")
    print(f" - Meta Ads (CAFELAX 100456846237420): {'READY' if meta_ok else 'ATTENTION NEEDED'}")
    print(f" - Google Ads Transparency:            {'READY' if google_ok else 'ATTENTION NEEDED'}")
    print(f" - Backend API Connectivity:          {'READY' if api_ok else 'ATTENTION NEEDED'}")
    print("=" * 65)

    if not meta_ok:
        sys.exit(1)


if __name__ == "__main__":
    main()
