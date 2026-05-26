# 🏠 Home Lab

Personal home-lab infrastructure managed with Docker Compose, plus a small .NET worker named Digger that discovers movies and queues downloads through Transmission.

The Docker stack lives in `infra/compose.yaml`. Compose runtime values are expected in `infra/.env`, which is created locally by you or during deployment by the GitHub Actions workflow.

## 🗂️ Repository Layout

```text
.
├── .github/workflows/
│   └── publish-home-lab.yaml  # 🚀 Manual deployment workflow
├── infra/
│   ├── compose.yaml           # 📦 Home-lab Docker Compose stack
│   ├── digger/Dockerfile      # 🎬 Container image for the Digger worker
│   ├── lms/Dockerfile         # 🧠 Container image for LM Studio
│   └── openclaw/Dockerfile    # 🔐 Container image for OpenClaw
├── scripts/
│   ├── copy-env-vars.sh       # 🛠️ GitHub environment variable helper
│   └── readme.md
└── src/
    └── Digger/                # ⚙️ .NET Digger solution
```

## 🧱 Infrastructure

`infra/compose.yaml` defines the services that run on the home-lab host. Most services mount host directories supplied through environment variables, so `infra/.env` must exist before starting the stack.

Current services:

| Service | Purpose | Ports |
| --- | --- | --- |
| � `forgejo` | Self-hosted Git service | internal |
| 📹 `frigate` | NVR and camera processing | internal |
| 🤖 `hermes` | Nous Research Hermes AI agent gateway | `8642`, `9119` |
| 🏡 `homeassistant` | Home automation | internal |
| 🎞️ `jellyfin` | Media server | internal |
| 🧠 `lms` | LM Studio - Local model runtime and UI | `1234` |
| 📡 `mosquitto` | MQTT broker | internal |
| 🔁 `n8n` | Automation workflows backed by PostgreSQL | reverse proxy / internal |
| 🌐 `npm` | Nginx Proxy Manager | `80`, `81`, `443` |
| 💬 `openwebui` | Web UI for LM Studio | internal |
| 📋 `planka` | Kanban board application | `1337` |
| 🗄️ `pgadmin` | PostgreSQL administration UI | reverse proxy / internal |
| 📊 `portainer` | Docker management UI | internal |
| 🗃️ `postgres` | Shared PostgreSQL database | internal |
| ⬇️ `transmission` | Torrent client used by Digger | internal
| ⬇️ `transmission` | Torrent client used by Digger | `9091`, `51413` |

All services are attached to a custom bridge network on `10.51.0.0/24` with static container addresses.

## 🔧 Configuration

Create `infra/.env` with the variables referenced by `infra/compose.yaml`. The workflow also writes this file during deployment from the selected GitHub Environment's variables and secrets.

Required Compose variables:
ORGEJO_DATA_DIR
FRIGATE_CONFIG_DIR
FRIGATE_MEDIA_DIR
FRIGATE_PASSWORD
HOMEASSISTANT_CONFIG_DIR
JELLYFIN_CACHE_DIR
JELLYFIN_CONFIG_DIR
JELLYFIN_MEDIA_DIR
JELLYFIN_SERVER_URL
LMS_DATA_DIR (optional, defaults to ~/lms)
MOSQUITTO_CONFIG_DIR
MOSQUITTO_DATA_DIR
MOSQUITTO_LOGS_DIR
N8N_DATA_DIR
N8N_ENCRYPTION_KEY
NPM_CERTIFICATE_DIR
NPM_DATA_DIR
OPEN_WEBUI_DATA_DIR
PGADMIN_DATA_DIR
PGADMIN_DEFAULT_EMAIL
PGADMIN_DEFAULT_PASSWORD
PLANKA_DATA_DIR (optional, defaults to ~/planka)
PGADMIN_DATA_DIR
PGADMIN_DEFAULT_EMAIL
PGADMIN_DEFAULT_PASSWORD
PORTAINER_DATA_DIR
POSTGRES_DATA_DIR
POSTGRES_PASSWORD
POSTGRES_USER
TRANSMISSION_CONFIG_DIR
TRANSMISSION_DOWNLOADS_DIR
TRANSMISSION_PASSWORD
TRANSMISSION_USERNAME
TRANSMISSION_WHITELIST
```

Keep real values out of the repository. `infra/.env` contains local paths and secrets for the host where the stack runs.

## ▶️ Running Locally

From the repository root:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

Check status:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml ps
```

Follow logs for one service:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml logs -f transmission
```

Stop the stack:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml down
```

Pull updated images and recreate containers:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml pull
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

## 🚀 Deployment Workflow

`.github/workflows/publish-home-lab.yaml` defines a manual `workflow_dispatch` deployment for a self-hosted runner.

The workflow:

- 🎯 lets you choose a GitHub Environment, currently `mouras-home-lab`;
- 📥 checks out this repository on the runner;
- 📦 installs Docker;
- 🔐 creates `infra/.env` from the selected environment's GitHub Actions `vars` and `secrets`;
- 🚢 runs `docker compose -f infra/compose.yaml up -d --quiet-pull`.

The selected GitHub Environment must contain the same variable names required by `infra/compose.yaml`.

## 🎬 Digger Worker

`src/Digger` contains the .NET worker used by the `digger` Compose service. It reads movie data from YTS, stores state in SQLite, and talks to Transmission through the RPC API.

Important settings live in `src/Digger/Digger.Worker/appsettings.json`:

| SYts:Genres` | Genres to discover (action, adventure, comedy, crime, drama, etc.) |
| `Yts:Languages` | Languages to filter (e.g., en, pt) |
| `Yts:MinimumSeeders` | Minimum seeders threshold for torrents |
| `Yts:YearsBack` | How many years back to search for movies |
| `etting | Description |
| --- | --- |
| `Transmission:ServerUrl` | Transmission RPC endpoint inside the Compose network |
| `Transmission:User`, `Transmission:Password` | Transmission RPC credentials |
| `Transmission:UseAuth` | Enables Transmission RPC authentication |
| `Yts:*` | Movie discovery filters, languages, seed threshold, and API URL |
| `DownloadDirectory` | Container path for organized movie downloads |
| `ConnectionStrings:DiggerContext` | SQLite database location |
| `MaxAllocatedSpace` | Maximum allocated movie storage |
| `MaxEnqueuedRetries` | Retry limit before marking a movie failed |
| `MaxEnqueuedTorrents` | Maximum active queued torrents |
| `StopTime` | Delay between worker cycles, in minutes |

The Compose service mounts:Forgejo, Plank

```text
${TRANSMISSION_DOWNLOADS_DIR}/movies -> /downloads/movies
${DIGGER_DATA_DIR}                   -> /digger/data
```

## 💾 Backups

Back up the host directories referenced in `infra/.env` before host maintenance, image upgrades, or destructive Compose operations. The most important data is PostgreSQL, Nginx Proxy Manager certificates, Home Assistant, Frigate, n8n, Jellyfin, Portainer, Open WebUI, Ollama, Transmission, and Digger's SQLite database under `DIGGER_DATA_DIR`.
