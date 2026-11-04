#!/usr/bin/env bash
# ==============================================================================
# LazyBean Ads Collector - Contabo Linux VPS Provisioning & Runner Setup
# ==============================================================================
set -euo pipefail

echo "================================================================="
echo " LazyBean Self-Hosted Ads Collector VPS Setup (Contabo Linux)   "
echo "================================================================="

if [ "$EUID" -ne 0 ]; then
  echo "Error: Please run this script as root or with sudo."
  exit 1
fi

RUNNER_USER="lazycollector"
RUNNER_DIR="/home/${RUNNER_USER}/actions-runner"
RUNNER_VERSION="2.321.0" # GitHub Actions runner Linux x64

# 1. Update system packages and install dependencies
echo "[1/6] Installing OS dependencies..."
apt-get update -qq
apt-get install -y -qq \
    curl \
    git \
    jq \
    python3 \
    python3-pip \
    python3-venv \
    ca-certificates \
    libicu-dev \
    tar \
    gzip

# 2. Create non-root dedicated runner user
echo "[2/6] Configuring dedicated system user '${RUNNER_USER}'..."
if ! id -u "${RUNNER_USER}" >/dev/null 2>&1; then
    useradd -m -s /bin/bash "${RUNNER_USER}"
    echo "Created user ${RUNNER_USER}."
else
    echo "User ${RUNNER_USER} already exists."
fi

# 3. Create runner directory
echo "[3/6] Setting up actions runner directory at ${RUNNER_DIR}..."
mkdir -p "${RUNNER_DIR}"
chown -R "${RUNNER_USER}:${RUNNER_USER}" "/home/${RUNNER_USER}"

# 4. Download GitHub Actions runner package
echo "[4/6] Downloading GitHub Actions runner v${RUNNER_VERSION}..."
cd "${RUNNER_DIR}"
if [ ! -f "config.sh" ]; then
    RUNNER_TAR="actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz"
    if [ ! -f "${RUNNER_TAR}" ]; then
        curl -fsSL -o "${RUNNER_TAR}" "https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/${RUNNER_TAR}"
    fi
    tar xzf "${RUNNER_TAR}"
    rm -f "${RUNNER_TAR}"
    ./bin/installdependencies.sh
    chown -R "${RUNNER_USER}:${RUNNER_USER}" "${RUNNER_DIR}"
fi

echo "[5/6] Python environment pre-flight check..."
python3 -V

# 5. Instructions for completing registration
echo "================================================================="
echo " [6/6] Runner binaries installed successfully!                  "
echo " Next Steps to activate your runner with labels:                 "
echo "   self-hosted, lazybean-collector                              "
echo "================================================================="
echo "1. Run the config command as the non-root user (${RUNNER_USER}):"
echo "   sudo -u ${RUNNER_USER} -i"
echo "   cd ${RUNNER_DIR}"
echo "   ./config.sh --url https://github.com/mohammedelthoqaby/Data-Scraper- \\"
echo "               --token <GITHUB_RUNNER_TOKEN> \\"
echo "               --labels self-hosted,lazybean-collector \\"
echo "               --name contabo-vps-collector \\"
echo "               --work _work --unattended --replace"
echo ""
echo "2. Install and start the systemd service (as root):"
echo "   exit  # return to root"
echo "   cd ${RUNNER_DIR}"
echo "   ./svc.sh install ${RUNNER_USER}"
echo "   ./svc.sh start"
echo "   ./svc.sh status"
echo ""
echo "3. Run the verification script before decommissioning old runner:"
echo "   python3 tools/verify-collector.py"
echo "================================================================="
