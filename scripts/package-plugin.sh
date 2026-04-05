#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-0.1.0}"
PUBLISH_DIR="$ROOT_DIR/dist/plugin/Jellygate.JellyfinBridge"
ZIP_PATH="$ROOT_DIR/dist/plugin/Jellygate.JellyfinBridge-v${VERSION}.zip"

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$ROOT_DIR/plugins/Jellygate.JellyfinBridge/Jellygate.JellyfinBridge.csproj" -c Release -o "$PUBLISH_DIR" -p:Version="$VERSION"

rm -f "$ZIP_PATH"
cd "$ROOT_DIR/dist/plugin"
zip -rq "$ZIP_PATH" "Jellygate.JellyfinBridge"

printf 'Plugin zip created at %s\n' "$ZIP_PATH"
