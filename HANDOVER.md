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

Within a run, each job starts only after the job before it has finished. If a desktop app is connected when the run starts, the run waits for its Meta and Google jobs before matching, AI triage, and alerts. The progress shows the app's current step, for example "Meta page 4/5: Fengany". If no app is connected, the Meta job is skipped at once and the run carries on, and the Google job runs on the server as before. If the app disconnects for 5 minutes while the run waits, its unfinished ads jobs are skipped and the run carries on. Ad alerts are computed in the alerts phase, after the ads jobs.

**Claims and leases.** The app claims a job by polling. A claim stays with that app as long as it sends heartbeats. If the app stops for 5 minutes, the job returns to the queue. After 3 attempts the job fails.

**The skip rule.** A desktop job that nobody claims within 10 minutes of becoming available is marked **Skipped (collector not running)**. A skipped store job is never treated as missing products: nothing is marked Missing or Removed, and the run carries on. Skipped ads jobs simply leave the existing ads as they were.

**Statuses on the Collection Runs page.** Each competitor and each ads source shows where it ran (Desktop app or Server) and how it ended (Running, Completed, Failed, Skipped). The run stays **Running** until collection, the desktop ads jobs, matching, AI triage, and alerts have all finished. A run is **Partial** when a store or ads job was skipped or failed. It is **Completed** only when every source has finished and post-processing has recorded its phases.

**What the user sees.** While the desktop app has work for a run, the Collection Runs page shows "Keep LazyBean Collector open - collecting Meta ads (4/5)". When the app has nothing left, the page shows "Run complete - you can close the collector." The app itself shows "All done for this run - you can close the app" in its window and as a tray balloon.

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

### Password changes
A dashboard password change does not sign out paired desktop apps, because each app uses its own device token. To sign them out too, tick **Also sign out all paired desktop apps** on **Settings > Account & password**. Each app then has to be paired again.

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

### Window, tray and exit
- **X closes the window; collection keeps running** in the tray. The first time, a balloon says so. The window is simply closed; the work does not depend on it.
- **Double-click the tray icon** to open the window. Its menu has **Open**, **Collect ads now**, a status line (for example "Connected" or "Working 2/3"), and **Exit**. The tooltip shows the same status, and the icon has a green dot while a job runs.
- **Exit** (tray menu only) is the way out. If a job is running, the app asks first. The unfinished job is handed back to the server at once, so another PC or the next poll can take it.
- **Starting the app again** shows the running window instead of opening a second copy.

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
| `Admin__Username` / `Admin__Password` | First administrator login | Used when the admin user is created, and until its password is first changed on the website |
| `Admin__ResetPassword` | Password recovery | `true` applies `Admin__Password` once at startup. Remove it after use. See *Recovering the dashboard password*. |
| `Internal__Token` | Token for the `/api/internal/*` endpoints | Not used by the desktop app and not needed for collection |
| `Security__UseHttpsRedirection` | HTTPS redirect | `false`; TLS is terminated by the MonsterASP front end |

**Removed:** `GitHub__DispatchToken`, `GitHub__Repo`, and `GitHub__Ref`. They are no longer read, and the GitHub access token can be revoked in GitHub.

**Rotate:** `Internal__Token` must be a new random value of 64 characters or more. The development value that was once committed to this repository must never be used in production.

**In the app (encrypted in the database):** the Gemini API key, under **Settings > AI Configuration**.

### Developer environment variables

Nothing below is stored in the repository. Set them in your shell (or the IDE's run configuration) before using the tools.

| Variable | Used by | Purpose |
|---|---|---|
| `LAZYBEAN_DB_PASSWORD` | `backend/docker-compose.yml` | Password for the local Postgres `lazybean` user. Compose refuses to start without it. |
| `LAZYBEAN_TEST_DB_PASSWORD` | Integration tests | The same password, read by the test fixtures. CI sets it for each run. |
| `LAZYBEAN_ADMIN_PASSWORD` | `web/scripts/*.mjs` | Dashboard admin password for the local scripts. |
| `LAZYBEAN_TEST_AI_API_KEY` | `web/scripts/test-pgbouncer-end-to-end.mjs` | A Gemini test key. Use a key you can revoke. |

---

### Changing the dashboard password

Sign in, then open **Settings > Account & password**. Enter the current password, then the new one twice. The new password must be at least 10 characters and different from the current one. The page gives a strength hint as you type.

On success:

- This browser stays signed in. Every other browser session is signed out.
- Paired desktop apps keep working. Tick **Also sign out all paired desktop apps** to sign them out too.
- The page shows **Password changed on** with the date and time.

The change is refused unless the current password is right. Five attempts are allowed per client IP address every 15 minutes. The server logs each change and each refused attempt, never with a password.

`Admin__Password` creates the admin user, and until the password is first changed on the website it keeps the stored password in step with it. After that change, a restart never overwrites the stored password, and changing `Admin__Password` has no effect. Use the recovery below to replace it.

### Recovering the dashboard password

Use this only when the password is lost, or when you need to replace it from configuration. It sets the password to `Admin__Password` at the next startup, and it signs out every browser session of that user.

1. In MonsterASP **Website Settings > Environment Variables**, set `Admin__Password` to the new password, and set `Admin__ResetPassword` to `true`.
2. Restart the site. The log has a warning that the reset was applied.
3. Sign in with the new password. Then **remove `Admin__ResetPassword` and `Admin__Password`** from the environment, and restart the site again.

The reset is applied once for each value of `Admin__Password`. A restart with the same value does not undo a password changed on the website afterwards, and a different value is applied again. If `Admin__ResetPassword` is `true` but `Admin__Password` is empty, nothing changes, and the log says so.

For a development database, set the same two keys with user-secrets (`dotnet user-secrets set "Admin:Password" "..."` and `dotnet user-secrets set "Admin:ResetPassword" "true"`). Start the API once, then remove `Admin:ResetPassword`.

---

## 6. Ads Intelligence

- **Why an ad is not linked.** An unlinked ad says why: **No product info in this ad** (no copy and no landing link), **Links to homepage/collection** (the landing link is not a product page), or **No matching product in <competitor> catalog**. Google's listing has no landing link or copy. The desktop app reads each new or re-shown creative's detail (GetCreativeById), a few seconds apart, and sends it to the server. The detail gives the preview and variations, and the copy and landing link where a response carries them. The responses seen so far carry no copy or landing link, so after its detail is read a Google ad usually shows "No product info in this ad". Before its detail is read it shows "Link not checked yet".
- **Google formats** are Text, Image or Video. The listing gives the format, and the preview decides when it does not.
- **AI-read (optional, off by default).** In AI Configuration, Gemini can read the product name and price from the stored preview of up to N Google image ads a day. Only unlinked image ads are read, and each read counts toward the daily cap. Results are marked **AI-read**. The read runs inside the ingest request, with a 60-second budget, so the ads are saved even if it stops early.
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

Run this from the repository root, after the release commit is made:

```powershell
powershell -ExecutionPolicy Bypass -File tools/package-release.ps1
```

It writes these files to `artifacts/` (git-ignored):

- `LazyBeanCollector-<rev>.exe`: the desktop app, one self-contained win-x64 file.
- `lazybean-api-<rev>.zip`: the server package. It contains `wwwroot/downloads/LazyBeanCollector.exe`, so the installer is offered on **Settings > Collector**, and it has no `appsettings.Development.json`.

The script copies the exe into `wwwroot/downloads` only for the server publish, then removes the copy. The exe is never committed. Extract the zip into the MonsterASP site folder.

Settings > Collector shows the SHA-256 of the served installer, computed once at startup. Compare it with `Get-FileHash artifacts/LazyBeanCollector-<rev>.exe -Algorithm SHA256`.

---

## 9. Release Checklist

Work through this in order on the first deploy of this release.

1. **Remove the GitHub variables** from MonsterASP **Website Settings > Environment Variables**: `GitHub__DispatchToken`, `GitHub__Repo`, `GitHub__Ref`. Then revoke the GitHub token in GitHub. Nothing reads these any more.
2. **Rotate `Internal__Token`.** Set a new random value (64 characters or more) in the same place. Any script that calls `/api/internal/*` (for example `tools/verify-collector.py` with `INTERNAL_TOKEN`) must be given the new value.
3. **Check the admin login variables.** `Admin__Username` and `Admin__Password` must be set. Startup sets the admin password from `Admin__Password` only when the admin user is created, and until its password is first changed on the website. After that, use the recovery in section 5 to replace it. **Expect one sign-out:** the first start of this release signs out every open dashboard session, so sign in again after deploying.
4. **Back up the database** before the first start of this release.
5. **Deploy the server package.** Extract `lazybean-api-<rev>.zip` into the site folder and restart the site. Startup applies the migrations. The migration `RemoveLegacyCollectionHostHistoryRow` deletes one obsolete history row. No manual SQL is needed.
6. **Verify the deploy.** `GET /api/version` must return `<rev>`. Settings > Collector must show the installer's SHA-256, and it must match `Get-FileHash` on `LazyBeanCollector-<rev>.exe`.
7. **First pairing.** On the collecting PC, install the app (section 4, including the SmartScreen steps). Pair it with the dashboard admin username and password. The "No LazyBean Collector is connected" banner must clear within a minute.
8. **Confirm the first jobs.** Press **Collect ads now** on **Settings > Collector**. The status must show the batch as finished, with the Meta job completed by the app. Strong Coffee must be collected on the next run.

---

Database changes ship as EF Core migrations and are applied at startup. Never write schema changes as raw SQL at startup.
