# Services Reference

Complete documentation for all 13 Docker services in the home lab stack.

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
- [llama](#llama)
- [portainer](#portainer)
- [vault](#vault)
- [pgadmin](#pgadmin)

---

## digger

| Property | Value |
|---|---|
| IP | `10.51.0.2` |
| Image | Custom build (see `Dockerfile.digger`) |
| Restart | `unless-stopped` |
| Depends on | `transmission` |
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
| Image | Custom build (see `Dockerfile.transmission`) |
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

### Dockerfile Details

Base image: `ubuntu:latest`. Installs `transmission-daemon` via apt. Runs as daemon with authentication, configurable incomplete directory, and custom download directory.

---

## jellyfin

| Property | Value |
|---|---|
| IP | `10.51.0.5` |
| Image | `jellyfin/jellyfin:latest` |
| Restart | `unless-stopped` |

### Description

Open-source media server that organizes, streams, and transcodes media content. Consumes movies downloaded by Transmission from the shared media directory.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `JELLYFIN_SERVER_URL` | Published server URL | Yes |

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
| Image | `docker.n8n.io/n8nio/n8n:next` |
| Restart | `unless-stopped` |
| Depends on | `postgres` |

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

### Typical Proxy Configuration

The admin UI at port 81 should be used to configure proxy hosts for:
- Jellyfin
- Home Assistant
- n8n
- Keycloak
- Portainer
- Hermes

---

## keycloak

| Property | Value |
|---|---|
| IP | `10.51.0.16` |
| Image | `quay.io/keycloak/keycloak:latest` |
| Restart | `unless-stopped` |

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

- `~/keycloak:/opt/keycloak/data` - Realm configuration, users, sessions

---

## postgres

| Property | Value |
|---|---|
| IP | `10.51.0.14` |
| Image | `postgres:15` |
| Restart | `unless-stopped` |

### Description

Shared PostgreSQL 15 database used by n8n, Keycloak, and accessible via pgAdmin. Configured with 128MB shared memory.

### Environment Variables

| Variable | Description | Required |
|---|---|---|
| `POSTGRES_USER` | Database superuser | Yes |
| `POSTGRES_PASSWORD` | Database password | Yes |

### Storage

- `~/postgres/data:/var/lib/postgresql/data`

### Databases

The server creates a default `postgres` database. Services create their own databases:
- `n8n` - Workflow state
- `keycloak` - Realm configuration

---

## homeassistant

| Property | Value |
|---|---|
| IP | `10.51.0.4` |
| Image | `ghcr.io/home-assistant/home-assistant:stable` |
| Restart | `unless-stopped` |

### Description

Open-source home automation platform. Integrates with Frigate for camera-based automation. Can be integrated with n8n for complex automation workflows.

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
| Image | `nousresearch/hermes-agent:latest` |
| Restart | `unless-stopped` |
| Resources | 4GB RAM, 2 CPU cores |

### Description

AI agent gateway by Nous Research. Routes inference requests to local LLM backends (Llama/Ollama). Provides a dashboard with OIDC authentication.

### Environment Variables

| Variable | Description |
|---|---|
| `HERMES_DASHBOARD` | Enable dashboard (`1`) |
| `HERMES_DASHBOARD_OIDC_ISSUER` | OIDC issuer URL |
| `HERMES_DASHBOARD_OIDC_CLIENT_ID` | OIDC client ID |
| `HERMES_DASHBOARD_PUBLIC_URL` | Public URL for dashboard |
| `OPENCODE_API_KEY` | API key (shared for GO and ZEN) |

### Exposed Ports

| Port | Purpose |
|---|---|
| 8642 | Agent gateway |
| 9119 | Secondary interface |

### Resource Limits

- **Memory**: 4GB
- **CPUs**: 2.0

---

## llama

| Property | Value |
|---|---|
| IP | `10.51.0.9` |
| Image | Custom build (see `Dockerfile.llama`) |
| Restart | `unless-stopped` |

### Description

Local LLM inference server running the Ministral-3-3B-Reasoning model in Q8_0 quantization via the `llama` CLI. Optimized for local inference with 8 threads and a 65K token context window.

### Dockerfile Details

1. Base image: `ubuntu:latest`
2. Installs `curl`
3. Runs the Llama CLI installer (`llama.app/install.sh`)
4. Serves model `ggml-org/Ministral-3-3B-Reasoning-2512-GGUF:Q8_0`

### Entrypoint

```sh
llama serve -hf ggml-org/Ministral-3-3B-Reasoning-2512-GGUF:Q8_0 \
  --host 0.0.0.0 --port 8081 -t 8 -c 65536
```

### Exposed Ports

| Port | Container Port |
|---|---|
| 8081 | 8081 |

### Storage

- `~/llama:/root/.cache` - Cached model files

---

## portainer

| Property | Value |
|---|---|
| IP | `10.51.0.13` |
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
| Image | `hashicorp/vault:latest` |
| Restart | `unless-stopped` |

### Description

HashiCorp Vault for centralized secrets management. Uses file-based storage with a custom entrypoint script for initialization.

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

---

## pgadmin

| Property | Value |
|---|---|
| IP | `10.51.0.12` |
| Image | `dpage/pgadmin4:latest` |
| Restart | `unless-stopped` |
| Depends on | `postgres` |

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
