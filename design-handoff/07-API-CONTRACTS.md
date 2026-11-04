# Frontend API Contracts & Data Schema — Lazy Bean Market Intelligence

This document specifies the complete API contracts consumed by the Lazy Bean frontend. It maps every HTTP endpoint, query parameter, request payload, and consumed JSON property to support an independent backend re-implementation or frontend stack migration.

---

## 1. Authentication & Session Contracts

### 1.1 Session Discovery
- **Endpoint**: `GET /api/session`
- **Auth**: None (Public)
- **Response**:
  ```json
  {
    "mode": "local_preview"
  }
  ```
  *(Returns `"local_preview"` when accessing via localhost/127.0.0.1 without auth requirements, or `"token"` if Bearer authorization is enforced).*
- **Consumed By**: `boot()` in `app.js:210`. Controls whether `#login` dialog is presented or skipped.

---

### 1.2 Authenticated Headers
When in `token` mode, all `/api/*` requests require:
```http
Authorization: Bearer <token>
```
On `401 Unauthorized`, frontend traps the error, clears token, and displays `#login` modal.

---

## 2. Snapshot & Collection Execution Endpoints

### 2.1 List Collection Snapshots
- **Endpoint**: `GET /api/runs`
- **Auth**: Required
- **Response**:
  ```json
  [
    {
      "id": "ea0e412df4a846cf9fb1e51833d3d868",
      "key": "manual:12345",
      "state": "completed",
      "created_at": "2026-11-02T17:30:41.042343+00:00",
      "finished_at": "2026-11-02T17:42:15.112000+00:00",
      "lease_until": "1970-01-01T00:00:00+00:00",
      "error": null,
      "accepted_count": 1330
    }
  ]
  ```
- **Consumed By**: `load()`, `checkFreshCollection()`, `renderHistoryArchive()`.

---

### 2.2 Get Detailed Snapshot
- **Endpoint**: `GET /api/runs/{run_id}`
- **Auth**: Required
- **Response Properties Used by Frontend**:
  ```json
  {
    "id": "ea0e412df4a846cf9fb1e51833d3d868",
    "state": "completed",
    "created_at": "2026-11-02T17:30:41.042343+00:00",
    "finished_at": "2026-11-02T17:42:15.112000+00:00",
    "lease_until": "1970-01-01T00:00:00+00:00",
    "last_successful_run_id": "ce8748e2fe364a0d8afca53c49207f7c",
    "config": {
      "execution_runtime": "native",
      "cancellation_protocol": 1,
      "collection_kind": "catalog"
    },
    "sources": [
      {
        "source_id": "coffee-and-cream",
        "state": "completed",
        "error": null,
        "observed_at": "2026-11-02T17:35:10+00:00",
        "stats": {
          "accepted_products": 210,
          "fetched_products": 245,
          "total_observations": 260,
          "rejected_products": 15,
          "completed_scope": true,
          "truncated_scope": false,
          "batch": 10,
          "total_batches": 10
        }
      }
    ],
    "ads": [
      {
        "source_id": "coffee-and-cream",
        "platform": "meta",
        "state": "accepted",
        "data": {
          "observed_at": "2026-11-02T17:30:00+00:00",
          "observed_ads": 12,
          "candidate_ads_observed": 0,
          "credential_readiness": "ready",
          "blockers": []
        }
      }
    ],
    "report": {
      "narrative_en": "Recorded 1330 total observations...",
      "own_price_recommendations_available": true,
      "limitations": [
        "Store stock values are merchant claims."
      ],
      "observation_counts": {
        "total": 1330,
        "accepted": 1251,
        "quarantined": 79
      },
      "changes": [
        {
          "title": "Lavazza Oro 250g Ground",
          "source_id": "cafelax",
          "kind": "observed_price_change"
        }
      ],
      "recommendations": [
        {
          "title": "Illy Decaffeinato 250g Beans",
          "action": "review_price",
          "rule": "RULE-PRICE-GAP",
          "reason": "Observed competitor prices support a closer look.",
          "own_price_minor": 48000,
          "median_minor": 42000,
          "price_gap_pct": "-12.5",
          "store_count": 3,
          "stock_gate": "stock_verified",
          "observed_at": "2026-11-02T17:35:00+00:00",
          "own_url": "https://lazybean.co/products/illy-decaf-beans",
          "missing_data": [],
          "competitors": [
            {
              "source_id": "cafelax",
              "price_minor": 41000,
              "observed_at": "2026-11-02T17:35:00+00:00",
              "url": "https://www.cafelax.com/products/illy-decaf"
            }
          ]
        }
      ],
      "comparisons": [
        {
          "title": "Lavazza Qualita Oro 250g",
          "comparison_type": "exact_brand_weight",
          "own_price_minor": 42000,
          "median_minor": 43000,
          "price_gap_pct": "-2.3",
          "store_count": 3
        }
      ]
    }
  }
  ```
- **Consumed By**: `selectSnapshot()`, `pollCurrentRun()`, `render()`.

---

### 2.3 Create Collection Snapshot
- **Endpoint**: `POST /api/runs`
- **Request Body**:
  ```json
  {
    "source_ids": ["coffee-and-cream"] || null,
    "pilot": true,
    "draft": false
  }
  ```
- **Response**:
  ```json
  {
    "run_id": "34597db93aa5463592990ec44e4d8d52",
    "state": "running",
    "already_active": false
  }
  ```

---

### 2.4 Cancel Active Run
- **Endpoint**: `POST /api/runs/{run_id}/cancel`
- **Request Body**: None
- **Response**:
  ```json
  {
    "run_id": "34597db93aa5463592990ec44e4d8d52",
    "state": "cancelled"
  }
  ```

---

### 2.5 Collection Preflight Readiness
- **Endpoint**: `GET /api/collection/preflight?pilot={bool}&source_id={id}`
- **Response**:
  ```json
  {
    "ready": true,
    "message": "Ready to collect.",
    "state": "ready",
    "billing_url": "https://brightdata.com/cp/billing",
    "provider": {
      "cycle": "2026-11-01",
      "reset_at": "2026-12-01"
    }
  }
  ```

---

## 3. Complete Price Comparison Matrix Endpoints

### 3.1 Get Snapshot Price Matrix
- **Endpoint**: `GET /api/runs/{run_id}/price-matrix`
- **Auth**: Required
- **Response Payload**:
  ```json
  {
    "run_id": "ea0e412df4a846cf9fb1e51833d3d868",
    "total_own_products": 1251,
    "matched_products_count": 121,
    "categories": ["Whole Beans", "Ground Coffee", "Capsules"],
    "brands": ["lavazza", "illy", "davidoff"],
    "competitors": [
      {"id": "coffee-and-cream", "name": "Coffee & Cream"},
      {"id": "cafelax", "name": "CAFELAX"},
      {"id": "fengany", "name": "Fengany"},
      {"id": "coffee-zone-misr", "name": "Coffee Zone Misr"},
      {"id": "strong-coffee", "name": "Strong Coffee"}
    ],
    "total_competitor_products": {
      "coffee-and-cream": 210,
      "cafelax": 340,
      "fengany": 280,
      "coffee-zone-misr": 190,
      "strong-coffee": 150
    },
    "competitor_catalog": {
      "cafelax": [
        {
          "observation_id": "obs_999",
          "title": "Illy Decaf Ground 250g",
          "price_minor": 41000
        }
      ]
    },
    "rows": [
      {
        "product_id": "prod_1",
        "title": "Lavazza Qualita Oro Ground Coffee 250g",
        "brand": "lavazza",
        "category": "Ground Coffee",
        "subline": "Tin",
        "size_label": "250g",
        "vendor_name": "Lavazza Italy",
        "own_url": "https://lazybean.co/products/lavazza-oro-ground",
        "own_price_minor": 42000,
        "own_reference_minor": null,
        "own_currency": "EGP",
        "own_availability": "in_stock",
        "own_observed_at": "2026-11-02T17:30:00+00:00",
        "own_in_current_run": true,
        "own_attributes": {
          "brand": "Lavazza",
          "net_weight_g": 250
        },
        "median_minor": 43000,
        "price_gap_pct": "-2.3",
        "matched_count": 3,
        "by_competitor": {
          "coffee-and-cream": {
            "price_minor": 45000,
            "reference_minor": 48000,
            "currency": "EGP",
            "diff_pct": "+7.1",
            "match_status": "exact_auto",
            "match_basis": "Identical brand, variant & 250g pack weight",
            "title": "Lavazza Oro Ground 250g",
            "url": "https://coffeeandcream.app/products/oro-250g",
            "observed_at": "2026-11-02T17:35:00+00:00",
            "availability": "in_stock",
            "offer_scope": "regular",
            "in_current_run": true
          }
        }
      }
    ]
  }
  ```
- **Consumed By**: `mountMatrixDOM()`, `renderPriceMatrix()`, `renderHistoricalPriceMatrix()`.

---

### 3.2 Export Price Matrix CSV
- **Endpoint**: `GET /api/runs/{run_id}/export-matrix.csv`
- **Response**: `text/csv` attachment.

---

## 4. Weekly Actions & Offers Endpoints

### 4.1 Competitor Offers
- **Endpoint**: `GET /api/runs/{run_id}/competitor-offers`
- **Response**:
  ```json
  {
    "offers": [
      {
        "observation_id": "obs_cafelax_01",
        "source_id": "cafelax",
        "title": "Illy Decaf Ground Coffee 250g",
        "type": "Clearance discount",
        "terms": "20% off while supplies last",
        "scope": "Monitored pages",
        "price_minor": 41000,
        "reference_minor": 51000,
        "currency": "EGP",
        "discount_pct": "20",
        "availability": "in_stock",
        "observed_at": "2026-11-02T17:35:00+00:00",
        "url": "https://www.cafelax.com/products/illy-decaf",
        "match_state": "single",
        "our_product": {
          "title": "Illy Decaffeinato 250g Ground",
          "price_minor": 48000
        }
      }
    ],
    "needs_review": [
      {
        "id": "obs_unclear_01",
        "title": "Special Blend 500g Buy 1 Get 1",
        "source_id": "fengany",
        "observed_at": "2026-11-02T17:35:00+00:00",
        "url": "https://fengany.com/products/bundle-1"
      }
    ]
  }
  ```

---

## 5. Review Queue & Product Matching Endpoints

### 5.1 List Reviews
- **Endpoint**: `GET /api/reviews`
- **Response**:
  ```json
  {
    "matches": [
      {
        "id": "match_123",
        "source_id": "cafelax",
        "variant_key": "cafelax:sku_456",
        "basis": "Fuzzy brand and pack title similarity 88%",
        "product": {
          "id": "prod_1",
          "title": "Illy Decaffeinato 250g Beans",
          "attributes": {
            "net_weight_g": 250,
            "capsule_count": null,
            "capsule_system": null,
            "model_number": null
          }
        },
        "listing": {
          "id": "obs_789",
          "title": "Illy Espresso Decaf Whole Beans 250g",
          "url": "https://www.cafelax.com/products/illy-beans-decaf",
          "price_minor": 41000,
          "reference_minor": null,
          "currency": "EGP",
          "availability": "in_stock",
          "observed_at": "2026-11-02T17:35:00+00:00",
          "data": {
            "attributes": {
              "net_weight_g": 250,
              "brand": "Illy"
            }
          }
        }
      }
    ],
    "identities": [
      {
        "id": "ident_meta_cafelax",
        "source_id": "cafelax",
        "platform": "meta",
        "status": "needs_identity_review",
        "advertiser_id": null,
        "data": {
          "provider_identity_candidates": [
            {
              "advertiser_id": "1049284729102",
              "display_name": "Cafelax Egypt",
              "library_url": "https://www.facebook.com/ads/library/?id=1049284729102",
              "public_detail_url": "https://www.facebook.com/ads/library/?id=1049284729102",
              "ownership_url": "https://www.cafelax.com",
              "domain_match": true,
              "unresolved_question": "Confirm merchant ownership via official footer links."
            }
          ]
        }
      }
    ]
  }
  ```

---

### 5.2 Submit Match Decision
- **Endpoint**: `POST /api/matches/{match_id}/decision`
- **Body**:
  ```json
  {
    "notes": "Verified identical 250g tin weight and decaf roast variant.",
    "decision": "approve" // or "reject"
  }
  ```

---

### 5.3 Propose / Approve Match
- **Endpoint**: `POST /api/matches`
- **Body**:
  ```json
  {
    "product_id": "prod_1",
    "observation_id": "obs_789",
    "approve": true,
    "notes": "Manually linked and approved from price comparison matrix"
  }
  ```

---

### 5.4 Approve Advertiser Identity
- **Endpoint**: `POST /api/identities/{identity_id}/approve`
- **Body**:
  ```json
  {
    "advertiser_id": "1049284729102",
    "display_name": "Cafelax Egypt",
    "library_url": "https://www.facebook.com/ads/library/?id=1049284729102",
    "ownership_evidence_url": "https://www.cafelax.com",
    "notes": "Verified business address on Facebook matches Trivium branch listed on cafelax.com."
  }
  ```

---

## 6. Sources Management Endpoints

### 6.1 List Monitored Sources
- **Endpoint**: `GET /api/sources`
- **Response**: Array of source registry definitions (`id`, `name`, `role`, `enabled`, `config`).

---

### 6.2 Update Monitoring Status
- **Endpoint**: `PATCH /api/sources/{source_id}`
- **Body**:
  ```json
  {
    "enabled": true
  }
  ```

---

### 6.3 Update Source Configuration
- **Endpoint**: `PUT /api/sources/{source_id}/config`
- **Body**:
  ```json
  {
    "adapter_id": "commerce",
    "entry_points": ["https://..."],
    "notes": "Updated collections paths for seasonal catalogue."
  }
  ```

---

### 6.4 Source Pilot Validation Sample
- **Endpoint**: `GET /api/sources/{source_id}/validation-sample?run_id={run_id}`
- **Response**:
  ```json
  {
    "source_id": "coffee-and-cream",
    "sample": [
      {
        "id": "obs_1",
        "title": "Lavazza Crema e Gusto Ground 250g",
        "price_minor": 32000,
        "currency": "EGP",
        "availability": "in_stock",
        "cohort": "regular",
        "url": "https://coffeeandcream.app/products/crema-gusto"
      }
    ]
  }
  ```

---

### 6.5 Approve Source Validation
- **Endpoint**: `POST /api/sources/{source_id}/validate`
- **Body**:
  ```json
  {
    "run_id": "ea0e412df4a846cf9fb1e51833d3d868",
    "verified_observation_ids": ["obs_1", "obs_2", "..."],
    "robots_checked": true,
    "access_terms_reviewed": true,
    "notes": "Checked 10 products against real web pages; prices and availability verified."
  }
  ```

---

## 7. History Archive Endpoints

### 7.1 Historical Price Logs
- **Endpoint**: `GET /api/history/prices`
- **Response Properties Used**:
  - `total_observations`: Total historical rows.
  - `own_observations_count`: Historical Lazy Bean rows.
  - `competitor_observations_count`: Historical competitor rows.
  - `snapshots`: Array of snapshot metadata records.
  - `stores`: List of store names with item counts.
  - `brands`: List of unique brand names.
  - `records`: Array of individual price records (`observed_at`, `run_id`, `source_name`, `title`, `brand`, `category`, `price_minor`, `previous_price_minor`, `change_pct`, `availability`, `url`).

---

### 7.2 Export Full Price History CSV
- **Endpoint**: `GET /api/history/prices.csv`
- **Response**: `text/csv` stream attachment.

---

## 8. Integration & Provider Status Endpoints

### 8.1 System Integrations Readiness
- **Endpoint**: `GET /api/integrations`
- **Response Properties Used**:
  - `evidence_storage.state`: `ready` / `degraded`.
  - `ai.state`: Gemini model integration status.
  - `paid_ads`: `credential_configured`, `state`, `setup_url`, `connectors`.
  - `providers`: Array of quota records (`provider`, `provider_available`, `last_observed_provider_remaining`, `application_committed`, `application_cycle_cap`, `pending_reservations`, `safety_reserve`, `usable_headroom`, `application_run_cap`, `state`, `reset_at`).
  - `official_meta`: `credential_configured`, `state`, `checked_at`, `check.status_note`.
  - `brightdata`: `credential_configured`, `authentication`, `entitlement_state`, `capabilities`.
  - `websites.routes`: Source route provider associations.

---

### 8.2 Save Meta Graph API Token
- **Endpoint**: `POST /api/integrations/meta-official/token`
- **Body**: `{"token": "EA..."}`
- **Response**: `{state: string, status_note: string}`

---

### 8.3 Verify Meta Graph API Token
- **Endpoint**: `POST /api/integrations/meta-official/check`
- **Response**: `{state: string, status_note: string}`

---

## 9. Internal Data Import Endpoints

### 9.1 Preview CSV / XLSX Upload
- **Endpoint**: `POST /api/imports/preview`
- **Request**: `multipart/form-data` with `file: File`.
- **Response**:
  ```json
  {
    "id": "imp_123",
    "state": "preview", // or "error"
    "message": "File ready for review."
  }
  ```

---

### 9.2 Commit Internal Import
- **Endpoint**: `POST /api/imports/{import_id}/commit`
- **Response**:
  ```json
  {
    "id": "imp_123",
    "state": "committed"
  }
  ```
