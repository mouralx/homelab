#!/usr/bin/env python3
"""Exercise the actual workflow steps without changing Docker containers."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SERVICES = sorted(["openclaw", "npm", "n8n", "transmission", "digger", "jellyfin", "portainer", "homeassistant"])
workflow = json.loads(subprocess.check_output(
    ["ruby", "-ryaml", "-rjson", "-e", "puts JSON.generate(YAML.load_file(ARGV[0]))",
     str(ROOT / ".github/workflows/deploy.yaml")], text=True))
job = workflow["jobs"]["deploy"]
assert job["env"]["SELECTED_SERVICES"] == "all"
assert job["runs-on"] == ["self-hosted", "beelink"]
if "concurrency" in job:
    assert job["concurrency"]["cancel-in-progress"] is False
steps = job["steps"]
assert steps[0]["uses"].startswith("actions/checkout@")
assert steps[-1]["if"] == "${{ always() }}"
for step in steps[1:]:
    assert "\n" not in step["run"]
    command, script = step["run"].split()
    assert command == "bash" and (ROOT / script).is_file()
    subprocess.run(["bash", "-n", str(ROOT / script)], check=True)

with tempfile.TemporaryDirectory(prefix="homelab-workflow-") as directory:
    fixture = Path(directory)
    shutil.copytree(ROOT / "services", fixture / "services")
    shutil.copytree(ROOT / "scripts", fixture / "scripts")
    (fixture / "bin").mkdir()
    (fixture / "runner").mkdir()
    source_config = fixture / "source-config"
    source_config.mkdir()
    original_config = Path(os.environ.get("DOCKER_CONFIG", str(Path.home() / ".docker")))
    if (original_config / "config.json").is_file():
        original = json.loads((original_config / "config.json").read_text())
        # Only copy plugin discovery settings, never registry credentials.
        (source_config / "config.json").write_text(json.dumps({
            "cliPluginsExtraDirs": original.get("cliPluginsExtraDirs", [])
        }))
    if (original_config / "cli-plugins").is_dir():
        (source_config / "cli-plugins").symlink_to(original_config / "cli-plugins")
    wrapper = fixture / "bin/docker"
    wrapper.write_text(r"""#!/usr/bin/env python3
import json, os, subprocess, sys
args = sys.argv[1:]
if args[0] == '--config':
    assert args[2:] == ['login', 'ghcr.io', '--username', 'fixture-user', '--password-stdin']
    assert sys.stdin.read() == 'fixture-registry-token'
if args[:4] == ['compose', '--env-file', '.env', 'config']:
    sys.exit(subprocess.call([os.environ['REAL_DOCKER'], *args]))
if args[0] == 'inspect':
    print(json.dumps([
        {'Name': '/digger', 'Config': {'Env': ['TRANSMISSION_RPC_URL=http://transmission:9091/transmission/rpc', 'TRANSMISSION_USERNAME=fixture-user', 'TRANSMISSION_PASSWORD=fixture-password']}},
        {'Name': '/transmission', 'Config': {'Image': 'fixture-transmission'}}
    ]))
    sys.exit(0)
if args[0] == 'ps':
    if os.environ['FAIL_COMMAND'] == 'inventory':
        sys.exit(1)
    owner = next((a.split('=', 2)[2] for a in args if a.startswith('label=com.docker.compose.project=')), '')
    service = next((a.split('=', 2)[2] for a in args if a.startswith('label=com.docker.compose.service=')), '')
    installed = json.loads(os.environ['INSTALLED']).get(owner, [])
    for name in installed:
        if '--format' in args:
            print(json.dumps({'id': owner + '-' + name, 'service': name}))
        elif service == name:
            print(owner + '-' + name)
    sys.exit(0)
with open(os.environ['DOCKER_LOG'], 'a') as log:
    log.write(json.dumps(args) + '\n')
command = args[3] if args[0] == 'compose' else ('login' if args[0] == '--config' else args[0])
if command == os.environ['FAIL_COMMAND']:
    sys.exit(1)
if args[0] == 'run' and args[-2:] == ['--config', '-']:
    sys.stdin.read()
    print('{"result":"success","arguments":{"torrents":[]}}')
    print('403' if os.environ['FAIL_COMMAND'] == 'rpc' else '200', end='')
""")
    wrapper.chmod(0o755)
    base = os.environ.copy()
    for key in ["TIME_ZONE", "DIGGER_TAG", "DOCKER_CONFIG", "HOMELAB_REGISTRY_CONFIG"]:
        base.pop(key, None)
    base.update(job["env"])
    base.update({
        "PATH": str(fixture / "bin") + os.pathsep + base["PATH"],
        "REAL_DOCKER": shutil.which("docker"),
        "RUNNER_TEMP": str(fixture / "runner"),
        "GITHUB_ENV": str(fixture / "github-env"),
        "GITHUB_STEP_SUMMARY": str(fixture / "summary"),
        "DOCKER_LOG": str(fixture / "docker-log"),
        "DOCKER_CONFIG": str(fixture / "source-config"),
        "ENVIRONMENT_VARS": json.dumps({"TIME_ZONE": "Europe/Lisbon", "DIGGER_TAG": "stable"}),
        "ENVIRONMENT_SECRETS": json.dumps({"OPENCLAW_GATEWAY_TOKEN": "synthetic-token", "GITHUB_TOKEN": "excluded"}),
        "SELECTED_SERVICES": "all",
        "INSTALLED": json.dumps({"media": ["transmission", "unmanaged"], "agents": ["portainer_agent"]}),
    })
    secret_values = ["fixture$UNDEFINED_SECRET_SUFFIX", "a'b", "first\nsecond", "a # comment",
                     "a\\b", "ending\\", "a\\'b", 'a"b', "${MISSING}", "$$double", r"\$BACKSLASH"]
    for value in secret_values:
        env = base.copy()
        env["ENVIRONMENT_VARS"] = json.dumps({"TIME_ZONE": "Europe/Lisbon", "OPENCLAW_GATEWAY_TOKEN": "overridden", "TRANSMISSION_USERNAME": "fixture-user"})
        env["ENVIRONMENT_SECRETS"] = json.dumps({"OPENCLAW_GATEWAY_TOKEN": value, "GITHUB_TOKEN": "excluded", "TRANSMISSION_PASSWORD": "fixture-password"})
        subprocess.run(steps[1]["run"].split(), cwd=fixture, env=env, check=True)
        config = json.loads(subprocess.check_output(
            [env["REAL_DOCKER"], "compose", "--env-file", ".env", "config", "--format", "json"],
            cwd=fixture, env=env, text=True))
        # Compose escapes literal dollars when serializing resolved config.
        assert config["services"]["openclaw"]["environment"]["OPENCLAW_GATEWAY_TOKEN"] == value.replace("$", "$$")
        digger = config["services"]["digger"]
        transmission = config["services"]["transmission"]
        assert next(v["source"] for v in digger["volumes"] if v["target"] == "/movies") == next(v["source"] for v in transmission["volumes"] if v["target"] == "/movies")
        assert digger["environment"]["TRANSMISSION_USERNAME"] == "fixture-user"
        assert digger["environment"]["TRANSMISSION_PASSWORD"] == "fixture-password"
        assert "GITHUB_TOKEN" not in (fixture / ".env").read_text()
    print(f"All {len(secret_values)} special-character secret values survived real Compose parsing.")
    checks = 0
    cases = [
        (action, dry, update, down, "")
        for action in ["install", "uninstall"]
        for dry in [False, True]
        for update in [False, True]
        for down in [False, True]
    ] + [("install", False, True, True, failure) for failure in ["login", "manifest", "inventory", "pull", "build", "stop", "run", "up", "rpc"]]
    for action, dry, update, down, failure in cases:
        env = base.copy()
        env.update({"SERVICE_ACTION": action, "DRY_RUN": str(dry).lower(),
                    "UPDATE_IMAGES": str(update).lower(), "BRING_DOWN_FIRST": str(down).lower(),
                    "FAIL_COMMAND": failure})
        for path in [fixture / "github-env", fixture / "docker-log", fixture / "summary"]:
            path.write_text("")
        failed = False
        for step in steps[1:-1]:
            if step.get("if") and (action != "install" or ("!inputs.dry_run" in step["if"] and dry)):
                continue
            if "GHCR_TOKEN" in step.get("env", {}):
                assert step["env"]["GHCR_TOKEN"] == "${{ secrets.GH_PAT || github.token }}"
                env.update(GHCR_TOKEN="fixture-registry-token", GHCR_USERNAME="fixture-user")
            result = subprocess.run(step["run"].split(), cwd=fixture, env=env, capture_output=True, text=True)
            if result.returncode:
                failed = True
                assert failure, result.stdout + result.stderr
                break
            if "create-environment-file" in step["run"]:
                assert "GITHUB_TOKEN" not in (fixture / ".env").read_text()
                assert (fixture / ".env").stat().st_mode & 0o077 == 0
            for line in (fixture / "github-env").read_text().splitlines():
                key, value = line.split("=", 1)
                env[key] = value
        # GitHub applies GITHUB_ENV even when the previous step fails.
        for line in (fixture / "github-env").read_text().splitlines():
            key, value = line.split("=", 1)
            env[key] = value
        subprocess.run(steps[-1]["run"].split(), cwd=fixture, env=env, check=True)
        assert failed == bool(failure), (action, failure)
        assert not (fixture / ".env").exists()
        assert not list((fixture / "runner").iterdir())
        calls = [json.loads(line) for line in (fixture / "docker-log").read_text().splitlines()]
        rpc_calls = [call for call in calls if call[0] == "run" and call[-2:] == ["--config", "-"]]
        calls = [call for call in calls if call not in rpc_calls]
        preparation_calls = [call for call in calls if call[0] == "run"]
        calls = [call for call in calls if call not in preparation_calls]
        assert all(call[call.index("--network") + 1] == "none" for call in preparation_calls)
        if action == "install" and not dry and not failure:
            assert len(preparation_calls) == 4
            assert preparation_calls[0][-1] == "prepare" and preparation_calls[1][-1] == "check"
            assert preparation_calls[0][preparation_calls[0].index("--user") + 1] == "0:0"
            assert preparation_calls[1][preparation_calls[1].index("--user") + 1] == "911:911"
            assert "--user" not in preparation_calls[3]
            assert all(call[call.index("--network") + 1] == "none" for call in preparation_calls)
            assert len(rpc_calls) == 1 and rpc_calls[0][rpc_calls[0].index("--network") + 1] == "container:digger"
        registry_calls = [call for call in calls if call[0] in ["--config", "manifest"]]
        calls = [call for call in calls if call[0] not in ["--config", "manifest"]]
        if action == "install" and not failure:
            assert sum(call[0] == "manifest" for call in registry_calls) == 8
            assert sum(call[0] == "--config" for call in registry_calls) == 1
        if dry or failure in ["login", "manifest", "inventory"]:
            assert calls == [], calls
        elif not failure:
            expected = []
            if action == "install":
                if update:
                    expected.append(["compose", "--env-file", ".env", "pull", "--ignore-buildable", *SERVICES])
                expected.append(["compose", "--env-file", ".env", "build", *SERVICES])
                expected.append(["compose", "--env-file", ".env", "stop", "transmission"])
            expected += [["rm", "-f", "agents-portainer_agent"], ["rm", "-f", "media-transmission"]]
            if action == "uninstall":
                expected.append(["compose", "--env-file", ".env", "rm", "--stop", "--force", *SERVICES])
            else:
                if down:
                    expected.append(["compose", "--env-file", ".env", "stop", *SERVICES])
                expected.append(["compose", "--env-file", ".env", "up", "-d", "--wait", "--wait-timeout", "240", *SERVICES])
                expected.append(["exec", "transmission", "sh", "-c", "pidof transmission-daemon | xargs -r kill -HUP"])
            assert calls == expected, calls
        elif failure in ["pull", "build", "stop", "run"]:
            assert all(call[0] == "compose" for call in calls), calls
        assert all("unmanaged" not in " ".join(call) and "domus" not in " ".join(call) and "postgres" not in " ".join(call) for call in calls)
        checks += 1
    print(f"All {checks} deployment workflow scenarios passed using real Compose configuration and simulated Docker mutations.")
