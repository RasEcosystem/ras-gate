#!/usr/bin/env bash

set -Eeuo pipefail

SERVICE_NAME="rasgate.service"
SERVICE_USER="rasgate"
SERVICE_GROUP="rasgate"
INSTALL_DIR="/opt/rasgate"
UNIT_PATH="/etc/systemd/system/$SERVICE_NAME"
SOURCE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

function fail() {
  echo "Error: $*" >&2
  exit 1
}

function require_command() {
  command -v "$1" >/dev/null 2>&1 ||
    fail "required command '$1' was not found."
}

if [[ "$EUID" -ne 0 ]]; then
  fail "run this installer as root."
fi

require_command systemctl
require_command install
require_command useradd
require_command groupadd
require_command getent

if [[ ! -d /run/systemd/system ]]; then
  fail "systemd is not running on this host."
fi

if [[ ! -x "$SOURCE_DIR/RasGate.Web" ]]; then
  fail "RasGate executable was not found in $SOURCE_DIR."
fi

if [[ ! -f "$SOURCE_DIR/appsettings.json" ]]; then
  fail "appsettings.json was not found in $SOURCE_DIR."
fi

if [[ ! -f "$SOURCE_DIR/rasgate.service" ]]; then
  fail "rasgate.service was not found in $SOURCE_DIR."
fi

if systemctl list-unit-files "$SERVICE_NAME" --no-legend 2>/dev/null |
  awk '{print $1}' |
  grep -Fxq "$SERVICE_NAME"; then
  fail "$SERVICE_NAME already exists. Uninstall it before installing this release."
fi

echo "Validating RasGate configuration..."
"$SOURCE_DIR/RasGate.Web" --validate-config

if ! getent group "$SERVICE_GROUP" >/dev/null 2>&1; then
  groupadd --system "$SERVICE_GROUP"
fi

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  nologin_shell="$(command -v nologin || true)"
  nologin_shell="${nologin_shell:-/usr/sbin/nologin}"

  useradd \
    --system \
    --gid "$SERVICE_GROUP" \
    --home-dir "$INSTALL_DIR" \
    --shell "$nologin_shell" \
    "$SERVICE_USER"
elif [[ "$(id -gn "$SERVICE_USER")" != "$SERVICE_GROUP" ]]; then
  fail "existing user '$SERVICE_USER' does not use the '$SERVICE_GROUP' primary group."
fi

echo "Installing RasGate in $INSTALL_DIR..."
install -d -o root -g "$SERVICE_GROUP" -m 0750 "$INSTALL_DIR"
install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" -m 0750 \
  "$INSTALL_DIR/logs" \
  "$INSTALL_DIR/logs/requests" \
  "$INSTALL_DIR/logs/errors"

if [[ "$SOURCE_DIR" != "$INSTALL_DIR" ]]; then
  install -o root -g root -m 0755 \
    "$SOURCE_DIR/RasGate.Web" \
    "$INSTALL_DIR/RasGate.Web"

  install -o root -g "$SERVICE_GROUP" -m 0640 \
    "$SOURCE_DIR/appsettings.json" \
    "$INSTALL_DIR/appsettings.json"

  for file in README.md README.ru.md LICENSE SERVICE.md; do
    if [[ -f "$SOURCE_DIR/$file" ]]; then
      install -o root -g root -m 0644 \
        "$SOURCE_DIR/$file" \
        "$INSTALL_DIR/$file"
    fi
  done

  install -o root -g root -m 0755 \
    "$SOURCE_DIR/install-service.sh" \
    "$INSTALL_DIR/install-service.sh"

  install -o root -g root -m 0755 \
    "$SOURCE_DIR/uninstall-service.sh" \
    "$INSTALL_DIR/uninstall-service.sh"
else
  chown root:root \
    "$INSTALL_DIR/RasGate.Web" \
    "$INSTALL_DIR/install-service.sh" \
    "$INSTALL_DIR/uninstall-service.sh"

  chmod 0755 \
    "$INSTALL_DIR/RasGate.Web" \
    "$INSTALL_DIR/install-service.sh" \
    "$INSTALL_DIR/uninstall-service.sh"

  chown root:"$SERVICE_GROUP" "$INSTALL_DIR/appsettings.json"
  chmod 0640 "$INSTALL_DIR/appsettings.json"
fi

install -o root -g root -m 0644 \
  "$SOURCE_DIR/rasgate.service" \
  "$UNIT_PATH"

echo "Enabling and starting $SERVICE_NAME..."
systemctl daemon-reload
systemctl enable "$SERVICE_NAME"

if ! systemctl start "$SERVICE_NAME"; then
  echo "The service was installed but failed to start." >&2
  echo "Inspect it with: journalctl -u $SERVICE_NAME -n 100 --no-pager" >&2
  echo "Roll back with: $INSTALL_DIR/uninstall-service.sh" >&2
  exit 1
fi

echo
echo "RasGate is installed and running."
echo "Status:  systemctl status $SERVICE_NAME"
echo "Restart: systemctl restart $SERVICE_NAME"
echo "Journal: journalctl -u $SERVICE_NAME -f"
echo "Health:  http://127.0.0.1:5050/rasgate/status"
echo "Logs:    $INSTALL_DIR/logs"
