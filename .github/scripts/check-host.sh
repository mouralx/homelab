#!/usr/bin/env bash
set -euo pipefail

test -S "/run/user/${USER_ID}/docker.sock"
if [ -z "$GITEA_RUNNER_REGISTRATION_TOKEN" ]; then
  echo 'Configure the GITEA_RUNNER_REGISTRATION_TOKEN environment secret before deployment.' >&2
  exit 1
fi
