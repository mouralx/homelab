"""Offline replenisher policy tests; no live Radarr mutations."""
import importlib
import unittest
import json
import tempfile
from pathlib import Path
from types import SimpleNamespace
from datetime import datetime, timedelta, timezone

NOW = datetime(2026, 9, 6, 12, tzinfo=timezone.utc)


def movie(tmdb=1, **changes):
    item = dict(tmdbId=tmdb, title=f'Movie {tmdb}', year=2026,
                inCinemas='2026-06-01T00:00:00Z', digitalRelease='2026-08-01T00:00:00Z',
                physicalRelease=None, isExisting=False, isExcluded=False)
    item.update(changes)
    return item


class SelectionTests(unittest.TestCase):
    def test_selects_recent_released_in_provider_order(self):
        spec = importlib.util.find_spec('replenish')
        self.assertIsNotNone(spec, 'replenisher implementation is missing')
        r = importlib.import_module('replenish')
        items = [movie(1, inCinemas='2020-01-01T00:00:00Z'),
                 movie(2, digitalRelease='2027-01-01T00:00:00Z'),
                 movie(3, isExisting=True), movie(4, isExcluded=True), movie(5), movie(6)]
        self.assertEqual([x['tmdbId'] for x in r.select(items, {5}, NOW)], [6])
        self.assertEqual([x['tmdbId'] for x in r.select([movie(8), movie(7)], set(), NOW)], [8, 7])


    def test_date_boundaries_fallback_duplicates_and_malformed_dates(self):
        import replenish as r
        items = [movie(1, inCinemas=(NOW - timedelta(days=730)).isoformat()),
                 movie(2, inCinemas=(NOW - timedelta(days=730, seconds=1)).isoformat()),
                 movie(3, inCinemas=None, year=2025), movie(4, inCinemas=None, year=2024),
                 movie(5, inCinemas=(NOW + timedelta(seconds=1)).isoformat()),
                 movie(6, digitalRelease=None, physicalRelease=NOW.isoformat()),
                 movie(7), movie(7, isExcluded=True), movie(1)]
        self.assertEqual([x['tmdbId'] for x in r.select(items, set(), NOW)], [1, 3, 6])
        for value in (False, 0, [], {}, 'not-a-date', '2026-01-01'):
            with self.subTest(value=value):
                with self.assertRaises(r.SafetyError):
                    r.select([movie(1, inCinemas=value)], set(), NOW)


class BudgetTests(unittest.TestCase):
    def test_admits_at_sixteen_percent_but_bounds_pending_and_reservations(self):
        import replenish as r
        self.assertTrue(hasattr(r, 'budget'), 'disk admission policy is missing')
        gib = 1024 ** 3
        total = 1024 * gib
        self.assertIsNone(r.budget(total * 16 // 100, total, 0))
        self.assertEqual(r.budget(total * 14 // 100, total, 0), 'below_15_percent')
        self.assertEqual(r.budget(16 * gib, 100 * gib, 0), 'reservation_exceeds_free')
        self.assertIsNone(r.budget(40 * gib, 100 * gib, 1))
        self.assertEqual(r.budget(39 * gib, 100 * gib, 1), 'reservation_exceeds_free')
        self.assertEqual(r.budget(80 * gib, 100 * gib, 2), 'pending_limit')
        self.assertEqual(r.budget(30 * gib, 100 * gib, 0, reserve_gib=40), 'reservation_exceeds_free')
        for free, capacity, pending in [(-1, total, 0), (total + 1, total, 0), (1, 0, 0), (1, total, -1)]:
            with self.subTest(free=free, capacity=capacity, pending=pending):
                with self.assertRaises(r.SafetyError):
                    r.budget(free, capacity, pending)


class FakeAPI:
    radarr = 'http://offline/api/v3'

    def __init__(self):
        self.movies = []
        self.discovery = [movie(10), movie(11)]
        self.posts = []
        self.queue = []
        self.torrents = []

    def request(self, method, url, payload=None):
        if method == 'GET' and url == self.radarr + '/movie':
            return self.movies
        if method == 'GET' and url == self.radarr + '/importlist/movie?includePopular=true&includeTrending=true':
            return self.discovery
        if method == 'POST' and url == self.radarr + '/movie':
            self.posts.append(payload)
            item = dict(payload, id=99 + len(self.posts), hasFile=False, added=NOW.isoformat())
            self.movies.append(item)
            return item
        raise AssertionError((method, url))

    def movie(self, mid):
        return next(x.copy() for x in self.movies if x['id'] == mid)

    def inventory(self):
        return self.movies, self.torrents, self.queue

    def unmonitor(self, item):
        next(x for x in self.movies if x['id'] == item['id'])['monitored'] = False


class RunTests(unittest.TestCase):
    def setUp(self):
        import replenish
        self.r = replenish
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.state = Path(self.temp.name)
        self.api = FakeAPI()
        self.fs = SimpleNamespace(measure=lambda: (500 * 1024 ** 3, 1024 * 1024 ** 3))

    def run_once(self, **kwargs):
        return self.r.run(self.api, self.fs, self.state, now=NOW, **kwargs)

    def test_apply_adds_one_with_explicit_payload_readback_and_persisted_cooldown(self):
        result = self.run_once(apply=True, auto_tag_id=9)
        self.assertEqual(result['reason'], 'added')
        self.assertEqual(len(self.api.posts), 1)
        payload = self.api.posts[0]
        self.assertIs(payload['monitored'], True)
        self.assertEqual(payload['qualityProfileId'], 7)
        self.assertEqual(payload['rootFolderPath'], '/downloads/library/movies')
        self.assertEqual(payload['minimumAvailability'], 'released')
        self.assertEqual(payload['addOptions'], dict(searchForMovie=True, monitor='movieOnly'))
        self.assertEqual(payload['tags'], [9])
        state = json.loads((self.state / 'replenish.json').read_text())
        self.assertEqual(state['last_successful_add'], NOW.isoformat())
        self.assertEqual(self.run_once(apply=True)['reason'], 'cooldown')
        self.assertEqual(len(self.api.posts), 1)
        self.assertEqual(self.r.run(self.api, self.fs, self.state, apply=True,
                                   now=NOW + timedelta(hours=6), profile=8)['reason'], 'added')
        self.assertEqual(self.api.posts[-1]['qualityProfileId'], 8)

    def test_recent_managed_movie_enforces_cooldown_without_state(self):
        self.api.movies = [dict(id=1, tmdbId=99, monitored=True, hasFile=False,
                                tags=[9], added=(NOW - timedelta(hours=5)).isoformat())]
        self.assertEqual(self.run_once(apply=True, auto_tag_id=9)['reason'], 'cooldown')
        self.assertEqual(self.api.posts, [])

    def test_expires_one_old_managed_missing_only_when_queue_and_torrents_clear(self):
        for protection in ('none', 'queue', 'torrent', 'untagged', 'young', 'has_file'):
            with self.subTest(protection=protection):
                self.api = FakeAPI()
                self.api.discovery = []
                old = dict(id=1, tmdbId=91, title='Old', monitored=True, hasFile=False,
                           tags=[9], added=(NOW - timedelta(days=8)).isoformat())
                self.api.movies = [old, dict(old, id=2, tmdbId=92)]
                if protection == 'queue':
                    self.api.queue = [dict(movieId=1), dict(movieId=2)]
                if protection == 'torrent':
                    self.api.torrents = [dict(id=1)]
                if protection == 'untagged':
                    for item in self.api.movies:
                        item['tags'] = []
                if protection == 'young':
                    for item in self.api.movies:
                        item['added'] = (NOW - timedelta(days=7)).isoformat()
                if protection == 'has_file':
                    for item in self.api.movies:
                        item['hasFile'] = True
                result = self.run_once(apply=True, auto_tag_id=9)
                self.assertEqual(sum(not x['monitored'] for x in self.api.movies), int(protection == 'none'))
                self.assertEqual(len(self.api.movies), 2)
                if protection == 'none':
                    self.assertEqual(result['expired'], {'id': 1, 'title': 'Old'})

    def test_cli_defaults_and_mount_failure_emit_sanitized_json(self):
        import subprocess
        import os
        self.assertTrue(hasattr(self.r, 'parser'), 'CLI is missing')
        args = self.r.parser().parse_args([])
        self.assertFalse(args.apply)
        self.assertEqual(args.profile, 7)
        self.assertEqual(args.reserve_gib, 20)
        env = dict(os.environ, RADARR_API_KEY='DO-NOT-PRINT', EXPECTED_MOUNT_SOURCE='/impossible')
        reply = subprocess.run(['python3', str(Path(__file__).with_name('replenish.py')),
                                '--state-dir', str(self.state)], env=env, capture_output=True, text=True)
        self.assertEqual(reply.returncode, 2)
        self.assertEqual(json.loads(reply.stdout)['reason'], 'stopped')
        self.assertNotIn('DO-NOT-PRINT', reply.stdout + reply.stderr)

    def test_uncertain_post_and_bad_readback_retain_cooldown(self):
        for failure in ('post', 'id', 'tmdbId', 'qualityProfileId', 'rootFolderPath', 'monitored'):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as directory:
                api = FakeAPI()
                original_request, original_movie = api.request, api.movie
                def request(method, url, payload=None):
                    answer = original_request(method, url, payload)
                    if method == 'POST' and failure == 'post':
                        raise self.r.SafetyError('HTTP transport or JSON failed')
                    return answer
                def readback(mid):
                    item = original_movie(mid)
                    if failure != 'post':
                        item[failure] = False if failure == 'monitored' else 'wrong'
                    return item
                api.request, api.movie = request, readback
                with self.assertRaises(self.r.SafetyError):
                    self.r.run(api, self.fs, directory, apply=True, now=NOW)
                state = json.loads((Path(directory) / 'replenish.json').read_text())
                self.assertIsNone(state['last_successful_add'])
                self.assertEqual(state['last_attempt'], NOW.isoformat())
                self.assertEqual(self.r.run(api, self.fs, directory, apply=True, now=NOW)['reason'], 'cooldown')
                self.assertEqual(len(api.posts), 1)

    def test_bad_state_and_failed_intent_write_never_post(self):
        target = self.state / 'replenish.json'
        for text in ('not-json', '[]', '{}', '{"version":1,"last_attempt":"bad","last_successful_add":null}'):
            target.write_text(text)
            with self.assertRaises(self.r.SafetyError):
                self.run_once(apply=True)
        target.unlink()
        (self.state / 'replenish.tmp').mkdir()
        with self.assertRaises(self.r.SafetyError):
            self.run_once(apply=True)
        self.assertEqual(self.api.posts, [])

    def test_nonblocking_lock_and_real_mount_mismatch_fail_closed(self):
        with self.r.lock(self.state):
            with self.assertRaises(self.r.SafetyError):
                self.run_once(apply=True)
        self.fs = self.r.MediaFS(self.state, self.state, '', '/wrong', '/wrong')
        with self.assertRaises(self.r.SafetyError):
            self.run_once(apply=True)
        self.assertEqual(self.api.posts, [])

    def test_malformed_inventory_discovery_and_queue_never_mutate(self):
        for records in (None, {}, [dict(id=True)], [dict(id=1, tmdbId=2, monitored='true', hasFile=False)]):
            self.api.movies = records
            with self.assertRaises(self.r.SafetyError):
                self.run_once(apply=True)
        self.api.movies = []
        for records in (None, {}, [movie(), dict(tmdbId=2)], [movie(tmdb=True)]):
            self.api.discovery = records
            with self.assertRaises(self.r.SafetyError):
                self.run_once(apply=True)
        self.api.movies = [dict(id=1, tmdbId=1, title='Old', monitored=True, hasFile=False,
                                tags=[9], added=(NOW - timedelta(days=8)).isoformat())]
        self.api.queue = [dict(movieId=0)]
        with self.assertRaises(self.r.SafetyError):
            self.run_once(apply=True, auto_tag_id=9)
        self.assertTrue(self.api.movies[0]['monitored'])
        self.assertEqual(self.api.posts, [])

    def test_refresh_rechecks_pending_and_disk_before_post(self):
        original = self.api.request
        reads = []
        def request(method, url, payload=None):
            if url.endswith('/movie') and method == 'GET':
                reads.append(1)
                if len(reads) == 2:
                    return [dict(id=n, tmdbId=n, monitored=True, hasFile=False) for n in (1, 2)]
            return original(method, url, payload)
        self.api.request = request
        self.assertEqual(self.run_once(apply=True)['reason'], 'pending_limit')
        self.api.request = original
        measures = iter([(500 * 1024 ** 3, 1024 ** 4), (10 * 1024 ** 3, 1024 ** 4)])
        self.fs.measure = lambda: next(measures)
        self.assertEqual(self.run_once(apply=True)['reason'], 'below_15_percent')
        self.assertEqual(self.api.posts, [])

    def test_expiration_dryrun_and_readback_failure(self):
        self.api.movies = [dict(id=1, tmdbId=1, title='Old', monitored=True, hasFile=False,
                                tags=[9], added=(NOW - timedelta(days=8)).isoformat())]
        self.assertEqual(self.run_once(auto_tag_id=9)['reason'], 'would_expire')
        self.assertTrue(self.api.movies[0]['monitored'])
        self.api.unmonitor = lambda item: None
        with self.assertRaises(self.r.SafetyError):
            self.run_once(apply=True, auto_tag_id=9)
        self.assertEqual(self.api.posts, [])

    def test_dryrun_selects_without_mutations_and_existing_unmonitored_are_skipped(self):
        self.assertTrue(hasattr(self.r, 'run'), 'run-once integration is missing')
        self.api.movies = [dict(id=1, tmdbId=10, monitored=False, hasFile=False)]
        result = self.run_once()
        self.assertEqual(result['reason'], 'dry_run')
        self.assertEqual(result['title'], 'Movie 11')
        self.assertEqual(self.api.posts, [])
        self.assertFalse((self.state / 'replenish.json').exists())


if __name__ == '__main__':
    unittest.main()
