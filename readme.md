# Home Lab

One Docker Compose stack runs the home lab's agentic, media, monitoring, and tools services.

## Layout

- `services/compose.yaml`: all 14 services in the `home-lab` project
- `images/`: Dockerfiles for Honcho and LM Studio
- `scripts/`: entrypoints, environment generation, and Compose validation
- `.github/workflows/deploy.yaml`: manual deployment to the selected host
- `docs/`: architecture, service inventory, and operations

## Quick start

To manage services through GitHub, run **Actions → Deploy Home Lab**. Choose `install` or `uninstall` and tick the checkboxes for the services you want, or **Select all services**. Installs include dependencies automatically; uninstalls protect dependencies still needed by installed services and preserve stored data. Enable `dry_run` to preview the selection.

Create a local `.env` file with the required environment variables, then run:

```bash
docker compose --env-file .env -f services/compose.yaml up -d --build
```

Stop the stack with:

```bash
docker compose --env-file .env -f services/compose.yaml down
```

All services share one Compose network. Persistent data lives under `~/<service-name>`. Shared media lives in `~/transmission/downloads`; Docker socket mounts retain their system paths.

For an existing installation, follow the migration instructions before starting the unified stack.

## Documentation

- [Architecture](docs/architecture.md)
- [Services](docs/services.md)
- [Operations and migration](docs/operations.md)
