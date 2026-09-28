# 🏠 Home Lab

A compact, self-hosted stack for AI, home automation, media, and everyday infrastructure. Everything lives in one Compose project and is split into three workload profiles so each machine only runs what it needs.

## 🧭 What's inside

- 🧩 [`services/compose.yaml`](services/compose.yaml) — the `lab` stack
- 🛠️ [`scripts/`](scripts/) — deployment helpers and validation checks
- 🚀 [`.github/workflows/deploy.yaml`](.github/workflows/deploy.yaml) — manual GitHub deployment
- 📚 [`docs/`](docs/) — architecture, service inventory, and operations

## 🧰 Workload profiles

- 🤖 **AI** — Hermes, Postgres, Keycloak, Kanbada, pgAdmin, and Portainer Agent on Beelink
- 🏡 **Home** — Home Assistant, Jellyfin, Sonarr, Radarr, Prowlarr, Transmission, and Portainer Agent on Pi 5
- 🧭 **Management** — Nginx Proxy Manager and Portainer on Pi 4

Each profile has its own runner and persistent host storage. Services on different machines communicate through LAN addresses and published ports.

## 🚀 Quick start

### GitHub Actions

Run **Actions → Deploy Home Lab** and choose `install` or `uninstall`, then select one or more profiles. Use `dry_run` to preview the selection. Uninstall preserves stored data and protects dependencies still used by installed services.

### Local Compose

Create a local `.env` file with the required environment variables, then start a profile:

```bash
docker compose --env-file .env -f services/compose.yaml --profile ai up -d --build
```

Replace `ai` with `home` or `management` as needed. Stop a profile with:

```bash
docker compose --env-file .env -f services/compose.yaml --profile ai down
```

💡 Home Assistant uses host networking for device discovery. Other services share a Compose network on their host; networks do not span machines.

💾 Persistent data lives under `~/<service-name>` on the host running each service. Shared media lives in `~/transmission/downloads` on Pi 5.

⚠️ For an existing installation, follow the [migration instructions](docs/operations.md#moving-from-a-single-host-to-three-machines) before starting the unified stack.

## 📖 Documentation

- 🗺️ [Architecture](docs/architecture.md)
- 📦 [Services](docs/services.md)
- 🔧 [Operations and migration](docs/operations.md)
