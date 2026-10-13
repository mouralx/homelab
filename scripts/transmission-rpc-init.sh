#!/bin/sh
set -eu
# LSIO writes legacy hyphenated keys; newer saved settings use underscores.
# Apply the same access policy to both formats after the image's own init.
umask 077
settings_temp=$(mktemp /config/.homelab-rpc.XXXXXX)
trap 'rm -f "$settings_temp"' EXIT
jq '
  .["rpc-whitelist"] = "*" | .rpc_whitelist = "*"
  | .["rpc-whitelist-enabled"] = false | .rpc_whitelist_enabled = false
  | ((.rpc_host_whitelist // .["rpc-host-whitelist"] // "")
      | split(",") + ["transmission", "localhost"]
      | map(select(length > 0)) | unique | join(",")) as $hosts
  | .["rpc-host-whitelist"] = $hosts | .rpc_host_whitelist = $hosts
  | if ((env.USER // "") | length) > 0 and ((env.PASS // "") | length) > 0 then
      .["rpc-authentication-required"] = true | .rpc_authentication_required = true
      | .["rpc-username"] = env.USER | .rpc_username = env.USER
      | .["rpc-password"] = env.PASS | .rpc_password = env.PASS
    else . end
' /config/settings.json > "$settings_temp"
chown "${PUID:-911}:${PGID:-911}" "$settings_temp"
chmod 600 "$settings_temp"
mv "$settings_temp" /config/settings.json
echo 'Transmission RPC settings synchronized: all client IPs allowed and internal hostname included.'
