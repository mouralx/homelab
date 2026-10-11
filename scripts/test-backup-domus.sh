#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/bin"
cat > "$fixture/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$BACKUP_TEST_LOG"
case "$*" in
  *'config --format json'*) printf '%s\n' '{"name":"lab","services":{"domus-databases":{"environment":{"PGHOST":"postgres","PGPORT":"5432","PGUSER":"admin","PGPASSWORD":"fixture"}}}}';;
  'ps '*) printf '%s\n' 'board-id domus-boards' 'rabbit-id domus-rabbitmq' 'other-id jellyfin' 'pg-id postgres';;
  'inspect '*) printf '%s\n' '[]';;
  *'pg_dump '*) [[ "${BACKUP_TEST_FAIL:-false}" != true ]];;
  *'SELECT count('* ) echo 1;;
  *) :;;
esac
MOCK
chmod +x "$fixture/bin/docker"
export PATH="$fixture/bin:$PATH" BACKUP_TEST_LOG="$fixture/log" DOMUS_BACKUP_ROOT="$fixture/backups"
bash scripts/workflow.step.backup-domus.sh >/dev/null
rg -q '^stop --time 60 board-id rabbit-id$' "$fixture/log"
! rg -q '^start ' "$fixture/log"
[[ $(rg -c 'pg_dump --format=custom' "$fixture/log") == 4 ]]
: > "$fixture/log"
if BACKUP_TEST_FAIL=true bash scripts/workflow.step.backup-domus.sh >/dev/null 2>&1; then
 echo 'Expected dump failure to abort installation' >&2; exit 1
fi
rg -q '^start board-id rabbit-id$' "$fixture/log"
! rg -q '^stop .*other-id|^stop .*pg-id' "$fixture/log"
echo 'Domus backup checks passed: writer/broker quiescence, four database dumps, unrelated services preserved and rollback on dump failure.'
