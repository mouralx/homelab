# 🏠 Home Lab

A compact, self-hosted stack for AI, home automation, media, and everyday infrastructure. All services run together in the `lab` Compose project on the Beelink.

## What's inside

- [`services/compose.yaml`](services/compose.yaml) — the full stack
- [`.github/workflows/deploy.yaml`](.github/workflows/deploy.yaml) — manual GitHub deployment to the Beelink runner
- [`docs/`](docs/) — architecture, service inventory, and operations

## Quick start

### GitHub Actions

Run **Actions → Deploy Home Lab** and choose `install` or `uninstall`. Use `dry_run` to validate the full stack without changing running services. The workflow runs on the self-hosted runner labeled `beelink`.

### Local Compose

Create a `.env` file with the required environment variables, then start or stop the full stack:

```bash
docker compose --env-file .env -f services/compose.yaml up -d --wait --wait-timeout 240
docker compose --env-file .env -f services/compose.yaml down
```

Services have fixed container addresses on the `10.203.0.0/24` Docker network. Existing services keep their `~/service-name` bind mounts. Home Assistant uses bridge networking to keep a fixed address, which can limit automatic mDNS/broadcast discovery.

For an existing multi-host installation, review the [migration instructions](docs/operations.md#moving-everything-to-the-beelink) before deployment.

## Documentation

- [Architecture](docs/architecture.md)
- [Services](docs/services.md)
- [Operations and migration](docs/operations.md)
- [Authentication options](docs/authentication.md)
- [Tempo deployment and private images](docs/tempo.md)
- [Gitea Actions runner](docs/gitea-runner.md)
