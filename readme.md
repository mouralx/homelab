# 🏠 Home Lab

A compact, self-hosted stack for AI, home automation, media, and everyday infrastructure. All services run together in the `lab` Compose project on the Beelink.

## What's inside

- [`services/compose.yaml`](services/compose.yaml) — the full stack
- [`scripts/`](scripts/) — deployment helpers and validation checks
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

Public services and PostgreSQL clients have fixed container addresses on the `10.203.0.0/24` Docker network. Existing services keep their `~/service-name` bind mounts; Domus also uses named volumes for files, keys and its broker. Home Assistant uses bridge networking to keep a fixed address, which can limit automatic mDNS/broadcast discovery.

For an existing multi-host installation, review the [migration instructions](docs/operations.md#moving-everything-to-the-beelink) before deployment.

## Documentation

- [Architecture](docs/architecture.md)
- [Services](docs/services.md)
- [Operations and migration](docs/operations.md)
- [Authentication options](docs/authentication.md)
- [Domus and the shared PostgreSQL server](docs/domus.md)

## domus

Domus uses published `ghcr.io/mouralx/domus-*` images (`stable` by default), six independent application containers and RabbitMQ. The restored PostgreSQL service reuses `~/postgres` and its original PostgreSQL 15 image; Domus creates three dedicated databases on this same server. Configure the existing database credentials, dedicated Domus passwords and GHCR access before deployment. The portal is reached through Nginx Proxy Manager at `domus-portal:80`, without another host-port binding.

See [Domus deployment](docs/domus.md) for required secrets, image selection, proxy configuration, PostgreSQL storage checks and runtime tests. The workflow waits for healthy services and verifies the published images before changing containers.
