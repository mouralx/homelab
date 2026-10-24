# Services

All services, including Tempo and the [Gitea Actions runner](gitea-runner.md), are defined in [services/compose.yaml](../services/compose.yaml) under the `lab` project.

All services run together on Beelink in the `lab` Compose project.

Ports, addresses, storage paths and image versions below are defined in Compose. Service environment settings and the existing image tag overrides are documented in [`.env.example`](../.env.example).

## Host ports

| Service | Host → container ports |
| --- | --- |
| Gitea | `3000 → 3000` HTTP, `3001 → 22` SSH |
| OpenClaw | `18789 → 18789` |
| Nginx Proxy Manager | `8088 → 80`, `8448 → 443`, `8181 → 81` |
| n8n | `5678 → 5678` |
| Transmission | `9091 → 9091`, `51413 → 51413` TCP/UDP |
| Jellyfin | `8096 → 8096` TCP, `7359 → 7359` UDP |
| Portainer | `9000 → 9000`, `9443 → 9443` |
| Home Assistant | `8123 → 8123` |
| Tempo portal | `8080 → 8080` |
| Tempo Keycloak | `8081 → 8080` |
| Tempo API | `8082 → 8080` |
| Tempo PostgreSQL | `127.0.0.1:5433 → 5432` |
| Gitea runner cache | `127.0.0.1:8089 → 8088` |
| Vault | `127.0.0.1:8200 → 8200` |
| Digger | No network listener; no port mapping |

Ports are TCP unless specified otherwise. Loopback mappings are reachable only from Beelink. The remaining mappings bind on all host interfaces. Tempo sign-in continues to use the configured HTTPS domains.

## Container addresses

The `homelab_static` bridge uses subnet `10.203.0.0/24`. Service-to-service calls can use these addresses or the Compose service names:

| Service | Container IP |
| --- | --- |
| `openclaw` | `10.203.0.10` |
| `npm` | `10.203.0.12` |
| `n8n` | `10.203.0.20` |
| `transmission` | `10.203.0.21` |
| `jellyfin` | `10.203.0.22` |
| `portainer` | `10.203.0.23` |
| `digger` | `10.203.0.24` |
| `homeassistant` | `10.203.0.25` |
| `tempo-portal` | `10.203.0.26` |
| `keycloak` | `10.203.0.27` |
| `vault` | `10.203.0.28` |
| `tempo-api` | `10.203.0.29` |
| `gitea` | `10.203.0.30` |
| `gitea-runner` | `10.203.0.31` |
| `postgres` | `10.203.0.33` |

Make sure `10.203.0.0/24` is unused on Beelink's LAN, VPN, and Docker networks before deployment. Home Assistant is on the bridge to receive a fixed address; automatic mDNS/broadcast discovery may be limited. Its web interface remains published on host port `8123`.

## AI services

### `openclaw`

- Image: `ghcr.io/openclaw/openclaw:latest`
- Purpose: AI agent gateway and Control UI
- Port: `18789`
- Persistent configuration and workspace: `~/openclaw`; auth-profile secrets: `~/openclaw/auth-profile-secrets`.
- Optional `OPENCLAW_GATEWAY_TOKEN` overrides the token saved during onboarding. For GitHub deployments, store it as an environment secret.
- The gateway uses `--allow-unconfigured` to bypass the missing-config startup guard during bootstrap. This does not create configuration or configure a model provider. Set `OPENCLAW_GATEWAY_TOKEN` before starting an unconfigured gateway, since LAN binding requires authentication.

Before first startup, create both host directories and make them writable by the image's `node` user (UID/GID `1000:1000`; account for UID mapping when using rootless Docker). From the repository root, complete onboarding with your model provider credentials:

```bash
docker compose --env-file .env -f services/compose.yaml run --rm --no-deps --entrypoint node openclaw dist/index.js onboard --mode local --no-install-daemon
```

Configure `gateway.controlUi.allowedOrigins` in `~/openclaw/openclaw.json` for the exact HTTPS origin used to access the dashboard through Nginx Proxy Manager. Then start the stack. See the [official Docker setup documentation](https://docs.openclaw.ai/install/docker) for pairing and configuration details.

## Home services

### `homeassistant`
- Image: `ghcr.io/home-assistant/home-assistant:stable`
- Purpose: home automation and device management
- Uses bridge networking with fixed container address `10.203.0.25`, exposed on host port `8123`. Automatic mDNS/broadcast discovery may be limited; add integrations manually or configure a discovery relay.

### `transmission`
- Image: `lscr.io/linuxserver/transmission:latest`
- Purpose: torrent client
- Exposed on ports `9091`, `51413/tcp`, `51413/udp`

### `jellyfin`
- Image: `lscr.io/linuxserver/jellyfin:latest`
- Purpose: media playback
- Exposed on port `8096`

### `digger`

- Image: `ghcr.io/mouralx/digger:stable`; override with `DIGGER_TAG` for a version or preview.
- Purpose: discover movies, download through Transmission, and manage a movie storage budget. No published ports.
- SQLite persists in `~/digger`. Digger mounts `/mnt/ssd/transmission/downloads/complete/movies` at `/downloads/complete/movies`; Transmission sees the same path through its `/downloads` mount, and Jellyfin sees the movie directory at `/data/movies`. The matching download paths let Digger submit directories directly to Transmission.
- Exclude Digger's `.downloads` and `.state` directories from Jellyfin scans.
- Defaults for the 1 TB SSD (about 931 GiB usable before filesystem overhead): 750 GiB movie budget and 51200 MiB (50 GiB) free-space reserve. The budget includes partial downloads; the reserve protects free space across the drive when other services use it. Override with `DIGGER_MAX_LIBRARY_SIZE_GB` and `DIGGER_DISK_RESERVE_MB`; `DIGGER_YTS_API_URL` overrides discovery and must retain the `{0}` page placeholder.
- Digger uses `TRANSMISSION_USERNAME` and `TRANSMISSION_PASSWORD` as fallback values for its RPC client credentials. Set `DIGGER_TRANSMISSION_USERNAME` and `DIGGER_TRANSMISSION_PASSWORD` to override them. Configure the Transmission server’s matching credentials and RPC allowlists separately: the current Compose service does not pass these credential variables to Transmission.

All three media applications run as container UID/GID `0:0`: Digger uses `user: "0:0"`, while the LinuxServer images use `PUID=0` and `PGID=0` with their default init entrypoints. With [rootless Docker UID mapping](https://docs.docker.com/engine/security/rootless/uid-gid-mapping/), this maps to the host account running the Docker daemon. Do not substitute the host UID (such as 1000) here: that maps to a subordinate host UID. Transmission and Jellyfin use `UMASK=002`. The obsolete `TRANSMISSION_PUID` and `DIGGER_MEDIA_GID` variables are no longer used.

Before startup, follow [media storage preparation](operations.md#rootless-media-storage). Compose cannot repair existing host ownership or a read-only SSD mount.

After preparing storage and the existing stack's `.env`, start Transmission and Digger:

```bash
docker compose --env-file .env -f services/compose.yaml config --quiet
docker compose --env-file .env -f services/compose.yaml pull digger
docker compose --env-file .env -f services/compose.yaml up -d transmission digger
docker compose --env-file .env -f services/compose.yaml logs --tail 100 digger
```

## Platform services

### `gitea` and `gitea-runner`

Gitea uses `docker.gitea.com/gitea:28.2.0-rootless`, with data under `~/gitea/data` and configuration under `~/gitea/config`. Its Actions runner stores registration and cache data under `~/gitea/runner/data` and mounts `services/gitea-runner/config.yaml` read-only. See [runner setup](gitea-runner.md).

### `vault`

- Image: `hashicorp/vault:2.2.1`, running in server mode with the UI enabled.
- Single-node Raft storage persists in `~/vault/data`.
- Host API access is limited to localhost port `8200`; Nginx Proxy Manager can reach `http://vault:8200` on `homelab_static`.
- See [Vault setup and recovery](vault.md) for HTTPS routing, initialization, unsealing, and backups.

### `npm`
- Image: `jc21/nginx-proxy-manager:latest`
- Purpose: reverse proxy and HTTPS termination
- Host ports: `8088` (HTTP), `8448` (HTTPS), and `8181` (admin UI, container port `81`). Open the admin UI at `http://<beelink-lan-address>:8181`.

### `portainer`

- Image: `portainer/portainer-ce:latest`
- Published ports: `9000/tcp`, `9443/tcp`

Nginx Proxy Manager publishes host ports `8088/tcp` (container `80`), `8181/tcp` (container `81`), and `8448/tcp` (container `443`). Jellyfin also publishes discovery on `7359/udp`. Transmission publishes fixed TCP/UDP port `51413`.

### `n8n`

- Image: `n8nio/n8n:latest`
- Keep `~/n8n/data` mounted: it holds the default SQLite database, encryption key, and other instance settings.
- Published port: `5678`.

## Database storage

Nginx Proxy Manager, n8n, Home Assistant Recorder and Jellyfin use their default local databases unless their persistent configuration overrides those defaults. Tempo and Keycloak use PostgreSQL with separate roles and databases; see [PostgreSQL access](tempo.md#postgresql-access). Gitea database settings live in its persistent configuration. OpenClaw retains local storage, Portainer uses BoltDB, and Transmission uses configuration and torrent state files.

For authentication and optional centralized login, see [Authentication options](authentication.md).

## Tempo

See [Tempo deployment](tempo.md) for private-image PAT access, fresh installation, credentials and HTTPS routes. All Tempo services share `homelab_static` with the rest of the stack. The `keycloak` and `postgres` services retain DNS aliases `tempo-keycloak` and `tempo-postgres` for existing application URLs.
