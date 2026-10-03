# Services

All services are defined in [services/compose.yaml](../services/compose.yaml).

All services run together on Beelink in the `lab` Compose project.

## Fixed container addresses

The `homelab_static` bridge uses subnet `10.203.0.0/24`. Service-to-service calls can use these addresses or the Compose service names:

| Service | Container IP |
| --- | --- |
| `openclaw` | `10.203.0.10` |
| `hermes` | `10.203.0.11` |
| `npm` | `10.203.0.12` |
| `keycloak` | `10.203.0.13` |
| `kanbada-api` | `10.203.0.14` |
| `kanbada-portal` | `10.203.0.15` |
| `kanbada-worker` | `10.203.0.16` |
| `postgres` | `10.203.0.17` |
| `prowlarr` | `10.203.0.18` |
| `radarr` | `10.203.0.19` |
| `n8n` | `10.203.0.20` |
| `transmission` | `10.203.0.21` |
| `jellyfin` | `10.203.0.22` |
| `portainer` | `10.203.0.23` |
| `pgadmin` | `10.203.0.24` |
| `homeassistant` | `10.203.0.25` |

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

### `hermes`
- Container: `nousresearch/hermes-agent:latest`
- Purpose: AI gateway and agent access layer
- Ports: `8642`, `9119`
- Holographic memory is local to Hermes, under its existing `~/hermes` data mount; it does not require a Honcho service.

### `keycloak`
- Image: `quay.io/keycloak/keycloak:26.7.1`
- Purpose: identity and access management
- Exposed on port `8080`

### Kanbada

- `kanbada-api`, `kanbada-portal`, and `kanbada-worker` run on Beelink, using the published GHCR images.
- The API and Jira worker use the existing `postgres` service on the private Compose network. On startup, the API creates the `kanbada` database if needed and applies pending EF migrations. `POSTGRES_USER` and `POSTGRES_PASSWORD` are shared with the existing stack; the PostgreSQL role must be allowed to create databases and tables. Override the database with `KANBADA_POSTGRES_DB` if needed.
- The API is published on host port `5180` and the portal on `4173` by default. Set `KANBADA_API_PORT` and `KANBADA_PORTAL_PORT` to change them, and point Nginx Proxy Manager at the Beelink portal port.
- ASP.NET host filtering allows `kanbada.mouras.me` by default. Set `KANBADA_ALLOWED_HOSTS` to a semicolon-separated host list if the public hostname changes or additional hostnames are used.
- Set `KANBADA_PORTAL_ORIGIN` to the public HTTPS origin. Optional `KANBADA_PLATFORM_ADMIN_EMAILS`, `KANBADA_JIRA_ALLOWED_HOSTS`, and Google/Microsoft OAuth variables configure the application. `~/kanbada/data-protection` persists API and Jira encryption keys.
- Startup migration is enabled for this homelab deployment so a fresh database is initialized and upgrades are applied automatically.

### `postgres`
- Image: `pgvector/pgvector:pg15`
- Purpose: database for identity services; existing data is preserved

## Home services

### `homeassistant`
- Image: `ghcr.io/home-assistant/home-assistant:stable`
- Purpose: home automation and device management
- Uses bridge networking with fixed container address `10.203.0.25`, exposed on host port `8123`. Automatic mDNS/broadcast discovery may be limited; add integrations manually or configure a discovery relay.

### `prowlarr`
- Image: `lscr.io/linuxserver/prowlarr:latest`
- Purpose: indexer management
- Exposed on port `9696`

### `sonarr`
- Image: `lscr.io/linuxserver/sonarr:latest`
- Purpose: series management
- Exposed on port `8989`

### `radarr`
- Image: `lscr.io/linuxserver/radarr:latest`
- Purpose: movie management
- Exposed on port `7878`

### `transmission`
- Image: `lscr.io/linuxserver/transmission:latest`
- Purpose: torrent client
- Exposed on ports `9091`, `51413/tcp`, `51413/udp`

### `jellyfin`
- Image: `lscr.io/linuxserver/jellyfin:latest`
- Purpose: media playback
- Exposed on port `8096`

## Platform services

### `npm`
- Image: `jc21/nginx-proxy-manager:latest`
- Purpose: reverse proxy and HTTPS termination
- Host ports: `8088` (HTTP), `8448` (HTTPS), and `8181` (admin UI, container port `81`). Open the admin UI at `http://<beelink-lan-address>:8181`.

### `portainer`

- Image: `portainer/portainer-ce:latest`
- Published ports: `9000/tcp`, `9443/tcp`

### `pgadmin`

- Image: `dpage/pgadmin4:latest`
- Published ports: `4431/tcp` to container `443`, `8001/tcp` to container `80`

Postgres publishes `5432/tcp`; Nginx Proxy Manager publishes host ports `8088/tcp` (container `80`), `8181/tcp` (container `81`), and `8448/tcp` (container `443`). Jellyfin also publishes discovery on `7359/udp`. Transmission's TCP/UDP peer port follows `TRANSMISSION_PEER_PORT` (default `51413`).
