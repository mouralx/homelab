#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
trap 'rm -rf "$fixture"' EXIT
unset GHCR_TOKEN GHCR_USERNAME
mkdir -p "$fixture/source/contexts/meta/example" "$fixture/source/cli-plugins" "$fixture/runner"
printf '#!/usr/bin/env bash\necho compose-plugin-fixture\n' > "$fixture/source/cli-plugins/docker-compose"
chmod +x "$fixture/source/cli-plugins/docker-compose"
printf 'context fixture' > "$fixture/source/contexts/meta/example/meta.json"
cat > "$fixture/source/config.json" <<'JSON'
{"currentContext":"rootless","credsStore":"pass","credHelpers":{"ghcr.io":"pass","another.example":"pass"},"auths":{"ghcr.io":{"auth":"synthetic-invalid-token"},"https://ghcr.io/v1/":{"auth":"synthetic-invalid-token"},"another.example":{"auth":"synthetic-other-token"}}}
JSON
RUNNER_TEMP="$fixture/runner" GITHUB_ENV="$fixture/environment" DOCKER_CONFIG="$fixture/source" bash scripts/workflow.step.prepare-registry.sh
config=$(sed -n 's/^DOCKER_CONFIG=//p' "$fixture/environment")
jq -e '.currentContext == "rootless" and .credsStore == null and .auths["ghcr.io"] == null and .auths["https://ghcr.io/v1/"] == null and .credHelpers["ghcr.io"] == null and .auths["another.example"].auth == "synthetic-other-token" and .credHelpers["another.example"] == "pass"' "$config/config.json" >/dev/null
cmp "$fixture/source/contexts/meta/example/meta.json" "$config/contexts/meta/example/meta.json"
[[ $("$config/cli-plugins/docker-compose") == compose-plugin-fixture ]]
jq -e '.auths["ghcr.io"].auth == "synthetic-invalid-token"' "$fixture/source/config.json" >/dev/null
RUNNER_TEMP="$fixture/runner" GITHUB_ENV="$fixture/environment-empty" DOCKER_CONFIG="$fixture/missing" bash scripts/workflow.step.prepare-registry.sh
config=$(sed -n 's/^DOCKER_CONFIG=//p' "$fixture/environment-empty")
jq -e '. == {}' "$config/config.json" >/dev/null
mkdir -p "$fixture/bin"
cat > "$fixture/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[[ "$*" == "--config "*" login ghcr.io --username fixture-user --password-stdin" ]]
[[ $(cat) == fixture-token ]]
[[ -f "$2/config.json" ]]
[[ ${LOGIN_FAIL:-false} == false ]]
MOCK
chmod +x "$fixture/bin/docker"
PATH="$fixture/bin:$PATH" GHCR_USERNAME=fixture-user GHCR_TOKEN=fixture-token RUNNER_TEMP="$fixture/runner" GITHUB_ENV="$fixture/environment-auth" DOCKER_CONFIG="$fixture/source" bash scripts/workflow.step.prepare-registry.sh
if PATH="$fixture/bin:$PATH" LOGIN_FAIL=true GHCR_USERNAME=fixture-user GHCR_TOKEN=fixture-token RUNNER_TEMP="$fixture/runner" GITHUB_ENV="$fixture/environment-fail" DOCKER_CONFIG="$fixture/source" bash scripts/workflow.step.prepare-registry.sh; then
  echo 'Ignored failed registry login' >&2; exit 1
fi
config=$(sed -n 's/^DOCKER_CONFIG=//p' "$fixture/environment-fail")
HOMELAB_REGISTRY_CONFIG="$config" bash scripts/workflow.step.cleanup-environment.sh
[[ ! -d "$config" ]]
echo 'Registry checks passed: anonymous/authenticated access, login failure, cleanup, preserved context and CLI plugins.'
