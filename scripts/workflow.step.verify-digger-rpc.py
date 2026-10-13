#!/usr/bin/env python3
"""Check RPC from Digger's network namespace without printing credentials."""
import json
import os
import re
import subprocess
import time

digger = os.environ.get("DIGGER_CONTAINER", "digger")
transmission = os.environ.get("TRANSMISSION_CONTAINER", "transmission")
try:
    states = json.loads(subprocess.check_output(["docker", "inspect", digger, transmission], text=True))
except (subprocess.CalledProcessError, json.JSONDecodeError):
    raise SystemExit("Cannot inspect running Digger and Transmission containers.")
containers = {state["Name"].lstrip("/"): state for state in states}
environment = dict(value.split("=", 1) for value in containers[digger]["Config"]["Env"] if "=" in value)
image = containers[transmission]["Config"]["Image"]
options = {
    "url": environment["TRANSMISSION_RPC_URL"],
    "request": "POST",
    "data": json.dumps({"method": "torrent-get", "arguments": {"fields": ["hashString"]}}),
}
if environment.get("TRANSMISSION_USERNAME"):
    options["user"] = environment["TRANSMISSION_USERNAME"] + ":" + environment.get("TRANSMISSION_PASSWORD", "")
session = None
for attempt in range(10):
    configuration = "silent\nshow-error\nconnect-timeout = 5\nmax-time = 15\ndump-header = /dev/stderr\n"
    configuration += 'header = "Content-Type: application/json"\n'
    if session:
        configuration += "header = " + json.dumps("X-Transmission-Session-Id: " + session) + "\n"
    configuration += "".join(key + " = " + json.dumps(value, ensure_ascii=False) + "\n" for key, value in options.items())
    configuration += 'write-out = "%{stderr}\\nREMOTE-IP:%{remote_ip}\\n%{stdout}\\n%{http_code}"\n'
    # Share Digger's namespace so Transmission sees its actual source IP.
    result = subprocess.run(
        ["docker", "run", "--rm", "--network", "container:" + digger, "--interactive", "--entrypoint", "curl", image, "--noproxy", "*", "--config", "-"],
        input=configuration, text=True, capture_output=True)
    if result.returncode:
        if attempt < 9:
            time.sleep(2)
            continue
        raise SystemExit("Digger RPC verification failed: cannot connect to Transmission.")
    body, _, status = result.stdout.rpartition("\n")
    if status == "409":
        header = re.search(r"^X-Transmission-Session-Id:\s*([^\r\n]+)", result.stderr, re.I | re.M)
        if header:
            session = header.group(1)
            continue
    if status != "200":
        if status in ["401", "403", "421"] and attempt < 9:
            time.sleep(2)
            continue
        address = re.search(r"REMOTE-IP:([0-9a-fA-F:.]+)", result.stderr)
        if address:
            print("RPC responding IP:", address.group(1), flush=True)
        if "Unauthorized IP Address" in body:
            print("Transmission explicitly rejected the client IP.", flush=True)
        hint = {"401": "Check Transmission credentials.", "403": "Check Transmission's IP whitelist and saved configuration permissions.",
                "421": "Check Transmission's hostname whitelist."}.get(status, "Check Transmission RPC availability.")
        raise SystemExit("Digger RPC verification failed: HTTP " + status + ". " + hint)
    try:
        payload = json.loads(body)
    except json.JSONDecodeError:
        raise SystemExit("Transmission RPC returned an invalid response.")
    if payload.get("result") != "success":
        raise SystemExit("Transmission RPC returned an unsuccessful result.")
    print("Digger to Transmission RPC verified: HTTP 200, result success.")
    break
else:
    raise SystemExit("Transmission RPC session negotiation failed.")
