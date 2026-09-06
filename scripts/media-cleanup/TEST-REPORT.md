# Cleanup controller test report

Executed locally with Python stdlib unittest; no live Radarr/Transmission/media mutations, no container build or runner deployment.

Command (deliberately outside source directory):
```sh
cd /opt/hermes
/opt/hermes/.venv/bin/python -m unittest discover -s /opt/data/media-automation/cleanup -v
```

## Captured execution output

```text
test_cli_default_dry_run_and_no_mount_fails (test_cli.CLITests.test_cli_default_dry_run_and_no_mount_fails) ... ok
test_secret_env_file_and_xml (test_cli.CLITests.test_secret_env_file_and_xml) ... ok
test_candidate_bound_and_invalid_bound (test_controller.ControllerTests.test_candidate_bound_and_invalid_bound) ... ok
test_changed_movie_or_new_hardlink_after_unmonitor_aborts (test_controller.ControllerTests.test_changed_movie_or_new_hardlink_after_unmonitor_aborts) ... ok
test_corrupt_state_and_malformed_torrent_fail_closed (test_controller.ControllerTests.test_corrupt_state_and_malformed_torrent_fail_closed) ... ok
test_default_dry_run_no_mutations_or_state (test_controller.ControllerTests.test_default_dry_run_no_mutations_or_state) ... ok
test_end_to_end_oldest_unmonitor_delete_and_stop_at_target (test_controller.ControllerTests.test_end_to_end_oldest_unmonitor_delete_and_stop_at_target) ... ok
test_hardlink_block_is_reported (test_controller.ControllerTests.test_hardlink_block_is_reported) ... ok
test_inventory_refresh_before_each_mutation (test_controller.ControllerTests.test_inventory_refresh_before_each_mutation) ... ok
test_lock_blocks_second_instance (test_controller.ControllerTests.test_lock_blocks_second_instance) ... ok
test_no_progress_stops_and_retains_hysteresis (test_controller.ControllerTests.test_no_progress_stops_and_retains_hysteresis) ... ok
test_remote_failures_never_continue (test_controller.ControllerTests.test_remote_failures_never_continue) ... ok
test_torrent_symlink_alias_fails_closed (test_controller.ControllerTests.test_torrent_symlink_alias_fails_closed) ... ok
test_torrents_all_statuses_and_queue_are_protected (test_controller.ControllerTests.test_torrents_all_statuses_and_queue_are_protected) ... ok
test_mount_requires_real_mount_and_identity (test_controller.PathTests.test_mount_requires_real_mount_and_identity) ... ok
test_mount_source_root_pins_and_replacement (test_controller.PathTests.test_mount_source_root_pins_and_replacement) ... ok
test_scoped_regular_file_and_hardlinks (test_controller.PathTests.test_scoped_regular_file_and_hardlinks) ... ok
test_symlink_traversal_and_outside_fail (test_controller.PathTests.test_symlink_traversal_and_outside_fail) ... ok
test_thresholds_and_hysteresis (test_controller.PolicyTests.test_thresholds_and_hysteresis) ... ok
test_full_inventory_handshake_and_radarr_payload (test_http.HTTPTests.test_full_inventory_handshake_and_radarr_payload) ... ok
test_partial_queue_rpc_failure_and_request_budget (test_http.HTTPTests.test_partial_queue_rpc_failure_and_request_budget) ... ok

----------------------------------------------------------------------
Ran 21 tests in 1.186s

OK
```

Exit code: 0

TDD slices were executed red then green: thresholds, filesystem safety, cleanup sequence, protection/locking/hysteresis, race checks, HTTP transport/inventories, CLI/secrets, explicit hardlink reporting and mount-source pins. Initial missing-feature tests failed before implementation. Parent found a cwd-dependent test subprocess path; corrected to an absolute path and verified discovery from /opt/hermes.

Coverage: threshold boundaries, persistent hysteresis, oldest sorting, dry-run, unmonitor readback ordering, remote failures, hardlinks, torrent statuses including stopped, queue protection, symlinks/traversal, missing mount, pinned mount identity/replacement, changed files, inventory refresh, bounds, no-progress, lock contention, malformed state, Transmission 409 and real loopback HTTP payloads. Loopback server responses and free-space values are explicit fixtures, not live API results.

## Authorized source staging (not execution)

After tests, copied controller.py only through Portainer environment 4 into Bazarr `/config/media-cleanup/controller.py` (host `/home/moura/bazarr/media-cleanup/controller.py`). Readback verified 18,038 bytes, mode 0644, SHA256 `3097e7d7c2f9caa6b0931f22f539ef8192ffa98e0c3d3bb02c3c66eb48cef753`. No controller execution, scheduler or existing Bazarr configuration changes. The separately prepared state directory was not touched.

GitHub source on `mouralx/home-lab` branch `feat/rpi5-media-automation`, `scripts/media-cleanup/controller.py`, has blob SHA `87a1bec0fffd95f40da5f23a5003e82e0c80220f`, identical to the locally computed Git blob hash. Private raw URL returned 404; verification used authenticated GitHub MCP directory metadata instead. No PR or merge.

Limitations: no live-mounted filesystem or real-service integration tested; cross-service races cannot be atomic; retained links/recycle bin/open files may block reclamation; no scheduler installed. See README.md for container instructions and all safety limitations.
