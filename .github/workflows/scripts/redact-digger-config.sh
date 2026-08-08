#!/usr/bin/env bash
set -euo pipefail
: "${TRANSMISSION_USERNAME:?TRANSMISSION_USERNAME variable is required}"
: "${TRANSMISSION_PASSWORD:?TRANSMISSION_PASSWORD secret is required}"
appsettings_path="src/Digger/Digger.Worker/appsettings.json"
appsettings_tmp="$(mktemp)"
trap 'rm -f "$appsettings_tmp"' EXIT
jq --arg transmissionUsername "$TRANSMISSION_USERNAME" --arg transmissionPassword "$TRANSMISSION_PASSWORD" 'setpath(["Transmission", "User"]; $transmissionUsername) | setpath(["Transmission", "Password"]; $transmissionPassword)' "$appsettings_path" > "$appsettings_tmp"
jq empty "$appsettings_tmp"
mv "$appsettings_tmp" "$appsettings_path"
trap - EXIT
