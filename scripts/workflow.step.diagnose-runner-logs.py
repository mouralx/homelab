#!/usr/bin/env python3
"""Report logging errors and connectivity without exposing raw runner logs."""
import os
from pathlib import Path
import re
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
