#!/usr/bin/env python3
"""
Meta Ads Collection Script using meta-ads-collector (MIT).
Collects ads for a specific Facebook Page ID directly from the Meta Ad Library
without requiring Meta API tokens, user login, proxies, or CAPTCHA solving.
"""

import argparse
import json
import os
import sys
from datetime import datetime, timezone

try:
    from meta_ads_collector import MetaAdsCollector
    from meta_ads_collector.exceptions import (
        MetaAdsError,
        RateLimitError,
        SessionExpiredError,
        AuthenticationError,
    )
except ImportError as e:
    print(f"Error: missing dependency meta-ads-collector. Run 'pip install -r tools/meta-ads/requirements.txt'. Details: {e}", file=sys.stderr)
    sys.exit(1)


def normalize_ad(ad):
    """Normalize Ad object from meta_ads_collector into standard schema."""
    primary_creative = ad.creatives[0] if (ad.creatives and len(ad.creatives) > 0) else None
    
    body = primary_creative.body if primary_creative else None
    title = primary_creative.title if primary_creative else None
    link = primary_creative.link_url if primary_creative else None
    image = None
    if primary_creative:
        image = (
            getattr(primary_creative, "image_url", None)
            or getattr(primary_creative, "original_image_url", None)
            or getattr(primary_creative, "thumbnail_url", None)
        )

    # Standard public Ad Library snapshot URL
    snapshot = (
        ad.snapshot_url
        or ad.ad_snapshot_url
        or (f"https://www.facebook.com/ads/library/?id={ad.id}" if ad.id else None)
    )

    is_active = ad.is_active if ad.is_active is not None else (ad.ad_status == "ACTIVE" if ad.ad_status else True)

    return {
        "id": str(ad.id),
        "startDate": ad.delivery_start_time.isoformat() if ad.delivery_start_time else None,
        "stopDate": ad.delivery_stop_time.isoformat() if ad.delivery_stop_time else None,
        "status": "Active" if is_active else "Inactive",
        "body": body,
        "title": title,
        "link": link,
        "snapshotUrl": snapshot,
        "imageUrl": image,
        "publisherPlatforms": getattr(ad, "publisher_platforms", []),
    }


def collect_page_ads(page_id: str, country: str = "EG", status: str = "ALL", max_results: int = 50):
    """
    Collect ads for a single page ID.
    Differentiates between successful collection (including true 0 ads) and
    failures (RateLimit/Blocked/Failed).
    """
    collector = MetaAdsCollector()
    collected_ads = []

    try:
        # collect_by_page_id passes page_ids=[page_id] and search_type=PAGE
        # status="ALL" retrieves active + inactive ads
        iterator = collector.collect_by_page_id(
            page_id=str(page_id),
            country=country.upper(),
            status=status.upper(),
            max_results=max_results,
        )

        for ad in iterator:
            collected_ads.append(normalize_ad(ad))

        return {
            "status": "Success",
            "pageId": str(page_id),
            "country": country.upper(),
            "totalCollected": len(collected_ads),
            "ads": collected_ads,
            "error": None,
        }

    except RateLimitError as exc:
        return {
            "status": "Blocked",
            "pageId": str(page_id),
            "country": country.upper(),
            "totalCollected": 0,
            "ads": [],
            "error": f"Rate limit encountered from Meta: {exc}",
            "errorType": "RateLimitError",
        }
    except (AuthenticationError, SessionExpiredError) as exc:
        return {
            "status": "Blocked",
            "pageId": str(page_id),
            "country": country.upper(),
            "totalCollected": 0,
            "ads": [],
            "error": f"Session or verification challenge from Meta: {exc}",
            "errorType": exc.__class__.__name__,
        }
    except MetaAdsError as exc:
        err_msg = str(exc)
        is_blocked = any(kw in err_msg.lower() for kw in ["rate", "limit", "block", "challenge", "checkpoint", "captcha"])
        return {
            "status": "Blocked" if is_blocked else "Failed",
            "pageId": str(page_id),
            "country": country.upper(),
            "totalCollected": 0,
            "ads": [],
            "error": err_msg,
            "errorType": "MetaAdsError",
        }
    except Exception as exc:
        return {
            "status": "Failed",
            "pageId": str(page_id),
            "country": country.upper(),
            "totalCollected": 0,
            "ads": [],
            "error": f"Unexpected collection error: {exc}",
            "errorType": exc.__class__.__name__,
        }
    finally:
        try:
            collector.close()
        except Exception:
            pass


def main():
    parser = argparse.ArgumentParser(description="Collect Meta ads for a Facebook Page ID")
    parser.add_argument("--page-id", type=str, default="100456846237420", help="Numeric Facebook Page ID (default: 100456846237420 for CAFELAX)")
    parser.add_argument("--country", type=str, default="EG", help="ISO country code (default: EG)")
    parser.add_argument("--status", type=str, default="ALL", choices=["ALL", "ACTIVE", "INACTIVE"], help="Ad active status (default: ALL)")
    parser.add_argument("--max-results", type=int, default=50, help="Maximum ads to fetch (default: 50)")
    parser.add_argument("--output", type=str, default=None, help="Optional output JSON file path")
    parser.add_argument("--json", action="store_true", help="Print only raw JSON result")

    args = parser.parse_args()

    result = collect_page_ads(
        page_id=args.page_id,
        country=args.country,
        status=args.status,
        max_results=args.max_results,
    )

    if args.output:
        os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(result, f, indent=2, ensure_ascii=False)
        print(f"Results saved to {args.output}")

    if args.json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    else:
        print("=" * 60)
        print(f"Meta Ads Collection - Page ID: {result['pageId']}")
        print(f"Status: {result['status']}")
        print(f"Country: {result['country']}")
        print(f"Total Ads Collected: {result['totalCollected']}")
        if result["error"]:
            print(f"Error ({result.get('errorType')}): {result['error']}")
        print("=" * 60)

        sample_count = min(3, len(result["ads"]))
        if sample_count > 0:
            print(f"\nShowing {sample_count} sample ads:")
            for i, ad in enumerate(result["ads"][:sample_count], 1):
                print(f"\n--- Sample Ad #{i} ---")
                print(f"  ID:           {ad['id']}")
                print(f"  Start Date:   {ad['startDate']}")
                print(f"  Status:       {ad['status']}")
                print(f"  Body:         {(ad['body'][:120] + '...') if ad['body'] and len(ad['body']) > 120 else ad['body']}")
                print(f"  Title:        {ad['title']}")
                print(f"  Link:         {ad['link']}")
                print(f"  Snapshot URL: {ad['snapshotUrl']}")
                print(f"  Image URL:    {(ad['imageUrl'][:90] + '...') if ad['imageUrl'] and len(ad['imageUrl']) > 90 else ad['imageUrl']}")
        elif result["status"] == "Success":
            print("\nNo ads currently running/archived for this page in this country.")

    # Exit with appropriate exit code so callers/CI distinguish failure from success
    if result["status"] == "Blocked":
        sys.exit(2)
    elif result["status"] == "Failed":
        sys.exit(1)
    else:
        sys.exit(0)


if __name__ == "__main__":
    main()
