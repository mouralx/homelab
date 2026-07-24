# Utility Scripts

Reference documentation for the entrypoint scripts in `scripts/`.

## Contents

- [honcho.entrypoint.sh](#honchoentrypointsh)
- [lmstudio.entrypoint.sh](#lmstudioentrypointsh)
- [transmission.entrypoint.sh](#transmissionentrypointsh)

---

## honcho.entrypoint.sh

**Purpose**: Starts the Honcho container, waits for Postgres to become available, creates the required PostgreSQL extensions, runs Alembic migrations, and launches the Honcho app.

**Location**: `scripts/honcho.entrypoint.sh`

**Runtime behavior**:

1. Reads database connection values from environment variables.
2. Waits until Postgres is accepting connections.
3. Creates the `vector` and `pg_trgm` extensions if they are missing.
4. Runs the Alembic migrations.
5. Starts Honcho with FastAPI on port 8000.

**Relevant environment variables**:
- `DB_HOST`
- `DB_PORT`
- `DB_NAME`
- `DB_USER`
- `DB_PASSWORD`
- `OPENAI_API_BASE_URL`
- `OPENAI_BASE_URL`
- `HOST`
- `PORT`

---

## lmstudio.entrypoint.sh

**Purpose**: Installs LM Studio if needed, downloads the configured model, loads it, and starts the LM Studio server on port 4321.

**Location**: `scripts/lmstudio.entrypoint.sh`

**Runtime behavior**:

1. Checks whether the LM Studio CLI is already installed.
2. Installs the LM Studio toolchain if missing.
3. Downloads the model referenced by `LLM_MODEL`.
4. Loads the model into the local runtime.
5. Starts the LM Studio server on port 4321.

**Relevant environment variables**:
- `LLM_MODEL`

---

## transmission.entrypoint.sh

**Purpose**: Applies runtime configuration values (username, password, directories) to the Transmission daemon before starting it.

**Location**: `scripts/transmission.entrypoint.sh`

**Runtime behavior**:

1. Reads `TRANSMISSION_USER`, `TRANSMISSION_PASSWORD`, and directory paths from environment variables.
2. Rewrites the Transmission `settings.json` configuration file with runtime values.
3. Starts `transmission-daemon` with the updated configuration.

**Relevant environment variables**:
- `TRANSMISSION_USER`
- `TRANSMISSION_PASSWORD`
- `TRANSMISSION_DOWNLOADS_DIR`
- `TRANSMISSION_INCOMPLETE_DIR`
