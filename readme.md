# Home Lab

Personal home-lab infrastructure managed with Docker Compose, plus a small .NET 10 worker used to discover and enqueue movie downloads through Transmission.

The main stack lives in `infra/compose.yaml`. Runtime values are loaded from `infra/.env`.

## Repository Layout

```text
.
├── infra/
│   ├── compose.yaml       # Docker Compose stack
│   ├── .env               # Compose environment values
│   ├── digger/            # Compose build context for Digger
│   └── openclaw/          # Compose build context for OpenClaw
└── src/
    └── Digger/            # .NET Digger worker source
```

## Services

The compose stack currently includes:

| Service | Purpose | Exposed ports |
| --- | --- | --- |
| `digger` | Custom .NET worker that syncs YTS movie discovery with Transmission | none |
| `frigate` | NVR and camera processing | `8971`, `8554`, `8555`, `5000` |
| `homeassistant` | Home automation | `8123` |
| `jellyfin` | Media server | `8096`, `7359/udp` |
| `mosquitto` | MQTT broker | `1883` |
| `n8n` | Automation workflows | behind configured host/proxy |
| `npm` | Nginx Proxy Manager | `80`, `81`, `443` |
| `ollama` | Local model runtime | internal |
| `openclaw` | OpenClaw gateway | `18789` |
| `openwebui` | Web UI for Ollama | `8080` |
| `pgadmin` | PostgreSQL administration | behind configured host/proxy |
| `portainer` | Docker management UI | `9000` |
| `postgres` | Shared PostgreSQL database | `5432` |
| `transmission` | Torrent client | `9091`, `51413` |

All containers are attached to a custom bridge network on `10.51.0.0/24` with static addresses.

## Configuration

Review `infra/.env` before starting the stack. It contains the variable names used by Docker Compose, with each value written as a pass-through placeholder such as `DIGGER_DATA_DIR=${DIGGER_DATA_DIR}`. Set the real values in your shell, deployment environment, or another secret-management layer before starting the stack. At minimum, provide:

| Variable | Used for |
| --- | --- |
| `POSTGRES_USER`, `POSTGRES_PASSWORD` | PostgreSQL, n8n, and pgAdmin access |
| `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD` | Initial pgAdmin login |
| `TRANSMISSION_USERNAME`, `TRANSMISSION_PASSWORD`, `TRANSMISSION_WHITELIST` | Transmission authentication and access control |
| `FRIGATE_PASSWORD` | Frigate RTSP password |
| `*_DIR`, `*_DATA` | Host paths for persistent service data |

`infra/.env` is intentionally secret-free and machine-specific. Keep production secrets out of the repository and provide them at runtime.

## Running The Stack

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

Pull updated images:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml pull
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

## Digger Worker

`src/Digger` contains a .NET 10 background worker. It reads movie data from YTS, stores state in SQLite, and uses the Transmission RPC API to enqueue and clean up downloads.

Key settings are in `src/Digger/Digger.Worker/appsettings.json`:

| Setting | Description |
| --- | --- |
| `Transmission:ServerUrl` | Transmission RPC endpoint inside the compose network |
| `Transmission:User`, `Transmission:Password` | Transmission RPC credentials read by the worker |
| `Transmission:UseAuth` | Enables Transmission RPC authentication |
| `Yts:*` | Movie discovery filters, languages, seed threshold, and API URL |
| `DownloadDirectory` | Container path where movie downloads are organized |
| `ConnectionStrings:DiggerContext` | SQLite database location |
| `MaxAllocatedSpace` | Maximum allocated movie storage |
| `MaxEnqueuedRetries` | Retry limit before marking a movie failed |
| `MaxEnqueuedTorrents` | Maximum active queued torrents |
| `StopTime` | Delay between worker cycles, in minutes |

The compose service mounts:

```text
${TRANSMISSION_DOWNLOADS_DIR}/movies -> /downloads/movies
${DIGGER_DATA_DIR}                     -> /digger/data
```

## Data And Backups

Most persistent data is controlled by paths in `infra/.env`. Back up these directories before host maintenance, image upgrades, or destructive compose operations:

- PostgreSQL data
- Nginx Proxy Manager data and certificates
- Home Assistant, Frigate, Mosquitto, n8n, Jellyfin, Portainer, Open WebUI, Ollama, and Transmission data
- Digger SQLite data under `DIGGER_DATA_DIR`

## Notes

- `infra/.env` is machine-specific and is the environment file used by all documented Docker Compose commands.
- Several services assume reverse-proxy hostnames such as `automation.mouras.me` and the URLs supplied through the runtime environment.
- The stack uses privileged host integrations for some services, including Docker socket access for Portainer.
