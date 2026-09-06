"""Conservative, fail-closed Radarr movie-file cleanup (stdlib only)."""

import os
import stat
import re
from pathlib import Path


import base64
import urllib.request
import urllib.error
import urllib.parse


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise SafetyError('HTTP redirect refused')


class API:
    """Bounded HTTP client; no retries of Radarr writes or uncertain deletes."""
    def __init__(self, radarr, transmission, key, user, password, max_requests=500):
        for url in (radarr, transmission):
            parsed = urllib.parse.urlsplit(url)
            if parsed.scheme not in ('http', 'https') or not parsed.hostname or parsed.username or parsed.password or parsed.query or parsed.fragment:
                raise SafetyError('invalid service URL')
        if not key:
            raise SafetyError('Radarr key required')
        self.radarr = radarr.rstrip('/') + '/api/v3'
        self.transmission = transmission
        self.key = key
        self.auth = 'Basic ' + base64.b64encode((user + ':' + password).encode()).decode() if user or password else None
        self.session = None
        self.requests = 0
        self.max_requests = max_requests
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def request(self, method, url, payload=None, rpc=False):
        for attempt in range(2 if rpc else 1):
            if self.requests >= self.max_requests:
                raise SafetyError('request budget exhausted')
            self.requests += 1
            headers = {'Accept': 'application/json', 'Content-Type': 'application/json'}
            if rpc:
                if self.session:
                    headers['X-Transmission-Session-Id'] = self.session
                if self.auth:
                    headers['Authorization'] = self.auth
            else:
                headers['X-Api-Key'] = self.key
            data = None if payload is None else json.dumps(payload).encode()
            try:
                with self.opener.open(urllib.request.Request(url, data=data, headers=headers, method=method), timeout=15) as response:
                    body = response.read(32 * 1024 * 1024 + 1)
                    if len(body) > 32 * 1024 * 1024:
                        raise SafetyError('response size limit exceeded')
                    return json.loads(body) if body else None
            except urllib.error.HTTPError as exc:
                if rpc and exc.code == 409 and attempt == 0:
                    self.session = exc.headers.get('X-Transmission-Session-Id')
                    exc.close()
                    if not self.session:
                        raise SafetyError('Transmission 409 missing session ID') from None
                    continue
                status = exc.code
                exc.close()
                raise SafetyError('HTTP request failed (status %s)' % status) from None
            except (OSError, ValueError) as exc:
                raise SafetyError('HTTP transport or JSON failed') from None
        raise SafetyError('Transmission handshake failed')

    def movie(self, mid):
        return self.request('GET', self.radarr + '/movie/' + str(valid_id(mid)))

    def unmonitor(self, movie):
        payload = dict(movie)
        payload['monitored'] = False
        self.request('PUT', self.radarr + '/movie/' + str(valid_id(movie['id'])) + '?moveFiles=false', payload)

    def delete_file(self, fid):
        self.request('DELETE', self.radarr + '/moviefile/' + str(valid_id(fid)))

    def inventory(self):
        movies = self.request('GET', self.radarr + '/movie')
        validate_list(movies)
        ids = [valid_id(m['id']) for m in movies]
        if len(set(ids)) != len(ids):
            raise SafetyError('duplicate movie IDs')
        files = {}
        seen = set()
        for start in range(0, len(ids), 50):
            batch = ids[start:start+50]
            query = urllib.parse.urlencode([('movieId', mid) for mid in batch])
            records = self.request('GET', self.radarr + '/moviefile?' + query)
            validate_list(records)
            for file in records:
                mid, fid = valid_id(file['movieId']), valid_id(file['id'])
                if mid not in batch or mid in files or fid in seen:
                    raise SafetyError('ambiguous movie file inventory')
                files[mid] = file
                seen.add(fid)
        for movie in movies:
            if type(movie['hasFile']) is not bool or movie['hasFile'] != (movie['id'] in files):
                raise SafetyError('movie/file inventory inconsistent')
            movie['movieFile'] = files.get(movie['id'], {})
        page = self.request('GET', self.radarr + '/queue?page=1&pageSize=10000&includeUnknownMovieItems=true')
        queue = page['records']
        validate_list(queue)
        if type(page['totalRecords']) is not int or page['totalRecords'] != len(queue):
            raise SafetyError('queue inventory incomplete')
        reply = self.request('POST', self.transmission,
                             {'method': 'torrent-get', 'arguments': {'fields': ['id', 'status', 'downloadDir', 'files']}}, rpc=True)
        if reply['result'] != 'success':
            raise SafetyError('Transmission RPC failed')
        torrents = reply['arguments']['torrents']
        validate_list(torrents)
        torrent_paths(torrents)
        return movies, torrents, queue


def valid_id(value):
    if type(value) is not int or value <= 0:
        raise SafetyError('invalid API ID')
    return value


def validate_list(value):
    if not isinstance(value, list) or len(value) > 10000 or not all(isinstance(x, dict) for x in value):
        raise SafetyError('invalid or oversized API inventory')


class SafetyError(RuntimeError):
    pass


def scoped_path(value, root):
    path = Path(value)
    if not path.is_absolute() or '..' in path.parts or not path.is_relative_to(root):
        raise SafetyError('path outside allowed scope')
    current = Path('/')
    try:
        for part in path.parts[1:]:
            current /= part
            if stat.S_ISLNK(current.lstat().st_mode):
                raise SafetyError('symlink in path')
    except OSError as exc:
        raise SafetyError('path unavailable') from exc
    return path


def safe_file(value, root, device):
    path = scoped_path(value, root)
    s = path.stat()
    if not stat.S_ISREG(s.st_mode) or s.st_dev != device:
        raise SafetyError('not a regular file on media filesystem')
    return s.st_nlink == 1


class MediaFS:
    def __init__(self, mount, root, marker, expected_source='', expected_root=''):
        self.mount, self.root, self.marker = Path(mount), Path(root), marker
        self.expected_source, self.expected_root = expected_source, expected_root
        self.identity = None

    def verify(self):
        scoped_path(self.mount, self.mount)
        # mountinfo, unlike ismount(), recognizes same-device bind mounts.
        def unescape(s):
            return re.sub(r'\\([0-7]{3})', lambda m: chr(int(m[1], 8)), s)
        entries = [line.split() for line in Path('/proc/self/mountinfo').read_text().splitlines()]
        mounts = [e for e in entries if unescape(e[4]) == str(self.mount)]
        if len(mounts) != 1 or self.mount == Path('/'):
            raise SafetyError('media path is not a distinct mountpoint')
        entry = mounts[0]
        if self.expected_source or self.expected_root:
            if (not self.expected_source or not self.expected_root
                    or unescape(entry[entry.index('-') + 2]) != self.expected_source
                    or unescape(entry[3]) != self.expected_root):
                raise SafetyError('media mount source/root mismatch')
        elif not self.marker:
            raise SafetyError('require source/root pins or volume marker')
        if self.marker:
            marker = scoped_path(self.mount / '.cleanup-volume-id', self.mount)
            if marker.read_text().strip() != self.marker:
                raise SafetyError('media identity marker mismatch')
        root = scoped_path(self.root, self.mount)
        device = self.mount.stat().st_dev
        if not root.is_dir() or root.stat().st_dev != device:
            raise SafetyError('movie root is not on expected filesystem')
        identity = (entry[0], device, root.stat().st_ino)
        if self.identity is not None and self.identity != identity:
            raise SafetyError('mount changed during run')
        self.identity = identity
        return device

    def measure(self):
        self.verify()
        s = os.statvfs(self.mount)
        if s.f_blocks <= 0 or s.f_frsize <= 0:
            raise SafetyError('invalid filesystem capacity')
        return s.f_bavail * s.f_frsize, s.f_blocks * s.f_frsize


def should_clean(free, total, active):
    return free * 100 < total * (25 if active else 15)


import json
from datetime import datetime


def save_state(directory, active):
    target = directory / 'state.json'
    temporary = directory / 'state.tmp'
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'w') as f:
        json.dump({'version': 1, 'active': active}, f)
        f.flush()
        os.fsync(f.fileno())
    os.replace(temporary, target)
    fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


import fcntl
from contextlib import contextmanager


@contextmanager
def exclusive_lock(directory):
    scoped_path(directory, directory)
    fd = os.open(Path(directory) / 'controller.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise SafetyError('another cleanup holds the lock') from exc
        yield
    finally:
        os.close(fd)


def torrent_paths(torrents):
    protected = []
    if not isinstance(torrents, list):
        raise SafetyError('invalid torrent inventory')
    for torrent in torrents:
        directory = Path(torrent['downloadDir'])
        if not directory.is_absolute() or '..' in directory.parts or not torrent['files']:
            raise SafetyError('unsafe or incomplete torrent paths')
        for file in torrent['files']:
            name = Path(file['name'])
            if name.is_absolute() or '..' in name.parts or not name.parts:
                raise SafetyError('unsafe torrent filename')
            # Protect entire top-level torrent tree, including partial files.
            protected.append(directory / name.parts[0])
    return protected


def candidates(movies, torrents, queue, fs, blocked=None):
    device = fs.verify()
    protected = torrent_paths(torrents)
    for p in protected:
        if not p.is_relative_to(fs.mount):
            raise SafetyError('torrent path outside shared media mount')
        existing = p
        while not existing.exists() and not existing.is_symlink():
            existing = existing.parent
        scoped_path(existing, fs.mount)
    blocked = [] if blocked is None else blocked
    busy = set()
    for item in queue:
        if not item.get('movieId'):
            raise SafetyError('unmapped queue item: cannot prove safe')
        busy.add(item['movieId'])
    eligible = []
    for movie in movies:
        if not movie['hasFile']:
            continue
        folder = scoped_path(movie['path'], fs.root)
        file = movie['movieFile']
        path = scoped_path(file['path'], folder)
        if movie['id'] in busy or any(path.is_relative_to(p) or p.is_relative_to(folder) for p in protected):
            blocked.append({'id': movie['id'], 'reason': 'torrent_or_queue_protected'})
            continue
        if not safe_file(path, fs.root, device):
            blocked.append({'id': movie['id'], 'reason': 'hardlink_no_reclaim'})
            continue
        added = datetime.fromisoformat(movie['added'].replace('Z', '+00:00'))
        if added.tzinfo is None:
            raise SafetyError('added timestamp needs timezone')
        eligible.append((added, movie['id'], movie))
    return [m for _, _, m in sorted(eligible, key=lambda v: (v[0], v[1]))]


def run(api, fs, state_dir, apply=False, max_candidates=20):
    with exclusive_lock(Path(state_dir)):
        try:
            return _run(api, fs, Path(state_dir), apply, max_candidates)
        except (OSError, ValueError, KeyError, TypeError) as exc:
            raise SafetyError('invalid state, filesystem or API data; stopped') from exc


def _run(api, fs, state_dir, apply, max_candidates):
    if type(max_candidates) is not int or not 1 <= max_candidates <= 100:
        raise SafetyError('max candidates must be 1..100')
    state_file = state_dir / 'state.json'
    active = False
    if state_file.exists() or state_file.is_symlink():
        scoped_path(state_file, state_dir)
        state = json.loads(state_file.read_text())
        if state.get('version') != 1 or type(state.get('active')) is not bool:
            raise SafetyError('invalid persistent state')
        active = state['active']
    free, total = fs.measure()
    active = should_clean(free, total, active)
    result = {'active': active, 'free_bytes': free, 'total_bytes': total,
              'eligible': [], 'blocked': [], 'deleted': [], 'apply': apply}
    if not active:
        if apply:
            save_state(state_dir, False)
        return result
    movies, torrents, queue = api.inventory()
    selected = candidates(movies, torrents, queue, fs, result['blocked'])[:max_candidates]
    result['eligible'] = [m['id'] for m in selected]
    if not apply:
        return result
    save_state(state_dir, True)
    for candidate in selected:
        free, total = fs.measure()
        if not should_clean(free, total, True):
            break
        mid = candidate['id']
        fresh = candidates(*api.inventory(), fs)
        if mid not in [m['id'] for m in fresh]:
            continue
        movie = api.movie(mid)
        def check_same(current):
            if (current['id'] != mid or current['path'] != candidate['path']
                    or current['movieFile']['id'] != candidate['movieFile']['id']
                    or current['movieFile']['path'] != candidate['movieFile']['path']):
                raise SafetyError('movie changed during cleanup')
        check_same(movie)
        path = Path(movie['movieFile']['path'])
        original_stat = path.stat()
        api.unmonitor(movie)
        checked = api.movie(mid)
        check_same(checked)
        if checked['monitored'] is not False:
            raise SafetyError('unmonitor readback failed')
        fresh = candidates(*api.inventory(), fs)
        if mid not in [m['id'] for m in fresh]:
            raise SafetyError('movie became protected after unmonitor')
        if not safe_file(path, fs.root, fs.verify()):
            raise SafetyError('new hardlink after unmonitor')
        current_stat = path.stat()
        if (current_stat.st_dev, current_stat.st_ino, current_stat.st_size, current_stat.st_mtime_ns) != (original_stat.st_dev, original_stat.st_ino, original_stat.st_size, original_stat.st_mtime_ns):
            raise SafetyError('file changed during cleanup')
        api.delete_file(candidate['movieFile']['id'])
        after = api.movie(mid)
        if after['monitored'] is not False or after['hasFile'] is not False:
            raise SafetyError('post-delete movie readback failed')
        result['deleted'].append(mid)
        before = free
        free, total = fs.measure()
        if free <= before:
            result['stop_reason'] = 'no_progress'
            break
        if not should_clean(free, total, True):
            break
    active = should_clean(free, total, True)
    save_state(state_dir, active)
    result.update(active=active, free_bytes=free, total_bytes=total)
    return result


def secret(name):
    if name in os.environ:
        return os.environ[name].strip()
    if os.environ.get(name + '_FILE'):
        return Path(os.environ[name + '_FILE']).read_text().strip()
    return ''


def radarr_key():
    key = secret('RADARR_API_KEY')
    if not key and os.environ.get('RADARR_CONFIG_XML'):
        import xml.etree.ElementTree as ET
        key = ET.parse(os.environ['RADARR_CONFIG_XML']).getroot().findtext('ApiKey', '').strip()
    return key


def parser():
    import argparse
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--apply', action='store_true', help='enable Radarr unmonitor and movie-file DELETE; otherwise dry run')
    p.add_argument('--state-dir', type=Path, default=Path('/state'))
    p.add_argument('--max-candidates', type=int, default=20)
    p.add_argument('--max-requests', type=int, default=500)
    return p


def main():
    import sys
    args = parser().parse_args()
    try:
        if not 1 <= args.max_requests <= 10000:
            raise SafetyError('max requests must be 1..10000')
        fs = MediaFS('/downloads', '/downloads/library/movies', os.environ.get('MEDIA_VOLUME_ID', ''),
                     os.environ.get('EXPECTED_MOUNT_SOURCE', ''), os.environ.get('EXPECTED_MOUNT_ROOT', ''))
        fs.verify()
        api = API(os.environ.get('RADARR_URL', 'http://radarr:7878'),
                  os.environ.get('TRANSMISSION_URL', 'http://transmission:9091/transmission/rpc'),
                  radarr_key(), secret('TRANSMISSION_USERNAME'), secret('TRANSMISSION_PASSWORD'),
                  args.max_requests)
        print(json.dumps(run(api, fs, args.state_dir, args.apply, args.max_candidates), sort_keys=True))
        return 0
    except Exception as exc:
        message = str(exc) if isinstance(exc, SafetyError) else 'configuration, filesystem or API failure'
        print('cleanup stopped: ' + message, file=sys.stderr)
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
