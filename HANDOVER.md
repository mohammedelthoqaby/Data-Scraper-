# LazyBean - System Handover & Operational Guide

This document covers how LazyBean runs, what it depends on, and what to do when something needs attention.

LazyBean has two parts:

- **The server** (`https://lazy-bean.runasp.net`): the .NET API, the dashboard, the database, and every scheduled step that does not need a home PC.
- **LazyBean Collector** (desktop app, Windows): runs on a PC in Egypt and collects the sources that block server addresses: Strong Coffee and Meta ads.

GitHub is no longer part of collection. There are no GitHub Actions workflows that collect data, no self-hosted runner, and no GitHub access token. The only workflow left is `build.yml`, which builds the code.

---

## 1. Architecture

```
  Strong Coffee (WooCommerce)      Meta Ad Library          Google Ads Transparency
            |                            |                            |
            v                            v                            v
  +----------------------------------------------+        +---------------------+
  |  LazyBean Collector (desktop, paired PC)     |        |  Server (fallback)  |
  |  - polls the job queue every 10 s            |        |  Google only, when  |
  |  - collects with the PC's own connection     |        |  no app is connected|
  |  - reports via /api/collector/*              |        +----------+----------+
  +-------------------+--------------------------+                   |
                      | device token (bearer)                         |
                      v                                               v
  +-----------------------------------------------------------------------------+
  |  Server (MonsterASP): ASP.NET Core API + React dashboard                    |
  |  - Shopify competitors collected on the server (no desktop needed)          |
  |  - job queue, pairing, ingest, classification, matching, AI triage, alerts  |
  |  - Quartz daily run at 07:00 Africa/Cairo                                   |
  +-----------------------------------+-----------------------------------------+
                                      |
                                      v
                           PostgreSQL (MonsterASP)
```

### What runs where

| Work | Where | When |
|---|---|---|
| Shopify competitors (Coffee Zone, Cafelax, Coffee & Cream, Fengany, Lazy Bean) | Server | Daily run, and manual runs |
| Strong Coffee (WooCommerce) catalog | Desktop app | Every run; skipped if no app picks it up within 10 minutes |
| Meta ads (approved Page IDs) | Desktop app | Every run, and **Collect ads now**; skipped if no app picks it up within 10 minutes |
| Google ads (approved advertisers) | Desktop app if connected, otherwise the server | Every run, and **Collect ads now** |
| Classification, matching, AI triage, alerts | Server | After collection finishes |

Meta blocks datacenter addresses, which is why Meta and Strong Coffee run on the desktop app. Google is the one ads source that works from the server, so it falls back to the server when no app is connected.

---

## 2. Runs and the Job Queue

A collection run creates these jobs, in this order:

1. **Store collection** for each desktop competitor (today: Strong Coffee).
2. **Meta ads** for the approved Meta identities.
3. **Google ads** for the approved Google identities.

Within a run, each job starts only after the job before it has finished. The run does not wait for Meta or Google: once the store jobs are done, the run continues to matching, AI triage, and alerts, and the ads jobs settle on their own. Ad alerts are recalculated when an ads job completes.

**Claims and leases.** The app claims a job by polling. A claim stays with that app as long as it sends heartbeats. If the app stops for 5 minutes, the job returns to the queue. After 3 attempts the job fails.

**The skip rule.** A desktop job that nobody claims within 10 minutes of becoming available is marked **Skipped (collector not running)**. A skipped store job is never treated as missing products: nothing is marked Missing or Removed, and the run carries on. Skipped ads jobs simply leave the existing ads as they were.

**Statuses on the Collection Runs page.** Each competitor and each ads source shows where it ran (Desktop app or Server) and how it ended (Running, Completed, Failed, Skipped). The run stays **Running** until collection, matching, AI triage, and alerts have all finished, and progress only reaches 100% at that point.

---

## 3. Pairing and Revoking the Desktop App

### Pairing
1. Open LazyBean Collector on the PC.
2. Keep the default server address `https://lazy-bean.runasp.net`.
3. Sign in with a dashboard username and password.

The app receives a device token once and stores it encrypted for the current Windows user. The server keeps only a SHA-256 hash of the token. Pairing is limited to 5 attempts per 10 minutes per IP address.

A device token can only call `/api/collector/*`. A dashboard login cannot call those endpoints, and a device token cannot call the dashboard endpoints.

### Revoking
Go to **Settings > Collector**, find the device under **Paired devices**, and click **Revoke**. The token stops working on its next request, and any job that device was holding goes back to the queue. To use the app again, pair it again.

### Checking the connection
The dashboard header shows **Collector: Connected**, **Collector: Working 2/3** (jobs finished in the current batch), or **Collector: Not running**. A device counts as connected while it has polled within the last 45 seconds.

---

## 4. Installing the Desktop App

The installer is `LazyBeanCollector.exe`. It is served from the dashboard at **Settings > Collector**, which shows the file size and its SHA-256 hash. Compare the hash before you run it.

1. Download the file from **Settings > Collector**.
2. Windows SmartScreen may say the app is not recognised. Click **More info**, then **Run anyway**.
3. Pair the app as described in section 3.
4. Optional: turn on **Start with Windows** in the app's window.

If the installer is not listed on the page, it has not been published to the server yet.

### Where the app keeps its data
- `%LOCALAPPDATA%\LazyBeanCollector\settings.json`: server address, device name, and the last sync per source. It holds no secrets.
- `%LOCALAPPDATA%\LazyBeanCollector\device-token.bin`: the device token, encrypted with Windows DPAPI for the current Windows user. Another Windows account, or another PC, cannot read it.
- `%LOCALAPPDATA%\LazyBeanCollector\logs\yyyy-MM-dd.log`: the activity log. The window shows the same lines.

### Uninstall
1. Turn off **Start with Windows** in the app, if it is on.
2. Click **Sign out** in the app. This revokes the device on the server.
3. Right-click the tray icon and choose **Exit**.
4. Delete `LazyBeanCollector.exe` and the `%LOCALAPPDATA%\LazyBeanCollector` folder.

### App closed or PC off
- Strong Coffee and Meta jobs wait for the app. After 10 minutes with no app, they are skipped and the run finishes.
- Google continues on the server, so Google ads are collected whenever the app is closed.
- Nothing is lost: the next run creates new jobs.

---

## 5. Secrets and Configuration

No secret values are stored in the repository. Set them in the MonsterASP control panel under **Website Settings > Environment Variables**.

| Variable | Purpose | Notes |
|---|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string | Points at MonsterASP PostgreSQL (PgBouncer transaction mode is supported) |
| `Admin__Username` / `Admin__Password` | First administrator login | Used when the admin user is first created |
| `Internal__Token` | Token for the `/api/internal/*` endpoints | Not used by the desktop app and not needed for collection |
| `Security__UseHttpsRedirection` | HTTPS redirect | `false`; TLS is terminated by the MonsterASP front end |

**Removed:** `GitHub__DispatchToken`, `GitHub__Repo`, and `GitHub__Ref`. They are no longer read, and the GitHub access token can be revoked in GitHub.

**In the app (encrypted in the database):** the Gemini API key, under **Settings > AI Configuration**.

---

## 6. Ads Intelligence

- **Meta identities.** Approved automatically on startup for the seeded Page IDs. To stop collecting a page, reject its identity under **Manage Sources > Advertiser Identities**. A rejection survives restarts.
- **Google identities.** Click **Discover Google Advertisers** next to a competitor, review the candidates, and approve the right one. The result says either how many advertisers were found, or that none were found.
- **Collect ads now.** Starts Meta and Google collection without a full run. It is on **Settings > Collector**, and in the desktop app (its window and tray menu).

---

## 7. Troubleshooting

| Symptom | Likely cause | What to do |
|---|---|---|
| Banner: "No LazyBean Collector is connected" | The app is closed, or the PC is off | Open the app. Google still runs on the server. |
| Strong Coffee shows "Skipped (collector not running)" | No app picked it up within 10 minutes | Open the app, then run again or use **Collect ads now** |
| App says the token is invalid or revoked | The device was revoked, or the token is stale | Pair the app again |
| Pairing returns 429 | More than 5 attempts in 10 minutes from this IP | Wait 10 minutes |
| Meta page returns 0 ads | Either the page really has no ads in Egypt, or Meta is blocking this PC | Check the Page ID under Advertiser Identities; compare with the Ad Library in a browser |
| Run stays "Running" | An ads or store job is still in progress, or the post-processing step is working | Check **Sources for this run**; the run completes once its store jobs and post-processing finish |

---

## 8. Publishing a Release

```powershell
# 1. Build the server (this also builds web/ and copies it into wwwroot)
dotnet publish backend/src/LazyBean.Api/LazyBean.Api.csproj -c Release -r win-x86 --self-contained false -o publish

# 2. Remove development settings
Remove-Item publish/appsettings.Development.json -ErrorAction SilentlyContinue

# 3. Optional: publish the desktop app and place it where the server serves it
#    publish/wwwroot/downloads/LazyBeanCollector.exe
```

Zip the contents of `publish/` (files at the root of the archive) and extract them into the MonsterASP site folder. The desktop installer is only offered on the Settings page when `wwwroot/downloads/LazyBeanCollector.exe` exists.

Database changes ship as EF Core migrations and are applied at startup. Never write schema changes as raw SQL at startup.
