# Architecture

All 14 services are defined in [services/compose.yaml](../services/compose.yaml), under the Compose project `lab`.

## Services and networking

- Agentic: Hermes, Honcho, LM Studio, Keycloak, Postgres, and Nginx Proxy Manager.
- Media: Transmission, Jellyfin, Prowlarr, Sonarr, and Radarr.
- Monitoring: Portainer agent.
- Tools: Portainer server and pgAdmin.

All services join the project's default network and can address each other by service name. Honcho and Keycloak use `postgres:5432`; Honcho uses `lms:4321` for embeddings. Nginx Proxy Manager can reach services directly on this shared network.

Published host ports are distinct per protocol. Portainer server uses container name `portainer` and the agent uses `portainer_agent`.

Honcho waits for healthy Postgres and the LM Studio HTTP API; Keycloak waits for healthy Postgres. LM Studio has a ten-minute startup grace period for installation and model downloads. Its API healthcheck does not guarantee an embedding model is loaded. Sonarr and Radarr start after Prowlarr and Transmission; those integrations reconnect independently, so their ordering uses `service_started`. Prowlarr does not require Transmission to start. Proxy, administration, and playback services can start independently.

## Storage and builds

Persistent bind mounts live directly under `~/<service-name>`, with subdirectories for services that need separate storage locations. Nginx Proxy Manager uses `~/npm/data` and `~/npm/certs`; Transmission uses `~/transmission/config`, `~/transmission/downloads`, and `~/transmission/watch`. Sonarr, Radarr, and Jellyfin share `~/transmission/downloads`, with Jellyfin mounting it read-only. The Portainer agent uses `~/portainer_agent`; no named volumes remain. Docker socket mounts retain their system paths.

Build contexts point to the repository root; Dockerfiles are in `images/` and entrypoints are in `scripts/`.

## Environment and deployment

Runtime variables come from a repository-root `.env` file. GitHub Actions generates it from the selected environment, validates the resolved configuration and port bindings, and deploys the entire stack on the selected runner. Deployments to the same host are serialized.
