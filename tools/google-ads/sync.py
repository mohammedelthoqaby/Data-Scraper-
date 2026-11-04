#!/usr/bin/env python3
"""
Google Ads Collector & Ingestion Runner.
Fetches approved Google advertiser identities from the LazyBean API,
queries the Google Ads Transparency Center, and reports active ads or Blocked back to the backend.
Cross-platform: runs on Windows and Linux runners identically.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request
from typing import Any, Dict, List, Optional


def get_required_env(key: str) -> str:
    val = os.environ.get(key, "").strip()
    if not val:
        print(f"Error: Required environment variable '{key}' is missing or empty.", file=sys.stderr)
        sys.exit(1)
    return val


def fetch_identities(api_base: str, token: str) -> List[Dict[str, Any]]:
    url = f"{api_base}/api/internal/ads/google/identities"
    req = urllib.request.Request(
        url,
        headers={
            "X-Internal-Token": token,
            "Accept": "application/json",
            "User-Agent": "LazyBean-GoogleAds-Collector/1.0"
        }
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def post_results(api_base: str, token: str, payload: Dict[str, Any]) -> Dict[str, Any]:
    url = f"{api_base}/api/internal/ads/google"
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "X-Internal-Token": token,
            "Content-Type": "application/json",
            "Accept": "application/json",
            "User-Agent": "LazyBean-GoogleAds-Collector/1.0"
        }
    )
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def check_google_transparency(advertiser_id: str, region: str = "EG") -> Dict[str, Any]:
    """
    Direct probe to Google Ads Transparency endpoint for the advertiser ID.
    If challenge/captcha or anti-bot blocks connection, return Blocked status.
    """
    # Google Ads Transparency API direct query URL pattern
    target_url = f"https://adstransparency.google.com/anonymized/lookup?authuser=0&hl=en&region={region}&adv_id={advertiser_id}"
    req = urllib.request.Request(
        target_url,
        headers={
            "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Accept": "application/json, text/plain, */*",
            "Accept-Language": "en-US,en;q=0.9"
        }
    )
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = resp.read().decode("utf-8")
            if "lookup" in data:
                return {"status": "Success", "ads": [], "error": None}
            return {
                "status": "Blocked",
                "ads": [],
                "error": "Google Ads Transparency requires interactive verification or returned non-standard payload."
            }
    except urllib.error.HTTPError as e:
        if e.code in (403, 429):
            return {
                "status": "Blocked",
                "ads": [],
                "error": f"Google Ads Transparency returned HTTP {e.code} (Anti-bot/Rate Limit)."
            }
        return {
            "status": "Failed",
            "ads": [],
            "error": f"HTTP {e.code}: {e.reason}"
        }
    except Exception as e:
        return {
            "status": "Blocked",
            "ads": [],
            "error": f"Connection blocked or timed out: {e}"
        }


def main():
    api_base = get_required_env("API_BASE_URL").rstrip("/")
    token = get_required_env("INTERNAL_TOKEN")

    print("=" * 65)
    print("LazyBean Google Ads Collector Sync Pass")
    print(f"Target API Base: {api_base}")
    print("=" * 65)

    try:
        identities = fetch_identities(api_base, token)
    except Exception as e:
        print(f"Failed to fetch Google identities: {e}", file=sys.stderr)
        sys.exit(1)

    print(f"Total Google identities found: {len(identities)}")
    approved = [i for i in identities if str(i.get("status", "")).lower() == "approved"]
    print(f"Approved identities to sync: {len(approved)}")

    if not approved:
        print("No approved Google identities found. Exiting.")
        return

    for idx, identity in enumerate(approved, 1):
        adv_id = identity.get("externalId")
        name = identity.get("name")
        print(f"\n[{idx}/{len(approved)}] Querying Google Ads for '{name}' ({adv_id})...")

        probe_result = check_google_transparency(adv_id, region="EG")
        status = probe_result["status"]
        ads = probe_result.get("ads", [])
        error = probe_result.get("error")

        print(f"  -> Result: {status} ({len(ads)} ads)")
        if error:
            print(f"  -> Details: {error}")

        payload = {
            "advertiserId": adv_id,
            "status": status,
            "region": "EG",
            "error": error,
            "ads": ads
        }

        try:
            res = post_results(api_base, token, payload)
            print(f"  -> Backend response: {res}")
        except Exception as e:
            print(f"  -> Failed to post to backend: {e}", file=sys.stderr)

        time.sleep(5)

    print("\n" + "=" * 65)
    print("Google Ads sync pass finished.")
    print("=" * 65)


if __name__ == "__main__":
    main()
