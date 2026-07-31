#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOAD_TESTS_DIR="$ROOT_DIR/scripts/load"

function usage() {
    cat <<'EOF'
Usage:
  ./scripts/run-load-test.sh [test] [base-url]

Examples:
  ./scripts/run-load-test.sh smoke http://192.168.1.10:5050
  TEST=load BASE_URL=http://192.168.1.10:5050 ./scripts/run-load-test.sh

The API key is read from API_KEY or RASGATE_API_KEY.
If required values are omitted, the script asks for them interactively.
EOF
}

function fail() {
    echo "Error: $*" >&2
    exit 1
}

function require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        fail "required command '$1' was not found."
    fi
}

function available_tests() {
    find "$LOAD_TESTS_DIR" \
        -maxdepth 1 \
        -type f \
        -name '*.js' \
        -printf '%f\n' \
        | sed 's/\.js$//' \
        | sort \
        | paste -sd ',' - \
        | sed 's/,/, /g'
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 0
fi

if (( $# > 2 )); then
    usage >&2
    exit 1
fi

require_command k6

test_name="${1:-${TEST:-}}"
base_url="${2:-${BASE_URL:-}}"
api_key="${API_KEY:-${RASGATE_API_KEY:-}}"

if [[ -z "$test_name" ]]; then
    echo "Available tests: $(available_tests)"

    if ! read -r -p "Test name: " test_name; then
        fail "test name was not provided."
    fi
fi

if [[ ! "$test_name" =~ ^[A-Za-z0-9_-]+$ ]]; then
    fail "test name contains unsupported characters."
fi

test_file="$LOAD_TESTS_DIR/$test_name.js"

if [[ ! -f "$test_file" ]]; then
    fail "load test was not found: $test_file"
fi

if [[ -z "$base_url" ]]; then
    if ! read -r -p \
        "RasGate URL (for example http://192.168.1.10:5050): " \
        base_url; then
        fail "RasGate URL was not provided."
    fi
fi

case "$base_url" in
    http://* | https://*)
        ;;
    *)
        base_url="http://$base_url"
        ;;
esac

base_url="${base_url%/}"

if [[ ! "$base_url" =~ ^https?://[^[:space:]]+$ ]]; then
    fail "RasGate URL is invalid: $base_url"
fi

if [[ -z "$api_key" ]]; then
    if ! read -r -s -p "RasGate API key: " api_key; then
        echo >&2
        fail "API key was not provided."
    fi

    echo
fi

if [[ -z "$api_key" ]]; then
    fail "API key must not be empty."
fi

echo "Running '$test_name' against $base_url"

k6_arguments=(run)

if [[ -n "${SUMMARY_EXPORT:-}" ]]; then
    k6_arguments+=(
        --summary-export
        "$SUMMARY_EXPORT"
    )
fi

k6_arguments+=("$test_file")

exec env \
    BASE_URL="$base_url" \
    API_KEY="$api_key" \
    k6 "${k6_arguments[@]}"
