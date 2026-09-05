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

Open **Actions → Deploy Home Lab → Run workflow** and select the host and environment. Choose `install` (also updates existing services) or `uninstall`, then enter service names separated by commas or spaces in `services`, or enter `all` for the whole stack. For example, `sonarr,jellyfin` installs Sonarr and Jellyfin plus Sonarr's dependencies, Prowlarr and Transmission. See [the service inventory](services.md) for names.

Install automatically includes all transitive `depends_on` dependencies from Compose and respects their startup/healthcheck conditions. For example, `honcho` includes `postgres` and `lms`; `keycloak` includes `postgres`. Image updates, builds, and optional shutdown apply to the selection including dependencies. Shared dependencies may therefore restart during an update.

Uninstall stops and removes only the selected containers, preserving stored data, images, and volumes. It leaves dependencies installed. Removing a dependency is blocked if another installed service still needs it, including stopped containers and services in legacy projects. For example, removing `postgres` while Honcho or Keycloak is installed requires selecting those dependent services too, or uninstalling them first. Services omitted from a selection are left installed; each workflow run is an action, not a saved desired-state list.

A dry run generates the environment file, validates configuration and dependency safety, and displays the resolved selection in the job summary without changing containers. Real installs pull images when requested and build before removing selected legacy containers. The workflow removes its generated `.env` file even on failure and does not prune Docker volumes or unrelated containers.

The runner needs Docker Compose v2, Bash, and jq. Local regression checks run with `bash scripts/test-validate-compose.sh` and `python3 scripts/test-manage-services.py` (Python 3; uses a fake Docker CLI).

## Migration from the four previous projects

Real workflow installs remove selected services (including their dependencies) carrying Compose project labels `agentic`, `agents`, `media`, or `tools` before starting them in `lab`. This causes a brief interruption. It does not relocate stored data. Use `all` for the initial migration so dependent services move to the new network together. Partial migrations are blocked if an installed legacy dependent would be left behind when its dependency moves. Complete the storage migration below before deployment so services do not start with empty data directories.

Stop the existing services before copying data, preserving ownership and permissions:

- Move `~/agentic/hermes`, `~/agentic/npm`, `~/agentic/lms`, `~/agentic/keycloak`, and `~/agentic/postgres` to their corresponding `~/<service-name>` directories.
- Move `~/tools/portainer` and `~/tools/pgadmin` to `~/portainer` and `~/pgadmin`.
- Move `~/media/prowlarr`, `~/media/sonarr`, `~/media/radarr`, and `~/media/jellyfin` to their corresponding `~/<service-name>` directories.
- Move `~/media/transmission` to `~/transmission/config`, `~/media/data` to `~/transmission/downloads`, and `~/media/watch` to `~/transmission/watch`.
- Copy the contents of the old `agents_portainer` Docker volume into `~/portainer_agent`.

Keep a backup until the new stack is verified. Container mount targets remain unchanged, including the shared `/downloads` path and Jellyfin's `/data` path.

For manual migration, stop and remove the containers belonging to those four previous projects before running the new stack. Do not delete their volumes. Their old networks can remain unused. Nginx Proxy Manager routes that use container IP addresses should be updated to use service names because recreated containers can receive new IPs.

## Transmission peer port

`TRANSMISSION_PEER_PORT` defaults to `51413` and controls both the internal listener and the published TCP/UDP ports. Choose a value not published by another service; validation rejects collisions.

## Reset Honcho after the embedding backend change

Honcho uses LM Studio's `text-embedding-nomic-embed-text-v1.5` with 768 dimensions. An existing 1536-dimensional Honcho schema must be recreated to use those vectors. This deletes Honcho's stored memory; Keycloak's separate database is unaffected.

Keep Postgres running while stopping Honcho:

```bash
docker compose --env-file .env -f services/compose.yaml stop honcho
docker exec postgres psql -U "$POSTGRES_USER" -d postgres \
  -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
docker compose --env-file .env -f services/compose.yaml up -d honcho
```

Set `POSTGRES_USER` in your shell before running the reset. The LM Studio entrypoint fetches `nomic-embed-text` on first boot and uses CPU when no supported GPU is available.
