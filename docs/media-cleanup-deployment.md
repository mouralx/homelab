# RPi5 cleanup and replenishment deployment

Portainer **media-cleanup**, environment 4, runs retirement followed by automatic movie selection every 900 seconds, with `unless-stopped`. Stopping this stack disables both retirement and selection, but not already queued downloads.

## Deployment

Stage both tested modules under `/home/moura/bazarr/media-cleanup/`: `controller.py` and `replenish.py`. The directory is mounted read-only at `/app`. Media and Radarr config are read-only; `/state` is writable. Run as UID/GID 911 with dropped capabilities. Credentials come from the mounted Radarr config, never Git or Compose.

The live command is:

```sh
while true; do
  python -B /app/controller.py --apply && python -B /app/replenish.py --apply
  sleep 900
done
```

Use the existing Portainer stack, not a duplicate Compose project. Validate `services/compose.media-cleanup.yaml`, run both modules without `--apply` first on a new deployment, then verify deployed source hashes and live command. Rootless host UID mapping means host paths must not be blindly chowned to 911. Never clear persistent state to bypass an active cycle.

Cleanup remains below 15% actual free -> at least 25%, oldest eligible first, including unwatched, preserving every retained torrent and multiply linked file. Unmonitor/readback precedes Radarr movie-file deletion; keep the movie record.

Replenishment uses profile 7, root `/downloads/library/movies`, tag 1 (`auto-popular-recent`), an estimated 20 GiB per pending movie, one addition per six hours and at most two pending movies. Radarr release cap is 20480 MB, Transmission download queue size 1. See [media-automation.md](media-automation.md) for policy details and conservative limits.

## Verified progress and current blocker

The combined suite passed 35 tests, Compose validated, and deployed replenisher SHA256 matched tested source: `2d6d52cfee15c373a380466957f250e0311c7c6470ea636ae508601f58252319`. Scheduled apply logs show cleanup no-op and replenisher cooldown.

The first automatic selection, The Whisper Man (TMDb 860508), completed downloading and imported into Radarr automatically: 3840x2160, H265, English EAC3 Atmos. No real movie deletion was forced for testing. Jellyfin display/playback and the full-disk retirement/refill cycle remain unverified. Subtitle-provider credentials are still absent.

**A subsequent fresh-discovery check exposed an outbound Docker bridge network failure.** Internal APIs and DNS work; new TCP connections from both the lab and default Docker bridges to unrelated public addresses time out. The same addresses connect from a host-network probe. Thus the scheduler is enabled, but future discovery is blocked until bridge egress is restored. A successful cooldown run does not verify discovery.

Temporary diagnostic containers/stacks were removed. A temporary Transmission upload cap did not fix connectivity and was restored. No Docker-daemon restart or network recreation has been performed; an interrupting repair needs approval. Inspect local container logs for API failures, protected/no-progress retirement and replenishment reasons; no external alert channel is configured.
