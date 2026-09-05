#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

check() {
  local expected=$1 config=$2
  local actual=pass
  if ! printf '%s\n' "$config" | bash scripts/validate-compose.sh >/dev/null 2>&1; then
    actual=fail
  fi
  if [[ "$actual" != "$expected" ]]; then
    printf 'Expected %s, got %s for %s\n' "$expected" "$actual" "$config" >&2
    exit 1
  fi
}

check pass '{"services":{"a":{"ports":[{"published":"8080","protocol":"tcp"}]},"b":{"ports":[{"published":"8080","protocol":"udp"}]}}}'
check fail '{"services":{"a":{"ports":[{"published":"8080"}]},"b":{"ports":[{"published":"8080"}]}}}'
check fail '{"services":{"a":{"ports":[{"published":"8080-8090"}]},"b":{"ports":[{"published":"8085","host_ip":"127.0.0.1"}]}}}'
check pass '{"services":{"a":{"ports":[{"published":"8080","host_ip":"127.0.0.1"}]},"b":{"ports":[{"published":"8080","host_ip":"127.0.0.2"}]}}}'
check fail '{"services":{"a":{"ports":[{"published":"8080","host_ip":"::"}]},"b":{"ports":[{"published":"8080","host_ip":"127.0.0.1"}]}}}'
check fail '{"services":{"a":{"container_name":"same"},"b":{"container_name":"same"}}}'
check pass '{"services":{"app":{"depends_on":{"db":{"condition":"service_healthy"}}},"db":{"healthcheck":{"test":["CMD","pg_isready"]}}}}'
check fail '{"services":{"app":{"depends_on":{"db":{"condition":"service_healthy"}}},"db":{}}}'
check fail '{"services":{"app":{"depends_on":{"db":{}}}}}'
check fail '{"services":{"app":{"depends_on":{"db":{}}},"db":{"depends_on":{"app":{}}}}}'
printf 'All 10 Compose validator checks passed.\n'
