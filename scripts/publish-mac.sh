#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION=${1:-Release}
RUNTIME="maccatalyst-x64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/SBoxApp/SBoxApp.csproj"
DIST_ROOT="${REPO_ROOT}/dist/mac"
PUBLISH_DIR="${DIST_ROOT}/publish"
ARTIFACT_NAME="SolitonSBOX-${RUNTIME}.zip"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: dotnet SDK not found."
    echo "Please install the .NET 8 SDK from https://dotnet.microsoft.com/download and rerun this script."
    exit 1
fi

if ! xcode-select -p >/dev/null 2>&1; then
    echo "error: Xcode command line tools are not installed."
    echo "Run 'xcode-select --install' and follow the prompts, then rerun this script."
    exit 1
fi

if ! command -v codesign >/dev/null 2>&1; then
    echo "error: codesign not available. Install the full Xcode app from the App Store."
    exit 1
fi

if ! dotnet workload list | grep -q "maui-maccatalyst"; then
    echo "error: .NET MAUI Mac Catalyst workload not installed."
    echo "Run 'dotnet workload install maui' (this may take several minutes) and rerun this script."
    exit 1
fi

rm -rf "${DIST_ROOT}"
mkdir -p "${PUBLISH_DIR}"

dotnet publish "${PROJECT}" \
    -f net8.0-maccatalyst \
    -r "${RUNTIME}" \
    -c "${CONFIGURATION}" \
    -p:UseAppHost=true \
    -o "${PUBLISH_DIR}"

(
    cd "${PUBLISH_DIR}"
    zip -r "../${ARTIFACT_NAME}" .
)

echo "macOS publish output zipped at ${DIST_ROOT}/${ARTIFACT_NAME}"

APP_OUTPUT="${REPO_ROOT}/SBoxApp/bin/${CONFIGURATION}/net8.0-maccatalyst/${RUNTIME}"
if [ -d "${APP_OUTPUT}" ]; then
    PKG_FILE="$(find "${APP_OUTPUT}" -name '*.pkg' -print -quit || true)"
    if [ -n "${PKG_FILE}" ]; then
        cp "${PKG_FILE}" "${DIST_ROOT}/"
        echo "macOS PKG copied to ${DIST_ROOT}/$(basename "${PKG_FILE}")"
    else
        echo "warning: no .pkg found under ${APP_OUTPUT}" >&2
    fi
else
    echo "warning: macOS output directory not found (${APP_OUTPUT})" >&2
fi

echo "macOS artifacts generated."
