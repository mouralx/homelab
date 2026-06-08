# 🏠 Home Lab

This repository combines a Docker Compose home-lab stack with a .NET background worker named Digger. The stack runs media, automation, networking, and torrent services on a dedicated bridge network, while the Digger worker discovers movies from YTS, stores state in SQLite, and queues downloads through Transmission.

The implementation in this repository is not just infrastructure wiring; it also contains the actual worker logic under `src/Digger`, the compose file under `infra/compose.yaml`, the deployment automation under `.github/workflows/publish-home-lab.yaml`, and the helper scripts under `scripts/`.

---

## 1. Repository Layout

```text
.
├── .github/workflows/
│   └── publish-home-lab.yaml       # Manual deployment workflow for the self-hosted runner
├── infra/
│   ├── compose.yaml                 # Full home-lab stack definition
│   ├── digger/Dockerfile            # Digger worker container image
│   └── transmission/Dockerfile      # Transmission daemon image
├── scripts/
│   ├── externals/deepwiki-open.sh   # Optional image publishing helper
│   └── utils/copy-env-vars.sh       # Copy GitHub variables/secrets between environments
└── src/Digger/                      # .NET solution for the worker and services
```

### Key folders

- `.github/workflows/` contains the deployment automation used to build and start the stack on a self-hosted runner.
- `infra/` contains the runtime definition for every container and the Docker image definitions that support the stack.
- `scripts/` contains support tooling for deployment and GitHub environment maintenance.
- `src/Digger/` contains the full .NET application: the worker host, the YTS service, the Transmission service, and the SQLite-backed data layer.

---

## 2. Runtime Stack Overview

The stack is defined in `infra/compose.yaml` and uses a custom Docker bridge network with subnet `10.51.0.0/24`. Each service is assigned a static container IP in that range, which makes it possible to keep internal service-to-service communication predictable.

### Services in the current compose file

| Service | Image / Build | Purpose | Key runtime details |
| --- | --- | --- | --- |
| `digger` | Build from `../src/Digger` with `../../infra/digger/Dockerfile` | Runs the .NET movie-discovery worker | Depends on `transmission`; mounts `/mnt/ssd/transmission/downloads/movies:/downloads/movies` and `~/digger:/digger/data`; IP `10.51.0.2` |
| `frigate` | `ghcr.io/blakeblackshear/frigate:stable` | Camera/NVR processing | Uses `FRIGATE_PASSWORD`; IP `10.51.0.3` |
| `hermes` | `nousresearch/hermes-agent` | AI gateway/dashboard service | Exposes `8642` and `9119`; IP `10.51.0.17` |
| `homeassistant` | `ghcr.io/home-assistant/home-assistant:stable` | Home automation | IP `10.51.0.4` |
| `jellyfin` | `jellyfin/jellyfin` | Media server | Uses `JELLYFIN_SERVER_URL`; mounts `/mnt/ssd/transmission/downloads` as media source; IP `10.51.0.5` |
| `n8n` | `docker.n8n.io/n8nio/n8n:next` | Workflow automation | Uses PostgreSQL backend and `N8N_ENCRYPTION_KEY`; exposes `5678`; IP `10.51.0.7` |
| `npm` | `jc21/nginx-proxy-manager:latest` | Reverse proxy / HTTPS manager | Exposes `80`, `81`, `443`; IP `10.51.0.8` |
| `pgadmin` | `dpage/pgadmin4:latest` | PostgreSQL admin UI | Depends on `postgres`; IP `10.51.0.12` |
| `portainer` | `portainer/portainer-ce:latest` | Container management | Mounts `/run/user/1000/docker.sock`; IP `10.51.0.13` |
| `postgres` | `postgres:15` | Shared PostgreSQL database | Uses `POSTGRES_USER` / `POSTGRES_PASSWORD`; IP `10.51.0.14` |
| `transmission` | Custom image from `infra/transmission/Dockerfile` | Torrent daemon used by Digger | Exposes `9091` and `51413/udp`; mounts `/mnt/ssd/transmission/downloads` and `/mnt/ssd/transmission/incomplete`; IP `10.51.0.15` |

### Notes on service wiring

- The `digger` worker depends on `transmission`, meaning the worker is expected to start only after the torrent daemon is available.
- `n8n` is wired to the `postgres` service, not to an external database, and is configured with `DB_TYPE=postgresdb` and `DB_POSTGRESDB_HOST=postgres`.
- `jellyfin` reads from the Transmission download path, making the torrent download volume part of the media stack.
- `portainer` uses the host Docker socket to monitor and manage the local daemon.
- `transmission` is built from `infra/transmission/Dockerfile`, which starts `transmission-daemon` with authentication enabled and exposes the RPC and torrent ports.

---

## 3. Local Configuration and Environment Variables

The Compose file expects several runtime values to exist in `infra/.env`. That file is not committed to the repository and should contain local host-specific paths and secrets.

### Environment values currently referenced by the stack

The live compose file expects the following runtime values to be available from the host environment:

- `FRIGATE_PASSWORD`
- `JELLYFIN_SERVER_URL`
- `N8N_ENCRYPTION_KEY`
- `POSTGRES_PASSWORD`
- `POSTGRES_USER`
- `TRANSMISSION_PASSWORD`
- `TRANSMISSION_USER` (optional, defaults to `transmission` inside the custom image)

### Important operational note

The deployment workflow writes `infra/.env` on the runner from the selected GitHub environment, and it redacts runtime secrets into the worker configuration before starting containers. Never store real values in the repository; keep those values in the local environment or the GitHub environment used by the runner.

---

## 4. How to Run the Stack Locally

From the repository root, start the stack using the local environment file:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

Check container status:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml ps
```

View logs for a container:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml logs -f transmission
```

Stop the stack:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml down
```

If you want to pull the latest images and recreate containers:

```sh
docker compose --env-file infra/.env -f infra/compose.yaml pull
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

---

## 5. Deployment Workflow

The file `.github/workflows/publish-home-lab.yaml` defines the manual deployment flow used by the self-hosted GitHub runner.

### What the workflow does

1. Lets you choose a GitHub environment via `workflow_dispatch`.
2. Checks out the repository on the runner.
3. Optionally publishes or updates the DeepWiki-Open image.
4. Redacts Transmission RPC credentials into `src/Digger/Digger.Worker/appsettings.json`.
5. Creates `infra/.env` from the selected GitHub environment variables and secrets.
6. Builds the Docker Compose stack and starts it in detached mode.

### Deployment behavior details

- The workflow uses `runs-on: self-hosted`, so it expects a runner that can access the local Docker daemon and the target host volumes.
- The `Redacting configurations` step rewrites the worker settings file to inject the selected environment’s `TRANSMISSION_USERNAME` and `TRANSMISSION_PASSWORD` values.
- The `Create .env file from selected environment` step writes all selected GitHub variables and secrets into `infra/.env`, which `docker compose` then uses at runtime.
- The optional `build_deepwiki_open` input lets the pipeline publish the custom DeepWiki-Open image before deployment.

---

## 6. Digger Worker Architecture

The worker implementation lives under `src/Digger` and is split into the following layers:

- `Digger.Worker` — the hosted worker entrypoint and the two background services (`Worker` and `Cleaner`).
- `Digger.Services` — the YTS discovery service and the Transmission RPC client wrapper.
- `Digger.Data` and `Digger.Data.Common` — the SQLite database context, entity model, and movie status enum.

### Worker startup flow

The application entrypoint is `src/Digger/Digger.Worker/Program.cs`. It:

- creates the generic host;
- registers `Worker` and `Cleaner` as hosted services;
- configures `DiggerContext` to use SQLite via `UseSqlite(...)`;
- registers `IYtsService` as `YtsService`;
- registers `ITransmissionService` as `TransmissionService`;
- builds and runs the host.

### Runtime responsibilities

The `Worker` service repeatedly performs the following operations in a loop:

1. fetches new movies from YTS;
2. stores any new titles in the SQLite database;
3. marks old, ready-to-download entries for skipping when the disk allocation cap is exceeded;
4. checks completed or stopped torrents and updates the movie status accordingly;
5. retries movies marked as failed;
6. enqueues new torrents if the current download count is below the configured maximum.

The `Cleaner` service runs in parallel and removes directories for movies already marked as `RolledOut`, helping keep the download folder clean as the worker rolls out old content.

---

## 7. Data Model and Status Flow

The main entity is `Digger.Data.Entities.Movie`, which stores:

- `Id` — the YTS movie identifier.
- `Name` — the movie title.
- `Path` — the local download folder path.
- `TorrentUrl` — the torrent file URL used by Transmission.
- `Timestamp` — the moment the entry was created.
- `DownloadAttempt` — retry counter.
- `LastKnownStatus` — the current lifecycle state.
- `Size` — torrent size in bytes.
- `PublishDate` — the release date used in ordering.

The possible statuses are defined in `Digger.Data.Common.Enums.MovieStatus`:

```text
Stopped,
PendingCheck,
Checking,
PendingDownload,
Downloading,
PendingSeed,
Seeding,
NotEnqueued,
Enqueued,
Failed,
RolledOut,
Complete,
Skipped
```

This enum is used to represent the current lifecycle of each movie from discovery to completion or failure.

---

## 8. YTS Discovery Behavior

The `YtsService` implementation fetches pages from the YTS API and filters them using the values in `appsettings.json`.

### Current filter configuration

The current worker configuration sets:

- `Yts:ApiUrl = https://movies-api.accel.li/api/v2/list_movies.json`
- `Yts:TorrentBaseUrl = ""`
- `Yts:Parameters` with `quality=2160p`, `minimum_rating=4`, `sort_by=year`, `order_by=desc`, `limit=50`
- `Yts:YearsBack = 1`
- `Yts:Genres = [action, adventure, comedy, crime, drama, family, fantasy, film-noir, horror, musical, mystery, romance, sci-fi, thriller, war, western]`
- `Yts:Languages = [en, pt]`
- `Yts:MinimumSeeders = 10`

### What the service does

- Builds the YTS query string from the configured parameters.
- Fetches one page at a time.
- Filters by language, year, and genre.
- Picks the highest-seeded torrent matching the configured quality.
- Converts the result into `YtsMovieModel` entries that the worker can persist.

This means the worker is not discovering “all movies”; it is intentionally constrained to a limited set of recent, high-quality, high-seeder torrents.

---

## 9. Transmission Integration

The `TransmissionService` implements `ITransmissionService` and wraps the Transmission RPC client.

### What it handles

- `Download(string fileName, string downloadDirectory)` — adds a torrent to Transmission with a custom download directory.
- `GetStatus(string downloadDirectory)` — reads current torrent state for a given path.
- `DownloadsCount()` — returns the number of active torrents.
- `ClenByStatuses(params MovieStatus[] statuses)` — removes torrent entries and returns the matching download directories.
- `GetPaths()` — lists all torrent download paths.

### Current RPC configuration

The worker is currently configured with:

- `Transmission:ServerUrl = http://transmission:9091/transmission/rpc`
- `Transmission:UseAuth = true`
- `Transmission:User` and `Transmission:Password` injected by the deployment workflow

This is important because the worker talks to the Docker-internal RPC endpoint, not to a host-local URL.

---

## 10. Runtime Settings in `appsettings.json`

The current application settings in `src/Digger/Digger.Worker/appsettings.json` are:

| Setting | Current value | Meaning |
| --- | --- | --- |
| `Logging` | Information / Error | Logging level for runtime and EF Core database commands |
| `Transmission:ServerUrl` | `http://transmission:9091/transmission/rpc` | Internal RPC endpoint used by the worker |
| `Transmission:UseAuth` | `true` | Enables authentication for Transmission RPC |
| `Yts:ApiUrl` | `https://movies-api.accel.li/api/v2/list_movies.json` | Source of the movie list |
| `Yts:YearsBack` | `1` | Only recent movies are considered |
| `Yts:MinimumSeeders` | `10` | Minimum seed count required |
| `StopTime` | `1` | Worker polling interval in minutes |
| `DownloadDirectory` | `/downloads/movies` | Container download root |
| `ConnectionStrings:DiggerContext` | `Data Source=/digger/data/movies.db` | SQLite database file path |
| `MaxAllocatedSpace` | `750000000000` bytes | Maximum disk allocation the worker may keep |
| `MaxEnqueuedRetries` | `3` | Number of retries before a movie is marked failed |
| `MaxEnqueuedTorrents` | `5` | Maximum simultaneous downloads |

These settings are the operational baseline for the Digger worker in the current codebase.

---

## 11. Helper Scripts

The `scripts/` directory currently contains the tools that support deployment and environment maintenance.

### `scripts/utils/copy-env-vars.sh`

This script:

- checks for `brew`, `gh`, and `jq`;
- installs missing tools with Homebrew when needed;
- authenticates `gh` if a token is provided;
- reads variables and secret names from one GitHub environment;
- writes the variables to the target environment;
- writes placeholder secret values such as `REPLACE_WITH_SECRET_VALUE` because GitHub does not allow plain secret values to be retrieved.

### `scripts/externals/deepwiki-open.sh`

This helper is used by the deployment workflow when the `build_deepwiki_open` input is enabled. It publishes or refreshes the DeepWiki-Open image before the main stack starts.

---

## 12. Operational Notes and Maintenance

### Backup recommendations

Before updating images, changing the host filesystem layout, or doing destructive maintenance, back up the host directories used by the stack:

- PostgreSQL data
- Nginx Proxy Manager data and certificates
- Home Assistant config
- Frigate config and media
- Jellyfin config/cache/media
- Portainer data
- Transmission downloads and incomplete folders
- Digger’s SQLite database under `~/digger`

### Good operational habits

- Keep `infra/.env` local and uncommitted.
- Verify that the selected GitHub environment contains the same variable names expected by `infra/compose.yaml`.
- Treat the worker settings file as a redacted deployment artifact, not as a source of real runtime secrets.
- Review the Digger worker logs if downloads stop or if the torrent queue reaches its configured limit.

---

## 13. What This Repository Is Today

In its current form, the repository is a practical deployment and automation project for:

- running multiple self-hosted services on Docker;
- maintaining a small movie-discovery automation pipeline;
- integrating a YTS-based discovery service with a Transmission torrent daemon;
- deploying the entire stack through GitHub Actions on a self-hosted runner.

It is not just a simple Compose file; it is a combined infrastructure + worker + automation codebase that is meant to be operated as a single homelab platform.
