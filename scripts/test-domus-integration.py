#!/usr/bin/env python3
"""Exercise Domus homelab images with an isolated shared PostgreSQL 15 fixture.

By default pulls the configured GHCR images. For local source validation only,
set DOMUS_TEST_LOCAL_IMAGES=true to use already-built domus-* image IDs.
"""
import base64
import hashlib
import hmac
from http.cookiejar import CookieJar
import io
import json
import os
from pathlib import Path
import secrets
import socket
import struct
import subprocess
import tempfile
import time
from urllib.request import build_opener, HTTPCookieProcessor, Request
from urllib.error import HTTPError
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DOCKER = ['docker'] + (['--context', os.environ['DOCKER_CONTEXT']] if os.environ.get('DOCKER_CONTEXT') else [])
PROJECT = 'domus-homelab-test-' + uuid.uuid4().hex[:8]
with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0))
    PORT = listener.getsockname()[1]
ORIGIN = f'http://localhost:{PORT}'
ENV = dict(os.environ, TIME_ZONE='Europe/Lisbon', POSTGRES_USER='postgres', POSTGRES_PASSWORD=secrets.token_hex(24),
           DOMUS_MEMBERSHIP_DB_PASSWORD=secrets.token_hex(24), DOMUS_BOARDS_DB_PASSWORD=secrets.token_hex(24),
           DOMUS_AUTOMATIONS_DB_PASSWORD=secrets.token_hex(24), DOMUS_RABBITMQ_PASSWORD=secrets.token_hex(24),
           DOMUS_PUBLIC_URL=ORIGIN, DOMUS_ALLOWED_HOSTS='localhost;127.0.0.1', DOMUS_PLATFORM_ADMIN_EMAILS='',
           DOMUS_POSTGRES_HOST='postgres', DOMUS_POSTGRES_PORT='5432', DOMUS_MEMBERSHIP_DATABASE='membership',
           DOMUS_BOARDS_DATABASE='boards', DOMUS_AUTOMATIONS_DATABASE='automations')


def docker(*args, input=None):
    result = subprocess.run([*DOCKER, *args], cwd=ROOT, env=ENV, input=input, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        # Docker errors do not include the resolved Compose secrets.
        raise RuntimeError(result.stderr.decode()[-3000:])
    return result.stdout


class Client:
    def __init__(self):
        self.opener = build_opener(HTTPCookieProcessor(CookieJar()))

    def call(self, path, payload=None, raw=None, workspace=None, expected=200):
        headers = {'Origin': ORIGIN, 'X-Kanbada-Request': '1'}
        if workspace:
            headers['X-Domus-Workspace'] = workspace
        data = raw
        if payload is not None:
            data = json.dumps(payload).encode()
            headers['Content-Type'] = 'application/json'
        if raw is not None:
            headers['Content-Type'] = 'application/octet-stream'
        try:
            response = self.opener.open(Request(ORIGIN + path, data=data, headers=headers), timeout=20)
        except HTTPError as error:
            response = error
        content = response.read()
        if response.status != expected:
            raise AssertionError(f'{path}: expected {expected}, got {response.status}: {content[:200]!r}')
        if 'application/json' in response.headers.get('Content-Type', ''):
            return json.loads(content)
        return content


def totp(secret):
    key = base64.b32decode(secret + '=' * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack('>Q', int(time.time()) // 30), hashlib.sha1).digest()
    offset = digest[-1] & 15
    return f'{(struct.unpack(">I", digest[offset:offset+4])[0] & 0x7fffffff) % 1000000:06d}'


with tempfile.TemporaryDirectory() as directory:
    compose_file = Path(directory) / 'compose.json'
    config = json.loads(docker('compose', '-f', 'services/compose.yaml', 'config', '--format', 'json'))
    config['name'] = PROJECT
    config['services'] = {name: service for name, service in config['services'].items() if name.startswith('domus-') or name == 'postgres'}
    config['services']['postgres']['image'] = 'postgres:15-bookworm'
    config['services']['postgres']['volumes'] = [{'type': 'volume', 'source': 'postgres-test', 'target': '/var/lib/postgresql/data'}]
    config['services']['postgres'].pop('ports', None)
    config['volumes']['postgres-test'] = {}
    for volume in config['volumes'].values():
        volume.pop('name', None)
    for network in config['networks'].values():
        network.pop('name', None)
        network.pop('ipam', None)
    local = {'domus-portal': 'domus-portal', 'domus-membership': 'domus-membership', 'domus-boards': 'domus-work-api',
             'domus-automations': 'domus-automation-api', 'domus-boards-worker': 'domus-jira-worker',
             'domus-automation-worker': 'domus-automation-worker'}
    for name, service in config['services'].items():
        service.pop('container_name', None)
        service.pop('hostname', None)
        service['restart'] = 'no'
        for network_name, network in list(service['networks'].items()):
            service['networks'][network_name] = network or {}
            service['networks'][network_name].pop('ipv4_address', None)
        if name in local and os.environ.get('DOMUS_TEST_LOCAL_IMAGES') == 'true':
            service['image'] = docker('image', 'inspect', '--format', '{{.Id}}', local[name]).decode().strip()
            service['pull_policy'] = 'never'
    config['services']['domus-portal']['ports'] = [{'target': 80, 'published': str(PORT), 'host_ip': '127.0.0.1', 'protocol': 'tcp'}]
    compose_file.write_text(json.dumps(config))
    compose_file.chmod(0o600)

    def compose(*args):
        return docker('compose', '-f', str(compose_file), '-p', PROJECT, *args)

    def sql(database, query):
        return compose('exec', '-T', 'postgres', 'psql', '-U', 'postgres', '-d', database, '-tAc', query).decode().strip()

    try:
        print('Starting isolated homelab Domus configuration…', flush=True)
        compose('up', '-d', '--wait', '--wait-timeout', '240')
        assert sql('postgres', "SELECT count(*) FROM pg_database WHERE datname IN ('membership','boards','automations')") == '3'
        assert sql('boards', 'SELECT count(*) FROM users WHERE password_hash IS NOT NULL') == '0'
        # Idempotent initialization preserves application data and unrelated databases.
        sql('postgres', 'CREATE DATABASE unrelated_fixture')
        compose('run', '--rm', 'domus-databases')
        assert sql('postgres', "SELECT count(*) FROM pg_database WHERE datname='unrelated_fixture'") == '1'
        client = Client()
        assert b'domus' in client.call('/')
        password = 'Synthetic-domus-password-2026'
        registered = client.call('/api/auth/register', {'name': 'Homelab Owner', 'email': 'homelab-test@example.test', 'password': password})
        client.call('/automations-api/api/status', workspace='studio', expected=403)
        photo = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII='
        client.call('/api/auth/avatar', {'photo': photo}, expected=204)
        setup = client.call('/api/auth/two-factor/setup', {'password': password})
        client.call('/api/auth/two-factor/confirm', {'password': password, 'code': totp(setup['secret'])})
        workspace = client.call('/api/workspaces/studio')['workspace']['id']
        status = client.call('/automations-api/api/status', workspace=workspace)
        manifest = {
            'schemaVersion': 1, 'id': 'homelab-fixture', 'name': 'Homelab fixture', 'version': '1.0.0',
            'description': 'Synthetic homelab test', 'platform': {'os': status['platform'], 'arch': status['arch']},
        }
        # Copy the contract shape from a synthetic fixture kept alongside this test.
        manifest = json.loads((ROOT / 'scripts/fixtures/domus.automation.json').read_text()) | manifest
        archive = io.BytesIO()
        with zipfile.ZipFile(archive, 'w') as package:
            package.writestr('automation.json', json.dumps(manifest))
            entry = zipfile.ZipInfo('fixture.sh')
            entry.external_attr = 0o100755 << 16
            package.writestr(entry, '#!/bin/sh\nset -eu\nprintf "HOMELAB RESULT" > result.txt\n')
        installed = client.call('/automations-api/api/import', raw=archive.getvalue(), workspace=workspace, expected=201)
        job = client.call('/automations-api/api/runs', {'packageKey': installed['key'], 'values': {}}, workspace=workspace, expected=201)
        assert job['initiatedBy'] == registered['id']
        for attempt in range(150):
            detail = client.call('/automations-api/api/runs/' + job['id'], workspace=workspace)
            if detail['status'] == 'completed':
                break
            if detail['status'] == 'failed':
                raise AssertionError('Synthetic automation failed')
            time.sleep(.2)
        else:
            raise AssertionError('Automation execution timed out')
        data = client.call('/automations-api/api/runs/' + job['id'] + '/results.zip?workspace=' + workspace)
        assert data[:2] == b'PK'
        assert sql('membership', 'SELECT count(*) FROM users WHERE password_hash IS NOT NULL') == '1'
        assert sql('boards', 'SELECT count(*) FROM users WHERE password_hash IS NOT NULL') == '0'
        compose('run', '--rm', 'domus-databases')
        assert sql('membership', 'SELECT count(*) FROM users') == '1'
        assert sql('automations', 'SELECT count(*) FROM executions') == '1'
        assert sql('postgres', "SELECT has_database_privilege('domus_boards','membership','CONNECT')") == 'f'
        assert sql('postgres', "SELECT has_database_privilege('domus_automations','boards','CONNECT')") == 'f'
        client.call('/api/auth/logout', {}, expected=204)
        client.call('/automations-api/api/status', workspace=workspace, expected=401)
        # Database-name collision must fail without changing unrelated databases.
        ENV['DOMUS_MEMBERSHIP_DATABASE'] = 'unrelated_fixture'
        try:
            compose('run', '--rm', '-e', 'DOMUS_MEMBERSHIP_DATABASE=unrelated_fixture', 'domus-databases')
        except RuntimeError:
            pass
        else:
            raise AssertionError('Initializer accepted an unrelated database')
        assert sql('postgres', "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname='unrelated_fixture'") == 'postgres'
        print('Homelab Domus integration passed: PostgreSQL 15, safe database setup, registration, 2FA, shared APIs, worker execution, ZIP and logout.', flush=True)
    finally:
        compose('down', '--volumes', '--remove-orphans')
