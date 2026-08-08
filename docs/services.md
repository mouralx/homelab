# Services Reference

Complete documentation for all 15 Docker services in the home lab stack.

## Contents

- [digger](#digger)
- [transmission](#transmission)
- [jellyfin](#jellyfin)
- [n8n](#n8n)
- [npm (Nginx Proxy Manager)](#npm-nginx-proxy-manager)
- [keycloak](#keycloak)
- [postgres](#postgres)
- [homeassistant](#homeassistant)
- [hermes](#hermes)
- [llmster](#llmster)
- [honcho](#honcho)
- [owui (Open WebUI)](#owui-open-webui)
- [portainer](#portainer)
- [vault](#vault)
- [pgadmin](#pgadmin)

---

## digger

| Property | Value |
|---|---|
| IP | `10.51.0.2` |
| Profile | `media` |
| Image | Custom build (see `dockerfile.digger`) |
| Restart | `unless-stopped` |
| Depends on | `transmission` (condition: service_healthy) |
| Volumes | `${DIGGER_HOST_MOVIES_DIR}:/downloads/movies:rw`, `~/digger:/digger/data:rw` |

### Description

Custom .NET 10 worker service that discovers movies from the YTS API, manages their lifecycle in a SQLite database, and controls Transmission to automate downloads. This is the core automation engine of the media pipeline.

### Configuration

Environment variables for logging levels:
- `Logging__Level__Default`: `Warning`
- `Logging__Level__Microsoft`: `Warning`
- `Logging__LogLevel__Microsoft_Hosting_Lifetime`: `Warning`
- `Logging__LogLevel__Microsoft__EntityFrameworkCore__Database__Command`: `Warning`

Uses `appsettings.json` for runtime configuration, with Transmission credentials injected at deploy time.

### Key Paths

| Container Path | Purpose |
|---|---|
| `/digger/data/movies.db` | SQLite database |
| `/downloads/movies/` | Movie download directory |
| `/digger/Digger.Worker.dll` | Entry point |

---

## transmission

| Property | Value |
|---|---|
| IP | `10.51.0.15` |
| Profile | `media` |
| Image | Custom build (see `dockerfile.transmission`) |
| Restart | `unless-stopped` |

### Description

Custom Transmission daemon running on Ubuntu. Configured with authentication for the RPC interface. Manages torrent downloads to the shared media directory.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `TRANSMISSION_USERNAME` | RPC auth username | Yes |
| `TRANSMISSION_PASSWORD` | RPC auth password | Yes |

### Exposed Ports

| Port | Protocol | Purpose |
|---|---|---|
| 9091 | TCP | RPC interface |
| 51413 | TCP | Peer-to-peer |
| 51413 | UDP | DHT/PEX |

### Volumes

| Host Path | Container Path |
|---|---|
| `${TRANSMISSION_DOWNLOADS_DIR}` | `/downloads` |
| `${TRANSMISSION_INCOMPLETE_DIR}` | `/incomplete` |

### Healthcheck

HTTP GET to `http://localhost:9091/` every 30s, start period 15s.

---

## jellyfin

| Property | Value |
|---|---|
| IP | `10.51.0.5` |
| Profile | `media` |
| Image | `jellyfin/jellyfin` |
| Restart | `unless-stopped` |

### Description

Open-source media server that organizes, streams, and transcodes media content. Consumes movies downloaded by Transmission from the shared media directory.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `JELLYFIN_PublishedServerUrl` | Published server URL | Yes |

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/jellyfin/config` | `/config` | Server configuration |
| `~/jellyfin/cache` | `/cache` | Metadata and image cache |
| `${JELLYFIN_MEDIA_DIR}` | `/media` | Media library |

---

## n8n

| Property | Value |
|---|---|
| IP | `10.51.0.7` |
| Profile | `automation` |
| Image | `docker.n8n.io/n8nio/n8n:next` |
| Restart | `unless-stopped` |
| Depends on | `postgres` (condition: service_healthy) |

### Description

Advanced workflow automation platform with 400+ integrations, node-based visual programming, and webhook support. Configured with Postgres backend and HTTPS-only access.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `N8N_ENCRYPTION_KEY` | Workflow encryption key | Yes |
| `TIME_ZONE` | Timezone (shared across stack) | Yes |
| `N8N_HOST` | Hostname for webhook URLs | Yes |
| `POSTGRES_USER` | Database user | Yes |
| `POSTGRES_PASSWORD` | Database password | Yes |

### Database Configuration

- **Type**: PostgreSQL
- **Database**: `n8n`
- **Host**: `postgres` (Docker DNS)
- **Schema**: `public`

### Key Features

- Runner mode enabled for parallel execution
- Webhook URL set to HTTPS
- Editor base URL configured for reverse proxy

---

## npm (Nginx Proxy Manager)

| Property | Value |
|---|---|
| IP | `10.51.0.8` |
| Profile | \u2014 |
| Image | `jc21/nginx-proxy-manager:latest` |
| Restart | `unless-stopped` |

### Description

Web-based Nginx reverse proxy manager with built-in TLS termination, Let's Encrypt integration, and access controls. Serves as the single entry point for all web services in the stack.

### Environment Variables

| Variable | Description |
|---|---|
| `TIME_ZONE` | Container timezone |

### Exposed Ports

| Host Port | Container Port | Purpose |
|---|---|---|
| 80 | 80 | HTTP (redirects to HTTPS) |
| 443 | 443 | HTTPS traffic |
| 81 | 81 | Admin UI |

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/npm/data` | `/data` | Config, SSL certs, access logs |
| `~/npm/certs` | `/etc/letsencrypt` | Let's Encrypt certificates |

---

## keycloak

| Property | Value |
|---|---|
| IP | `10.51.0.16` |
| Profile | `identity` |
| Image | `quay.io/keycloak/keycloak:latest` |
| Restart | `unless-stopped` |
| Depends on | `postgres` (condition: service_healthy) |

### Description

Open-source identity and access management solution providing OIDC and SAML SSO. Configured for development mode with Postgres database backend.

### Environment Variables

| Variable | Description | Default |
|---|---|---|
| `KC_BOOTSTRAP_ADMIN_USERNAME` | Admin username | `admin` |
| `KC_BOOTSTRAP_ADMIN_PASSWORD` | Admin password | `admin` |
| `KC_PROXY_HEADERS` | Proxy forwarding headers | `xforwarded` |
| `KC_HTTP_ENABLED` | Allow HTTP | `false` |
| `KC_HOSTNAME` | Server hostname | Required |
| `POSTGRES_USER` | DB user | Required |
| `POSTGRES_PASSWORD` | DB password | Required |

### Database Configuration

- **Database**: `keycloak`
- **Host**: `postgres`
- **Username**: `${POSTGRES_USER}`

### Exposed Ports

| Host Port | Container Port |
|---|---|
| 8080 | 8080 |

### Storage

- `~/keycloak:/opt/keycloak/data` \u2014 Realm configuration, users, sessions

---

## postgres

| Property | Value |
|---|---|
| IP | `10.51.0.14` |
| Profile | \u2014 |
| Image | `pgvector/pgvector:pg15` |
| Restart | `unless-stopped` |

### Description

Shared PostgreSQL 15 database used by n8n, Keycloak, Honcho, and accessible via pgAdmin. Configured with 128MB shared memory and the pgvector extension support needed by Honcho for vector embeddings.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `POSTGRES_USER` | Database superuser | Yes |
| `POSTGRES_PASSWORD` | Database password | Yes |

### Storage

- `~/postgres/data:/var/lib/postgresql/data`

### Databases

The server creates a default `postgres` database. Services create their own databases:
- `n8n` \u2014 Workflow state
- `keycloak` \u2014 Realm configuration
- `honcho` \u2014 Memory and profile data (via Honcho)

---

## homeassistant

| Property | Value |
|---|---|
| IP | `10.51.0.4` |
| Profile | `home` |
| Image | `ghcr.io/home-assistant/home-assistant:stable` |
| Restart | `unless-stopped` |

### Description

Open-source home automation platform. Can be integrated with n8n for complex automation workflows.

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/homeassistant/config` | `/config` | Configuration |
| `/etc/localtime` | `/etc/localtime:ro` | Timezone sync |
| `/run/dbus` | `/run/dbus:ro` | Host system bus |

---

## hermes

| Property | Value |
|---|---|
| IP | `10.51.0.17` |
| Profile | `agentic` |
| Image | `nousresearch/hermes-agent:latest` |
| Restart | `unless-stopped` |
| Resources | 4GB RAM, 2 CPU cores |

### Description

AI agent gateway by Nous Research. Routes inference requests to local LLM backends (LLMster) and cloud API fallbacks (OpenCode). Provides a dashboard with OIDC authentication.

### Environment Variables

| Variable | Description |
|---|---|
| `API_SERVER_ENABLED` | Enable API server (`true`) |
| `API_SERVER_KEY` | API key for authentication |
| `HERMES_DASHBOARD` | Enable dashboard (`1`) |
| `HERMES_DASHBOARD_OIDC_ISSUER` | OIDC issuer URL |
| `HERMES_DASHBOARD_OIDC_CLIENT_ID` | OIDC client ID |
| `HERMES_DASHBOARD_PUBLIC_URL` | Public URL for dashboard |
| `OPENCODE_GO_API_KEY` | OpenCode Go API key |
| `OPENCODE_ZEN_API_KEY` | OpenCode Zen API key (fallback) |

### Exposed Ports

| Port | Purpose |
|---|---|
| 8642 | Agent gateway and dashboard |
| 9119 | Secondary interface |

### Resource Limits

- **Memory**: 4GB
- **CPUs**: 2.0

---

## llmster

| Property | Value |
|---|---|
| IP | `10.51.0.18` |
| Profile | `agentic` |
| Image | Custom build (see `dockerfile.llmster`) |
| Restart | `unless-stopped` |

### Description

Local LLM inference server running inside an LM Studio container. Exposes an OpenAI-compatible API on port 4321 for both chat completions and embeddings. Used by Hermes for agent inference and by Honcho for embedding generation.

### Environment Variables

| Variable | Description | Default |
|---|---|---|
| `LLM_MODEL` | Model to load at startup | `google/gemma-4-e2b` |

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/llmster` | `/root/.lmstudio/models` | Model files cache |

### Exposed Ports

| Host Port | Container Port |
|---|---|
| 4321 | 4321 |

### Healthcheck

HTTP GET to `http://localhost:4321/v1/models` every 30s, start period 120s.

---

## honcho

| Property | Value |
|---|---|
| IP | `10.51.0.19` |
| Profile | `agentic` |
| Image | Custom build (see `dockerfile.honcho`) |
| Restart | `unless-stopped` |
| Depends on | `postgres` (condition: service_healthy) |
| Resources | 2GB RAM (configurable), 1 CPU |

### Description

Honcho is a self-hosted AI memory and context persistence service. It provides long-term peer profiles, session search across conversations, and dialectic reasoning \u2014 enabling persistent, contextual AI interactions. Waits for Postgres to become available, enables required PostgreSQL extensions (pgvector, pg_trgm), runs Alembic migrations, and starts the FastAPI service.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `DB_HOST` | Postgres hostname | Yes |
| `DB_PORT` | Postgres port | Yes |
| `DB_NAME` | Database name | Yes |
| `DB_USER` | Postgres username | Yes |
| `DB_PASSWORD` | Postgres password | Yes |
| `AUTH_USE_AUTH` | Disable authentication | No (default: false) |
| `OPENCODE_API_KEY` | OpenCode API key for LLM calls | Yes |
| `LLM_DEFAULT_MAX_TOKENS` | Max tokens for LLM calls | No (default: 4096) |
| `EMBEDDING_VECTOR_DIMENSIONS` | Vector embedding dimensions | No (default: 768) |
| `HONCHO_MEMORY_LIMIT` | Container memory limit | No (default: 2G) |

### LLM Configuration

Honcho uses OpenCode (Go and Zen) as its LLM provider, configured to use `deepseek-v4-flash` for all dialectic levels and summary generation. Embeddings use `nomic-embed-text-v1.5` served by the local LLMster container.

### Exposed Ports

| Port | Purpose |
|---|---|
| 8000 | Honcho API |

---

## owui (Open WebUI)

| Property | Value |
|---|---|
| IP | `10.51.0.10` |
| Profile | `agentic` |
| Image | `ghcr.io/open-webui/open-webui:main` |
| Restart | `always` |

### Description

Open WebUI provides a ChatGPT-like web interface for interacting with LLMs. Supports multiple backends, conversation history, and model switching.

### Environment Variables

| Variable | Description |
|---|---|
| `ENABLE_EVALUATION_ARENA_MODELS` | Disable evaluation arena (`false`) |

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/owui` | `/app/backend/data` | Chat history, user data |

### Exposed Ports

| Host Port | Container Port |
|---|---|
| 3003 | 8080 |

---

## portainer

| Property | Value |
|---|---|
| IP | `10.51.0.13` |
| Profile | `monitoring` |
| Image | `portainer/portainer-ce:latest` |
| Restart | `unless-stopped` |

### Description

Container management dashboard providing a web UI for managing Docker resources. Includes external stack management capability.

### Environment Variables

| Variable | Description |
|---|---|
| `PORTAINER_MANAGE_EXTERNAL_STACKS` | Enable external stack management (`1`) |

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `/var/run/docker.sock` | `/var/run/docker.sock` | Docker API access |
| `~/portainer/data` | `/data` | Persistent state |

---

## vault

| Property | Value |
|---|---|
| IP | `10.51.0.6` |
| Profile | `security` |
| Image | `hashicorp/vault:latest` |
| Restart | `unless-stopped` |
| Resources | 512MB RAM, 0.5 CPU |

### Description

HashiCorp Vault for centralized secrets management. Uses file-based storage with a custom entrypoint script for initialization and unsealing.

### Volumes

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/vault/config` | `/vault/config` | Server configuration |
| `~/vault/logs` | `/vault/logs` | Audit logs |
| `~/vault/file` | `/vault/file` | Encrypted storage backend |

### Exposed Ports

| Host Port | Container Port |
|---|---|
| 1234 | 1234 |

### Entrypoint

Custom entrypoint at `/vault/config/entrypoint.sh` (mounted from host).

---

## pgadmin

| Property | Value |
|---|---|
| IP | `10.51.0.12` |
| Profile | `data` |
| Image | `dpage/pgadmin4:latest` |
| Restart | `unless-stopped` |
| Depends on | `postgres` (condition: service_healthy) |

### Description

Web-based PostgreSQL administration tool. Pre-configured with server connections via a mounted JSON file.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `PGADMIN_DEFAULT_EMAIL` | Login email | Yes |
| `PGADMIN_DEFAULT_PASSWORD` | Login password | Yes |

### Storage

| Host Path | Container Path | Purpose |
|---|---|---|
| `~/pgadmin/servers.json` | `/pgadmin4/servers.json` | Pre-configured server list |
