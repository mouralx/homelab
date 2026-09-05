# Home Lab

One Docker Compose file groups the home lab's services into three machine profiles.

## Layout

- `services/compose.yaml`: all 14 services in the `lab` project, organized by machine profile
- `images/`: Dockerfiles for Honcho and LM Studio
- `scripts/`: entrypoints, environment generation, and Compose validation
- `.github/workflows/deploy.yaml`: manual deployment to the selected host
- `docs/`: architecture, service inventory, and operations

## Quick start

To manage services through GitHub, run **Actions → Deploy Home Lab**. Choose `install` or `uninstall` and check one or more machine profiles. Each selected profile runs on its matching self-hosted runner. Enable `dry_run` to preview the selection. Uninstall preserves stored data and protects dependencies still used by installed services.

- `beelink`: Hermes, Honcho, LM Studio, Postgres, Keycloak, pgAdmin, Portainer Agent.
- `rpi5`: Jellyfin, Sonarr, Radarr, Prowlarr, Transmission, Portainer Agent.
- `rpi4`: Nginx Proxy Manager and Portainer.

Create a local `.env` file with the required environment variables, then run:

```bash
docker compose --env-file .env -f services/compose.yaml --profile beelink up -d --build
```

Replace `beelink` with the local machine's profile. Stop that profile with:

```bash
docker compose --env-file .env -f services/compose.yaml --profile beelink down
```

Services share a Compose network on each host; networks do not span machines. Persistent data lives under `~/<service-name>` on the host running each service. Shared media lives in `~/transmission/downloads` on Pi 5; Docker socket mounts retain their system paths.

For an existing installation, follow the migration instructions before starting the unified stack.

## Documentation

- [Architecture](docs/architecture.md)
- [Services](docs/services.md)
- [Operations and migration](docs/operations.md)
