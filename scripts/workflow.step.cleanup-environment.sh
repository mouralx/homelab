#!/usr/bin/env bash
set -euo pipefail
rm -f .env
if [[ -n "${HOMELAB_REGISTRY_CONFIG:-}" ]]; then rm -rf "$HOMELAB_REGISTRY_CONFIG"; fi
