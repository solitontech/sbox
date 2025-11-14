#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION=${1:-Release}
RUNTIME="maccatalyst-x64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/SBoxApp/SBoxApp.csproj"
DIST_ROOT="${REPO_ROOT}/dist/mac"
PUBLISH_DIR="${DIST_ROOT}/publish"
ARTIFACT_NAME="SBoxApp-${RUNTIME}.zip"

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
    rm -f "../${ARTIFACT_NAME}"
    zip -r "../${ARTIFACT_NAME}" .
)

echo "macOS build complete. Artifact: ${DIST_ROOT}/${ARTIFACT_NAME}"
