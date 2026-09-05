#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
checks=0
for beelink in false true; do
  for rpi5 in false true; do
    for rpi4 in false true; do
      inputs=$(jq -cn --argjson beelink "$beelink" --argjson rpi5 "$rpi5" --argjson rpi4 "$rpi4" '{beelink: $beelink, rpi5: $rpi5, rpi4: $rpi4, dry_run: true}')
      if [[ $beelink == false && $rpi5 == false && $rpi4 == false ]]; then
        if WORKFLOW_INPUTS="$inputs" bash scripts/workflow.step.select-profiles.sh >/dev/null 2>&1; then
          echo 'Empty selection was accepted' >&2; exit 1
        fi
      else
        result=$(WORKFLOW_INPUTS="$inputs" bash scripts/workflow.step.select-profiles.sh)
        jq -e --argjson inputs "$inputs" '
          .profile as $selected
          | ($selected | unique | length) == ($selected | length)
          and all(["beelink", "rpi5", "rpi4"][]; . as $name | ($selected | index($name) != null) == $inputs[$name])
          and ($selected - ["beelink", "rpi5", "rpi4"] | length) == 0
        ' <<< "$result" >/dev/null
      fi
      checks=$((checks + 1))
    done
  done
done
printf 'All %s profile selection checks passed.\n' "$checks"
