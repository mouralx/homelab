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
  # GHCR images must not inherit an expired token or a global helper.
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
if [[ -d "$source_config/cli-plugins" ]]; then
  # Keep Compose available when it is installed in the runner's Docker config.
  ln -s "$source_config/cli-plugins" "$registry_config/cli-plugins"
fi
if [[ -n "${GHCR_TOKEN:-}" ]]; then
  : "${GHCR_USERNAME:?GHCR_USERNAME is required when GHCR_TOKEN is set}"
  printf '%s' "$GHCR_TOKEN" | docker --config "$registry_config" login ghcr.io \
    --username "$GHCR_USERNAME" --password-stdin >/dev/null
  echo 'GHCR credentials configured in the temporary Docker configuration.'
else
  echo 'No GHCR token configured; only public images can be accessed.'
fi
