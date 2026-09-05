#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Fake Docker keeps these checks independent of a daemon or running services.
test_dir=$(mktemp -d)
trap 'rm -rf "$test_dir"' EXIT
export PATH="$test_dir:$PATH"
export DOCKER_LOG="$test_dir/docker.log"
export GITHUB_STEP_SUMMARY="$test_dir/summary"
export TEST_CONFIG='{"name":"lab","services":{"honcho":{"depends_on":{"postgres":{},"lms":{}}},"postgres":{},"lms":{},"keycloak":{"depends_on":{"postgres":{}}},"frontend":{"depends_on":{"honcho":{}}},"jellyfin":{}}}'
TEST_CONFIG=$(jq '.services |= with_entries(.value.profiles = (if .key == "jellyfin" then ["media"] elif .key == "frontend" then ["debug"] else ["ai"] end))' <<< "$TEST_CONFIG")

cat > "$test_dir/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
if [[ $1 == compose && $4 == config ]]; then
  [[ $COMPOSE_PROFILES == '*' ]] || exit 1
  printf '%s\n' "$TEST_CONFIG"
elif [[ $1 == ps ]]; then
  [[ ${PS_FAIL:-false} == false ]] || exit 1
  owner='' service='' formatted=false
  for arg in "$@"; do
    case "$arg" in
      label=com.docker.compose.project=*) owner=${arg#label=com.docker.compose.project=} ;;
      label=com.docker.compose.service=*) service=${arg#label=com.docker.compose.service=} ;;
      --format) formatted=true ;;
    esac
  done
  if [[ $formatted == true ]]; then
    jq -c --arg owner "$owner" '.[$owner][]? | {id: ($owner + "-" + .), service: .}' <<< "$INSTALLED"
  else
    jq -r --arg owner "$owner" --arg service "$service" \
      '.[$owner][]? | select(. == $service) | $owner + "-" + .' <<< "$INSTALLED"
  fi
else
  [[ $COMPOSE_PROFILES == "${SELECTED_PROFILE:-*}" ]] || exit 1
  jq -cn --args '$ARGS.positional' -- "$@" >> "$DOCKER_LOG"
fi
MOCK
chmod +x "$test_dir/docker"

checks=0
run_case() {
  local expected=$1
  shift
  : > "$DOCKER_LOG"
  local actual=pass
  if ! env SERVICE_ACTION=install SELECTED_SERVICES=honcho INSTALLED='{}' \
      DRY_RUN=false UPDATE_IMAGES=false BRING_DOWN_FIRST=false PS_FAIL=false SELECTED_PROFILE='' \
      "$@" bash scripts/workflow.step.manage-services.sh > "$test_dir/output" 2>&1; then
    actual=fail
  fi
  if [[ $actual != "$expected" ]]; then
    cat "$test_dir/output" >&2
    printf 'Expected %s, got %s\n' "$expected" "$actual" >&2
    exit 1
  fi
  if [[ $expected == fail && -s $DOCKER_LOG ]]; then
    echo 'Failed action changed containers' >&2
    exit 1
  fi
  checks=$((checks + 1))
}
assert_calls() {
  if ! jq -se "$1" "$DOCKER_LOG" >/dev/null; then
    cat "$DOCKER_LOG" >&2
    echo 'Unexpected Docker commands' >&2
    exit 1
  fi
}

run_case pass SELECTED_SERVICES=$' frontend, jellyfin\nfrontend ' UPDATE_IMAGES=true BRING_DOWN_FIRST=true
assert_calls '. == [
  ["compose","--env-file",".env","pull","--ignore-buildable","frontend","honcho","jellyfin","lms","postgres"],
  ["compose","--env-file",".env","build","frontend","honcho","jellyfin","lms","postgres"],
  ["compose","--env-file",".env","stop","frontend","honcho","jellyfin","lms","postgres"],
  ["compose","--env-file",".env","up","-d","frontend","honcho","jellyfin","lms","postgres"]]'

for selection in '' ' , ' unknown all,honcho --help '$(touch /tmp/no)'; do
  run_case fail "SELECTED_SERVICES=$selection"
done
for action in install uninstall; do
  run_case pass "SERVICE_ACTION=$action" DRY_RUN=true UPDATE_IMAGES=true
  assert_calls 'length == 0'
done
for project in lab agentic; do
  run_case fail SERVICE_ACTION=uninstall SELECTED_SERVICES=postgres "INSTALLED={\"$project\":[\"frontend\"]}"
  [[ $(< "$test_dir/output") == *frontend* ]]
done

run_case pass SERVICE_ACTION=uninstall 'INSTALLED={"lab":["honcho","postgres","keycloak"]}'
assert_calls '. == [["compose","--env-file",".env","rm","--stop","--force","honcho"]]'
run_case pass SERVICE_ACTION=uninstall SELECTED_SERVICES=honcho,postgres,keycloak 'INSTALLED={"lab":["honcho","postgres","keycloak"]}'
assert_calls '. == [["compose","--env-file",".env","rm","--stop","--force","honcho","keycloak","postgres"]]'

for action in install uninstall; do
  run_case pass "SERVICE_ACTION=$action" SELECTED_SERVICES=all 'INSTALLED={"lab":["frontend","honcho","jellyfin","keycloak","lms","postgres"]}'
  assert_calls '.[-1][-6:] == ["frontend","honcho","jellyfin","keycloak","lms","postgres"]'
done

run_case pass SELECTED_SERVICES=jellyfin 'INSTALLED={"media":["jellyfin","sonarr"]}'
assert_calls '. == [
  ["compose","--env-file",".env","build","jellyfin"],
  ["rm","-f","media-jellyfin"],
  ["compose","--env-file",".env","up","-d","jellyfin"]]'
run_case fail SERVICE_ACTION=uninstall SELECTED_SERVICES=all PS_FAIL=true
run_case fail 'INSTALLED={"agentic":["postgres","keycloak"]}'
[[ $(< "$test_dir/output") == *keycloak* ]]
run_case pass SELECTED_SERVICES=honcho,keycloak 'INSTALLED={"agentic":["postgres","keycloak"]}'
assert_calls 'any(.[]; . == ["rm","-f","agentic-postgres"])'
run_case pass SELECTED_PROFILE=ai
assert_calls '.[-1] == ["compose","--env-file",".env","up","-d","honcho","keycloak","lms","postgres"]'
run_case pass SELECTED_PROFILE=media
assert_calls '.[-1] == ["compose","--env-file",".env","up","-d","jellyfin"]'
run_case fail SELECTED_PROFILE=unknown
run_case fail SELECTED_PROFILE=debug
run_case fail SERVICE_ACTION=uninstall SELECTED_PROFILE=ai 'INSTALLED={"lab":["frontend"]}'
run_case pass SERVICE_ACTION=uninstall SELECTED_PROFILE=media 'INSTALLED={"lab":["jellyfin","honcho"]}'
assert_calls '. == [["compose","--env-file",".env","rm","--stop","--force","jellyfin"]]'
run_case pass SELECTED_PROFILE=ai DRY_RUN=true
assert_calls 'length == 0'
printf 'All %s service workflow checks passed.\n' "$checks"
