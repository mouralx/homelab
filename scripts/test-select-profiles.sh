#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
checks=0
for ai in false true; do
  for media in false true; do
    for management in false true; do
      inputs=$(jq -cn --argjson ai "$ai" --argjson media "$media" --argjson management "$management" '{ai: $ai, media: $media, management: $management, dry_run: true}')
      if [[ $ai == false && $media == false && $management == false ]]; then
        if WORKFLOW_INPUTS="$inputs" bash scripts/workflow.step.select-profiles.sh >/dev/null 2>&1; then
          echo 'Empty selection was accepted' >&2; exit 1
        fi
      else
        result=$(WORKFLOW_INPUTS="$inputs" bash scripts/workflow.step.select-profiles.sh)
        jq -e --argjson inputs "$inputs" '
          [.include[].profile] as $selected
          | ($selected | unique | length) == ($selected | length)
          and all(["ai", "media", "management"][]; . as $name | ($selected | index($name) != null) == $inputs[$name])
          and ($selected - ["ai", "media", "management"] | length) == 0
          and all(.include[]; .host == ({ai: "beelink", media: "rpi5", management: "rpi4"}[.profile]))
        ' <<< "$result" >/dev/null
      fi
      checks=$((checks + 1))
    done
  done
done
printf 'All %s profile selection checks passed.\n' "$checks"
