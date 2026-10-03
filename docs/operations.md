# Operations

## Validate, build, and deploy

Run from the repository root with a configured `.env`:

```bash
docker compose --env-file .env -f services/compose.yaml config --format json | bash scripts/validate-compose.sh
docker compose --env-file .env -f services/compose.yaml up -d --build
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

Install manages all services and respects Compose startup and healthcheck conditions. Image updates, builds, and optional shutdown apply to the full stack.

Uninstall stops and removes all stack containers, preserving stored data, images, and volumes. Each workflow run is an action, not a saved desired-state list.

A dry run generates the environment file, validates configuration and dependency safety, and displays the resolved selection in the job summary without changing containers. Real installs pull images when requested and build before removing selected legacy containers. The workflow removes its generated `.env` file even on failure and does not prune Docker volumes or unrelated containers.

Local regression checks are available with `bash scripts/test-validate-compose.sh` and `bash scripts/test-manage-services.sh` (uses a fake Docker CLI).

## Moving everything to the Beelink

The workflow only changes containers on Beelink. Before deploying the full stack, back up and copy each service's data from the old host to the corresponding directory under the Beelink home directory, preserving ownership. Stop and remove old containers on other hosts explicitly after verifying the migration.

Keep downloads and media directories together when copying. Update Nginx Proxy Manager routes to Beelink's LAN DNS/IP and published ports. Set Hermes's Keycloak issuer to the externally reachable HTTPS URL. The selected GitHub environment supplies runtime configuration; ensure values such as socket user IDs match Beelink.

## Migration from the four previous projects

Real workflow installs remove selected services (including their dependencies) carrying Compose project labels `agentic`, `agents`, `media`, or `tools` on the target host before starting them in `lab`. This causes a brief interruption. It does not relocate stored data or remove containers on other hosts. Partial migrations are blocked if an installed legacy dependent would be left behind when its dependency moves. Complete the storage migration below and any cross-host transfer before deployment so services do not start with empty data directories.

Stop the existing services before copying data, preserving ownership and permissions:

- Move `~/agentic/hermes`, `~/agentic/npm`, `~/agentic/keycloak`, and `~/agentic/postgres` to their corresponding `~/<service-name>` directories.
- Move `~/tools/portainer` and `~/tools/pgadmin` to `~/portainer` and `~/pgadmin`.
- Move `~/media/prowlarr`, `~/media/sonarr`, `~/media/radarr`, and `~/media/jellyfin` to their corresponding `~/<service-name>` directories.
- Move `~/media/transmission` to `~/transmission/config`, `~/media/data` to `~/transmission/downloads`, and `~/media/watch` to `~/transmission/watch`.
- Copy the contents of the old `agents_portainer` Docker volume into `~/portainer_agent`.

Keep a backup until the new stack is verified. Container mount targets remain unchanged, including the shared `/downloads` path and Jellyfin's `/data` path.

For manual migration, stop and remove the containers belonging to those four previous projects before starting the full stack. Do not delete their volumes. Their old networks can remain unused.

## Transmission peer port

`TRANSMISSION_PEER_PORT` defaults to `51413` and controls both the internal listener and the published TCP/UDP ports. Choose a value not published by another service; validation rejects collisions.

## Honcho retirement

Honcho is no longer part of the home lab stack. Hermes uses local Holographic memory instead; it does not require the retired Honcho service.

Preserve existing PostgreSQL data, including any historical Honcho schemas and Keycloak's database. Do not drop schemas, reset Postgres, or delete `~/postgres` as part of this retirement. The former embedding-backend reset procedure is obsolete and must not be run. Retiring Honcho does not migrate its stored memories into local Holographic memory.
