# 🏠 Home Lab

A compact, self-hosted stack for AI, home automation, media, and everyday infrastructure. All services run together in the `lab` Compose project on the Beelink.

## What's inside

- [`services/compose.yaml`](services/compose.yaml) — the full stack
- [`.env.example`](.env.example) — local settings and required credential placeholders
- [`.github/workflows/deploy.yaml`](.github/workflows/deploy.yaml) — manual GitHub deployment to the Beelink runner
- [`.github/scripts/`](.github/scripts/) — deployment shell scripts
- [`docs/`](docs/) — architecture, service inventory, and operations

## Quick start

### GitHub Actions

Run **Actions → Deploy Home Lab** and choose `install` or `uninstall`. Use `dry_run` to validate the full stack without changing running services. The workflow runs on the self-hosted runner labeled `beelink`. Configure the required [Tempo passwords and registry access](docs/tempo.md) and the [runner registration token](docs/gitea-runner.md) first. Installation stops Tempo, verifies PostgreSQL access and applies schema migrations before starting the stack.

### Local Compose

Start on a Linux host with Docker Engine, Docker Compose v2 supporting `up --wait --wait-timeout`, and the rootless Docker socket described in [operations](docs/operations.md). Copy the template without overwriting an existing `.env`:

```bash
cp -n .env.example .env
chmod 600 .env
```

The example has empty assignments with commented sample values and per-setting instructions. Edit `.env` and fill every required value, including `COMPOSE_FILE=services/compose.yaml`, `USER_ID`, timezone, image tags, service settings, public URLs and credentials. Generate each new password/token separately with `openssl rand -hex 32`. Reuse actual credentials for existing databases. Blank required values intentionally prevent a complete first boot.

Prepare [host storage and the SSD](docs/operations.md#rootless-media-storage), complete [OpenClaw onboarding](docs/services.md#openclaw), and configure the [Tempo HTTPS routes](docs/tempo.md#https-routing). Set `VAULT_API_ADDR` in `.env` for your Vault domain. Sign in to GHCR using `docker login ghcr.io` with a package-read token that can access the private Tempo and Digger images.

For a new Gitea installation, start it first, finish its setup, enable Actions and create the [runner registration token](docs/gitea-runner.md). Put that token in `.env` before starting the runner:

```bash
docker compose --env-file .env -f services/compose.yaml config --quiet
docker compose --env-file .env -f services/compose.yaml up -d gitea
```

Then complete the [Tempo database login checks and migration](docs/tempo.md#schema-updates-and-manual-installation) before starting the stack. A plain `up` does not apply schema changes.

```bash
# After the migration succeeds:
docker compose --env-file .env -f services/compose.yaml up -d
docker compose --env-file .env -f services/compose.yaml logs --tail 100
# Stop the stack while retaining host data:
docker compose --env-file .env -f services/compose.yaml down
```

Provision your [Tempo account](docs/tempo.md#first-login-and-verification) and [initialize/unseal Vault](docs/vault.md#initialize-and-unseal) after startup. GitHub deployment uses only GitHub secrets and variables. It does not read `.env.example` or the runner’s local `.env`; copying the example does not configure GitHub.

Services have fixed addresses on `10.203.0.0/24` and host data paths defined in Compose. `.env` configures service environment values and the existing image-tag/socket UID settings; it does not configure ports, IPs or host paths. Compose has no fallback values, so copy the complete template and merge new keys when upgrading. Home Assistant’s bridge network may limit automatic discovery.

For an existing multi-host installation, review the [migration instructions](docs/operations.md#moving-everything-to-the-beelink) before deployment.

## Documentation

- [Architecture](docs/architecture.md)
- [Services](docs/services.md)
- [Operations and migration](docs/operations.md)
- [Authentication options](docs/authentication.md)
- [Tempo deployment and private images](docs/tempo.md)
- [Gitea Actions runner](docs/gitea-runner.md)
- [Vault setup and recovery](docs/vault.md)
