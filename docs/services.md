# Services

## Agentic stack

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

## Media stack

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
