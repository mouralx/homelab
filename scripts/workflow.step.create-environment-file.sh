#!/usr/bin/env bash
set -euo pipefail
: "${ENVIRONMENT_VARS:?ENVIRONMENT_VARS is required}"
: "${ENVIRONMENT_SECRETS:?ENVIRONMENT_SECRETS is required}"

jq -r 'to_entries[] | select(.key != "GITHUB_TOKEN") | "\(.key)=\(.value | tostring)"' <<< "$ENVIRONMENT_VARS" > .env
jq -r 'to_entries[] | select(.key != "GITHUB_TOKEN") | "\(.key)=\(.value | tostring)"' <<< "$ENVIRONMENT_SECRETS" >> .env
