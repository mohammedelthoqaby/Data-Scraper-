# Self-Hosted Ads Collector Host Guide

This document describes how to configure, run, and migrate the self-hosted GitHub Actions runner used for weekly Meta Ads collection (`meta-ads.yml`). (Google Ads collection runs directly in the C# backend on MonsterASP).

The workflow targets the labels:
```yaml
runs-on: [self-hosted, lazybean-collector]
```

---

## 1. Local Windows Runner Setup (Current Dev Machine)

Use this setup on your local Windows PC to collect Meta and Google Ads directly from your residential IP without triggering Meta datacenter blocks.

### 1.1 Prerequisites
- **Git for Windows**: Installed and available in PATH.
- **Python 3.10+**: `python --version` returns Python 3.10 or higher.
- **PowerShell 7 (`pwsh`)**: Install via `winget install Microsoft.PowerShell` (or standard Windows PowerShell 5.1).
- GitHub Repo Admin permissions on `mohammedelthoqaby/Data-Scraper-`.

### 1.2 Download & Configure Runner
1. Open GitHub repo -> **Settings** -> **Actions** -> **Runners** -> **New self-hosted runner** -> **Windows**.
2. Open PowerShell as Administrator and execute:
   ```powershell
   # Create runner folder
   mkdir C:\actions-runner; cd C:\actions-runner

   # Download runner binary (v2.321.0 or latest)
   Invoke-WebRequest -Uri https://github.com/actions/runner/releases/download/v2.321.0/actions-runner-win-x64-2.321.0.zip -OutFile actions-runner.zip
   Expand-Archive -Path actions-runner.zip -DestinationPath .
   Remove-Item actions-runner.zip

   # Configure runner with required labels
   .\config.cmd --url https://github.com/mohammedelthoqaby/Data-Scraper- `
                --token <RUNNER_REGISTRATION_TOKEN> `
                --labels self-hosted,lazybean-collector `
                --name windows-dev-collector `
                --unattended --replace
   ```

### 1.3 Install and Run as a Background Windows Service
To ensure the runner stays active without keeping a console window open and starts automatically on Windows boot:
```powershell
# Install Windows Service
.\svc.cmd install

# Start Windows Service
.\svc.cmd start

# Check status
.\svc.cmd status
```

### 1.4 Configure GitHub Repository Secrets
Under **Settings** -> **Secrets and variables** -> **Actions**, ensure the only 2 required secrets are set:
- `API_BASE_URL`: e.g., `https://api.lazybean.example.com` (or local ngrok / Cloudflare tunnel URL during dev).
- `INTERNAL_TOKEN`: The shared secret matching `Internal:Token` from backend `appsettings.json`.

---

## 2. Migration to Contabo Linux VPS

When ready to offload collection to a 24/7 Linux server, use our turnkey automation script.

### 2.1 Server Requirements
- **OS**: Ubuntu 22.04 LTS / 24.04 LTS or Debian 12.
- **Specs**: 1 vCPU, 2 GB RAM, 20 GB SSD (Contabo VPS S is plenty).
- **Network**: Direct IPv4 (clean residential or non-blacklisted VPS IP).

### 2.2 Provisioning with `setup.sh`
1. SSH into the Contabo VPS as `root`.
2. Clone the repo or copy `tools/deploy/setup.sh`:
   ```bash
   curl -fsSL https://raw.githubusercontent.com/mohammedelthoqaby/Data-Scraper-/main/tools/deploy/setup.sh -o setup.sh
   chmod +x setup.sh
   sudo ./setup.sh
   ```
   This script automatically:
   - Installs `python3`, `python3-venv`, `git`, `curl`, and system dependencies.
   - Creates a dedicated unprivileged user `lazycollector` (home at `/home/lazycollector`).
   - Downloads and unpacks GitHub Actions runner into `/home/lazycollector/actions-runner`.

### 2.3 Register Runner as `lazycollector`
Generate a new registration token in GitHub repository settings, then run:
```bash
sudo -u lazycollector -i
cd /home/lazycollector/actions-runner

./config.sh --url https://github.com/mohammedelthoqaby/Data-Scraper- \
            --token <RUNNER_REGISTRATION_TOKEN> \
            --labels self-hosted,lazybean-collector \
            --name contabo-vps-collector \
            --work _work --unattended --replace
exit
```

### 2.4 Register & Enable systemd Service
From root:
```bash
cd /home/lazycollector/actions-runner
./svc.sh install lazycollector
./svc.sh start
./svc.sh status
```

### 2.5 Decommission the Windows Runner
Once the Linux runner is verified and active:
1. On the Windows PC, open PowerShell as Administrator:
   ```powershell
   cd C:\actions-runner
   .\svc.cmd stop
   .\svc.cmd uninstall
   .\config.cmd remove --token <RUNNER_REMOVAL_TOKEN>
   ```
2. In GitHub -> **Settings** -> **Actions** -> **Runners**, confirm `windows-dev-collector` is offline or removed, and `contabo-vps-collector` shows **Idle** with labels `self-hosted, lazybean-collector`.

---

## 3. One-Command Pre-Switch Verification

Before switching from Windows to Contabo VPS (or whenever debugging connectivity), run the verification tool:

```bash
# On Linux or Windows
python3 tools/verify-collector.py
```

Or with API backend verification:
```bash
API_BASE_URL="http://localhost:5188" INTERNAL_TOKEN="dev-internal-token-secret-12345" python3 tools/verify-collector.py
```

### What it tests:
1. **Meta Ads Collection**: Probes CAFELAX (Page ID `100456846237420`) using `meta-ads-collector`. Confirms the host IP is recognized as residential/unblocked and retrieves active ads.
2. **Google Ads Transparency**: Queries Google Ads Transparency endpoint to verify direct scraping connectivity.
3. **Backend API Endpoints**: Tests `GET /api/internal/ads/meta/identities` using `INTERNAL_TOKEN`.
