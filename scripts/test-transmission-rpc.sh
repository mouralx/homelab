#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
fixture=$(mktemp -d)
suffix=$(basename "$fixture" | tr '[:upper:].' '[:lower:]-')
network="homelab-rpc-$suffix"
server="homelab-rpc-transmission-$suffix"
client="homelab-rpc-digger-$suffix"
volumes=("homelab-rpc-config-$suffix" "homelab-rpc-downloads-$suffix" "homelab-rpc-watch-$suffix")
cleanup() {
  docker rm -f "$client" "$server" >/dev/null 2>&1 || true
  docker network rm "$network" >/dev/null
  docker volume rm "${volumes[@]}" >/dev/null
  rmdir "$fixture"
}
docker network create --internal "$network" >/dev/null
for volume in "${volumes[@]}"; do docker volume create "$volume" >/dev/null; done
trap cleanup EXIT
mounts=(-v "${volumes[0]}:/config" -v "${volumes[1]}:/downloads" -v "${volumes[2]}:/watch")
docker run --rm --network none "${mounts[@]}" busybox:1.37 sh -c '
  printf "%s\n" "{\"rpc_whitelist\":\"127.0.0.1,::1,10.203.0.12\",\"rpc_whitelist_enabled\":true,\"rpc_host_whitelist\":\"transmission.mouras.me\",\"rpc_host_whitelist_enabled\":true}" > /config/settings.json
  chmod 600 /config/settings.json
'
docker run --rm --network none "${mounts[@]}" -v "$PWD/scripts/transmission-storage.sh:/setup.sh:ro" \
  -e TRANSMISSION_STORAGE_UID=911 -e TRANSMISSION_STORAGE_GID=911 busybox:1.37 sh /setup.sh prepare
docker run -d --name "$server" --network "$network" --network-alias transmission "${mounts[@]}" \
  -v "$PWD/scripts/transmission-rpc-init.sh:/custom-cont-init.d/20-homelab-rpc.sh:ro" \
  -e PUID=911 -e PGID=911 -e WHITELIST='*' -e UMASK=002 -e USER=fixture-user -e PASS=fixture-password \
  lscr.io/linuxserver/transmission:latest >/dev/null
docker run -d --name "$client" --network "$network" \
  -e "TRANSMISSION_RPC_URL=http://transmission:9091/transmission/rpc" \
  -e TRANSMISSION_USERNAME=fixture-user -e TRANSMISSION_PASSWORD=fixture-password \
  busybox:1.37 sleep 120 >/dev/null
ready=false
for _attempt in {1..30}; do
  status=$(docker exec "$server" curl --silent --output /dev/null --write-out '%{http_code}' http://127.0.0.1:9091/transmission/rpc || true)
  if [[ "$status" == 401 || "$status" == 409 ]]; then ready=true; break; fi
  sleep 1
done
[[ "$ready" == true ]] || { echo 'Test Transmission daemon did not become ready' >&2; exit 1; }
DIGGER_CONTAINER="$client" TRANSMISSION_CONTAINER="$server" python3 scripts/workflow.step.verify-digger-rpc.py
docker exec "$server" sh -c 'pidof transmission-daemon | xargs -r kill -HUP'
docker exec "$server" jq -e '.rpc_whitelist == "*" and .rpc_whitelist_enabled == false and .rpc_authentication_required == true and (.rpc_host_whitelist | contains("transmission"))' /config/settings.json >/dev/null
echo 'Transmission integration passed: all-IP whitelist, authenticated RPC and session negotiation from another container IP.'
