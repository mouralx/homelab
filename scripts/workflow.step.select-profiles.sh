#!/usr/bin/env bash
set -euo pipefail
inputs=${WORKFLOW_INPUTS:-'{}'}
jq -ce '
  . as $inputs
  | [{profile: "ai", host: "beelink"},
     {profile: "media", host: "rpi5"},
     {profile: "management", host: "rpi4"}]
  | map(select($inputs[.profile] == true))
  | if length == 0 then error("Check at least one workload profile")
    else {include: .} end
' <<< "$inputs"
