"""Bounded Radarr movie replenishment; dry run unless --apply."""
from datetime import datetime, timedelta, timezone

import fcntl
import json
import os
from pathlib import Path
from contextlib import contextmanager

from controller import API, MediaFS, SafetyError, radarr_key, secret, scoped_path, valid_id, validate_list


@contextmanager
def lock(directory):
    scoped_path(directory, directory)
    fd = os.open(directory / 'replenish.lock', os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise SafetyError('replenisher already running') from None
        yield
    finally:
        os.close(fd)


def inventory(items):
    validate_list(items)
    ids, tids, pending = set(), set(), 0
    for item in items:
        mid, tid = valid_id(item['id']), valid_id(item['tmdbId'])
        if mid in ids or tid in tids:
            raise SafetyError('duplicate movie inventory')
        if any(type(item[k]) is not bool for k in ('monitored', 'hasFile')):
            raise SafetyError('invalid movie inventory flags')
        ids.add(mid)
        tids.add(tid)
        pending += item['monitored'] and not item['hasFile']
    return tids, pending


def load_state(directory):
    path = directory / 'replenish.json'
    if not path.exists() and not path.is_symlink():
        return dict(version=1, last_attempt=None, last_successful_add=None)
    scoped_path(path, directory)
    state = json.loads(path.read_text())
    if not isinstance(state, dict) or type(state.get('version')) is not int or state['version'] != 1:
        raise SafetyError('invalid replenisher state')
    for key in ('last_attempt', 'last_successful_add'):
        if state[key] is not None:
            timestamp(state[key])
    if state['last_successful_add'] is not None and (state['last_attempt'] is None or
            timestamp(state['last_successful_add']) > timestamp(state['last_attempt'])):
        raise SafetyError('inconsistent replenisher state')
    return state


def save_state(directory, state):
    target, temporary = directory / 'replenish.json', directory / 'replenish.tmp'
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'w') as stream:
        json.dump(state, stream)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, target)
    fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)
    if load_state(directory) != state:
        raise SafetyError('state readback failed')


def expire(api, fs, candidates, tag, now, apply):
    def safe(mid):
        movies, torrents, queue = api.inventory()  # validates complete queue/RPC inventories
        inventory(movies)
        validate_list(torrents)
        validate_list(queue)
        busy = {valid_id(item['movieId']) for item in queue}
        current = next((m for m in movies if m['id'] == mid), None)
        if torrents or mid in busy or current is None:
            return None
        if (current['monitored'] and not current['hasFile'] and tag in current['tags']
                and now - timestamp(current['added']) > timedelta(days=7)):
            return current
        return None
    for candidate in candidates:
        current = safe(candidate['id'])
        if current is None:
            continue
        summary = dict(id=current['id'], title=current['title'])
        if not apply:
            return dict(apply=False, reason='would_expire', expired=summary)
        current = safe(candidate['id'])
        if current is None:
            continue
        fs.measure()
        api.unmonitor(current)
        checked = api.movie(current['id'])
        if (valid_id(checked['id']) != current['id'] or valid_id(checked['tmdbId']) != current['tmdbId']
                or checked['monitored'] is not False or checked['hasFile'] is not False):
            raise SafetyError('expiration readback failed')
        return dict(apply=True, reason='expired', expired=summary)
    return None


def run(api, fs, state_dir, apply=False, now=None, profile=7, reserve_gib=20, auto_tag_id=None):
    now = now or datetime.now(timezone.utc)
    with lock(Path(state_dir)):
        try:
            valid_id(profile)
            if auto_tag_id is not None:
                valid_id(auto_tag_id)
            state_dir = Path(state_dir)
            state = load_state(state_dir)
            free, total = fs.measure()
            if state['last_attempt'] and now - timestamp(state['last_attempt']) < timedelta(hours=6):
                return dict(apply=apply, reason='cooldown')
            movies = api.request('GET', api.radarr + '/movie')
            existing, pending = inventory(movies)
            managed = []
            if auto_tag_id is not None:
                for item in movies:
                    if not isinstance(item['tags'], list) or any(type(t) is not int or t <= 0 for t in item['tags']):
                        raise SafetyError('invalid movie tags')
                    if auto_tag_id in item['tags']:
                        managed.append((timestamp(item['added']), item))
                if any(now - added < timedelta(hours=6) for added, _ in managed):
                    return dict(apply=apply, reason='cooldown')
                stale = [m for added, m in managed if m['monitored'] and not m['hasFile']
                         and now - added > timedelta(days=7)]
                expired = expire(api, fs, stale, auto_tag_id, now, apply)
                if expired:
                    return expired  # expiration-only run; next scheduled run can admit
            reason = budget(free, total, pending, reserve_gib)
            result = dict(apply=apply, reason=reason, pending=pending, free_bytes=free,
                          reserved_bytes=(pending + 1) * reserve_gib * 1024 ** 3)
            if reason:
                return result
            choices = select(api.request('GET', api.radarr + '/importlist/movie?includePopular=true&includeTrending=true'), existing, now)
            if not choices:
                return dict(result, reason='no_eligible_movies')
            chosen = choices[0]
            result.update(title=chosen['title'], tmdbId=chosen['tmdbId'])
            if not apply:
                return dict(result, reason='dry_run')
            existing, pending = inventory(api.request('GET', api.radarr + '/movie'))
            free, total = fs.measure()
            reason = budget(free, total, pending, reserve_gib)
            if reason or chosen['tmdbId'] in existing:
                return dict(result, reason=reason or 'became_existing')
            payload = {k: chosen[k] for k in ('title', 'year', 'tmdbId')}
            payload.update(monitored=True, qualityProfileId=profile,
                           rootFolderPath='/downloads/library/movies', minimumAvailability='released',
                           tags=[] if auto_tag_id is None else [auto_tag_id],
                           addOptions=dict(searchForMovie=True, monitor='movieOnly'))
            # Durable intent BEFORE POST blocks immediate retry on ambiguous writes/readbacks.
            state['last_attempt'] = now.isoformat()
            save_state(state_dir, state)
            added = api.request('POST', api.radarr + '/movie', payload)
            mid = valid_id(added['id'])
            checked = api.movie(mid)
            if (valid_id(checked['id']) != mid or valid_id(checked['tmdbId']) != chosen['tmdbId']
                    or checked['monitored'] is not True or type(checked['qualityProfileId']) is not int
                    or checked['qualityProfileId'] != profile or checked['rootFolderPath'] != payload['rootFolderPath']
                    or (auto_tag_id is not None and auto_tag_id not in checked['tags'])):
                raise SafetyError('add readback failed; cooldown retained')
            state['last_successful_add'] = now.isoformat()
            save_state(state_dir, state)
            return dict(result, reason='added', id=mid)
        except (OSError, ValueError, KeyError, TypeError):
            raise SafetyError('invalid state, filesystem or API data; stopped') from None


def timestamp(value):
    if not isinstance(value, str):
        raise SafetyError('invalid release timestamp')
    try:
        value = datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError:
        raise SafetyError('invalid release timestamp') from None
    if value.tzinfo is None:
        raise SafetyError('release timestamp needs timezone')
    return value


def budget(free, total, pending, reserve_gib=20):
    if (any(type(x) is not int for x in (free, total, pending, reserve_gib))
            or total <= 0 or not 0 <= free <= total or pending < 0 or reserve_gib <= 0):
        raise SafetyError('invalid disk budget')
    if pending >= 2:
        return 'pending_limit'
    if free * 100 < total * 15:
        return 'below_15_percent'
    if (pending + 1) * reserve_gib * 1024 ** 3 > free:
        return 'reservation_exceeds_free'
    return None


def select(items, existing, now):
    validate_list(items)
    blocked = set(existing)
    eligible = []
    for item in items:
        tid = valid_id(item['tmdbId'])
        if any(type(item[k]) is not bool for k in ('isExisting', 'isExcluded')):
            raise SafetyError('invalid discovery flags')
        if not isinstance(item['title'], str) or not item['title'].strip():
            raise SafetyError('invalid discovery title')
        if type(item['year']) is not int or not 1800 <= item['year'] <= 9999:
            raise SafetyError('invalid discovery year')
        dates = {k: timestamp(item[k]) if item.get(k) is not None else None
                 for k in ('inCinemas', 'digitalRelease', 'physicalRelease')}
        cinema = dates['inCinemas']
        recent = (now - timedelta(days=730) <= cinema <= now if cinema else
                  item['year'] in (now.year, now.year - 1))
        released = any(d is not None and d <= now for k, d in dates.items() if k != 'inCinemas')
        if item['isExisting'] or item['isExcluded']:
            blocked.add(tid)
        if recent and released:
            eligible.append(item)
    result = []
    for item in eligible:
        if item['tmdbId'] not in blocked:
            result.append(item)
            blocked.add(item['tmdbId'])
    return result


def parser():
    import argparse
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--apply', action='store_true')
    p.add_argument('--state-dir', type=Path, default=Path('/state'))
    p.add_argument('--profile', type=int, default=os.environ.get('QUALITY_PROFILE_ID', '7'))
    p.add_argument('--reserve-gib', type=int, default=os.environ.get('RESERVE_GIB', '20'),
                   help='estimated per-movie reservation; configure matching Radarr release cap')
    p.add_argument('--auto-tag-id', type=int, default=os.environ.get('AUTO_TAG_ID'))
    return p


def main():
    try:
        args = parser().parse_args()
        fs = MediaFS('/downloads', '/downloads/library/movies', os.environ.get('MEDIA_VOLUME_ID', ''),
                     os.environ.get('EXPECTED_MOUNT_SOURCE', ''), os.environ.get('EXPECTED_MOUNT_ROOT', ''))
        fs.verify()
        api = API(os.environ.get('RADARR_URL', 'http://radarr:7878'),
                  os.environ.get('TRANSMISSION_URL', 'http://transmission:9091/transmission/rpc'),
                  radarr_key(), secret('TRANSMISSION_USERNAME'), secret('TRANSMISSION_PASSWORD'), 500)
        print(json.dumps(run(api, fs, args.state_dir, args.apply, profile=args.profile,
                             reserve_gib=args.reserve_gib, auto_tag_id=args.auto_tag_id), sort_keys=True))
        return 0
    except Exception as exc:
        message = str(exc) if isinstance(exc, SafetyError) else 'configuration, filesystem or API failure'
        print(json.dumps(dict(reason='stopped', error=message), sort_keys=True))
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
