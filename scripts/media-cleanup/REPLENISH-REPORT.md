# Bounded replenisher verification

Implemented `replenish.py` and `test_replenish.py`; `controller.py` unchanged.

## Verified locally

Run from `/opt/hermes`:

```text
python3 -m unittest discover -s /opt/data/media-automation/cleanup -p 'test_*.py' -v
Ran 35 tests in 1.306s
OK
```

Includes 14 replenisher tests, with observed RED/GREEN cycles for selection, admission, dry run, add/cooldown, bootstrap cooldown, expiration, CLI, and malformed dates. Remaining regression cases exercise ambiguous POST/readback, failed state writes, malformed APIs/state, real mount mismatch, nonblocking locking, queue/torrent expiration protections, and refreshed pending/disk checks. Network mutations use a fake API only; numeric filesystem measurements are injected for policy tests. Existing controller HTTP tests use loopback only. No live replenishment was executed by this implementation task.

## Policy and integration

- Provider order retained from combined popular/trending discovery. Recent means theatrical release within 730 days, or current/previous year when absent; digital or physical release must have arrived.
- Never adds existing (including unmonitored) or excluded TMDb IDs. At most one add/run, two monitored missing pending, six hours between attempts/additions.
- Updated admission policy: actual free >=15%, and 20GiB per existing pending movie plus one new movie must fit actual free. No additional 15% reservation floor. Test admits at 16% of 1TiB; cleanup's existing 15% ->25% hysteresis is unchanged.
- Uses controller API, secrets and pinned MediaFS validation. CLI defaults dry-run; enable `--apply`. State directory must already exist and be writable. Own nonblocking `/state/replenish.lock`; fsynced atomic `/state/replenish.json` with pre-POST intent and readback.
- `QUALITY_PROFILE_ID=7`, `RESERVE_GIB=20`, `AUTO_TAG_ID=1` are compatible environment settings (`--profile`, `--reserve-gib`, `--auto-tag-id` overrides). Without AUTO_TAG_ID additions are untagged and automatic expiration/bootstrap managed cooldown is disabled.
- Managed Radarr movie `added` timestamps enforce six-hour cooldown even with absent state; no initialization needed for the first externally added tagged movie.
- Tagged monitored missing movies older than seven days can be unmonitored, never deleted, only with complete validated inventory, no matching queue item and no Transmission torrents at all. Fresh protection reads and unmonitor readback required. Maximum one expiration/run; an expiration-only run defers addition until the next schedule.

## Limitations

Reservation is an estimate, not a disk quota; configure matching maximum release size and bounded download concurrency externally. Expanded/extracted files and unrelated writers can exceed it. Parent reported verifying 20480MB release cap and download concurrency one; this task did not modify or independently verify those settings. Cross-service reads and writes are not transactional; other actors may race after final checks. Feed ordering is preserved, not independently reranked as popular/trending labels are absent. Persistent intent blocks immediate retry for six hours on uncertain POST/readback; corrupted/unwritable state fails closed. No fixed library count is imposed. Parent owns scheduler deployment and live verification.
