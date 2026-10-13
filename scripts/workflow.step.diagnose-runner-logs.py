#!/usr/bin/env python3
"""Report logging errors and connectivity without exposing raw runner logs."""
import os
import json
from pathlib import Path
import re
import subprocess
import time
import urllib.error
import urllib.request

runner_root = Path(os.environ["RUNNER_TEMP"]).resolve().parent.parent
diagnostics = runner_root / "_diag"
print("Runner logging diagnostics:", diagnostics, flush=True)
categories = {
    "live log stream": r"websocket|liveconsole|web console|appendtimelinerecordfeed|logstream",
    "log upload": r"log.*upload|upload.*log|resultsserver|results-receiver",
    "job status updates": r"update steps|timeline records",
    "connection": r"httprequestexception|socketexception|timeoutexception|taskcanceledexception",
}
for pattern, count in [("Runner_*.log", 1), ("Worker_*.log", 3)]:
    for path in sorted(diagnostics.glob(pattern), key=lambda p: p.stat().st_mtime)[-count:]:
        with path.open("rb") as stream:
            stream.seek(max(0, path.stat().st_size - 2_000_000))
            lines = stream.read().decode("utf-8", errors="replace").splitlines()
        print("File:", path.name, flush=True)
        delivered = total = 0
        for line in lines:
            counters = re.search(r"append .* web console lines.*success rate: (\d+)/(\d+)", line, re.I)
            if counters:
                delivered += int(counters[1])
                total += int(counters[2])
        print("Live log batches acknowledged:", delivered, "/", total, flush=True)
        errors = 0
        for line in lines:
            if not re.search(r"error|warn|exception|fail|timeout", line, re.I):
                continue
            matched = [name for name, expression in categories.items() if re.search(expression, line, re.I)]
            if not matched:
                continue
            stamp = re.search(r"\d{4}-\d{2}-\d{2}[T ][\d:.]+Z?", line)
            statuses = re.findall(r"\b(?:401|403|404|408|429|500|502|503|504)\b", line)
            component = re.search(r"\[.*? (?:ERR|WARN|INFO) ([A-Za-z0-9_.]+)\]", line)
            exception_types = re.findall(r"\b(?:WebSocketException|HttpRequestException|SocketException|TimeoutException|TaskCanceledException|OperationCanceledException)\b", line)
            # Only report known categories, timestamps and status codes; never raw messages.
            print(stamp.group() if stamp else "[no timestamp]", ", ".join(matched),
                  component.group(1) if component else "", ",".join(exception_types),
                  "HTTP codes:" + ",".join(statuses) if statuses else "", flush=True)
            errors += 1
        print("Relevant warning/error lines:", errors, flush=True)

for url in ["https://pipelines.actions.githubusercontent.com/_apis/health",
            "https://results-receiver.actions.githubusercontent.com/"]:
    started = time.monotonic()
    try:
        with urllib.request.urlopen(url, timeout=15) as response:
            result = "HTTP " + str(response.status)
    except urllib.error.HTTPError as error:
        result = "HTTP " + str(error.code) + " (server reachable)"
    except (urllib.error.URLError, TimeoutError) as error:
        result = "connection failed: " + type(error).__name__
    print(url, result, f"({time.monotonic() - started:.1f}s)", flush=True)

print("Transmission effective access settings:", flush=True)
inspection = subprocess.run(["docker", "inspect", "transmission"], capture_output=True, text=True)
if inspection.returncode == 0:
    state = json.loads(inspection.stdout)[0]
    hook = "/custom-cont-init.d/20-homelab-rpc.sh"
    print("RPC startup hook mounted:", any(mount["Destination"] == hook for mount in state.get("Mounts", [])), flush=True)
    hook_check = subprocess.run(["docker", "exec", "transmission", "sh", "-c", "stat -c '%a' /custom-cont-init.d/20-homelab-rpc.sh; if [ -x /custom-cont-init.d/20-homelab-rpc.sh ]; then echo executable=yes; else echo executable=no; fi"], capture_output=True, text=True)
    print("RPC startup hook mode/status:", hook_check.stdout.strip(), flush=True)
    logs = subprocess.run(["docker", "logs", "--tail", "200", "transmission"], capture_output=True, text=True)
    for line in (logs.stdout + logs.stderr).splitlines():
        if "[custom-init] 20-homelab-rpc.sh:" in line:
            print("RPC startup hook:", line.strip(), flush=True)
        if "Transmission RPC settings synchronized:" in line:
            print("RPC startup hook reported settings synchronization.", flush=True)
    environment = dict(value.split("=", 1) for value in state["Config"]["Env"] if "=" in value)
    print(json.dumps({key: environment.get(key) for key in ["PUID", "PGID", "WHITELIST", "HOST_WHITELIST"]}), flush=True)
    user = environment.get("PUID", "911") + ":" + environment.get("PGID", "911")
    keys = ["rpc-whitelist", "rpc-whitelist-enabled", "rpc_whitelist", "rpc_whitelist_enabled",
            "rpc-host-whitelist", "rpc-host-whitelist-enabled", "rpc_host_whitelist", "rpc_host_whitelist_enabled",
            "rpc-authentication-required", "rpc_authentication_required"]
    query = "with_entries(select(.key as $key | " + json.dumps(keys) + " | index($key)))"
    settings = subprocess.run(["docker", "exec", "--user", user, "transmission", "jq", query, "/config/settings.json"], capture_output=True, text=True)
    if settings.returncode == 0:
        print(settings.stdout, flush=True)
    else:
        print("Transmission's configured user cannot read/parse settings.json.", flush=True)
    permissions = subprocess.run(["docker", "exec", "transmission", "stat", "-c", "%u %g %a", "/config/settings.json"], capture_output=True, text=True)
    if permissions.returncode == 0:
        print("settings.json owner/group/mode:", permissions.stdout.strip(), flush=True)
    dns = subprocess.run(["docker", "exec", "digger", "getent", "hosts", "transmission"], capture_output=True, text=True)
    print("Transmission DNS from Digger:", dns.stdout.strip(), flush=True)
    print("Current Digger RPC probe:", flush=True)
    subprocess.run(["python3", str(Path(__file__).with_name("workflow.step.verify-digger-rpc.py"))])
