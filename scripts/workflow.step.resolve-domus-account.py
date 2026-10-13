#!/usr/bin/env python3
import json, os, subprocess
state = json.loads(subprocess.check_output(['docker', 'inspect', 'postgres']))[0]
environment = dict(value.split('=', 1) for value in state['Config']['Env'] if '=' in value)
query = "SELECT id::text, password_hash IS NOT NULL FROM users WHERE lower(email) = lower(:'email');"
result = subprocess.run(['docker', 'exec', '-i', '-e', 'PGPASSWORD=' + environment['POSTGRES_PASSWORD'], 'postgres', 'psql', '--no-psqlrc', '-U', environment.get('POSTGRES_USER', 'admin'), '-d', 'membership', '-At', '--set=email=' + os.environ['ACCOUNT_EMAIL']], input=query, text=True, capture_output=True)
if result.returncode:
    raise SystemExit("Account lookup failed; check database access on the runner.")
rows = [line.split('|') for line in result.stdout.splitlines() if '|' in line]
local = [row[0] for row in rows if row[1] == 't']
print('Matching identity count:', len(rows))
if len(local) != 1:
    raise SystemExit('Cannot uniquely identify the existing password account.')
print('Original local account ID:', local[0])
