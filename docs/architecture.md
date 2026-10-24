# Architecture

All fifteen services are defined in [services/compose.yaml](../services/compose.yaml), under the Compose project `lab`.

## Services and networking

The stack includes the AI service OpenClaw, home automation and media services, and management tools (Nginx Proxy Manager and Portainer).

All services run together on the Beelink. The Compose file has no workload profiles, so an unqualified `compose up` starts the full stack. Portainer manages the local Docker host through its mounted socket; no Portainer Agent is needed.

Services share the user-defined Docker bridge network `homelab_static` (`10.203.0.0/24`). Services on the lab network have reserved addresses, listed in [services.md](services.md#container-addresses). Check that this subnet does not overlap Beelink's LAN, VPN, or another Docker network before deployment.

All services, including Home Assistant, use bridge networking so each can have a fixed container address. Home Assistant's mDNS and other broadcast-based device discovery may not cross the Docker bridge; use the Home Assistant UI to add integrations manually or configure a discovery relay if automatic discovery is needed. The former host-networked media and download services now publish their application ports explicitly.

Persistent service data stays on Beelink under each service's home directory. Restarting the Beelink interrupts the full stack; there is no automatic failover.

Published host ports are distinct per protocol. Portainer server uses container name `portainer`.

## Storage and builds

Persistent bind mounts live directly under `~/<service-name>`, with subdirectories for services that need separate storage locations, as defined in Compose. Host paths remain defined in Compose. Nginx Proxy Manager uses `~/npm/data` and `~/npm/certs`; Transmission uses `~/transmission/config`, `/mnt/ssd/transmission/downloads`, and `~/transmission/watch`. Jellyfin mounts `/mnt/ssd/transmission/downloads/complete` at `/data`. Docker socket mounts retain their system paths.

The stack uses published images; no local image build is required. Service configuration files live under `services/` and are mounted read-only from the repository checkout. Compose uses block lists and mappings throughout; braces in `${...}` environment interpolation remain part of the variable syntax.

## Environment and deployment

Compose has no fallback values. Local runtime variables come from a repository-root `.env` file; `.env.example` is only a local setup reference. GitHub Actions reads configuration exclusively from GitHub variables and secrets through `.github/scripts/prepare-config.py`, validates the variables referenced by Compose and runs Docker Compose commands on the self-hosted runner labeled `beelink`. The workflow calls scripts in `.github/scripts/`, validates credentials and Compose configuration, and serializes deployments. Installation stops the Tempo portal/API, waits for PostgreSQL health, tests both database logins, runs the API image once with `--migrate`, then starts the stack. There is no persistent migration service. Tempo API and Keycloak wait for PostgreSQL to be healthy; other service health checks are disabled. Storage preparation and cleanup of previous Compose projects are manual host operations.

## Authentication

Tempo uses a dedicated Keycloak instance; other applications retain their existing authentication and are not connected to it. See [authentication.md](authentication.md) for supported integrations, licensing requirements, and setup steps. Database credentials remain separate from user authentication.
