# Architecture

All 16 services are defined in [services/compose.yaml](../services/compose.yaml), under the Compose project `lab`.

## Services and networking

The stack includes the AI service OpenClaw, home automation and media services, and management tools (Nginx Proxy Manager and Portainer).

All services run together on the Beelink. The Compose file has no workload profiles, so an unqualified `compose up` starts the full stack. Portainer manages the local Docker host through its mounted socket; no Portainer Agent is needed.

Services share the user-defined Docker bridge network `homelab_static` (`10.203.0.0/24`). Services on the lab network have reserved addresses, listed in [services.md](services.md#fixed-container-addresses). Check that this subnet does not overlap Beelink's LAN, VPN, or another Docker network before deployment.

All services, including Home Assistant, use bridge networking so each can have a fixed container address. Home Assistant's mDNS and other broadcast-based device discovery may not cross the Docker bridge; use the Home Assistant UI to add integrations manually or configure a discovery relay if automatic discovery is needed. The former host-networked media and download services now publish their application ports explicitly.

Persistent service data stays on Beelink under each service's home directory. Restarting the Beelink interrupts the full stack; there is no automatic failover.

Published host ports are distinct per protocol. Portainer server uses container name `portainer`.

Domus uses three databases in the shared PostgreSQL service. Its initialization client exits successfully before the APIs/workers start; the portal waits for the APIs to be healthy. Other application services retain their default local database/file storage. Domus also has a dedicated network for internal aliases and RabbitMQ.

## Storage and builds

Persistent bind mounts live directly under `~/<service-name>`, with subdirectories for services that need separate storage locations. Nginx Proxy Manager uses `~/npm/data` and `~/npm/certs`; Transmission uses `~/transmission/config`, `~/transmission/downloads`, and `~/transmission/watch`. Jellyfin mounts `~/transmission/downloads/complete` at `/data`. Domus uses four persistent named volumes for its shared files, encryption keys and broker state; PostgreSQL retains its existing ~/postgres bind mount. Docker socket mounts retain their system paths.

The stack uses published images and bind-mounted configuration directories; no local image build is required.

## Environment and deployment

Runtime variables come from a repository-root `.env` file. GitHub Actions generates it from the selected environment and runs the full install or uninstall on the self-hosted runner labeled `beelink`. The workflow validates configuration and dependencies before changing services, and serializes deployments to that host. Installs remove obsolete containers from the previous `agentic`, `agents`, `media`, and `tools` Compose projects on Beelink, and preserve unmanaged/orphaned containers in the `lab` project. They do not move data from other machines.

## Authentication

No centralized identity provider is deployed, and application SSO has not been enabled. See [authentication.md](authentication.md) for supported integrations, licensing requirements, and setup steps. Database credentials remain separate from user authentication.

## domus

The six Domus components use published GHCR images with per-component tags, defaulting to stable. PostgreSQL remains one shared server on its historical image/data path. A PostgreSQL client creates only Domus databases and refuses unrelated database-name collisions. The deployment preflight checks the existing data major and verifies authenticated GHCR image access before changing containers. Detailed boundaries and setup are in [domus.md](domus.md).
