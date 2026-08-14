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

## Reset Honcho after the embedding backend change

Honcho's embeddings now come from LM Studio (`lms`) via `text-embedding-nomic-embed-text-v1.5` (768-dim), and the `ollama` service has been removed. `EMBEDDING_VECTOR_DIMENSIONS` changed from 1536 to 768. That only takes effect on a fresh Honcho schema: if Honcho already ran, its pgvector columns were created at 1536 dimensions and must be recreated before 768-dim vectors can be inserted. Honcho owns the `public` schema in the `postgres` database; Keycloak uses its own separate `keycloak` database and is unaffected.

```bash
docker compose --env-file .env -f services/compose.agentic.yaml down

# Reset Honcho's schema (postgres DB), keeping Keycloak's keycloak DB intact
docker exec postgres psql -U "$POSTGRES_USER" -d postgres \
  -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'

docker compose --env-file .env -f services/compose.agentic.yaml up -d
```

The `lms` entrypoint fetches `nomic-embed-text` automatically on first boot, so no manual model pull is needed. On a host without an AMD GPU, `lms` runs the embedding model on CPU (slow but functional).

## Maintenance notes

- Prefer the `services/` directory as the source of truth for compose files.
- Prefer `images/` for build definitions and `scripts/` for runtime setup code.
