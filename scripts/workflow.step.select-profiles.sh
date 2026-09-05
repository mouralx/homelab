#!/usr/bin/env bash
set -euo pipefail
inputs=${WORKFLOW_INPUTS:-'{}'}
jq -ce '
  . as $inputs
  | ["beelink", "rpi5", "rpi4"]
  | map(select($inputs[.] == true))
  | if length == 0 then error("Check at least one machine profile")
    else {profile: .} end
' <<< "$inputs"
