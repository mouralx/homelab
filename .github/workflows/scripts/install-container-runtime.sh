#!/usr/bin/env bash
set -euo pipefail
if command -v docker >/dev/null 2>&1; then
  echo "Docker is already installed. Skipping installation."
  exit 0
fi
installer="$(mktemp)"
trap 'rm -f "$installer"' EXIT
curl -fsSL https://get.docker.com -o "$installer"
"$SUDO" sh "$installer"
