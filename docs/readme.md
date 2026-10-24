# Documentation

This folder contains the documentation for the repository configuration and deployment requirements.

## Documents

- [architecture.md](architecture.md) — service layout, networking, and deployment model
- [services.md](services.md) — stack inventory and container descriptions
- [authentication.md](authentication.md) — authentication compatibility and optional SSO requirements
- [operations.md](operations.md) — deployment scripts, start, stop and maintenance tasks
- [tempo.md](tempo.md) — credentials, registry access, schema migrations and PostgreSQL access
- [gitea-runner.md](gitea-runner.md) — runner registration, configuration and verification
- [vault.md](vault.md) — configuration, HTTPS, initialization, unsealing and backups

## Quick links

- Repository root: [../readme.md](../readme.md)
- Local environment template: [../.env.example](../.env.example)
- Deployment scripts: [../.github/scripts/](../.github/scripts/)
- Deployment workflow: [../.github/workflows/deploy.yaml](../.github/workflows/deploy.yaml)
- Unified Compose file: [../services/compose.yaml](../services/compose.yaml)
