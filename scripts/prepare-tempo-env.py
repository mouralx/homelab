#!/usr/bin/env python3
"""Persist fresh-install credentials on the runner without logging their values."""
import json
import os
from pathlib import Path
import secrets
import re

KEYS = ('TEMPO_POSTGRES_PASSWORD', 'TEMPO_APP_DB_PASSWORD', 'TEMPO_IDENTITY_DB_PASSWORD', 'TEMPO_KEYCLOAK_ADMIN_PASSWORD')

def prepare(directory, overrides):
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    path = directory / 'credentials.json'
    saved = json.loads(path.read_text()) if path.exists() else {}
    if path.exists() and set(saved) != set(KEYS):
        raise ValueError("Persisted credentials are incomplete; restore the original credentials file.")
    for key in KEYS:
        supplied = overrides.get(key)
        if key in saved and supplied and supplied != saved[key]:
            raise ValueError(f'{key} differs from persisted credentials; use the documented database/password rotation procedure.')
        saved.setdefault(key, supplied or secrets.token_hex(32))
        if not isinstance(saved[key], str) or not re.fullmatch(r'[A-Za-z0-9_-]{16,}', saved[key]):
            raise ValueError(f'Invalid value for {key}.')
    if not path.exists():
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'w') as output:
            json.dump(saved, output)
    return saved

if __name__ == '__main__':
    directory = Path.home() / '.config/tempo'
    values = prepare(directory, os.environ)
    with open(os.environ['GITHUB_ENV'], 'a') as output:
        for key, value in values.items():
            # Escape GitHub command metacharacters before masking.
            masked = value.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
            print(f'::add-mask::{masked}')
            output.write(f'{key}={value}\n')
    print(f'Tempo credentials stored on runner at {directory / "credentials.json"} (mode 0600).')
