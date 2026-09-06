# Live RPi5 cleanup deployment

The approved cleanup policy is enabled in the separate Portainer **media-cleanup** stack (environment4). The container runs the controller, then sleeps900 seconds, repeating while running; `unless-stopped` makes it persistent across daemon restarts. Stop this stack to disable automatic deletion without affecting other services.

- Trigger: strictly below15% free; retain active hysteresis until at least25% free.
- Oldest Radarr-added eligible movie first, including unwatched.
- Protect all retained Transmission torrents, queued imports and multiply-linked files. Files retained by these protections can prevent the free-space target; never weaken protection automatically.
- Unmonitor and read back before deleting only the movie file through Radarr. Keep the movie record unmonitored; no direct filesystem deletion by the controller.
- Read-only media/source/config mounts; only persistent `/state` is writable. Run as911:911, capabilities dropped, no exposed ports. The Radarr API key is read privately from its read-only config.xml mount, not embedded in Compose.
- Pin the verified block-device source and bind-root. If storage is migrated, verify the new filesystem before intentionally updating these pins. Do not copy this host-specific manifest blindly to another machine.

## Reproduce or update

From a checked-out repository on RPi5, copy the controller to the dedicated host code directory:

```sh
install -Dm644 scripts/media-cleanup/controller.py "$HOME/bazarr/media-cleanup/controller.py"
docker exec --user 911:911 bazarr python3 -c "from pathlib import Path; Path('/config/media-cleanup-state').mkdir(mode=0o700, exist_ok=True)"
docker compose -p media-cleanup -f services/compose.media-cleanup.yaml config --quiet
```

Do not host-chown to911 blindly: Docker here is rootless and container IDs map to subordinate host IDs. Never overwrite the existing persistent state to clear an active cleanup cycle.

Before enabling on a new deployment, use a temporary manifest with restart `no` and command `[python, -B, /app/controller.py]` (no `--apply`) and verify the dry-run output. Then apply the committed manifest. For the existing Portainer-managed stack, update it through Portainer rather than launching a differently named duplicate stack. Source updates must precede runner restarts; verify deployed source SHA256 against the tested file.

## Observed verification

-21 isolated tests passed from a different working directory, including HTTP fixtures and failure cases.
-Controller source copied to RPi5 and independently hash-verified against local tested source and GitHub blob.
-One-shot live dry-run exited0 with apply=false, active=false, no deletions.
-First scheduled enabled execution reported apply=true, active=false, no deletions.
-Read-only inventory from the runner using its mounted Radarr config succeeded:0 movies,0 torrents,0 queued items.
-Persistent state read-back was version1, active=false.

No real user media was deleted to test this. A real media download/import/playback round trip remains untested. Inspect container logs for active cycles, skipped IDs, no-progress stops or API failures; no external alert channel is configured.

Radarr movie renaming is enabled with the existing title/year/quality format. The rest of the live movie/subtitle setup and outstanding provider-account requirement are documented in [media-automation.md](media-automation.md).
