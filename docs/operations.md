# Operations

## Validate, build, and deploy

Run from the repository root with a configured `.env`:

```bash
docker compose --env-file .env -f services/compose.yaml config --quiet
docker compose --env-file .env -f services/compose.yaml up -d --build --remove-orphans
```

## Logs and shutdown

```bash
docker compose --env-file .env -f services/compose.yaml logs -f
docker compose --env-file .env -f services/compose.yaml down
```

Append a service name to `logs` to inspect just that service.

## GitHub deployment

Open **Actions → Deploy Home Lab → Run workflow** and choose `install` or `uninstall`. The workflow runs direct Docker Compose commands on the self-hosted runner labeled `beelink`, which needs Docker Compose v2. Deployments to that host are serialized.

Configure the selected GitHub environment with the variables mapped in the workflow, including `TIME_ZONE`, `USER_ID`, `TRANSMISSION_PEER_PORT`, and optional `DIGGER_*` settings. Store `GITEA_RUNNER_REGISTRATION_TOKEN`, `OPENCLAW_GATEWAY_TOKEN`, `TRANSMISSION_USERNAME`, `TRANSMISSION_PASSWORD`, and optional Digger RPC credential overrides as secrets. `GH_PAT` and `GH_USER` optionally override the workflow token and actor for GHCR login; the token must have access to the Digger package.

A dry run validates Compose configuration without changing containers. Install optionally pulls images and stops services, then starts the full stack without waiting for application readiness. Container health checks, including image-provided checks, are disabled. Startup dependencies enforce container order; the API still waits for successful database migrations. Uninstall runs `docker compose down`, preserving bind-mounted data and named volumes. Runtime values are passed directly through workflow environment mappings; no environment file is generated.

Prepare host storage ownership and permissions before installation. Cleanup of previous Compose projects is manual. Application SSO remains unconfigured; see [Authentication options](authentication.md).

## Rootless media storage

Run these commands on Beelink as the account running the rootless Docker daemon (also the deployment runner account). Confirm `docker info --format '{{json .SecurityOptions}}'` includes `rootless`. The Compose media identity assumes rootless Docker.

Stop the media services before repairing existing storage. Create the directories, then transfer ownership of these specific service directories and grant their owner read/write access (and directory traversal). `sudo` is needed only when existing data belongs to another account:

```bash
docker compose -f services/compose.yaml stop digger jellyfin transmission
mkdir -p ~/digger ~/jellyfin ~/transmission/config ~/transmission/watch /mnt/ssd/transmission/downloads/complete/movies
sudo chown -R "$(id -u):$(id -g)" ~/digger ~/jellyfin ~/transmission/config ~/transmission/watch /mnt/ssd/transmission/downloads
chmod -R u+rwX ~/digger ~/jellyfin ~/transmission/config ~/transmission/watch /mnt/ssd/transmission/downloads
docker compose -f services/compose.yaml up -d transmission digger jellyfin
```

If creating the SSD directory fails, have its owner or administrator create it and assign it to the daemon account first. That account must also have traversal permission on `/mnt` and `/mnt/ssd`. For filesystems without Unix ownership support, configure the mount's UID/GID and access modes for that account instead. The SSD must be mounted read/write before starting the stack.

Transmission and Digger share `/movies`, backed by `/mnt/ssd/transmission/downloads/complete/movies`; Jellyfin sees those movies at `/data/movies`. Existing Transmission torrents keep their saved download paths; check them before moving any existing media. Configure Jellyfin's movie library for `/data/movies`.

## Moving everything to the Beelink

The workflow changes containers only on Beelink. Before deploying the full stack, back up and copy each service's data from its old host to the corresponding directory under the Beelink home directory, preserving ownership. Stop and remove old containers on other hosts explicitly after verifying the migration; the workflow cannot reach or clean them up.

Keep downloads and media directories together when copying. Update Nginx Proxy Manager routes to Beelink's LAN DNS/IP and published ports. Because rootless Docker cannot bind privileged host ports, configure router port forwarding from external TCP `80` to Beelink TCP `8088` and external TCP `443` to Beelink TCP `8448`. Access the Nginx Proxy Manager admin UI on Beelink port `8181`. The selected GitHub environment supplies runtime configuration; ensure values such as socket user IDs match Beelink.

## Migration from the four previous projects

Stop and remove old service containers on Beelink manually before starting the full `lab` stack. Complete storage migration before deployment.

Stop the existing services before copying data, preserving ownership and permissions:

- Move `~/agentic/npm` to `~/npm`.
- Move `~/tools/portainer` to `~/portainer`.
- Move `~/media/jellyfin` to `~/jellyfin`.
- Move `~/media/transmission` to `~/transmission/config`, `~/media/data` to `~/transmission/downloads`, and `~/media/watch` to `~/transmission/watch`.

Keep a backup until the new stack is verified. Container mount targets remain unchanged, including the shared `/downloads` path and Jellyfin's `/data` path.

For manual migration, stop and remove the containers belonging to those four previous projects and any old Portainer Agent before starting the full stack. Do not delete their volumes. Their old networks can remain unused.

## Transmission peer port

`TRANSMISSION_PEER_PORT` defaults to `51413` and controls both the internal listener and the published TCP/UDP ports. Choose a value not published by another service; avoid collisions with other published ports.

## Retired services

Hermes, Kanbada, Prowlarr, Radarr, Keycloak, pgAdmin, Honcho, and PostgreSQL are not part of the current stack. Workflow installs preserve orphaned containers and their data; retire unwanted services explicitly. Containers from older projects or other hosts require manual cleanup. Unused `POSTGRES_*`, `HERMES_*`, `KANBADA_*`, `KC_*`, `OPENCODE_API_KEY`, `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD`, and `HOMEASSISTANT_DB_URL` settings can be removed from the selected GitHub environment if no other workflow uses them. Preserve retired service data until you decide it is no longer needed.

If the earlier Recorder PostgreSQL configuration was applied on Beelink, remove `recorder: !include recorder-postgres.yaml` or the PostgreSQL `db_url` override from Home Assistant configuration before deployment. Home Assistant will then use its default SQLite database.

Transmission allows all client IPs and hostnames through explicitly empty `WHITELIST` and `HOST_WHITELIST` environment variables. The LinuxServer image configures access and authentication directly; credentials come from `TRANSMISSION_USERNAME` and `TRANSMISSION_PASSWORD`.
