# Services

All services are defined in [services/compose.yaml](../services/compose.yaml).

## Agentic services

### `hermes`
- Container: `nousresearch/hermes-agent:latest`
- Purpose: AI gateway and agent access layer
- Ports: `8642`, `9119`

### `honcho`
- Build: `images/dockerfile.honcho`
- Purpose: agent runtime and backend logic
- Depends on Postgres
- Exposed on port `8000`

### `lms`
- Build: `images/dockerfile.lms`
- Purpose: local model runtime
- Exposed on port `4321`

### `keycloak`
- Image: `quay.io/keycloak/keycloak:26.7.1`
- Purpose: identity and access management
- Exposed on port `8080`

### `postgres`
- Image: `pgvector/pgvector:pg15`
- Purpose: shared database for agentic and identity services

### `npm`
- Image: `jc21/nginx-proxy-manager:latest`
- Purpose: reverse proxy and HTTPS termination

## Media services

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

## Monitoring and tools

### `portainer_agent`

- Image: `portainer/agent:2.39.6`
- Container name: `portainer_agent`
- Published port: `9001/tcp`
- Data directory: `~/portainer_agent`

### `portainer`

- Image: `portainer/portainer-ce:latest`
- Published ports: `9000/tcp`, `9443/tcp`

### `pgadmin`

- Image: `dpage/pgadmin4:latest`
- Published ports: `4431/tcp` to container `443`, `8001/tcp` to container `80`

Postgres publishes `5432/tcp`; Nginx Proxy Manager publishes `80/tcp`, `81/tcp`, and `443/tcp`. Jellyfin also publishes discovery on `7359/udp`. Transmission's TCP/UDP peer port follows `TRANSMISSION_PEER_PORT` (default `51413`).
