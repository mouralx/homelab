#!/usr/bin/env bash
set -euo pipefail

# Read resolved JSON from: docker compose --env-file .env config --format json
jq -er '
  def require($ok; $message): if $ok then . else error($message) end;
  def visit($services; $name; $path):
    if ($path | index($name)) != null then error("Dependency cycle involving " + $name)
    else $services[$name].depends_on // {} | keys[] | visit($services; .; $path + [$name])
    end;

  .services as $services
  | require(($services | type) == "object"; "Missing services")
  | [$services | to_entries[] as $service
      | ($service.value.depends_on // {} | to_entries[])
      | .key as $dependency
      | require($services | has($dependency); "Missing dependency " + $dependency + " for " + $service.key)
      | if .value.condition == "service_healthy" then
          ($services[$dependency].healthcheck // {}) as $health
          | require(($health.test != null and $health.test != ["NONE"] and $health.disable != true);
                    "Dependency " + $dependency + " needs a healthcheck")
        else . end] as $dependencies
  | [$services | keys[] | visit($services; .; [])] as $cycles
  | [$services[] | .container_name // empty] as $names
  | require(($names | length) == ($names | unique | length); "Duplicate container name")
  | [$services | to_entries[] as $service | $service.value.ports[]?
      | select(.published != null)
      | (.published | tostring | split("-") | map(tonumber)) as $range
      | {service: $service.key, host: (.host_ip // "0.0.0.0"), protocol: (.protocol // "tcp"),
         start: $range[0], end: $range[-1]}
      | require((.start >= 1 and .end <= 65535 and .start <= .end); "Invalid published port")
    ] as $ports
  | [$ports | to_entries[] as $left | to_entries[]
      | select(.key > $left.key) | .value as $right | $left.value
      | select(.protocol == $right.protocol and .start <= $right.end and $right.start <= .end)
      | select(.host == $right.host or .host == "0.0.0.0" or .host == "::"
               or $right.host == "0.0.0.0" or $right.host == "::")
      | .service + " and " + $right.service + " on " + (.start | tostring) + "/" + .protocol
    ] as $conflicts
  | require(($conflicts | length) == 0; "Published port conflict: " + ($conflicts | join(", ")))
  | "Validated \($services | length) services, their dependencies, and \($ports | length) published port bindings; no conflicts."
'
