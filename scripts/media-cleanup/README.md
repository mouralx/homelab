# Conservative movie disk cleanup

Python 3.11+ / Linux / stdlib only. **Not deployed or scheduled by this artifact.** No torrent deletion and no direct media filesystem writes. Default is dry-run; `--apply` enables remote Radarr mutations.

## Policy and safety

- Measure `statvfs('/downloads')`: available bytes (`f_bavail * f_frsize`) divided by actual filesystem capacity (`f_blocks * f_frsize`), not container overlay or Radarr's diskspace API.
- Start strictly **below 15%**. Persist the active cycle in `/state/state.json`; continue between invocations until **at least 25%**. Exactly 15% does not start a new cycle; exactly 25% ends one.
- Oldest **Radarr `added` timestamp** first (ID tie-break), including unwatched movies; no watch-history exemption.
- Protect **every** Transmission torrent, including stopped, downloading and seeding torrents. Protect its entire top-level file/tree and all Radarr queued movies. An unmapped queue item blocks the run. All torrent paths must map into the shared `/downloads` namespace; ambiguous paths fail closed.
- **Hardlinks with `st_nlink > 1` are skipped** and reported as `hardlink_no_reclaim`. No attempt is made to remove torrent links. Retained torrents/hardlinks may prevent reaching 25%; this is intentional conservative protection, not successful reclamation.
- Full Radarr movie + per-movie-file inventories, full Radarr queue and Transmission `torrent-get` without an IDs filter must succeed before mutation. Refresh full inventories before each unmonitor and again before deletion. Unsupported/truncated/malformed inventories stop the run.
- PUT the freshly fetched complete `/api/v3/movie/{id}?moveFiles=false` resource with `monitored=false`; GET and verify it before DELETE `/api/v3/moviefile/{id}`. GET the movie after deletion and require it remain unmonitored with no file. **Never DELETE the movie record.** A failure after unmonitor leaves the record unmonitored for safety.
- Require an exact `/downloads` mountinfo entry and either source/root pins or an operator-prepared identity marker. No symlinks or `..`, regular files only, same filesystem device. Recheck paths, inode/size/mtime, hardlinks and mount identity before DELETE. The mount ID is captured only within one invocation, not pinned across restarts.
- One nonblocking `flock` on persistent `/state/controller.lock`, atomic/fsynced hysteresis updates. Missing state directory or malformed state fails closed. Dry-run may create the lock file but does not change hysteresis or remote state.
- Default 20 candidates (maximum 100), 500 HTTP requests (maximum 10,000), 15-second per-request timeout, 32 MiB response cap and 10,000 records per inventory. File inventories use 50-movie batches. A queue response that does not contain its declared total aborts rather than assuming pagination is complete. Radarr writes are never retried. Transmission supports one HTTP 409 session-ID retry.
- Remeasure after each deletion. Stop immediately if available bytes do not increase; JSON reports `stop_reason: no_progress`. An active result is **not** a claim that target capacity was reached.

## Run tests locally

```sh
cd /opt/data/media-automation/cleanup
/opt/hermes/.venv/bin/python -m unittest -v
/opt/hermes/.venv/bin/python controller.py --help
```

Tests use private temporary files and a loopback HTTP server, not real media, credentials or services.

## Configuration (no secrets in source or CLI)

- `RADARR_URL`: default `http://radarr:7878`
- `TRANSMISSION_URL`: default `http://transmission:9091/transmission/rpc`
- `RADARR_API_KEY` or `RADARR_API_KEY_FILE`; alternatively `RADARR_CONFIG_XML=/run/radarr/config.xml` reads `<ApiKey>` from an explicitly configured read-only file.
- `TRANSMISSION_USERNAME` / `TRANSMISSION_PASSWORD` or corresponding `_FILE` variables. Omit both only if RPC authentication is intentionally disabled.
- Prefer **both** `EXPECTED_MOUNT_SOURCE=/dev/nvme0n1p2` and `EXPECTED_MOUNT_ROOT=/home/moura/transmission/downloads` (verify against the deployment host's mountinfo). Alternatively set `MEDIA_VOLUME_ID` to match an already provisioned `/downloads/.cleanup-volume-id`. The controller does not create the marker. If both mechanisms are configured, both must pass.

## Container deployment instructions (operator review required)

Use the same media bind and path namespace as Radarr and Transmission. The media bind can and should be **read-only in this controller**: Radarr performs deletion through its own writable mount. Use a dedicated writable persistent state directory; all instances must share it. Do not mount the Docker socket. Example, replace network/state/script host paths after review:

```sh
docker run --rm --read-only --cap-drop ALL --security-opt no-new-privileges \
  --network YOUR_MEDIA_NETWORK \
  --mount type=bind,src=/home/moura/transmission/downloads,dst=/downloads,readonly \
  --mount type=bind,src=/home/moura/bazarr/media-cleanup/controller.py,dst=/app/controller.py,readonly \
  --mount type=bind,src=/home/moura/radarr/config.xml,dst=/run/radarr/config.xml,readonly \
  --mount type=bind,src=/YOUR/DEDICATED/cleanup-state,dst=/state \
  -e RADARR_CONFIG_XML=/run/radarr/config.xml \
  -e EXPECTED_MOUNT_SOURCE=/dev/nvme0n1p2 \
  -e EXPECTED_MOUNT_ROOT=/home/moura/transmission/downloads \
  python:3.13-slim python /app/controller.py
```

If Transmission needs authentication, mount private username/password files and set their `_FILE` paths. Verify a dry run first; append `--apply` **only after review**. Run as a restricted numeric UID if it has read access to media/config and write access to state. Image supports ARM64; pin a reviewed image digest for production. This project neither builds nor starts a container or scheduler.

Exit 0 means the bounded run completed (possibly still active/blocked); exit 2 means fail-closed abort. Monitor JSON `active`, `free_bytes`, `total_bytes`, `blocked`, `deleted` and error output.

## Limits requiring deployment review

Radarr and Transmission do not expose a cross-service transaction/lock: snapshot rechecks narrow but cannot eliminate the final race with imports, torrent changes or external remonitoring. Coordinate external automation; do not let another actor remonitor cleaned records. Other clients/downloaders are not inventoried. Radarr recycle-bin settings, open files, concurrent disk writers and retained hardlinks can prevent physical reclamation; no-progress detection stops further deletion in that invocation, with hysteresis left active. Validate recycle-bin behavior before scheduling. Dry-run lists eligible candidates but cannot predict true reclaimed bytes. Same-device bind aliases outside the agreed path namespace cannot be fully detected. No live service/mount integration or container build has been performed by these tests.

API payload reference: official Radarr source, `develop` branch (reviewed during implementation):
- https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/Movies/MovieController.cs
- https://github.com/Radarr/Radarr/blob/develop/src/Radarr.Api.V3/MovieFiles/MovieFileController.cs
