# Gitea Actions runner

The runner uses the official `docker.io/gitea/runner:2` image and the existing Beelink rootless Docker daemon. It is defined in `services/compose.yaml` under the `lab` project. The **Deploy Home Lab** workflow deploys the full stack, including the runner, and requires the four Tempo password secrets to validate the unified configuration. Runner configuration lives in `services/gitea-runner/config.yaml`, mounted read-only as documented by the [official Docker setup](https://docs.gitea.com/runner/2/installation/docker/).

It joins the existing `homelab_static` network at `10.203.0.31`. Gitea must already be running on that network with the `gitea` DNS alias. The default registration URL is `http://gitea:3000`. If Gitea's configured public URL differs, set `GITEA_INSTANCE_URL` to that URL and ensure both the runner and job containers can reach it; checkout uses Gitea's configured clone URLs. The Actions cache is published at `127.0.0.1:8089` (container port `8088`). Job containers share this network and reach the cache at `10.203.0.31:8088`.

## Register and deploy through GitHub Actions

1. Enable Actions for the intended Gitea repository. Under its **Settings → Actions → Runners**, create a registration token. Use organization or site administration settings instead if the runner should serve that wider scope.
2. In GitHub's `mouras-home-lab` environment, add secret `GITEA_RUNNER_REGISTRATION_TOKEN`. Optional variables are `GITEA_INSTANCE_URL`, `GITEA_RUNNER_NAME` (default `beelink`), `GITEA_RUNNER_TAG` (default `2`) and `USER_ID` (default `1000`, matching the rootless daemon account).
3. Publish these files to the GitHub repository's default branch. Run **Deploy Home Lab** with `action=install`, first with `dry_run=true`, then with `dry_run=false`.
4. Confirm the runner appears **Online** in Gitea. Container startup alone does not verify registration. Run the smoke workflow below in the repository where Actions is enabled.

The existing GitHub runner deploys the new Gitea runner; these are separate registrations. Registration persists in the `~/gitea/runner/data` host directory along with the job cache. Preserve this directory across updates to avoid duplicate registrations. For an existing standalone runner, stop its old container and copy its registration and cache data into `~/gitea/runner/data` before starting the unified project; retain the original data until the migration is verified. Compose creates the shared network during installation. The deployment workflow checks the registration token and the rootless Docker socket for `USER_ID` before starting services. Network name and runner IP remain defined in Compose; update the runner YAML too if changing those values.

Only register repositories whose workflows you trust: the runner controls Beelink's Docker daemon and jobs share the homelab network. The registration token is a GitHub environment secret, never committed. Because it is passed through container environment variables, Docker administrators can inspect container configuration. The image entrypoint consumes the token during registration. Job containers do not automatically receive the host Docker socket; builds needing Docker require an explicit runner configuration and a job image containing the Docker CLI.

## Smoke workflow

Commit this as `.gitea/workflows/runner-smoke.yaml` in a Gitea repository:

```yaml
name: Runner smoke test
on: [push, workflow_dispatch]
jobs:
  smoke:
    runs-on: homelab
    steps:
      - uses: actions/checkout@v4
      - run: |
          node --version
          git --version
          test -d .git
```

Labels `homelab` and `ubuntu-latest` both select a Debian-based `node:22-bookworm` job container. They are labels, not a promise of GitHub's preinstalled Ubuntu tools. Install additional tools in workflows or use a purpose-built job image. One job runs at a time.

For diagnosis on the host, use `docker compose --env-file .env -f services/compose.yaml logs --tail 100 gitea-runner`. If registration fails, check the token scope, instance URL, Gitea Actions settings and network reachability. To update, rerun **Deploy Home Lab** with `action=install`. To retire it, stop the `gitea-runner` service and remove its registration in Gitea; retain the host data directory until you intend to discard the registration and cache.

References: [official Docker installation](https://docs.gitea.com/runner/2/installation/docker/) and [runner configuration](https://docs.gitea.com/runner/2/configuration/).
