# Operations

## Validate and deploy

Copy [`.env.example`](../.env.example) to `.env` without overwriting an existing file, restrict it to mode 0600 and fill the required values. Follow the [first-boot steps](../readme.md#local-compose) for registry login, Gitea registration and OpenClaw onboarding. Run from the repository root with that configured `.env`. For an initial Tempo install or schema update, complete the [Tempo migration procedure](tempo.md#schema-updates-and-manual-installation) before `up`:

```bash
docker compose --env-file .env -f services/compose.yaml config --quiet
docker compose --env-file .env -f services/compose.yaml up -d
```

## Logs and shutdown

```bash
docker compose --env-file .env -f services/compose.yaml logs -f
docker compose --env-file .env -f services/compose.yaml down
```

Append a service name to `logs` to inspect just that service.

## GitHub deployment

Open **Actions → Deploy Home Lab → Run workflow** and choose `install` or `uninstall`. The workflow runs scripts that invoke Docker Compose on the self-hosted runner labeled `beelink`, which needs Python 3 and Docker Compose v2 supporting `up --wait --wait-timeout`. Deployments to that host are serialized. All workflow executable code live in `.github/scripts/`; the workflow YAML defines step ordering, conditions, actions and script calls. Run these scripts from the repository root with the workflow environment variables configured.

Configure the selected GitHub environment with the variables mapped in the workflow, including every configuration variable referenced by Compose and `COMPOSE_FILE`. Store `GITEA_RUNNER_REGISTRATION_TOKEN`, `OPENCLAW_GATEWAY_TOKEN`, `TRANSMISSION_USERNAME`, `TRANSMISSION_PASSWORD`, and optional Digger RPC credential overrides as secrets. Also configure the four required `TEMPO_*_PASSWORD` secrets listed in [Tempo credentials](tempo.md#credentials); reuse the existing values for initialized databases. `GH_PAT` is required for the private cross-repository Tempo packages and must also have access to Digger; `GH_USER` defaults to the workflow actor when omitted.

Every run, including dry-run and uninstall, validates the four Tempo secrets and Compose configuration. A dry run does not check host prerequisites, registry access, database logins or application readiness, and does not change containers. Install optionally pulls images and stops services, then stops the Tempo portal/API, starts PostgreSQL, waits for its health check, verifies both database logins and applies migrations using a temporary API container. It starts the full stack only after migration succeeds, without waiting for application readiness. PostgreSQL has a TCP readiness health check; other container health checks, including image-provided checks, are disabled. Startup dependencies enforce container order. Migrations run in the workflow, not through a Compose service or normal API startup. Uninstall runs `docker compose down`, preserving bind-mounted data and named volumes. The workflow reads only GitHub variables and injected secrets, validates the names required by Compose, and exports settings through `GITHUB_ENV`. It neither reads `.env.example` nor generates a repository `.env`; automatic loading of a runner’s local `.env` is disabled.

### Workflow scripts

All scripts run from the repository root. For local use, set `COMPOSE_FILE=services/compose.yaml` in `.env`; its example placeholder is intentionally empty. Scripts inherit exported environment variables; Bash does not load `.env` automatically. Compose reads the root `.env` itself, but `validate-tempo-secrets.sh` needs the four passwords exported in its environment. `prepare-config.py` is for GitHub Actions: it requires `GITHUB_ENV` and reads repository/environment variables from the workflow’s `HOMELAB_VARIABLES_JSON`.

| Script in `.github/scripts/` | Purpose |
| --- | --- |
| `prepare-config.py` | Validate GitHub variables/secrets against Compose and export settings without printing their values |
| `validate-tempo-secrets.sh` | Validate the four required password values without printing them |
| `validate-compose.sh` | Validate Compose configuration without rendering secrets to logs |
| `check-host.sh` | Check the rootless Docker socket and runner registration token |
| `pull-images.sh` | Pull stack images when `update_images` is enabled |
| `stop-services.sh` | Stop the stack when `bring_down_first` is enabled |
| `migrate-tempo.sh` | Stop Tempo writers, wait for PostgreSQL, verify both database logins and apply migrations |
| `install-services.sh` | Start the stack after successful migration |
| `uninstall-services.sh` | Stop and remove stack containers and network, retaining host data |

The YAML retains ordering and conditions. `install-services.sh` alone does not migrate the database; run the migration procedure first. A migration or login failure stops deployment with the Tempo API and portal left stopped. Take and verify a backup before schema-changing releases; backup is not automated by these scripts.

Service configuration is versioned in `services/gitea-runner/config.yaml`, `services/tempo/init.sh`, `services/tempo/tempo-realm.json` and `services/vault/vault.hcl`. Compose mounts these files read-only from the checkout, with `create_host_path: false` so missing files fail rather than become directories. See the [Compose bind mount reference](https://docs.docker.com/reference/compose-file/services/#volumes). Do not place credentials in these files.

Prepare host storage ownership and permissions before installation. Cleanup of previous Compose projects is manual. Tempo has its own Keycloak realm; SSO for other applications remains unconfigured; see [Authentication options](authentication.md).

## Configuration changes

[`.env.example`](../.env.example) lists empty placeholders for service environment settings and the pre-existing image-tag/socket UID variables. Fill these values for local use; GitHub deployment does not read this file. Compose references these without fallback values. Ports, network addresses, host paths, resource limits and image versions remain in Compose. Keep the template’s keys when creating `.env`; merge new keys when upgrading without replacing existing passwords.

GitHub Actions does not read this example. `prepare-config.py` discovers required variable names in the configured Compose file and reads their values only from GitHub variables and injected secrets. Missing required values stop deployment. Injected nonempty secrets take precedence over variables; optional Digger credentials fall back to the configured Transmission credentials. Keep credentials in GitHub secrets, not variables. Internal database names, connection URLs and container paths must stay consistent with Compose, database initialization and the migration script if changed.

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

Transmission and Digger share `/downloads/complete/movies`, backed by `/mnt/ssd/transmission/downloads/complete/movies`; Jellyfin sees those movies at `/data/movies`. Existing Transmission torrents keep their saved download paths; check them before moving any existing media. Configure Jellyfin's movie library for `/data/movies`.

## Moving everything to the Beelink

The workflow changes containers only on Beelink. Before deploying the full stack, back up and copy each service's data from its old host to the corresponding directory under the Beelink home directory, preserving ownership. Stop and remove old containers on other hosts explicitly after verifying the migration; the workflow cannot reach or clean them up.

Keep downloads and media directories together when copying. Update Nginx Proxy Manager routes to Beelink's LAN DNS/IP and published ports. Because rootless Docker cannot bind privileged host ports, configure router port forwarding from external TCP `80` to Beelink TCP `8088` and external TCP `443` to Beelink TCP `8448`. Access the Nginx Proxy Manager admin UI on Beelink port `8181`. The selected GitHub environment supplies runtime configuration; ensure values such as socket user IDs match Beelink.

## Migration from the four previous projects

Stop and remove old service containers on Beelink manually before starting the full `lab` stack. Complete storage migration before deployment.

Stop the existing services before copying data, preserving ownership and permissions:

- Move `~/agentic/npm` to `~/npm`.
- Move `~/tools/portainer` to `~/portainer`.
- Move `~/media/jellyfin` to `~/jellyfin`.
- Move `~/media/transmission` to `~/transmission/config` and `~/media/watch` to `~/transmission/watch`. Migrate the download tree from `~/media/data` to `/mnt/ssd/transmission/downloads`, preserving the `complete/movies` layout and checking existing torrent paths.

Keep a backup until the new stack is verified. Container mount targets remain unchanged, including the shared `/downloads` path and Jellyfin's `/data` path.

For manual migration, stop and remove the containers belonging to those four previous projects and any old Portainer Agent before starting the full stack. Do not delete their volumes. Their old networks can remain unused.

## Transmission peer port

Transmission publishes fixed TCP/UDP port `51413`. Change Compose mappings and the server listener configuration together if another port is needed.

## Retired services

Hermes, Kanbada, Prowlarr, Radarr, pgAdmin and Honcho are not part of the current stack. Keycloak and PostgreSQL are active Tempo dependencies. Workflow installs preserve orphaned containers and their data; retire unwanted services explicitly. Containers from older projects or other hosts require manual cleanup. Unused `POSTGRES_*`, `HERMES_*`, `KANBADA_*`, `KC_*`, `OPENCODE_API_KEY`, `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD`, and `HOMEASSISTANT_DB_URL` settings can be removed from the selected GitHub environment if no other workflow uses them. Preserve retired service data until you decide it is no longer needed.

If the earlier Recorder PostgreSQL configuration was applied on Beelink, remove `recorder: !include recorder-postgres.yaml` or the PostgreSQL `db_url` override from Home Assistant configuration before deployment. Home Assistant will then use its default SQLite database.

Transmission’s service currently passes only timezone, UID/GID and umask settings. Its authentication and allowlists must be configured in the persisted Transmission configuration; the workflow’s `TRANSMISSION_USERNAME` and `TRANSMISSION_PASSWORD` mappings supply Digger’s fallback RPC client credentials but are not forwarded to the Transmission server. Verify that the server credentials match and its RPC allowlists permit Digger.
