#!/bin/sh
set -eu
: "${PGHOST:?Existing PostgreSQL host is required}"
: "${PGUSER:?Existing PostgreSQL administrator is required}"
: "${PGPASSWORD:?Existing PostgreSQL administrator password is required}"
: "${DOMUS_MEMBERSHIP_DB_PASSWORD:?Membership database password is required}"
: "${DOMUS_BOARDS_DB_PASSWORD:?Boards database password is required}"
: "${DOMUS_AUTOMATIONS_DB_PASSWORD:?Automations database password is required}"
: "${DOMUS_AI_DB_PASSWORD:?AI database password is required}"

# This is a client: it never starts or upgrades a PostgreSQL server.
for attempt in $(seq 1 60); do
  if pg_isready -q; then break; fi
  if [ "$attempt" = 60 ]; then echo 'Existing PostgreSQL server did not become ready.' >&2; exit 1; fi
  sleep 2
done
psql --no-psqlrc --dbname postgres -v ON_ERROR_STOP=1 \
  --set=membership_db="${DOMUS_MEMBERSHIP_DATABASE:-membership}" \
  --set=boards_db="${DOMUS_BOARDS_DATABASE:-boards}" \
  --set=automations_db="${DOMUS_AUTOMATIONS_DATABASE:-automations}" \
  --set=ai_db="${DOMUS_AI_DATABASE:-ai}" \
  --set=membership_password="$DOMUS_MEMBERSHIP_DB_PASSWORD" \
  --set=boards_password="$DOMUS_BOARDS_DB_PASSWORD" \
  --set=ai_password="$DOMUS_AI_DB_PASSWORD" \
  --set=automations_password="$DOMUS_AUTOMATIONS_DB_PASSWORD" <<'SQL'
-- Refuse collisions with unrelated databases before changing any role or database.
SELECT CASE WHEN owner <> expected_owner THEN format('Refusing database %s owned by another role', datname)::integer ELSE 0 END
FROM (
 SELECT datname, pg_get_userbyid(datdba) AS owner,
 CASE datname WHEN :'membership_db' THEN 'domus_membership' WHEN :'boards_db' THEN 'domus_boards' WHEN :'ai_db' THEN 'domus_ai' ELSE 'domus_automations' END AS expected_owner
 FROM pg_database WHERE datname IN (:'membership_db', :'boards_db', :'automations_db', :'ai_db')
) AS existing;
SELECT CASE WHEN :'membership_db' = :'boards_db' OR :'membership_db' = :'automations_db' OR :'boards_db' = :'automations_db' OR :'ai_db' IN (:'membership_db', :'boards_db', :'automations_db')
 THEN format('Domus database names must be distinct')::integer ELSE 0 END;
SELECT format('CREATE ROLE %I LOGIN', role) FROM (VALUES ('domus_membership'), ('domus_boards'), ('domus_automations'), ('domus_ai')) AS desired(role)
 WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname=role) \gexec
ALTER ROLE domus_membership PASSWORD :'membership_password';
ALTER ROLE domus_boards PASSWORD :'boards_password';
ALTER ROLE domus_automations PASSWORD :'automations_password';
ALTER ROLE domus_ai PASSWORD :'ai_password';
SELECT format('CREATE DATABASE %I OWNER %I', db, owner)
 FROM (VALUES (:'membership_db','domus_membership'), (:'boards_db','domus_boards'), (:'automations_db','domus_automations'), (:'ai_db','domus_ai')) AS desired(db,owner)
 WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname=db) \gexec
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', db)
 FROM (VALUES (:'membership_db'), (:'boards_db'), (:'automations_db'), (:'ai_db')) AS desired(db) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', db, owner)
 FROM (VALUES (:'membership_db','domus_membership'), (:'boards_db','domus_boards'), (:'automations_db','domus_automations'), (:'ai_db','domus_ai')) AS desired(db,owner) \gexec
SQL
