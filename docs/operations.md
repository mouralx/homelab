# Operations

## Validate, build, and deploy

Run from the repository root with a configured `.env`:

```bash
docker compose --env-file .env -f services/compose.yaml config --format json | bash scripts/validate-compose.sh
docker compose --env-file .env -f services/compose.yaml up -d --build --remove-orphans
```

The validator rejects duplicate container names and overlapping published ports, including overrides of `TRANSMISSION_PEER_PORT`. TCP and UDP may use the same number. It checks the Compose configuration; other processes on the target host can still occupy a published port.

## Logs and shutdown

```bash
docker compose --env-file .env -f services/compose.yaml logs -f
docker compose --env-file .env -f services/compose.yaml down
```

Append a service name to `logs` to inspect just that service.

## GitHub deployment

Open **Actions → Deploy Home Lab → Run workflow**. Choose the environment and `install` (also updates existing services) or `uninstall`. The workflow manages the full Compose stack on Beelink.

The Beelink self-hosted runner must have the `self-hosted` and `beelink` labels and needs Docker Compose v2, Bash, and jq. The workflow has no GitHub-hosted runner or routing to other hosts. If Beelink is offline, the deployment remains queued.

Before deployment, configure the selected GitHub environment:

| Setting | Where to store it | Used by |
| --- | --- | --- |
| `TIME_ZONE` | Variable, for example `Europe/Lisbon` | Nginx Proxy Manager |
| `OPENCLAW_GATEWAY_TOKEN` | Secret | OpenClaw; required when bootstrapping a LAN-bound gateway without saved authentication |
| `USER_ID` | Optional variable, default `1000` | Portainer Docker socket path |
| `TRANSMISSION_PEER_PORT` | Optional variable, default `51413` | Transmission TCP/UDP peer port |

The environment-file step already includes every selected environment variable and secret except `GITHUB_TOKEN`; no workflow mapping changes are needed. Domus has a one-shot client that creates its three databases on the shared PostgreSQL server. It does not migrate existing application records or SQLite data. Unmanaged `lab` containers are preserved; stored data and retired application databases are preserved. Obsolete `KC_*` settings can be removed from the selected GitHub environment.

Application SSO remains unconfigured; see [Authentication options](authentication.md) for separate setup requirements.

Install manages all services and respects Compose startup and healthcheck conditions. Image updates, builds, and optional shutdown apply to the full stack.

Uninstall stops and removes all stack containers, preserving stored data, images, and volumes. Each workflow run is an action, not a saved desired-state list.

A dry run generates the environment file, validates configuration and dependency safety, and displays the full service plan in the job summary without changing containers. Real installs pull images when requested, build the stack, remove matching legacy containers from the old projects on Beelink, remove a retired Portainer Agent from the old `agents` project there, then start the stack and wait for health checks. Unmanaged Compose orphans are retained. The workflow removes its generated `.env` file even on failure and does not prune Docker volumes or unrelated containers.

Local regression checks are available with `bash scripts/test-validate-compose.sh` and `bash scripts/test-manage-services.sh` (uses a fake Docker CLI).

## Moving everything to the Beelink

The workflow changes containers only on Beelink. Before deploying the full stack, back up and copy each service's data from its old host to the corresponding directory under the Beelink home directory, preserving ownership. Stop and remove old containers on other hosts explicitly after verifying the migration; the workflow cannot reach or clean them up.

Keep downloads and media directories together when copying. Update Nginx Proxy Manager routes to Beelink's LAN DNS/IP and published ports. Because rootless Docker cannot bind privileged host ports, configure router port forwarding from external TCP `80` to Beelink TCP `8088` and external TCP `443` to Beelink TCP `8448`. Access the Nginx Proxy Manager admin UI on Beelink port `8181`. The selected GitHub environment supplies runtime configuration; ensure values such as socket user IDs match Beelink.

## Migration from the four previous projects

Real workflow installs remove matching service containers carrying Compose project labels `agentic`, `agents`, `media`, or `tools` on Beelink before starting the full `lab` stack. They also remove the retired Portainer Agent from the old `agents` project on Beelink. This causes a brief interruption. It does not relocate stored data or remove containers on other hosts. Complete the storage migration below before deployment so services do not start with empty data directories.

Stop the existing services before copying data, preserving ownership and permissions:

- Move `~/agentic/npm` to `~/npm`.
- Move `~/tools/portainer` to `~/portainer`.
- Move `~/media/jellyfin` to `~/jellyfin`.
- Move `~/media/transmission` to `~/transmission/config`, `~/media/data` to `~/transmission/downloads`, and `~/media/watch` to `~/transmission/watch`.

Keep a backup until the new stack is verified. Container mount targets remain unchanged, including the shared `/downloads` path and Jellyfin's `/data` path.

For manual migration, stop and remove the containers belonging to those four previous projects and any old Portainer Agent before starting the full stack. Do not delete their volumes. Their old networks can remain unused.

## Transmission peer port

`TRANSMISSION_PEER_PORT` defaults to `51413` and controls both the internal listener and the published TCP/UDP ports. Choose a value not published by another service; validation rejects collisions.

## Retired services

Hermes, Kanbada, Prowlarr, Radarr, Keycloak, pgAdmin, and Honcho are not part of the current stack. Domus now uses the shared PostgreSQL service; the original data directory and major are preserved. Workflow installs preserve orphaned containers and their data; retire unwanted services explicitly. Containers from older projects or other hosts require manual cleanup. Keep POSTGRES_USER and POSTGRES_PASSWORD for the existing shared server. Unused `HERMES_*`, `KANBADA_*`, `KC_*`, `OPENCODE_API_KEY`, `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD`, and `HOMEASSISTANT_DB_URL` settings can be removed from the selected GitHub environment if no other workflow uses them. Preserve retired service data until you decide it is no longer needed.

If the earlier Recorder PostgreSQL configuration was applied on Beelink, remove `recorder: !include recorder-postgres.yaml` or the PostgreSQL `db_url` override from Home Assistant configuration before deployment. Home Assistant will then use its default SQLite database.

## domus and PostgreSQL

Configure the additional variables/secrets in [services/domus/environment.example](../services/domus/environment.example), then follow [Domus deployment](domus.md). The existing PostgreSQL service/data directory is reused. Do not upgrade the PostgreSQL image's major as part of this install. Storage validation and authenticated GHCR manifest checks happen before container changes. The workflow preserves the runner's Docker context while isolating registry credentials.

Add a Proxy Manager host for the configured DOMUS_PUBLIC_URL, forwarding to domus-portal:80 with HTTPS. No Domus host port is allocated. Four Domus named volumes retain files, keys and RabbitMQ; preserve them alongside database backups. Image tags are independently configurable and stable by default. To update them, run install with update_images enabled.

Additional regression checks: `bash scripts/test-postgres-storage.sh` and `python3 scripts/test-domus-integration.py`. The latter tests published images against a temporary PostgreSQL 15 fixture and deletes only that test project; local-source validation can use DOMUS_TEST_LOCAL_IMAGES=true.
