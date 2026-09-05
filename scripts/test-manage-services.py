#!/usr/bin/env python3
"""Exercise workflow service actions with a fake Docker CLI; no daemon needed."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
CONFIG = {"name": "lab", "services": {
    "honcho": {"depends_on": {"postgres": {}, "lms": {}}},
    "postgres": {}, "lms": {},
    "keycloak": {"depends_on": {"postgres": {}}},
    "frontend": {"depends_on": {"honcho": {}}},
    "jellyfin": {},
}}
MOCK = r'''#!/usr/bin/env python3
import json, os, sys
args = sys.argv[1:]
with open(os.environ["DOCKER_LOG"], "a") as log:
    log.write(json.dumps(args) + "\n")
if "config" in args:
    print(os.environ["TEST_CONFIG"])
elif args[0] == "ps":
    if os.environ.get("PS_FAIL"):
        sys.exit(1)
    owner = next(a.split("=", 2)[2] for a in args if a.startswith("label=com.docker.compose.project="))
    for service in json.loads(os.environ["INSTALLED"]).get(owner, []):
        if "--format" in args:
            print(json.dumps({"id": owner + "-" + service, "service": service}))
        elif "label=com.docker.compose.service=" + service in args:
            print(owner + "-" + service)
'''


class ManageServicesTests(unittest.TestCase):
    def run_action(self, selection, action="install", installed=None, **flags):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            docker = path / "docker"
            docker.write_text(MOCK)
            docker.chmod(0o755)
            log = path / "docker.log"
            env = dict(os.environ, PATH=temp + os.pathsep + os.environ["PATH"],
                       DOCKER_LOG=str(log), TEST_CONFIG=json.dumps(CONFIG),
                       INSTALLED=json.dumps(installed or {}), SERVICE_ACTION=action,
                       SELECTED_SERVICES=selection, DRY_RUN="false", UPDATE_IMAGES="false",
                       BRING_DOWN_FIRST="false", GITHUB_STEP_SUMMARY=str(path / "summary"))
            env.update(flags)
            result = subprocess.run(["bash", "scripts/workflow.step.manage-services.sh"],
                                    cwd=ROOT, env=env, text=True, capture_output=True)
            calls = [json.loads(line) for line in log.read_text().splitlines()]
            mutations = [c for c in calls if c[0] != "ps" and "config" not in c]
            return result, mutations

    def test_install_transitive_dependencies(self):
        result, calls = self.run_action(" frontend, jellyfin\nfrontend ", UPDATE_IMAGES="true",
                                        BRING_DOWN_FIRST="true")
        self.assertEqual(result.returncode, 0, result.stderr)
        targets = ["frontend", "honcho", "jellyfin", "lms", "postgres"]
        self.assertEqual(calls, [
            ["compose", "--env-file", ".env", "pull", "--ignore-buildable"] + targets,
            ["compose", "--env-file", ".env", "build"] + targets,
            ["compose", "--env-file", ".env", "stop"] + targets,
            ["compose", "--env-file", ".env", "up", "-d"] + targets])

    def test_invalid_selection_never_mutates(self):
        for selection in ["", " , ", "unknown", "all,honcho", "--help", "$(touch /tmp/no)"]:
            with self.subTest(selection=selection):
                result, calls = self.run_action(selection)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(calls, [])

    def test_dry_run(self):
        for action in ["install", "uninstall"]:
            result, calls = self.run_action("honcho", action, DRY_RUN="true", UPDATE_IMAGES="true")
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(calls, [])

    def test_uninstall_dependency_is_blocked(self):
        for project in ["lab", "agentic"]:
            result, calls = self.run_action("postgres", "uninstall", {project: ["frontend"]})
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("frontend", result.stderr)
            self.assertEqual(calls, [])

    def test_uninstall_app_retains_dependencies(self):
        result, calls = self.run_action("honcho", "uninstall", {"lab": ["honcho", "postgres", "keycloak"]})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls, [["compose", "--env-file", ".env", "rm", "--stop", "--force", "honcho"]])

    def test_uninstall_with_dependents(self):
        result, calls = self.run_action("honcho,postgres,keycloak", "uninstall",
                                        {"lab": ["honcho", "postgres", "keycloak"]})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls[-1][-3:], ["honcho", "keycloak", "postgres"])

    def test_all(self):
        for action in ["install", "uninstall"]:
            result, calls = self.run_action("all", action, {"lab": list(CONFIG["services"])})
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(calls[-1][-len(CONFIG["services"]):], sorted(CONFIG["services"]))

    def test_legacy_cleanup_is_scoped(self):
        result, calls = self.run_action("jellyfin", installed={"media": ["jellyfin", "sonarr"]})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(calls[1], ["rm", "-f", "media-jellyfin"])
        self.assertNotIn("media-sonarr", str(calls))

    def test_inventory_failure_never_mutates(self):
        result, calls = self.run_action("all", "uninstall", PS_FAIL="true")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(calls, [])

    def test_legacy_shared_dependency_requires_dependents(self):
        result, calls = self.run_action("honcho", installed={"agentic": ["postgres", "keycloak"]})
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("keycloak", result.stderr)
        self.assertEqual(calls, [])

    def test_legacy_dependents_can_migrate_together(self):
        result, calls = self.run_action("honcho,keycloak", installed={"agentic": ["postgres", "keycloak"]})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(["rm", "-f", "agentic-postgres"], calls)


if __name__ == "__main__":
    unittest.main()
