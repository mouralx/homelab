# Architecture

All 16 services are defined in [services/compose.yaml](../services/compose.yaml), under the Compose project `lab`.

## Services and networking

The stack includes AI services (OpenClaw, Hermes, and Kanbada), shared infrastructure (Postgres and Keycloak), home automation and media services, and management tools (Nginx Proxy Manager, Portainer, and pgAdmin).

All services run together on the Beelink. The Compose file has no workload profiles, so an unqualified `compose up` starts the full stack. Portainer manages the local Docker host through its mounted socket; no Portainer Agent is needed.

Services share the user-defined Docker bridge network `homelab_static` (`10.203.0.0/24`). Every service has a reserved address, listed in [services.md](services.md#fixed-container-addresses). Check that this subnet does not overlap Beelink's LAN, VPN, or another Docker network before deployment. Keycloak and Kanbada use local `postgres:5432`; Kanbada uses its own database in the shared PostgreSQL instance. Hermes uses Keycloak's externally reachable HTTPS issuer URL.

All services, including Home Assistant, use bridge networking so each can have a fixed container address. Home Assistant's mDNS and other broadcast-based device discovery may not cross the Docker bridge; use the Home Assistant UI to add integrations manually or configure a discovery relay if automatic discovery is needed. The former host-networked media and download services now publish their application ports explicitly.

Persistent service data stays on Beelink under each service's home directory. Restarting the Beelink interrupts the full stack; there is no automatic failover.

Published host ports are distinct per protocol. Portainer server uses container name `portainer`.

Keycloak waits for healthy Postgres. Sonarr and Radarr start after Prowlarr and Transmission; those integrations reconnect independently, so their ordering uses `service_started`. Prowlarr does not require Transmission to start. Proxy, administration, and playback services can start independently.

## Storage and builds

Persistent bind mounts live directly under `~/<service-name>`, with subdirectories for services that need separate storage locations. Nginx Proxy Manager uses `~/npm/data` and `~/npm/certs`; Transmission uses `~/transmission/config`, `~/transmission/downloads`, and `~/transmission/watch`. Sonarr, Radarr, and Jellyfin share `~/transmission/downloads`, with Jellyfin mounting it read-only. No named volumes remain. Docker socket mounts retain their system paths.

The stack uses published images and bind-mounted configuration directories; no local image build is required.

## Environment and deployment

Runtime variables come from a repository-root `.env` file. GitHub Actions generates it from the selected environment and runs the full install or uninstall on the self-hosted runner labeled `beelink`. The workflow validates configuration and dependencies before changing services, and serializes deployments to that host. Installs remove obsolete containers from the previous `agentic`, `agents`, `media`, and `tools` Compose projects on Beelink, then remove orphan containers from the `lab` project. They do not move data from other machines.
