# Operations

## Build and deploy

### Build the agentic stack

```bash
docker compose --env-file .env -f services/compose.agentic.yaml build
```

### Build the media stack

```bash
docker compose --env-file .env -f services/compose.media.yaml build
```

### Start the stacks

```bash
docker compose --env-file .env -f services/compose.agentic.yaml up -d
docker compose --env-file .env -f services/compose.media.yaml up -d
```

### Stop the stacks

```bash
docker compose --env-file .env -f services/compose.agentic.yaml down
docker compose --env-file .env -f services/compose.media.yaml down
```

## Logs

```bash
docker compose --env-file .env -f services/compose.agentic.yaml logs -f
docker compose --env-file .env -f services/compose.media.yaml logs -f
```

## Cleanup

```bash
bash scripts/workflow.step.clean-docker.sh
```

## Generated environment file

The deployment workflow writes the runtime env values into the repository root `.env` file before running Docker Compose.

## Maintenance notes

- Prefer the `services/` directory as the source of truth for compose files.
- Prefer `images/` for build definitions and `scripts/` for runtime setup code.
