# Architecture

All 13 services are defined in [services/compose.yaml](../services/compose.yaml), under the Compose project `lab`.

## Services and networking

- Agentic: Hermes, LM Studio, Keycloak, Postgres, and Nginx Proxy Manager.
- Media: Transmission, Jellyfin, Prowlarr, Sonarr, and Radarr.
- Monitoring: Portainer agent.
- Tools: Portainer server and pgAdmin.

Services are assigned to three Compose profiles:

- `ai` (Ryzen 9, 24 GB RAM, 500 GB SSD): Hermes, LM Studio, Postgres, Keycloak, pgAdmin, and Portainer Agent.
- `media` (8 GB RAM, 1 TB SSD): Jellyfin, Sonarr, Radarr, Prowlarr, Transmission, and Portainer Agent.
- `management` (4 GB RAM, 256 GB SD card): Nginx Proxy Manager and Portainer.

Profiles describe workloads; the workflow separately maps `ai` to Beelink, `media` to Pi 5, and `management` to Pi 4. Every service has a profile, so an unqualified `compose up` does not start the whole lab. Portainer Agent belongs to `ai` and `media` and runs independently on their hosts. All declared dependencies stay within their dependent service's profile.

Each host has its own project network. Keycloak uses local `postgres:5432`. Cross-host connections use stable LAN DNS names or reserved IPs and published ports. Nginx Proxy Manager on Pi 4 routes to services on Beelink and Pi 5. Configure Portainer with the two remote agent endpoints on port `9001`; its local Docker socket manages Pi 4. Hermes uses Keycloak's externally reachable HTTPS issuer URL.

Postgres stays on the Beelink SSD. Restarting that machine interrupts identity and AI services. Jellyfin on Pi 5 is intended for Direct Play, including remote playback. These profiles distribute workloads without providing automatic failover.

Published host ports are distinct per protocol. Portainer server uses container name `portainer` and the agent uses `portainer_agent`.

Keycloak waits for healthy Postgres. LM Studio has a ten-minute startup grace period for installation and model downloads. Its API healthcheck does not guarantee an embedding model is loaded. Sonarr and Radarr start after Prowlarr and Transmission; those integrations reconnect independently, so their ordering uses `service_started`. Prowlarr does not require Transmission to start. Proxy, administration, and playback services can start independently.

## Storage and builds

Persistent bind mounts live directly under `~/<service-name>`, with subdirectories for services that need separate storage locations. Nginx Proxy Manager uses `~/npm/data` and `~/npm/certs`; Transmission uses `~/transmission/config`, `~/transmission/downloads`, and `~/transmission/watch`. Sonarr, Radarr, and Jellyfin share `~/transmission/downloads`, with Jellyfin mounting it read-only. The Portainer agent uses `~/portainer_agent`; no named volumes remain. Docker socket mounts retain their system paths.

Build contexts point to the repository root; Dockerfiles are in `images/` and entrypoints are in `scripts/`.

## Environment and deployment

Runtime variables come from a repository-root `.env` file. GitHub Actions generates it from the selected environment and creates a separate job for each checked profile on its mapped runner label (`beelink`, `rpi5`, or `rpi4`, plus `self-hosted`). The mapping lives in `scripts/workflow.step.select-profiles.sh`. It validates configuration and dependencies before installing or uninstalling the profile. Jobs on different hosts can run concurrently; deployments to the same host are serialized. Profile selection does not move existing containers or data between hosts.
