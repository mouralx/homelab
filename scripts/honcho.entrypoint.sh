#!/bin/sh

set -eu

DB_HOST=${DB_HOST:-postgres}
DB_PORT=${DB_PORT:-5432}
DB_NAME=${DB_NAME:-${POSTGRES_DB:-postgres}}
DB_USER=${DB_USER:-${POSTGRES_USER:-postgres}}
DB_PASSWORD=${DB_PASSWORD:-${POSTGRES_PASSWORD:-postgres}}
DB_URI=${DB_CONNECTION_URI:-postgresql+psycopg://${DB_USER}:${DB_PASSWORD}@${DB_HOST}:${DB_PORT}/${DB_NAME}}

export DB_CONNECTION_URI="$DB_URI"
export PGPASSWORD="$DB_PASSWORD"
export OPENAI_API_BASE_URL="${OPENAI_API_BASE_URL:-}"
export OPENAI_BASE_URL="${OPENAI_BASE_URL:-}"

until pg_isready -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" >/dev/null 2>&1
 do
  echo "waiting for postgres at $DB_HOST:$DB_PORT"
  sleep 2
done

psql "postgresql://$DB_USER:$DB_PASSWORD@$DB_HOST:$DB_PORT/$DB_NAME" -c "CREATE EXTENSION IF NOT EXISTS vector; CREATE EXTENSION IF NOT EXISTS pg_trgm;" >/dev/null 2>&1 || true

uv run alembic upgrade head
uv run fastapi dev src/main.py --host "${HOST:-0.0.0.0}" --port "${PORT:-8000}"
