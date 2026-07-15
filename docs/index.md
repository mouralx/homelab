# Home Lab Documentation

Welcome to the Home Lab documentation. This repository defines a self-hosted infrastructure that combines media serving, automation, identity management, networking, AI inference, and content-processing services on a single Docker host.

## Quick Start

```sh
git clone https://github.com/mouralx/home-lab.git
cd home-lab

# Create your .env file (see Configuration section)
cp infra/.env.example infra/.env

# Start the stack
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

## Documentation Sections

| Section | Description |
|---|---|
| [Architecture](architecture.md) | System architecture, layers, network model, storage layout, service relationships |
| [Services](services.md) | Complete reference for all 13 Docker services |
| [Digger](digger.md) | Custom .NET worker for automated movie discovery and download management |
| [Deployment](deployment.md) | GitHub Actions CI/CD pipeline, self-hosted runner setup, environment management |
| [Configuration](configuration.md) | All configuration options, environment variables, secrets management |
| [Operations](operations.md) | Day-to-day operations, monitoring, backup, maintenance, troubleshooting |
| [Scripts](scripts.md) | Utility scripts reference |

## Project Layout

```text
.
├── .github/
│   └── workflows/
│       └── publish-home-lab.yaml   # Deployment pipeline
├── infra/
│   ├── compose.yaml                # Docker Compose stack (13 services)
│   ├── Dockerfile.digger           # Digger worker build
│   ├── Dockerfile.transmission     # Custom Transmission image
│   └── Dockerfile.llama            # Local LLM inference image
├── scripts/
│   ├── externals/
│   │   └── deepwiki-open.sh        # Build & publish DeepWiki-Open image
│   └── utils/
│       └── copy-env-vars.sh        # Sync GitHub env vars between repos
├── src/
│   └── Digger/                     # .NET 10 worker solution
│       ├── Digger.slnx
│       ├── Digger.Worker/          # Background services (Worker + Cleaner)
│       ├── Digger.Services/        # YTS API + Transmission RPC clients
│       ├── Digger.Services.Models/ # API response DTOs
│       ├── Digger.Data/            # EF Core DbContext + SQLite entities
│       └── Digger.Data.Common/     # Shared enums (MovieStatus)
└── docs/                           # This documentation
```

## Service Overview

The stack runs 13 containers on a dedicated Docker bridge network (`10.51.0.0/24`) with static IPs:

| Service | IP | Function |
|---|---|---|
| digger | 10.51.0.2 | Automated movie discovery & download management |
| vault | 10.51.0.6 | Secrets management |
| hermes | 10.51.0.17 | AI gateway for local model inference |
| homeassistant | 10.51.0.4 | Home automation platform |
| jellyfin | 10.51.0.5 | Media server |
| n8n | 10.51.0.7 | Workflow automation |
| npm | 10.51.0.8 | Reverse proxy (Nginx Proxy Manager) |
| llama | 10.51.0.9 | Local LLM inference (Ministral-3B) |
| pgadmin | 10.51.0.12 | Postgres administration |
| portainer | 10.51.0.13 | Container management dashboard |
| postgres | 10.51.0.14 | Shared relational database |
| transmission | 10.51.0.15 | Torrent client |
| keycloak | 10.51.0.16 | Identity and access management |

## Key Concepts

- **Single-host deployment**: All services run on one machine, orchestrated by Docker Compose
- **Static IP networking**: Services communicate over a dedicated bridge network with predictable addresses
- **Environment-driven configuration**: Runtime values are injected via `.env` file at deploy time
- **Secrets management**: Sensitive values are stored in GitHub Environments, never committed to the repo
- **Automated media pipeline**: The Digger worker discovers movies from YTS, manages downloads through Transmission, and tracks everything in SQLite
- **Self-hosted runner**: Deployment happens via GitHub Actions on a local self-hosted runner

## Architecture Layers

### Access Layer
Nginx Proxy Manager provides TLS termination and reverse proxy for all web services. Keycloak handles authentication and identity federation.

### Automation Layer
n8n orchestrates workflows, Hermes provides AI gateway capabilities backed by local LLMs (Ollama/Llama), and Digger automates the media lifecycle.

### Media Layer
Transmission handles torrenting, Jellyfin serves media to users, and Digger bridges content discovery with download management.

### Operations Layer
Portainer provides container management, Vault manages secrets, and the GitHub Actions pipeline enables repeatable deployments.
