# Architecture

All 13 services are defined in [services/compose.yaml](../services/compose.yaml), under the Compose project `lab`.

## Services and networking

- Agentic: Hermes, Keycloak, and Postgres.
- Home: Home Assistant, Transmission, Jellyfin, Prowlarr, Sonarr, and Radarr.
- Monitoring: Portainer agent.
- Management: Nginx Proxy Manager and Portainer.
- Tools: pgAdmin.

All services run together on the Beelink. The Compose file has no workload profiles, so an unqualified `compose up` starts the full stack. Portainer Agent runs locally alongside Portainer.

Services share the Beelink's Compose network. Home Assistant uses host networking for device discovery. Keycloak and Kanbada use local `postgres:5432`; Kanbada uses its own database in the shared PostgreSQL instance. Hermes uses Keycloak's externally reachable HTTPS issuer URL.

Persistent service data stays on Beelink under each service's home directory. Restarting the Beelink interrupts the full stack; there is no automatic failover.

Published host ports are distinct per protocol. Portainer server uses container name `portainer` and the agent uses `portainer_agent`.

Keycloak waits for healthy Postgres. Sonarr and Radarr start after Prowlarr and Transmission; those integrations reconnect independently, so their ordering uses `service_started`. Prowlarr does not require Transmission to start. Proxy, administration, and playback services can start independently.

## Storage and builds

Persistent bind mounts live directly under `~/<service-name>`, with subdirectories for services that need separate storage locations. Nginx Proxy Manager uses `~/npm/data` and `~/npm/certs`; Transmission uses `~/transmission/config`, `~/transmission/downloads`, and `~/transmission/watch`. Sonarr, Radarr, and Jellyfin share `~/transmission/downloads`, with Jellyfin mounting it read-only. The Portainer agent uses `~/portainer_agent`; no named volumes remain. Docker socket mounts retain their system paths.

The stack uses published images and bind-mounted configuration directories; no local image build is required.

## Environment and deployment

Runtime variables come from a repository-root `.env` file. GitHub Actions generates it from the selected environment and runs the full install or uninstall on the self-hosted runner labeled `beelink`. The workflow validates configuration and dependencies before changing services, and serializes deployments to that host. Existing containers and data on other machines are not moved by this workflow.
