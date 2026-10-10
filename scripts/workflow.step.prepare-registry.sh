#!/usr/bin/env bash
set -euo pipefail
: "${RUNNER_TEMP:?RUNNER_TEMP is required}"
: "${GITHUB_ENV:?GITHUB_ENV is required}"
source_config=${DOCKER_CONFIG:-$HOME/.docker}
registry_config=$(mktemp -d "$RUNNER_TEMP/homelab-registry.XXXXXX")
chmod 700 "$registry_config"
printf 'HOMELAB_REGISTRY_CONFIG=%s\nDOCKER_CONFIG=%s\n' "$registry_config" "$registry_config" >> "$GITHUB_ENV"
umask 077
if [[ -f "$source_config/config.json" ]]; then
  # Public GHCR images must not inherit an expired token or a global helper.
  # Keep the runner context and unrelated file-based registry credentials.
  jq 'def ghcr: test("^(https?://)?ghcr\\.io(/.*)?$");
      del(.credsStore)
      | .auths = ((.auths // {}) | with_entries(select(.key | ghcr | not)))
      | .credHelpers = ((.credHelpers // {}) | with_entries(select(.key | ghcr | not)))' \
      "$source_config/config.json" > "$registry_config/config.json"
else
  printf '{}\n' > "$registry_config/config.json"
fi
if [[ -d "$source_config/contexts" ]]; then
  cp -R "$source_config/contexts" "$registry_config/contexts"
fi
echo 'Public GHCR images will be checked and pulled anonymously.'
