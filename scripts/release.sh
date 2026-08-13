#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_FILE="$ROOT_DIR/src/RasGate.Web/RasGate.Web.csproj"
SOLUTION_FILE="$ROOT_DIR/RasGate.sln"
VERSION_FILE="$ROOT_DIR/version.json"
ARTIFACTS_DIR="$ROOT_DIR/artifacts"
PUBLISH_DIR="$ARTIFACTS_DIR/publish"

RUNTIMES=(
  "linux-x64"
  "win-x64"
)

function require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Error: required command '$1' was not found." >&2
    exit 1
  fi
}

function read_version() {
  python3 -c '
import json
import sys

with open(sys.argv[1], encoding="utf-8") as file:
    data = json.load(file)

version = data.get("version")

if not isinstance(version, str) or not version.strip():
    raise SystemExit("version.json does not contain a valid version field")

print(version.strip())
' "$VERSION_FILE"
}

function package_runtime() {
  local runtime="$1"
  local publish_path="$PUBLISH_DIR/$runtime"
  local package_name="rasgate-$VERSION-$runtime"

  echo
  echo "==> Publishing $runtime"

  dotnet publish "$PROJECT_FILE" \
    --configuration Release \
    --runtime "$runtime" \
    --self-contained true \
    --output "$publish_path" \
    -m:1 \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:IsTransformWebConfigDisabled=true \
    -p:DebugType=None \
    -p:DebugSymbols=false

  echo "==> Packaging $package_name"

  case "$runtime" in
    win-*)
      (
        cd "$publish_path"
        zip -q -r "$ARTIFACTS_DIR/$package_name.zip" .
      )
      ;;

    linux-*)
      tar \
        --create \
        --gzip \
        --file "$ARTIFACTS_DIR/$package_name.tar.gz" \
        --directory "$publish_path" \
        .
      ;;

    *)
      echo "Error: unsupported runtime '$runtime'." >&2
      exit 1
      ;;
  esac
}

require_command dotnet
require_command python3
require_command zip
require_command tar
require_command sha256sum

if [[ ! -f "$PROJECT_FILE" ]]; then
  echo "Error: project file was not found: $PROJECT_FILE" >&2
  exit 1
fi

if [[ ! -f "$SOLUTION_FILE" ]]; then
  echo "Error: solution file was not found: $SOLUTION_FILE" >&2
  exit 1
fi

if [[ ! -f "$VERSION_FILE" ]]; then
  echo "Error: version file was not found: $VERSION_FILE" >&2
  exit 1
fi

VERSION="$(read_version)"

echo "Verifying RasGate $VERSION before packaging"

dotnet restore "$SOLUTION_FILE" -m:1
dotnet test "$SOLUTION_FILE" \
  --configuration Release \
  --no-restore \
  -m:1

echo "Building RasGate $VERSION"

rm -rf "$ARTIFACTS_DIR"
mkdir -p "$PUBLISH_DIR"

for runtime in "${RUNTIMES[@]}"; do
  package_runtime "$runtime"
done

rm -rf "$PUBLISH_DIR"

echo
echo "==> Calculating SHA-256 checksums"

(
  cd "$ARTIFACTS_DIR"
  sha256sum \
    rasgate-"$VERSION"-*.tar.gz \
    rasgate-"$VERSION"-*.zip \
    > SHA256SUMS
)

echo
echo "Release artifacts are ready:"
find "$ARTIFACTS_DIR" \
  -maxdepth 1 \
  -type f \
  -printf "  %f\n" \
  | sort
