#!/bin/sh
set -eu
# Passwords are passed as psql variables and quoted as SQL literals, never interpolated into SQL.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  --set=app_password="$APP_DB_PASSWORD" --set=identity_password="$IDENTITY_DB_PASSWORD" <<'SQL'
CREATE USER tempo WITH PASSWORD :'app_password';
CREATE DATABASE tempo OWNER tempo;
CREATE USER keycloak WITH PASSWORD :'identity_password';
CREATE DATABASE keycloak OWNER keycloak;
REVOKE ALL ON DATABASE tempo FROM PUBLIC;
REVOKE ALL ON DATABASE keycloak FROM PUBLIC;
SQL
