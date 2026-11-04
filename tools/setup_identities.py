#!/usr/bin/env python3
"""
Setup and Approve Real Meta Page Identities via the API.
"""

import json
import sys
import urllib.request
import urllib.parse
from http.cookiejar import CookieJar

API_BASE = "http://localhost:5188"
INTERNAL_TOKEN = "dev-internal-token-secret-12345"

TARGET_IDENTITIES = [
    {
        "competitorKey": "cafelax",
        "competitorId": "b37c47a6-6306-47ee-be16-6aefdfa59707",
        "name": "CAFELAX",
        "pageId": "100456846237420"
    },
    {
        "competitorKey": "strong-coffee",
        "competitorId": "233eb56f-f08f-454c-aec0-37763555c5a8",
        "name": "Strong Coffee",
        "pageId": "102631558265863"
    },
    {
        "competitorKey": "coffee-and-cream",
        "competitorId": "7407ec0a-f1d4-4fa8-96fe-469d23768183",
        "name": "Coffee & Cream",
        "pageId": "109786085108938"
    },
    {
        "competitorKey": "coffee-zone",
        "competitorId": "51564006-eca2-421a-a46f-c636ab861d98",
        "name": "Coffee Zone Misr",
        "pageId": "110140451538461"
    },
    {
        "competitorKey": "fengany",
        "competitorId": "de583ab5-93df-4c1d-8d6f-42ae0296090b",
        "name": "Fengany",
        "pageId": "833564813655781"
    },
    {
        "competitorKey": "lazy-bean",
        "competitorId": "1a19c725-b430-467c-9f65-f3d87b4db3e1",
        "name": "Lazy Bean",
        "pageId": "433792133147901"
    }
]

cj = CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cj))


def api_request(method, path, body=None, headers=None):
    url = f"{API_BASE}{path}"
    h = {"Accept": "application/json"}
    if body is not None:
        h["Content-Type"] = "application/json"
        data = json.dumps(body).encode("utf-8")
    else:
        data = None
    if headers:
        h.update(headers)

    req = urllib.request.Request(url, data=data, headers=h, method=method)
    with opener.open(req) as resp:
        content = resp.read().decode("utf-8")
        if content:
            return json.loads(content)
        return None


def main():
    print("1. Logging in as admin...")
    login_res = api_request("POST", "/api/auth/login", {"username": "admin", "password": "Dev-Only-P@ssw0rd!"})
    print(f"Logged in: {login_res}")

    print("\n2. Fetching current identities...")
    identities = api_request("GET", "/api/identities")
    print(f"Current identities count: {len(identities)}")

    # Reject old guessed CAFELAX identity if present
    for i in identities:
        if i.get("platform") == "Meta" and i.get("externalId") == "1049284729102" and i.get("status") != "Rejected":
            print(f"Rejecting old guessed CAFELAX identity {i['id']}...")
            try:
                api_request("POST", f"/api/identities/{i['id']}/reject", {"notes": "Replaced by verified page ID 100456846237420"})
            except Exception as e:
                print(f"Failed to reject: {e}")

    # Process 6 targets
    for t in TARGET_IDENTITIES:
        page_id = t["pageId"]
        comp_id = t["competitorId"]
        name = t["name"]
        print(f"\nProcessing {name} (Page ID: {page_id})...")

        # Find existing
        matched = [i for i in identities if i.get("platform") == "Meta" and i.get("externalId") == page_id]
        if matched:
            identity_id = matched[0]["id"]
            current_status = matched[0]["status"]
            print(f"  Found existing identity {identity_id} (Status: {current_status})")
        else:
            print(f"  Creating new identity for {name} ({page_id})...")
            create_payload = {
                "competitorId": comp_id,
                "platform": "Meta",
                "externalId": page_id,
                "name": name,
                "notes": "Verified real Meta page ID"
            }
            res = api_request("POST", "/api/identities", create_payload)
            identity_id = res["id"]
            current_status = res["status"]
            print(f"  Created: {identity_id} (Status: {current_status})")

        if current_status != "Approved":
            print(f"  Approving identity {identity_id}...")
            appr_res = api_request("POST", f"/api/identities/{identity_id}/approve", {"notes": "Approved real page ID"})
            print(f"  Approved! New status: {appr_res.get('status')}")
        else:
            print("  Already approved.")

    print("\n3. Verifying via GET /api/internal/ads/meta/identities...")
    approved_meta = api_request("GET", "/api/internal/ads/meta/identities", headers={"X-Internal-Token": INTERNAL_TOKEN})
    print(f"Approved Meta identities ({len(approved_meta)} total):")
    for a in approved_meta:
        print(f" - [{a['competitorKey']}] {a['competitorName']} -> Page ID: {a['pageId']}")


if __name__ == "__main__":
    main()
