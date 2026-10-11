#!/usr/bin/env bash
set -euo pipefail
umask 077
config=$(docker compose --env-file .env config --format json)
project=$(jq -r '.name' <<< "$config")
backup_dir="${DOMUS_BACKUP_ROOT:-$HOME/domus-backups}/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
export PGHOST PGPORT PGUSER PGPASSWORD
PGHOST=$(jq -r '.services["domus-databases"].environment.PGHOST' <<< "$config")
PGPORT=$(jq -r '.services["domus-databases"].environment.PGPORT' <<< "$config")
PGUSER=$(jq -r '.services["domus-databases"].environment.PGUSER' <<< "$config")
PGPASSWORD=$(jq -r '.services["domus-databases"].environment.PGPASSWORD' <<< "$config")
writers=()
while IFS= read -r id; do [[ -z "$id" ]] || writers+=("$id"); done < <(
 docker ps --filter "label=com.docker.compose.project=$project" --format '{{.ID}} {{.Label "com.docker.compose.service"}}' |
 awk '$2 ~ /^domus-/ && $2 != "domus-databases" {print $1}')
restore_on_error() {
 status=$?
 if [[ $status != 0 && ${#writers[@]} != 0 ]]; then docker start "${writers[@]}" >/dev/null || true; fi
 exit "$status"
}
trap restore_on_error EXIT
if [[ ${#writers[@]} != 0 ]]; then
 docker inspect "${writers[@]}" | jq '[.[] | {name:.Name,image:.Config.Image,imageId:.Image}]' > "$backup_dir/previous-images.json"
 docker stop --time 60 "${writers[@]}" >/dev/null
fi
for key in DOMUS_MEMBERSHIP_DATABASE DOMUS_BOARDS_DATABASE DOMUS_AUTOMATIONS_DATABASE DOMUS_AI_DATABASE; do
 case "$key" in DOMUS_MEMBERSHIP_DATABASE) fallback=membership;; DOMUS_BOARDS_DATABASE) fallback=boards;; DOMUS_AUTOMATIONS_DATABASE) fallback=automations;; *) fallback=ai;; esac
 database=$(jq -r --arg key "$key" --arg fallback "$fallback" '.services["domus-databases"].environment[$key] // $fallback' <<< "$config")
 [[ "$database" =~ ^[a-zA-Z0-9_-]+$ ]] || { echo 'Unsupported database name for backup' >&2; exit 1; }
 exists=$(docker run --rm --network homelab_static -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD postgres:18-bookworm psql -d postgres -Atc "SELECT count(*) FROM pg_database WHERE datname='$database'")
 if [[ "$exists" == 1 ]]; then
  docker run --rm --network homelab_static -e PGHOST -e PGPORT -e PGUSER -e PGPASSWORD -v "$backup_dir:/backup" postgres:18-bookworm pg_dump --format=custom --dbname "$database" --file "/backup/$database.dump"
 fi
done
for suffix in domus-work-keys domus-automation-data domus-automation-keys domus-rabbitmq-data domus-ai-data; do
 volume="${project}_$suffix"
 if docker volume inspect "$volume" >/dev/null 2>&1; then
  docker run --rm -v "$volume:/source:ro" -v "$backup_dir:/backup" postgres:18-bookworm sh -c 'tar -czf "/backup/$1.tar.gz" -C /source .' sh "$suffix"
 fi
done
docker run --rm -v "$backup_dir:/backup" postgres:18-bookworm sh -c 'chmod -R go-rwx /backup; chown -R "$1:$2" /backup' sh "$(id -u)" "$(id -g)"
printf 'Domus database and persistent-volume backups: %s\n' "$backup_dir"
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then printf 'Private Domus backups on the runner: `%s`\n' "$backup_dir" >> "$GITHUB_STEP_SUMMARY"; fi
# The following Compose installation starts the updated domain services.
