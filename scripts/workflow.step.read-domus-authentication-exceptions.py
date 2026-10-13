#!/usr/bin/env python3
import re, subprocess
for service in ['domus-membership', 'domus-boards']:
    print('Service:', service)
    result = subprocess.run(['docker', 'logs', '--since', '12h', '--tail', '3000', service], capture_output=True, text=True)
    lines = (result.stdout + result.stderr).splitlines()
    remaining = 0
    for line in lines:
        if re.search(r'Exception|fail:|error:|could not|key.*not found|permission denied', line, re.I):
            remaining = 12
        if remaining:
            line = re.sub(r'(?i)(password|secret|token|authorization)\s*[=:]\s*[^;\s]+', r'\1=[redacted]', line)
            line = re.sub(r'[\w.+-]+@[\w.-]+', '[email]', line)
            print(line[:1500])
            remaining -= 1
