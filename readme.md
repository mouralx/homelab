# Home Lab

This repository contains the current container stack for my home lab: agentic services, media services, and the supporting scripts used to build and deploy them.

## Current layout

```text
.
├── .github/
│   └── workflows/
│       └── deploy.yaml
├── docs/
│   ├── readme.md
│   ├── architecture.md
│   ├── services.md
│   └── operations.md
├── images/
│   ├── dockerfile.honcho
│   └── dockerfile.lms
├── scripts/
│   ├── image.entrypoint.honcho.sh
│   ├── image.entrypoint.lmstudio.sh
│   ├── workflow.step.clean-docker.sh
│   └── workflow.step.create-environment-file.sh
├── services/
│   ├── compose.agentic.yaml
│   └── compose.media.yaml
├── .gitignore
├── readme.md
└── .env.example (optional local file)
```

## Stack overview

- Agentic services: Honcho, Hermes, Keycloak, Postgres, shared LLM runtime
- Media services: Transmission, Jellyfin, Prowlarr, Sonarr, Radarr
- Deployment workflow: GitHub Actions builds and deploys the two compose stacks

## Quick start

1. Create a local `.env` file with the required variables.
2. Start the agentic stack:

```bash
docker compose --env-file .env -f services/compose.agentic.yaml up -d
```

3. Start the media stack:

```bash
docker compose --env-file .env -f services/compose.media.yaml up -d
```

4. Stop and remove everything if needed:

```bash
docker compose --env-file .env -f services/compose.agentic.yaml down
docker compose --env-file .env -f services/compose.media.yaml down
```

## Important notes

- Docker build contexts point to `images/` and entrypoint scripts live in `scripts/`.
- Compose files are split into separate stacks: `services/compose.agentic.yaml` and `services/compose.media.yaml`.

## Documentation

- [docs/readme.md](docs/readme.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/services.md](docs/services.md)
- [docs/operations.md](docs/operations.md)
