# domus deployment

Domus joins the existing `lab` Compose project using eight published GHCR images. There are no application builds and no separate Domus PostgreSQL server. Defaults use `stable`; select `preview`, a version such as `v0.1.0` / `prev-v0.1.0`, or a per-image digest through the image reference when pinning.

| Service | Image | Lab address |
| --- | --- | --- |
| domus-portal | ghcr.io/mouralx/domus-portal | 10.203.0.30 |
| domus-membership | ghcr.io/mouralx/domus-membership | 10.203.0.31 |
| domus-boards | ghcr.io/mouralx/domus-boards | 10.203.0.32 |
| domus-automations | ghcr.io/mouralx/domus-automations | 10.203.0.33 |
| domus-boards-worker | ghcr.io/mouralx/domus-boards-worker | 10.203.0.34 |
| domus-automation-worker | ghcr.io/mouralx/domus-automation-worker | 10.203.0.35 |
| domus-databases | PostgreSQL client only; exits after setup | 10.203.0.37 |
| domus-rabbitmq | rabbitmq:4-management | Private domus network |
| postgres | Existing pgvector/pgvector:pg15 server | 10.203.0.17 |

## Existing PostgreSQL

The historical PostgreSQL service is restored with the original container name `postgres`, PostgreSQL 15 image, port 5432 and `~/postgres:/var/lib/postgresql/data` mount. Reuse the current administrator username and password. **Do not change the server major version or mount a new directory over its data.** The deployment helper compares `PG_VERSION`, `POSTGRES_MAJOR` and recognizable image tags before making changes. If your existing server differs from the historical PostgreSQL 15 configuration, set matching image/major values first. Existing data is not migrated automatically.

`domus-databases` starts only after PostgreSQL is healthy. It connects as a client and creates `membership`, `boards` and `automations` with separate roles (`domus_membership`, `domus_boards`, `domus_automations`). It updates passwords only for those dedicated roles, and preserves databases and data on repeated runs. Existing databases owned by other roles cause setup to fail rather than taking ownership. Names can be changed with `DOMUS_MEMBERSHIP_DATABASE`, `DOMUS_BOARDS_DATABASE`, `DOMUS_AUTOMATIONS_DATABASE`.

Application containers receive only their dedicated credentials. Membership delivers public identity projections through an authenticated HTTP outbox; it has no boards database credentials. Invitations are owned by boards. Administrator credentials are mounted into the database setup client, not the APIs or workers. Other homelab applications retain their existing database configurations.

## GitHub configuration and images

Copy the settings from [environment.example](../services/domus/environment.example) into the `mouras-home-lab` GitHub environment. Store the existing `POSTGRES_PASSWORD`, four `DOMUS_*_DB_PASSWORD` values and `DOMUS_RABBITMQ_PASSWORD` as secrets. Generate application database passwords as hex strings (`openssl rand -hex 32`) so they can be used directly in the .NET connection strings. Store hostnames, public URL and image tags as variables. Existing server credentials must match its stored roles; changing POSTGRES_PASSWORD alone does not reset an existing server password.

The Domus GHCR images are public and are pulled anonymously. No GH_PAT, GHCR_TOKEN or registry login is required. The workflow prepares a temporary Docker configuration, removing GHCR credentials and the default global credential helper so stale tokens cannot block public pulls. It preserves the runner's Docker context and unrelated explicit registry settings, and removes the temporary configuration at the end. All eight selected image manifests are checked before any containers are managed, including during dry runs.


The eight image tags can be configured independently: `DOMUS_PORTAL_TAG`, `DOMUS_MEMBERSHIP_TAG`, `DOMUS_BOARDS_TAG`, `DOMUS_AUTOMATIONS_TAG`, `DOMUS_BOARDS_WORKER_TAG`, `DOMUS_AUTOMATIONS_WORKER_TAG`. Do not assume one version tag exists for every component: Domus publishes only changed images. Defaults use each image's current `stable` alias.

Run **Actions → Deploy Home Lab**, choose `install`, and enable `update_images` when applying newly published stable/preview images. Start with `dry_run` to validate settings, PostgreSQL storage and anonymous access to all eight selected image tags without changing containers. Real installation waits for healthy services. Uninstall preserves PostgreSQL data and named volumes. Orphan containers are not automatically removed, protecting externally managed services.

## Public access

Create an Nginx Proxy Manager proxy host for `domus.mouras.me`, forwarding HTTP to **domus-portal:80** (or `10.203.0.30:80`), enable WebSocket support and obtain an HTTPS certificate. Point DNS at the homelab gateway. Set DOMUS_PUBLIC_URL to that exact HTTPS origin and include its hostname in DOMUS_ALLOWED_HOSTS; localhost must remain for health checks. No new host port is published for the portal or APIs, avoiding the existing port 8088 used by Proxy Manager.

Register a Domus account, select an avatar and configure 2FA. Leave DOMUS_PLATFORM_ADMIN_EMAILS empty on the first deployment. Once the account exists, set its email and update/recreate membership and boards to manage platform branding and the global GitHub repository/PAT. Automations still have workspace-scoped permissions.

## Persistence and verification

PostgreSQL data remains in `~/postgres`. Four named volumes retain Domus account/Jira keys, shared automation files, API-only PAT keys and RabbitMQ state: `lab_domus-work-keys`, `lab_domus-automation-data`, `lab_domus-automation-keys`, `lab_domus-rabbitmq-data`. Back up all of these alongside the four databases. Do not use `down -v`; preserve encryption keys when moving existing Domus data. This configuration starts a fresh Domus installation unless its databases and key/file volumes are migrated together.

```sh
bash scripts/test-validate-compose.sh
bash scripts/test-manage-services.sh
bash scripts/test-postgres-storage.sh
DOCKER_CONTEXT=your-context python3 scripts/test-domus-integration.py
```

The integration test pulls the configured published images by default and runs only Domus against an isolated PostgreSQL 15 fixture, separate ports, networks and volumes. It checks database setup/ownership, registration, 2FA, API authorization, execution, ZIP downloads and revocation. It removes only its temporary test project. For source-image testing before publication, `DOMUS_TEST_LOCAL_IMAGES=true` uses already-built local Domus images and does not establish that GHCR access is configured. The Beelink deployment and Nginx Proxy Manager/DNS setup are separate from this local test.

## AI, MCP and upgrading this version

Add the dedicated `DOMUS_AI_DB_PASSWORD` and a random `DOMUS_IDENTITY_SYNC_SECRET` (at least 32 characters) to environment secrets. `domus-ai` owns the fourth database and `lab_domus-ai-data` encryption-key volume. `domus-mcp-server` exposes authenticated tools for boards, membership, automations and AI settings; it has no database or public port. The portal proxies `/ai-api/` through the AI network alias. Model credentials never go to MCP.

The eight current image tags include `DOMUS_AI_TAG`, `DOMUS_MCP_TAG` and the plural `DOMUS_AUTOMATIONS_WORKER_TAG`. The deployment removes only the retired singular automation worker container, preserving its data volume. Choose `scope=domus` to avoid updating unrelated applications or pulling a new shared PostgreSQL server image. Leave `bring_down_first=false`.

Real installation first stops Domus writers, creates private database and volume backups under `~/domus-backups/<UTC timestamp>/`, then starts the selected stack. A backup failure restarts the previous writers and aborts installation. This release renames Jira fields, moves unassigned cards to configured defaults, and removes workspace covers; restore backups with matching prior images for rollback. A failed later installation requires checking the logs and restoring matching backups before reverting images after migrations. Do not upload these backups as public Actions artifacts.

Direct subscription OAuth is disabled by default at the remote HTTPS hostname because the OSS callback runs on the browser's local computer. The AI settings now support OpenAI's documented self-hosted transfer route: connect ChatGPT in local Domus, expand **Move connection to another Domus**, enter `https://domus.mouras.me`, then choose **Export and disconnect here**. In the homelab AI settings, upload the encrypted JSON file and enter its separate transfer code within 30 minutes. The source disconnects so the homelab owns future refreshes. Import is authenticated, destination-bound, replay-protected and stored encrypted for the current Domus member; the server's own host ID is retained. Each member imports their own account and the workspace owner enables AI. No API keys or Domus monthly cap are needed. A separately approved OAuth client can still use its registered remote callback.

See [OpenAI's self-hosted VM guide](https://developers.openai.com/siwc/token-sharing-open-source/self-hosted-vms). Do not put a connection file or transfer code in git, workflow inputs or chat messages.
