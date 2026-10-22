# Services

The main stack is defined in [services/compose.yaml](../services/compose.yaml). The [Gitea Actions runner](gitea-runner.md) has a separate [Compose project](../services/gitea-runner.yaml) for independent deployment.

All services run together on Beelink in the `lab` Compose project.

## Host ports

| Service | Host → container ports |
| --- | --- |
| Gitea | `3000 → 3000` HTTP, `3001 → 2222` SSH |
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
| Digger / Tempo migration | No network listener; no port mapping |

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
| `tempo-keycloak` | `10.203.0.27` |
| `gitea-runner` | `10.203.0.31` |

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
- SQLite persists in `~/digger`. Digger and Transmission both mount `/mnt/ssd/transmission/downloads/complete/movies` at `/movies`; Transmission also sees it at `/downloads/complete/movies`, and Jellyfin sees it at `/data`. The matching `/movies` paths let Digger submit download directories directly to Transmission.
- Exclude Digger's `.downloads` and `.state` directories from Jellyfin scans.
- Defaults for the 1 TB SSD (about 931 GiB usable before filesystem overhead): 750 GiB movie budget and 51200 MiB (50 GiB) free-space reserve. The budget includes partial downloads; the reserve protects free space across the drive when other services use it. Override with `DIGGER_MAX_LIBRARY_SIZE_GB` and `DIGGER_DISK_RESERVE_MB`; `DIGGER_YTS_API_URL` overrides discovery and must retain the `{0}` page placeholder.
- Digger uses existing `TRANSMISSION_USERNAME` and `TRANSMISSION_PASSWORD` environment settings for RPC authentication. Set `DIGGER_TRANSMISSION_USERNAME` and `DIGGER_TRANSMISSION_PASSWORD` to override them. Its RPC allowlists must admit Digger on `homelab_static`.

All three media applications run as container UID/GID `0:0`: Digger uses `user: "0:0"`, while the LinuxServer images use `PUID=0` and `PGID=0` with their default init entrypoints. With [rootless Docker UID mapping](https://docs.docker.com/engine/security/rootless/uid-gid-mapping/), this maps to the host account running the Docker daemon. Do not substitute the host UID (such as 1000) here: that maps to a subordinate host UID. Transmission and Jellyfin use `UMASK=002`. The obsolete `TRANSMISSION_PUID` and `DIGGER_MEDIA_GID` variables are no longer used.

Before startup, follow [media storage preparation](operations.md#rootless-media-storage). Compose cannot repair existing host ownership or a read-only SSD mount.

After preparing storage and the existing stack's `.env`, install only Digger and its Transmission dependency:

```bash
docker compose --env-file .env -f services/compose.yaml config --quiet
docker compose --env-file .env -f services/compose.yaml pull digger
docker compose --env-file .env -f services/compose.yaml up -d digger
docker compose --env-file .env -f services/compose.yaml logs --tail 100 digger
```

## Platform services

### `npm`
- Image: `jc21/nginx-proxy-manager:latest`
- Purpose: reverse proxy and HTTPS termination
- Host ports: `8088` (HTTP), `8448` (HTTPS), and `8181` (admin UI, container port `81`). Open the admin UI at `http://<beelink-lan-address>:8181`.

### `portainer`

- Image: `portainer/portainer-ce:latest`
- Published ports: `9000/tcp`, `9443/tcp`

Nginx Proxy Manager publishes host ports `8088/tcp` (container `80`), `8181/tcp` (container `81`), and `8448/tcp` (container `443`). Jellyfin also publishes discovery on `7359/udp`. Transmission's TCP/UDP peer port follows `TRANSMISSION_PEER_PORT` (default `51413`).

### `n8n`

- Image: `n8nio/n8n:latest`
- Keep `~/n8n/data` mounted: it holds the default SQLite database, encryption key, and other instance settings.
- Published port: `5678`.

## Database storage

Nginx Proxy Manager, n8n, Home Assistant Recorder and Jellyfin keep their default SQLite databases. OpenClaw retains local storage, Portainer uses BoltDB, and Transmission uses configuration and torrent state files.

For authentication and optional centralized login, see [Authentication options](authentication.md).

## Tempo

See [Tempo deployment](tempo.md) for private-image PAT access, fresh installation, credentials and HTTPS routes. API and PostgreSQL use a separate private network.
