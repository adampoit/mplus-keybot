#!/usr/bin/env bash
set -euo pipefail

: "${DEPLOY_HOST:?Set DEPLOY_HOST to the target hostname or IP.}"

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd -- "${SCRIPT_DIR}/.." && pwd)"

DEPLOY_USER="${DEPLOY_USER:-root}"
DEPLOY_PATH="${DEPLOY_PATH:-/srv/mplus-keybot}"
SYSTEMD_PATH="${SYSTEMD_PATH:-/etc/systemd/system}"
REMOTE="${DEPLOY_USER}@${DEPLOY_HOST}"

ssh -T "${REMOTE}" "mkdir -p \"${DEPLOY_PATH}\" \"${DEPLOY_PATH}-stage\""

scp "${REPO_ROOT}/artifacts"/* "${REMOTE}:${DEPLOY_PATH}-stage"
scp "${REPO_ROOT}/deploy/systemd/mplus-keybot.service" "${REMOTE}:${SYSTEMD_PATH}/mplus-keybot.service"

ssh -T "${REMOTE}" <<EOF
	sudo systemctl stop mplus-keybot.service
	mv "${DEPLOY_PATH}-stage"/* "${DEPLOY_PATH}"

	sudo systemctl daemon-reload
	sudo systemctl start mplus-keybot.service
	sudo systemctl status mplus-keybot.service
EOF
