# Home Lab Documentation

Welcome to the Home Lab documentation. This repository defines a self-hosted infrastructure that combines media serving, automation, identity management, networking, AI inference, and content-processing services on a single Docker host.

## Quick Start

```sh
git clone https://github.com/mouralx/home-lab.git
cd home-lab

# Create your .env file
# Define required variables: POSTGRES_USER, POSTGRES_PASSWORD, storage paths, etc.
# See the Configuration section for the full reference
cp infra/.env.example infra/.env 2>/dev/null || touch infra/.env

# Start all services
docker compose --env-file infra/.env -f infra/compose.yaml up -d
```

## Documentation Sections

| Section | Description |
|---|---|
| [Architecture](architecture.md) | System architecture, layers, network model, storage layout, service relationships |
| [Services](services.md) | Complete reference for all 15 Docker services |
| [Digger](digger.md) | Custom .NET worker for automated movie discovery and download management |
| [Deployment](deployment.md) | GitHub Actions CI/CD pipeline, self-hosted runner setup, environment management |
| [Configuration](configuration.md) | All configuration options, environment variables, secrets management |
| [Operations](operations.md) | Day-to-day operations, monitoring, backup, maintenance, troubleshooting |
| [Scripts](scripts.md) | Entrypoint scripts reference |

## Project Layout

```text
.
├── .github/
│   └── workflows/
│       └── publish-home-lab.yaml   # Deployment pipeline
├── infra/
│   ├── compose.yaml                # Docker Compose stack (15 services)
│   ├── Dockerfile.digger           # Digger worker build
│   ├── Dockerfile.honcho           # Honcho API image
│   ├── Dockerfile.llmster          # Local LLM runtime image
│   └── Dockerfile.transmission     # Custom Transmission image
├── scripts/
│   ├── honcho.entrypoint.sh        # Honcho container startup
│   ├── lmstudio.entrypoint.sh      # LM Studio model loader
│   └── transmission.entrypoint.sh  # Transmission config injector
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

The stack runs 15 containers on a dedicated Docker bridge network (`10.51.0.0/24`) with static IPs, organised into profiles:

| Service | IP | Profile | Function |
|---|---|---|---|
| digger | 10.51.0.2 | media | Automated movie discovery & download management |
| transmission | 10.51.0.15 | media | Torrent client |
| hermes | 10.51.0.17 | ai | AI agent gateway |
| llmster | 10.51.0.18 | ai | Local model runtime |
| honcho | 10.51.0.19 | ai | Honcho API service |
| keycloak | 10.51.0.16 | ai | Identity and access management |
| owui | 10.51.0.10 | ai | Open WebUI |
| vault | 10.51.0.6 | tools | Secrets management |
| n8n | 10.51.0.7 | tools | Workflow automation |
| pgadmin | 10.51.0.12 | tools | Postgres administration |
| homeassistant | 10.51.0.4 | — | Home automation platform |
| jellyfin | 10.51.0.5 | — | Media server |
| npm | 10.51.0.8 | — | Reverse proxy (Nginx Proxy Manager) |
| portainer | 10.51.0.13 | — | Container management dashboard |
| postgres | 10.51.0.14 | — | Shared relational database (pgvector) |

## Key Concepts

- **Single-host deployment**: All services run on one machine, orchestrated by Docker Compose
- **Compose profiles**: Services are grouped into `ai`, `media`, and `tools` profiles for selective deployment
- **Static IP networking**: Services communicate over a dedicated bridge network with predictable addresses
- **Environment-driven configuration**: Runtime values are injected via `.env` file at deploy time
- **Secrets management**: Sensitive values are stored in GitHub Environments, never committed to the repo
- **Automated media pipeline**: The Digger worker discovers movies from YTS, manages downloads through Transmission, and tracks everything in SQLite
- **Self-hosted runner**: Deployment happens via GitHub Actions on a local self-hosted runner
- **Local AI inference**: Hermes and Honcho both use LLMster for local LLM inference, with OpenCode API fallback

## Architecture Layers

### Access Layer
Nginx Proxy Manager provides TLS termination and reverse proxy for all web services. Keycloak handles authentication and identity federation.

### Automation Layer
n8n orchestrates workflows, Hermes provides AI gateway capabilities, Honcho provides persistent memory and context for AI agents, Open WebUI offers a ChatGPT-like interface, and Digger automates the media lifecycle.

### Media Layer
Transmission handles torrenting, Jellyfin serves media to users, and Digger bridges content discovery with download management.

### Operations Layer
Portainer provides container management, Vault manages secrets, and the GitHub Actions pipeline enables repeatable deployments.
