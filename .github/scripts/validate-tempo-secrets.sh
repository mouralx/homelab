#!/usr/bin/env bash
set -euo pipefail

for key in TEMPO_POSTGRES_PASSWORD TEMPO_APP_DB_PASSWORD TEMPO_IDENTITY_DB_PASSWORD TEMPO_KEYCLOAK_ADMIN_PASSWORD; do
  if [[ ! "${!key}" =~ ^[A-Za-z0-9_-]{16,}$ ]]; then
    echo "Configure $key with at least 16 letters, digits, underscores or hyphens in the GitHub environment secrets." >&2
    exit 1
  fi
done
