#!/usr/bin/env bash
set -euo pipefail
: "${ENVIRONMENT_VARS:?ENVIRONMENT_VARS is required}"
: "${ENVIRONMENT_SECRETS:?ENVIRONMENT_SECRETS is required}"

umask 077
# Quoted Compose dotenv values preserve comments, newlines and literal dollars.
write_values() {
  jq -r '
    to_entries[] | select(.key != "GITHUB_TOKEN")
    | "\(.key)=\(.value | tostring | tojson | gsub("\\$"; "\\$"))"
  '
}
write_values <<< "$ENVIRONMENT_VARS" > .env
write_values <<< "$ENVIRONMENT_SECRETS" >> .env
