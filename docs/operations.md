# Operations

## Validate, build, and deploy

Run from the repository root with a configured `.env`:

```bash
docker compose --env-file .env -f services/compose.yaml --profile '*' config --format json | bash scripts/validate-compose.sh
docker compose --env-file .env -f services/compose.yaml --profile ai up -d --build
```

The validator rejects duplicate container names and overlapping published ports, including overrides of `TRANSMISSION_PEER_PORT`. TCP and UDP may use the same number. It checks the Compose configuration; other processes on the target host can still occupy a published port.

## Logs and shutdown

```bash
docker compose --env-file .env -f services/compose.yaml --profile ai logs -f
docker compose --env-file .env -f services/compose.yaml --profile ai down
```

Replace `ai` with `media` or `management` as needed. Append a service name to `logs` to inspect just that service.

## GitHub deployment

Open **Actions → Deploy Home Lab → Run workflow**. Choose the environment and `install` (also updates existing services) or `uninstall`, then check **AI**, **Media**, and/or **Management**. All checkboxes start unchecked; select at least one. Service membership comes directly from Compose. See [the profile inventory](services.md#workload-profiles).

Profile names describe workloads. Runner routing is defined separately in `scripts/workflow.step.select-profiles.sh`: `ai` → `beelink`, `media` → `rpi5`, and `management` → `rpi4`. Each selected profile runs a job on its mapped host. Change that mapping to relocate a workload without renaming the Compose profile.

Assign distinct labels to your self-hosted runners: `beelink`, `rpi5`, and `rpi4`. The previous generic `rpi` label is no longer used for routing. Only give a machine its own host label; an offline or missing runner leaves its job queued. The selection/test job uses GitHub's `ubuntu-latest` runner. Deployments to each host are serialized, even if multiple profiles are mapped to it, and one machine failing does not cancel the other selected machines.

Install automatically includes all transitive `depends_on` dependencies from Compose and respects their startup/healthcheck conditions. For example, `honcho` includes `postgres` and `lms`; `keycloak` includes `postgres`. Image updates, builds, and optional shutdown apply to the selection including dependencies. Shared dependencies may therefore restart during an update.

Uninstall stops and removes all containers in the selected profile, including that profile's dependencies, preserving stored data, images, and volumes. Removal is blocked if another installed service outside the profile still needs one of those dependencies, including stopped containers and services in legacy projects. Services outside the profile are left installed. Each workflow run is an action, not a saved desired-state list.

A dry run generates the environment file, validates configuration and dependency safety, and displays the resolved selection in the job summary without changing containers. Real installs pull images when requested and build before removing selected legacy containers. The workflow removes its generated `.env` file even on failure and does not prune Docker volumes or unrelated containers.

The runner needs Docker Compose v2, Bash, and jq. Local regression checks run with `bash scripts/test-select-profiles.sh`, `bash scripts/test-validate-compose.sh`, and `bash scripts/test-manage-services.sh` (uses a fake Docker CLI).

## Moving from a single host to three machines

Profiles do not transfer data or remove services from their old hosts. Stop a service on its old host, back up and copy its data to the corresponding directory on the new host with ownership preserved, then install its new workload profile. Remove obsolete containers from the old host explicitly after verifying the migration; starting a profile does not remove other installed services.

Keep Postgres and both database consumers on Beelink. Keep downloads and media directories together on Pi 5. Update Nginx Proxy Manager routes to the destination host's LAN DNS/IP and published port. Set Hermes's Keycloak issuer to the externally reachable HTTPS URL. Add the Beelink and Pi 5 agent endpoints to Portainer on Pi 4. Use 64-bit Linux on both Pis and verify ARM64 image support before deployment. The selected GitHub environment supplies runtime configuration; make sure host-specific values such as socket user IDs match your runners.

## Migration from the four previous projects

Real workflow installs remove selected services (including their dependencies) carrying Compose project labels `agentic`, `agents`, `media`, or `tools` on the target host before starting them in `lab`. This causes a brief interruption. It does not relocate stored data or remove containers on other hosts. Partial migrations are blocked if an installed legacy dependent would be left behind when its dependency moves. Complete the storage migration below and any cross-host transfer before deployment so services do not start with empty data directories.

Stop the existing services before copying data, preserving ownership and permissions:

- Move `~/agentic/hermes`, `~/agentic/npm`, `~/agentic/lms`, `~/agentic/keycloak`, and `~/agentic/postgres` to their corresponding `~/<service-name>` directories.
- Move `~/tools/portainer` and `~/tools/pgadmin` to `~/portainer` and `~/pgadmin`.
- Move `~/media/prowlarr`, `~/media/sonarr`, `~/media/radarr`, and `~/media/jellyfin` to their corresponding `~/<service-name>` directories.
- Move `~/media/transmission` to `~/transmission/config`, `~/media/data` to `~/transmission/downloads`, and `~/media/watch` to `~/transmission/watch`.
- Copy the contents of the old `agents_portainer` Docker volume into `~/portainer_agent`.

Keep a backup until the new stack is verified. Container mount targets remain unchanged, including the shared `/downloads` path and Jellyfin's `/data` path.

For manual migration, stop and remove the containers belonging to those four previous projects before running the new profiles. Do not delete their volumes. Their old networks can remain unused. Nginx Proxy Manager routes across hosts must use host LAN DNS/IP addresses and published ports, rather than container IPs or Docker service names.

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
