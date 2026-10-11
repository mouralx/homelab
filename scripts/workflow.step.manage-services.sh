#!/usr/bin/env bash
set -euo pipefail

export COMPOSE_FILE=${COMPOSE_FILE:-services/compose.yaml}
action=${SERVICE_ACTION:-install}
case "$action" in install|uninstall) ;; *) echo 'Invalid service action' >&2; exit 1 ;; esac
for flag in DRY_RUN UPDATE_IMAGES BRING_DOWN_FIRST; do
  case "${!flag:-false}" in true|false) ;; *) echo "Invalid boolean: $flag" >&2; exit 1 ;; esac
done

config=$(docker compose --env-file .env config --format json)
printf '%s\n' "$config" | bash scripts/validate-compose.sh
project=$(jq -er '.name' <<< "$config")
SELECTED_SERVICES=${SELECTED_SERVICES-all}

# Include stopped containers and legacy projects when protecting dependencies.
inventory=''
legacy_inventory=''
for owner in "$project" agentic agents media tools; do
  rows=$(docker ps -a --filter "label=com.docker.compose.project=$owner" \
    --format '{"id":"{{.ID}}","service":"{{.Label "com.docker.compose.service"}}"}')
  inventory+="$rows"$'\n'
  if [[ "$owner" != "$project" ]]; then legacy_inventory+="$rows"$'\n'; fi
done
installed=$(jq -s '[.[].service] | unique' <<< "$inventory")

plan=$(jq -er --arg selection "${SELECTED_SERVICES:-}" --arg action "$action" \
  --argjson installed "$installed" '
  .services as $services
  | def closure($name): $name, (($services[$name].depends_on // {} | keys[]) | closure(.));
  ($selection | gsub("[,\\s]+"; " ") | split(" ") | map(select(length > 0)) | unique) as $names
  | (if $names == ["all"] then ($services | keys) else $names end) as $selected
  | if ($selected | length) == 0 then error("Select at least one service, or all") else . end
  | ($selected - ($services | keys)) as $unknown
  | if ($unknown | length) > 0 then error("Unknown services: " + ($unknown | join(", "))) else . end
  | if $action == "install" then [$selected[] | closure(.)] | unique
    else
      [($installed - $selected)[] as $name
        | select($services | has($name))
        | [$name | closure(.)] as $dependencies
        | select(any($selected[]; . as $target | $dependencies | index($target)))
        | $name] as $blockers
      | if ($blockers | length) > 0 then
          error("Cannot uninstall: installed services still depend on this selection. Also select or uninstall: " + ($blockers | join(", ")))
        else $selected end
    end
  | .[]' <<< "$config")
targets=()
while IFS= read -r service; do targets+=("$service"); done <<< "$plan"
if [[ $action == install ]]; then
  # Moving a shared dependency to lab also changes its Compose network.
  # Require legacy dependents to move together rather than stranding them.
  legacy=$(jq -s '[.[].service] | unique' <<< "$legacy_inventory")
  jq -e --argjson legacy "$legacy" --arg plan "$plan" '
    .services as $services
    | def closure($name): $name, (($services[$name].depends_on // {} | keys[]) | closure(.));
    ($plan | split("\n")) as $targets
    | ($legacy - ($legacy - $targets)) as $moving
    | [($legacy - $targets)[] as $name
        | select($services | has($name))
        | [$name | closure(.)] as $dependencies
        | select(any($moving[]; . as $target | $dependencies | index($target)))
        | $name] as $blockers
    | if ($blockers | length) > 0 then
        error("Also select these legacy dependents to migrate together: " + ($blockers | join(", ")))
      else true end' <<< "$config" >/dev/null
fi
summary="$action (full stack): ${targets[*]} (dry run: ${DRY_RUN:-false})"
printf '%s\n' "$summary"
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  printf '%s\n' "$summary" >> "$GITHUB_STEP_SUMMARY"
fi
if [[ $action == install ]] && jq -e '.services.postgres.volumes // [] | any(.target == "/var/lib/postgresql/data")' <<< "$config" >/dev/null && [[ " ${targets[*]} " == *" postgres "* ]]; then
  # Avoid accidentally starting a different PostgreSQL major against existing data.
  validation_file=$(mktemp)
  printf '%s\n' "$config" > "$validation_file"
  if ! bash scripts/validate-postgres-storage.sh "$validation_file"; then
    rm -f "$validation_file"; exit 1
  fi
  rm -f "$validation_file"
fi
if [[ ${DRY_RUN:-false} == true ]]; then exit 0; fi

if [[ $action == install ]]; then
  if [[ ${UPDATE_IMAGES:-false} == true ]]; then
    pull_targets=("${targets[@]}")
    if [[ "$SELECTED_SERVICES" != all && "$SELECTED_SERVICES" == *domus-* ]]; then
      pull_targets=()
      for service in "${targets[@]}"; do [[ "$service" == postgres ]] || pull_targets+=("$service"); done
    fi
    docker compose --env-file .env pull --ignore-buildable "${pull_targets[@]}"
  fi
  docker compose --env-file .env build "${targets[@]}"
fi

# Retire only the renamed Domus worker; preserve its named data volumes.
if [[ $action == install && " ${targets[*]} " == *" domus-automations-worker "* ]]; then
  retired=$(docker ps -aq --filter "label=com.docker.compose.project=$project" --filter 'label=com.docker.compose.service=domus-automation-worker')
  while IFS= read -r id; do [[ -z "$id" ]] || docker rm -f "$id"; done <<< "$retired"
fi

# Migrate/remove only selected legacy services; leave their data in place.
retired_agent_ids=$(docker ps -aq \
  --filter 'label=com.docker.compose.project=agents' \
  --filter 'label=com.docker.compose.service=portainer_agent')
while IFS= read -r id; do
  if [[ -n "$id" ]]; then docker rm -f "$id"; fi
done <<< "$retired_agent_ids"
for owner in agentic agents media tools; do
  for service in "${targets[@]}"; do
    ids=$(docker ps -aq --filter "label=com.docker.compose.project=$owner" \
      --filter "label=com.docker.compose.service=$service")
    while IFS= read -r id; do
      if [[ -n "$id" ]]; then docker rm -f "$id"; fi
    done <<< "$ids"
  done
done

if [[ $action == uninstall ]]; then
  docker compose --env-file .env rm --stop --force "${targets[@]}"
else
  if [[ ${BRING_DOWN_FIRST:-false} == true ]]; then
    docker compose --env-file .env stop "${targets[@]}"
  fi
  docker compose --env-file .env up -d --wait --wait-timeout 240 "${targets[@]}"
fi
