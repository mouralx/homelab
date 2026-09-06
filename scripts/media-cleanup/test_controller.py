import unittest
import os
import tempfile
from pathlib import Path


class PathTests(unittest.TestCase):
    def test_scoped_regular_file_and_hardlinks(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            f = root / 'movie.mkv'
            f.write_bytes(b'fixture')
            self.assertTrue(c.safe_file(f, root, root.stat().st_dev))
            os.link(f, root / 'torrent.mkv')
            self.assertFalse(c.safe_file(f, root, root.stat().st_dev))

    def test_symlink_traversal_and_outside_fail(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / 'library'
            root.mkdir()
            f = Path(tmp) / 'outside.mkv'
            f.write_bytes(b'fixture')
            (root / 'link').symlink_to(f)
            for bad in [f, root / 'link', root / '..' / 'outside.mkv', root / 'missing']:
                with self.subTest(path=str(bad)), self.assertRaises(c.SafetyError):
                    c.safe_file(bad, root, root.stat().st_dev)

    def test_mount_source_root_pins_and_replacement(self):
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as tmp:
            mount = Path(tmp)
            fs = c.MediaFS(mount, mount, '', expected_source='/dev/test', expected_root='/media')
            line = f'7 1 8:1 /media {mount} ro - ext4 /dev/test rw\n'
            with patch.object(Path, 'read_text', return_value=line):
                self.assertEqual(fs.verify(), mount.stat().st_dev)
                fs.expected_root = '/wrong'
                with self.assertRaises(c.SafetyError):
                    fs.verify()
                fs.expected_root = '/media'
            with patch.object(Path, 'read_text', return_value=line.replace('7 1', '8 1')):
                with self.assertRaises(c.SafetyError):
                    fs.verify()

    def test_mount_requires_real_mount_and_identity(self):
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(c.SafetyError):
                c.MediaFS(Path(tmp), Path(tmp), 'uuid-marker').verify()
import controller as c


class PolicyTests(unittest.TestCase):
    def test_thresholds_and_hysteresis(self):
        for free, active, expected in [(14, False, True), (15, False, False),
                                       (20, False, False), (20, True, True),
                                       (25, True, False), (26, True, False)]:
            with self.subTest(free=free, active=active):
                self.assertEqual(c.should_clean(free, 100, active), expected)


class FakeAPI:
    def __init__(self, movies):
        import copy
        self.movies = copy.deepcopy(movies)
        self.events = []
        self.fail = None
        self.verify_false = True
        self.torrents = []
        self.queue = []

    def inventory(self):
        self.events.append('inventory')
        if self.fail == 'inventory':
            raise c.SafetyError('offline')
        import copy
        return copy.deepcopy(self.movies), list(self.torrents), list(self.queue)

    def movie(self, mid):
        self.events.append(('get', mid))
        import copy
        return copy.deepcopy(next(m for m in self.movies if m['id'] == mid))

    def unmonitor(self, movie):
        self.events.append(('put', movie['id']))
        if self.fail == 'put':
            raise c.SafetyError('put failed')
        if self.verify_false:
            next(m for m in self.movies if m['id'] == movie['id'])['monitored'] = False

    def delete_file(self, fid):
        self.events.append(('delete', fid))
        if self.fail == 'delete':
            raise c.SafetyError('delete failed')
        m = next(m for m in self.movies if m['movieFile']['id'] == fid)
        # Only remove private test fixture, never real media.
        Path(m['movieFile']['path']).unlink()
        m['hasFile'] = False
        m['movieFile'] = {}


class FakeFS:
    def __init__(self, root, values):
        self.root = root
        self.mount = root.parent
        self.values = iter(values)
        self.last = (10, 100)

    def verify(self):
        return self.root.stat().st_dev

    def measure(self):
        self.last = next(self.values, self.last)
        return self.last


class ControllerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.root = self.base / 'library'
        self.root.mkdir()
        self.movies = []
        for mid, added in [(1, '2024-01-01T00:00:00Z'), (2, '2020-01-01T00:00:00Z')]:
            folder = self.root / str(mid)
            folder.mkdir()
            path = folder / 'movie.mkv'
            path.write_bytes(b'fixture')
            self.movies.append(dict(id=mid, added=added, monitored=True, hasFile=True,
                                    path=str(folder), movieFile=dict(id=mid+10, movieId=mid,
                                                                  path=str(path))))
        self.api = FakeAPI(self.movies)
        self.fs = FakeFS(self.root, [(10, 100), (10, 100), (25, 100)])
        self.state = self.base / 'state'
        self.state.mkdir()

    def run_controller(self, apply=False, **kwargs):
        return c.run(self.api, self.fs, self.state, apply=apply, **kwargs)

    def test_end_to_end_oldest_unmonitor_delete_and_stop_at_target(self):
        result = self.run_controller(apply=True)
        self.assertEqual(result['deleted'], [2])
        events = self.api.events
        self.assertLess(events.index(('put', 2)), events.index(('get', 2), events.index(('put', 2))))
        self.assertLess(events.index(('get', 2), events.index(('put', 2))), events.index(('delete', 12)))
        self.assertFalse(self.api.movie(2)['monitored'])
        import json
        self.assertFalse(json.loads((self.state / 'state.json').read_text())['active'])

    def test_torrents_all_statuses_and_queue_are_protected(self):
        self.fs = FakeFS(self.root, [(10, 100)] * 30)
        for status in (0, 4, 6):
            self.api.torrents = [dict(id=9, status=status, downloadDir=self.movies[1]['path'],
                                      files=[{'name': 'movie.mkv'}])]
            self.assertEqual(self.run_controller()['eligible'], [1])
        self.api.torrents = []
        self.api.queue = [{'movieId': 2}]
        self.assertEqual(self.run_controller()['eligible'], [1])

    def test_no_progress_stops_and_retains_hysteresis(self):
        self.fs = FakeFS(self.root, [(10, 100)] * 10)
        result = self.run_controller(apply=True)
        self.assertEqual(result['deleted'], [2])
        self.assertEqual(result['stop_reason'], 'no_progress')
        self.fs = FakeFS(self.root, [(20, 100)])
        self.assertEqual(self.run_controller()['eligible'], [1])

    def test_lock_blocks_second_instance(self):
        with c.exclusive_lock(self.state):
            with self.assertRaises(c.SafetyError):
                self.run_controller(apply=True)
        self.assertEqual(self.api.events, [])

    def test_remote_failures_never_continue(self):
        self.fs = FakeFS(self.root, [(10, 100)] * 30)
        for failure in ['inventory', 'put', 'delete']:
            self.api = FakeAPI(self.movies)
            self.api.fail = failure
            with self.subTest(failure=failure), self.assertRaises(c.SafetyError):
                self.run_controller(apply=True)
            self.assertNotIn(('put', 1), self.api.events)
        self.api = FakeAPI(self.movies)
        self.api.verify_false = False
        with self.assertRaises(c.SafetyError):
            self.run_controller(apply=True)
        self.assertNotIn(('delete', 12), self.api.events)

    def test_corrupt_state_and_malformed_torrent_fail_closed(self):
        (self.state / 'state.json').write_text('{"active": "false"}')
        with self.assertRaises(c.SafetyError):
            self.run_controller(apply=True)
        (self.state / 'state.json').unlink()
        self.api.torrents = [{'id': 3, 'downloadDir': '/downloads', 'files': [{'name': '../bad'}]}]
        with self.assertRaises(c.SafetyError):
            self.run_controller(apply=True)
        self.assertFalse(any(isinstance(e, tuple) and e[0] == 'put' for e in self.api.events))

    def test_inventory_refresh_before_each_mutation(self):
        original = self.api.inventory
        calls = []
        def changing():
            calls.append(1)
            if len(calls) > 1:
                self.api.queue = [{'movieId': 2}, {'movieId': 1}]
            return original()
        self.api.inventory = changing
        self.assertEqual(self.run_controller(apply=True)['deleted'], [])
        self.assertNotIn(('put', 2), self.api.events)

    def test_changed_movie_or_new_hardlink_after_unmonitor_aborts(self):
        original = self.api.unmonitor
        def changed(movie):
            original(movie)
            os.link(movie['movieFile']['path'], self.base / 'new-torrent-link')
        self.api.unmonitor = changed
        with self.assertRaises(c.SafetyError):
            self.run_controller(apply=True)
        self.assertNotIn(('delete', 12), self.api.events)

    def test_candidate_bound_and_invalid_bound(self):
        self.assertEqual(self.run_controller(max_candidates=1)['eligible'], [2])
        with self.assertRaises(c.SafetyError):
            self.run_controller(max_candidates=-1)

    def test_hardlink_block_is_reported(self):
        os.link(self.movies[1]['movieFile']['path'], self.base / 'retained-torrent')
        result = self.run_controller()
        self.assertEqual(result['eligible'], [1])
        self.assertIn({'id': 2, 'reason': 'hardlink_no_reclaim'}, result['blocked'])

    def test_torrent_symlink_alias_fails_closed(self):
        (self.base / 'alias').symlink_to(self.root, target_is_directory=True)
        self.api.torrents = [{'downloadDir': str(self.base / 'alias'), 'files': [{'name': '2/movie.mkv'}]}]
        with self.assertRaises(c.SafetyError):
            self.run_controller(apply=True)
        self.assertNotIn(('put', 2), self.api.events)

    def test_default_dry_run_no_mutations_or_state(self):
        result = self.run_controller()
        self.assertEqual(result['eligible'], [2, 1])
        self.assertFalse(any(isinstance(e, tuple) and e[0] in ('put', 'delete') for e in self.api.events))
        self.assertFalse((self.state / 'state.json').exists())


if __name__ == '__main__':
    unittest.main()
