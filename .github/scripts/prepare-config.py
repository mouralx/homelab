#!/usr/bin/env python3
"""Validate and export deployment configuration supplied by GitHub."""

import json
import os
from pathlib import Path
import re


def configuration(compose, variables, environment):
    names = set(re.findall(r'(?<!\$)\$\{([A-Z][A-Z_0-9]*)', compose))
    names.update(['COMPOSE_FILE', 'TRANSMISSION_USERNAME', 'TRANSMISSION_PASSWORD'])
    values = {}
    for name in sorted(names):
        value = environment.get(name) or variables.get(name, '')
        if not isinstance(value, str) or '\n' in value or '\r' in value:
            raise ValueError(f'{name} must be a single-line string')
        values[name] = value
    # Optional Digger overrides use the configured Transmission credentials.
    for suffix in ['USERNAME', 'PASSWORD']:
        name = f'DIGGER_TRANSMISSION_{suffix}'
        values[name] = values[name] or values[f'TRANSMISSION_{suffix}']
    optional = {
        'TRANSMISSION_USERNAME', 'TRANSMISSION_PASSWORD',
        'DIGGER_TRANSMISSION_USERNAME', 'DIGGER_TRANSMISSION_PASSWORD',
    }
    missing = sorted(name for name, value in values.items() if not value and name not in optional)
    if missing:
        raise ValueError('Missing GitHub configuration: ' + ', '.join(missing))
    # Deployment must not silently consume a runner's local .env file either.
    values['COMPOSE_DISABLE_ENV_FILE'] = '1'
    return values


def main():
    root = Path(__file__).resolve().parents[2]
    variables = json.loads(os.environ['HOMELAB_VARIABLES_JSON'])
    compose_file = os.environ.get('COMPOSE_FILE') or variables.get('COMPOSE_FILE')
    if not compose_file:
        raise ValueError('Set COMPOSE_FILE in GitHub variables')
    values = configuration(
        (root / compose_file).read_text(),
        variables,
        os.environ,
    )
    with open(os.environ['GITHUB_ENV'], 'a') as output:
        for name, value in values.items():
            output.write(f'{name}={value}\n')
    print(f'Loaded {len(values)} deployment settings from GitHub variables and secrets.')


if __name__ == '__main__':
    main()
