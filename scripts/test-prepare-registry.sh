#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
trap 'rm -rf "$fixture"' EXIT
mkdir -p "$fixture/source/contexts/meta/example" "$fixture/runner"
printf 'context fixture' > "$fixture/source/contexts/meta/example/meta.json"
cat > "$fixture/source/config.json" <<'JSON'
{"currentContext":"rootless","credsStore":"pass","credHelpers":{"ghcr.io":"pass","another.example":"pass"},"auths":{"ghcr.io":{"auth":"synthetic-invalid-token"},"https://ghcr.io/v1/":{"auth":"synthetic-invalid-token"},"another.example":{"auth":"synthetic-other-token"}}}
JSON
RUNNER_TEMP="$fixture/runner" GITHUB_ENV="$fixture/environment" DOCKER_CONFIG="$fixture/source" bash scripts/workflow.step.prepare-registry.sh
config=$(sed -n 's/^DOCKER_CONFIG=//p' "$fixture/environment")
jq -e '.currentContext == "rootless" and .credsStore == null and .auths["ghcr.io"] == null and .auths["https://ghcr.io/v1/"] == null and .credHelpers["ghcr.io"] == null and .auths["another.example"].auth == "synthetic-other-token" and .credHelpers["another.example"] == "pass"' "$config/config.json" >/dev/null
cmp "$fixture/source/contexts/meta/example/meta.json" "$config/contexts/meta/example/meta.json"
jq -e '.auths["ghcr.io"].auth == "synthetic-invalid-token"' "$fixture/source/config.json" >/dev/null
RUNNER_TEMP="$fixture/runner" GITHUB_ENV="$fixture/environment-empty" DOCKER_CONFIG="$fixture/missing" bash scripts/workflow.step.prepare-registry.sh
config=$(sed -n 's/^DOCKER_CONFIG=//p' "$fixture/environment-empty")
jq -e '. == {}' "$config/config.json" >/dev/null
echo 'Registry preparation checks passed: anonymous GHCR, preserved source/context and clean empty configuration.'
