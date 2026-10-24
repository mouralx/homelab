#!/usr/bin/env bash
set -euo pipefail

# Keep application writers stopped until migration succeeds.
docker compose stop tempo-portal tempo-api
docker compose up -d --wait --wait-timeout 120 postgres
# Test real logins over the same DNS name and TCP port used by clients.
# pg_isready alone does not validate credentials or database access.
docker compose exec -T postgres sh -ec '
  export PGCONNECT_TIMEOUT=5
  PGPASSWORD="$APP_DB_PASSWORD" psql -X -w -h tempo-postgres -p 5432 -U tempo -d tempo -v ON_ERROR_STOP=1 -c "SELECT 1" >/dev/null
  PGPASSWORD="$IDENTITY_DB_PASSWORD" psql -X -w -h tempo-postgres -p 5432 -U keycloak -d keycloak -v ON_ERROR_STOP=1 -c "SELECT 1" >/dev/null
'
docker compose run --rm --no-deps -T tempo-api --migrate
