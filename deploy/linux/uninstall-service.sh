#!/usr/bin/env bash

set -Eeuo pipefail

SERVICE_NAME="rasgate.service"
INSTALL_DIR="/opt/rasgate"
UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"

function fail() {
  echo "Error: $*" >&2
  exit 1
}

if [[ "$EUID" -ne 0 ]]; then
  fail "run this uninstaller as root."
fi

if [[ ! -e "$UNIT_PATH" ]]; then
  echo "$SERVICE_NAME is not installed."
  echo "Application files, configuration, and logs were not changed."
  exit 0
fi

echo "Stopping and disabling $SERVICE_NAME..."
systemctl disable --now "$SERVICE_NAME"

rm -- "$UNIT_PATH"
systemctl daemon-reload
systemctl reset-failed "$SERVICE_NAME" 2>/dev/null || true

echo
echo "RasGate service registration was removed."
echo "Application files, configuration, and logs were preserved in $INSTALL_DIR."
echo "The rasgate system user was preserved."
